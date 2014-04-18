using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading;

namespace CREA2014
{
    public interface IChannel
    {
        byte[] ReadBytes();
        void WriteBytes(byte[] data);
    }

    public class SessionChannel : IChannel
    {
        public SessionChannel(Action<KeyValuePair<uint, byte[]>> __write)
        {
            _write = __write;
            id = BitConverter.ToUInt32(new byte[] { (byte)256.RandomNum(), (byte)256.RandomNum(), (byte)256.RandomNum(), (byte)256.RandomNum() }, 0);
        }

        public SessionChannel(uint _id)
        {
            id = _id;
        }

        private readonly Action<KeyValuePair<uint, byte[]>> _write;

        public readonly uint id;

        public event EventHandler Closed = delegate { };

        public byte[] ReadBytes()
        {
            throw new NotImplementedException();
        }

        public void WriteBytes(byte[] data)
        {
            _write(new KeyValuePair<uint, byte[]>(id, data));
        }

        public void Close()
        {
            Closed(this, EventArgs.Empty);
        }
    }

    public enum ChannelDirection { inbound, outbound }

    public class SocketChannel : IChannel
    {
        public SocketChannel(ISocket _isocket, INetworkStream _ins, RijndaelManaged _rm, ChannelDirection _direction)
        {
            if (_isocket.AddressFamily != AddressFamily.InterNetwork && _isocket.AddressFamily != AddressFamily.InterNetworkV6)
                throw new NotSupportedException("not_supported_socket");

            isocket = _isocket;
            ins = _ins;
            rm = _rm;
            direction = _direction;
        }

        private readonly int maxTimeout = 150;
        private readonly int maxBufferSize = 16384;
        private readonly int minBufferSize = 4096;
        private readonly int bufferSize = 1024;

        private readonly ISocket isocket;
        private readonly INetworkStream ins;
        private readonly RijndaelManaged rm;

        public readonly ChannelDirection direction;

        private readonly object isClosedLock = new object();
        public bool isClosed { get; private set; }
        private readonly object isSessionedLock = new object();
        public bool isSessioned { get; private set; }

        public event EventHandler Closed = delegate { };
        public event EventHandler Failed = delegate { };

        public IPAddress ZibunIpAddress
        {
            get { return ((IPEndPoint)isocket.LocalEndPoint).Address; }
        }

        public ushort ZibunPortNumber
        {
            get { return (ushort)((IPEndPoint)isocket.LocalEndPoint).Port; }
        }

        public string ZibunAddressText
        {
            get { return string.Join(":", ZibunIpAddress.ToString(), ZibunPortNumber.ToString()); }
        }

        public IPAddress AiteIpAddress
        {
            get { return ((IPEndPoint)isocket.RemoteEndPoint).Address; }
        }

        public ushort AitePortNumber
        {
            get { return (ushort)((IPEndPoint)isocket.RemoteEndPoint).Port; }
        }

