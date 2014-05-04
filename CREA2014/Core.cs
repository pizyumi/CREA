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

    #region ソケット通信

    public interface IChannel
    {
        byte[] ReadBytes();
        void WriteBytes(byte[] data);
    }

    public enum ChannelDirection { inbound, outbound }

    public class SocketChannel : IChannel
    {
        public SocketChannel(ISocket _isocket, INetworkStream _ins, RijndaelManaged _rm, ChannelDirection _direction, DateTime _connectionTime)
        {
            if (_isocket.AddressFamily != AddressFamily.InterNetwork && _isocket.AddressFamily != AddressFamily.InterNetworkV6)
                throw new NotSupportedException("not_supported_socket");

            isocket = _isocket;
            ins = _ins;
            rm = _rm;
            direction = _direction;

            zibunIpAddress = ((IPEndPoint)isocket.LocalEndPoint).Address;
            zibunPortNumber = (ushort)((IPEndPoint)isocket.LocalEndPoint).Port;
            aiteIpAddress = ((IPEndPoint)isocket.RemoteEndPoint).Address;
            aitePortNumber = (ushort)((IPEndPoint)isocket.RemoteEndPoint).Port;

            _read = (id) =>
            {
                ReadItem read;
                lock (readsLock)
                {
                    if ((read = reads.Where((a) => a.id == id).FirstOrDefault()) != null)
                    {
                        reads.Remove(read);
                        return read.data;
                    }
                    else
                        reads.Add(read = new ReadItem(id));
                }

                if (read.are.WaitOne(30000))
                    if (read.data != null)
                        return read.data;
                    else
                        throw new ClosedException("channel_already_closed");
                else
                {
                    lock (readsLock)
                        reads.Remove(read);

                    throw new TimeoutException("socket_chennel_timeouted");
                }
            };

            _write = (id, data) =>
            {
                lock (writesLock)
                    writes.Enqueue(new WriteItem(id, data));

                areWrites.Set();
            };

            this.StartTask("socket_channel_write", "socket_channel_write", () =>
            {
                try
                {
                    while (!isWriteEnding)
                    {
                        //念のため30秒経過したら待機を終了する
                        areWrites.WaitOne(30000);

                        while (true)
                        {
                            WriteItem write;
                            lock (writesLock)
                                if (writes.Count == 0)
                                    break;
                                else
                                    write = writes.Dequeue();

                            WriteBytesInner(BitConverter.GetBytes(write.id).Combine(write.data), false);

                            if (write.id == uint.MaxValue)
                            {
                                isWriteEnding = true;
                                break;
                            }
                        }
                    }

                    lock (endLock)
                    {
                        if (isocket.Connected)
                            isocket.Shutdown(SocketShutdown.Send);
                        if (isReadEnded)
                        {
                            rm.Dispose();
                            ins.Close();
                            isocket.Close();
                        }

                        isWriteEnded = true;
                    }

                    Closed(this, EventArgs.Empty);
                }
                catch (Exception ex)
                {
                    this.RaiseError("socket_channel_write".GetLogMessage(), 5, ex);

                    Failed(this, EventArgs.Empty);

                    rm.Dispose();
                    ins.Close();
                    isocket.Close();
                }

                string.Join(":", ChannelAddressText, "write_thread_exit").ConsoleWriteLine();
            });

            this.StartTask("socket_channel_read", "socket_channel_read", () =>
            {
                try
                {
                    while (!isReadEnding)
                    {
                        try
                        {
                            byte[] bytes = ReadBytesInnner(false);

                            uint id = BitConverter.ToUInt32(bytes, 0);
                            byte[] data = bytes.Decompose(4);

                            if (id != 0)
                            {
                                bool isExisted = false;
                                lock (sessionsLock)
                                    if (sessions.Where((a) => a.id == id).FirstOrDefault() != null)
                                        isExisted = true;
                                if (!isExisted)
                                {
                                    SessionChannel sc = new SessionChannel(id, _read, _write);
                                    lock (sessionsLock)
                                        sessions.Add(sc);

                                    this.StartTask("session", "session", () => Sessioned(this, sc));
                                }
                            }

                            if (id == uint.MaxValue)
                            {
                                isReadEnding = true;
                                continue;
                            }

                            lock (readsLock)
                            {
                                ReadItem read = reads.Where((e) => e.id == id).FirstOrDefault();
                                if (read != null)
                                {
                                    reads.Remove(read);

                                    read.SetData(data);
                                    read.are.Set();
                                }
                                else
                                    reads.Add(new ReadItem(id, data));
                            }
                        }
                        catch (Exception ex)
                        {
                            if (ex.InnerException != null && ex.InnerException is SocketException)
                            {
                                SocketException sex = ex.InnerException as SocketException;
                                if (sex.ErrorCode == 10060)
                                {
                                    string.Join(":", ChannelAddressText, "timeout").ConsoleWriteLine();
                                    continue;
                                }
                                else if (sex.ErrorCode == 10054)
                                {
                                    string.Join(":", ChannelAddressText, "connection_reset").ConsoleWriteLine();
                                    break;
                                }
                                else if (sex.ErrorCode == 10053)
                                {
                                    string.Join(":", ChannelAddressText, "connection_aborted").ConsoleWriteLine();
                                    break;
                                }
                            }

                            throw ex;
                        }
                    }

                    lock (readsLock)
                    {
                        ReadItem read;
                        while ((read = reads.FirstOrDefault()) != null)
                        {
                            reads.Remove(read);

                            if (read.are != null)
                                read.are.Set();
                        }
                    }

                    lock (endLock)
                    {
                        if (isocket.Connected)
                            isocket.Shutdown(SocketShutdown.Receive);
                        if (isWriteEnded)
                        {
                            rm.Dispose();
                            ins.Close();
                            isocket.Close();
                        }

                        isReadEnded = true;
                    }

                    Closed(this, EventArgs.Empty);
                }
                catch (Exception ex)
                {
                    this.RaiseError("socket_channel_read".GetLogMessage(), 5, ex);

                    lock (readsLock)
                    {
                        ReadItem read;
                        while ((read = reads.FirstOrDefault()) != null)
                        {
                            reads.Remove(read);

                            if (read.are != null)
                                read.are.Set();
                        }
                    }

                    Failed(this, EventArgs.Empty);

                    rm.Dispose();
                    ins.Close();
                    isocket.Close();
                }

                string.Join(":", ChannelAddressText, "read_thread_exit").ConsoleWriteLine();
            });
        }

        private readonly int maxTimeout = 150;
        private readonly int maxBufferSize = 16384;
        private readonly int minBufferSize = 4096;
        private readonly int bufferSize = 1024;

        private readonly object sessionsLock = new object();
        private readonly List<SessionChannel> sessions = new List<SessionChannel>();
        private readonly object writesLock = new object();
        private readonly Queue<WriteItem> writes = new Queue<WriteItem>();
        private readonly AutoResetEvent areWrites = new AutoResetEvent(false);
        private readonly object readsLock = new object();
        private readonly List<ReadItem> reads = new List<ReadItem>();

        private readonly ISocket isocket;
        private readonly INetworkStream ins;
        private readonly RijndaelManaged rm;
        private readonly Func<uint, byte[]> _read;
        private readonly Action<uint, byte[]> _write;

        private readonly object endLock = new object();
        private bool isWriteEnding;
        private bool isWriteEnded;
        private bool isReadEnding;
        private bool isReadEnded;

        public readonly ChannelDirection direction;
        public readonly DateTime connectionTime;
        public readonly IPAddress zibunIpAddress;
        public readonly ushort zibunPortNumber;
        public readonly IPAddress aiteIpAddress;
        public readonly ushort aitePortNumber;

        private readonly object isClosedLock = new object();
        public bool isClosed { get; private set; }

        public event EventHandler Closed = delegate { };
        public event EventHandler Failed = delegate { };
        public event EventHandler<SessionChannel> Sessioned = delegate { };

        public class TimeoutException : Exception
        {
            public TimeoutException(string _message) : base(_message) { }
        }

        public class ClosedException : Exception
        {
            public ClosedException(string _message) : base(_message) { }
        }

        public class WriteItem
        {
            public WriteItem(byte[] _data) : this(0, _data) { }

            public WriteItem(uint _id, byte[] _data)
            {
                id = _id;
                data = _data;
            }

            public uint id { get; private set; }
            public byte[] data { get; private set; }
        }

        public class ReadItem
        {
            public ReadItem() : this(0) { }

            public ReadItem(uint _id)
            {
                id = _id;
                are = new AutoResetEvent(false);
            }

            public ReadItem(byte[] _data) : this(0, _data) { }

            public ReadItem(uint _id, byte[] _data)
            {
                id = _id;
                data = _data;
            }

            public uint id { get; private set; }
            public AutoResetEvent are { get; private set; }
            public byte[] data { get; private set; }

            public void SetData(byte[] _data)
            {
                data = _data;
            }
        }

        public string ZibunAddressText
        {
            get { return string.Join(":", zibunIpAddress.ToString(), zibunPortNumber.ToString()); }
        }

        public string AiteAddressText
        {
            get { return string.Join(":", aiteIpAddress.ToString(), aitePortNumber.ToString()); }
        }

        public string ChannelAddressText
        {
            get
            {
                if (direction == ChannelDirection.inbound)
                    return string.Join("-->", AiteAddressText, ZibunAddressText);
                else
                    return string.Join("-->", ZibunAddressText, AiteAddressText);
            }
        }

        public int ReceiveTimeout
        {
            get { return isocket.ReceiveTimeout; }
            set
            {
                if (value > maxTimeout)
                    throw new ArgumentOutOfRangeException("max_timeout");

                isocket.ReceiveTimeout = value;
            }
        }

        public int SendTimeout
        {
            get { return isocket.SendTimeout; }
            set
            {
                if (value > maxTimeout)
                    throw new ArgumentOutOfRangeException("max_timeout");

                isocket.SendTimeout = value;
            }
        }

        public int ReceiveBufferSize
        {
            get { return isocket.ReceiveBufferSize; }
            set
            {
                if (value > maxBufferSize)
                    throw new ArgumentOutOfRangeException("max_buffer_size");
                else if (value < minBufferSize)
                    throw new ArgumentOutOfRangeException("min_buffer_size");

                isocket.ReceiveBufferSize = value;
            }
        }

        public int SendBufferSize
        {
            get { return isocket.SendBufferSize; }
            set
            {
                if (value > maxBufferSize)
                    throw new ArgumentOutOfRangeException("max_buffer_size");
                else if (value < minBufferSize)
                    throw new ArgumentOutOfRangeException("min_buffer_size");

                isocket.SendBufferSize = value;
            }
        }

        public TimeSpan Duration
        {
            get { return DateTime.Now - connectionTime; }
        }

        public bool CanEncrypt
        {
            get { return rm != null; }
        }

        private bool isEncrypted = true;
        public bool IsEncrypted
        {
            get
            {
                if (!CanEncrypt)
                    throw new InvalidOperationException("cant_encrypt");
                else
                    return isEncrypted;
            }
            set
            {
                if (!CanEncrypt)
                    throw new InvalidOperationException("cant_encrypt");
                else
                    isEncrypted = value;
            }
        }

        public override string ToString() { return ChannelAddressText; }

        public byte[] ReadBytes()
        {
            if (isClosed)
                throw new ClosedException("channel_already_closed");

            return _read(0);
        }

        public void WriteBytes(byte[] data)
        {
            if (isClosed)
                throw new ClosedException("channel_already_closed");

            _write(0, data);
        }

        public SessionChannel NewSession()
        {
            if (isClosed)
                throw new ClosedException("channel_already_closed");

            SessionChannel session = new SessionChannel(_read, _write);
            session.Closed += (sender, e) =>
            {
                lock (sessionsLock)
                    sessions.Remove(session);
            };

            lock (sessionsLock)
                sessions.Add(session);

            return session;
        }

        public void Close()
        {
            lock (isClosedLock)
            {
                if (isClosed)
                    throw new ClosedException("channel_already_closed");

                isClosed = true;
            }

            _write(uint.MaxValue, new byte[] { });

            Closed(this, EventArgs.Empty);
        }

        private byte[] ReadBytesInnner(bool isCompressed)
        {
            int headerBytesLength = 4 + 32 + 4;

            byte[] headerBytes = new byte[headerBytesLength];
            if (ins.Read(headerBytes, 0, headerBytesLength) != headerBytesLength)
                throw new InvalidDataException("cant_read_header");
            //最初の4バイトは本来のデータの長さ
            int dataLength = BitConverter.ToInt32(headerBytes, 0);
            //最後の4バイトは受信データの長さ
            int readDataLength = BitConverter.ToInt32(headerBytes, headerBytesLength - 4);

            if (dataLength == 0)
                return new byte[] { };

            byte[] readData = null;
            using (MemoryStream ms = new MemoryStream())
            {
                byte[] buffer = new byte[bufferSize];
                while (ms.Length != readDataLength)
                {
                    ms.Write(buffer, 0, ins.Read(buffer, 0, (int)ms.Length + bufferSize > readDataLength ? readDataLength - (int)ms.Length : bufferSize));

                    if (ms.Length > readDataLength)
                        throw new InvalidDataException("read_overed");
                }
                readData = ms.ToArray();
            }

            if (!headerBytes.Skip(4).Take(32).ToArray().BytesEquals(new SHA256Managed().ComputeHash(readData)))
                throw new InvalidDataException("receive_data_corrupt");

            byte[] data = new byte[dataLength];
            using (MemoryStream ms = new MemoryStream(readData))
                if (CanEncrypt && isEncrypted)
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
            byte[] writeData = null;
            if (data.Length == 0)
                writeData = new byte[] { };
            else
                using (MemoryStream ms = new MemoryStream())
                {
                    if (CanEncrypt && isEncrypted)
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

            ins.Write(BitConverter.GetBytes(data.Length).Combine(new SHA256Managed().ComputeHash(writeData), BitConverter.GetBytes(writeData.Length)), 0, 4 + 32 + 4);
            if (data.Length != 0)
                ins.Write(writeData, 0, writeData.Length);
        }
    }

    public class SessionChannel : IChannel
    {
        public SessionChannel(Func<uint, byte[]> __read, Action<uint, byte[]> __write)
        {
            while (id == 0 || id == uint.MaxValue)
                id = BitConverter.ToUInt32(new byte[] { (byte)256.RandomNum(), (byte)256.RandomNum(), (byte)256.RandomNum(), (byte)256.RandomNum() }, 0);

            _read = __read;
            _write = __write;
        }

        public SessionChannel(uint _id, Func<uint, byte[]> __read, Action<uint, byte[]> __write)
        {
            id = _id;
            _read = __read;
            _write = __write;
        }

        private readonly Func<uint, byte[]> _read;
        private readonly Action<uint, byte[]> _write;

        public readonly uint id;

        public event EventHandler Closed = delegate { };

        public byte[] ReadBytes()
        {
            return _read(id);
        }

        public void WriteBytes(byte[] data)
        {
            _write(id, data);
        }

        public void Close()
        {
            Closed(this, EventArgs.Empty);
        }
    }

    public interface INetworkStream
    {
        void Write(byte[] buffer, int offset, int size);
        int Read(byte[] buffer, int offset, int size);
        void Close();
    }

    public class RealNetworkStream : INetworkStream
    {
        public RealNetworkStream(NetworkStream _ns)
        {
            ns = _ns;
        }

        private readonly NetworkStream ns;

        public void Write(byte[] buffer, int offset, int size)
        {
            ns.Write(buffer, offset, size);
        }

        public int Read(byte[] buffer, int offset, int size)
        {
            return ns.Read(buffer, offset, size);
        }

        public void Close()
        {
            ns.Close();
        }
    }

    public interface ISocket
    {
        AddressFamily AddressFamily { get; }
        EndPoint LocalEndPoint { get; }
        EndPoint RemoteEndPoint { get; }
        bool Connected { get; }

        int ReceiveTimeout { get; set; }
        int SendTimeout { get; set; }
        int ReceiveBufferSize { get; set; }
        int SendBufferSize { get; set; }

        void Connect(IPAddress ipAddress, ushort portNumber);
        void Bind(IPEndPoint localEP);
        void Listen(int backlog);
        ISocket Accept();
        void Shutdown(SocketShutdown how);
        void Close();
        void Dispose();
    }

    public class RealSocket : ISocket
    {
        public RealSocket(Socket _socket)
        {
            socket = _socket;
        }

        public Socket socket { get; private set; }

        public AddressFamily AddressFamily
        {
            get { return socket.AddressFamily; }
        }

        public EndPoint LocalEndPoint
        {
            get { return socket.LocalEndPoint; }
        }

        public EndPoint RemoteEndPoint
        {
            get { return socket.RemoteEndPoint; }
        }

        public bool Connected
        {
            get { return socket.Connected; }
        }

        public int ReceiveTimeout
        {
            get { return socket.ReceiveTimeout; }
            set { socket.ReceiveTimeout = value; }
        }

        public int SendTimeout
        {
            get { return socket.SendTimeout; }
            set { socket.SendTimeout = value; }
        }

        public int ReceiveBufferSize
        {
            get { return socket.ReceiveBufferSize; }
            set { socket.ReceiveBufferSize = value; }
        }

        public int SendBufferSize
        {
            get { return socket.SendBufferSize; }
            set { socket.SendBufferSize = value; }
        }

        public void Connect(IPAddress ipAddress, ushort portNumber)
        {
            socket.Connect(ipAddress, (int)portNumber);
        }

        public void Bind(IPEndPoint localEP)
        {
            socket.Bind(localEP);
        }

        public void Listen(int backlog)
        {
            socket.Listen(backlog);
        }

        public ISocket Accept()
        {
            return new RealSocket(socket.Accept());
        }

        public void Shutdown(SocketShutdown how)
        {
            socket.Shutdown(how);
        }

        public void Close()
        {
            socket.Close();
        }

        public void Dispose() { socket.Dispose(); }
    }

    public enum RsaKeySize { rsa1024, rsa2048 }

    public abstract class OutboundChannelBase
    {
        public OutboundChannelBase(IPAddress _ipAddress, ushort _portNumber) : this(_ipAddress, _portNumber, RsaKeySize.rsa2048, null) { }

        public OutboundChannelBase(IPAddress _ipAddress, ushort _portNumber, RsaKeySize _rsaKeySize, string _privateRsaParameters)
        {
            if (_ipAddress.AddressFamily != AddressFamily.InterNetwork && _ipAddress.AddressFamily != AddressFamily.InterNetworkV6)
                throw new NotSupportedException("not_supported_address");

            ipAddress = _ipAddress;
            portNumber = _portNumber;
            rsaKeySize = _rsaKeySize;
            privateRsaParameters = _privateRsaParameters;
        }

        private readonly int receiveTimeout = 30000;
        private readonly int sendTimeout = 30000;
        private readonly int receiveBufferSize = 8192;
        private readonly int sendBufferSize = 8192;

        private readonly IPAddress ipAddress;
        private readonly ushort portNumber;
        private readonly string privateRsaParameters;
        private readonly RsaKeySize rsaKeySize;

        private ISocket isocket;

        private readonly object isConnectionRequestedLock = new object();
        public bool isConnectionRequested { get; private set; }

        public event EventHandler<SocketChannel> Connected = delegate { };
        public event EventHandler Failed = delegate { };

        protected abstract ISocket CreateISocket(AddressFamily _addressFamily);
        protected abstract INetworkStream CreateINetworkStream(ISocket socket);

        public void RequestConnection()
        {
            lock (isConnectionRequestedLock)
            {
                if (isConnectionRequested)
                    throw new InvalidOperationException("already_connection_requested");

                isConnectionRequested = true;
            }

            this.StartTask("outbound_chennel", "outbound_chennel", () =>
            {
                try
                {
                    isocket = CreateISocket(ipAddress.AddressFamily);
                    isocket.ReceiveTimeout = receiveTimeout;
                    isocket.SendTimeout = sendTimeout;
                    isocket.ReceiveBufferSize = receiveBufferSize;
                    isocket.SendBufferSize = sendBufferSize;
                    isocket.Connect(ipAddress, portNumber);

                    DateTime connectionTime = DateTime.Now;
                    INetworkStream ins = CreateINetworkStream(isocket);

                    RijndaelManaged rm = null;
                    if (privateRsaParameters != null)
                    {
                        RSACryptoServiceProvider rsacsp = new RSACryptoServiceProvider();
                        rsacsp.FromXmlString(privateRsaParameters);

                        if ((rsaKeySize == RsaKeySize.rsa1024 && rsacsp.KeySize != 1024) || (rsaKeySize == RsaKeySize.rsa2048 && rsacsp.KeySize != 2048))
                            throw new Exception("rsa_key_size");

                        RSAParameters rsaParameters = rsacsp.ExportParameters(true);
                        byte[] modulus = rsaParameters.Modulus;
                        byte[] exponent = rsaParameters.Exponent;

                        ins.Write(modulus, 0, modulus.Length);
                        ins.Write(exponent, 0, exponent.Length);

                        RSAPKCS1KeyExchangeDeformatter rsapkcs1ked = new RSAPKCS1KeyExchangeDeformatter(rsacsp);

                        byte[] encryptedKey = rsaKeySize == RsaKeySize.rsa1024 ? new byte[128] : new byte[256];
                        byte[] encryptedIv = rsaKeySize == RsaKeySize.rsa1024 ? new byte[128] : new byte[256];

                        ins.Read(encryptedKey, 0, encryptedKey.Length);
                        ins.Read(encryptedIv, 0, encryptedIv.Length);

                        rm = new RijndaelManaged();
                        rm.Padding = PaddingMode.Zeros;
                        rm.Key = rsapkcs1ked.DecryptKeyExchange(encryptedKey);
                        rm.IV = rsapkcs1ked.DecryptKeyExchange(encryptedIv);
                    }

                    Connected(this, new SocketChannel(isocket, ins, rm, ChannelDirection.outbound, connectionTime));
                }
                catch (Exception ex)
                {
                    this.RaiseError("outbound_chennel".GetLogMessage(), 5, ex);

                    if (isocket.Connected)
                        isocket.Shutdown(SocketShutdown.Both);
                    isocket.Close();

                    Failed(this, EventArgs.Empty);
                }
            });
        }
    }

    public class RealOutboundChannel : OutboundChannelBase
    {
        public RealOutboundChannel(IPAddress _ipAddress, ushort _portNumber) : this(_ipAddress, _portNumber, RsaKeySize.rsa2048, null) { }

        public RealOutboundChannel(IPAddress _ipAddress, ushort _portNumber, RsaKeySize _rsaKeySize, string _privateRsaParameters) : base(_ipAddress, _portNumber, _rsaKeySize, _privateRsaParameters) { }

        protected override ISocket CreateISocket(AddressFamily _addressFamily)
        {
            return new RealSocket(new Socket(_addressFamily, SocketType.Stream, ProtocolType.Tcp));
        }

        protected override INetworkStream CreateINetworkStream(ISocket socket)
        {
            return new RealNetworkStream(new NetworkStream(((RealSocket)socket).socket));
        }
    }

    public abstract class InboundChannelsBase
    {
        public InboundChannelsBase(ushort _portNumber, int _backlog) : this(_portNumber, false, RsaKeySize.rsa2048, _backlog) { }

        public InboundChannelsBase(ushort _portNumber, RsaKeySize _rsaKeySize, int _backlog) : this(_portNumber, true, _rsaKeySize, _backlog) { }

        private InboundChannelsBase(ushort _portNumber, bool _isEncrypted, RsaKeySize _rsaKeySize, int _backlog)
        {
            portNumber = _portNumber;
            isEncrypted = _isEncrypted;
            rsaKeySize = _rsaKeySize;
            backlog = _backlog;
        }

        private readonly int receiveTimeout = 30000;
        private readonly int sendTimeout = 30000;
        private readonly int receiveBufferSize = 8192;
        private readonly int sendBufferSize = 8192;

        private readonly ushort portNumber;
        private readonly bool isEncrypted;
        private readonly RsaKeySize rsaKeySize;
        private readonly int backlog;

        private ISocket isocket;

        private readonly object isAcceptanceStartRequestedLock = new object();
        public bool isAcceptanceStratRequested { get; private set; }
        private readonly object isAcceptanceEndedLock = new object();
        public bool isAcceptanceEnded { get; private set; }

        public event EventHandler Failed = delegate { };
        public event EventHandler<SocketChannel> Accepted = delegate { };
        public event EventHandler<IPAddress> AcceptanceFailed = delegate { };

        protected abstract ISocket CreateISocket(AddressFamily _addressFamily);
        protected abstract INetworkStream CreateINetworkStream(ISocket socket);

        public void RequestAcceptanceStart()
        {
            lock (isAcceptanceStartRequestedLock)
            {
                if (isAcceptanceStratRequested)
                    throw new InvalidOperationException("already_acceptance_requested");

                isAcceptanceStratRequested = true;
            }

            this.StartTask("inbound_channels", "inbound_channels", () =>
            {
                try
                {
                    isocket = CreateISocket(AddressFamily.InterNetwork);
                    isocket.Bind(new IPEndPoint(IPAddress.Any, portNumber));
                    isocket.Listen(backlog);

                    while (true)
                    {
                        ISocket isocket2 = isocket.Accept();

                        DateTime connectedTime = DateTime.Now;

                        this.StartTask("inbound_channel", "inbound_channel", () =>
                        {
                            try
                            {
                                isocket2.ReceiveTimeout = receiveTimeout;
                                isocket2.SendTimeout = sendTimeout;
                                isocket2.ReceiveBufferSize = receiveBufferSize;
                                isocket2.SendBufferSize = sendBufferSize;

                                INetworkStream ins = CreateINetworkStream(isocket2);

                                RijndaelManaged rm = null;
                                if (isEncrypted)
                                {
                                    byte[] modulus = rsaKeySize == RsaKeySize.rsa1024 ? new byte[128] : new byte[256];
                                    byte[] exponent = new byte[3];

                                    ins.Read(modulus, 0, modulus.Length);
                                    ins.Read(exponent, 0, exponent.Length);

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

                                    ins.Write(encryptedKey, 0, encryptedKey.GetLength(0));
                                    ins.Write(encryptedIv, 0, encryptedIv.GetLength(0));
                                }

                                Accepted(this, new SocketChannel(isocket2, ins, rm, ChannelDirection.inbound, connectedTime));
                            }
                            catch (Exception ex)
                            {
                                this.RaiseError("inbound_channel".GetLogMessage(), 5, ex);

                                if (isocket2.Connected)
                                    isocket2.Shutdown(SocketShutdown.Both);
                                isocket2.Close();

                                AcceptanceFailed(this, ((IPEndPoint)isocket.RemoteEndPoint).Address);
                            }
                        });
                    }
                }
                catch (Exception ex)
                {
                    this.RaiseError("inbound_channels".GetLogMessage(), 5, ex);

                    EndAcceptance();

                    Failed(this, EventArgs.Empty);
                }
            });
        }

        public void EndAcceptance()
        {
            lock (isAcceptanceEndedLock)
            {
                if (!isAcceptanceStratRequested)
                    throw new InvalidOperationException("yet_acceptance_requested");
                if (isAcceptanceEnded)
                    throw new InvalidOperationException("already_acceptance_ended");

                isAcceptanceEnded = true;
            }

            isocket.Close();
        }
    }

    public class RealInboundChennel : InboundChannelsBase
    {
        public RealInboundChennel(ushort _portNumber, int _backlog) : base(_portNumber, _backlog) { }

        public RealInboundChennel(ushort _portNumber, RsaKeySize _rsaKeySize, int _backlog) : base(_portNumber, _rsaKeySize, _backlog) { }

        protected override ISocket CreateISocket(AddressFamily _addressFamily)
        {
            return new RealSocket(new Socket(_addressFamily, SocketType.Stream, ProtocolType.Tcp));
        }

        protected override INetworkStream CreateINetworkStream(ISocket socket)
        {
            return new RealNetworkStream(new NetworkStream(((RealSocket)socket).socket));
        }
    }

    public class ConnectionData : INTERNALDATA
    {
        public ConnectionData(SocketChannel _sc) : this(_sc.aiteIpAddress, _sc.aitePortNumber, _sc.zibunPortNumber, _sc.direction, _sc.connectionTime) { }

        public ConnectionData(IPAddress _ipAddress, ushort _portNumber, ushort _myPortNumber, ChannelDirection _direction, DateTime _connectionTime)
        {
            ipAddress = _ipAddress;
            portNumber = _portNumber;
            myPortNumber = _myPortNumber;
            direction = _direction;
            connectionTime = _connectionTime;
        }

        public readonly IPAddress ipAddress;
        public readonly ushort portNumber;
        public readonly ushort myPortNumber;
        public readonly ChannelDirection direction;
        public readonly DateTime connectionTime;

        public TimeSpan Duration
        {
            get { return DateTime.Now - connectionTime; }
        }
    }

    public class ConnectionHistory
    {
        public ConnectionHistory(IPAddress _ipAddress)
        {
            ipAddress = _ipAddress;
        }

        public readonly IPAddress ipAddress;

        public int success { get; private set; }
        public int failure { get; private set; }

        private readonly object failureTimeLock = new object();
        private readonly List<DateTime> failureTime = new List<DateTime>();
        public DateTime[] FailureTime
        {
            get { return failureTime.ToArray(); }
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

            lock (failureTimeLock)
                failureTime.Clear();
        }

        public void IncrementFailure()
        {
            failure++;

            lock (failureTimeLock)
            {
                if (failureTime.Count >= 2)
                    while (failureTime.Count >= 2)
                        failureTime.RemoveAt(0);

                failureTime.Add(DateTime.Now);
            }
        }
    }

    #endregion

    #region P2Pネットワーク

    public abstract class P2PNODE2
    {
        public P2PNODE2() : this(0) { }

        public P2PNODE2(ushort _portNumber)
        {
            portNumber = _portNumber;
        }

        public IPAddress ipAddress { get; private set; }
        public ushort portNumber { get; private set; }
        public FirstNodeInformation firstNodeInfo { get; private set; }
        private readonly object isStartedLock = new object();
        public bool isStarted { get; private set; }
        public bool isStartCompleted { get; private set; }

        protected string privateRsaParameters { get; private set; }
        protected FirstNodeInformation[] firstNodeInfos { get; private set; }

        private readonly object connectionsLock = new object();
        private readonly List<ConnectionData> connections = new List<ConnectionData>();
        public ConnectionData[] Connections
        {
            get { return connections.ToArray(); }
        }

        private readonly object connectionHistoriesLock = new object();
        private readonly List<ConnectionHistory> connectionHistories = new List<ConnectionHistory>();
        public ConnectionHistory[] ConnectionHistories
        {
            get { return connectionHistories.ToArray(); }
        }

        public event EventHandler StartCompleted = delegate { };
        public event EventHandler ConnectionAdded = delegate { };
        public event EventHandler ConnectionRemoved = delegate { };

        protected abstract Network Network { get; }

        protected abstract IPAddress GetIpAddress();
        protected abstract string GetPrivateRsaParameters();
        protected abstract void NotifyFirstNodeInfo();
        protected abstract void CreateNodeInfo();
        protected abstract FirstNodeInformation[] GetFirstNodeInfos();
        protected abstract void KeepConnections();
        protected abstract void OnAccepted(SocketChannel sc);

        public bool IsPort0
        {
            get { return portNumber == 0; }
        }

        public bool IsServer
        {
            get { return !IsPort0 && ipAddress != null; }
        }

        public void Start()
        {
            lock (isStartedLock)
            {
                if (isStarted)
                    throw new InvalidOperationException("node_already_started");

                isStarted = true;
            }

            this.StartTask("node_start", "node_start", () =>
            {
                if (IsPort0)
                    this.RaiseNotification("port0".GetLogMessage(), 5);
                else
                {
                    //IPアドレスの取得には時間が掛かる可能性がある
                    ipAddress = GetIpAddress();

                    if ((privateRsaParameters = GetPrivateRsaParameters()) == null)
                        throw new CryptographicException("cant_create_key_pair");

                    if (IsServer)
                    {
                        RealInboundChennel ric = new RealInboundChennel(portNumber, RsaKeySize.rsa2048, 100);
                        ric.Accepted += (sender, e) =>
                        {
                            try
                            {
                                ConnectionData cd = new ConnectionData(e);
                                AddConnection(cd);

                                e.Closed += (sender2, e2) =>
                                {
                                    RemoveConnection(cd);
                                    RegisterResult(e.aiteIpAddress, true);
                                };
                                e.Failed += (sender2, e2) =>
                                {
                                    RemoveConnection(cd);
                                    RegisterResult(e.aiteIpAddress, false);
                                };

                                OnAccepted(e);
                            }
                            catch (Exception ex)
                            {
                                this.RaiseError("ric".GetLogMessage(), 5, ex);

                                e.Close();
                            }
                        };
                        ric.AcceptanceFailed += (sender, e) => RegisterResult(e, false);
                        ric.Failed += (sender, e) => { };
                        ric.RequestAcceptanceStart();

                        this.RaiseNotification("server_started".GetLogMessage(ipAddress.ToString(), portNumber.ToString()), 5);

                        firstNodeInfo = new FirstNodeInformation(ipAddress, portNumber, Network);

                        NotifyFirstNodeInfo();

                        CreateNodeInfo();
                    }
                }

                if (firstNodeInfo == null)
                    firstNodeInfos = GetFirstNodeInfos();
                else
                    firstNodeInfos = GetFirstNodeInfos().Where((a) => !a.Equals(firstNodeInfo)).ToArray();

                KeepConnections();

                isStartCompleted = true;

                StartCompleted(this, EventArgs.Empty);
            });
        }

        protected SocketChannel Connect(IPAddress aiteIpAddress, ushort aitePortNumber)
        {
            SocketChannel sc = null;
            AutoResetEvent are = new AutoResetEvent(false);

            RealOutboundChannel roc = new RealOutboundChannel(aiteIpAddress, aitePortNumber, RsaKeySize.rsa2048, privateRsaParameters);
            roc.Connected += (sender, e) =>
            {
                try
                {
                    ConnectionData cd = new ConnectionData(e);
                    AddConnection(cd);

                    e.Closed += (sender2, e2) =>
                    {
                        RemoveConnection(cd);
                        RegisterResult(e.aiteIpAddress, true);
                    };
                    e.Failed += (sender2, e2) =>
                    {
                        RemoveConnection(cd);
                        RegisterResult(e.aiteIpAddress, false);
                    };

                    sc = e;
                    are.Set();
                }
                catch (Exception ex)
                {
                    this.RaiseError("roc".GetLogMessage(), 5, ex);

                    e.Close();
                }
            };
            roc.Failed += (sender, e) => RegisterResult(aiteIpAddress, false);
            roc.RequestConnection();

            if (are.WaitOne(30000))
                return sc;
            else
                throw new Exception("cant_connect");
        }

        private void AddConnection(ConnectionData cd)
        {
            lock (connectionsLock)
            {
                if (connections.Contains(cd))
                    throw new InvalidOperationException("exist_connection");

                this.ExecuteBeforeEvent(() => connections.Add(cd), ConnectionAdded);
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
                if ((connectionHistory = connectionHistories.Where((e) => e.ipAddress.Equals(ipAddress)).FirstOrDefault()) == null)
                    connectionHistories.Add(connectionHistory = new ConnectionHistory(ipAddress));

            if (isSucceeded)
                connectionHistory.IncrementSuccess();
            else
                connectionHistory.IncrementFailure();
        }
    }

    #endregion

    #region CREAネットワーク

    public enum Network { localtest = 0, global = 1 }

    public enum MessageName
    {
        inv = 10,
        getdata = 11,
        tx = 12,
        block = 13,
        notfound = 14,

        PingReq = 100,
        PingRes = 101,
        StoreReq = 102,
        FindNodesReq = 103,
        NeighborNodes = 104,
        FindvalueReq = 105,
        Value = 106,
        GetIdsAndValuesReq = 107,
        IdsAndValues = 108,
    }

    public class Message : SHAREDDATA
    {
        public Message() : this(null) { }

        public Message(MessageBase _messageBase)
            : base(0)
        {
            messageBase = _messageBase;
        }

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

        protected override Func<ReaderWriter, IEnumerable<MainDataInfomation>> StreamInfo
        {
            get { return (msrw) => StreamInfoInner(msrw); }
        }
        private IEnumerable<MainDataInfomation> StreamInfoInner(ReaderWriter msrw)
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

        public override bool IsVersioned
        {
            get { return true; }
        }

        public override bool IsCorruptionChecked
        {
            get
            {
                if (Version <= 0)
                    return false;
                else
                    throw new NotSupportedException("message_check");
            }
        }
    }

    public abstract class MessageBase : SHAREDDATA
    {
        public MessageBase() : base(null) { }

        public MessageBase(int? _version) : base(_version) { }

        public Func<ReaderWriter, IEnumerable<MainDataInfomation>> PublicStreamInfo
        {
            get { return StreamInfo; }
        }
    }

    public abstract class MessageSha256Hash : MessageBase
    {
        public MessageSha256Hash(Sha256Hash _hash) : this(null, _hash) { }

        public MessageSha256Hash(int? _version, Sha256Hash _hash)
            : base(_version)
        {
            hash = _hash;
        }

        public Sha256Hash hash { get; private set; }

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
        public Inv() : this(new Sha256Hash()) { }

        public Inv(Sha256Hash _hash) : base(0, _hash) { }

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
                    return false;
                else
                    throw new NotSupportedException("inv_check");
            }
        }
    }

    public class Getdata : MessageSha256Hash
    {
        public Getdata() : this(new Sha256Hash()) { }

        public Getdata(Sha256Hash _hash) : base(0, _hash) { }

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
                    return false;
                else
                    throw new NotSupportedException("getdata_check");
            }
        }
    }

    //試験用
    public class TxTest : MessageBase
    {
        public TxTest()
            : base(0)
        {
            data = new byte[1024];
            for (int i = 0; i < data.Length; i++)
                data[i] = (byte)256.RandomNum();
        }

        public byte[] data { get; private set; }

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
                    return false;
                else
                    throw new NotSupportedException("tx_test_check");
            }
        }
    }

    public class Header : SHAREDDATA
    {
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

        public NodeInformation nodeInfo { get; private set; }
        public int creaVersion { get; private set; }
        public int protocolVersion { get; private set; }
        public string client { get; private set; }
        public bool isTemporary { get; private set; }

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
                    return false;
                else
                    throw new NotSupportedException("header_corruption_checked");
            }
        }
    }

    public class HeaderResponse : SHAREDDATA
    {
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

        public NodeInformation nodeInfo { get; private set; }
        public bool isSameNetwork { get; private set; }
        public bool isAlreadyConnected { get; private set; }
        public NodeInformation correctNodeInfo { get; private set; }
        public bool isOldCreaVersion { get; private set; }
        public int protocolVersion { get; private set; }
        public string client { get; private set; }

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
                    throw new NotSupportedException("header_res_stream_info");
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
                    return false;
                else
                    throw new NotSupportedException("header_res_corruption_checked");
            }
        }
    }

    public class PingReq : MessageBase
    {
        public PingReq() : base(0) { }

        protected override Func<ReaderWriter, IEnumerable<MainDataInfomation>> StreamInfo
        {
            get
            {
                if (Version == 0)
                    return (msrw) => new MainDataInfomation[] { };
                else
                    throw new NotSupportedException("ping_req_main_data_info");
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
                    return false;
                else
                    throw new NotSupportedException("ping_req_check");
            }
        }
    }

    public class PingRes : MessageBase
    {
        public PingRes() : base(0) { }

        protected override Func<ReaderWriter, IEnumerable<MainDataInfomation>> StreamInfo
        {
            get
            {
                if (Version == 0)
                    return (msrw) => new MainDataInfomation[] { };
                else
                    throw new NotSupportedException("ping_res_main_data_info");
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
                    return false;
                else
                    throw new NotSupportedException("ping_res_check");
            }
        }
    }

    public class StoreReq : MessageBase
    {
        public StoreReq(Sha256Hash _id, byte[] _data)
            : base(0)
        {
            id = _id;
            data = _data;
        }

        public Sha256Hash id { get; private set; }
        public byte[] data { get; private set; }

        protected override Func<ReaderWriter, IEnumerable<MainDataInfomation>> StreamInfo
        {
            get
            {
                if (Version == 0)
                    return (msrw) => new MainDataInfomation[]{
                        new MainDataInfomation(typeof(Sha256Hash), null, () => id, (o) => id = (Sha256Hash)o),
                        new MainDataInfomation(typeof(byte[]), null, () => data, (o) => data = (byte[])o),
                    };
                else
                    throw new NotSupportedException("store_req_main_data_info");
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
                    return false;
                else
                    throw new NotSupportedException("store_req_check");
            }
        }
    }

    public class FindNodesReq : MessageBase
    {
        public FindNodesReq(Sha256Hash _id)
            : base(0)
        {
            id = _id;
        }

        public Sha256Hash id { get; private set; }

        protected override Func<ReaderWriter, IEnumerable<MainDataInfomation>> StreamInfo
        {
            get
            {
                if (Version == 0)
                    return (msrw) => new MainDataInfomation[]{
                        new MainDataInfomation(typeof(Sha256Hash), null, () => id, (o) => id = (Sha256Hash)o),
                    };
                else
                    throw new NotSupportedException("find_nodes_req_main_data_info");
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
                    return false;
                else
                    throw new NotSupportedException("find_nodes_req_check");
            }
        }
    }

    public class NeighborNodes : MessageBase
    {
        public NeighborNodes(NodeInformation[] _nodeInfos)
            : base(0)
        {
            nodeInfos = _nodeInfos;
        }

        private NodeInformation[] nodeInfos;
        public NodeInformation[] NodeInfos
        {
            get { return nodeInfos.ToArray(); }
        }

        protected override Func<ReaderWriter, IEnumerable<MainDataInfomation>> StreamInfo
        {
            get
            {
                if (Version == 0)
                    return (msrw) => new MainDataInfomation[]{
                        new MainDataInfomation(typeof(NodeInformation[]), 0, null, () => nodeInfos, (o) => nodeInfos = (NodeInformation[])o),
                    };
                else
                    throw new NotSupportedException("neighbor_nodes_main_data_info");
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
                    return false;
                else
                    throw new NotSupportedException("neighbor_nodes_req_check");
            }
        }
    }

    public class FindValueReq : MessageBase
    {
        public FindValueReq(Sha256Hash _id)
            : base(0)
        {
            id = _id;
        }

        public Sha256Hash id { get; private set; }

        protected override Func<ReaderWriter, IEnumerable<MainDataInfomation>> StreamInfo
        {
            get
            {
                if (Version == 0)
                    return (msrw) => new MainDataInfomation[] {
                        new MainDataInfomation(typeof(Sha256Hash), null, () => id, (o) => id = (Sha256Hash)o),
                    };
                else
                    throw new NotSupportedException("find_value_req_main_data_info");
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
                    return false;
                else
                    throw new NotSupportedException("find_value_req_check");
            }
        }
    }

    public class Value : MessageBase
    {
        public Value(byte[] _data)
            : base(0)
        {
            data = _data;
        }

        public byte[] data { get; private set; }

        protected override Func<ReaderWriter, IEnumerable<MainDataInfomation>> StreamInfo
        {
            get
            {
                if (Version == 0)
                    return (msrw) => new MainDataInfomation[] {
                        new MainDataInfomation(typeof(byte[]), null, () => data, (o) => data = (byte[])o),
                    };
                else
                    throw new NotSupportedException("value_main_data_info");
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
                    return false;
                else
                    throw new NotSupportedException("value_req_check");
            }
        }
    }

    public class GetIdsAndValuesReq : MessageBase
    {
        public GetIdsAndValuesReq() : base(0) { }

        protected override Func<ReaderWriter, IEnumerable<MainDataInfomation>> StreamInfo
        {
            get
            {
                if (Version == 0)
                    return (msrw) => new MainDataInfomation[] { };
                else
                    throw new NotSupportedException("get_ids_and_values_main_data_info");
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
                    return false;
                else
                    throw new NotSupportedException("get_ids_and_values_check");
            }
        }
    }

    public class IdsAndValues : MessageBase
    {
        public IdsAndValues() : base(0) { }

        private Sha256Hash[] ids;
        public Sha256Hash[] Ids
        {
            get { return ids.ToArray(); }
        }

        private byte[][] datas;
        public byte[][] Datas
        {
            get { return datas.ToArray(); }
        }

        protected override Func<ReaderWriter, IEnumerable<MainDataInfomation>> StreamInfo
        {
            get
            {
                if (Version == 0)
                    return (msrw) => new MainDataInfomation[] {
                        new MainDataInfomation(typeof(Sha256Hash[]), null, null, () => ids, (o) => ids = (Sha256Hash[])o),
                        new MainDataInfomation(typeof(byte[][]), null, () => datas, (o) => datas = (byte[][])o),
                    };
                else
                    throw new NotSupportedException("ids_and_value_main_data_info");
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
                    return false;
                else
                    throw new NotSupportedException("ids_and_value_check");
            }
        }
    }

    public abstract class CREANODEBASE2 : P2PNODE2
    {
        public CREANODEBASE2(ushort _portNumber, int _creaVersion, string _appnameWithVersion)
            : base(_portNumber)
        {
            creaVersion = _creaVersion;
            appnameWithVersion = _appnameWithVersion;
        }

        private readonly int protocolVersion = 0;

        private readonly int creaVersion;
        private readonly string appnameWithVersion;

        public NodeInformation nodeInfo { get; private set; }

        protected abstract bool IsContinue { get; }

        protected abstract bool IsAlreadyConnected(NodeInformation nodeInfo);
        protected abstract void UpdateNodeState(NodeInformation nodeInfo, bool isSucceeded);
        protected abstract bool IsListenerCanContinue(NodeInformation nodeInfo);
        protected abstract bool IsWantToContinue(NodeInformation nodeInfo);
        protected abstract bool IsClientCanContinue(NodeInformation nodeInfo);
        protected abstract void InboundProtocol(IChannel sc, Action<string> _ConsoleWriteLine);
        protected abstract void OutboundProtocol(MessageBase[] messages, IChannel sc, Action<string> _ConsoleWriteLine);
        protected abstract void InboundContinue(NodeInformation nodeInfo, SocketChannel sc, Action<string> _ConsoleWriteLine);
        protected abstract void OutboundContinue(NodeInformation nodeInfo, SocketChannel sc, Action<string> _ConsoleWriteLine);
        protected abstract void Request(NodeInformation nodeinfo, params MessageBase[] messages);
        protected abstract void Diffuse(params MessageBase[] messages);

        protected override void CreateNodeInfo()
        {
            using (RSACryptoServiceProvider rsacsp = new RSACryptoServiceProvider(2048))
            {
                rsacsp.FromXmlString(privateRsaParameters);
                nodeInfo = new NodeInformation(ipAddress, portNumber, Network, rsacsp.ToXmlString(false));
            }
        }

        protected override void OnAccepted(SocketChannel sc)
        {
            Header header = SHAREDDATA.FromBinary<Header>(sc.ReadBytes());

            NodeInformation aiteNodeInfo = null;
            if (!header.nodeInfo.IpAddress.Equals(sc.aiteIpAddress))
            {
                this.RaiseNotification("aite_wrong_node_info".GetLogMessage(sc.aiteIpAddress.ToString(), header.nodeInfo.Port.ToString()), 5);

                aiteNodeInfo = new NodeInformation(sc.aiteIpAddress, header.nodeInfo.Port, header.nodeInfo.Network, header.nodeInfo.PublicRSAParameters);
            }

            HeaderResponse headerResponse = new HeaderResponse(nodeInfo, header.nodeInfo.Network == Network, IsAlreadyConnected(header.nodeInfo), aiteNodeInfo, header.creaVersion < creaVersion, protocolVersion, appnameWithVersion);

            if (aiteNodeInfo == null)
                aiteNodeInfo = header.nodeInfo;

            if ((!headerResponse.isSameNetwork).RaiseNotification(GetType(), "aite_wrong_network".GetLogMessage(aiteNodeInfo.IpAddress.ToString(), aiteNodeInfo.Port.ToString()), 5))
            {
                sc.Close();

                return;
            }
            if (headerResponse.isAlreadyConnected.RaiseNotification(GetType(), "aite_already_connected".GetLogMessage(aiteNodeInfo.IpAddress.ToString(), aiteNodeInfo.Port.ToString()), 5))
            {
                sc.Close();

                return;
            }
            //<未実装>不良ノードは拒否する？

            UpdateNodeState(aiteNodeInfo, true);

            if (header.creaVersion > creaVersion)
            {
                //相手のクライアントバージョンの方が大きい場合の処理
                //<未実装>使用者への通知
                //<未実装>自動ダウンロード、バージョンアップなど
                //ここで直接行うべきではなく、イベントを発令するべきだろう
            }

            sc.WriteBytes(headerResponse.ToBinary());

            int sessionProtocolVersion = Math.Min(header.protocolVersion, protocolVersion);
            if (sessionProtocolVersion == 0)
            {
                Action<string> _ConsoleWriteLine = (text) => string.Join(" ", sc.ChannelAddressText, text).ConsoleWriteLine();

                if (header.isTemporary)
                {
                    InboundProtocol(sc, _ConsoleWriteLine);

                    if (!IsContinue)
                        sc.Close();
                    else
                    {
                        bool isWantToContinue = IsWantToContinue(header.nodeInfo);
                        sc.WriteBytes(BitConverter.GetBytes(isWantToContinue));
                        if (isWantToContinue)
                        {
                            bool isClientCanContinue = BitConverter.ToBoolean(sc.ReadBytes(), 0);
                            if (isClientCanContinue)
                                InboundContinue(aiteNodeInfo, sc, _ConsoleWriteLine);
                        }
                    }
                }
                else if (IsContinue)
                {
                    bool isCanListenerContinue = IsListenerCanContinue(header.nodeInfo);
                    sc.WriteBytes(BitConverter.GetBytes(isCanListenerContinue));
                    if (isCanListenerContinue)
                        InboundContinue(aiteNodeInfo, sc, _ConsoleWriteLine);
                }
            }
            else
                throw new NotSupportedException("not_supported_protocol_ver");
        }

        protected void Connect(IPAddress aiteIpAddress, ushort aitePortNumber, bool isTemporary, Action _Continued, params MessageBase[] messages)
        {
            SocketChannel sc = Connect(aiteIpAddress, aitePortNumber);

            sc.WriteBytes(new Header(nodeInfo, creaVersion, protocolVersion, appnameWithVersion, isTemporary).ToBinary());
            HeaderResponse headerResponse = SHAREDDATA.FromBinary<HeaderResponse>(sc.ReadBytes());

            if ((!headerResponse.isSameNetwork).RaiseNotification(GetType(), "wrong_network".GetLogMessage(aiteIpAddress.ToString(), aitePortNumber.ToString()), 5))
            {
                sc.Close();

                return;
            }
            if (headerResponse.isAlreadyConnected.RaiseNotification(GetType(), "already_connected".GetLogMessage(aiteIpAddress.ToString(), aitePortNumber.ToString()), 5))
            {
                sc.Close();

                return;
            }
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
                Action<string> _ConsoleWriteLine = (text) => string.Join(" ", sc.ChannelAddressText, text).ConsoleWriteLine();

                if (isTemporary)
                {
                    OutboundProtocol(messages, sc, _ConsoleWriteLine);

                    if (!IsContinue)
                        sc.Close();
                    else
                    {
                        bool isWantToContinue = BitConverter.ToBoolean(sc.ReadBytes(), 0);
                        if (isWantToContinue)
                        {
                            bool isClientCanContinue = IsClientCanContinue(headerResponse.nodeInfo);
                            sc.WriteBytes(BitConverter.GetBytes(isClientCanContinue));
                            if (isClientCanContinue)
                            {
                                _Continued();

                                OutboundContinue(headerResponse.nodeInfo, sc, _ConsoleWriteLine);
                            }
                        }
                    }
                }
                else if (IsContinue)
                {
                    bool isListenerCanContinue = BitConverter.ToBoolean(sc.ReadBytes(), 0);
                    if (isListenerCanContinue)
                    {
                        _Continued();

                        OutboundContinue(headerResponse.nodeInfo, sc, _ConsoleWriteLine);
                    }
                }
            }
            else
                throw new NotSupportedException("not_supported_protocol_ver");
        }
    }

    #region 試験用

    public abstract class CreaNodeLocalTest2 : CREANODEBASE2
    {
        public CreaNodeLocalTest2(ushort _portNumber, int _creaVersion, string _appnameWithVersion) : base(_portNumber, _creaVersion, _appnameWithVersion) { }

        private static readonly object fnisLock = new object();
        private static readonly List<FirstNodeInformation> fnis = new List<FirstNodeInformation>();
        private static readonly string testPrivateRsaParameters;

        //試験用
        private readonly Dictionary<Sha256Hash, byte[]> txtests = new Dictionary<Sha256Hash, byte[]>();
        private readonly object txtestsLock = new object();

        public event EventHandler<NodeInformation> TxtestReceived = delegate { };
        public event EventHandler<NodeInformation> TxtestAlreadyExisted = delegate { };

        static CreaNodeLocalTest2()
        {
            using (RSACryptoServiceProvider rsacsp = new RSACryptoServiceProvider(2048))
                testPrivateRsaParameters = rsacsp.ToXmlString(true);
        }

        protected override Network Network
        {
            get { return Network.localtest; }
        }

        protected override IPAddress GetIpAddress()
        {
            return IPAddress.Loopback;
        }

        protected override string GetPrivateRsaParameters()
        {
            return testPrivateRsaParameters;
        }

        protected override void NotifyFirstNodeInfo()
        {
            lock (fnisLock)
            {
                while (fnis.Count >= 20)
                    fnis.RemoveAt(fnis.Count - 1);
                fnis.Insert(0, firstNodeInfo);
            }
        }

        protected override FirstNodeInformation[] GetFirstNodeInfos()
        {
            lock (fnisLock)
                return fnis.ToArray();
        }

        protected override void InboundProtocol(IChannel sc, Action<string> _ConsoleWriteLine)
        {
            Message message = SHAREDDATA.FromBinary<Message>(sc.ReadBytes());
            if (message.name == MessageName.inv)
            {
                Inv inv = message.messageBase as Inv;
                bool isNew = !txtests.Keys.Contains(inv.hash);
                sc.WriteBytes(BitConverter.GetBytes(isNew));
                if (isNew)
                {
                    TxTest txtest = SHAREDDATA.FromBinary<TxTest>(sc.ReadBytes());
                    lock (txtestsLock)
                        if (!txtests.Keys.Contains(inv.hash))
                            txtests.Add(inv.hash, txtest.data);
                        else
                            return;

                    _ConsoleWriteLine("txtest受信");

                    TxtestReceived(this, nodeInfo);

                    this.StartTask(string.Empty, string.Empty, () => DiffuseInv(txtest, inv));
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

        protected override void OutboundProtocol(MessageBase[] messages, IChannel sc, Action<string> _ConsoleWriteLine)
        {
            Message message = new Message(messages[0]);

            sc.WriteBytes(message.ToBinary());
            if (message.name == MessageName.inv)
            {
                bool isNew = BitConverter.ToBoolean(sc.ReadBytes(), 0);
                if (isNew)
                    sc.WriteBytes(messages[1].ToBinary());
            }
            else
                throw new NotSupportedException("protocol_not_supported");
        }

        //試験用
        public void DiffuseInv(TxTest txtest, Inv inv)
        {
            if (txtest == null && inv == null)
            {
                txtest = new TxTest();
                inv = new Inv(new Sha256Hash(txtest.data.ComputeSha256()));

                lock (txtestsLock)
                    txtests.Add(inv.hash, txtest.data);

                (string.Join(":", ipAddress.ToString(), portNumber.ToString()) + " txtest作成").ConsoleWriteLine();
            }

            Diffuse(inv, txtest);
        }
    }

    public class CreaNodeLocalTestNotContinue2 : CreaNodeLocalTest2
    {
        public CreaNodeLocalTestNotContinue2(ushort _portNumber, int _creaVersion, string _appnameWithVersion) : base(_portNumber, _creaVersion, _appnameWithVersion) { }

        protected override bool IsContinue
        {
            get { return false; }
        }

        protected override bool IsAlreadyConnected(NodeInformation nodeInfo) { return false; }

        protected override void UpdateNodeState(NodeInformation nodeInfo, bool isSucceeded) { }

        protected override bool IsListenerCanContinue(NodeInformation nodeInfo) { return false; }

        protected override bool IsWantToContinue(NodeInformation nodeInfo) { return false; }

        protected override bool IsClientCanContinue(NodeInformation nodeInfo) { return false; }

        protected override void InboundContinue(NodeInformation nodeInfo, SocketChannel sc, Action<string> _ConsoleWriteLine) { }

        protected override void OutboundContinue(NodeInformation nodeInfo, SocketChannel sc, Action<string> _ConsoleWriteLine) { }

        protected override void Request(NodeInformation nodeinfo, params MessageBase[] messages)
        {
            Connect(nodeinfo.IpAddress, nodeinfo.Port, true, () => { }, messages);
        }

        protected override void Diffuse(params MessageBase[] messages)
        {
            for (int i = 0; i < 16 && i < firstNodeInfos.Length; i++)
                Connect(firstNodeInfos[i].IpAddress, firstNodeInfos[i].Port, true, () => { }, messages);
        }

        protected override void KeepConnections() { }
    }

    public class CreaNodeLocalTestContinue2 : CreaNodeLocalTest2
    {
        public CreaNodeLocalTestContinue2(ushort _portNumber, int _creaVersion, string _appnameWithVersion) : base(_portNumber, _creaVersion, _appnameWithVersion) { }

        private readonly int maxInboundConnection = 16;
        private readonly int maxOutboundConnection = 8;

        private readonly object clientNodesLock = new object();
        private Dictionary<NodeInformation, Connection> clientNodes = new Dictionary<NodeInformation, Connection>();
        private readonly object listenerNodesLock = new object();
        private Dictionary<NodeInformation, Connection> listenerNodes = new Dictionary<NodeInformation, Connection>();

        public class Connection
        {
            public Connection(SocketChannel _sc, Action<string> __ConsoleWriteLine)
            {
                sc = _sc;
                _ConsoleWriteLine = __ConsoleWriteLine;
            }

            public readonly SocketChannel sc;
            public readonly Action<string> _ConsoleWriteLine;
        }

        protected override bool IsContinue
        {
            get { return true; }
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

        protected override void UpdateNodeState(NodeInformation nodeInfo, bool isSucceeded) { }

        protected override bool IsListenerCanContinue(NodeInformation nodeInfo)
        {
            lock (listenerNodesLock)
                return listenerNodes.Count < maxInboundConnection;
        }

        protected override bool IsWantToContinue(NodeInformation nodeInfo)
        {
            lock (listenerNodesLock)
                return listenerNodes.Count < maxInboundConnection;
        }

        protected override bool IsClientCanContinue(NodeInformation nodeInfo)
        {
            lock (clientNodesLock)
                return clientNodes.Count < maxOutboundConnection;
        }

        protected override void InboundContinue(NodeInformation nodeInfo, SocketChannel sc, Action<string> _ConsoleWriteLine)
        {
            lock (listenerNodesLock)
                listenerNodes.Add(nodeInfo, new Connection(sc, _ConsoleWriteLine));

            sc.Closed += (sender, e) =>
            {
                lock (listenerNodesLock)
                    listenerNodes.Remove(nodeInfo);
            };
            sc.Closed += (sender, e) =>
            {
                lock (listenerNodesLock)
                    listenerNodes.Remove(nodeInfo);
            };

            Continue(nodeInfo, sc, _ConsoleWriteLine);
        }

        protected override void OutboundContinue(NodeInformation nodeInfo, SocketChannel sc, Action<string> _ConsoleWriteLine)
        {
            lock (clientNodesLock)
                clientNodes.Add(nodeInfo, new Connection(sc, _ConsoleWriteLine));

            sc.Closed += (sender, e) =>
            {
                lock (clientNodesLock)
                    clientNodes.Remove(nodeInfo);
            };
            sc.Failed += (sender, e) =>
            {
                lock (clientNodesLock)
                    clientNodes.Remove(nodeInfo);
            };

            Continue(nodeInfo, sc, _ConsoleWriteLine);
        }

        private void Continue(NodeInformation nodeInfo, SocketChannel sc, Action<string> _ConsoleWriteLine)
        {
            _ConsoleWriteLine("常時接続" + string.Join(",", clientNodes.Count.ToString(), listenerNodes.Count.ToString()));

            sc.Sessioned += (sender, e) =>
            {
                try
                {
                    _ConsoleWriteLine("新しいセッション");

                    InboundProtocol(e, _ConsoleWriteLine);
                }
                catch (Exception ex)
                {
                    this.RaiseError("inbound_session".GetLogMessage(), 5, ex);
                }
                finally
                {
                    e.Close();

                    _ConsoleWriteLine("セッション終わり");
                }
            };
        }

        protected override void Request(NodeInformation nodeinfo, params MessageBase[] messages)
        {
            Connection connection = null;

            lock (clientNodesLock)
                if (clientNodes.Keys.Contains(nodeinfo))
                    connection = clientNodes[nodeinfo];
            lock (listenerNodesLock)
                if (listenerNodes.Keys.Contains(nodeinfo))
                    connection = listenerNodes[nodeinfo];

            if (connection != null)
            {
                SessionChannel sc2 = null;
                try
                {
                    sc2 = connection.sc.NewSession();

                    connection._ConsoleWriteLine("新しいセッション");

                    OutboundProtocol(messages, sc2, connection._ConsoleWriteLine);
                }
                catch (Exception ex)
                {
                    this.RaiseError("outbound_session".GetLogMessage(), 5, ex);
                }
                finally
                {
                    if (sc2 != null)
                    {
                        sc2.Close();

                        connection._ConsoleWriteLine("セッション終わり");
                    }
                }
            }
            else
                try
                {
                    Connect(nodeInfo.IpAddress, nodeInfo.Port, true, () => { }, messages);
                }
                catch (Exception ex)
                {
                    this.RaiseError("outbound_session".GetLogMessage(), 5, ex);
                }
        }

        protected override void Diffuse(params MessageBase[] messages)
        {
            List<Connection> connections = new List<Connection>();
            lock (clientNodesLock)
                foreach (Connection cq in clientNodes.Values)
                    connections.Add(cq);
            lock (listenerNodesLock)
                foreach (Connection cq in listenerNodes.Values)
                    connections.Add(cq);

            foreach (Connection c in connections)
            {
                SessionChannel sc2 = null;
                try
                {
                    sc2 = c.sc.NewSession();

                    c._ConsoleWriteLine("新しいセッション");

                    OutboundProtocol(messages, sc2, c._ConsoleWriteLine);
                }
                catch (Exception ex)
                {
                    this.RaiseError("diffuse".GetLogMessage(), 5, ex);
                }
                finally
                {
                    sc2.Close();

                    c._ConsoleWriteLine("セッション終わり");
                }
            }
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

                    if (count < maxOutboundConnection)
                        try
                        {
                            Connect(firstNodeInfos[i].IpAddress, firstNodeInfos[i].Port, false, () => are.Set());
                        }
                        catch (Exception ex)
                        {
                            this.RaiseError("keep_conn".GetLogMessage(), 5, ex);

                            are.Set();
                        }

                    are.WaitOne();
                }

                this.RaiseNotification("keep_conn_completed".GetLogMessage(), 5);
            }
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
                        tb.Margin = new Thickness(0.0, 10.0, 0.0, 10.0);

                        sp2.Children.Add(tb);
                    })).BeginExecuteInUIThread();

                    SimulationWindow sw = new SimulationWindow();
                    sw.ShowDialog();

                    //this.StartTask(string.Empty, string.Empty, () =>
                    //{
                    //    Test10NodesInv();
                    //});
                };

                Closed += (sender, e) =>
                {
                    string fileText = string.Empty;
                    foreach (var child in sp.Children)
                        fileText += (child as TextBlock).Text + Environment.NewLine;

                    File.AppendAllText(Path.Combine(new FileInfo(Assembly.GetEntryAssembly().Location).DirectoryName, "LogTest.txt"), fileText);
                };
            }

            private void Test10NodesInv()
            {
                Stopwatch stopwatch = new Stopwatch();
                int counter = 0;

                int numOfNodes = 5;
                CreaNodeLocalTestContinue2[] cnlts = new CreaNodeLocalTestContinue2[numOfNodes];
                for (int i = 0; i < numOfNodes; i++)
                {
                    cnlts[i] = new CreaNodeLocalTestContinue2((ushort)(7777 + i), 0, "test");
                    cnlts[i].TxtestReceived += (sender2, e2) =>
                    {
                        counter++;
                        (string.Join(":", e2.IpAddress.ToString(), e2.Port.ToString()) + " " + counter.ToString() + " " + ((double)counter / (double)numOfNodes).ToString() + " " + stopwatch.Elapsed.ToString()).ConsoleWriteLine();

                        if (counter == numOfNodes - 1)
                            stopwatch.Stop();
                    };
                    cnlts[i].Start();
                    while (!cnlts[i].isStartCompleted)
                        Thread.Sleep(100);
                }

                MessageBox.Show("start");

                stopwatch.Start();

                cnlts[numOfNodes - 1].DiffuseInv(null, null);
            }

            private void Test2NodesInv2()
            {
                CreaNodeLocalTestContinue2 cnlt1 = new CreaNodeLocalTestContinue2(7777, 0, "test");
                cnlt1.Start();
                while (!cnlt1.isStartCompleted)
                    Thread.Sleep(100);
                CreaNodeLocalTestContinue2 cnlt2 = new CreaNodeLocalTestContinue2(7778, 0, "test");
                cnlt2.Start();
                while (!cnlt2.isStartCompleted)
                    Thread.Sleep(100);

                cnlt2.DiffuseInv(null, null);
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

        public override bool Equals(object obj) { return (obj as FirstNodeInformation).Operate((o) => o != null && Equals(o)); }

        public override int GetHashCode() { return ipAddress.GetHashCode() ^ port.GetHashCode(); }

        public override string ToString() { return ipAddress + ":" + port.ToString(); }

        public bool Equals(FirstNodeInformation other) { return ipAddress.ToString() == other.ipAddress.ToString() && port == other.port; }
    }

    public class NodeInformation : FirstNodeInformation, IEquatable<NodeInformation>
    {
        public NodeInformation() : base(0) { }

        public NodeInformation(IPAddress _ipAddress, ushort _port, Network _network, string _publicRSAParameters)
            : base(0, _ipAddress, _port, _network)
        {
            participation = DateTime.Now;
            publicRSAParameters = _publicRSAParameters;
        }

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

        public override bool Equals(object obj) { return (obj as NodeInformation).Operate((o) => o != null && Equals(o)); }

        public override int GetHashCode() { return Id.GetHashCode(); }

        public override string ToString() { return Id.ToString(); }

        public bool Equals(NodeInformation other) { return Id.Equals(other.Id); }
    }

    #endregion

    #region Cremlia実装

    public class CremliaIdFactory : ICremliaIdFactory
    {
        public CremliaIdFactory() { }

        public ICremliaId Create() { return new CremliaId(); }
    }

    public class CremliaId : ICremliaId, IComparable<CremliaId>, IEquatable<CremliaId>, IComparable
    {
        public CremliaId() : this(new Sha256Hash()) { }

        public CremliaId(Sha256Hash _hash)
        {
            hash = _hash;
        }

        public readonly Sha256Hash hash;

        public int Size
        {
            get { return hash.size; }
        }

        public byte[] Bytes
        {
            get { return hash.bytes; }
        }

        public void FromBytes(byte[] bytes)
        {
            if (bytes.Length != Bytes.Length)
                throw new ArgumentException("cremlia_id_bytes_length");

            hash.bytes = bytes;
        }

        public ICremliaId XOR(ICremliaId id)
        {
            if (id.Size != Size)
                throw new ArgumentException("not_equal_hash");

            byte[] xorBytes = new byte[Bytes.Length];
            for (int i = 0; i < xorBytes.Length; i++)
                xorBytes[i] = (byte)(Bytes[i] ^ id.Bytes[i]);
            return new CremliaId(new Sha256Hash(xorBytes));
        }

        public override bool Equals(object obj)
        {
            if (!(obj is CremliaId))
                return false;
            return hash.Equals((obj as CremliaId).hash);
        }

        public bool Equals(CremliaId other) { return hash.Equals(other); }

        public int CompareTo(object obj) { return hash.CompareTo((obj as CremliaId).hash); }

        public int CompareTo(CremliaId other) { return hash.CompareTo(other.hash); }

        public override int GetHashCode() { return hash.GetHashCode(); }

        public override string ToString() { return hash.ToString(); }
    }

    public class CremliaNodeInfomation : ICremliaNodeInfomation, IEquatable<CremliaNodeInfomation>
    {
        public CremliaNodeInfomation(NodeInformation _nodeInfo)
        {
            nodeInfo = _nodeInfo;
        }

        public readonly NodeInformation nodeInfo;

        public ICremliaId Id
        {
            get { return new CremliaId(nodeInfo.Id); }
        }

        public override bool Equals(object obj) { return (obj as CremliaNodeInfomation).Operate((o) => o != null && Equals(o)); }

        public override int GetHashCode() { return Id.GetHashCode(); }

        public override string ToString() { return Id.ToString(); }

        public bool Equals(CremliaNodeInfomation other) { return Id.Equals(other.Id); }
    }

    public class CremliaDatabaseIo : ICremliaDatabaseIo
    {
        private int tExpire;
        public int TExpire
        {
            set { tExpire = value; }
        }

        public byte[] Get(ICremliaId id)
        {
            throw new NotImplementedException();
        }

        public Tuple<ICremliaId, byte[]>[] GetOriginals()
        {
            throw new NotImplementedException();
        }

        public Tuple<ICremliaId, byte[]>[] GetCharges()
        {
            throw new NotImplementedException();
        }

        public void Set(ICremliaId id, byte[] data, bool isOriginal, bool isCache)
        {
            throw new NotImplementedException();
        }
    }

    #endregion

    #region cremlia

    public interface ICremliaIdFactory
    {
        ICremliaId Create();
    }

    public interface ICremliaId
    {
        int Size { get; }
        byte[] Bytes { get; }

        void FromBytes(byte[] bytes);
        ICremliaId XOR(ICremliaId id);
    }

    public interface ICremliaNodeInfomation
    {
        ICremliaId Id { get; }
    }

    public interface ICremliaNetworkIo
    {
        event EventHandler<ICremliaNetworkIoSession> SessionStarted;

        ICremliaNetworkIoSession StartSession(ICremliaNodeInfomation nodeInfo);
    }

    public interface ICremliaNetworkIoSession
    {
        ICremliaNodeInfomation NodeInfo { get; }

        void Write(CremliaMessageBase message);
        CremliaMessageBase Read();
        void Close();
    }

    public interface ICremliaDatabaseIo
    {
        int TExpire { set; }

        byte[] Get(ICremliaId id);
        Tuple<ICremliaId, byte[]>[] GetOriginals();
        Tuple<ICremliaId, byte[]>[] GetCharges();
        void Set(ICremliaId id, byte[] data, bool isOriginal, bool isCache);
    }

    public class Cremlia
    {
        public Cremlia(ICremliaIdFactory _idFactory, ICremliaDatabaseIo _databaseIo, ICremliaNetworkIo _networkIo, ICremliaNodeInfomation _myNodeInfo) : this(_idFactory, _databaseIo, _networkIo, _myNodeInfo, 256, 20, 3, 86400 * 1000, 3600 * 1000, 3600 * 1000, 86400 * 1000) { }

        public Cremlia(ICremliaIdFactory _idFactory, ICremliaDatabaseIo _databaseIo, ICremliaNetworkIo _networkIo, ICremliaNodeInfomation _myNodeInfo, int _keySpace, int _K, int _α, int _tExpire, int _tRefresh, int _tReplicate, int _tRepublish)
        {
            //鍵空間は8の倍数とする
            if (_keySpace % 8 != 0)
                throw new InvalidDataException("key_space");
            if (_myNodeInfo.Id.Size != _keySpace)
                throw new InvalidDataException("id_size_and_key_space");

            idFactory = _idFactory;
            databaseIo = _databaseIo;
            networkIo = _networkIo;
            myNodeInfo = _myNodeInfo;
            keySpace = _keySpace;
            K = _K;
            α = _α;
            tExpire = _tExpire;
            tRefresh = _tRefresh;
            tReplicate = _tReplicate;
            tRepublish = _tRepublish;

            kbuckets = new List<ICremliaNodeInfomation>[keySpace];
            for (int i = 0; i < kbuckets.Length; i++)
                kbuckets[i] = new List<ICremliaNodeInfomation>();
            kbucketsLocks = new object[keySpace];
            for (int i = 0; i < kbucketsLocks.Length; i++)
                kbucketsLocks[i] = new object();
            kbucketsUpdatedTime = new DateTime[keySpace];

            networkIo.SessionStarted += (sender, e) =>
            {
                Action<ICremliaId> _ResNeighborNodes = (id) =>
                {
                    SortedList<ICremliaId, ICremliaNodeInfomation> findTable = new SortedList<ICremliaId, ICremliaNodeInfomation>();
                    GetNeighborNodesTable(id, findTable);
                    e.Write(new NeighborNodesMessage(findTable.Values.ToArray()));
                };

                CremliaMessageBase message = e.Read();
                if (message is PingReqMessage)
                    e.Write(new PingResMessage());
                else if (message is StoreReqMessage)
                {
                    StoreReqMessage srm = message as StoreReqMessage;
                    databaseIo.Set(srm.id, srm.data, false, false);
                }
                else if (message is FindNodesReqMessage)
                    _ResNeighborNodes((message as FindNodesReqMessage).id);
                else if (message is FindValueReqMessage)
                {
                    ICremliaId id = (message as FindValueReqMessage).id;

                    byte[] data = databaseIo.Get(id);
                    if (data == null)
                        _ResNeighborNodes(id);
                    else
                        e.Write(new ValueMessage(data));
                }
                //独自実装
                else if (message is GetIdsAndValuesReqMessage)
                    e.Write(new IdsAndValuesMessage(databaseIo.GetCharges()));
                else
                    throw new NotSupportedException("cremlia_not_supported_message");

                e.Close();

                UpdateNodeState(e.NodeInfo, true);
            };

            databaseIo.TExpire = tExpire;

            Timer timerRefresh = new Timer((state) =>
            {
                for (int i = 0; i < kbuckets.Length; i++)
                    //時間が掛かるので若干判定条件を短めに
                    if (kbuckets[i].Count != 0 && kbucketsUpdatedTime[i] <= DateTime.Now - new TimeSpan(0, 0, (int)(tRefresh * 0.9)))
                        FindNodes(GetRamdomHash(i));
            }, null, tRefresh, tRefresh);

            Action<Tuple<ICremliaId, byte[]>[]> _StoreIdsAndValues = (idsAndValues) =>
            {
                foreach (Tuple<ICremliaId, byte[]> idAndValue in idsAndValues)
                    foreach (ICremliaNodeInfomation nodeInfo in FindNodes(idAndValue.Item1))
                        ReqStore(nodeInfo, idAndValue.Item1, idAndValue.Item2);
            };

            Timer timerReplicate = new Timer((state) => _StoreIdsAndValues(databaseIo.GetCharges()), null, tReplicate, tReplicate);
            Timer timerRepublish = new Timer((state) => _StoreIdsAndValues(databaseIo.GetOriginals()), null, tRepublish, tRepublish);
        }

        private readonly ICremliaIdFactory idFactory;
        private readonly ICremliaDatabaseIo databaseIo;
        private readonly ICremliaNetworkIo networkIo;
        private readonly object[] kbucketsLocks;
        private readonly List<ICremliaNodeInfomation>[] kbuckets;
        private readonly DateTime[] kbucketsUpdatedTime;

        public readonly ICremliaNodeInfomation myNodeInfo;
        public readonly int keySpace;
        public readonly int K;
        public readonly int α;
        public readonly int tExpire;
        public readonly int tRefresh;
        public readonly int tReplicate;
        public readonly int tRepublish;

        public bool ReqPing(ICremliaNodeInfomation nodeInfo)
        {
            if (nodeInfo.Equals(myNodeInfo))
                throw new ArgumentException("cremlia_my_node");

            ICremliaNetworkIoSession session = networkIo.StartSession(nodeInfo);
            if (session == null)
            {
                UpdateNodeState(nodeInfo, false);
                return false;
            }

            session.Write(new PingReqMessage());
            CremliaMessageBase message = session.Read();
            session.Close();

            if (message == null || !(message is PingResMessage))
            {
                UpdateNodeState(nodeInfo, false);
                return false;
            }

            UpdateNodeState(nodeInfo, true);
            return true;
        }

        public bool ReqStore(ICremliaNodeInfomation nodeInfo, ICremliaId id, byte[] data)
        {
            if (nodeInfo.Equals(myNodeInfo))
                throw new ArgumentException("cremlia_my_node");

            ICremliaNetworkIoSession session = networkIo.StartSession(nodeInfo);
            if (session == null)
            {
                UpdateNodeState(nodeInfo, false);
                return false;
            }

            session.Write(new StoreReqMessage(id, data));
            session.Close();

            UpdateNodeState(nodeInfo, true);
            return true;
        }

        public ICremliaNodeInfomation[] ReqFindNodes(ICremliaNodeInfomation nodeInfo, ICremliaId id)
        {
            if (nodeInfo.Equals(myNodeInfo))
                throw new ArgumentException("cremlia_my_node");

            ICremliaNetworkIoSession session = networkIo.StartSession(nodeInfo);
            if (session == null)
            {
                UpdateNodeState(nodeInfo, false);
                return null;
            }

            session.Write(new FindNodesReqMessage(id));
            CremliaMessageBase message = session.Read();
            session.Close();

            if (message == null || !(message is NeighborNodesMessage))
            {
                UpdateNodeState(nodeInfo, false);
                return null;
            }

            UpdateNodeState(nodeInfo, true);
            return (message as NeighborNodesMessage).nodeInfos;
        }

        public MultipleReturn<ICremliaNodeInfomation[], byte[]> ReqFindValue(ICremliaNodeInfomation nodeInfo, ICremliaId id)
        {
            if (nodeInfo.Equals(myNodeInfo))
                throw new ArgumentException("cremlia_my_node");

            ICremliaNetworkIoSession session = networkIo.StartSession(nodeInfo);
            if (session == null)
            {
                UpdateNodeState(nodeInfo, false);
                return null;
            }

            session.Write(new FindValueReqMessage(id));
            CremliaMessageBase message = session.Read();
            session.Close();

            if (message != null)
                if (message is NeighborNodesMessage)
                {
                    UpdateNodeState(nodeInfo, true);
                    return new MultipleReturn<ICremliaNodeInfomation[], byte[]>((message as NeighborNodesMessage).nodeInfos);
                }
                else if (message is ValueMessage)
                {
                    UpdateNodeState(nodeInfo, true);
                    return new MultipleReturn<ICremliaNodeInfomation[], byte[]>((message as ValueMessage).data);
                }

            UpdateNodeState(nodeInfo, false);
            return null;
        }

        //独自実装
        public Tuple<ICremliaId, byte[]>[] ReqGetIdsAndValues(ICremliaNodeInfomation nodeInfo)
        {
            if (nodeInfo.Equals(myNodeInfo))
                throw new ArgumentException("cremlia_my_node");

            ICremliaNetworkIoSession session = networkIo.StartSession(nodeInfo);
            if (session == null)
            {
                UpdateNodeState(nodeInfo, false);
                return null;
            }

            session.Write(new GetIdsAndValuesReqMessage());
            CremliaMessageBase message = session.Read();
            session.Close();

            if (message == null || !(message is IdsAndValuesMessage))
            {
                UpdateNodeState(nodeInfo, false);
                return null;
            }

            UpdateNodeState(nodeInfo, true);
            return (message as IdsAndValuesMessage).idsAndValues;
        }

        public ICremliaNodeInfomation[] FindNodes(ICremliaId id)
        {
            object findLock = new object();
            SortedList<ICremliaId, ICremliaNodeInfomation> findTable = new SortedList<ICremliaId, ICremliaNodeInfomation>();
            Dictionary<ICremliaNodeInfomation, bool> checkTable = new Dictionary<ICremliaNodeInfomation, bool>();
            Dictionary<ICremliaNodeInfomation, bool?> checkTableSucceedOrFail = new Dictionary<ICremliaNodeInfomation, bool?>();

            GetNeighborNodesTable(id, findTable);
            foreach (var nodeInfo in findTable.Values)
                checkTable.Add(nodeInfo, false);
            foreach (var nodeInfo in findTable.Values)
                checkTableSucceedOrFail.Add(nodeInfo, null);

            AutoResetEvent[] ares = new AutoResetEvent[α];
            for (int i = 0; i < α; i++)
            {
                int inner = i;

                ares[inner] = new AutoResetEvent(false);

                this.StartTask("cremlia_find_nodes", "cremlia_find_nodes", () =>
                {
                    while (true)
                    {
                        ICremliaNodeInfomation nodeInfo = null;
                        int c = 0;
                        lock (findLock)
                            for (int j = 0; j < findTable.Count; j++)
                                if (!checkTable[findTable[findTable.Keys[j]]])
                                {
                                    nodeInfo = findTable[findTable.Keys[j]];
                                    checkTable[findTable[findTable.Keys[j]]] = true;
                                    break;
                                }
                                else if (checkTableSucceedOrFail[findTable[findTable.Keys[j]]] == true && ++c == K)
                                    break;

                        //適切なノード情報が見付からない場合はほぼ確実にノードの探索は殆ど終わっていると想定し、残りの処理は他のスレッドに任せる
                        if (nodeInfo == null)
                            break;
                        else
                        {
                            ICremliaNodeInfomation[] nodeInfos = ReqFindNodes(nodeInfo, id);
                            lock (findLock)
                                if (nodeInfos == null)
                                    checkTableSucceedOrFail[nodeInfo] = false;
                                else
                                {
                                    checkTableSucceedOrFail[nodeInfo] = true;

                                    foreach (ICremliaNodeInfomation ni in nodeInfos)
                                    {
                                        ICremliaId xor = id.XOR(ni.Id);
                                        if (!ni.Equals(myNodeInfo) && !findTable.Keys.Contains(xor))
                                        {
                                            findTable.Add(xor, ni);
                                            checkTable.Add(ni, false);
                                            checkTableSucceedOrFail.Add(ni, null);
                                        }
                                    }
                                }
                        }
                    }

                    ares[inner].Set();
                });
            }

            for (int i = 0; i < α; i++)
                ares[i].WaitOne();

            List<ICremliaNodeInfomation> findNodes = new List<ICremliaNodeInfomation>();
            for (int i = 0; i < findTable.Count && findNodes.Count < K; i++)
                if (checkTable[findTable[findTable.Keys[i]]] && checkTableSucceedOrFail[findTable[findTable.Keys[i]]] == true)
                    findNodes.Add(findTable[findTable.Keys[i]]);

            return findNodes.ToArray();
        }

        public byte[] FindValue(ICremliaId id)
        {
            object findLock = new object();
            SortedList<ICremliaId, ICremliaNodeInfomation> findTable = new SortedList<ICremliaId, ICremliaNodeInfomation>();
            Dictionary<ICremliaNodeInfomation, bool> checkTable = new Dictionary<ICremliaNodeInfomation, bool>();
            Dictionary<ICremliaNodeInfomation, bool?> checkTableSucceedOrFail = new Dictionary<ICremliaNodeInfomation, bool?>();

            GetNeighborNodesTable(id, findTable);
            foreach (var nodeInfo in findTable.Values)
                checkTable.Add(nodeInfo, false);
            foreach (var nodeInfo in findTable.Values)
                checkTableSucceedOrFail.Add(nodeInfo, null);

            byte[] data = null;
            AutoResetEvent[] ares = new AutoResetEvent[α];
            for (int i = 0; i < α; i++)
            {
                int inner = i;

                ares[inner] = new AutoResetEvent(false);

                this.StartTask("cremlia_find_value", "cremlia_find_value", () =>
                {
                    while (data == null)
                    {
                        ICremliaNodeInfomation nodeInfo = null;
                        int c = 0;
                        lock (findLock)
                            for (int j = 0; j < findTable.Count; j++)
                                if (!checkTable[findTable[findTable.Keys[j]]])
                                {
                                    nodeInfo = findTable[findTable.Keys[j]];
                                    checkTable[findTable[findTable.Keys[j]]] = true;
                                    break;
                                }
                                else if (checkTableSucceedOrFail[findTable[findTable.Keys[j]]] == true && ++c == K)
                                    break;

                        //適切なノード情報が見付からない場合はほぼ確実にノードの探索は殆ど終わっていると想定し、残りの処理は他のスレッドに任せる
                        if (nodeInfo == null)
                            break;
                        else
                        {
                            MultipleReturn<ICremliaNodeInfomation[], byte[]> multipleReturn = ReqFindValue(nodeInfo, id);
                            lock (findLock)
                                if (multipleReturn == null)
                                    checkTableSucceedOrFail[nodeInfo] = false;
                                else
                                {
                                    checkTableSucceedOrFail[nodeInfo] = true;

                                    if (multipleReturn.IsValue1)
                                        foreach (ICremliaNodeInfomation ni in multipleReturn.Value1)
                                        {
                                            ICremliaId xor = id.XOR(ni.Id);
                                            if (!ni.Equals(myNodeInfo) && !findTable.Keys.Contains(xor))
                                            {
                                                findTable.Add(xor, ni);
                                                checkTable.Add(ni, false);
                                                checkTableSucceedOrFail.Add(ni, null);
                                            }
                                        }
                                    else if (multipleReturn.IsValue2)
                                        data = multipleReturn.Value2;
                                }
                        }
                    }

                    ares[inner].Set();
                });
            }

            for (int i = 0; i < α; i++)
                ares[i].WaitOne();

            if (data != null)
                databaseIo.Set(id, data, false, true);

            return data;
        }

        public void StoreOriginal(ICremliaId id, byte[] data)
        {
            databaseIo.Set(id, data, true, false);

            foreach (ICremliaNodeInfomation nodeInfo in FindNodes(id))
                ReqStore(nodeInfo, id, data);
        }

        public void Join(ICremliaNodeInfomation[] nodeInfos)
        {
            foreach (ICremliaNodeInfomation nodeInfo in nodeInfos)
                UpdateNodeStateWhenJoin(nodeInfo);

            foreach (ICremliaNodeInfomation nodeInfo in FindNodes(myNodeInfo.Id))
                foreach (Tuple<ICremliaId, byte[]> idAndValue in ReqGetIdsAndValues(nodeInfo))
                    databaseIo.Set(idAndValue.Item1, idAndValue.Item2, false, false);
        }

        public ICremliaId GetRamdomHash(int distanceLevel)
        {
            byte[] bytes = new byte[keySpace / 8];
            for (int i = bytes.Length - 1; i >= 0; i--, distanceLevel -= 8)
                if (distanceLevel >= 8)
                    bytes[i] = (byte)256.RandomNum();
                else
                {
                    if (distanceLevel == 0)
                        bytes[i] = 1;
                    else if (distanceLevel == 1)
                        bytes[i] = (byte)(2 + 2.RandomNum());
                    else if (distanceLevel == 2)
                        bytes[i] = (byte)(4 + 4.RandomNum());
                    else if (distanceLevel == 3)
                        bytes[i] = (byte)(8 + 8.RandomNum());
                    else if (distanceLevel == 4)
                        bytes[i] = (byte)(16 + 16.RandomNum());
                    else if (distanceLevel == 5)
                        bytes[i] = (byte)(32 + 32.RandomNum());
                    else if (distanceLevel == 6)
                        bytes[i] = (byte)(64 + 64.RandomNum());
                    else if (distanceLevel == 7)
                        bytes[i] = (byte)(128 + 128.RandomNum());

                    break;
                }

            ICremliaId xor = idFactory.Create();
            if (xor.Size != keySpace)
                throw new InvalidDataException("invalid_id_size");
            xor.FromBytes(bytes);
            return xor.XOR(myNodeInfo.Id);
        }

        public int GetDistanceLevel(ICremliaId id)
        {
            ICremliaId xor = id.XOR(myNodeInfo.Id);

            //距離が0の場合にはdistanceLevelは-1
            //　　論文ではハッシュ値の衝突が考慮されていないっぽい？
            int distanceLevel = keySpace - 1;
            for (int i = 0; i < xor.Bytes.Length; i++)
                if (xor.Bytes[i] == 0)
                    distanceLevel -= 8;
                else
                {
                    if (xor.Bytes[i] == 1)
                        distanceLevel -= 7;
                    else if (xor.Bytes[i] <= 3 && xor.Bytes[i] >= 2)
                        distanceLevel -= 6;
                    else if (xor.Bytes[i] <= 7 && xor.Bytes[i] >= 4)
                        distanceLevel -= 5;
                    else if (xor.Bytes[i] <= 15 && xor.Bytes[i] >= 8)
                        distanceLevel -= 4;
                    else if (xor.Bytes[i] <= 31 && xor.Bytes[i] >= 16)
                        distanceLevel -= 3;
                    else if (xor.Bytes[i] <= 63 && xor.Bytes[i] >= 32)
                        distanceLevel -= 2;
                    else if (xor.Bytes[i] <= 127 && xor.Bytes[i] >= 64)
                        distanceLevel -= 1;

                    break;
                }

            return distanceLevel;
        }

        public void GetNeighborNodesTable(ICremliaId id, SortedList<ICremliaId, ICremliaNodeInfomation> findTable)
        {
            Func<ICremliaNodeInfomation, bool> TryFindTableAddition = (nodeInfo) =>
            {
                ICremliaId xor = id.XOR(nodeInfo.Id);
                if (!findTable.ContainsKey(xor))
                    findTable.Add(xor, nodeInfo);
                else
                    this.RaiseError("find_table_already_added".GetLogMessage(xor.ToString(), findTable[xor].Id.ToString(), nodeInfo.Id.ToString()), 5);

                return findTable.Count >= K;
            };

            int distanceLevel = GetDistanceLevel(id);

            //原論文では探索するidが自己のノード情報のidと同一である場合を想定していない・・・
            if (distanceLevel == -1)
                distanceLevel = 0;

            lock (kbucketsLocks[distanceLevel])
                foreach (ICremliaNodeInfomation nodeInfo in kbuckets[distanceLevel])
                    if (TryFindTableAddition(nodeInfo))
                        return;

            for (int i = distanceLevel - 1; i >= 0; i--)
                lock (kbucketsLocks[i])
                    foreach (ICremliaNodeInfomation nodeInfo in kbuckets[i])
                        if (TryFindTableAddition(nodeInfo))
                            return;

            for (int i = distanceLevel + 1; i < kbuckets.Length; i++)
                lock (kbucketsLocks[i])
                    foreach (ICremliaNodeInfomation nodeInfo in kbuckets[i])
                        if (TryFindTableAddition(nodeInfo))
                            return;
        }

        public ICremliaNodeInfomation[] GetKbuckets(int distanceLevel)
        {
            List<ICremliaNodeInfomation> nodeInfos = new List<ICremliaNodeInfomation>();
            lock (kbuckets[distanceLevel])
                for (int i = 0; i < kbuckets[distanceLevel].Count; i++)
                    nodeInfos.Add(kbuckets[distanceLevel][i]);
            return nodeInfos.ToArray();
        }

        public void UpdateNodeState(ICremliaNodeInfomation nodeInfo, bool isValid)
        {
            if (nodeInfo.Id.Size != keySpace)
                throw new InvalidOperationException("invalid_id_size");

            if (nodeInfo.Equals(myNodeInfo))
                this.RaiseWarning("my_node_info".GetLogMessage(), 5);
            else
            {
                int distanceLevel = GetDistanceLevel(nodeInfo.Id);
                lock (kbuckets[distanceLevel])
                    if (isValid)
                        if (kbuckets[distanceLevel].Contains(nodeInfo))
                        {
                            kbuckets[distanceLevel].Remove(nodeInfo);
                            kbuckets[distanceLevel].Add(nodeInfo);
                        }
                        else
                        {
                            if (kbuckets[distanceLevel].Count >= K)
                            {
                                ICremliaNodeInfomation pingNodeInfo = kbuckets[distanceLevel][0];
                                this.StartTask("update_node_state", "update_node_state", () =>
                                {
                                    bool isResponded = ReqPing(pingNodeInfo);

                                    lock (kbuckets[distanceLevel])
                                        if (kbuckets[distanceLevel][0] == pingNodeInfo)
                                            if (isResponded)
                                            {
                                                kbuckets[distanceLevel].RemoveAt(0);
                                                kbuckets[distanceLevel].Add(pingNodeInfo);
                                            }
                                            else
                                            {
                                                kbuckets[distanceLevel].RemoveAt(0);
                                                kbuckets[distanceLevel].Add(nodeInfo);
                                            }
                                });
                            }
                            else
                                kbuckets[distanceLevel].Add(nodeInfo);
                        }
                    else if (kbuckets[distanceLevel].Contains(nodeInfo))
                        kbuckets[distanceLevel].Remove(nodeInfo);

            }
        }

        public void UpdateNodeStateWhenJoin(ICremliaNodeInfomation nodeInfo)
        {
            if (nodeInfo.Id.Size != keySpace)
                throw new InvalidOperationException("invalid_id_size");

            if (nodeInfo.Equals(myNodeInfo))
                this.RaiseWarning("my_node_info".GetLogMessage(), 5);
            else
            {
                int distanceLevel = GetDistanceLevel(nodeInfo.Id);
                if (kbuckets[distanceLevel].Contains(nodeInfo))
                {
                    kbuckets[distanceLevel].Remove(nodeInfo);
                    kbuckets[distanceLevel].Add(nodeInfo);
                }
                else if (kbuckets[distanceLevel].Count >= K)
                {
                    kbuckets[distanceLevel].RemoveAt(0);
                    kbuckets[distanceLevel].Add(nodeInfo);
                }
                else
                    kbuckets[distanceLevel].Add(nodeInfo);
            }
        }
    }

    public abstract class CremliaMessageBase { }

    public class PingReqMessage : CremliaMessageBase { }

    public class PingResMessage : CremliaMessageBase { }

    public class StoreReqMessage : CremliaMessageBase
    {
        public StoreReqMessage(ICremliaId _id, byte[] _data)
        {
            id = _id;
            data = _data;
        }

        public readonly ICremliaId id;
        public readonly byte[] data;
    }

    public class FindNodesReqMessage : CremliaMessageBase
    {
        public FindNodesReqMessage(ICremliaId _id)
        {
            id = _id;
        }

        public readonly ICremliaId id;
    }

    public class NeighborNodesMessage : CremliaMessageBase
    {
        public NeighborNodesMessage(ICremliaNodeInfomation[] _nodeInfos)
        {
            nodeInfos = _nodeInfos;
        }

        public readonly ICremliaNodeInfomation[] nodeInfos;
    }

    public class FindValueReqMessage : CremliaMessageBase
    {
        public FindValueReqMessage(ICremliaId _id)
        {
            id = _id;
        }

        public readonly ICremliaId id;
    }

    public class ValueMessage : CremliaMessageBase
    {
        public ValueMessage(byte[] _data)
        {
            data = _data;
        }

        public readonly byte[] data;
    }

    public class GetIdsAndValuesReqMessage : CremliaMessageBase { }

    public class IdsAndValuesMessage : CremliaMessageBase
    {
        public IdsAndValuesMessage(Tuple<ICremliaId, byte[]>[] _idsAndValues)
        {
            idsAndValues = _idsAndValues;
        }

        public readonly Tuple<ICremliaId, byte[]>[] idsAndValues;
    }

    #endregion

    #region データ

    //<未改良>2014/05/03 Sha256HashとRipemd160hashの統合
    //　　　　共通の基底クラスを作る
    public class Sha256Hash : SHAREDDATA, IComparable<Sha256Hash>, IEquatable<Sha256Hash>, IComparable
    {
        public Sha256Hash()
        {
            bytesLength = 32;
            size = bytesLength * 8;
            bytes = new byte[bytesLength];
        }

        public Sha256Hash(string value) : this(value.FromHexstring()) { }

        public Sha256Hash(byte[] _bytes)
        {
            bytesLength = 32;
            size = bytesLength * 8;

            if (_bytes.Length != bytesLength)
                throw new ArgumentException("Sha256_bytes_length");

            bytes = _bytes;
        }

        public readonly int bytesLength;
        public readonly int size;

        public byte[] bytes { get; set; }

        protected override Func<ReaderWriter, IEnumerable<MainDataInfomation>> StreamInfo
        {
            get
            {
                return (msrw) => new MainDataInfomation[]{
                    new MainDataInfomation(typeof(byte[]), bytesLength, () => bytes, (o) => bytes = (byte[])o),
                };
            }
        }

        public override bool Equals(object obj) { return (obj as Sha256Hash).Operate((o) => o != null && Equals(o)); }

        public bool Equals(Sha256Hash other) { return bytes.BytesEquals(other.bytes); }

        public int CompareTo(object obj) { return bytes.BytesCompareTo((obj as Sha256Hash).bytes); }

        public int CompareTo(Sha256Hash other) { return bytes.BytesCompareTo(other.bytes); }

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
    }

    //<未改良>2014/05/03 Sha256HashとRipemd160hashの統合
    //　　　　共通の基底クラスを作る
    public class Ripemd160Hash : SHAREDDATA, IComparable<Ripemd160Hash>, IEquatable<Ripemd160Hash>, IComparable
    {
        public Ripemd160Hash()
        {
            bytes = new byte[bytesLength];
        }

        public Ripemd160Hash(string value) : this(value.FromHexstring()) { }

        public Ripemd160Hash(byte[] _bytes)
        {
            if (_bytes.Length != bytesLength)
                throw new ArgumentException("Ripemd160_bytes_length");

            bytes = _bytes;
        }

        private readonly int bytesLength = 20;

        public byte[] bytes { get; private set; }

        protected override Func<ReaderWriter, IEnumerable<MainDataInfomation>> StreamInfo
        {
            get
            {
                return (msrw) => new MainDataInfomation[]{
                    new MainDataInfomation(typeof(byte[]), bytesLength, () => bytes, (o) => bytes = (byte[])o),
                };
            }
        }

        public override bool Equals(object obj) { return (obj as Ripemd160Hash).Operate((o) => o != null && Equals(o)); }

        public bool Equals(Ripemd160Hash other) { return bytes.BytesEquals(other.bytes); }

        public int CompareTo(object obj) { return bytes.BytesCompareTo((obj as Ripemd160Hash).bytes); }

        public int CompareTo(Ripemd160Hash other) { return bytes.BytesCompareTo(other.bytes); }

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
                        byte[] hashBytes = hash.bytes;

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
}