using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace CREA2014
{
    #region ソケット通信

    public interface IChannel
    {
        byte[] ReadBytes();
        void WriteBytes(byte[] data);
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
                if (isClosed)
                    throw new ClosedException("channel_already_closed");

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
                if (isClosed)
                    throw new ClosedException("channel_already_closed");

                lock (writesLock)
                    writes.Enqueue(new WriteItem(id, data));

                areWrites.Set();
            };

            this.StartTask(() =>
            {
                while (!isClosed)
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

                        if (write == null)
                            break;

                        try
                        {
                            WriteBytesInner(BitConverter.GetBytes(write.id).Combine(write.data), false);
                        }
                        catch (Exception ex)
                        {
                            this.RaiseError("socket_channel_write".GetLogMessage(), 5, ex);

                            Failed(this, EventArgs.Empty);
                        }
                    }
                }

                rm.Dispose();
                ins.Close();
                //if (isocket.Connected)
                //    isocket.Shutdown(SocketShutdown.Both);
                isocket.Close();

                Closed(this, EventArgs.Empty);

                string.Join(":", ChannelAddressText, "write_thread_exit").ConsoleWriteLine();
            }, "socket_channel_write", "socket_channel_write");

            this.StartTask(() =>
            {
                while (!isClosed)
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

                                Task.Factory.StartNew(() => Sessioned(this, sc));
                            }
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
                                string.Join(":", ChannelAddressText, "timeout").ConsoleWriteLine();
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
                            else
                                throw ex;
                        }
                        else
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

                string.Join(":", ChannelAddressText, "read_thread_exit").ConsoleWriteLine();
            }, "socket_channel_read", "socket_channel_read");
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
            return _read(0);
        }

        public void WriteBytes(byte[] data)
        {
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

            //ソケットの閉鎖などはwriteスレッドで行う
            areWrites.Set();
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

                        DateTime connectedTime = DateTime.Now;

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

    #endregion

    #region P2P通信

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

            this.StartTask(() =>
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
                            ConnectionData cd = new ConnectionData(e);
                            AddConnection(cd);

                            e.Closed += (sender2, e2) =>
                            {
                                RemoveConnection(cd);
                                RegisterResult(e.aiteIpAddress, true);
                            };
                            e.Failed += (sender2, e2) => RegisterResult(e.aiteIpAddress, false);

                            OnAccepted(e);
                        };
                        ric.AcceptanceFailed += (sender, e) => RegisterResult(e, false);
                        ric.Failed += (sender, e) =>
                        {
                            throw new Exception("ric_failed");
                        };
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
            }, "node_start", "node_start");
        }

        protected SocketChannel Connect(IPAddress aiteIpAddress, ushort aitePortNumber)
        {
            SocketChannel sc = null;
            AutoResetEvent are = new AutoResetEvent(false);

            RealOutboundChannel roc = new RealOutboundChannel(aiteIpAddress, aitePortNumber, RsaKeySize.rsa2048, privateRsaParameters);
            roc.Connected += (sender, e) =>
            {
                ConnectionData cd = new ConnectionData(e);
                AddConnection(cd);

                e.Closed += (sender2, e2) =>
                {
                    RemoveConnection(cd);
                    RegisterResult(e.aiteIpAddress, true);
                };
                e.Failed += (sender2, e2) => RegisterResult(e.aiteIpAddress, false);

                sc = e;
                are.Set();
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
        protected abstract void InboundProtocol(SocketChannel sc, Action<string> _ConsoleWriteLine);
        protected abstract void OutboundProtocol(MessageBase[] messages, SocketChannel sc, Action<string> _ConsoleWriteLine);
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

        protected override void InboundProtocol(SocketChannel sc, Action<string> _ConsoleWriteLine)
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

        protected override void OutboundProtocol(MessageBase[] messages, SocketChannel sc, Action<string> _ConsoleWriteLine)
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

    #endregion
}