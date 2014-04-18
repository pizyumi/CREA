using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace CREA2014
{
    public class Core
    {
        private AccountHolderDatabase accountHolderDatabase;
        public AccountHolderDatabase AccountHolderDatabase
        {
            get { return accountHolderDatabase; }
        }

        public Core(string _basepath)
        {
            string ahdFileName = "ahs.dat";

            //Coreが2回以上実体化されないことを保証する
            //2回以上呼ばれた際には例外が発生する
            Instantiate();

            basepath = _basepath;
            accountHolderDatabasePath = Path.Combine(basepath, ahdFileName);
        }

        private static readonly Action Instantiate = OneTime.GetOneTime();

        private readonly object coreLock = new object();

        private string basepath;
        private string accountHolderDatabasePath;
        private bool isSystemStarted;

        public void StartSystem()
        {
            lock (coreLock)
            {
                if (isSystemStarted)
                    throw new InvalidOperationException("core_started");

                accountHolderDatabase = new AccountHolderDatabase();

                if (File.Exists(accountHolderDatabasePath))
                    accountHolderDatabase.FromBinary(File.ReadAllBytes(accountHolderDatabasePath));

                isSystemStarted = true;
            }
        }

        public void EndSystem()
        {
            lock (coreLock)
            {
                if (!isSystemStarted)
                    throw new InvalidOperationException("core_not_started");

                File.WriteAllBytes(accountHolderDatabasePath, accountHolderDatabase.ToBinary());

                isSystemStarted = false;
            }
        }
    }

    #region P2Pネットワーク

    public abstract class P2PNODE
    {
        private readonly object connectionsLock = new object();
        private List<ConnectionData> connections;
        public ConnectionData[] Connections
        {
            get { return connections.ToArray(); }
        }

        private readonly object connectionHistoriesLock = new object();
        private List<ConnectionHistory> connectionHistories;
        public ConnectionHistory[] ConnectionHistories
        {
            get { return connectionHistories.ToArray(); }
        }

        private string privateRsaParametersCache;
        protected virtual string PrivateRsaParameters
        {
            get
            {
                if (privateRsaParametersCache != null)
                    return privateRsaParametersCache;
                else
                {
                    using (RSACryptoServiceProvider rsacsp = new RSACryptoServiceProvider(2048))
                        return privateRsaParametersCache = rsacsp.ToXmlString(true);
                }
            }
        }

        private int connectionNumber;

        public P2PNODE()
        {
            connections = new List<ConnectionData>();
            connectionHistories = new List<ConnectionHistory>();
            connectionNumber = 0;
        }

        public event EventHandler ConnectionAdded = delegate { };
        public event EventHandler ConnectionRemoved = delegate { };

        protected Listener CreateListener(ushort port, Action<CommunicationApparatus, IPEndPoint> protocolProcess)
        {
            Dictionary<SocketData, ConnectionData> listenerConnections = new Dictionary<SocketData, ConnectionData>();

            Listener listener = new Listener(port, RsaKeySize.rsa2048, protocolProcess);
            listener.ClientConnected += (sender, e) =>
            {
                ConnectionData connection = new ConnectionData(connectionNumber++, e.IpAddress, e.Port, e.MyPort, ConnectionData.ConnectionDirection.up);

                lock (listenerConnections)
                    listenerConnections.Add(e, connection);

                AddConnection(connection);
            };
            listener.ClientDisconnected += (sender, e) =>
            {
                ConnectionData connection;

                lock (listenerConnections)
                {
                    if (!listenerConnections.ContainsKey(e))
                        throw new InvalidDataException("not_contains_socket_data");

                    connection = listenerConnections[e];

                    listenerConnections.Remove(e);
                }

                RemoveConnection(connection);
            };
            listener.ClientSucceeded += (sender, e) => RegisterResult(e.IpAddress, true);
            listener.ClientFailed += (sender, e) =>
            {
                if (e.Value2 is SocketException)
                    RegisterResult(e.Value1.IpAddress, false);
            };

            return listener;
        }

        protected Client CreateClient(IPAddress ipAddress, ushort port, Action<CommunicationApparatus, IPEndPoint> protocolProcess)
        {
            ConnectionData connection = null;

            Client client = new Client(ipAddress, port, RsaKeySize.rsa2048, PrivateRsaParameters, protocolProcess);
            client.ConnectSucceeded += (sender, e) =>
            {
                connection = new ConnectionData(connectionNumber++, e.IpAddress, e.Port, e.MyPort, ConnectionData.ConnectionDirection.down);

                AddConnection(connection);
            };
            client.Disconnected += (sender, e) => RemoveConnection(connection);
            //<未改良>接続失敗と接続成功後の通信失敗の場合のRegisterResultを区別するべきかも知れない
            client.Succeeded += (sender, e) => RegisterResult(e.IpAddress, true);
            client.Failed += (sender, e) =>
            {
                if (e.Value2 is SocketException)
                    RegisterResult(e.Value1.IpAddress, false);
            };
            client.ConnectFailed += (sender, e) => RegisterResult(client.IpAdress, false);

            return client;
        }

        private void AddConnection(ConnectionData connection)
        {
            lock (connectionsLock)
            {
                if (connections.Contains(connection))
                    throw new InvalidOperationException("exist_connection");

                this.ExecuteBeforeEvent(() => connections.Add(connection), ConnectionAdded);
            }
        }

        private void RemoveConnection(ConnectionData connection)
        {
            lock (connectionsLock)
            {
                if (!connections.Contains(connection))
                    throw new InvalidOperationException("not_exist_connection");

                this.ExecuteBeforeEvent(() => connections.Remove(connection), ConnectionRemoved);
            }
        }

        private void RegisterResult(IPAddress ipAddress, bool isSucceeded)
        {
            ConnectionHistory connectionHistory;
            lock (connectionHistoriesLock)
                if ((connectionHistory = connectionHistories.Where((e) => e.IpAddress.Equals(ipAddress)).FirstOrDefault()) == null)
                    connectionHistories.Add(connectionHistory = new ConnectionHistory(ipAddress));

            if (isSucceeded)
                connectionHistory.IncrementSuccess();
            else
                connectionHistory.IncrementFailure();
        }
    }

    public interface ICommunicationApparatus
    {
        byte[] ReadBytes();
        void WriteBytes(byte[] data);
    }

    public class CommunicationApparatus : ICommunicationApparatus
    {
        private readonly NetworkStream ns;
        private readonly RijndaelManaged rm;

        public CommunicationApparatus(NetworkStream _ns, RijndaelManaged _rm)
        {
            ns = _ns;
            rm = _rm;
        }

        public CommunicationApparatus(NetworkStream _ns) : this(_ns, null) { }

        public byte[] ReadBytes()
        {
            return ReadBytesInnner(false);
        }

        public void WriteBytes(byte[] data)
        {
            WriteBytesInner(data, false);
        }

        public byte[] ReadCompressedBytes()
        {
            return ReadBytesInnner(true);
        }

        public void WriteCompreddedBytes(byte[] data)
        {
            WriteBytesInner(data, true);
        }

        private byte[] ReadBytesInnner(bool isCompressed)
        {
            //最初の4バイトは本来のデータの長さ
            byte[] dataLengthBytes = new byte[4];
            if (ns.Read(dataLengthBytes, 0, 4) != 4)
                throw new Exception("cant_read_data_length_bytes");
            int dataLength = BitConverter.ToInt32(dataLengthBytes, 0);

            if (dataLength == 0)
                return new byte[] { };

            //次の32バイトは受信データのハッシュ（破損検査用）
            byte[] hash = new byte[32];
            if (ns.Read(hash, 0, 32) != 32)
                throw new Exception("cant_read_hash");

            //次の4バイトは受信データの長さ
            byte[] readDataLengthBytes = new byte[4];
            if (ns.Read(readDataLengthBytes, 0, 4) != 4)
                throw new Exception("cant_read_read_data_length_bytes");
            int readDataLength = BitConverter.ToInt32(readDataLengthBytes, 0);

            byte[] readData = null;

            using (MemoryStream ms = new MemoryStream())
            {
                int bufsize = 1024;
                byte[] buffer = new byte[bufsize];

                while (true)
                {
                    int mustReadSize = (int)ms.Length + bufsize > readDataLength ? readDataLength - (int)ms.Length : bufsize;

                    int byteSize = ns.Read(buffer, 0, mustReadSize);
                    ms.Write(buffer, 0, byteSize);

                    if (ms.Length == readDataLength)
                        break;
                    else if (ms.Length > readDataLength)
                        throw new Exception("overread");
                }

                readData = ms.ToArray();
            }

            if (!hash.BytesEquals(new SHA256Managed().ComputeHash(readData)))
                throw new Exception("receive_data_corrupt");

            byte[] data = new byte[dataLength];

            using (MemoryStream ms = new MemoryStream(readData))
                if (rm != null)
                {
                    using (ICryptoTransform icf = rm.CreateDecryptor(rm.Key, rm.IV))
                    using (CryptoStream cs = new CryptoStream(ms, icf, CryptoStreamMode.Read))
                        if (isCompressed)
                            using (DeflateStream ds = new DeflateStream(cs, CompressionMode.Decompress))
                                ds.Read(data, 0, data.Length);
                        else
                            cs.Read(data, 0, data.Length);

                    return data;
                }
                else
                    if (isCompressed)
                    {
                        using (DeflateStream ds = new DeflateStream(ms, CompressionMode.Decompress))
                            ds.Read(data, 0, data.Length);

                        return data;
                    }
                    else
                        return readData;
        }

        private void WriteBytesInner(byte[] data, bool isCompressed)
        {
            ns.Write(BitConverter.GetBytes(data.Length), 0, 4);

            if (data.Length == 0)
                return;

            byte[] writeData = null;

            using (MemoryStream ms = new MemoryStream())
            {
                if (rm != null)
                    using (ICryptoTransform icf = rm.CreateEncryptor(rm.Key, rm.IV))
                    using (CryptoStream cs = new CryptoStream(ms, icf, CryptoStreamMode.Write))
                        if (isCompressed)
                            using (DeflateStream ds = new DeflateStream(cs, CompressionMode.Compress))
                            {
                                ds.Write(data, 0, data.Length);
                                ds.Flush();
                            }
                        else
                        {
                            cs.Write(data, 0, data.Length);
                            cs.FlushFinalBlock();
                        }
                else
                    if (isCompressed)
                        using (DeflateStream ds = new DeflateStream(ms, CompressionMode.Compress))
                        {
                            ds.Write(data, 0, data.Length);
                            ds.Flush();
                        }
                    else
                    {
                        ms.Write(data, 0, data.Length);
                        ms.Flush();
                    }

                writeData = ms.ToArray();
            }

            ns.Write(new SHA256Managed().ComputeHash(writeData), 0, 32);
            ns.Write(BitConverter.GetBytes(writeData.Length), 0, 4);
            ns.Write(writeData, 0, writeData.Length);
        }
    }

    public enum RsaKeySize { rsa1024, rsa2048 }

    public class SocketData : INTERNALDATA
    {
        public readonly IPAddress IpAddress;
        public readonly ushort Port;
        public readonly ushort MyPort;

        public SocketData(IPAddress _ipAddress, ushort _port, ushort _myPort)
        {
            IpAddress = _ipAddress;
            Port = _port;
            MyPort = _myPort;
        }
    }

    public class Client
    {
        private readonly IPAddress ipAddress;
        public IPAddress IpAdress
        {
            get { return ipAddress; }
        }

        private readonly ushort port;
        public ushort Port
        {
            get { return port; }
        }

        private readonly string privateRsaParameter;
        public bool IsEncrypted
        {
            get { return privateRsaParameter != null; }
        }

        private readonly RsaKeySize keySize;
        public RsaKeySize KeySize
        {
            get { return keySize; }
        }

        private readonly Action<CommunicationApparatus, IPEndPoint> protocolProcess;

        private int receiveTimeout = 30000;
        public int ReceiveTimeout
        {
            get { return receiveTimeout; }
            set
            {
                if (client != null)
                    throw new InvalidOperationException("client_already_started");

                receiveTimeout = value;
            }
        }

        private int sendTimeout = 30000;
        public int SendTimeout
        {
            get { return sendTimeout; }
            set
            {
                if (client != null)
                    throw new InvalidOperationException("client_already_started");

                sendTimeout = value;
            }
        }

        private int receiveBufferSize = 8192;
        public int ReceiveBufferSize
        {
            get { return receiveBufferSize; }
            set
            {
                if (client != null)
                    throw new InvalidOperationException("client_already_started");

                receiveBufferSize = value;
            }
        }

        private int sendBufferSize = 8192;
        public int SendBufferSize
        {
            get { return sendBufferSize; }
            set
            {
                if (client != null)
                    throw new InvalidOperationException("client_already_started");

                sendBufferSize = value;
            }
        }

        private Socket client;

        public Client(IPAddress _ipAddress, ushort _port, RsaKeySize _keySize, string _privateRsaParameter, Action<CommunicationApparatus, IPEndPoint> _protocolProcess)
        {
            ipAddress = _ipAddress;
            port = _port;
            keySize = _keySize;
            privateRsaParameter = _privateRsaParameter;
            protocolProcess = _protocolProcess;
        }

        public Client(IPAddress _ipAddress, ushort _port, Action<CommunicationApparatus, IPEndPoint> _protocolProcess) : this(_ipAddress, _port, RsaKeySize.rsa2048, null, _protocolProcess) { }

        public event EventHandler<SocketData> ConnectSucceeded = delegate { };
        public event EventHandler ConnectFailed = delegate { };
        public event EventHandler<SocketData> Disconnected = delegate { };
        public event EventHandler<SocketData> Succeeded = delegate { };
        public event EventHandler<EventArgs<SocketData, Exception>> Failed = delegate { };

        public void StartClient()
        {
            if (client != null)
                throw new InvalidOperationException("client_already_started");

            this.StartTask(() =>
            {
                try
                {
                    client = new Socket(ipAddress.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                    client.ReceiveTimeout = receiveTimeout;
                    client.SendTimeout = sendTimeout;
                    client.ReceiveBufferSize = receiveBufferSize;
                    client.SendBufferSize = sendBufferSize;
                    client.Connect(ipAddress, port);
                }
                catch (Exception ex)
                {
                    this.RaiseError("client_socket".GetLogMessage(), 5, ex);

                    EndClient();

                    ConnectFailed(this, EventArgs.Empty);
                }

                SocketData socketData = new SocketData(ipAddress, port, (ushort)((IPEndPoint)client.LocalEndPoint).Port);

                ConnectSucceeded(this, socketData);

                try
                {
                    using (NetworkStream ns = new NetworkStream(client))
                    {
                        RijndaelManaged rm = null;

                        if (IsEncrypted)
                        {
                            RSACryptoServiceProvider rsacsp = new RSACryptoServiceProvider();
                            rsacsp.FromXmlString(privateRsaParameter);

                            if ((keySize == RsaKeySize.rsa1024 && rsacsp.KeySize != 1024) || (keySize == RsaKeySize.rsa2048 && rsacsp.KeySize != 2048))
                                throw new Exception("client_rsa_key_size");

                            RSAParameters rsaParameters = rsacsp.ExportParameters(true);
                            byte[] modulus = rsaParameters.Modulus;
                            byte[] exponent = rsaParameters.Exponent;

                            ns.Write(modulus, 0, modulus.Length);
                            ns.Write(exponent, 0, exponent.Length);

                            RSAPKCS1KeyExchangeDeformatter rsapkcs1ked = new RSAPKCS1KeyExchangeDeformatter(rsacsp);

                            byte[] encryptedKey = keySize == RsaKeySize.rsa1024 ? new byte[128] : new byte[256];
                            byte[] encryptedIv = keySize == RsaKeySize.rsa1024 ? new byte[128] : new byte[256];

                            ns.Read(encryptedKey, 0, encryptedKey.Length);
                            ns.Read(encryptedIv, 0, encryptedIv.Length);

                            rm = new RijndaelManaged();
                            rm.Padding = PaddingMode.Zeros;
                            rm.Key = rsapkcs1ked.DecryptKeyExchange(encryptedKey);
                            rm.IV = rsapkcs1ked.DecryptKeyExchange(encryptedIv);
                        }

                        protocolProcess(new CommunicationApparatus(ns, rm), (IPEndPoint)client.RemoteEndPoint);
                    }

                    Succeeded(this, socketData);
                }
                catch (Exception ex)
                {
                    this.RaiseError("client_socket".GetLogMessage(), 5, ex);

                    EndClient();

                    Failed(this, new EventArgs<SocketData, Exception>(socketData, ex));
                }
                finally
                {
                    Disconnected(this, socketData);
                }
            }, "client", string.Empty);
        }

        private void EndClient()
        {
            if (client == null)
                throw new InvalidOperationException("client_not_started");

            try
            {
                if (client.Connected)
                    client.Shutdown(SocketShutdown.Both);
                client.Close();
            }
            catch (Exception ex)
            {
                this.RaiseError("client_socket".GetLogMessage(), 5, ex);
            }
        }
    }

    public class Listener
    {
        private readonly ushort port;
        public ushort Port
        {
            get { return port; }
        }

        private readonly bool isEncrypted;
        public bool IsEncrypted
        {
            get { return isEncrypted; }
        }

        private readonly RsaKeySize keySize;
        public RsaKeySize KeySize
        {
            get { return keySize; }
        }

        private readonly Action<CommunicationApparatus, IPEndPoint> protocolProcess;

        private int receiveTimeout = 30000;
        public int ReceiveTimeout
        {
            get { return receiveTimeout; }
            set
            {
                if (listener != null)
                    throw new InvalidOperationException("listener_already_started");

                receiveTimeout = value;
            }
        }

        private int sendTimeout = 30000;
        public int SendTimeout
        {
            get { return sendTimeout; }
            set
            {
                if (listener != null)
                    throw new InvalidOperationException("listener_already_started");

                sendTimeout = value;
            }
        }

        private int receiveBufferSize = 8192;
        public int ReceiveBufferSize
        {
            get { return receiveBufferSize; }
            set
            {
                if (listener != null)
                    throw new InvalidOperationException("listener_already_started");

                receiveBufferSize = value;
            }
        }

        private int sendBufferSize = 8192;
        public int SendBufferSize
        {
            get { return sendBufferSize; }
            set
            {
                if (listener != null)
                    throw new InvalidOperationException("listener_already_started");

                sendBufferSize = value;
            }
        }

        private int backlog = 100;
        public int Backlog
        {
            get { return backlog; }
            set
            {
                if (listener != null)
                    throw new InvalidOperationException("listener_already_started");

                backlog = value;
            }
        }

        private Socket listener;
        private readonly object lobject = new object();
        private readonly List<Socket> clients;

        private Listener(ushort _port, bool _isEncrypted, RsaKeySize _keySize, Action<CommunicationApparatus, IPEndPoint> _protocolProcess)
        {
            port = _port;
            isEncrypted = _isEncrypted;
            keySize = _keySize;
            protocolProcess = _protocolProcess;

            clients = new List<Socket>();
        }

        public Listener(ushort _port, RsaKeySize _keySize, Action<CommunicationApparatus, IPEndPoint> _protocolProcess) : this(_port, true, _keySize, _protocolProcess) { }

        public Listener(ushort _port, Action<CommunicationApparatus, IPEndPoint> _protocolProcess) : this(_port, false, RsaKeySize.rsa2048, _protocolProcess) { }

        public event EventHandler<Exception> Failed = delegate { };
        public event EventHandler<SocketData> ClientConnected = delegate { };
        public event EventHandler<SocketData> ClientDisconnected = delegate { };
        public event EventHandler<SocketData> ClientSucceeded = delegate { };
        public event EventHandler<EventArgs<SocketData, Exception>> ClientFailed = delegate { };

        public void StartListener()
        {
            if (listener != null)
                throw new InvalidOperationException("listener_already_started");

            this.StartTask(() =>
            {
                try
                {
                    listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                    listener.Bind(new IPEndPoint(IPAddress.Any, port));
                    listener.Listen(backlog);

                    while (true)
                    {
                        Socket client = listener.Accept();
                        client.ReceiveTimeout = receiveTimeout;
                        client.SendTimeout = sendTimeout;
                        client.ReceiveBufferSize = receiveBufferSize;
                        client.SendBufferSize = sendBufferSize;

                        SocketData socketData = new SocketData(((IPEndPoint)client.RemoteEndPoint).Address, (ushort)((IPEndPoint)client.RemoteEndPoint).Port, (ushort)((IPEndPoint)client.LocalEndPoint).Port);

                        ClientConnected(this, socketData);

                        lock (lobject)
                            clients.Add(client);

                        this.StartTask(() =>
                        {
                            try
                            {
                                using (NetworkStream ns = new NetworkStream(client))
                                {
                                    RijndaelManaged rm = null;

                                    if (isEncrypted)
                                    {
                                        byte[] modulus = keySize == RsaKeySize.rsa1024 ? new byte[128] : new byte[256];
                                        byte[] exponent = new byte[3];

                                        ns.Read(modulus, 0, modulus.Length);
                                        ns.Read(exponent, 0, exponent.Length);

                                        RSACryptoServiceProvider rsacsp = new RSACryptoServiceProvider();
                                        RSAParameters rsaParameters = new RSAParameters();
                                        rsaParameters.Modulus = modulus;
                                        rsaParameters.Exponent = exponent;
                                        rsacsp.ImportParameters(rsaParameters);

                                        RSAPKCS1KeyExchangeFormatter rsapkcs1kef = new RSAPKCS1KeyExchangeFormatter(rsacsp);

                                        rm = new RijndaelManaged();
                                        rm.Padding = PaddingMode.Zeros;

                                        byte[] encryptedKey = rsapkcs1kef.CreateKeyExchange(rm.Key);
                                        byte[] encryptedIv = rsapkcs1kef.CreateKeyExchange(rm.IV);

                                        ns.Write(encryptedKey, 0, encryptedKey.GetLength(0));
                                        ns.Write(encryptedIv, 0, encryptedIv.GetLength(0));
                                    }

                                    protocolProcess(new CommunicationApparatus(ns, rm), (IPEndPoint)client.RemoteEndPoint);

                                    ClientSucceeded(this, socketData);
                                }
                            }
                            catch (Exception ex)
                            {
                                this.RaiseError("listner_socket".GetLogMessage(), 5, ex);

                                try
                                {
                                    lock (lobject)
                                        clients.Remove(client);

                                    if (client.Connected)
                                        client.Shutdown(SocketShutdown.Both);
                                    client.Close();
                                }
                                catch (Exception ex2)
                                {
                                    this.RaiseError("listner_socket".GetLogMessage(), 5, ex2);
                                }

                                ClientFailed(this, new EventArgs<SocketData, Exception>(socketData, ex));
                            }
                            finally
                            {
                                ClientDisconnected(this, socketData);
                            }
                        }, "listener_client", string.Empty);
                    }
                }
                catch (Exception ex)
                {
                    this.RaiseError("listner_socket".GetLogMessage(), 5, ex);

                    EndListener();

                    Failed(this, ex);
                }
            }, "listener", string.Empty);
        }

        public void EndListener()
        {
            if (listener == null)
                throw new InvalidOperationException("listener_not_started");

            try
            {
                listener.Close();

                lock (lobject)
                    foreach (var client in clients)
                    {
                        if (client.Connected)
                            client.Shutdown(SocketShutdown.Both);
                        client.Close();
                    }
            }
            catch (Exception ex)
            {
                this.RaiseError("listner_socket".GetLogMessage(), 5, ex);
            }
        }
    }

    public class ConnectionData : INTERNALDATA
    {
        private readonly int number;
        public int Number
        {
            get { return number; }
        }

        private readonly IPAddress ipAddress;
        public IPAddress IpAddress
        {
            get { return ipAddress; }
        }

        private readonly ushort port;
        public ushort Port
        {
            get { return port; }
        }

        private readonly ushort myPort;
        public ushort MyPort
        {
            get { return myPort; }
        }

        private readonly ConnectionDirection direction;
        public ConnectionDirection Direction
        {
            get { return direction; }
        }

        private readonly DateTime connectedTime;
        public DateTime ConnectedTime
        {
            get { return connectedTime; }
        }

        public enum ConnectionDirection { up, down }

        public ConnectionData(int _number, IPAddress _ipAddress, ushort _port, ushort _myPort, ConnectionDirection _direction)
        {
            number = _number;
            ipAddress = _ipAddress;
            port = _port;
            myPort = _myPort;
            direction = _direction;
            connectedTime = DateTime.Now;
        }

        public TimeSpan Duration
        {
            get { return DateTime.Now - connectedTime; }
        }
    }

    public class ConnectionHistory
    {
        private readonly IPAddress ipAddress;
        public IPAddress IpAddress
        {
            get { return ipAddress; }
        }

        private int success;
        public int Success
        {
            get { return success; }
        }

        private int failure;
        public int Failure
        {
            get { return failure; }
        }

        private readonly object failureTimeLock = new object();
        private List<DateTime> failureTime;
        public DateTime[] FailureTime
        {
            get { return failureTime.ToArray(); }
        }

        public ConnectionHistory(IPAddress _ipAddress)
        {
            ipAddress = _ipAddress;
            failureTime = new List<DateTime>();
        }

        public int Connection
        {
            get { return success + failure; }
        }

        public double SuccessRate
        {
            get { return (double)success / (double)Connection; }
        }

        public double FailureRate
        {
            get { return (double)failure / (double)Connection; }
        }

        public bool IsDead
        {
            get
            {
                lock (failureTimeLock)
                    return failureTime.Count >= 2 && failureTime[1] - failureTime[0] <= new TimeSpan(0, 5, 0) && DateTime.Now - failureTime[1] <= new TimeSpan(0, 15, 0);
            }
        }

        public bool IsBad
        {
            get { return Connection > 10 && FailureRate > 0.9; }
        }

        public void IncrementSuccess()
        {
            success++;

            lock (failureTime)
                failureTime.Clear();
        }

        public void IncrementFailure()
        {
            failure++;

            lock (failureTime)
            {
                if (failureTime.Count >= 2)
                    while (failureTime.Count >= 2)
                        failureTime.RemoveAt(0);

                failureTime.Add(DateTime.Now);
            }
        }
    }

    #endregion

    #region CREAネットワーク

    public enum Network { localtest = 0, global = 1 }

    public abstract class CREANODEBASE : P2PNODE
    {
        public enum MessageName
        {
            inv = 10,
            getdata = 11,
            tx = 12,
            block = 13,
            notfound = 14,
        }

        public class Message : SHAREDDATA
        {
            public MessageBase messageBase { get; private set; }

            public MessageName name
            {
                get
                {
                    if (messageBase is Inv)
                        return MessageName.inv;
                    else if (messageBase is Getdata)
                        return MessageName.getdata;
                    else if (messageBase is TxTest)
                        return MessageName.tx;
                    else
                        throw new NotSupportedException("massage_base_not_supported");
                }
            }

            public Message(MessageBase _messageBase)
                : base(0)
            {
                messageBase = _messageBase;
            }

            public Message() : this(null) { }

            protected override Func<ReaderWriter, IEnumerable<MainDataInfomation>> StreamInfo
            {
                get { return (msrw) => StreamInfoInner; }
            }
            private IEnumerable<MainDataInfomation> StreamInfoInner
            {
                get
                {
                    if (Version == 0)
                    {
                        MessageName mn;
                        yield return new MainDataInfomation(typeof(int), () => (int)name, (o) =>
                        {
                            mn = (MessageName)o;
                            if (mn == MessageName.inv)
                                messageBase = new Inv();
                            else if (mn == MessageName.getdata)
                                messageBase = new Getdata();
                            else if (mn == MessageName.tx)
                                messageBase = new TxTest();
                            else
                                throw new NotSupportedException("message_name_not_supported");
                        });
                        foreach (var mdi in messageBase.PublicStreamInfo(null))
                            yield return mdi;
                    }
                    else
                        throw new NotSupportedException("message_main_data_info");
                }
            }

            public override bool IsVersioned
            {
                get { return true; }
            }

            public override bool IsCorruptionChecked
            {
                get
                {
                    if (Version <= 0)
                        return true;
                    else
                        throw new NotSupportedException("message_check");
                }
            }
        }

        public abstract class MessageBase : SHAREDDATA
        {
            public MessageBase(int? _version) : base(_version) { }

            public MessageBase() : base(null) { }

            public virtual Func<ReaderWriter, IEnumerable<MainDataInfomation>> PublicStreamInfo
            {
                get { return StreamInfo; }
            }
        }

        public abstract class MessageSha256Hash : MessageBase
        {
            public Sha256Hash hash { get; private set; }

            public MessageSha256Hash(int? _version, Sha256Hash _hash)
                : base(_version)
            {
                hash = _hash;
            }

            public MessageSha256Hash(Sha256Hash _hash) : this(null, _hash) { }

            protected override Func<ReaderWriter, IEnumerable<MainDataInfomation>> StreamInfo
            {
                get
                {
                    return (msrw) => new MainDataInfomation[]{
                        new MainDataInfomation(typeof(Sha256Hash), null, () => hash, (o) => hash = (Sha256Hash)o),
                    };
                }
            }
        }

        public class Inv : MessageSha256Hash
        {
            public Inv(Sha256Hash _hash) : base(0, _hash) { }

            public Inv() : this(new Sha256Hash()) { }

            protected override Func<ReaderWriter, IEnumerable<MainDataInfomation>> StreamInfo
            {
                get
                {
                    if (Version == 0)
                        return base.StreamInfo;
                    else
                        throw new NotSupportedException("inv_main_data_info");
                }
            }

            public override bool IsVersioned
            {
                get { return true; }
            }

            public override bool IsCorruptionChecked
            {
                get
                {
                    if (Version <= 0)
                        return true;
                    else
                        throw new NotSupportedException("inv_check");
                }
            }
        }

        public class Getdata : MessageSha256Hash
        {
            public Getdata(Sha256Hash _hash) : base(0, _hash) { }

            public Getdata() : this(new Sha256Hash()) { }

            protected override Func<ReaderWriter, IEnumerable<MainDataInfomation>> StreamInfo
            {
                get
                {
                    if (Version == 0)
                        return base.StreamInfo;
                    else
                        throw new NotSupportedException("getdata_main_data_info");
                }
            }

            public override bool IsVersioned
            {
                get { return true; }
            }

            public override bool IsCorruptionChecked
            {
                get
                {
                    if (Version <= 0)
                        return true;
                    else
                        throw new NotSupportedException("getdata_check");
                }
            }
        }

        //試験用
        public class TxTest : MessageBase
        {
            public byte[] data { get; private set; }

            public TxTest()
                : base(0)
            {
                data = new byte[1024];
                for (int i = 0; i < data.Length; i++)
                    data[i] = (byte)256.RandomNum();
            }

            protected override Func<ReaderWriter, IEnumerable<MainDataInfomation>> StreamInfo
            {
                get
                {
                    if (Version == 0)
                        return (msrw) => new MainDataInfomation[]{
                            new MainDataInfomation(typeof(byte[]), 1024, () => data, (o) => data = (byte[])o),
                        };
                    else
                        throw new NotSupportedException("tx_test_main_data_info");
                }
            }

            public override bool IsVersioned
            {
                get { return true; }
            }

            public override bool IsCorruptionChecked
            {
                get
                {
                    if (Version <= 0)
                        return true;
                    else
                        throw new NotSupportedException("tx_test_check");
                }
            }
        }

        public class Header : SHAREDDATA
        {
            public NodeInformation nodeInfo { get; private set; }
            public int creaVersion { get; private set; }
            public int protocolVersion { get; private set; }
            public string client { get; private set; }
            public bool isTemporary { get; private set; }

            public Header() : base(0) { }

            public Header(NodeInformation _nodeInfo, int _creaVersion, int _protocolVersion, string _client, bool _isTemporary)
                : base(0)
            {
                if (_client.Length > 256)
                    throw new InvalidDataException("client_too_lengthy");

                nodeInfo = _nodeInfo;
                creaVersion = _creaVersion;
                protocolVersion = _protocolVersion;
                client = _client;
                isTemporary = _isTemporary;
            }

            protected override Func<ReaderWriter, IEnumerable<MainDataInfomation>> StreamInfo
            {
                get { return (msrw) => StreamInfoInner; }
            }
            private IEnumerable<MainDataInfomation> StreamInfoInner
            {
                get
                {
                    if (Version == 0)
                    {
                        bool isInBound = nodeInfo != null;
                        yield return new MainDataInfomation(typeof(bool), () => isInBound, (o) => isInBound = (bool)o);
                        if (isInBound)
                            yield return new MainDataInfomation(typeof(NodeInformation), 0, () => nodeInfo, (o) => nodeInfo = (NodeInformation)o);
                        yield return new MainDataInfomation(typeof(int), () => creaVersion, (o) => creaVersion = (int)o);
                        yield return new MainDataInfomation(typeof(int), () => protocolVersion, (o) => protocolVersion = (int)o);
                        yield return new MainDataInfomation(typeof(string), () => client, (o) => client = (string)o);
                        yield return new MainDataInfomation(typeof(bool), () => isTemporary, (o) => isTemporary = (bool)o);
                    }
                    else
                        throw new NotSupportedException("header_stream_info");
                }
            }

            public override bool IsVersioned
            {
                get { return true; }
            }

            public override bool IsCorruptionChecked
            {
                get
                {
                    if (Version <= 0)
                        return true;
                    else
                        throw new NotSupportedException("header_corruption_checked");
                }
            }
        }

        public class HeaderResponse : SHAREDDATA
        {
            public NodeInformation nodeInfo { get; private set; }
            public bool isSameNetwork { get; private set; }
            public bool isAlreadyConnected { get; private set; }
            public NodeInformation correctNodeInfo { get; private set; }
            public bool isOldCreaVersion { get; private set; }
            public int protocolVersion { get; private set; }
            public string client { get; private set; }

            public HeaderResponse() : base(0) { }

            public HeaderResponse(NodeInformation _nodeInfo, bool _isSameNetwork, bool _isAlreadyConnected, NodeInformation _correctNodeInfo, bool _isOldCreaVersion, int _protocolVersion, string _client)
                : base(0)
            {
                if (_client.Length > 256)
                    throw new InvalidDataException("client_too_lengthy");

                nodeInfo = _nodeInfo;
                isSameNetwork = _isSameNetwork;
                isAlreadyConnected = _isAlreadyConnected;
                correctNodeInfo = _correctNodeInfo;
                isOldCreaVersion = _isOldCreaVersion;
                protocolVersion = _protocolVersion;
                client = _client;
            }

            protected override Func<ReaderWriter, IEnumerable<MainDataInfomation>> StreamInfo
            {
                get { return (msrw) => StreamInfoInner; }
            }
            private IEnumerable<MainDataInfomation> StreamInfoInner
            {
                get
                {
                    if (Version == 0)
                    {
                        yield return new MainDataInfomation(typeof(NodeInformation), 0, () => nodeInfo, (o) => nodeInfo = (NodeInformation)o);
                        yield return new MainDataInfomation(typeof(bool), () => isSameNetwork, (o) => isSameNetwork = (bool)o);
                        bool isCorrectNodeInfo = correctNodeInfo == null;
                        yield return new MainDataInfomation(typeof(bool), () => isCorrectNodeInfo, (o) => isCorrectNodeInfo = (bool)o);
                        if (!isCorrectNodeInfo)
                            yield return new MainDataInfomation(typeof(NodeInformation), 0, () => correctNodeInfo, (o) => correctNodeInfo = (NodeInformation)o);
                        yield return new MainDataInfomation(typeof(bool), () => isOldCreaVersion, (o) => isOldCreaVersion = (bool)o);
                        yield return new MainDataInfomation(typeof(bool), () => isAlreadyConnected, (o) => isAlreadyConnected = (bool)o);
                        yield return new MainDataInfomation(typeof(int), () => protocolVersion, (o) => protocolVersion = (int)o);
                        yield return new MainDataInfomation(typeof(string), () => client, (o) => client = (string)o);
                    }
                    else
                        throw new NotSupportedException("header_stream_info");
                }
            }

            public override bool IsVersioned
            {
                get { return true; }
            }

            public override bool IsCorruptionChecked
            {
                get
                {
                    if (Version <= 0)
                        return true;
                    else
                        throw new NotSupportedException("header_res_corruption_checked");
                }
            }
        }

        private readonly int creaVersion;
        private readonly string appnameWithVersion;
        private readonly int protocolVersion;

        private readonly ushort port;
        public ushort Port
        {
            get { return port; }
        }

        protected IPAddress ipAddress;
        public IPAddress IpAddress
        {
            get
            {
                if (!isStartCompleted)
                    throw new InvalidOperationException("crea_node_not_start_completed");

                return ipAddress;
            }
        }

        protected NodeInformation nodeInfo;
        public NodeInformation NodeInfo
        {
            get
            {
                if (!isStartCompleted)
                    throw new InvalidOperationException("crea_node_not_start_completed");

                return nodeInfo;
            }
        }

        private bool isStarted;
        public bool IsStarted
        {
            get { return isStarted; }
        }

        private bool isStartCompleted;
        public bool IsStartCompleted
        {
            get { return isStartCompleted; }
        }

        public bool IsPort0
        {
            get { return port == 0; }
        }

        public bool IsServer
        {
            get
            {
                if (!isStartCompleted)
                    throw new InvalidOperationException("crea_node_not_start_completed");

                return !IsPort0 && ipAddress != null;
            }
        }

        public CREANODEBASE(ushort _port, int _creaVersion, string _appnameWithVersion)
        {
            port = _port;
            creaVersion = _creaVersion;
            appnameWithVersion = _appnameWithVersion;
            protocolVersion = 0;
        }

        private Listener listener;
        protected FirstNodeInformation[] firstNodeInfos;

        private string privateRsaParameters;
        protected override string PrivateRsaParameters
        {
            get
            {
                if (privateRsaParameters != null)
                    return privateRsaParameters;
                else
                    return base.PrivateRsaParameters;
            }
        }

        protected abstract IPAddress GetIpAddress();
        protected abstract RSACryptoServiceProvider GetRSACryptoServiceProvider();
        protected abstract void NotifyFirstNodeInfo();
        protected abstract FirstNodeInformation[] GetFirstNodeInfos();
        protected abstract void UpdateNodeState(NodeInformation nodeInfo, bool isSucceeded);
        protected abstract void KeepConnections();
        protected abstract bool IsAlreadyConnected(NodeInformation nodeInfo);
        protected abstract bool IsListenerCanContinue(NodeInformation nodeInfo);
        protected abstract bool IsWantToContinue(NodeInformation nodeInfo);
        protected abstract bool IsClientCanContinue(NodeInformation nodeInfo);

        protected abstract Network Network { get; }
        protected abstract bool IsContinue { get; }

        protected virtual void ServerProtocol(Message message, CommunicationApparatus ca, Action<string> _ConsoleWriteLine)
        {
            if (message.name == MessageName.inv)
            {
                Inv inv = message.messageBase as Inv;
                bool isNew = !txtests.Keys.Contains(inv.hash);
                ca.WriteBytes(BitConverter.GetBytes(isNew));
                if (isNew)
                {
                    TxTest txtest = SHAREDDATA.FromBinary<TxTest>(ca.ReadBytes());
                    lock (txtestsLock)
                        if (!txtests.Keys.Contains(inv.hash))
                            txtests.Add(inv.hash, txtest.data);
                        else
                            return;

                    _ConsoleWriteLine("txtest受信");

                    TxtestReceived(this, nodeInfo);

                    this.StartTask(() => DiffuseInv(txtest, inv), string.Empty, string.Empty);
                }
                else
                {
                    _ConsoleWriteLine("txtest既に存在する");

                    TxtestAlreadyExisted(this, nodeInfo);
                }
            }
            else
                throw new NotSupportedException("protocol_not_supported");
        }

        protected virtual void ClientProtocol(MessageBase[] messages, CommunicationApparatus ca, Action<string> _ConsoleWriteLine)
        {
            Message message = new Message(messages[0]);

            ca.WriteBytes(message.ToBinary());
            if (message.name == MessageName.inv)
            {
                bool isNew = BitConverter.ToBoolean(ca.ReadBytes(), 0);
                if (isNew)
                    ca.WriteBytes(messages[1].ToBinary());
            }
            else
                throw new NotSupportedException("protocol_not_supported");
        }

        protected virtual void ClientContinue(NodeInformation nodeInfo, CommunicationApparatus ca, Action<string> _ConsoleWriteLine) { }

        protected virtual void ListenerContinue(NodeInformation nodeInfo, CommunicationApparatus ca, Action<string> _ConsoleWriteLine) { }

        public event EventHandler ServerStarted = delegate { };
        public event EventHandler ServerChanged = delegate { };
        public event EventHandler ServerEnded = delegate { };

        public void Start()
        {
            if (isStarted)
                throw new InvalidOperationException("crea_node_already_started");

            this.StartTask(() =>
            {
                isStarted = true;

                if (IsPort0)
                    this.RaiseNotification("port0".GetLogMessage(), 5);
                else if ((ipAddress = GetIpAddress()) != null)
                {
                    RSACryptoServiceProvider rsacsp = GetRSACryptoServiceProvider();
                    if (rsacsp != null)
                    {
                        privateRsaParameters = rsacsp.ToXmlString(true);
                        nodeInfo = new NodeInformation(ipAddress, port, Network, rsacsp.ToXmlString(false));
                        listener = CreateListener(port, (ca, ipEndPoint) =>
                        {
                            Header header = SHAREDDATA.FromBinary<Header>(ca.ReadBytes());

                            NodeInformation aiteNodeInfo = null;
                            if (!header.nodeInfo.IpAddress.Equals(ipEndPoint.Address))
                            {
                                this.RaiseNotification("aite_wrong_node_info".GetLogMessage(ipEndPoint.Address.ToString(), header.nodeInfo.Port.ToString()), 5);

                                aiteNodeInfo = new NodeInformation(ipEndPoint.Address, header.nodeInfo.Port, header.nodeInfo.Network, header.nodeInfo.PublicRSAParameters);
                            }

                            HeaderResponse headerResponse = new HeaderResponse(nodeInfo, header.nodeInfo.Network == Network, IsAlreadyConnected(header.nodeInfo), aiteNodeInfo, header.creaVersion < creaVersion, protocolVersion, appnameWithVersion);

                            if (aiteNodeInfo == null)
                                aiteNodeInfo = header.nodeInfo;

                            if ((!headerResponse.isSameNetwork).RaiseNotification(GetType(), "aite_wrong_network".GetLogMessage(aiteNodeInfo.IpAddress.ToString(), aiteNodeInfo.Port.ToString()), 5))
                                return;
                            if (headerResponse.isAlreadyConnected.RaiseNotification(GetType(), "aite_already_connected".GetLogMessage(aiteNodeInfo.IpAddress.ToString(), aiteNodeInfo.Port.ToString()), 5))
                                return;
                            //<未実装>不良ノードは拒否する？

                            UpdateNodeState(aiteNodeInfo, true);

                            if (header.creaVersion > creaVersion)
                            {
                                //相手のクライアントバージョンの方が大きい場合の処理
                                //<未実装>使用者への通知
                                //<未実装>自動ダウンロード、バージョンアップなど
                                //ここで直接行うべきではなく、イベントを発令するべきだろう
                            }

                            ca.WriteBytes(headerResponse.ToBinary());

                            int sessionProtocolVersion = Math.Min(header.protocolVersion, protocolVersion);
                            if (sessionProtocolVersion == 0)
                            {
                                string aite = string.Join(":", header.nodeInfo.IpAddress.ToString(), header.nodeInfo.Port.ToString());
                                string zibun = string.Join(":", ipAddress.ToString(), port.ToString());
                                string aiteZibun = string.Join("-->", aite, zibun);

                                Action<string> _ConsoleWriteLine = (text) => string.Join(" ", aiteZibun, text).ConsoleWriteLine();

                                if (header.isTemporary)
                                {
                                    ServerProtocol(SHAREDDATA.FromBinary<Message>(ca.ReadBytes()), ca, _ConsoleWriteLine);

                                    if (IsContinue)
                                    {
                                        bool isWantToContinue = IsWantToContinue(header.nodeInfo);
                                        ca.WriteBytes(BitConverter.GetBytes(isWantToContinue));
                                        if (isWantToContinue)
                                        {
                                            bool isClientCanContinue = BitConverter.ToBoolean(ca.ReadBytes(), 0);
                                            if (isClientCanContinue)
                                                ListenerContinue(aiteNodeInfo, ca, _ConsoleWriteLine);
                                        }
                                    }
                                }
                                else if (IsContinue)
                                {
                                    bool isCanListenerContinue = IsListenerCanContinue(header.nodeInfo);
                                    ca.WriteBytes(BitConverter.GetBytes(isCanListenerContinue));
                                    if (isCanListenerContinue)
                                        ListenerContinue(aiteNodeInfo, ca, _ConsoleWriteLine);
                                }
                            }
                            else
                                throw new NotSupportedException("not_supported_protocol_ver");
                        });
                        listener.ClientFailed += (sender, e) =>
                        {
                            //<未実装>接続成功後通信に失敗した場合はどうすれば良いか？
                            //　　　　ノード離脱と推測してノード情報を更新するべきか？
                        };
                        listener.StartListener();

                        this.RaiseNotification("server_started".GetLogMessage(ipAddress.ToString(), port.ToString()), 5);

                        ServerStarted(this, EventArgs.Empty);

                        NotifyFirstNodeInfo();
                    }
                }

                if (nodeInfo == null)
                    firstNodeInfos = GetFirstNodeInfos();
                else
                {
                    FirstNodeInformation myfni = nodeInfo.FirstNodeInfo;
                    List<FirstNodeInformation> fnislist = new List<FirstNodeInformation>();
                    foreach (var fni in GetFirstNodeInfos())
                        if (!fni.Equals(myfni))
                            fnislist.Add(fni);
                    firstNodeInfos = fnislist.ToArray();
                }

                KeepConnections();

                isStartCompleted = true;
            }, "creanode", string.Empty);
        }

        public void End()
        {
            if (!isStartCompleted)
                throw new InvalidOperationException("crea_node_not_start_completed");
            if (!IsServer)
                throw new InvalidOperationException("crea_node_not_is_server");

            listener.EndListener();

            this.RaiseNotification("server_ended".GetLogMessage(ipAddress.ToString(), port.ToString()), 5);

            ServerEnded(this, EventArgs.Empty);
        }

        private readonly Dictionary<Sha256Hash, byte[]> txtests = new Dictionary<Sha256Hash, byte[]>();
        private readonly object txtestsLock = new object();

        public event EventHandler<NodeInformation> TxtestReceived = delegate { };
        public event EventHandler<NodeInformation> TxtestAlreadyExisted = delegate { };

        protected Client Connect(IPAddress aiteIpAddress, ushort aitePort, bool isTemporary, Action _Continued, params MessageBase[] messages)
        {
            NodeInformation aiteNodeInfo = null;
            Client client = CreateClient(aiteIpAddress, aitePort, (ca, ipEndPoint) =>
            {
                ca.WriteBytes(new Header(nodeInfo, creaVersion, protocolVersion, appnameWithVersion, isTemporary).ToBinary());
                HeaderResponse headerResponse = SHAREDDATA.FromBinary<HeaderResponse>(ca.ReadBytes());

                aiteNodeInfo = headerResponse.nodeInfo;

                if ((!headerResponse.isSameNetwork).RaiseNotification(GetType(), "wrong_network".GetLogMessage(aiteIpAddress.ToString(), aitePort.ToString()), 5))
                    return;
                if (headerResponse.isAlreadyConnected.RaiseNotification(GetType(), "already_connected".GetLogMessage(aiteIpAddress.ToString(), aitePort.ToString()), 5))
                    return;
                if (headerResponse.correctNodeInfo != null)
                {
                    //<未実装>ノード情報のIPアドレスが間違っている疑いがある場合の処理
                    //　　　　他の幾つかのノードに問い合わせて本当に間違っていたら修正
                }

                UpdateNodeState(headerResponse.nodeInfo, true);

                if (headerResponse.isOldCreaVersion)
                {
                    //相手のクライアントバージョンの方が大きい場合の処理
                    //<未実装>使用者への通知
                    //<未実装>自動ダウンロード、バージョンアップなど
                    //ここで直接行うべきではなく、イベントを発令するべきだろう
                }

                int sessionProtocolVersion = Math.Min(headerResponse.protocolVersion, protocolVersion);
                if (sessionProtocolVersion == 0)
                {
                    string zibun = string.Join(":", ipAddress.ToString(), port.ToString());
                    string aite = string.Join(":", aiteIpAddress.ToString(), aitePort.ToString());
                    string zibunAite = string.Join("-->", zibun, aite);

                    Action<string> _ConsoleWriteLine = (text) => string.Join(" ", zibunAite, text).ConsoleWriteLine();

                    if (isTemporary)
                    {
                        ClientProtocol(messages, ca, _ConsoleWriteLine);

                        if (IsContinue)
                        {
                            bool isWantToContinue = BitConverter.ToBoolean(ca.ReadBytes(), 0);
                            if (isWantToContinue)
                            {
                                bool isClientCanContinue = IsClientCanContinue(headerResponse.nodeInfo);
                                ca.WriteBytes(BitConverter.GetBytes(isClientCanContinue));
                                if (isClientCanContinue)
                                {
                                    _Continued();

                                    ClientContinue(headerResponse.nodeInfo, ca, _ConsoleWriteLine);
                                }
                            }
                        }
                    }
                    else if (IsContinue)
                    {
                        bool isListenerCanContinue = BitConverter.ToBoolean(ca.ReadBytes(), 0);
                        if (isListenerCanContinue)
                        {
                            _Continued();

                            ClientContinue(headerResponse.nodeInfo, ca, _ConsoleWriteLine);
                        }
                    }
                }
                else
                    throw new NotSupportedException("not_supported_protocol_ver");
            });
            client.ConnectFailed += (sender, e) =>
            {
                //<未実装>接続に失敗した場合のノード情報更新
            };
            client.Failed += (sender, e) =>
            {
                //<未実装>接続成功後通信に失敗した場合はどうすれば良いか？
                //　　　　ノード離脱と推測してノード情報を更新するべきか？
            };
            return client;
        }

        protected abstract void Request(NodeInformation nodeinfo, params MessageBase[] messages);
        protected abstract void Diffuse(params MessageBase[] messages);

        public void DiffuseInv(TxTest txtest, Inv inv)
        {
            if (txtest == null && inv == null)
            {
                txtest = new TxTest();
                inv = new Inv(new Sha256Hash(txtest.data.ComputeSha256()));

                lock (txtestsLock)
                    txtests.Add(inv.hash, txtest.data);

                (string.Join(":", ipAddress.ToString(), port.ToString()) + " txtest作成").ConsoleWriteLine();
            }

            Diffuse(inv, txtest);
        }

        public class NodeState
        {
            public DateTime connectedTime { get; private set; }

            public NodeState() { }

            public void Connected()
            {
                connectedTime = DateTime.Now;
            }
        }
    }

    public class CreaNode : CREANODEBASE
    {
        public CreaNode(ushort _port, int _creaVersion, string _appnameWithVersion) : base(_port, _creaVersion, _appnameWithVersion) { }

        protected override Network Network
        {
            get { return Network.global; }
        }

        protected override IPAddress GetIpAddress()
        {
            UPnPWanService upnpws = UPnPWanService.FindUPnPWanService();

            if (upnpws.IsNull().RaiseError(this.GetType(), "upnp_not_found".GetLogMessage(), 5))
                return null;

            return upnpws.GetExternalIPAddress().Operate((ip) => this.RaiseNotification("upnp_ipaddress".GetLogMessage(ip.ToString()), 5));
        }

        protected override RSACryptoServiceProvider GetRSACryptoServiceProvider()
        {
            try
            {
                return new RSACryptoServiceProvider(2048).Operate(() => this.RaiseNotification("rsa_key_create".GetLogMessage(), 5));
            }
            catch (Exception)
            {
                this.RaiseError("rsa_key_cant_create".GetLogMessage(), 5);

                return null;
            }
        }

        protected override void NotifyFirstNodeInfo()
        {
            throw new NotImplementedException();
        }

        protected override FirstNodeInformation[] GetFirstNodeInfos()
        {
            throw new NotImplementedException();
        }

        protected override void UpdateNodeState(NodeInformation nodeInfo, bool isSecceeded)
        {
            throw new NotImplementedException();
        }

        protected override void KeepConnections()
        {
            throw new NotImplementedException();
        }

        protected override bool IsAlreadyConnected(NodeInformation nodeInfo)
        {
            throw new NotImplementedException();
        }

        protected override bool IsListenerCanContinue(NodeInformation nodeInfo)
        {
            throw new NotImplementedException();
        }

        protected override bool IsWantToContinue(NodeInformation nodeInfo)
        {
            throw new NotImplementedException();
        }

        protected override bool IsClientCanContinue(NodeInformation nodeInfo)
        {
            throw new NotImplementedException();
        }

        protected override bool IsContinue
        {
            get { return true; }
        }

        protected override void ServerProtocol(Message message, CommunicationApparatus ca, Action<string> _ConsoleWriteLine)
        {
            throw new NotImplementedException();
        }

        protected override void ClientProtocol(MessageBase[] messages, CommunicationApparatus ca, Action<string> _ConsoleWriteLine)
        {
            throw new NotImplementedException();
        }

        protected override void ClientContinue(NodeInformation nodeInfo, CommunicationApparatus ca, Action<string> _ConsoleWriteLine)
        {
            throw new NotImplementedException();
        }

        protected override void ListenerContinue(NodeInformation nodeInfo, CommunicationApparatus ca, Action<string> _ConsoleWriteLine)
        {
            throw new NotImplementedException();
        }

        protected override void Request(NodeInformation nodeinfo, params MessageBase[] messages)
        {
            throw new NotImplementedException();
        }

        protected override void Diffuse(params MessageBase[] messages)
        {
            throw new NotImplementedException();
        }
    }

    #region 試験用

    public abstract class CreaNodeLocalTest : CREANODEBASE
    {
        private static readonly object fnisLock = new object();
        private static readonly List<FirstNodeInformation> fnis;
        private static readonly RSACryptoServiceProvider rsacsp;

        static CreaNodeLocalTest()
        {
            fnis = new List<FirstNodeInformation>();

            try
            {
                rsacsp = new RSACryptoServiceProvider(2048);
            }
            catch (Exception)
            {
            }
        }

        public CreaNodeLocalTest(ushort _port, int _creaVersion, string _appnameWithVersion) : base(_port, _creaVersion, _appnameWithVersion) { }

        protected override Network Network
        {
            get { return Network.localtest; }
        }

        protected override IPAddress GetIpAddress()
        {
            return IPAddress.Loopback;
        }

        protected override RSACryptoServiceProvider GetRSACryptoServiceProvider()
        {
            return rsacsp;
        }

        protected override void NotifyFirstNodeInfo()
        {
            lock (fnisLock)
            {
                while (fnis.Count >= 20)
                    fnis.RemoveAt(fnis.Count - 1);
                fnis.Insert(0, nodeInfo.FirstNodeInfo);
            }
        }

        protected override FirstNodeInformation[] GetFirstNodeInfos()
        {
            lock (fnisLock)
                return fnis.ToArray();
        }
    }

    public class CreaNodeLocalTestNotContinue : CreaNodeLocalTest
    {
        public CreaNodeLocalTestNotContinue(ushort _port, int _creaVersion, string _appnameWithVersion) : base(_port, _creaVersion, _appnameWithVersion) { }

        protected override bool IsAlreadyConnected(NodeInformation nodeInfo) { return false; }

        protected override void UpdateNodeState(NodeInformation nodeInfo, bool isSecceeded) { }

        protected override void KeepConnections() { }

        protected override bool IsContinue
        {
            get { return false; }
        }

        protected override bool IsListenerCanContinue(NodeInformation nodeInfo) { return false; }

        protected override bool IsWantToContinue(NodeInformation nodeInfo) { return false; }

        protected override bool IsClientCanContinue(NodeInformation nodeInfo) { return false; }

        protected override void Request(NodeInformation nodeinfo, params MessageBase[] messages)
        {
            Connect(nodeInfo.IpAddress, nodeInfo.Port, true, () => { }, messages).StartClient();
        }

        protected override void Diffuse(params MessageBase[] messages)
        {
            for (int i = 0; i < 16 && i < firstNodeInfos.Length; i++)
                Connect(firstNodeInfos[i].IpAddress, firstNodeInfos[i].Port, true, () => { }, messages).StartClient();
        }
    }

    public class CreaNodeLocalTestContinue : CreaNodeLocalTest
    {
        private readonly object nodeStatesLock = new object();
        private Dictionary<NodeInformation, NodeState> nodeStates;
        private readonly object clientNodesLock = new object();
        private Dictionary<NodeInformation, CommunicationQueue> clientNodes;
        private readonly object listenerNodesLock = new object();
        private Dictionary<NodeInformation, CommunicationQueue> listenerNodes;

        public CreaNodeLocalTestContinue(ushort _port, int _creaVersion, string _appnameWithVersion)
            : base(_port, _creaVersion, _appnameWithVersion)
        {
            nodeStates = new Dictionary<NodeInformation, NodeState>();
            clientNodes = new Dictionary<NodeInformation, CommunicationQueue>();
            listenerNodes = new Dictionary<NodeInformation, CommunicationQueue>();
        }

        public event EventHandler KeepConnectionsCompleted = delegate { };

        protected override void UpdateNodeState(NodeInformation nodeInfo, bool isSecceeded)
        {
            lock (nodeStatesLock)
                if (isSecceeded)
                {
                    if (!nodeStates.Keys.Contains(nodeInfo))
                        nodeStates.Add(nodeInfo, new NodeState());
                    nodeStates[nodeInfo].Connected();
                }
                else if (nodeStates.Keys.Contains(nodeInfo))
                    nodeStates.Remove(nodeInfo);
        }

        protected override void KeepConnections()
        {
            if (firstNodeInfos.Length == 0)
            {
                //<未実装>初期ノード情報がない場合の処理
            }
            else
            {
                for (int i = 0; i < firstNodeInfos.Length; i++)
                {
                    int count;
                    lock (clientNodesLock)
                        count = clientNodes.Count;

                    AutoResetEvent are = new AutoResetEvent(false);

                    if (count < 8)
                    {
                        Client client = Connect(firstNodeInfos[i].IpAddress, firstNodeInfos[i].Port, false, () => are.Set());
                        client.ConnectFailed += (sender, e) => are.Set();
                        client.Failed += (sender, e) => are.Set();
                        client.StartClient();
                    }

                    are.WaitOne();
                }

                this.RaiseNotification("keep_conn_completed".GetLogMessage(), 5);

                KeepConnectionsCompleted(this, EventArgs.Empty);
            }
        }

        protected override bool IsAlreadyConnected(NodeInformation nodeInfo)
        {
            lock (clientNodesLock)
                if (clientNodes.Keys.Contains(nodeInfo))
                    return true;
            lock (listenerNodesLock)
                if (listenerNodes.Keys.Contains(nodeInfo))
                    return true;
            return false;
        }

        protected override bool IsListenerCanContinue(NodeInformation nodeInfo)
        {
            lock (listenerNodesLock)
                return listenerNodes.Count < 16;
        }

        protected override bool IsWantToContinue(NodeInformation nodeInfo)
        {
            lock (listenerNodesLock)
                return listenerNodes.Count < 16;
        }

        protected override bool IsClientCanContinue(NodeInformation nodeInfo)
        {
            lock (clientNodesLock)
                return clientNodes.Count < 8;
        }

        protected override void ClientContinue(NodeInformation nodeInfo, CommunicationApparatus ca, Action<string> _ConsoleWriteLine)
        {
            lock (clientNodesLock)
                clientNodes.Add(nodeInfo, new CommunicationQueue(ca, _ConsoleWriteLine));

            try
            {
                Continue(nodeInfo, ca, _ConsoleWriteLine);
            }
            finally
            {
                lock (clientNodesLock)
                    clientNodes.Remove(nodeInfo);
            }
        }

        protected override void ListenerContinue(NodeInformation nodeInfo, CommunicationApparatus ca, Action<string> _ConsoleWriteLine)
        {
            lock (listenerNodesLock)
                listenerNodes.Add(nodeInfo, new CommunicationQueue(ca, _ConsoleWriteLine));

            try
            {
                Continue(nodeInfo, ca, _ConsoleWriteLine);
            }
            finally
            {
                lock (listenerNodesLock)
                    listenerNodes.Remove(nodeInfo);
            }
        }

        private void Continue(NodeInformation nodeInfo, CommunicationApparatus ca, Action<string> _ConsoleWriteLine)
        {
            _ConsoleWriteLine("常時接続" + string.Join(",", clientNodes.Count.ToString(), listenerNodes.Count.ToString()));

            while (true)
                try
                {
                    ServerProtocol(SHAREDDATA.FromBinary<Message>(ca.ReadBytes()), ca, _ConsoleWriteLine);
                }
                catch (IOException ex)
                {
                    if (ex.InnerException is SocketException && (ex.InnerException as SocketException).ErrorCode == 10060)
                        _ConsoleWriteLine("WSAETIMEDOUT");
                    else
                        throw ex;
                }
        }

        protected override void Request(NodeInformation nodeinfo, params MessageBase[] messages)
        {
            CommunicationQueue cq = null;

            lock (clientNodesLock)
                if (clientNodes.Keys.Contains(nodeinfo))
                    cq = clientNodes[nodeinfo];
            lock (listenerNodesLock)
                if (listenerNodes.Keys.Contains(nodeinfo))
                    cq = listenerNodes[nodeinfo];

            if (cq != null)
                try
                {
                    ClientProtocol(messages, cq.ca, cq._ConsoleWriteLine);
                }
                catch (Exception ex)
                {
                    this.RaiseError("client_socket".GetLogMessage(), 5, ex);

                    //<未実装>通信失敗の場合の処理
                }
            else
                Connect(nodeInfo.IpAddress, nodeInfo.Port, true, () => { }, messages).StartClient();
        }

        protected override void Diffuse(params MessageBase[] messages)
        {
            List<KeyValuePair<NodeInformation, CommunicationQueue>> cqs = new List<KeyValuePair<NodeInformation, CommunicationQueue>>();
            lock (clientNodesLock)
                foreach (var cq in clientNodes)
                    cqs.Add(cq);
            lock (listenerNodesLock)
                foreach (var cq in listenerNodes)
                    cqs.Add(cq);

            foreach (var cq in cqs)
                try
                {
                    ClientProtocol(messages, cq.Value.ca, cq.Value._ConsoleWriteLine);
                }
                catch (Exception ex)
                {
                    this.RaiseError("client_socket".GetLogMessage(), 5, ex);

                    //<未実装>通信失敗の場合の処理
                }
        }

        public class CommunicationQueue
        {
            public CommunicationApparatus ca { get; private set; }
            public Action<string> _ConsoleWriteLine { get; private set; }

            public CommunicationQueue(CommunicationApparatus _ca, Action<string> __ConsoleWriteLine)
            {
                ca = _ca;
                _ConsoleWriteLine = __ConsoleWriteLine;
            }

            public Session NewSession()
            {
                return null;
            }
        }

        public class Session
        {
            public void Close()
            {

            }
        }

        public class SessionMessage : Message
        {
            public uint id { get; private set; }

            public SessionMessage(MessageBase _messageBase, uint _id)
                : base(_messageBase)
            {
                id = _id;
            }

            public SessionMessage() : base(null) { }

            protected override Func<ReaderWriter, IEnumerable<MainDataInfomation>> StreamInfo
            {
                get
                {
                    return (msrw) => base.StreamInfo(msrw).Concat(new MainDataInfomation[]{
                        new MainDataInfomation(typeof(uint), () => id, (o) => id = (uint)o),
                    });
                }
            }
        }

        protected override bool IsContinue
        {
            get { return true; }
        }
    }

    public class CreaNetworkLocalTest
    {
        public CreaNetworkLocalTest(Program.Logger _logger, Action<Exception, Program.ExceptionKind> _OnException)
        {
            App app = new App();
            app.DispatcherUnhandledException += (sender, e) =>
            {
                _OnException(e.Exception, Program.ExceptionKind.wpf);
            };
            app.Startup += (sender, e) =>
            {
                TestWindow tw = new TestWindow(_logger);
                tw.Show();
            };
            app.InitializeComponent();
            app.Run();
        }

        public class TestWindow : Window
        {
            public TestWindow(Program.Logger _logger)
            {
                StackPanel sp = null;

                Loaded += (sender, e) =>
                {
                    Grid grid = new Grid();
                    grid.RowDefinitions.Add(new RowDefinition());
                    grid.RowDefinitions.Add(new RowDefinition());
                    grid.ColumnDefinitions.Add(new ColumnDefinition());

                    ScrollViewer sv1 = new ScrollViewer();
                    sv1.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
                    sv1.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
                    sv1.SetValue(Grid.RowProperty, 0);
                    sv1.SetValue(Grid.ColumnProperty, 0);

                    StackPanel sp1 = sp = new StackPanel();
                    sp1.Background = Brushes.Black;

                    ScrollViewer sv2 = new ScrollViewer();
                    sv2.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
                    sv2.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
                    sv2.SetValue(Grid.RowProperty, 1);
                    sv2.SetValue(Grid.ColumnProperty, 0);

                    StackPanel sp2 = new StackPanel();
                    sp2.Background = Brushes.Black;

                    sv1.Content = sp1;
                    sv2.Content = sp2;

                    grid.Children.Add(sv1);
                    grid.Children.Add(sv2);

                    Content = grid;

                    Console.SetOut(new TextBlockStreamWriter(sp1));

                    _logger.LogAdded += (sender2, e2) => ((Action)(() =>
                    {
                        TextBlock tb = new TextBlock();
                        tb.Text = e2.Text;
                        tb.Foreground = e2.Kind == Program.LogData.LogKind.error ? Brushes.Red : Brushes.White;

                        sp2.Children.Add(tb);
                    })).BeginExecuteInUIThread();

                    this.StartTask(() =>
                    {
                        RealInboundChennel ric = new RealInboundChennel(7777, 100);
                        ric.Accepted += (sender2, e2) =>
                        {
                            this.Lambda(() => Console.WriteLine("accepted")).BeginExecuteInUIThread();

                            for (int i = 0; i < 4; i++)
                            {
                                byte[] bytes = e2.ReadBytes();

                                uint id = BitConverter.ToUInt32(bytes, 0);
                                string text = Encoding.UTF8.GetString(bytes, 4, bytes.Length - 4);

                                this.Lambda(() => Console.WriteLine(string.Join(":", id.ToString(), text))).BeginExecuteInUIThread();
                            }

                            //string str = Encoding.UTF8.GetString(e2.ReadBytes());

                            //this.Lambda(() => Console.WriteLine(str)).BeginExecuteInUIThread();
                        };
                        ric.RequestAcceptanceStart();

                        Thread.Sleep(1000);

                        RealOutboundChannel roc = new RealOutboundChannel(IPAddress.Loopback, 7777);
                        roc.Connected += (sender2, e2) =>
                        {
                            SessionChannel sc1 = e2.NewSession();

                            sc1.WriteBytes(Encoding.UTF8.GetBytes("test1-sc1"));

                            SessionChannel sc2 = e2.NewSession();

                            sc2.WriteBytes(Encoding.UTF8.GetBytes("test2-sc2"));

                            sc1.WriteBytes(Encoding.UTF8.GetBytes("test3-sc1"));

                            sc2.WriteBytes(Encoding.UTF8.GetBytes("test4-sc2"));

                            //this.Lambda(() => Console.WriteLine("connected")).BeginExecuteInUIThread();

                            //string str = "test";

                            //e2.WriteBytes(Encoding.UTF8.GetBytes(str));
                        };
                        roc.RequestConnection();

                        //Test2NodesContinue();
                    }, string.Empty, string.Empty);
                };

                Closed += (sender, e) =>
                {
                    string fileText = string.Empty;
                    foreach (var child in sp.Children)
                        fileText += (child as TextBlock).Text + Environment.NewLine;

                    File.AppendAllText(Path.Combine(new FileInfo(Assembly.GetEntryAssembly().Location).DirectoryName, "LogTest.txt"), fileText);
                };
            }

            private void Test2NodesContinue()
            {
                CreaNodeLocalTestContinue cnltc1 = new CreaNodeLocalTestContinue(7777, 0, "test");
                cnltc1.Start();
                while (!cnltc1.IsStartCompleted)
                    Thread.Sleep(100);
                CreaNodeLocalTestContinue cnltc2 = new CreaNodeLocalTestContinue(7778, 0, "test");
                //cnltc2.KeepConnectionsCompleted += (sender, e) => cnltc2.DiffuseInv(null, null);
                cnltc2.Start();
                while (!cnltc2.IsStartCompleted)
                    Thread.Sleep(100);
            }

            private void Test2NodesInv()
            {
                CreaNodeLocalTestNotContinue cnlt1 = new CreaNodeLocalTestNotContinue(7777, 0, "test");
                cnlt1.Start();
                while (!cnlt1.IsStartCompleted)
                    Thread.Sleep(100);
                CreaNodeLocalTestNotContinue cnlt2 = new CreaNodeLocalTestNotContinue(7778, 0, "test");
                cnlt2.Start();
                while (!cnlt2.IsStartCompleted)
                    Thread.Sleep(100);

                cnlt2.DiffuseInv(null, null);
            }

            private void Test100NodesInv()
            {
                Stopwatch stopwatch = new Stopwatch();
                int counter = 0;

                int numOfNodes = 100;
                CreaNodeLocalTest[] cnlts = new CreaNodeLocalTest[numOfNodes];
                for (int i = 0; i < numOfNodes; i++)
                {
                    cnlts[i] = new CreaNodeLocalTestNotContinue((ushort)(7777 + i), 0, "test");
                    cnlts[i].TxtestReceived += (sender2, e2) =>
                    {
                        counter++;
                        (string.Join(":", e2.IpAddress.ToString(), e2.Port.ToString()) + " " + counter.ToString() + " " + ((double)counter / (double)numOfNodes).ToString() + " " + stopwatch.Elapsed.ToString()).ConsoleWriteLine();

                        if (counter == numOfNodes - 1)
                            stopwatch.Stop();
                    };
                    cnlts[i].Start();
                    while (!cnlts[i].IsStartCompleted)
                        Thread.Sleep(100);
                }

                MessageBox.Show("start");

                stopwatch.Start();

                cnlts[numOfNodes - 1].DiffuseInv(null, null);
            }

            public class TextBlockStreamWriter : TextWriter
            {
                StackPanel sp = null;

                public TextBlockStreamWriter(StackPanel _sp)
                {
                    sp = _sp;
                }

                public override void WriteLine(string value)
                {
                    base.WriteLine(value);

                    TextBlock tb = new TextBlock();
                    tb.Text = string.Join(" ", DateTime.Now.ToString(), value);
                    tb.Foreground = Brushes.White;

                    sp.Children.Add(tb);
                }

                public override Encoding Encoding
                {
                    get { return Encoding.UTF8; }
                }
            }
        }
    }

    #endregion

    public class FirstNodeInformation : SHAREDDATA, IEquatable<FirstNodeInformation>
    {
        private IPAddress ipAddress;
        public IPAddress IpAddress
        {
            get { return ipAddress; }
        }

        private ushort port;
        public ushort Port
        {
            get { return port; }
        }

        private Network network;
        public Network Network
        {
            get { return network; }
        }

        protected FirstNodeInformation(int? _version) : base(_version) { }

        public FirstNodeInformation() : this((int?)null) { }

        protected FirstNodeInformation(int? _version, IPAddress _ipAddress, ushort _port, Network _network)
            : base(_version)
        {
            ipAddress = _ipAddress;
            port = _port;
            network = _network;

            if (ipAddress.AddressFamily != AddressFamily.InterNetwork && ipAddress.AddressFamily != AddressFamily.InterNetworkV6)
                throw new ArgumentException("first_node_info_ip_address");
            if (port == 0)
                throw new ArgumentException("first_node_info_port");
        }

        public FirstNodeInformation(IPAddress _ipAddress, ushort _port, Network _network) : this(null, _ipAddress, _port, _network) { }

        public FirstNodeInformation(string _hex)
        {
            Hex = _hex;
        }

        public string Hex
        {
            get
            {
                byte[] plainBytes = ipAddress.GetAddressBytes().Combine(BitConverter.GetBytes(port), BitConverter.GetBytes((int)network));
                byte[] cypherBytes = new byte[plainBytes.Length * 4];

                for (int i = 0; i < plainBytes.Length / 2; i++)
                {
                    //正則行列の生成
                    byte[] matrix = new byte[4];
                    do
                    {
                        for (int j = 0; j < 4; j++)
                            matrix[j] = (byte)128.RandomNum();
                    }
                    while (matrix[0] * matrix[3] - matrix[1] * matrix[2] == 0);

                    //答えの生成
                    byte[] answer1 = BitConverter.GetBytes((ushort)(matrix[0] * plainBytes[2 * i] + matrix[1] * plainBytes[2 * i + 1]));
                    byte[] answer2 = BitConverter.GetBytes((ushort)(matrix[2] * plainBytes[2 * i] + matrix[3] * plainBytes[2 * i + 1]));

                    Array.Copy(matrix, 0, cypherBytes, 8 * i, 4);
                    Array.Copy(answer1, 0, cypherBytes, 8 * i + 4, 2);
                    Array.Copy(answer2, 0, cypherBytes, 8 * i + 6, 2);
                }

                using (MemoryStream ms = new MemoryStream())
                {
                    using (DeflateStream ds = new DeflateStream(ms, CompressionMode.Compress))
                    {
                        ds.Write(cypherBytes, 0, cypherBytes.Length);
                        ds.Flush();
                    }

                    return ms.ToArray().ToHexstring();
                }
            }
            protected set
            {
                byte[] cypherBytes;
                using (MemoryStream ms = new MemoryStream(value.FromHexstring()))
                using (DeflateStream ds = new DeflateStream(ms, CompressionMode.Decompress))
                {
                    byte[] buffer1 = new byte[(4 + 2 + 4) * 4];
                    byte[] buffer2 = new byte[(16 + 2 + 4) * 4 - (4 + 2 + 4) * 4];
                    ds.Read(buffer1, 0, (4 + 2 + 4) * 4);
                    ds.Read(buffer2, 0, (16 + 2 + 4) * 4 - (4 + 2 + 4) * 4);

                    cypherBytes = buffer2.IsZeroBytes() ? buffer1 : buffer1.Combine(buffer2);
                }

                byte[] plainBytes = new byte[cypherBytes.Length / 4];

                for (int i = 0; i < plainBytes.Length / 2; i++)
                {
                    byte[] matrix = new byte[4];
                    byte[] answer1 = new byte[2];
                    byte[] answer2 = new byte[2];
                    Array.Copy(cypherBytes, 8 * i, matrix, 0, 4);
                    Array.Copy(cypherBytes, 8 * i + 4, answer1, 0, 2);
                    Array.Copy(cypherBytes, 8 * i + 6, answer2, 0, 2);

                    ushort answer3 = BitConverter.ToUInt16(answer1, 0);
                    ushort answer4 = BitConverter.ToUInt16(answer2, 0);

                    plainBytes[2 * i] = (byte)((matrix[3] * answer3 - matrix[1] * answer4) / (matrix[0] * matrix[3] - matrix[1] * matrix[2]));
                    plainBytes[2 * i + 1] = (byte)((-matrix[2] * answer3 + matrix[0] * answer4) / (matrix[0] * matrix[3] - matrix[1] * matrix[2]));
                }

                byte[] ipAddressBytes = cypherBytes.Length == 40 ? new byte[4] : new byte[16];
                byte[] portBytes = new byte[2];
                byte[] networkBytes = new byte[4];
                Array.Copy(plainBytes, 0, ipAddressBytes, 0, cypherBytes.Length == 40 ? 4 : 16);
                Array.Copy(plainBytes, cypherBytes.Length == 40 ? 4 : 16, portBytes, 0, 2);
                Array.Copy(plainBytes, cypherBytes.Length == 40 ? 4 + 2 : 16 + 2, networkBytes, 0, 4);

                ipAddress = new IPAddress(ipAddressBytes);
                port = BitConverter.ToUInt16(portBytes, 0);
                network = (Network)BitConverter.ToInt32(networkBytes, 0);

                if (port == 0)
                    throw new ArgumentException("first_node_info_port");
            }
        }

        protected override Func<ReaderWriter, IEnumerable<MainDataInfomation>> StreamInfo
        {
            get
            {
                return (msrw) => new MainDataInfomation[]{
                    new MainDataInfomation(typeof(string), () => Hex, (o) => Hex = (string)o),
                };
            }
        }

        public override bool Equals(object obj)
        {
            return (obj as FirstNodeInformation).Operate((o) => o != null && Equals(o));
        }

        public override int GetHashCode()
        {
            return ipAddress.GetHashCode() ^ port.GetHashCode();
        }

        public override string ToString()
        {
            return ipAddress + ":" + port.ToString();
        }

        public bool Equals(FirstNodeInformation other)
        {
            return ipAddress.ToString() == other.ipAddress.ToString() && port == other.port;
        }
    }

    public class NodeInformation : FirstNodeInformation
    {
        private DateTime participation;
        public DateTime Participation
        {
            get { return participation; }
        }

        private string publicRSAParameters;
        public string PublicRSAParameters
        {
            get { return publicRSAParameters; }
        }

        public NodeInformation() : base(0) { }

        public NodeInformation(IPAddress _ipAddress, ushort _port, Network _network, string _publicRSAParameters)
            : base(0, _ipAddress, _port, _network)
        {
            participation = DateTime.Now;
            publicRSAParameters = _publicRSAParameters;
        }

        private Sha256Hash idCache;
        public Sha256Hash Id
        {
            get
            {
                if (idCache == null)
                    return idCache = new Sha256Hash(IpAddress.GetAddressBytes().Combine(BitConverter.GetBytes(Port), Encoding.UTF8.GetBytes(publicRSAParameters)).ComputeSha256());
                else
                    return idCache;
            }
        }

        public FirstNodeInformation FirstNodeInfo
        {
            //型変換ではなく新しいオブジェクトを作成しないとSHAREDDATA.ToBinaryで例外が発生する
            get { return new FirstNodeInformation(IpAddress, Port, Network); }
        }

        protected override Func<ReaderWriter, IEnumerable<MainDataInfomation>> StreamInfo
        {
            get
            {
                if (Version == 0)
                    return (msrw) => base.StreamInfo(msrw).Concat(new MainDataInfomation[]{
                        new MainDataInfomation(typeof(DateTime), () => participation, (o) => participation = (DateTime)o),
                        new MainDataInfomation(typeof(string), () => publicRSAParameters, (o) => publicRSAParameters = (string)o),
                    });
                else
                    throw new NotSupportedException("node_info_stream_info");
            }
        }

        public override bool IsVersioned
        {
            get { return true; }
        }

        public override bool IsCorruptionChecked
        {
            get
            {
                if (Version <= 0)
                    return true;
                else
                    throw new NotSupportedException("node_info_corruption_checked");
            }
        }

        public override bool Equals(object _obj)
        {
            return (_obj as NodeInformation).Operate((o) => o != null && Equals(o));
        }

        public override int GetHashCode()
        {
            return Id.GetHashCode();
        }

        public override string ToString()
        {
            return Id.ToString();
        }

        public bool Equals(NodeInformation other)
        {
            return Id.Equals(other.Id);
        }
    }

    #endregion

    #region データ

    public class Sha256Hash : SHAREDDATA, IComparable<Sha256Hash>, IEquatable<Sha256Hash>, IComparable
    {
        private byte[] bytes;
        public byte[] Bytes
        {
            get { return bytes; }
            set { bytes = value; }
        }

        public Sha256Hash(byte[] _bytes)
        {
            if (_bytes.Length != 32)
                throw new ArgumentException("Sha256_bytes_length");

            bytes = _bytes;
        }

        public Sha256Hash(string value) : this(value.FromHexstring()) { }

        public Sha256Hash()
        {
            bytes = new byte[32];
        }

        protected override Func<ReaderWriter, IEnumerable<MainDataInfomation>> StreamInfo
        {
            get
            {
                return (msrw) => new MainDataInfomation[]{
                    new MainDataInfomation(typeof(byte[]), 32, () => bytes, (o) => bytes = (byte[])o),
                };
            }
        }

        #region .NETオーバーライド、インターフェイス実装

        public override bool Equals(object obj)
        {
            if (!(obj is Sha256Hash))
                return false;
            return bytes.BytesEquals((obj as Sha256Hash).bytes);
        }

        public bool Equals(Sha256Hash other) { return bytes.BytesEquals(other.bytes); }

        public int CompareTo(object obj)
        {
            Sha256Hash other = obj as Sha256Hash;

            int returnValue = 0;
            //大きい桁（要素の最初）から大小を調べていけば良い
            for (int i = 0; i < bytes.Length; i++)
                if ((returnValue = bytes[i].CompareTo(other.bytes[i])) != 0)
                    return returnValue;
            return returnValue;
        }

        public int CompareTo(Sha256Hash other)
        {
            int returnValue = 0;
            //大きい桁（要素の最初）から大小を調べていけば良い
            for (int i = 0; i < bytes.Length; i++)
                if ((returnValue = bytes[i].CompareTo(other.bytes[i])) != 0)
                    return returnValue;
            return returnValue;
        }

        public override int GetHashCode()
        {
            //暗号通貨におけるハッシュ値は先頭に0が並ぶことがあるので
            //ビットの並びをばらばらにしてから計算することにした
            //この実装でも0の数は変わらないので値が偏るのかもしれない
            //先頭の0を取り除いたものから計算するべきなのかもしれない
            //2014/04/06 常に同一の並びでなければ値が毎回変わってしまう
            byte[] ramdomBytes = bytes.BytesRandomCache();
            byte[] intByte = new byte[4];
            for (int i = 0; i < 4; i++)
                for (int j = 0; j < 8; j++)
                    intByte[i] ^= ramdomBytes[i * 8 + j];
            int hash = 0;
            for (int i = 0; i < 4; i++)
                hash |= intByte[i] << (i * 8);
            return hash;
        }

        public override string ToString() { return bytes.ToHexstring(); }

        #endregion
    }

    public class Ripemd160Hash : SHAREDDATA, IComparable<Ripemd160Hash>, IEquatable<Ripemd160Hash>, IComparable
    {
        private byte[] bytes;
        public byte[] Bytes
        {
            get { return bytes; }
            set { bytes = value; }
        }

        public Ripemd160Hash(byte[] _bytes)
        {
            if (_bytes.Length != 20)
                throw new ArgumentException("Ripemd160_bytes_length");

            bytes = _bytes;
        }

        public Ripemd160Hash(string value) : this(value.FromHexstring()) { }

        public Ripemd160Hash()
        {
            bytes = new byte[20];
        }

        protected override Func<ReaderWriter, IEnumerable<MainDataInfomation>> StreamInfo
        {
            get
            {
                return (msrw) => new MainDataInfomation[]{
                    new MainDataInfomation(typeof(byte[]), 20, () => bytes, (o) => bytes = (byte[])o),
                };
            }
        }

        #region .NETオーバーライド、インターフェイス実装

        public override bool Equals(object obj)
        {
            if (!(obj is Ripemd160Hash))
                return false;
            return bytes.BytesEquals((obj as Ripemd160Hash).bytes);
        }

        public bool Equals(Ripemd160Hash other) { return bytes.BytesEquals(other.bytes); }

        public int CompareTo(object obj)
        {
            Ripemd160Hash other = obj as Ripemd160Hash;

            int returnValue = 0;
            //大きい桁（要素の最初）から大小を調べていけば良い
            for (int i = 0; i < bytes.Length; i++)
                if ((returnValue = bytes[i].CompareTo(other.bytes[i])) != 0)
                    return returnValue;
            return returnValue;
        }

        public int CompareTo(Ripemd160Hash other)
        {
            int returnValue = 0;
            //大きい桁（要素の最初）から大小を調べていけば良い
            for (int i = 0; i < bytes.Length; i++)
                if ((returnValue = bytes[i].CompareTo(other.bytes[i])) != 0)
                    return returnValue;
            return returnValue;
        }

        public override int GetHashCode()
        {
            //暗号通貨におけるハッシュ値は先頭に0が並ぶことがあるので
            //ビットの並びをばらばらにしてから計算することにした
            //この実装でも0の数は変わらないので値が偏るのかもしれない
            //先頭の0を取り除いたものから計算するべきなのかもしれない
            //2014/04/06 常に同一の並びでなければ値が毎回変わってしまう
            byte[] ramdomBytes = bytes.BytesRandomCache();
            byte[] intByte = new byte[4];
            for (int i = 0; i < 4; i++)
                for (int j = 0; j < 5; j++)
                    intByte[i] ^= ramdomBytes[i * 5 + j];
            int hash = 0;
            for (int i = 0; i < 4; i++)
                hash |= intByte[i] << (i * 8);
            return hash;
        }

        public override string ToString() { return bytes.ToHexstring(); }

        #endregion
    }

    public class EcdsaKey : SHAREDDATA
    {
        private EcdsaKeyLength keyLength;
        public EcdsaKeyLength KeyLength
        {
            get { return keyLength; }
        }

        private byte[] publicKey;
        public byte[] PublicKey
        {
            get { return publicKey; }
        }

        private byte[] privateKey;
        public byte[] PrivateKey
        {
            get { return privateKey; }
        }

        public enum EcdsaKeyLength { Ecdsa256, Ecdsa384, Ecdsa521 }

        public EcdsaKey(EcdsaKeyLength _keyLength)
            : base(0)
        {
            keyLength = _keyLength;

            CngAlgorithm ca;
            if (keyLength == EcdsaKeyLength.Ecdsa256)
                ca = CngAlgorithm.ECDsaP256;
            else if (keyLength == EcdsaKeyLength.Ecdsa384)
                ca = CngAlgorithm.ECDsaP384;
            else if (keyLength == EcdsaKeyLength.Ecdsa521)
                ca = CngAlgorithm.ECDsaP521;
            else
                throw new NotSupportedException("ecdsa_key_length_not_suppoeted");

            CngKey ck = CngKey.Create(ca, null, new CngKeyCreationParameters { ExportPolicy = CngExportPolicies.AllowPlaintextExport });

            publicKey = ck.Export(CngKeyBlobFormat.EccPublicBlob);
            privateKey = ck.Export(CngKeyBlobFormat.EccPrivateBlob);
        }

        public EcdsaKey() : this(EcdsaKeyLength.Ecdsa256) { }

        protected override Func<ReaderWriter, IEnumerable<MainDataInfomation>> StreamInfo
        {
            get
            {
                if (Version == 0)
                {
                    return (msrw) => new MainDataInfomation[]{
                        new MainDataInfomation(typeof(int), () => (int)keyLength, (o) => keyLength = (EcdsaKeyLength)o),
                        new MainDataInfomation(typeof(byte[]), null, () => publicKey, (o) => publicKey = (byte[])o),
                        new MainDataInfomation(typeof(byte[]), null, () => privateKey, (o) => privateKey = (byte[])o),
                    };
                }
                else
                    throw new NotSupportedException("ecdsa_key_main_data_info");

            }
        }

        public override bool IsVersioned
        {
            get { return true; }
        }

        public override bool IsCorruptionChecked
        {
            get
            {
                if (Version <= 0)
                    return true;
                else
                    throw new NotSupportedException("ecdsa_key_check");
            }
        }
    }

    public class Account : SHAREDDATA
    {
        private string name;
        public string Name
        {
            get { return name; }
            set { this.ExecuteBeforeEvent(() => name = value, AccountChanged); }
        }

        private string description;
        public string Description
        {
            get { return description; }
            set { this.ExecuteBeforeEvent(() => description = value, AccountChanged); }
        }

        private EcdsaKey key;
        public EcdsaKey Key
        {
            get { return key; }
        }

        public Account(string _name, string _description, EcdsaKey.EcdsaKeyLength _keyLength)
            : base(0)
        {
            name = _name;
            description = _description;
            key = new EcdsaKey(_keyLength);
        }

        public Account(string _name, string _description) : this(_name, _description, EcdsaKey.EcdsaKeyLength.Ecdsa256) { }

        public Account(EcdsaKey.EcdsaKeyLength _keyLength) : this(string.Empty, string.Empty, _keyLength) { }

        public Account() : this(string.Empty, string.Empty, EcdsaKey.EcdsaKeyLength.Ecdsa256) { }

        public class AccountAddress
        {
            private Ripemd160Hash hash;
            public Ripemd160Hash Hash
            {
                get
                {
                    if (hash != null)
                        return hash;
                    else
                    {
                        byte[] mergedMergedBytes = Base58Encoding.Decode(base58);

                        byte[] mergedBytes = new byte[mergedMergedBytes.Length - 4];
                        Array.Copy(mergedMergedBytes, 0, mergedBytes, 0, mergedBytes.Length);
                        byte[] checkBytes = new byte[4];
                        Array.Copy(mergedMergedBytes, mergedBytes.Length, checkBytes, 0, 4);

                        int check1 = BitConverter.ToInt32(checkBytes, 0);
                        int check2 = BitConverter.ToInt32(mergedBytes.ComputeSha256().ComputeSha256(), 0);
                        if (check1 != check2)
                            throw new InvalidDataException("base58_check");

                        byte[] identifierBytes = new byte[3];
                        Array.Copy(mergedBytes, 0, identifierBytes, 0, identifierBytes.Length);
                        byte[] hashBytes = new byte[mergedBytes.Length - identifierBytes.Length];
                        Array.Copy(mergedBytes, identifierBytes.Length, hashBytes, 0, hashBytes.Length);

                        //base58表現の先頭がCREAになるようなバイト配列を使っている
                        byte[] correctIdentifierBytes = new byte[] { 84, 122, 143 };

                        if (!identifierBytes.BytesEquals(correctIdentifierBytes))
                            throw new InvalidDataException("base58_identifier");

                        return hash = new Ripemd160Hash(hashBytes);
                    }
                }
                set
                {
                    hash = value;
                    base58 = null;
                }
            }

            private string base58;
            public string Base58
            {
                get
                {
                    if (base58 != null)
                        return base58;
                    else
                    {
                        //base58表現の先頭がCREAになるようなバイト配列を使っている
                        byte[] identifierBytes = new byte[] { 84, 122, 143 };
                        byte[] hashBytes = hash.Bytes;

                        byte[] mergedBytes = new byte[identifierBytes.Length + hashBytes.Length];
                        Array.Copy(identifierBytes, 0, mergedBytes, 0, identifierBytes.Length);
                        Array.Copy(hashBytes, 0, mergedBytes, identifierBytes.Length, hashBytes.Length);

                        //先頭4バイトしか使用しない
                        byte[] checkBytes = mergedBytes.ComputeSha256().ComputeSha256();

                        byte[] mergedMergedBytes = new byte[mergedBytes.Length + 4];
                        Array.Copy(mergedBytes, 0, mergedMergedBytes, 0, mergedBytes.Length);
                        Array.Copy(checkBytes, 0, mergedMergedBytes, mergedBytes.Length, 4);

                        return base58 = Base58Encoding.Encode(mergedMergedBytes);
                    }
                }
                set
                {
                    base58 = value;
                    hash = null;
                }
            }

            public AccountAddress(byte[] _publicKey)
            {
                hash = new Ripemd160Hash(_publicKey.ComputeSha256().ComputeRipemd160());
            }

            public AccountAddress(Ripemd160Hash _hash)
            {
                hash = _hash;
            }

            public AccountAddress(string _base58)
            {
                base58 = _base58;
            }

            public override string ToString() { return Base58; }
        }

        protected override Func<ReaderWriter, IEnumerable<MainDataInfomation>> StreamInfo
        {
            get
            {
                if (Version == 0)
                    return (msrw) => new MainDataInfomation[]{
                        new MainDataInfomation(typeof(string), () => name, (o) => name = (string)o),
                        new MainDataInfomation(typeof(string), () => description, (o) => description = (string)o),
                        new MainDataInfomation(typeof(EcdsaKey), 0, () => key, (o) => key = (EcdsaKey)o),
                    };
                else
                    throw new NotSupportedException("account_main_data_info");
            }
        }

        public override bool IsVersioned
        {
            get { return true; }
        }

        public override bool IsCorruptionChecked
        {
            get
            {
                if (Version <= 0)
                    return true;
                else
                    throw new NotSupportedException("account_check");
            }
        }

        public event EventHandler AccountChanged = delegate { };

        public AccountAddress Address
        {
            get { return new AccountAddress(key.PublicKey); }
        }

        public override string ToString() { return Address.Base58; }
    }

    public abstract class AccountHolder : SHAREDDATA
    {
        private readonly object accountsLock = new object();
        private List<Account> accounts;
        public Account[] Accounts
        {
            get { return accounts.ToArray(); }
        }

        public AccountHolder(int? _version)
            : base(_version)
        {
            accounts = new List<Account>();

            account_changed = (sender, e) => AccountHolderChanged(this, EventArgs.Empty);
        }

        public AccountHolder() : this(null) { }

        protected override Func<ReaderWriter, IEnumerable<MainDataInfomation>> StreamInfo
        {
            get
            {
                return (msrw) => new MainDataInfomation[]{
                    new MainDataInfomation(typeof(Account[]), 0, null, () => accounts.ToArray(), (o) =>
                    {
                        accounts = ((Account[])o).ToList();
                        foreach (var account in accounts)
                            account.AccountChanged += account_changed;
                    }),
                };
            }
        }

        public event EventHandler AccountAdded = delegate { };
        protected EventHandler PAccountAdded
        {
            get { return AccountAdded; }
        }

        public event EventHandler AccountRemoved = delegate { };
        protected EventHandler PAccountRemoved
        {
            get { return AccountRemoved; }
        }

        public event EventHandler AccountHolderChanged = delegate { };
        protected EventHandler PAccountHolderChanged
        {
            get { return AccountHolderChanged; }
        }

        private EventHandler account_changed;

        public void AddAccount(Account account)
        {
            lock (accountsLock)
            {
                if (accounts.Contains(account))
                    throw new InvalidOperationException("exist_account");

                this.ExecuteBeforeEvent(() =>
                {
                    accounts.Add(account);
                    account.AccountChanged += account_changed;
                }, AccountAdded, AccountHolderChanged);
            }
        }

        public void RemoveAccount(Account account)
        {
            lock (accountsLock)
            {
                if (!accounts.Contains(account))
                    throw new InvalidOperationException("not_exist_account");

                this.ExecuteBeforeEvent(() =>
                {
                    accounts.Remove(account);
                    account.AccountChanged -= account_changed;
                }, AccountRemoved, AccountHolderChanged);
            }
        }
    }

    public class AnonymousAccountHolder : AccountHolder
    {
        public AnonymousAccountHolder() : base(0) { }

        protected override Func<ReaderWriter, IEnumerable<MainDataInfomation>> StreamInfo
        {
            get
            {
                if (Version == 0)
                    return base.StreamInfo;
                else
                    throw new NotSupportedException("aah_main_data_info");
            }
        }

        public override bool IsVersioned
        {
            get { return true; }
        }

        public override bool IsCorruptionChecked
        {
            get
            {
                if (Version <= 0)
                    return true;
                else
                    throw new NotSupportedException("aah_check");
            }
        }
    }

    public class PseudonymousAccountHolder : AccountHolder
    {
        private string name;
        public string Name
        {
            get { return name; }
            set { this.ExecuteBeforeEvent(() => name = value, PAccountAdded); }
        }

        private EcdsaKey key;
        public EcdsaKey Key
        {
            get { return key; }
        }

        public PseudonymousAccountHolder(string _name, EcdsaKey.EcdsaKeyLength _keyLength)
            : base(0)
        {
            name = _name;
            key = new EcdsaKey(_keyLength);
        }

        public PseudonymousAccountHolder(string _name) : this(_name, EcdsaKey.EcdsaKeyLength.Ecdsa256) { }

        public PseudonymousAccountHolder(EcdsaKey.EcdsaKeyLength _keyLength) : this(string.Empty, _keyLength) { }

        public PseudonymousAccountHolder() : this(string.Empty, EcdsaKey.EcdsaKeyLength.Ecdsa256) { }

        protected override Func<ReaderWriter, IEnumerable<MainDataInfomation>> StreamInfo
        {
            get
            {
                if (Version == 0)
                    return (msrw) => base.StreamInfo(msrw).Concat(new MainDataInfomation[]{
                        new MainDataInfomation(typeof(string), () => name, (o) => name = (string)o),
                        new MainDataInfomation(typeof(EcdsaKey), 0, () => key, (o) => key = (EcdsaKey)o),
                    });
                else
                    throw new NotSupportedException("pah_main_data_info");
            }
        }

        public override bool IsVersioned
        {
            get { return true; }
        }

        public override bool IsCorruptionChecked
        {
            get
            {
                if (Version <= 0)
                    return true;
                else
                    throw new NotSupportedException("pah_check");
            }
        }

        public string Trip
        {
            get { return Convert.ToBase64String(key.PublicKey).Operate((s) => s.Substring(s.Length - 12, 12)); }
        }

        public string Sign
        {
            get { return name + "◆" + Trip; }
        }

        public override string ToString() { return Sign; }
    }

    public class AccountHolderDatabase : SHAREDDATA
    {
        private AnonymousAccountHolder anonymousAccountHolder;
        public AnonymousAccountHolder AnonymousAccountHolder
        {
            get { return anonymousAccountHolder; }
        }

        private readonly object pahsLock = new object();
        private List<PseudonymousAccountHolder> pseudonymousAccountHolders;
        public PseudonymousAccountHolder[] PseudonymousAccountHolders
        {
            get { return pseudonymousAccountHolders.ToArray(); }
        }

        private readonly object cahsLock = new object();
        private List<PseudonymousAccountHolder> candidateAccountHolders;
        public PseudonymousAccountHolder[] CandidateAccountHolders
        {
            get { return candidateAccountHolders.ToArray(); }
        }

        public AccountHolderDatabase()
            : base(0)
        {
            anonymousAccountHolder = new AnonymousAccountHolder();
            pseudonymousAccountHolders = new List<PseudonymousAccountHolder>();
            candidateAccountHolders = new List<PseudonymousAccountHolder>();

            accountHolders_changed = (sender, e) => AccountHoldersChanged(this, EventArgs.Empty);
        }

        protected override Func<ReaderWriter, IEnumerable<MainDataInfomation>> StreamInfo
        {
            get
            {
                if (Version == 0)
                    return (mswr) => new MainDataInfomation[]{
                        new MainDataInfomation(typeof(AnonymousAccountHolder), 0, () => anonymousAccountHolder, (o) =>
                        {
                            anonymousAccountHolder = (AnonymousAccountHolder)o;
                            anonymousAccountHolder.AccountHolderChanged += accountHolders_changed;
                        }),
                        new MainDataInfomation(typeof(PseudonymousAccountHolder[]), 0, null, () => pseudonymousAccountHolders.ToArray(), (o) =>
                        {
                            pseudonymousAccountHolders = ((PseudonymousAccountHolder[])o).ToList();
                            foreach (var pah in pseudonymousAccountHolders)
                                pah.AccountHolderChanged += accountHolders_changed;
                        }),
                        new MainDataInfomation(typeof(PseudonymousAccountHolder[]), 0, null, () => candidateAccountHolders.ToArray(), (o) => candidateAccountHolders = ((PseudonymousAccountHolder[])o).ToList()),
                    };
                else
                    throw new NotSupportedException("account_holder_database_main_data_info");
            }
        }

        public override bool IsVersioned
        {
            get { return true; }
        }

        public override bool IsCorruptionChecked
        {
            get
            {
                if (Version <= 0)
                    return true;
                else
                    throw new NotSupportedException("account_holder_database_check");
            }
        }

        public event EventHandler AccountHolderAdded = delegate { };
        public event EventHandler AccountHolderRemoved = delegate { };
        public event EventHandler AccountHoldersChanged = delegate { };

        private EventHandler accountHolders_changed;

        public void AddAccountHolder(PseudonymousAccountHolder ah)
        {
            lock (pahsLock)
            {
                if (pseudonymousAccountHolders.Contains(ah))
                    throw new InvalidOperationException("exist_account_holder");

                if (!pseudonymousAccountHolders.Where((e) => e.Name == ah.Name).FirstOrDefault().IsNotNull().RaiseError(this.GetType(), "exist_same_name_account_holder".GetLogMessage(), 5))
                    this.ExecuteBeforeEvent(() =>
                    {
                        pseudonymousAccountHolders.Add(ah);
                        ah.AccountHolderChanged += accountHolders_changed;
                    }, AccountHolderAdded, AccountHoldersChanged);
            }
        }

        public void DeleteAccountHolder(PseudonymousAccountHolder ah)
        {
            lock (pahsLock)
            {
                if (!pseudonymousAccountHolders.Contains(ah))
                    throw new InvalidOperationException("not_exist_account_holder");

                this.ExecuteBeforeEvent(() =>
                {
                    pseudonymousAccountHolders.Remove(ah);
                    ah.AccountHolderChanged -= accountHolders_changed;
                }, AccountHolderRemoved, AccountHoldersChanged);
            }
        }

        public void AddCandidateAccountHolder(PseudonymousAccountHolder ah)
        {
            lock (cahsLock)
            {
                if (candidateAccountHolders.Contains(ah))
                    throw new InvalidOperationException("exist_candidate_account_holder");

                candidateAccountHolders.Add(ah);
            }
        }

        public void DeleteCandidateAccountHolder(PseudonymousAccountHolder ah)
        {
            lock (cahsLock)
            {
                if (!candidateAccountHolders.Contains(ah))
                    throw new InvalidOperationException("not_exist_candidate_account_holder");

                candidateAccountHolders.Remove(ah);
            }
        }

        public void ClearCandidateAccountHolders()
        {
            lock (cahsLock)
                candidateAccountHolders.Clear();
        }
    }

    #endregion

    public class TransactionInput
    {
        private Sha256Hash previousTransactionHash;
        public Sha256Hash PreviousTransactionHash
        {
            get { return previousTransactionHash; }
            set { previousTransactionHash = value; }
        }

        private int previousTransactionOutputIndex;
        public int PreviousTransactionOutputIndex
        {
            get { return previousTransactionOutputIndex; }
            set { previousTransactionOutputIndex = value; }
        }
    }

    public class TransactionOutput
    {

    }

    public class BlockHeader
    {
        public static readonly int MaxBlockSize = 1000000;
        public static readonly int MaxVerify = MaxBlockSize / 50;

        private int version;
        public int Version
        {
            get { return version; }
            set { version = value; }
        }

        private Sha256Hash prevBlockHash;
        public Sha256Hash PrevBlockHash
        {
            get { return prevBlockHash; }
            set { prevBlockHash = value; }
        }

        private Sha256Hash merkleRoot;
        public Sha256Hash MercleRoot
        {
            get { return merkleRoot; }
            set { merkleRoot = value; }
        }

        private int time;
        public int Time
        {
            get { return time; }
            set { time = value; }
        }

        private int difficultyTarget;
        public int DifficultyTarget
        {
            get { return difficultyTarget; }
            set { difficultyTarget = value; }
        }

        private int nonce;
        public int Nonce
        {
            get { return nonce; }
            set { nonce = value; }
        }
    }

    #region UPnP

    public class UPnPWanService
    {
        /// <summary>
        /// ネットワーク内の UPnP Wan サービスを探す。ちょっと時間がかかる。
        /// 見つからなかったときはnullが返るので気をつける。
        /// </summary>
        /// <returns></returns>
        public static UPnPWanService FindUPnPWanService()
        {
            Guid UPnPDeviceFinderClass = new Guid("E2085F28-FEB7-404A-B8E7-E659BDEAAA02");
            IUPnPDeviceFinder finder = Activator.CreateInstance(Type.GetTypeFromCLSID(UPnPDeviceFinderClass)) as IUPnPDeviceFinder;

            string[] deviceTypes = { "urn:schemas-upnp-org:service:WANPPPConnection:1", "urn:schemas-upnp-org:service:WANIPConnection:1", };
            string[] serviceNames = { "urn:upnp-org:serviceId:WANPPPConn1", "urn:upnp-org:serviceId:WANIPConn1", };
            foreach (string deviceType in deviceTypes)
                foreach (IUPnPDevice device in finder.FindByType(deviceType, 0))
                    foreach (string serviceName in serviceNames)
                        foreach (IUPnPService service in device.Services)
                            return new UPnPWanService() { service = service };
            return null;
        }

        IUPnPService service;
        private UPnPWanService() { }

        static void ThrowForHR(string action, HRESULT_UPnP hr, object result)
        {
            if (hr != HRESULT_UPnP.S_OK)
                throw new COMException("Action " + action + " returns " + hr + " " + result, (int)hr);
        }

        /// <summary>
        /// ポートマッピングを追加する。
        /// </summary>
        /// <param name="remoteHost">通信相手。通信先を限定する場合に指定。</param>
        /// <param name="externalPort">グローバルポート番号。</param>
        /// <param name="protocol">プロトコル名。"TCP" or "UDP"を指定。</param>
        /// <param name="internalPort">内部クライアントのポート番号。</param>
        /// <param name="internalClient">内部クライアントのIPアドレス。</param>
        /// <param name="description">説明。任意。</param>
        /// <param name="leaseDuration">リース期間(秒単位)。0を指定すると無期限</param>
        public void AddPortMapping(string remoteHost, ushort externalPort, string protocol, ushort internalPort, IPAddress internalClient, bool enabled, string description, uint leaseDuration)
        {
            if (string.IsNullOrEmpty(remoteHost)) remoteHost = "";
            if (string.IsNullOrEmpty(description)) description = "";
            if (protocol != "TCP" && protocol != "UDP") throw new ArgumentException("protocol must be \"TCP\" or \"UDP\"", protocol);

            object outArgs = null;
            object result = null;
            HRESULT_UPnP hresult = service.InvokeAction("AddPortMapping",
                new object[] { remoteHost, externalPort, protocol, internalPort, internalClient.ToString(), enabled, description, leaseDuration },
                ref outArgs, out result);
            ThrowForHR("AddPortMapping", hresult, result);
        }

        /// <summary>
        /// ポートマッピングを追加する。
        /// </summary>
        /// <param name="portMapping">追加するポートマッピング</param>
        public void AddPortMapping(UPnPPortMapping portMapping)
        {
            AddPortMapping(portMapping.RemoteHost, portMapping.ExternalPort, portMapping.Protocol, portMapping.InternalPort,
                portMapping.InternalClient, portMapping.Enabled, portMapping.PortMappingDescription, portMapping.LeaseDuration);
        }

        /// <summary>
        /// ポートマッピングを削除する。
        /// </summary>
        /// <param name="remoteHost">追加時に指定した通信相手。</param>
        /// <param name="externalPort">追加時に指定した外部ポート番号。</param>
        /// <param name="protocol">追加時に指定されたプロトコル。</param>
        public void DeletePortMapping(string remoteHost, ushort externalPort, string protocol)
        {
            if (string.IsNullOrEmpty(remoteHost)) remoteHost = "";
            if (protocol != "TCP" && protocol != "UDP") throw new ArgumentException("protocol must be \"TCP\" or \"UDP\"", protocol);

            object outArgs = null;
            object result = null;
            HRESULT_UPnP hresult = service.InvokeAction("DeletePortMapping", new object[] { remoteHost, externalPort, protocol }, ref outArgs, out result);
            ThrowForHR("DeletePortMapping", hresult, result);
        }

        /// <summary>
        /// ポートマッピングを削除する。
        /// </summary>
        /// <param name="portMapping">削除するポートマッピング。RemoteHostとExternalPortとProtocolだけが使われる。</param>
        public void DeletePortMapping(UPnPPortMapping portMapping)
        {
            DeletePortMapping(portMapping.RemoteHost, portMapping.ExternalPort, portMapping.Protocol);
        }

        /// <summary>
        /// 現在設定されているポートマッピング情報を得る。
        /// </summary>
        /// <returns>ポートマッピング情報</returns>
        public List<UPnPPortMapping> GetGenericPortMappingEntries()
        {
            object outArgs = null;
            object result = null;
            List<UPnPPortMapping> list = new List<UPnPPortMapping>(16);
            for (int i = 0; ; i++)
            {
                HRESULT_UPnP hresult = service.InvokeAction("GetGenericPortMappingEntry", new object[] { i }, ref outArgs, out result);
                if (hresult != HRESULT_UPnP.S_OK) break;

                object[] array = (object[])outArgs;
                list.Add(new UPnPPortMapping
                {
                    RemoteHost = (string)array[0],
                    ExternalPort = (ushort)array[1],
                    Protocol = (string)array[2],
                    InternalPort = (ushort)array[3],
                    InternalClient = IPAddress.Parse((string)array[4]),
                    Enabled = (bool)array[5],
                    PortMappingDescription = (string)array[6],
                    LeaseDuration = (uint)array[7],
                });
            }
            return list;
        }

        /// <summary>
        /// グローバルIPアドレスを得る。
        /// </summary>
        /// <returns>IPアドレス</returns>
        public System.Net.IPAddress GetExternalIPAddress()
        {
            object outArgs = null;
            object result = null;
            HRESULT_UPnP hresult = service.InvokeAction("GetExternalIPAddress", new object[] { }, ref outArgs, out result);
            ThrowForHR("GetExternalIPAddress", hresult, result);
            return IPAddress.Parse((string)((object[])outArgs)[0]);
        }

        /// <summary>
        /// 自分のIPアドレス(v4)を得る。
        /// </summary>
        /// <returns>IPアドレス</returns>
        public System.Net.IPAddress GetLocalIPAddress()
        {
            IPAddress[] address = Array.FindAll(Dns.GetHostAddresses(Dns.GetHostName()),
                a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
            if (address.Length > 0) return address[0];
            return null;
        }

        #region COM Interop

        [ComImport]
        [Guid("ADDA3D55-6F72-4319-BFF9-18600A539B10")]
        [InterfaceType(ComInterfaceType.InterfaceIsDual)]
        [SuppressUnmanagedCodeSecurity]
        interface IUPnPDeviceFinder
        {
            [DispId(1610744809)]
            IUPnPDevices FindByType(string bstrTypeURI, int dwFlags);
            [DispId(1610744812)]
            int CreateAsyncFind(string bstrTypeURI, int dwFlags, object punkDeviceFinderCallback);
            [DispId(1610744813)]
            void StartAsyncFind(int lFindData);
            [DispId(1610744814)]
            void CancelAsyncFind(int lFindData);
            [DispId(1610744811)]
            IUPnPDevice FindByUDN(string bstrUDN);
        }

        [ComImport]
        [Guid("3D44D0D1-98C9-4889-ACD1-F9D674BF2221")]
        [InterfaceType(ComInterfaceType.InterfaceIsDual)]
        [SuppressUnmanagedCodeSecurity]
        interface IUPnPDevice
        {
            [DispId(1610747809)]
            bool IsRootDevice { get; }
            [DispId(1610747810)]
            IUPnPDevice RootDevice { get; }
            [DispId(1610747811)]
            IUPnPDevice ParentDevice { get; }
            [DispId(1610747812)]
            bool HasChildren { get; }
            [DispId(1610747813)]
            IUPnPDevices Children { get; }
            [DispId(1610747814)]
            string UniqueDeviceName { get; }
            [DispId(1610747815)]
            string FriendlyName { get; }
            [DispId(1610747816)]
            string Type { get; }
            [DispId(1610747817)]
            string PresentationURL { get; }
            [DispId(1610747818)]
            string ManufacturerName { get; }
            [DispId(1610747819)]
            string ManufacturerURL { get; }
            [DispId(1610747820)]
            string ModelName { get; }
            [DispId(1610747821)]
            string ModelNumber { get; }
            [DispId(1610747822)]
            string Description { get; }
            [DispId(1610747823)]
            string ModelURL { get; }
            [DispId(1610747824)]
            string UPC { get; }
            [DispId(1610747825)]
            string SerialNumber { get; }
            [DispId(1610747827)]
            string IconURL(string bstrEncodingFormat, int lSizeX, int lSizeY, int lBitDepth);
            [DispId(1610747828)]
            IUPnPServices Services { get; }
        }

        [ComImport]
        [Guid("FDBC0C73-BDA3-4C66-AC4F-F2D96FDAD68C")]
        [InterfaceType(ComInterfaceType.InterfaceIsDual)]
        [SuppressUnmanagedCodeSecurity]
        interface IUPnPDevices : IEnumerable
        {
            [DispId(1610747309)]
            int Count { get; }

            [DispId(-4)]
            [TypeLibFunc(TypeLibFuncFlags.FHidden | TypeLibFuncFlags.FRestricted)]
            new IEnumerator GetEnumerator();

            [DispId(0)]
            IUPnPDevice this[string bstrUDN] { get; }
        }

        [ComImport]
        [Guid("3F8C8E9E-9A7A-4DC8-BC41-FF31FA374956")]
        [InterfaceType(ComInterfaceType.InterfaceIsDual)]
        [SuppressUnmanagedCodeSecurity]
        interface IUPnPServices : IEnumerable
        {
            [DispId(1610745809)]
            int Count { get; }

            [DispId(-4)]
            [TypeLibFunc(TypeLibFuncFlags.FHidden | TypeLibFuncFlags.FRestricted)]
            new IEnumerator GetEnumerator();

            [DispId(0)]
            IUPnPService this[string bstrServiceId] { get; }
        }

        [ComImport]
        [Guid("A295019C-DC65-47DD-90DC-7FE918A1AB44")]
        [InterfaceType(ComInterfaceType.InterfaceIsDual)]
        [SuppressUnmanagedCodeSecurity]
        interface IUPnPService
        {
            [PreserveSig]
            [DispId(1610746309)]
            HRESULT_UPnP QueryStateVariable(string bstrVariableName, out /*VARIANT*/ object pvarValue);
            [PreserveSig]
            [DispId(1610746310)]
            HRESULT_UPnP InvokeAction(string bstrActionName, /*VARIANT*/ object vInActionArgs, ref /*VARIANT*/ object pvOutActionArgs, out /*VARIANT*/ object pvarRetVal);

            [DispId(1610746311)]
            string ServiceTypeIdentifier { get; }
            [DispId(1610746312)]
            void AddCallback(/*IUnknown*/ object pUnkCallback);
            [DispId(1610746313)]
            string Id { get; }
            [DispId(1610746314)]
            int LastTransportStatus { get; }
        }

        enum HRESULT_UPnP : int
        {
            S_OK = 0,
            UPNP_FACILITY_ITF = -2147221504,
            UPNP_E_ROOT_ELEMENT_EXPECTED = UPNP_FACILITY_ITF + 0x0200,
            UPNP_E_DEVICE_ELEMENT_EXPECTED,
            UPNP_E_SERVICE_ELEMENT_EXPECTED,
            UPNP_E_SERVICE_NODE_INCOMPLETE,
            UPNP_E_DEVICE_NODE_INCOMPLETE,
            UPNP_E_ICON_ELEMENT_EXPECTED,
            UPNP_E_ICON_NODE_INCOMPLETE,
            UPNP_E_INVALID_ACTION,
            UPNP_E_INVALID_ARGUMENTS,
            UPNP_E_OUT_OF_SYNC,
            UPNP_E_ACTION_REQUEST_FAILED,
            UPNP_E_TRANSPORT_ERROR,
            UPNP_E_VARIABLE_VALUE_UNKNOWN,
            UPNP_E_INVALID_VARIABLE,
            UPNP_E_DEVICE_ERROR,
            UPNP_E_PROTOCOL_ERROR,
            UPNP_E_ERROR_PROCESSING_RESPONS,
            UPNP_E_DEVICE_TIMEOUT,
            UPNP_E_INVALID_DOCUMENT = UPNP_FACILITY_ITF + 0x0500,
            UPNP_E_EVENT_SUBSCRIPTION_FAILED,
            UPNP_E_ACTION_SPECIFIC_BASE = UPNP_FACILITY_ITF + 0x0300,
            UPNP_E_ACTION_SPECIFIC_MAX = UPNP_E_ACTION_SPECIFIC_BASE + 0x0258,
        }

        #endregion
    }

    public class UPnPPortMapping
    {
        public string RemoteHost { get; set; }
        public ushort ExternalPort { get; set; }
        public string Protocol { get; set; }
        public ushort InternalPort { get; set; }
        public IPAddress InternalClient { get; set; }
        public bool Enabled { get; set; }
        public string PortMappingDescription { get; set; }
        public uint LeaseDuration { get; set; }
    }

    #endregion

    #region base58

    public static class Base58Encoding
    {
        private static readonly char[] Alphabet = "123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz".ToCharArray();
        private static readonly int Base58Length = Alphabet.Length;
        private static readonly int[] INDEXES = new int[128];
        private const int Base256Length = 256;

        static Base58Encoding()
        {
            for (int i = 0; i < INDEXES.Length; i++)
                INDEXES[i] = -1;
            for (int i = 0; i < Alphabet.Length; i++)
                INDEXES[Alphabet[i]] = i;
        }

        public static string Encode(IReadOnlyList<byte> input)
        {
            var buffer = input.MutableCopy();

            char[] temp = new char[buffer.Length * 2];
            int j = temp.Length;

            int zeroCount = LeadingZerosCount(buffer);
            int startAt = zeroCount;
            while (startAt < buffer.Length)
            {
                byte mod = divmod58(buffer, startAt);
                if (buffer[startAt] == 0)
                    ++startAt;
                temp[--j] = Alphabet[mod];
            }

            while (j < temp.Length && temp[j] == Alphabet[0])
                ++j;

            while (--zeroCount >= 0)
                temp[--j] = Alphabet[0];

            return new string(temp, j, temp.Length - j);
        }

        public static byte[] Decode(string input)
        {
            if (input.Length == 0)
                return new byte[0];

            byte[] input58 = new byte[input.Length];

            for (int i = 0; i < input.Length; ++i)
            {
                char c = input[i];

                int digit58 = -1;
                if (c >= 0 && c < 128)
                    digit58 = INDEXES[c];

                if (digit58 < 0)
                    throw new AddressFormatException("Illegal character " + c + " at " + i);

                input58[i] = (byte)digit58;
            }

            int zeroCount = 0;
            while (zeroCount < input58.Length && input58[zeroCount] == 0)
                ++zeroCount;

            byte[] temp = new byte[input.Length];
            int j = temp.Length;

            int startAt = zeroCount;
            while (startAt < input58.Length)
            {
                byte mod = divmod256(input58, startAt);
                if (input58[startAt] == 0)
                    ++startAt;

                temp[--j] = mod;
            }

            while (j < temp.Length && temp[j] == 0)
                ++j;

            var result = new byte[temp.Length - (j - zeroCount)];
            Array.Copy(temp, j - zeroCount, result, 0, result.Length);
            return result;
        }

        private static int LeadingZerosCount(IReadOnlyList<byte> buffer)
        {
            int leadingZeros = 0;
            for (leadingZeros = 0; leadingZeros < buffer.Count && buffer[leadingZeros] == 0; leadingZeros++) ;
            return leadingZeros;
        }

        private static byte divmod58(byte[] number, int startAt)
        {
            int remainder = 0;
            for (int i = startAt; i < number.Length; i++)
            {
                int digit256 = (int)number[i] & 0xFF;
                int temp = remainder * Base256Length + digit256;

                number[i] = (byte)(temp / Base58Length);

                remainder = temp % Base58Length;
            }

            return (byte)remainder;
        }

        private static byte divmod256(byte[] number58, int startAt)
        {
            int remainder = 0;
            for (int i = startAt; i < number58.Length; i++)
            {
                int digit58 = (int)number58[i] & 0xFF;
                int temp = remainder * Base58Length + digit58;

                number58[i] = (byte)(temp / Base256Length);

                remainder = temp % Base256Length;
            }

            return (byte)remainder;
        }

        private static byte[] copyOfRange(byte[] buffer, int start, int end)
        {
            var result = new byte[end - start];
            Array.Copy(buffer, start, result, 0, end - start);
            return result;
        }
    }

    public static class Utils
    {
        internal static byte[] DoubleDigest(byte[] input)
        {
            return DoubleDigest(input, 0, input.Length);
        }

        internal static byte[] DoubleDigest(byte[] input, int offset, int count)
        {
            using (var hashAlgorithm = HashAlgorithm.Create("SHA-256"))
            {
                byte[] hash = hashAlgorithm.ComputeHash(input, offset, count);
                return hashAlgorithm.ComputeHash(hash);
            }
        }

        internal static bool ArraysEqual(IReadOnlyList<byte> array1, IReadOnlyList<byte> array2)
        {
            if (array1.Count != array2.Count)
                return false;

            for (int i = 0; i < array1.Count; i++)
                if (array1[i] != array2[i])
                    return false;

            return true;
        }

        internal static byte[] MutableCopy(this IReadOnlyList<byte> readOnlyBuffer)
        {
            var buffer = new byte[readOnlyBuffer.Count];
            for (int i = 0; i < readOnlyBuffer.Count; i++)
                buffer[i] = readOnlyBuffer[i];

            return buffer;
        }
    }

    public class AddressFormatException : Exception
    {
        public AddressFormatException(string message, Exception innerException = null) : base(message, innerException) { }
    }

    #endregion

    #region Ethereum

    #endregion

    #region 自作データ構造

    //使わないと思うが勉強がてら作ってしまった

    public class ForwardLinkedList<T> : IEnumerable<T>
    {
        private Node first;
        public Node First
        {
            get { return first; }
        }

        public class Node
        {
            private T val;
            public T Value
            {
                get { return val; }
                set { val = value; }
            }

            private Node next;
            public Node Next
            {
                get { return next; }
                internal set { next = value; }
            }

            internal Node(T _val, Node _next)
            {
                val = _val;
                next = _next;
            }
        }

        public ForwardLinkedList()
        {
            first = null;
        }

        //O(n)
        public int Count
        {
            get { return first.CountLoop((p) => p != null, (p) => p.Next); }
        }

        //O(1)
        public Node InsertFirst(T element)
        {
            return first = new Node(element, first);
        }

        //O(1)
        public void DeleteFirst()
        {
            if (first != null)
                first = first.Next;
        }

        //O(1)
        public Node InsertAfter(Node node, T element)
        {
            return node.Next = new Node(element, node.Next);
        }

        //O(1)
        public void DeleteAfter(Node node)
        {
            if (node.Next != null)
                node.Next = node.Next.Next;
        }

        //O(n)
        public Node Delete(Node node)
        {
            if (first == null)
                return null;
            else if (first == node)
                return first = null;

            for (Node p = first; p.Next != null; p = p.Next)
                if (p.Next == node)
                    return p.Next = p.Next.Next;

            return null;
        }

        public IEnumerator<T> GetEnumerator()
        {
            for (Node p = first; p != null; p = p.Next)
                yield return p.Value;
        }

        IEnumerator IEnumerable.GetEnumerator() { return GetEnumerator(); }
    }

    public class LinkedList<T> : IEnumerable<T>
    {
        private Node first;
        public Node First
        {
            get { return first; }
        }

        private Node last;
        public Node Last
        {
            get { return last; }
        }

        public class Node
        {
            private T val;
            public T Value
            {
                get { return val; }
                set { val = value; }
            }

            private Node previous;
            public Node Previous
            {
                get { return previous; }
                internal set { previous = value; }
            }

            private Node next;
            public Node Next
            {
                get { return next; }
                internal set { next = value; }
            }

            internal Node(T _val, Node _previous, Node _next)
            {
                val = _val;
                previous = _previous;
                next = _next;
            }
        }

        public LinkedList()
        {
            first = null;
            last = null;
        }

        //O(n)
        public int Count
        {
            get { return first.CountLoop((p) => p != null, (p) => p.Next); }
        }

        //O(1)
        public Node InsertAfter(Node node, T element)
        {
            return node.Next = node.Next.Previous = new Node(element, node, node.Next);
        }

        //O(1)
        public Node InsertBefore(Node node, T element)
        {
            return node.Previous = node.Previous.Next = new Node(element, node.Previous, node);
        }

        //O(1)
        public Node InsertFirst(T element)
        {
            Node node = new Node(element, null, first);
            if (first == null)
                first = last = node;
            else
            {
                first.Previous = node;
                first = node;
            }
            return node;
        }

        //O(1)
        public Node InsertLast(T element)
        {
            Node node = new Node(element, last, null);
            if (last == null)
                first = last = node;
            else
            {
                last.Next = node;
                last = node;
            }
            return node;
        }

        //O(1)
        public Node Delete(Node node)
        {
            if (node != first)
                node.Previous.Next = node.Next;
            else
                first = node.Next;

            if (node != last)
                node.Next.Previous = node.Previous;
            else
                last = node.Previous;

            return node.Next;
        }

        //O(1)
        public void DeleteFirst()
        {
            if (first == last)
                first = last = null;
            else
            {
                first = first.Next;
                if (first != null)
                    first.Previous = null;
            }
        }

        //O(1)
        public void DeleteLast()
        {
            if (first == last)
                first = last = null;
            else
            {
                last = last.Previous;
                if (last != null)
                    last.Next = null;
            }
        }

        public IEnumerator<T> GetEnumerator()
        {
            for (Node p = first; p != null; p = p.Next)
                yield return p.Value;
        }

        IEnumerator IEnumerable.GetEnumerator() { return GetEnumerator(); }
    }

    public class MyArrayList<T> : IEnumerable<T>
    {
        private T[] data;
        private int count;

        public MyArrayList(int capacity)
        {
            data = new T[capacity];
            count = 0;
        }

        public MyArrayList() : this(256) { }

        public int Count
        {
            get { return count; }
        }

        //O(1)
        public T this[int index]
        {
            get { return data[index]; }
            set { data[index] = value; }
        }

        //O(n)
        public void Insert(int index, T element)
        {
            if (count >= data.Length)
            {
                T[] newData = new T[data.Length * 2];
                for (int i = 0; i < data.Length; i++)
                    newData[i] = data[i];
                data = newData;
            }

            for (int i = count; i > index; i--)
                data[i] = data[i - 1];
            data[index] = element;

            count++;
        }

        //O(1)
        public void Add(T element) { Insert(count, element); }

        //O(n)
        public void Delete(int index)
        {
            count--;

            for (int i = index; index < count; i++)
                data[i] = data[i + 1];
        }

        //O(1)
        public void DeleteLast() { count--; }

        public IEnumerator<T> GetEnumerator()
        {
            for (int i = 0; i < count; i++)
                yield return data[i];
        }

        IEnumerator IEnumerable.GetEnumerator() { return GetEnumerator(); }
    }

    public class RingBuffer<T> : IEnumerable<T>
    {
        private T[] data;
        private int top;
        private int bottom;

        public RingBuffer(int capacity)
        {
            data = new T[capacity];
            top = 0;
            bottom = 0;
        }

        public RingBuffer() : this(256) { }

        public int Count
        {
            //負数の剰余は正しく計算できない
            get { return (bottom - top + data.Length) % data.Length; }
        }

        //O(1)
        public T this[int index]
        {
            get { return data[(index + top) % data.Length]; }
            set { data[(index + top) % data.Length] = value; }
        }

        public void Insert(int index, T element)
        {
        }

        public void InsertFirst(T element)
        {
        }

        public IEnumerator<T> GetEnumerator()
        {
            throw new NotImplementedException();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            throw new NotImplementedException();
        }
    }

    public class BinaryTree<T> : IEnumerable<T> where T : IComparable<T>
    {
        private Node root;
        public Node Root
        {
            get { return root; }
        }

        public class Node
        {
            private T val;
            public T Value
            {
                get { return val; }
                internal set { val = value; }
            }

            private Node left;
            public Node Left
            {
                get { return left; }
                internal set { left = value; }
            }

            private Node right;
            public Node Right
            {
                get { return right; }
                internal set { right = value; }
            }

            private Node parent;
            public Node Parent
            {
                get { return parent; }
                internal set { parent = value; }
            }

            internal Node(T _val, Node _parent)
            {
                val = _val;
                parent = _parent;
            }

            internal Node() : this(default(T), null) { }

            public Node Next
            {
                get
                {
                    Node node = this;
                    if (node.right != null)
                        return node.right.Min;
                    else
                    {
                        while (node.parent != null && node.parent.left != node)
                            node = node.parent;
                        return node.parent;
                    }
                }
            }

            public Node Previous
            {
                get
                {
                    Node node = this;
                    if (node.left != null)
                        return node.left.Max;
                    else
                    {
                        while (node.parent != null && node.parent.right != node)
                            node = node.parent;
                        return node.parent;
                    }
                }
            }

            internal Node Min
            {
                get
                {
                    Node node = this;
                    while (node.left != null)
                        node = node.left;
                    return node;
                }
            }

            internal Node Max
            {
                get
                {
                    Node node = this;
                    while (node.right != null)
                        node = node.right;
                    return node;
                }
            }
        }

        public int Count
        {
            get
            {
                Node node = root;
                if (node == null)
                    return 0;
                else
                {
                    int count = 0;
                    for (Node n = node.Min; n != null; n = n.Next)
                        count++;
                    return count;
                }
            }
        }

        //O(log n)
        public bool Contains(T element) { return Find(element) != null; }

        //O(log n)
        public Node Find(T element)
        {
            Node node = root;
            while (node != null)
            {
                if (node.Value.CompareTo(element) > 0)
                    node = node.Left;
                else if (node.Value.CompareTo(element) < 0)
                    node = node.Right;
                else
                    break;
            }
            return node;
        }

        //O(log n)
        public void Insert(T element)
        {
            if (root == null)
                root = new Node(element, null);
            else
            {
                Node node = root;
                Node parent = null;

                while (node != null)
                {
                    parent = node;
                    if (node.Value.CompareTo(element) > 0)
                        node = node.Left;
                    else
                        node = node.Right;
                }

                node = new Node(element, parent);
                if (parent.Value.CompareTo(element) > 0)
                    parent.Left = node;
                else
                    parent.Right = node;
            }
        }

        //O(log n)
        public void Erase(T element) { Erase(Find(element)); }

        public void Erase(Node node)
        {
            if (node == null)
                return;

            if (node.Left == null)
                Replace(node, node.Right);
            else if (node.Right == null)
                Replace(node, node.Left);
            else
            {
                Node min = node.Right.Min;
                node.Value = min.Value;
                Replace(min, min.Right);
            }
        }

        private void Replace(Node node1, Node node2)
        {
            Node parent = node1.Parent;
            if (node2 != null)
                node2.Parent = parent;
            if (node1 == root)
                root = node2;
            else if (parent.Left == node1)
                parent.Left = node2;
            else
                parent.Right = node2;
        }

        public IEnumerator<T> GetEnumerator()
        {
            Node node = root;
            if (node != null)
                for (Node n = node.Min; n != null; n = n.Next)
                    yield return n.Value;
        }

        IEnumerator IEnumerable.GetEnumerator() { return GetEnumerator(); }
    }

    #endregion
}