        public string AiteAddressText
        {
            get { return string.Join(":", AiteIpAddress.ToString(), AitePortNumber.ToString()); }
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

        public bool CanEncrypt
        {
            get { return rm != null; }
        }

        public override string ToString() { return ChannelAddressText; }

        public byte[] ReadBytes()
        {
            if (isClosed)
                throw new InvalidOperationException("channel_already_closed");
            if (isSessioned)
                throw new InvalidOperationException("sessioned");

            return ReadBytesInnner(false);
        }

        public void WriteBytes(byte[] data)
        {
            if (isClosed)
                throw new InvalidOperationException("channel_already_closed");
            if (isSessioned)
                throw new InvalidOperationException("sessioned");

            WriteBytesInner(data, false);
        }

        private readonly object sessionsLock = new object();
        private List<SessionChannel> sessions;

        private readonly object writesLock = new object();
        private Queue<KeyValuePair<uint, byte[]>> writes;
        private AutoResetEvent areWrites;

        private Action<KeyValuePair<uint, byte[]>> _write;

        private readonly object readsLock = new object();
        private Queue<KeyValuePair<uint, AutoResetEvent>> reads;

        private Func<uint, byte[]> _read;

        public SessionChannel NewSession()
        {
            lock (isClosedLock)
            {
                if (isClosed)
                    throw new InvalidOperationException("channel_already_closed");

                bool isFirst = !isSessioned;

                isSessioned = true;

                if (isFirst)
                {
                    sessions = new List<SessionChannel>();
                    writes = new Queue<KeyValuePair<uint, byte[]>>();
                    areWrites = new AutoResetEvent(false);

                    _write = (kvp) =>
                    {
                        lock (writesLock)
                            writes.Enqueue(kvp);

                        areWrites.Set();
                    };

                    this.StartTask(() =>
                    {
                        while (true)
                        {
                            if (isClosed)
                                break;

                            areWrites.WaitOne();

                            if (isClosed)
                                break;

                            while (true)
                            {
                                KeyValuePair<uint, byte[]> write;
                                lock (writesLock)
                                {
                                    if (writes.Count == 0)
                                        break;
                                    write = writes.Dequeue();
                                }

                                WriteBytesInner(BitConverter.GetBytes(write.Key).Combine(write.Value), false);
                            }
                        }

                    }, "sessions_write", "sessions_write");

                    this.StartTask(() =>
                    {

                    }, "sessions_read", "sessions_read");
                }

                SessionChannel session = new SessionChannel(_write);
                session.Closed += (sender, e) =>
                {
                    lock (sessionsLock)
                        sessions.Remove(session);
                };

                lock (sessionsLock)
                    sessions.Add(session);

                return session;
            }
        }

        public void Close()
        {
            lock (isClosedLock)
            {
                if (isClosed)
                    throw new InvalidOperationException("channel_already_closed");

                isClosed = true;
            }

            areWrites.Set();

            //<未実装>セッションを停止する必要

            rm.Dispose();
            ins.Close();
            isocket.Dispose();

            Closed(this, EventArgs.Empty);
        }

        private readonly object ReadBytesInnnerLock = new object();
        private byte[] ReadBytesInnner(bool isCompressed)
        {
            lock (ReadBytesInnnerLock)
            {
                int headerBytesLength = 4 + 32 + 4;

                byte[] headerBytes = new byte[headerBytesLength];
                if (ins.Read(headerBytes, 0, headerBytesLength) != headerBytesLength)
                    throw new Exception("cant_read_header");
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
                            throw new Exception("read_overed");
                    }
                    readData = ms.ToArray();
                }

                if (!headerBytes.Skip(4).Take(32).ToArray().BytesEquals(new SHA256Managed().ComputeHash(readData)))
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
        }

        private readonly object WriteBytesInnerLock = new object();
        private void WriteBytesInner(byte[] data, bool isCompressed)
        {
            lock (WriteBytesInnerLock)
            {
                byte[] writeData = null;
                if (data.Length == 0)
                    writeData = new byte[] { };
                else
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

                ins.Write(BitConverter.GetBytes(data.Length).Combine(new SHA256Managed().ComputeHash(writeData), BitConverter.GetBytes(writeData.Length)), 0, 4 + 32 + 4);
                if (data.Length != 0)
                    ins.Write(writeData, 0, writeData.Length);
            }
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

            this.StartTask(() =>
            {
                try
                {
                    isocket = CreateISocket(ipAddress.AddressFamily);
                    isocket.ReceiveTimeout = receiveTimeout;
                    isocket.SendTimeout = sendTimeout;
                    isocket.ReceiveBufferSize = receiveBufferSize;
                    isocket.SendBufferSize = sendBufferSize;
                    isocket.Connect(ipAddress, portNumber);

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

                    Connected(this, new SocketChannel(isocket, ins, rm, ChannelDirection.outbound));
                }
                catch (Exception ex)
                {
                    this.RaiseError("outbound_chennel".GetLogMessage(), 5, ex);

                    if (isocket.Connected)
                        isocket.Shutdown(SocketShutdown.Both);
                    isocket.Close();

                    Failed(this, EventArgs.Empty);
                }
            }, "outbound_chennel", "outbound_chennel");
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

            this.StartTask(() =>
            {
                try
                {
                    isocket = CreateISocket(AddressFamily.InterNetwork);
                    isocket.Bind(new IPEndPoint(IPAddress.Any, portNumber));
                    isocket.Listen(backlog);

                    while (true)
                    {
                        ISocket isocket2 = isocket.Accept();

                        this.StartTask(() =>
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

                                Accepted(this, new SocketChannel(isocket2, ins, rm, ChannelDirection.inbound));
                            }
                            catch (Exception ex)
                            {
                                this.RaiseError("inbound_channel".GetLogMessage(), 5, ex);

                                if (isocket2.Connected)
                                    isocket2.Shutdown(SocketShutdown.Both);
                                isocket2.Close();
                            }
                        }, "inbound_channel", "inbound_channel");
                    }
                }
                catch (Exception ex)
                {
                    this.RaiseError("inbound_channels".GetLogMessage(), 5, ex);

                    EndAcceptance();

                    Failed(this, EventArgs.Empty);
                }
            }, "inbound_channels", "inbound_channels");
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
}