//がをがを～！
//2014/11/03 分割

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace CREA2014
{
    #region ソケット通信

    public interface IChannel
    {
        byte[] ReadBytes();
        void WriteBytes(byte[] data);
    }

    public enum ChannelDirection { inbound, outbound }

    //<未実装>IPv6対応
    public class SocketChannel : IChannel
    {
        public SocketChannel(ISocket _isocket, INetworkStream _ins, RijndaelManaged _rm, ChannelDirection _direction, DateTime _connectionTime)
        {
            if (_isocket.AddressFamily != AddressFamily.InterNetwork)
                throw new NotSupportedException("socket_channel_not_supported_socket");

            isocket = _isocket;
            ins = _ins;
            rm = _rm;
            direction = _direction;

            zibunIpAddress = ((IPEndPoint)isocket.LocalEndPoint).Address;
            zibunPortNumber = (ushort)((IPEndPoint)isocket.LocalEndPoint).Port;
            aiteIpAddress = ((IPEndPoint)isocket.RemoteEndPoint).Address;
            aitePortNumber = (ushort)((IPEndPoint)isocket.RemoteEndPoint).Port;

            if (isOutputClosed)
                Closed += (sender, e) => this.ConsoleWriteLine(string.Join(":", ChannelAddressText, "closed"));
            if (isOutputFailed)
                Failed += (sender, e) => this.ConsoleWriteLine(string.Join(":", ChannelAddressText, "failed"));
            if (isOutputSessioned)
                Sessioned += (sender, e) => this.ConsoleWriteLine(string.Join(":", ChannelAddressText, "sessioned"));

            _read = (id) =>
            {
                ReadItem read;
                lock (readsLock)
                    if ((read = reads.Where((a) => a.id == id).FirstOrDefault()) != null)
                    {
                        reads.Remove(read);
                        return read.data;
                    }
                    else
                        reads.Add(read = new ReadItem(id));

                if (read.are.WaitOne(30000))
                    if (read.data != null)
                        return read.data;
                    else
                        throw new ClosedException("socket_channel_outside_read_data_null");
                else
                {
                    lock (readsLock)
                        reads.Remove(read);

                    throw new TimeoutException("socket_chennel_outside_timeout");
                }
            };

            _write = (id, data) =>
            {
                lock (writesLock)
                    writes.Enqueue(new WriteItem(id, data));

                areWrites.Set();
            };

            this.StartTask(string.Join(":", "socket_channel_write", ChannelAddressText), "socket_channel_write", () =>
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

                            WriteBytesInner(write.id.ToByteArray().Combine(write.data), false);

                            if (isOutputWrite)
                                this.ConsoleWriteLine(string.Join(":", ChannelAddressText, write.id == endId ? "write_end" : "write", write.id.ToString()));

                            if (write.id == endId)
                            {
                                isWriteEnding = true;
                                break;
                            }
                        }
                    }

                    lock (endLock)
                    {
                        isWriteEnded = true;

                        ReleaseResources();
                    }

                    if (!isClosedOrFailed)
                    {
                        isClosedOrFailed = true;

                        Closed(this, EventArgs.Empty);
                    }
                }
                catch (Exception ex)
                {
                    this.RaiseError("socket_channel_inside_write", 5, ex);

                    if (isOutputWriteException)
                        this.ConsoleWriteLine(string.Join(":", ChannelAddressText, "write_exception"));

                    if (!isClosedOrFailed)
                    {
                        isClosedOrFailed = true;

                        Failed(this, EventArgs.Empty);
                    }

                    ReleaseResources();
                }
                finally
                {
                    this.ConsoleWriteLine(string.Join(":", ChannelAddressText, "write_thread_exit"));
                }
            });

            this.StartTask(string.Join(" ", "socket_channel_read", ChannelAddressText), "socket_channel_read", () =>
            {
                try
                {
                    while (!isReadEnding)
                    {
                        try
                        {
                            byte[] bytes = ReadBytesInnner(false);

                            Guid id = new Guid(bytes.Decompose(0, 16));
                            byte[] data = bytes.Decompose(16);

                            if (isOutputRead)
                                this.ConsoleWriteLine(string.Join(":", ChannelAddressText, id == endId ? "read_end" : "read", id.ToString()));

                            if (id != Guid.Empty)
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

                                    this.StartTask(string.Join(":", "session", ChannelAddressText), "session", () => Sessioned(this, sc));
                                }
                            }

                            if (id == endId)
                            {
                                isReadEnding = true;
                                continue;
                            }

                            lock (readsLock)
                            {
                                ReadItem read = reads.Where((e) => e.id == id && e.are != null).FirstOrDefault();
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
                                    this.ConsoleWriteLine(string.Join(":", ChannelAddressText, "read_timeout"));
                                    continue;
                                }
                                //else if (sex.ErrorCode == 10054)
                                //{
                                //    string.Join(":", ChannelAddressText, "connection_reset").ConsoleWriteLine();
                                //    break;
                                //}
                                //else if (sex.ErrorCode == 10053)
                                //{
                                //    string.Join(":", ChannelAddressText, "connection_aborted").ConsoleWriteLine();
                                //    break;
                                //}
                            }

                            throw new Exception(string.Empty, ex);
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
                        isReadEnded = true;

                        ReleaseResources();

                        _write(endId, new byte[] { });
                    }

                    if (!isClosedOrFailed)
                    {
                        isClosedOrFailed = true;

                        Closed(this, EventArgs.Empty);
                    }
                }
                catch (Exception ex)
                {
                    this.RaiseError("socket_channel_inside_read", 5, ex);

                    if (isOutputReadException)
                        this.ConsoleWriteLine(string.Join(":", ChannelAddressText, "read_exception"));

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

                    if (!isClosedOrFailed)
                    {
                        isClosedOrFailed = true;

                        Failed(this, EventArgs.Empty);
                    }

                    ReleaseResources();

                    _write(endId, new byte[] { });
                }
                finally
                {
                    this.ConsoleWriteLine(string.Join(":", ChannelAddressText, "read_thread_exit"));
                }
            });
        }

        private void ReleaseResources()
        {
            if (rm != null)
            {
                rm.Dispose();
                rm = null;

                if (isOutputReleasedRm)
                    this.ConsoleWriteLine("release_rm");
            }

            if (ins != null)
            {
                ins.Close();
                ins = null;

                if (isOutputReleasedIns)
                    this.ConsoleWriteLine("release_ins");
            }

            if (isocket != null)
            {
                if (isocket.Connected)
                    isocket.Shutdown(SocketShutdown.Both);
                isocket.Close();
                isocket = null;

                if (isOutputReleasedIsocket)
                    this.ConsoleWriteLine("release_isocket");
            }
        }

        private static readonly bool isOutputWrite = true;
        private static readonly bool isOutputRead = true;
        private static readonly bool isOutputWriteException = true;
        private static readonly bool isOutputReadException = true;
        private static readonly bool isOutputClosed = true;
        private static readonly bool isOutputFailed = true;
        private static readonly bool isOutputSessioned = true;
        private static readonly bool isOutputReleasedRm = true;
        private static readonly bool isOutputReleasedIns = true;
        private static readonly bool isOutputReleasedIsocket = true;

        private static readonly bool isOutputReadBytesInner = true;

        private static readonly int maxTimeout = 150;
        private static readonly int maxBufferSize = 16384;
        private static readonly int minBufferSize = 4096;
        private static readonly int bufferSize = 1024;

        private static readonly Guid endId = new Guid("11c78561e4671d48bd894553e09794a0");
        private static readonly Guid mainId = Guid.Empty;

        private readonly object sessionsLock = new object();
        private readonly List<SessionChannel> sessions = new List<SessionChannel>();
        private readonly object writesLock = new object();
        private readonly Queue<WriteItem> writes = new Queue<WriteItem>();
        private readonly AutoResetEvent areWrites = new AutoResetEvent(false);
        private readonly object readsLock = new object();
        private readonly List<ReadItem> reads = new List<ReadItem>();

        private ISocket isocket;
        private INetworkStream ins;
        private RijndaelManaged rm;
        private readonly Func<Guid, byte[]> _read;
        private readonly Action<Guid, byte[]> _write;

        private readonly object endLock = new object();
        private bool isWriteEnding;
        private bool isWriteEnded;
        private bool isReadEnding;
        private bool isReadEnded;
        private bool isClosedOrFailed;

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

        public class TimeoutException : Exception { public TimeoutException(string _message) : base(_message) { } }
        public class ClosedException : Exception { public ClosedException(string _message) : base(_message) { } }

        public class WriteItem
        {
            public WriteItem(byte[] _data) : this(mainId, _data) { }

            public WriteItem(Guid _id, byte[] _data)
            {
                id = _id;
                data = _data;
            }

            public Guid id { get; private set; }
            public byte[] data { get; private set; }
        }

        public class ReadItem
        {
            public ReadItem() : this(mainId) { }

            public ReadItem(Guid _id)
            {
                id = _id;
                are = new AutoResetEvent(false);
            }

            public ReadItem(byte[] _data) : this(mainId, _data) { }

            public ReadItem(Guid _id, byte[] _data)
            {
                id = _id;
                data = _data;
            }

            public Guid id { get; private set; }
            public AutoResetEvent are { get; private set; }
            public byte[] data { get; private set; }

            public void SetData(byte[] _data) { data = _data; }
        }

        public string ZibunAddressText { get { return string.Join(":", zibunIpAddress.ToString(), zibunPortNumber.ToString()); } }

        public string AiteAddressText { get { return string.Join(":", aiteIpAddress.ToString(), aitePortNumber.ToString()); } }

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
                if (value < minBufferSize)
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
                if (value < minBufferSize)
                    throw new ArgumentOutOfRangeException("min_buffer_size");

                isocket.SendBufferSize = value;
            }
        }

        public TimeSpan Duration { get { return DateTime.Now - connectionTime; } }

        public bool CanEncrypt { get { return rm != null; } }

        private bool isEncrypted = true;
        public bool IsEncrypted
        {
            get
            {
                if (!CanEncrypt)
                    throw new InvalidOperationException("cant_encrypt");
                return isEncrypted;
            }
            set
            {
                if (!CanEncrypt)
                    throw new InvalidOperationException("cant_encrypt");
                isEncrypted = value;
            }
        }

        public override string ToString() { return ChannelAddressText; }

        public byte[] ReadBytes()
        {
            if (isClosed)
                throw new ClosedException("channel_already_closed");

            return _read(mainId);
        }

        public void WriteBytes(byte[] data)
        {
            if (isClosed)
                throw new ClosedException("channel_already_closed");

            _write(mainId, data);
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

            _write(endId, new byte[] { });
        }

        private byte[] ReadBytesInnner(bool isCompressed)
        {
            int headerBytesLength = 4 + 32 + 4;

            byte[] headerBytes = new byte[headerBytesLength];

            for (int i = 0; i < 100; i++)
            {
                int l = ins.Read(headerBytes, 0, headerBytesLength);

                if (isOutputReadBytesInner)
                    this.ConsoleWriteLine(l.ToString());

                if (l == 0)
                    continue;
                else if (l == headerBytesLength)
                    break;
                else
                    throw new InvalidDataException("cant_read_header");
            }

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
        public SessionChannel(Func<Guid, byte[]> __read, Action<Guid, byte[]> __write)
        {
            //while (id == 0 || id == uint.MaxValue)
            //    id = BitConverter.ToUInt32(new byte[] { (byte)256.RandomNum(), (byte)256.RandomNum(), (byte)256.RandomNum(), (byte)256.RandomNum() }, 0);

            id = Guid.NewGuid();

            _read = __read;
            _write = __write;
        }

        public SessionChannel(Guid _id, Func<Guid, byte[]> __read, Action<Guid, byte[]> __write)
        {
            id = _id;
            _read = __read;
            _write = __write;
        }

        private readonly Func<Guid, byte[]> _read;
        private readonly Action<Guid, byte[]> _write;

        public Guid id { get; private set; }

        public event EventHandler Closed = delegate { };

        public byte[] ReadBytes() { return _read(id); }

        public void WriteBytes(byte[] data) { _write(id, data); }

        public void Close() { Closed(this, EventArgs.Empty); }
    }

    public interface INetworkStream
    {
        void Write(byte[] buffer, int offset, int size);
        int Read(byte[] buffer, int offset, int size);
        void Close();
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

    public class RealNetworkStream : INetworkStream
    {
        public RealNetworkStream(NetworkStream _ns) { ns = _ns; }

        private readonly NetworkStream ns;

        public void Write(byte[] buffer, int offset, int size) { ns.Write(buffer, offset, size); }
        public int Read(byte[] buffer, int offset, int size) { return ns.Read(buffer, offset, size); }
        public void Close() { ns.Close(); }
    }

    public class RealSocket : ISocket
    {
        public RealSocket(Socket _socket) { socket = _socket; }

        public Socket socket { get; private set; }

        public AddressFamily AddressFamily { get { return socket.AddressFamily; } }
        public EndPoint LocalEndPoint { get { return socket.LocalEndPoint; } }
        public EndPoint RemoteEndPoint { get { return socket.RemoteEndPoint; } }
        public bool Connected { get { return socket.Connected; } }

        public int ReceiveTimeout { get { return socket.ReceiveTimeout; } set { socket.ReceiveTimeout = value; } }
        public int SendTimeout { get { return socket.SendTimeout; } set { socket.SendTimeout = value; } }
        public int ReceiveBufferSize { get { return socket.ReceiveBufferSize; } set { socket.ReceiveBufferSize = value; } }
        public int SendBufferSize { get { return socket.SendBufferSize; } set { socket.SendBufferSize = value; } }

        public void Connect(IPAddress ipAddress, ushort portNumber) { socket.Connect(ipAddress, (int)portNumber); }
        public void Bind(IPEndPoint localEP) { socket.Bind(localEP); }
        public void Listen(int backlog) { socket.Listen(backlog); }
        public ISocket Accept() { return new RealSocket(socket.Accept()); }
        public void Shutdown(SocketShutdown how) { socket.Shutdown(how); }
        public void Close() { socket.Close(); }
        public void Dispose() { socket.Dispose(); }
    }

    public enum RsaKeySize { rsa1024, rsa2048 }

    //<未実装>IPv6対応
    public abstract class OutboundChannelBase
    {
        public OutboundChannelBase(IPAddress _ipAddress, ushort _portNumber) : this(_ipAddress, _portNumber, RsaKeySize.rsa2048, null) { }

        public OutboundChannelBase(IPAddress _ipAddress, ushort _portNumber, RsaKeySize _rsaKeySize, string _privateRsaParameters)
        {
            if (_ipAddress.AddressFamily != AddressFamily.InterNetwork)
                throw new NotSupportedException("not_supported_address");

            ipAddress = _ipAddress;
            portNumber = _portNumber;
            rsaKeySize = _rsaKeySize;
            privateRsaParameters = _privateRsaParameters;
        }

        private static readonly int receiveTimeout = 30000;
        private static readonly int sendTimeout = 30000;
        private static readonly int receiveBufferSize = 8192;
        private static readonly int sendBufferSize = 8192;

        private readonly IPAddress ipAddress;
        private readonly ushort portNumber;
        private readonly string privateRsaParameters;
        private readonly RsaKeySize rsaKeySize;

        private ISocket isocket;
        private INetworkStream ins;
        private SocketChannel sc;

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

                    ins = CreateINetworkStream(isocket);

                    int version = 0;

                    ins.Write(BitConverter.GetBytes(version), 0, 4);

                    if (version == 0)
                    {
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

                        sc = new SocketChannel(isocket, ins, rm, ChannelDirection.outbound, connectionTime);

                        //2014/09/15
                        //SocketChannelは使用後Closeを呼び出さなければならない
                        //このイベントの処理中に例外が発生する可能性もある
                        //このイベントの処理スレッドで例外が発生した場合には例外が捕捉され、Closeが呼び出される
                        //SocketChannelをこのイベントの処理スレッドでしか使用しない場合には、イベントハンドラのどこかでCloseを呼び出さなければならない
                        //SocketChannelを別のスレッドで使用する場合には、例外が発生した場合も含めて、必ずCloseが呼び出されるようにしなければならない
                        Connected(this, sc);
                    }
                    else
                        throw new NotSupportedException("outbound_connection_version");
                }
                catch (Exception ex)
                {
                    this.RaiseError("outbound_chennel", 5, ex);

                    //SocketChannelのCloseを呼び出すとINetworkStreamやISocketのCloseも呼び出される筈
                    if (sc != null)
                        sc.Close();
                    else
                    {
                        if (ins != null)
                            ins.Close();
                        if (isocket != null)
                        {
                            if (isocket.Connected)
                                isocket.Shutdown(SocketShutdown.Both);
                            isocket.Close();
                        }
                    }

                    Failed(this, EventArgs.Empty);
                }
            });
        }
    }

    public class RealOutboundChannel : OutboundChannelBase
    {
        public RealOutboundChannel(IPAddress _ipAddress, ushort _portNumber) : this(_ipAddress, _portNumber, RsaKeySize.rsa2048, null) { }

        public RealOutboundChannel(IPAddress _ipAddress, ushort _portNumber, RsaKeySize _rsaKeySize, string _privateRsaParameters) : base(_ipAddress, _portNumber, _rsaKeySize, _privateRsaParameters) { }

        protected override ISocket CreateISocket(AddressFamily _addressFamily) { return new RealSocket(new Socket(_addressFamily, SocketType.Stream, ProtocolType.Tcp)); }
        protected override INetworkStream CreateINetworkStream(ISocket socket) { return new RealNetworkStream(new NetworkStream(((RealSocket)socket).socket)); }
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

        private static readonly int receiveTimeout = 30000;
        private static readonly int sendTimeout = 30000;
        private static readonly int receiveBufferSize = 8192;
        private static readonly int sendBufferSize = 8192;

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
                        ISocket isocket2 = null;
                        INetworkStream ins = null;
                        SocketChannel sc = null;

                        try
                        {
                            isocket2 = isocket.Accept();

                            DateTime connectedTime = DateTime.Now;

                            this.StartTask("inbound_channel", "inbound_channel", () =>
                            {
                                try
                                {
                                    isocket2.ReceiveTimeout = receiveTimeout;
                                    isocket2.SendTimeout = sendTimeout;
                                    isocket2.ReceiveBufferSize = receiveBufferSize;
                                    isocket2.SendBufferSize = sendBufferSize;

                                    ins = CreateINetworkStream(isocket2);

                                    byte[] versionBytes = new byte[4];
                                    ins.Read(versionBytes, 0, versionBytes.Length);
                                    int version = BitConverter.ToInt32(versionBytes, 0);

                                    if (version == 0)
                                    {
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

                                        sc = new SocketChannel(isocket2, ins, rm, ChannelDirection.inbound, connectedTime);

                                        //2014/09/15
                                        //SocketChannelは使用後Closeを呼び出さなければならない
                                        //このイベントの処理中に例外が発生する可能性もある
                                        //このイベントの処理スレッドで例外が発生した場合には例外が捕捉され、Closeが呼び出される
                                        //SocketChannelをこのイベントの処理スレッドでしか使用しない場合には、イベントハンドラのどこかでCloseを呼び出さなければならない
                                        //SocketChannelを別のスレッドで使用する場合には、例外が発生した場合も含めて、必ずCloseが呼び出されるようにしなければならない
                                        Accepted(this, sc);
                                    }
                                    else
                                        throw new NotSupportedException("inbound_connection_version");
                                }
                                catch (Exception ex)
                                {
                                    this.RaiseError("inbound_channel", 5, ex);

                                    //SocketChannelのCloseを呼び出すとINetworkStreamやISocketのCloseも呼び出される筈
                                    if (sc != null)
                                        sc.Close();
                                    else
                                    {
                                        if (ins != null)
                                            ins.Close();
                                        if (isocket2 != null)
                                        {
                                            if (isocket2.Connected)
                                                isocket2.Shutdown(SocketShutdown.Both);
                                            isocket2.Close();
                                        }
                                    }

                                    AcceptanceFailed(this, ((IPEndPoint)isocket.RemoteEndPoint).Address);
                                }
                            });
                        }
                        catch (Exception ex)
                        {
                            if (isocket2 != null)
                                isocket2.Close();

                            throw ex;
                        }
                    }
                }
                catch (Exception ex)
                {
                    this.RaiseError("inbound_channels", 5, ex);

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

            if (isocket != null)
                isocket.Close();
        }
    }

    public class RealInboundChennel : InboundChannelsBase
    {
        public RealInboundChennel(ushort _portNumber, int _backlog) : base(_portNumber, _backlog) { }

        public RealInboundChennel(ushort _portNumber, RsaKeySize _rsaKeySize, int _backlog) : base(_portNumber, _rsaKeySize, _backlog) { }

        protected override ISocket CreateISocket(AddressFamily _addressFamily) { return new RealSocket(new Socket(_addressFamily, SocketType.Stream, ProtocolType.Tcp)); }
        protected override INetworkStream CreateINetworkStream(ISocket socket) { return new RealNetworkStream(new NetworkStream(((RealSocket)socket).socket)); }
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

        public TimeSpan Duration { get { return DateTime.Now - connectionTime; } }
    }

    public class ConnectionHistory
    {
        public ConnectionHistory(IPAddress _ipAddress)
        {
            ipAddress = _ipAddress;

            failureTime = new List<DateTime>();
            failureTimeCache = new CachedData<DateTime[]>(() =>
            {
                lock (failureTimeLock)
                    return failureTime.ToArray();
            });
        }

        public readonly IPAddress ipAddress;

        public int success { get; private set; }
        public int failure { get; private set; }

        private readonly object failureTimeLock = new object();
        private readonly List<DateTime> failureTime;
        private CachedData<DateTime[]> failureTimeCache;
        public DateTime[] FailureTime { get { return failureTimeCache.Data; } }

        public int Connection { get { return success + failure; } }

        public double SuccessRate { get { return (double)success / (double)Connection; } }

        public double FailureRate { get { return (double)failure / (double)Connection; } }

        public bool IsDead
        {
            get
            {
                lock (failureTimeLock)
                    return failureTime.Count >= 2 && failureTime[1] - failureTime[0] <= new TimeSpan(0, 5, 0) && DateTime.Now - failureTime[1] <= new TimeSpan(0, 15, 0);
            }
        }

        public bool IsBad { get { return Connection > 10 && FailureRate > 0.9; } }

        public void IncrementSuccess()
        {
            success++;

            lock (failureTimeLock)
            {
                failureTime.Clear();
                failureTimeCache.IsModified = true;
            }
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
                failureTimeCache.IsModified = true;
            }
        }
    }

    #endregion

    #region P2Pネットワーク

    public abstract class P2PNODE
    {
        public P2PNODE() : this(0) { }

        public P2PNODE(ushort _portNumber)
        {
            myPortNumber = _portNumber;

            connections = new List<ConnectionData>();
            connectionsCache = new CachedData<ConnectionData[]>(() =>
            {
                lock (connectionsLock)
                    return connections.ToArray();
            });

            connectionHistories = new List<ConnectionHistory>();
            connectionHistoriesCache = new CachedData<ConnectionHistory[]>(() =>
            {
                lock (connectionHistoriesLock)
                    return connectionHistories.ToArray();
            });
        }

        public IPAddress myIpAddress { get; private set; }
        public ushort myPortNumber { get; private set; }
        public FirstNodeInformation myFirstNodeInfo { get; private set; }

        private readonly object isStartedLock = new object();
        public bool isStarted { get; private set; }
        public bool isStartCompleted { get; private set; }

        protected string myPrivateRsaParameters { get; private set; }
        protected FirstNodeInformation[] firstNodeInfos { get; private set; }

        private readonly object connectionsLock = new object();
        private readonly List<ConnectionData> connections;
        private readonly CachedData<ConnectionData[]> connectionsCache;
        public ConnectionData[] Connections { get { return connectionsCache.Data; } }

        private readonly object connectionHistoriesLock = new object();
        private readonly List<ConnectionHistory> connectionHistories;
        private readonly CachedData<ConnectionHistory[]> connectionHistoriesCache;
        public ConnectionHistory[] ConnectionHistories { get { return connectionHistoriesCache.Data; } }

        public event EventHandler StartCompleted = delegate { };
        public event EventHandler ConnectionAdded = delegate { };
        public event EventHandler ConnectionRemoved = delegate { };

        protected abstract Network Network { get; }

        protected abstract string GetPrivateRsaParameters();
        protected abstract IPAddress GetIpAddressAndOpenPort();
        protected abstract void NotifyFirstNodeInfo();
        protected abstract void CreateNodeInfo();
        protected abstract FirstNodeInformation[] GetFirstNodeInfos();
        protected abstract void KeepConnections();
        protected abstract void OnAccepted(SocketChannel sc);

        public bool IsPort0 { get { return myPortNumber == 0; } }
        public bool IsServer { get { return !IsPort0 && myIpAddress != null; } }

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
                if ((myPrivateRsaParameters = GetPrivateRsaParameters()) == null)
                    throw new CryptographicException("cant_create_key_pair");

                if (IsPort0)
                    this.RaiseNotification("port0", 5);
                else
                {
                    //IPアドレスの取得やポートの開放には時間が掛かる可能性がある
                    myIpAddress = GetIpAddressAndOpenPort();

                    if (!IsServer)
                        this.RaiseNotification("not_server", 5);
                    else
                    {
                        myFirstNodeInfo = new FirstNodeInformation(myIpAddress, myPortNumber, Network);

                        NotifyFirstNodeInfo();

                        CreateNodeInfo();

                        RealInboundChennel ric = new RealInboundChennel(myPortNumber, RsaKeySize.rsa2048, 100);
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

                                //2014/09/15
                                //SocketChannelは使用後Closeを呼び出さなければならない
                                //このイベントの処理スレッドで例外が発生した場合には例外が捕捉され、Closeが呼び出される
                                //SocketChannelをこのイベントの処理スレッドでしか使用しない場合には、イベントハンドラのどこかでCloseを呼び出さなければならない
                                //この場合はOnAcceptedのどこかで呼び出さなければならない
                                //SocketChannelを別のスレッドで使用する場合には、例外が発生した場合も含めて、必ずCloseが呼び出されるようにしなければならない
                                OnAccepted(e);
                            }
                            catch (Exception ex)
                            {
                                this.RaiseError("ric", 5, ex);

                                //別スレッドでないので例外を再スローすればCloseが呼び出されるのだが、
                                //ここで呼んでも良いだろう
                                e.Close();
                            }
                        };
                        ric.AcceptanceFailed += (sender, e) => RegisterResult(e, false);
                        ric.Failed += (sender, e) => { };
                        ric.RequestAcceptanceStart();

                        this.RaiseNotification("server_started", 5, myIpAddress.ToString(), myPortNumber.ToString());
                    }
                }

                if (myFirstNodeInfo == null)
                    firstNodeInfos = GetFirstNodeInfos();
                else
                    firstNodeInfos = GetFirstNodeInfos().Where((a) => !a.Equals(myFirstNodeInfo)).ToArray();

                KeepConnections();

                isStartCompleted = true;

                StartCompleted(this, EventArgs.Empty);
            });
        }

        protected SocketChannel Connect(IPAddress aiteIpAddress, ushort aitePortNumber)
        {
            SocketChannel sc = null;
            AutoResetEvent are = new AutoResetEvent(false);

            RealOutboundChannel roc = new RealOutboundChannel(aiteIpAddress, aitePortNumber, RsaKeySize.rsa2048, myPrivateRsaParameters);
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

                    //2014/09/15
                    //SocketChannelは使用後Closeを呼び出さなければならない
                    //このイベントの処理スレッドで例外が発生した場合には例外が捕捉され、Closeが呼び出される
                    //SocketChannelをこのイベントの処理スレッドでしか使用しない場合には、イベントハンドラのどこかでCloseを呼び出さなければならない
                    //SocketChannelを別のスレッドで使用する場合には、例外が発生した場合も含めて、必ずCloseが呼び出されるようにしなければならない
                    //この場合はこのメソッドの呼び出し元が、必ずCloseが呼び出されるようにしなければならない
                    sc = e;
                }
                catch (Exception ex)
                {
                    this.RaiseError("roc", 5, ex);

                    //別スレッドでないので例外を再スローすればCloseが呼び出されるのだが、
                    //ここで呼んでも良いだろう
                    e.Close();
                }
                finally
                {
                    //2014/10/04
                    //例外が発生した場合には30秒も待機すべきではないだろう
                    are.Set();
                }
            };
            roc.Failed += (sender, e) =>
            {
                RegisterResult(aiteIpAddress, false);

                //2014/09/27
                //失敗が確定しているのに30秒も待機すべきではないだろう
                are.Set();
            };
            roc.RequestConnection();

            if (are.WaitOne(30000))
            {
                if (sc == null)
                    throw new Exception("cant_connect");

                return sc;
            }
            else
                throw new Exception("cant_connect_timeout");
        }

        private void AddConnection(ConnectionData cd)
        {
            lock (connectionsLock)
            {
                if (connections.Contains(cd))
                    throw new InvalidOperationException("exist_connection");

                this.ExecuteBeforeEvent(() =>
                {
                    connections.Add(cd);
                    connectionsCache.IsModified = true;
                }, ConnectionAdded);
            }
        }

        private void RemoveConnection(ConnectionData connection)
        {
            lock (connectionsLock)
            {
                if (!connections.Contains(connection))
                    throw new InvalidOperationException("not_exist_connection");

                this.ExecuteBeforeEvent(() =>
                {
                    connections.Remove(connection);
                    connectionsCache.IsModified = true;
                }, ConnectionRemoved);
            }
        }

        private void RegisterResult(IPAddress ipAddress, bool isSucceeded)
        {
            ConnectionHistory connectionHistory;
            lock (connectionHistoriesLock)
                if ((connectionHistory = connectionHistories.Where((e) => e.ipAddress.Equals(ipAddress)).FirstOrDefault()) == null)
                {
                    connectionHistories.Add(connectionHistory = new ConnectionHistory(ipAddress));
                    connectionHistoriesCache.IsModified = true;
                }

            if (isSucceeded)
                connectionHistory.IncrementSuccess();
            else
                connectionHistory.IncrementFailure();
        }
    }

    #endregion

    #region CREAネットワーク

    public enum Network { localtest = 0, globaltest = 1, global = 2 }

    public enum MessageName
    {
        reqNodeInfos = 1,

        notifyNewTransaction = 10,
        reqTransactions = 11,
        notifyNewBlock = 12,
        reqBlocks = 13,

        PingReq = 100,
        PingRes = 101,
        StoreReq = 102,
        FindNodesReq = 103,
        NeighborNodes = 104,
        FindvalueReq = 105,
        Value = 106,
        GetIdsAndValuesReq = 107,
        IdsAndValues = 108,

        NotifyNewChat = 1000,
    }

    public class Message : SHAREDDATA
    {
        public Message() : base(0) { }

        public Message(MessageName _name, int _version)
            : base(0)
        {
            name = _name;
            version = _version;
        }

        public MessageName name { get; private set; }
        public int version { get; private set; }

        protected override Func<ReaderWriter, IEnumerable<MainDataInfomation>> StreamInfo
        {
            get
            {
                if (Version == 0)
                    return (msrw) => new MainDataInfomation[]{
                        new MainDataInfomation(typeof(int), () => (int)name, (o) => name = (MessageName)o),
                        new MainDataInfomation(typeof(int), () => version, (o) => version = (int)o),
                    };
                else
                    throw new NotSupportedException("node_infos_main_data_info");
            }
        }
        public override bool IsVersioned { get { return true; } }
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

    public class ResNodeInfos : SHAREDDATA
    {
        public ResNodeInfos() : base(0) { }

        public ResNodeInfos(NodeInformation[] _nodeInfos) : base(0) { nodeInfos = _nodeInfos; }

        public NodeInformation[] nodeInfos { get; private set; }

        protected override Func<ReaderWriter, IEnumerable<MainDataInfomation>> StreamInfo
        {
            get
            {
                if (Version == 0)
                    return (msrw) => new MainDataInfomation[]{
                        new MainDataInfomation(typeof(NodeInformation[]), 0, null, () => nodeInfos, (o) => nodeInfos = (NodeInformation[])o),
                    };
                else
                    throw new NotSupportedException("node_infos_main_data_info");
            }
        }
        public override bool IsVersioned { get { return true; } }
        public override bool IsCorruptionChecked
        {
            get
            {
                if (Version <= 0)
                    return false;
                else
                    throw new NotSupportedException("node_infos_check");
            }
        }
    }

    public abstract class MessageHash<HashType> : SHAREDDATA where HashType : HASHBASE
    {
        public MessageHash(HashType _hash) : this(null, _hash) { }

        public MessageHash(int? _version, HashType _hash) : base(_version) { hash = _hash; }

        public HashType hash { get; private set; }

        protected override Func<ReaderWriter, IEnumerable<MainDataInfomation>> StreamInfo
        {
            get
            {
                return (msrw) => new MainDataInfomation[]{
                    new MainDataInfomation(typeof(HashType), null, () => hash, (o) => hash = (HashType)o),
                };
            }
        }
    }

    public abstract class MessageHashes<HashType> : SHAREDDATA where HashType : HASHBASE
    {
        public MessageHashes(HashType[] _hashes) : this(null, _hashes) { }

        public MessageHashes(int? _version, HashType[] _hashes) : base(_version) { hashes = _hashes; }

        public HashType[] hashes { get; private set; }

        protected override Func<ReaderWriter, IEnumerable<MainDataInfomation>> StreamInfo
        {
            get
            {
                return (msrw) => new MainDataInfomation[]{
                    new MainDataInfomation(typeof(HashType[]), null, null, () => hashes, (o) => hashes = (HashType[])o),
                };
            }
        }
    }

    public abstract class MessageGuid : SHAREDDATA
    {
        public MessageGuid(Guid _id) : this(null, _id) { }

        public MessageGuid(int? _version, Guid _id) : base(_version) { Id = _id; }

        public Guid Id { get; private set; }

        protected override Func<ReaderWriter, IEnumerable<MainDataInfomation>> StreamInfo
        {
            get
            {
                return (msrw) => new MainDataInfomation[]{
//					new MainDataInfomation(typeof(Guid), null, () => Id, (o) => Id = (Guid)o),
					new MainDataInfomation(typeof(Byte[]), 16, () => this.Id.ToByteArray(), (o) => this.Id = new Guid((byte[])o))
				};
            }
        }
    }

    public class NotifyNewChat : MessageGuid
    {
        public NotifyNewChat() : this(new Guid()) { }

        public NotifyNewChat(Guid _id) : base(0, _id) { }

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
        public override bool IsVersioned { get { return true; } }
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

    public class NotifyNewTransaction : MessageHash<Sha256Sha256Hash>
    {
        public NotifyNewTransaction() : this(new Sha256Sha256Hash()) { }

        public NotifyNewTransaction(Sha256Sha256Hash _hash) : base(0, _hash) { }

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
        public override bool IsVersioned { get { return true; } }
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

    public class ReqTransactions : MessageHashes<Sha256Sha256Hash>
    {
        public ReqTransactions() : this(new Sha256Sha256Hash[] { }) { }

        public ReqTransactions(Sha256Sha256Hash[] _hashes) : base(0, _hashes) { }

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
        public override bool IsVersioned { get { return true; } }
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

    public class ResTransaction : SHAREDDATA
    {
        public ResTransaction() : base(0) { }

        public ResTransaction(Transaction _transaction) : base(0) { transaction = _transaction; }

        public Transaction transaction { get; private set; }

        protected override Func<ReaderWriter, IEnumerable<MainDataInfomation>> StreamInfo
        {
            get
            {
                if (Version == 0)
                    return (msrw) => new MainDataInfomation[]{
                        new MainDataInfomation(typeof(Transaction), 0, null, () => transaction, (o) => transaction = (Transaction)o),
                    };
                else
                    throw new NotSupportedException("res_tx_main_data_info");
            }
        }
        public override bool IsVersioned { get { return true; } }
        public override bool IsCorruptionChecked
        {
            get
            {
                if (Version <= 0)
                    return false;
                else
                    throw new NotSupportedException("res_tx_check");
            }
        }
    }

    public class ResTransactions : SHAREDDATA
    {
        public ResTransactions() : base(0) { }

        public ResTransactions(Transaction[] _transactions) : base(0) { transactions = _transactions; }

        public Transaction[] transactions { get; private set; }

        protected override Func<ReaderWriter, IEnumerable<MainDataInfomation>> StreamInfo
        {
            get
            {
                if (Version == 0)
                    return (msrw) => new MainDataInfomation[]{
                        new MainDataInfomation(typeof(Transaction[]), 0, null, () => transactions, (o) => transactions = (Transaction[])o),
                    };
                else
                    throw new NotSupportedException("res_txs_main_data_info");
            }
        }
        public override bool IsVersioned { get { return true; } }
        public override bool IsCorruptionChecked
        {
            get
            {
                if (Version <= 0)
                    return false;
                else
                    throw new NotSupportedException("res_txs_check");
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

        //サーバが起動していない場合（ポート0又はIPアドレスが取得できなかったような場合）にはノード情報はnull
        public NodeInformation nodeInfo { get; private set; }
        public int creaVersion { get; private set; }
        public int protocolVersion { get; private set; }
        public string client { get; private set; }
        public bool isTemporary { get; private set; }

        protected override Func<ReaderWriter, IEnumerable<MainDataInfomation>> StreamInfo { get { return (msrw) => StreamInfoInner; } }
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
        public override bool IsVersioned { get { return true; } }
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

        protected override Func<ReaderWriter, IEnumerable<MainDataInfomation>> StreamInfo { get { return (msrw) => StreamInfoInner; } }
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
        public override bool IsVersioned { get { return true; } }
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

    public class PingReq : SHAREDDATA
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

    public class PingRes : SHAREDDATA
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

    public class StoreReq<IdType> : SHAREDDATA where IdType : HASHBASE
    {
        public StoreReq(IdType _id, byte[] _data)
            : base(0)
        {
            id = _id;
            data = _data;
        }

        public IdType id { get; private set; }
        public byte[] data { get; private set; }

        protected override Func<ReaderWriter, IEnumerable<MainDataInfomation>> StreamInfo
        {
            get
            {
                if (Version == 0)
                    return (msrw) => new MainDataInfomation[]{
                        new MainDataInfomation(typeof(IdType), null, () => id, (o) => id = (IdType)o),
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

    public class FindNodesReq<IdType> : SHAREDDATA where IdType : HASHBASE
    {
        public FindNodesReq(IdType _id)
            : base(0)
        {
            id = _id;
        }

        public IdType id { get; private set; }

        protected override Func<ReaderWriter, IEnumerable<MainDataInfomation>> StreamInfo
        {
            get
            {
                if (Version == 0)
                    return (msrw) => new MainDataInfomation[]{
                        new MainDataInfomation(typeof(IdType), null, () => id, (o) => id = (IdType)o),
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

    public class NeighborNodes : SHAREDDATA
    {
        public NeighborNodes() : base(0) { }

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

    public class FindValueReq<IdType> : SHAREDDATA where IdType : HASHBASE
    {
        public FindValueReq(IdType _id)
            : base(0)
        {
            id = _id;
        }

        public IdType id { get; private set; }

        protected override Func<ReaderWriter, IEnumerable<MainDataInfomation>> StreamInfo
        {
            get
            {
                if (Version == 0)
                    return (msrw) => new MainDataInfomation[] {
                        new MainDataInfomation(typeof(IdType), null, () => id, (o) => id = (IdType)o),
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

    public class Value : SHAREDDATA
    {
        public Value() : base(0) { }

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

    public class GetIdsAndValuesReq : SHAREDDATA
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

    public class IdsAndValues<IdType> : SHAREDDATA where IdType : HASHBASE
    {
        public IdsAndValues() : base(0) { }

        private IdType[] ids;
        public IdType[] Ids
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
                        new MainDataInfomation(typeof(IdType[]), null, null, () => ids, (o) => ids = (IdType[])o),
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

    public abstract class CREANODEBASE : P2PNODE
    {
        public CREANODEBASE(ushort _portNumber, int _creaVersion, string _appnameWithVersion)
            : base(_portNumber)
        {
            creaVersion = _creaVersion;
            appnameWithVersion = _appnameWithVersion;
        }

        private static readonly int protocolVersion = 0;

        protected readonly int creaVersion;
        protected readonly string appnameWithVersion;

        public NodeInformation myNodeInfo { get; private set; }

        protected abstract bool IsContinue { get; }
        protected abstract bool IsTemporaryContinue { get; }

        protected abstract bool IsAlreadyConnected(NodeInformation nodeInfo);
        protected abstract void UpdateNodeState(NodeInformation nodeInfo, bool isSucceeded);
        protected abstract void UpdateNodeState(IPAddress ipAddress, ushort portNumber, bool isSucceeded);
        protected abstract bool IsListenerCanContinue(NodeInformation nodeInfo);
        protected abstract bool IsWantToContinue(NodeInformation nodeInfo);
        protected abstract bool IsClientCanContinue(NodeInformation nodeInfo);
        protected abstract void InboundProtocol(NodeInformation nodeInfo, IChannel sc, Action<string> _ConsoleWriteLine);
        protected abstract SHAREDDATA[] OutboundProtocol(NodeInformation nodeInfo, Message message, SHAREDDATA[] datas, IChannel sc, Action<string> _ConsoleWriteLine);
        protected abstract void InboundContinue(NodeInformation nodeInfo, SocketChannel sc, Action<string> _ConsoleWriteLine);
        protected abstract void OutboundContinue(NodeInformation nodeInfo, SocketChannel sc, Action<string> _ConsoleWriteLine);
        protected abstract SHAREDDATA[] Request(NodeInformation nodeinfo, Message message, params SHAREDDATA[] datas);
        protected abstract void Diffuse(NodeInformation source, Message message, params SHAREDDATA[] datas);

        protected override void CreateNodeInfo()
        {
            using (RSACryptoServiceProvider rsacsp = new RSACryptoServiceProvider(2048))
            {
                rsacsp.FromXmlString(myPrivateRsaParameters);
                myNodeInfo = new NodeInformation(myIpAddress, myPortNumber, Network, rsacsp.ToXmlString(false));
            }
        }

        //このメソッドのどこかで（例外を除く全ての場合において）SocketChannelのCloseが呼び出されるようにしなければならない
        protected override void OnAccepted(SocketChannel sc)
        {
            Header header = SHAREDDATA.FromBinary<Header>(sc.ReadBytes());

            NodeInformation aiteNodeInfo = null;
            if (!header.nodeInfo.ipAddress.Equals(sc.aiteIpAddress))
            {
                this.RaiseNotification("aite_wrong_node_info", 5, sc.aiteIpAddress.ToString(), header.nodeInfo.portNumber.ToString());

                aiteNodeInfo = new NodeInformation(sc.aiteIpAddress, header.nodeInfo.portNumber, header.nodeInfo.network, header.nodeInfo.publicRSAParameters);
            }

            HeaderResponse headerResponse = new HeaderResponse(myNodeInfo, header.nodeInfo.network == Network, IsAlreadyConnected(header.nodeInfo), aiteNodeInfo, header.creaVersion < creaVersion, protocolVersion, appnameWithVersion);

            sc.WriteBytes(headerResponse.ToBinary());

            if (aiteNodeInfo == null)
                aiteNodeInfo = header.nodeInfo;

            bool isWrongNetwork = (!headerResponse.isSameNetwork).RaiseNotification(GetType(), "aite_wrong_network", 5, aiteNodeInfo.ipAddress.ToString(), aiteNodeInfo.portNumber.ToString());
            bool isAlreadyConnected = headerResponse.isAlreadyConnected.RaiseNotification(GetType(), "aite_already_connected", 5, aiteNodeInfo.ipAddress.ToString(), aiteNodeInfo.portNumber.ToString());
            //<未実装>不良ノードは拒否する？
            if (isWrongNetwork || isAlreadyConnected)
            {
                sc.Close();

                UpdateNodeState(aiteNodeInfo, false);

                return;
            }

            UpdateNodeState(aiteNodeInfo, true);

            if (header.creaVersion > creaVersion)
            {
                //相手のクライアントバージョンの方が大きい場合の処理
                //<未実装>使用者への通知
                //<未実装>自動ダウンロード、バージョンアップなど
                //ここで直接行うべきではなく、イベントを発令するべきだろう
            }

            int sessionProtocolVersion = Math.Min(header.protocolVersion, protocolVersion);
            if (sessionProtocolVersion == 0)
            {
                //<未改良>拡張メソッドのConsoleWriteLineを直接使うように
                Action<string> _ConsoleWriteLine = (text) => this.ConsoleWriteLine(string.Join(" ", sc.ChannelAddressText, text));

                if (header.isTemporary)
                {
                    InboundProtocol(aiteNodeInfo, sc, _ConsoleWriteLine);

                    if (IsTemporaryContinue)
                    {
                        bool isWantToContinue = IsWantToContinue(header.nodeInfo);
                        sc.WriteBytes(BitConverter.GetBytes(isWantToContinue));
                        if (isWantToContinue)
                        {
                            bool isClientCanContinue = BitConverter.ToBoolean(sc.ReadBytes(), 0);
                            if (isClientCanContinue)
                                //このメソッドのどこかで（例外を除く全ての場合において）SocketChannelのCloseが呼び出されるようにしなければならない
                                InboundContinue(aiteNodeInfo, sc, _ConsoleWriteLine);
                            else
                                sc.Close();
                        }
                        else
                            sc.Close();
                    }
                    else
                        sc.Close();
                }
                else if (IsContinue)
                {
                    bool isCanListenerContinue = IsListenerCanContinue(header.nodeInfo);
                    sc.WriteBytes(BitConverter.GetBytes(isCanListenerContinue));
                    if (isCanListenerContinue)
                        //このメソッドのどこかで（例外を除く全ての場合において）SocketChannelのCloseが呼び出されるようにしなければならない
                        InboundContinue(aiteNodeInfo, sc, _ConsoleWriteLine);
                    else
                        sc.Close();
                }
                else
                    throw new InvalidOperationException("not_temporary_and_not_continue");
            }
            else
                throw new NotSupportedException("not_supported_protocol_ver");
        }

        protected SHAREDDATA[] Connect(NodeInformation aiteNodeInfo, bool isTemporary, Action _Continued, Message message, params SHAREDDATA[] reqDatas)
        {
            try
            {
                return ConnectInner(aiteNodeInfo.ipAddress, aiteNodeInfo.portNumber, isTemporary, _Continued, message, reqDatas);
            }
            catch (Exception ex)
            {
                this.RaiseError("connect", 5, ex);

                UpdateNodeState(aiteNodeInfo, false);

                return null;
            }
        }

        protected SHAREDDATA[] Connect(IPAddress aiteIpAddress, ushort aitePortNumber, bool isTemporary, Action _Continued, Message message, params SHAREDDATA[] reqDatas)
        {
            try
            {
                return ConnectInner(aiteIpAddress, aitePortNumber, isTemporary, _Continued, message, reqDatas);
            }
            catch (Exception ex)
            {
                this.RaiseError("connect", 5, ex);

                UpdateNodeState(aiteIpAddress, aitePortNumber, false);

                return null;
            }
        }

        //このメソッドのどこかで（例外を含む全ての場合において）SocketChannelのCloseが呼び出されるようにしなければならない
        private SHAREDDATA[] ConnectInner(IPAddress aiteIpAddress, ushort aitePortNumber, bool isTemporary, Action _Continued, Message message, params SHAREDDATA[] reqDatas)
        {
            SocketChannel sc = Connect(aiteIpAddress, aitePortNumber);

            try
            {
                //サーバが起動していない場合（ポート0又はIPアドレスが取得できなかったような場合）にはノード情報はnull
                sc.WriteBytes(new Header(myNodeInfo, creaVersion, protocolVersion, appnameWithVersion, isTemporary).ToBinary());
                HeaderResponse headerResponse = SHAREDDATA.FromBinary<HeaderResponse>(sc.ReadBytes());

                bool isWrongNetwork = (!headerResponse.isSameNetwork).RaiseNotification(GetType(), "wrong_network", 5, aiteIpAddress.ToString(), aitePortNumber.ToString());
                bool isAlreadyConnected = headerResponse.isAlreadyConnected.RaiseNotification(GetType(), "already_connected", 5, aiteIpAddress.ToString(), aitePortNumber.ToString());
                if (isWrongNetwork || isAlreadyConnected)
                {
                    sc.Close();

                    UpdateNodeState(headerResponse.nodeInfo, false);

                    return null;
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
                    //<未改良>拡張メソッドのConsoleWriteLineを直接使うように
                    Action<string> _ConsoleWriteLine = (text) => this.ConsoleWriteLine(string.Join(" ", sc.ChannelAddressText, text));

                    if (isTemporary)
                    {
                        SHAREDDATA[] resDatas = OutboundProtocol(headerResponse.nodeInfo, message, reqDatas, sc, _ConsoleWriteLine);

                        if (IsTemporaryContinue)
                        {
                            bool isWantToContinue = BitConverter.ToBoolean(sc.ReadBytes(), 0);
                            if (isWantToContinue)
                            {
                                bool isClientCanContinue = IsClientCanContinue(headerResponse.nodeInfo);
                                sc.WriteBytes(BitConverter.GetBytes(isClientCanContinue));
                                if (isClientCanContinue)
                                {
                                    _Continued();

                                    //このメソッドのどこかで（例外を除く全ての場合において）SocketChannelのCloseが呼び出されるようにしなければならない
                                    OutboundContinue(headerResponse.nodeInfo, sc, _ConsoleWriteLine);
                                }
                                else
                                    sc.Close();
                            }
                            else
                                sc.Close();
                        }
                        else
                            sc.Close();

                        return resDatas;
                    }
                    else if (IsContinue)
                    {
                        bool isListenerCanContinue = BitConverter.ToBoolean(sc.ReadBytes(), 0);
                        if (isListenerCanContinue)
                        {
                            _Continued();

                            //このメソッドのどこかで（例外を除く全ての場合において）SocketChannelのCloseが呼び出されるようにしなければならない
                            OutboundContinue(headerResponse.nodeInfo, sc, _ConsoleWriteLine);
                        }
                        else
                            sc.Close();
                    }
                    else
                        throw new InvalidOperationException("not_temporary_and_not_continue");
                }
                else
                    throw new NotSupportedException("not_supported_protocol_ver");
            }
            catch (Exception ex)
            {
                sc.Close();

                throw ex;
            }

            return null;
        }
    }

    public class CreaNode : CREANODEBASE
    {
        public CreaNode(ushort _portNumber, int _creaVersion, string _appnameWithVersion, FirstNodeInfosDatabase _fnisDatabase)
            : base(_portNumber, _creaVersion, _appnameWithVersion)
        {
            fnisDatabase = _fnisDatabase;

            fnis = new List<FirstNodeInformation>();

            processedTransactions = new TransactionCollection();
            processedChats = new ChatCollection();
        }

        private readonly FirstNodeInfosDatabase fnisDatabase;
        private readonly List<FirstNodeInformation> fnis;

        private static readonly string fnisRegistryURL = "http://www.pizyumi.com/nodes.aspx?add=";

        private NodeInformation dhtNodeInfo;

        private object[] kbucketsLocks;
        private List<NodeInformation>[] kbuckets;

        private object[] outboundConnectionsLock;
        private List<Connection>[] outboundConnections;
        private object[] inboundConnectionsLock;
        private List<Connection>[] inboundConnections;

        private bool isInitialized = false;

        private static readonly int keepConnectionNodeInfosMin = 4;
        private static readonly int outboundConnectionsMax = 2;
        private static readonly int inboundConnectionsMax = 4;

        public int NodeIdSizeByte { get { return dhtNodeInfo.Id.SizeByte; } }
        public int NodeIdSizeBit { get { return dhtNodeInfo.Id.SizeBit; } }

        private readonly TransactionCollection processedTransactions;
        private readonly ChatCollection processedChats;

        public event EventHandler<Transaction> ReceivedNewTransaction = delegate { };
        public event EventHandler<Chat> ReceivedNewChat = delegate { };

        protected override Network Network
        {
            get
            {
#if TEST
                return Network.globaltest;
#else
                return Network.global;
#endif
            }
        }

        protected override string GetPrivateRsaParameters()
        {
            using (RSACryptoServiceProvider rsacsp = new RSACryptoServiceProvider(2048))
                return rsacsp.ToXmlString(true);
        }

        protected override IPAddress GetIpAddressAndOpenPort()
        {
            DefaltNetworkInterface defaultNetworkInterface = new DefaltNetworkInterface();
            defaultNetworkInterface.Get();

            if ((!defaultNetworkInterface.IsExisted).RaiseWarning(this.GetType(), "fail_network_interface", 5))
                return null;

            this.RaiseNotification("succeed_network_interface", 5, defaultNetworkInterface.Name);

            UPnP3 upnp = null;
            try
            {
                upnp = new UPnP3(defaultNetworkInterface.MachineIpAddress, defaultNetworkInterface.GatewayIpAddress);
            }
            catch (UPnP3.DeviceDescriptionException)
            {
                this.RaiseError("fail_upnp", 5);

                return null;
            }

            this.RaiseNotification("start_open_port_search", 5);

            bool isNeededOpenPort = true;
            try
            {
                for (int i = 0; ; i++)
                {
                    GenericPortMappingEntry gpe = upnp.GetGenericPortMappingEntry(i);
                    if (gpe == null)
                        break;

                    this.RaiseNotification("generic_port_mapping_entry", 5, gpe.ToString());

                    if (gpe.NewInternalPort == myPortNumber && gpe.NewExternalPort == myPortNumber)
                    {
                        this.RaiseNotification("already_port_opened", 5);

                        isNeededOpenPort = false;

                        break;
                    }
                }
            }
            catch (Exception) { }

            if (isNeededOpenPort)
                try
                {
                    upnp.AddPortMapping(myPortNumber, myPortNumber, "TCP", appnameWithVersion).Pipe((isSucceed) => isSucceed.RaiseNotification(this.GetType(), "succeed_open_port", 5).NotRaiseNotification(this.GetType(), "fail_open_port", 5));
                }
                catch (Exception ex)
                {
                    this.RaiseError("fail_open_port", 5, ex);
                }

            try
            {
                IPAddress externalIpAddress = upnp.GetExternalIPAddress();

                if (externalIpAddress != null)
                    return externalIpAddress.Pipe((ipaddress) => this.RaiseNotification("succeed_get_global_ip", 5, ipaddress.ToString()));

                this.RaiseError("fail_get_global_ip", 5);
            }
            catch (Exception ex)
            {
                this.RaiseError("fail_get_global_ip", 5, ex);
            }

            return null;
        }

        protected override void NotifyFirstNodeInfo()
        {
            HttpWebRequest hwreq = WebRequest.Create(fnisRegistryURL + myFirstNodeInfo.Hex) as HttpWebRequest;
            using (HttpWebResponse hwres = hwreq.GetResponse() as HttpWebResponse)
            using (Stream stream = hwres.GetResponseStream())
            using (StreamReader sr = new StreamReader(stream, Encoding.UTF8))
                AddFirstNodeInfos(sr.ReadToEnd().Split(new string[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries).Select((elem) => elem.Trim()));

            this.RaiseNotification("register_fni", 5);
            this.RaiseNotification("get_fnis", 5);
        }

        protected override FirstNodeInformation[] GetFirstNodeInfos()
        {
            AddFirstNodeInfos(fnisDatabase.GetFirstNodeInfosData());

            return fnis.ToArray();
        }

        private void AddFirstNodeInfos(IEnumerable<string> nodes)
        {
            foreach (string node in nodes)
            {
                FirstNodeInformation fni = null;
                try
                {
                    fni = new FirstNodeInformation(node);
                }
                catch
                {
                    this.RaiseError("cant_decode_fni", 5);
                }

                if (fni != null && !fnis.Contains(fni))
                {
                    fnis.Add(fni);

                    this.RaiseNotification("add_fni", 5, fni.ToString());
                }
            }
        }

        protected NodeInformation[] GetNodeInfos()
        {
            List<NodeInformation> nodeInfos = new List<NodeInformation>();

            if (myNodeInfo != null)
                nodeInfos.Add(myNodeInfo);

            if (isInitialized)
                for (int i = 0; i < NodeIdSizeBit; i++)
                    lock (kbucketsLocks[i])
                        nodeInfos.AddRange(kbuckets[i]);

            return nodeInfos.ToArray();
        }

        protected override void InboundProtocol(NodeInformation nodeInfo, IChannel sc, Action<string> _ConsoleWriteLine)
        {
            Message message = SHAREDDATA.FromBinary<Message>(sc.ReadBytes());

            _ConsoleWriteLine(message.name.ToString());

            if (message.version != 0)
                throw new NotSupportedException();

            if (message.name == MessageName.reqNodeInfos)
                sc.WriteBytes(new ResNodeInfos(GetNodeInfos()).ToBinary());
            else if (message.name == MessageName.notifyNewTransaction)
            {
                NotifyNewTransaction nnt = SHAREDDATA.FromBinary<NotifyNewTransaction>(sc.ReadBytes());
                bool isNew = !processedTransactions.Contains(nnt.hash);
                sc.WriteBytes(BitConverter.GetBytes(isNew));
                if (isNew)
                {
                    ResTransaction rt = SHAREDDATA.FromBinary<ResTransaction>(sc.ReadBytes());
                    TransferTransaction tt = rt.transaction as TransferTransaction;

                    if (tt == null)
                        throw new InvalidOperationException();
                    if (!nnt.hash.Equals(tt.Id))
                        throw new InvalidOperationException();

                    if (!processedTransactions.AddTransaction(tt))
                        return;

                    ReceivedNewTransaction(this, tt);

                    this.StartTask("diffuseNewTransactions", "diffuseNewTransactions", () => DiffuseNewTransaction(nodeInfo, nnt, rt));
                }
            }
            else if (message.name == MessageName.NotifyNewChat)
            {
                NotifyNewChat nnc = SHAREDDATA.FromBinary<NotifyNewChat>(sc.ReadBytes());

                _ConsoleWriteLine("read_nnc");

                bool isNew = !processedChats.Contains(nnc.Id);
                sc.WriteBytes(BitConverter.GetBytes(isNew));

                _ConsoleWriteLine("write_isnew");

                if (isNew)
                {
                    Chat chat = SHAREDDATA.FromBinary<Chat>(sc.ReadBytes());

                    _ConsoleWriteLine("read_chat");

                    if (chat == null)
                        throw new InvalidOperationException();
                    if (nnc.Id != chat.Id)
                        throw new InvalidOperationException();

                    if (!processedChats.AddChat(chat))
                        return;

                    ReceivedNewChat(this, chat);

                    this.StartTask("diffuseNewChat", "diffuseNewChat", () => DiffuseNewChat(nodeInfo, nnc, chat));
                }
            }
            else
                throw new NotSupportedException("protocol_not_supported");
        }

        protected override SHAREDDATA[] OutboundProtocol(NodeInformation nodeInfo, Message message, SHAREDDATA[] datas, IChannel sc, Action<string> _ConsoleWriteLine)
        {
            if (message.version != 0)
                throw new NotSupportedException();

            sc.WriteBytes(message.ToBinary());

            _ConsoleWriteLine(message.name.ToString());

            if (message.name == MessageName.reqNodeInfos)
                return new SHAREDDATA[] { SHAREDDATA.FromBinary<ResNodeInfos>(sc.ReadBytes()) };
            else if (message.name == MessageName.notifyNewTransaction)
            {
                if (datas.Length != 2)
                    throw new InvalidOperationException();

                NotifyNewTransaction nnt = datas[0] as NotifyNewTransaction;
                ResTransaction rt = datas[1] as ResTransaction;
                TransferTransaction tt = rt.transaction as TransferTransaction;

                if (nnt == null || rt == null)
                    throw new InvalidOperationException();
                if (tt == null)
                    throw new InvalidOperationException();
                if (!nnt.hash.Equals(tt.Id))
                    throw new InvalidOperationException();

                sc.WriteBytes(nnt.ToBinary());
                if (BitConverter.ToBoolean(sc.ReadBytes(), 0))
                    sc.WriteBytes(rt.ToBinary());

                return new SHAREDDATA[] { };
            }
            else if (message.name == MessageName.NotifyNewChat)
            {
                if (datas.Length != 2)
                    throw new InvalidOperationException();

                NotifyNewChat nnc = datas[0] as NotifyNewChat;
                Chat chat = datas[1] as Chat;

                if (nnc == null || chat == null)
                    throw new InvalidOperationException();
                if (nnc.Id != chat.Id)
                    throw new InvalidOperationException();

                sc.WriteBytes(nnc.ToBinary());

                _ConsoleWriteLine("write_nnc");

                if (BitConverter.ToBoolean(sc.ReadBytes(), 0))
                {
                    _ConsoleWriteLine("read_isnew");

                    sc.WriteBytes(chat.ToBinary());

                    _ConsoleWriteLine("write_chat");
                }

                return new SHAREDDATA[] { };
            }
            else
                throw new NotSupportedException("protocol_not_supported");
        }

        public void DiffuseNewTransaction(Transaction transaction)
        {
            if (processedTransactions.Contains(transaction.Id).RaiseNotification(this.GetType(), "alredy_processed_tx", 3))
                return;

            DiffuseNewTransaction(null, new NotifyNewTransaction(transaction.Id), new ResTransaction(transaction));
        }

        private void DiffuseNewTransaction(NodeInformation source, NotifyNewTransaction nnt, ResTransaction rt) { Diffuse(source, new Message(MessageName.notifyNewTransaction, 0), nnt, rt); }

        public void DiffuseNewChat(Chat chat)
        {
            if (processedChats.Contains(chat.Id).RaiseNotification(this.GetType(), "alredy_processed_chat", 3))
                return;

            DiffuseNewChat(null, new NotifyNewChat(chat.Id), chat);
        }

        private void DiffuseNewChat(NodeInformation source, NotifyNewChat nnc, Chat chat) { Diffuse(source, new Message(MessageName.NotifyNewChat, 0), nnc, chat); }

        protected override bool IsContinue { get { return true; } }
        protected override bool IsTemporaryContinue { get { return true; } }

        protected override bool IsAlreadyConnected(NodeInformation nodeInfo)
        {
            if (isInitialized)
            {
                int distanceLevel = GetDistanceLevel(nodeInfo);

                if (outboundConnections[distanceLevel].Count > 0)
                    lock (outboundConnectionsLock[distanceLevel])
                        if (outboundConnections[distanceLevel].Where((elem) => elem.nodeInfo.Equals(nodeInfo)).FirstOrDefault() != null)
                            return true;

                if (inboundConnections[distanceLevel].Count > 0)
                    lock (inboundConnectionsLock[distanceLevel])
                        if (inboundConnections[distanceLevel].Where((elem) => elem.nodeInfo.Equals(nodeInfo)).FirstOrDefault() != null)
                            return true;
            }

            return false;
        }

        protected override void UpdateNodeState(NodeInformation nodeInfo, bool isSucceeded)
        {
            //<未改良>単純な追加と削除ではなく優先順位をつけるべき？
            if (isInitialized)
            {
                int distanceLevel = GetDistanceLevel(nodeInfo);

                if (isSucceeded)
                {
                    lock (kbucketsLocks[distanceLevel])
                        if (!kbuckets[distanceLevel].Contains(nodeInfo))
                            kbuckets[distanceLevel].Add(nodeInfo);
                }
                else if (kbuckets[distanceLevel].Count > 0)
                    lock (kbucketsLocks[distanceLevel])
                        if (kbuckets[distanceLevel].Contains(nodeInfo))
                            kbuckets[distanceLevel].Remove(nodeInfo);
            }
        }

        protected override void UpdateNodeState(IPAddress ipAddress, ushort portNumber, bool isSucceeded) { }

        protected override bool IsListenerCanContinue(NodeInformation nodeInfo)
        {
            return isInitialized && GetDistanceLevel(nodeInfo).Pipe((distanceLevel) => distanceLevel != -1 && inboundConnections[distanceLevel].Count < inboundConnectionsMax);
        }

        protected override bool IsWantToContinue(NodeInformation nodeInfo)
        {
            return isInitialized && GetDistanceLevel(nodeInfo).Pipe((distanceLevel) => distanceLevel != -1 && inboundConnections[distanceLevel].Count < inboundConnectionsMax);
        }

        protected override bool IsClientCanContinue(NodeInformation nodeInfo)
        {
            return isInitialized && GetDistanceLevel(nodeInfo).Pipe((distanceLevel) => distanceLevel != -1 && outboundConnections[distanceLevel].Count < outboundConnectionsMax);
        }

        protected override void InboundContinue(NodeInformation nodeInfo, SocketChannel sc, Action<string> _ConsoleWriteLine)
        {
            Connection connection = new Connection(nodeInfo, sc, _ConsoleWriteLine);
            int distanceLevel = GetDistanceLevel(nodeInfo);

            lock (inboundConnectionsLock[distanceLevel])
                inboundConnections[distanceLevel].Add(connection);

            sc.Closed += (sender, e) => RemoveAndRefillConnections(distanceLevel, connection, inboundConnectionsLock, inboundConnections, inboundConnectionsMax);
            sc.Failed += (sender, e) => RemoveAndRefillConnections(distanceLevel, connection, inboundConnectionsLock, inboundConnections, inboundConnectionsMax);

            Continue(nodeInfo, sc, _ConsoleWriteLine);
        }

        protected override void OutboundContinue(NodeInformation nodeInfo, SocketChannel sc, Action<string> _ConsoleWriteLine)
        {
            Connection connection = new Connection(nodeInfo, sc, _ConsoleWriteLine);
            int distanceLevel = GetDistanceLevel(nodeInfo);

            lock (outboundConnectionsLock[distanceLevel])
                outboundConnections[distanceLevel].Add(connection);

            sc.Closed += (sender, e) => RemoveAndRefillConnections(distanceLevel, connection, outboundConnectionsLock, outboundConnections, outboundConnectionsMax);
            sc.Failed += (sender, e) => RemoveAndRefillConnections(distanceLevel, connection, outboundConnectionsLock, outboundConnections, outboundConnectionsMax);

            Continue(nodeInfo, sc, _ConsoleWriteLine);
        }

        private void RemoveAndRefillConnections(int distanceLevel, Connection connection, object[] locks, List<Connection>[] connections, int max)
        {
            lock (locks[distanceLevel])
                connections[distanceLevel].Remove(connection);

            List<NodeInformation> nodeInfos;
            List<NodeInformation> nodeInfosConnected;
            lock (kbucketsLocks[distanceLevel])
                nodeInfos = new List<NodeInformation>(kbuckets[distanceLevel]);
            lock (outboundConnectionsLock[distanceLevel])
                nodeInfosConnected = new List<NodeInformation>(outboundConnections[distanceLevel].Select((elem) => elem.nodeInfo));
            lock (inboundConnectionsLock[distanceLevel])
                nodeInfosConnected.AddRange(inboundConnections[distanceLevel].Select((elem) => elem.nodeInfo));

            foreach (var nodeInfo in nodeInfos)
                if (connections[distanceLevel].Count < max)
                {
                    if (!nodeInfosConnected.Contains(nodeInfo))
                        Connect(nodeInfo, false, () => { }, null);
                }
                else
                    break;
        }

        private void Continue(NodeInformation nodeInfo, SocketChannel sc, Action<string> _ConsoleWriteLine)
        {
            _ConsoleWriteLine("常時接続");

            sc.Sessioned += (sender, e) =>
            {
                try
                {
                    _ConsoleWriteLine("新しいセッション");

                    InboundProtocol(nodeInfo, e, _ConsoleWriteLine);
                }
                catch (Exception ex)
                {
                    this.RaiseError("inbound_session", 5, ex);
                }
                finally
                {
                    e.Close();

                    _ConsoleWriteLine("セッション終わり");
                }
            };
        }

        protected override SHAREDDATA[] Request(NodeInformation nodeInfo, Message message, params SHAREDDATA[] datas)
        {
            Connection connection = null;
            if (isInitialized)
            {
                int distanceLevel = GetDistanceLevel(myNodeInfo);

                if (outboundConnections[distanceLevel].Count > 0)
                    lock (outboundConnectionsLock[distanceLevel])
                        connection = outboundConnections[distanceLevel].Where((elem) => elem.nodeInfo.Equals(nodeInfo)).FirstOrDefault();

                if (connection == null)
                    if (inboundConnections[distanceLevel].Count > 0)
                        lock (inboundConnectionsLock[distanceLevel])
                            connection = inboundConnections[distanceLevel].Where((elem) => elem.nodeInfo.Equals(nodeInfo)).FirstOrDefault();
            }

            if (connection == null)
                return Connect(nodeInfo, true, () => { }, message, datas);

            SessionChannel sc2 = null;
            try
            {
                sc2 = connection.sc.NewSession();

                connection._ConsoleWriteLine("新しいセッション");

                return OutboundProtocol(nodeInfo, message, datas, sc2, connection._ConsoleWriteLine);
            }
            catch (Exception ex)
            {
                this.RaiseError("outbound_session", 5, ex);
            }
            finally
            {
                if (sc2 != null)
                {
                    sc2.Close();

                    connection._ConsoleWriteLine("セッション終わり");
                }
            }

            return null;
        }

        protected override void Diffuse(NodeInformation source, Message message, params SHAREDDATA[] datas)
        {
            if (!isInitialized)
                throw new InvalidOperationException("not_yet_connections_keeped");

            List<Connection> connections = new List<Connection>();
            for (int i = 0; i < NodeIdSizeBit; i++)
            {
                if (outboundConnections[i].Count > 0)
                    lock (outboundConnectionsLock[i])
                        connections.AddRange(outboundConnections[i]);
                if (inboundConnections[i].Count > 0)
                    lock (inboundConnectionsLock[i])
                        connections.AddRange(inboundConnections[i]);
            }

            foreach (Connection connection in connections)
            {
                if (source != null && connection.nodeInfo.Equals(source))
                    continue;

                SessionChannel sc2 = null;
                try
                {
                    sc2 = connection.sc.NewSession();

                    connection._ConsoleWriteLine("新しいセッション");

                    OutboundProtocol(connection.nodeInfo, message, datas, sc2, connection._ConsoleWriteLine);
                }
                catch (Exception ex)
                {
                    this.RaiseError("diffuse", 5, ex);
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
        }

        private void Initialize()
        {
            kbuckets = new List<NodeInformation>[NodeIdSizeBit];
            kbucketsLocks = new object[NodeIdSizeBit];

            outboundConnections = new List<Connection>[NodeIdSizeBit];
            outboundConnectionsLock = new object[NodeIdSizeBit];
            inboundConnections = new List<Connection>[NodeIdSizeBit];
            inboundConnectionsLock = new object[NodeIdSizeBit];

            for (int i = 0; i < NodeIdSizeBit; i++)
            {
                kbuckets[i] = new List<NodeInformation>();
                kbucketsLocks[i] = new object();

                outboundConnections[i] = new List<Connection>();
                outboundConnectionsLock[i] = new object();
                inboundConnections[i] = new List<Connection>();
                inboundConnectionsLock[i] = new object();
            }

            isInitialized = true;
        }

        //<未実装>別スレッドで常時動かすべき？
        protected override void KeepConnections()
        {
            if (myNodeInfo != null)
            {
                dhtNodeInfo = myNodeInfo;

                Initialize();
            }

            if (firstNodeInfos.Length == 0)
            {
                //<未実装>初期ノード情報がない場合の処理
                //選択肢1 -> 10分くらい待って再度初期ノード情報取得
                //選択肢2 -> 使用者に任せる（使用者によって手動で初期ノード情報が追加された時に常時接続再実行）

                this.RaiseNotification("keep_connection_fnis_zero", 5);

                return;
            }

            List<NodeInformation> nodeInfos = new List<NodeInformation>();
            for (int i = 0; i < firstNodeInfos.Length && nodeInfos.Count < keepConnectionNodeInfosMin; i++)
            {
                SHAREDDATA[] resDatas = Connect(firstNodeInfos[i].ipAddress, firstNodeInfos[i].portNumber, true, () => { }, new Message(MessageName.reqNodeInfos, 0));
                ResNodeInfos resNodeInfos;
                if (resDatas != null && resDatas.Length == 1 && (resNodeInfos = resDatas[0] as ResNodeInfos) != null)
                    //<要検討>更新時間順に並び替えるべき？
                    nodeInfos.AddRange(resNodeInfos.nodeInfos);
            }
            if (nodeInfos.Count == 0)
            {
                //<未実装>初期ノードからノード情報を取得できなかった場合の処理
                //選択肢1 -> 10分くらい待って再度ノード情報取得
                //選択肢2 -> 使用者に任せる（使用者によって手動で初期ノード情報が追加された時に常時接続再実行）

                this.RaiseNotification("keep_connection_nis_zero", 5);

                return;
            }

            if (myNodeInfo == null)
            {
                dhtNodeInfo = nodeInfos[0];

                Initialize();
            }

            foreach (var nodeInfo in nodeInfos)
            {
                int distanceLevel = GetDistanceLevel(nodeInfo);
                if (distanceLevel != -1)
                    lock (kbucketsLocks[distanceLevel])
                        kbuckets[distanceLevel].Add(nodeInfo);
            }

            for (int i = 0; i < NodeIdSizeBit; i++)
            {
                if (kbuckets[i].Count == 0 || outboundConnections[i].Count >= outboundConnectionsMax)
                    continue;

                lock (kbucketsLocks[i])
                    nodeInfos = new List<NodeInformation>(kbuckets[i]);

                foreach (var nodeInfo in nodeInfos)
                    if (outboundConnections[i].Count < outboundConnectionsMax)
                    {
                        if (!IsAlreadyConnected(nodeInfo))
                            Connect(nodeInfo, false, () => { }, null);
                    }
                    else
                        break;
            }

            this.RaiseNotification("keep_conn_completed", 5);
        }

        private int GetDistanceLevel(NodeInformation nodeInfo2)
        {
            Sha256Hash xor = dhtNodeInfo.Id.XOR(nodeInfo2.Id);

            int distanceLevel = NodeIdSizeBit - 1;

            int? minus = null;
            for (int i = 0, j = 0; i < NodeIdSizeByte && (!minus.HasValue || minus.Value == 8); i++)
                for (j = 0, minus = null; j < distanceParameters.Length && minus == null; j++)
                    if (xor.hash[i] >= distanceParameters[j].hashByteMin && xor.hash[i] <= distanceParameters[j].hashByteMax)
                        distanceLevel -= (minus = distanceParameters[j].minus).Value;

            return distanceLevel;
        }

        private static readonly DistanceParameter[] distanceParameters = new DistanceParameter[]{
            new DistanceParameter(0, 0, 8), 
            new DistanceParameter(1, 1, 7), 
            new DistanceParameter(2, 3, 6), 
            new DistanceParameter(4, 7, 5), 
            new DistanceParameter(8, 15, 4), 
            new DistanceParameter(16, 31, 3), 
            new DistanceParameter(32, 63, 2), 
            new DistanceParameter(64, 127, 1), 
            new DistanceParameter(128, 255, 0), 
        };

        public class DistanceParameter
        {
            public DistanceParameter(int _hashByteMin, int _hashByteMax, int _minus)
            {
                hashByteMin = _hashByteMin;
                hashByteMax = _hashByteMax;
                minus = _minus;
            }

            public int hashByteMin { get; private set; }
            public int hashByteMax { get; private set; }
            public int minus { get; private set; }
        }

        public class Connection
        {
            public Connection(NodeInformation _nodeInfo, SocketChannel _sc, Action<string> __ConsoleWriteLine)
            {
                nodeInfo = _nodeInfo;
                sc = _sc;
                _ConsoleWriteLine = __ConsoleWriteLine;
            }

            public readonly NodeInformation nodeInfo;
            public readonly SocketChannel sc;
            public readonly Action<string> _ConsoleWriteLine;
        }
    }

    #region 試験用

    public abstract class CreaNodeLocalTest : CREANODEBASE
    {
        public CreaNodeLocalTest(ushort _portNumber, int _creaVersion, string _appnameWithVersion)
            : base(_portNumber, _creaVersion, _appnameWithVersion)
        {
            processedTransactions = new TransactionCollection();
            processedChats = new ChatCollection();
        }

        private readonly List<FirstNodeInformation> fnis = new List<FirstNodeInformation>();

        private static readonly string testPrivateRsaParameters;

        private static readonly int fnisRegistryPortNumber = 12345;
        private static readonly string fnisRegistryURL = "http://localhost:" + fnisRegistryPortNumber.ToString() + "/nodes?add=";
        private static readonly string fnisRegistryFileName = "nodes.txt";
        private static readonly int fnisRegistryMaxNodes = 128;

        protected abstract NodeInformation[] GetNodeInfos();

        static CreaNodeLocalTest()
        {
            using (RSACryptoServiceProvider rsacsp = new RSACryptoServiceProvider(2048))
                testPrivateRsaParameters = rsacsp.ToXmlString(true);

            HttpListener hl = new HttpListener();
            hl.Prefixes.Add("http://*:" + fnisRegistryPortNumber.ToString() + "/");
            try
            {
                hl.Start();
            }
            catch (HttpListenerException ex)
            {
                if (ex.ErrorCode == 183)
                    return;
                else if (ex.ErrorCode == 5)
                    throw new HttpListenerException(ex.ErrorCode, "require_administrator");

                throw ex;
            }

            object dummy = new object();
            dummy.StartTask("fnisRegistry", "fnisRegistry", () =>
            {
                while (true)
                {
                    HttpListenerContext hlc = null;

                    try
                    {
                        hlc = hl.GetContext();
                    }
                    catch (HttpListenerException)
                    {
                        hl.Close();
                        break;
                    }

                    //<未実装>ノード情報をデコードして同じノード情報は弾いた方が良いかもしれない
                    using (HttpListenerResponse hlres = hlc.Response)
                        if (hlc.Request.Url.OriginalString.StartsWith(fnisRegistryURL))
                        {
                            string queryAdd = hlc.Request.Url.OriginalString.Substring(fnisRegistryURL.Length);
                            if (queryAdd.Contains("&"))
                                throw new InvalidOperationException("fnis_registry_query");

                            List<string> nodes = File.Exists(fnisRegistryFileName) ? File.ReadAllLines(fnisRegistryFileName).ToList() : new List<string>();

                            if (nodes.Contains(queryAdd))
                                nodes.Remove(queryAdd);

                            nodes.Insert(0, queryAdd);

                            while (nodes.Count > fnisRegistryMaxNodes)
                                nodes.RemoveAt(nodes.Count - 1);

                            File.WriteAllLines(fnisRegistryFileName, nodes);

                            byte[] bytes = Encoding.UTF8.GetBytes(string.Join(Environment.NewLine, nodes));
                            hlres.OutputStream.Write(bytes, 0, bytes.Length);
                        }
                        else
                            throw new InvalidOperationException("fnis_registry_invalid_url");
                }
            });
        }

        protected override Network Network { get { return Network.localtest; } }

        protected override string GetPrivateRsaParameters() { return testPrivateRsaParameters; }

        protected override IPAddress GetIpAddressAndOpenPort() { return IPAddress.Loopback; }

        protected override void NotifyFirstNodeInfo()
        {
            HttpWebRequest hwreq = WebRequest.Create(fnisRegistryURL + myFirstNodeInfo.Hex) as HttpWebRequest;
            using (HttpWebResponse hwres = hwreq.GetResponse() as HttpWebResponse)
            using (Stream stream = hwres.GetResponseStream())
            using (StreamReader sr = new StreamReader(stream, Encoding.UTF8))
            {
                foreach (string node in sr.ReadToEnd().Split(new string[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries))
                {
                    FirstNodeInformation fni = null;
                    try
                    {
                        fni = new FirstNodeInformation(node);
                    }
                    catch { }

                    if (fni != null && !fnis.Contains(fni))
                        fnis.Add(fni);
                }
            }
        }

        protected override FirstNodeInformation[] GetFirstNodeInfos() { return fnis.ToArray(); }

        private readonly TransactionCollection processedTransactions;
        private readonly ChatCollection processedChats;

        public event EventHandler<Transaction> ReceivedNewTransaction = delegate { };
        public event EventHandler<Chat> ReceivedNewChat = delegate { };

        protected override void InboundProtocol(NodeInformation nodeInfo, IChannel sc, Action<string> _ConsoleWriteLine)
        {
            Message message = SHAREDDATA.FromBinary<Message>(sc.ReadBytes());

            _ConsoleWriteLine(message.name.ToString());

            if (message.version != 0)
                throw new NotSupportedException();

            if (message.name == MessageName.reqNodeInfos)
                sc.WriteBytes(new ResNodeInfos(GetNodeInfos()).ToBinary());
            else if (message.name == MessageName.notifyNewTransaction)
            {
                NotifyNewTransaction nnt = SHAREDDATA.FromBinary<NotifyNewTransaction>(sc.ReadBytes());
                bool isNew = !processedTransactions.Contains(nnt.hash);
                sc.WriteBytes(BitConverter.GetBytes(isNew));
                if (isNew)
                {
                    ResTransaction rt = SHAREDDATA.FromBinary<ResTransaction>(sc.ReadBytes());
                    TransferTransaction tt = rt.transaction as TransferTransaction;

                    if (tt == null)
                        throw new InvalidOperationException();
                    if (!nnt.hash.Equals(tt.Id))
                        throw new InvalidOperationException();

                    if (!processedTransactions.AddTransaction(tt))
                        return;

                    ReceivedNewTransaction(this, tt);

                    this.StartTask("diffuseNewTransactions", "diffuseNewTransactions", () => DiffuseNewTransaction(nodeInfo, nnt, rt));
                }
            }
            else if (message.name == MessageName.NotifyNewChat)
            {
                NotifyNewChat nnc = SHAREDDATA.FromBinary<NotifyNewChat>(sc.ReadBytes());

                //_ConsoleWriteLine("read_nnc");

                bool isNew = !processedChats.Contains(nnc.Id);
                sc.WriteBytes(BitConverter.GetBytes(isNew));

                //_ConsoleWriteLine("write_isnew");

                if (isNew)
                {
                    Chat chat = SHAREDDATA.FromBinary<Chat>(sc.ReadBytes());

                    //_ConsoleWriteLine("read_chat");

                    if (chat == null)
                        throw new InvalidOperationException();
                    if (nnc.Id != chat.Id)
                        throw new InvalidOperationException();

                    if (!processedChats.AddChat(chat))
                        return;

                    ReceivedNewChat(this, chat);

                    this.StartTask("diffuseNewChat", "diffuseNewChat", () => DiffuseNewChat(nodeInfo, nnc, chat));
                }
            }
            else
                throw new NotSupportedException("protocol_not_supported");
        }

        protected override SHAREDDATA[] OutboundProtocol(NodeInformation nodeInfo, Message message, SHAREDDATA[] datas, IChannel sc, Action<string> _ConsoleWriteLine)
        {
            if (message.version != 0)
                throw new NotSupportedException();

            sc.WriteBytes(message.ToBinary());

            _ConsoleWriteLine(message.name.ToString());

            if (message.name == MessageName.reqNodeInfos)
                return new SHAREDDATA[] { SHAREDDATA.FromBinary<ResNodeInfos>(sc.ReadBytes()) };
            else if (message.name == MessageName.notifyNewTransaction)
            {
                if (datas.Length != 2)
                    throw new InvalidOperationException();

                NotifyNewTransaction nnt = datas[0] as NotifyNewTransaction;
                ResTransaction rt = datas[1] as ResTransaction;
                TransferTransaction tt = rt.transaction as TransferTransaction;

                if (nnt == null || rt == null)
                    throw new InvalidOperationException();
                if (tt == null)
                    throw new InvalidOperationException();
                if (!nnt.hash.Equals(tt.Id))
                    throw new InvalidOperationException();

                sc.WriteBytes(nnt.ToBinary());
                if (BitConverter.ToBoolean(sc.ReadBytes(), 0))
                    sc.WriteBytes(rt.ToBinary());

                return new SHAREDDATA[] { };
            }
            else if (message.name == MessageName.NotifyNewChat)
            {
                if (datas.Length != 2)
                    throw new InvalidOperationException();

                NotifyNewChat nnc = datas[0] as NotifyNewChat;
                Chat chat = datas[1] as Chat;

                if (nnc == null || chat == null)
                    throw new InvalidOperationException();
                if (nnc.Id != chat.Id)
                    throw new InvalidOperationException();

                sc.WriteBytes(nnc.ToBinary());

                //_ConsoleWriteLine("write_nnc");

                if (BitConverter.ToBoolean(sc.ReadBytes(), 0))
                {
                    //_ConsoleWriteLine("read_isnew");

                    sc.WriteBytes(chat.ToBinary());

                    //_ConsoleWriteLine("write_chat");
                }

                return new SHAREDDATA[] { };
            }
            else
                throw new NotSupportedException("protocol_not_supported");
        }

        public void DiffuseNewTransaction(Transaction transaction)
        {
            if (processedTransactions.Contains(transaction.Id).RaiseNotification(this.GetType(), "alredy_processed_tx", 3))
                return;

            processedTransactions.AddTransaction(transaction);

            DiffuseNewTransaction(null, new NotifyNewTransaction(transaction.Id), new ResTransaction(transaction));
        }

        private void DiffuseNewTransaction(NodeInformation source, NotifyNewTransaction nnt, ResTransaction rt) { Diffuse(source, new Message(MessageName.notifyNewTransaction, 0), nnt, rt); }

        public void DiffuseNewChat(Chat chat)
        {
            if (processedChats.Contains(chat.Id).RaiseNotification(this.GetType(), "alredy_processed_chat", 3))
                return;

            processedChats.AddChat(chat);

            DiffuseNewChat(null, new NotifyNewChat(chat.Id), chat);
        }

        private void DiffuseNewChat(NodeInformation source, NotifyNewChat nnc, Chat chat) { Diffuse(source, new Message(MessageName.NotifyNewChat, 0), nnc, chat); }
    }

    public class CreaNodeLocalTestNotContinue : CreaNodeLocalTest
    {
        public CreaNodeLocalTestNotContinue(ushort _portNumber, int _creaVersion, string _appnameWithVersion) : base(_portNumber, _creaVersion, _appnameWithVersion) { }

        protected override bool IsContinue { get { return false; } }
        protected override bool IsTemporaryContinue { get { return false; } }

        protected override bool IsAlreadyConnected(NodeInformation nodeInfo) { return false; }

        protected override void UpdateNodeState(NodeInformation nodeInfo, bool isSucceeded) { }

        protected override void UpdateNodeState(IPAddress ipAddress, ushort portNumber, bool isSucceeded) { }

        protected override bool IsListenerCanContinue(NodeInformation nodeInfo) { return false; }

        protected override bool IsWantToContinue(NodeInformation nodeInfo) { return false; }

        protected override bool IsClientCanContinue(NodeInformation nodeInfo) { return false; }

        //このメソッドのどこかで（例外を除く全ての場合において）SocketChannelのCloseが呼び出されるようにしなければならない
        protected override void InboundContinue(NodeInformation nodeInfo, SocketChannel sc, Action<string> _ConsoleWriteLine) { sc.Close(); }

        //このメソッドのどこかで（例外を除く全ての場合において）SocketChannelのCloseが呼び出されるようにしなければならない
        protected override void OutboundContinue(NodeInformation nodeInfo, SocketChannel sc, Action<string> _ConsoleWriteLine) { sc.Close(); }

        protected override SHAREDDATA[] Request(NodeInformation nodeinfo, Message message, params SHAREDDATA[] datas)
        {
            return Connect(nodeinfo, true, () => { }, message, datas);
        }

        protected override void Diffuse(NodeInformation source, Message message, params SHAREDDATA[] datas)
        {
            for (int i = 0; i < 16 && i < firstNodeInfos.Length; i++)
                Connect(firstNodeInfos[i].ipAddress, firstNodeInfos[i].portNumber, true, () => { }, message, datas);
        }

        protected override void KeepConnections() { }

        protected override NodeInformation[] GetNodeInfos() { return new NodeInformation[] { }; }
    }

    public class CreaNodeLocalTestContinue : CreaNodeLocalTest
    {
        public CreaNodeLocalTestContinue(ushort _portNumber, int _creaVersion, string _appnameWithVersion) : base(_portNumber, _creaVersion, _appnameWithVersion) { }

        private static readonly int maxInboundConnection = 16;
        private static readonly int maxOutboundConnection = 8;

        private readonly object clientNodesLock = new object();
        private readonly Dictionary<NodeInformation, Connection> clientNodes = new Dictionary<NodeInformation, Connection>();
        private readonly object listenerNodesLock = new object();
        private readonly Dictionary<NodeInformation, Connection> listenerNodes = new Dictionary<NodeInformation, Connection>();

        public class Connection
        {
            public Connection(NodeInformation _nodeInfo, SocketChannel _sc, Action<string> __ConsoleWriteLine)
            {
                nodeInfo = _nodeInfo;
                sc = _sc;
                _ConsoleWriteLine = __ConsoleWriteLine;
            }

            public readonly NodeInformation nodeInfo;
            public readonly SocketChannel sc;
            public readonly Action<string> _ConsoleWriteLine;
        }

        protected override bool IsContinue { get { return true; } }
        protected override bool IsTemporaryContinue { get { return true; } }

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

        protected override void UpdateNodeState(IPAddress ipAddress, ushort portNumber, bool isSucceeded) { }

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

        //このメソッドのどこかで（例外を除く全ての場合において）SocketChannelのCloseが呼び出されるようにしなければならない
        protected override void InboundContinue(NodeInformation nodeInfo, SocketChannel sc, Action<string> _ConsoleWriteLine)
        {
            lock (listenerNodesLock)
                listenerNodes.Add(nodeInfo, new Connection(nodeInfo, sc, _ConsoleWriteLine));

            sc.Closed += (sender, e) =>
            {
                lock (listenerNodesLock)
                    listenerNodes.Remove(nodeInfo);
            };
            sc.Failed += (sender, e) =>
            {
                lock (listenerNodesLock)
                    listenerNodes.Remove(nodeInfo);
            };

            Continue(nodeInfo, sc, _ConsoleWriteLine);
        }

        //このメソッドのどこかで（例外を除く全ての場合において）SocketChannelのCloseが呼び出されるようにしなければならない
        protected override void OutboundContinue(NodeInformation nodeInfo, SocketChannel sc, Action<string> _ConsoleWriteLine)
        {
            lock (clientNodesLock)
                clientNodes.Add(nodeInfo, new Connection(nodeInfo, sc, _ConsoleWriteLine));

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

                    InboundProtocol(nodeInfo, e, _ConsoleWriteLine);
                }
                catch (Exception ex)
                {
                    this.RaiseError("inbound_session", 5, ex);
                }
                finally
                {
                    e.Close();

                    _ConsoleWriteLine("セッション終わり");
                }
            };
        }

        protected override SHAREDDATA[] Request(NodeInformation nodeinfo, Message message, params SHAREDDATA[] datas)
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

                    return OutboundProtocol(nodeinfo, message, datas, sc2, connection._ConsoleWriteLine);
                }
                catch (Exception ex)
                {
                    this.RaiseError("outbound_session", 5, ex);

                    return null;
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
                return Connect(nodeinfo, true, () => { }, message, datas);
        }

        protected override void Diffuse(NodeInformation source, Message message, params SHAREDDATA[] datas)
        {
            List<Connection> connections = new List<Connection>();
            lock (clientNodesLock)
                foreach (Connection cq in clientNodes.Values)
                    connections.Add(cq);
            lock (listenerNodesLock)
                foreach (Connection cq in listenerNodes.Values)
                    connections.Add(cq);

            foreach (Connection connection in connections)
            {
                SessionChannel sc2 = null;
                try
                {
                    sc2 = connection.sc.NewSession();

                    connection._ConsoleWriteLine("新しいセッション");

                    OutboundProtocol(connection.nodeInfo, message, datas, sc2, connection._ConsoleWriteLine);
                }
                catch (Exception ex)
                {
                    this.RaiseError("diffuse", 5, ex);
                }
                finally
                {
                    sc2.Close();

                    connection._ConsoleWriteLine("セッション終わり");
                }
            }
        }

        //<未実装>別スレッドで常時動かすべき？
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

                    if (count < maxOutboundConnection)
                        Connect(firstNodeInfos[i].ipAddress, firstNodeInfos[i].portNumber, false, () => { }, null);
                }

                this.RaiseNotification("keep_conn_completed", 5);
            }
        }

        protected override NodeInformation[] GetNodeInfos() { return new NodeInformation[] { }; }
    }

    public class CreaNodeLocalTestContinueDHT : CreaNodeLocalTest
    {
        public CreaNodeLocalTestContinueDHT(ushort _portNumber, int _creaVersion, string _appnameWithVersion) : base(_portNumber, _creaVersion, _appnameWithVersion) { }

        private NodeInformation dhtNodeInfo;

        private object[] kbucketsLocks;
        private List<NodeInformation>[] kbuckets;

        private object[] outboundConnectionsLock;
        private List<Connection>[] outboundConnections;
        private object[] inboundConnectionsLock;
        private List<Connection>[] inboundConnections;

        private bool isInitialized = false;

        private static readonly int keepConnectionNodeInfosMin = 4;
        private static readonly int outboundConnectionsMax = 2;
        private static readonly int inboundConnectionsMax = 4;

        public int NodeIdSizeByte { get { return dhtNodeInfo.Id.SizeByte; } }
        public int NodeIdSizeBit { get { return dhtNodeInfo.Id.SizeBit; } }

        public class Connection
        {
            public Connection(NodeInformation _nodeInfo, SocketChannel _sc, Action<string> __ConsoleWriteLine)
            {
                nodeInfo = _nodeInfo;
                sc = _sc;
                _ConsoleWriteLine = __ConsoleWriteLine;
            }

            public readonly NodeInformation nodeInfo;
            public readonly SocketChannel sc;
            public readonly Action<string> _ConsoleWriteLine;
        }

        protected override NodeInformation[] GetNodeInfos()
        {
            List<NodeInformation> nodeInfos = new List<NodeInformation>();

            if (myNodeInfo != null)
                nodeInfos.Add(myNodeInfo);

            if (isInitialized)
                for (int i = 0; i < NodeIdSizeBit; i++)
                    lock (kbucketsLocks[i])
                        nodeInfos.AddRange(kbuckets[i]);

            return nodeInfos.ToArray();
        }

        protected override bool IsContinue { get { return true; } }
        protected override bool IsTemporaryContinue { get { return true; } }

        protected override bool IsAlreadyConnected(NodeInformation nodeInfo)
        {
            if (isInitialized)
            {
                int distanceLevel = GetDistanceLevel(nodeInfo);

                if (outboundConnections[distanceLevel].Count > 0)
                    lock (outboundConnectionsLock[distanceLevel])
                        if (outboundConnections[distanceLevel].Where((elem) => elem.nodeInfo.Equals(nodeInfo)).FirstOrDefault() != null)
                            return true;

                if (inboundConnections[distanceLevel].Count > 0)
                    lock (inboundConnectionsLock[distanceLevel])
                        if (inboundConnections[distanceLevel].Where((elem) => elem.nodeInfo.Equals(nodeInfo)).FirstOrDefault() != null)
                            return true;
            }

            return false;
        }

        protected override void UpdateNodeState(NodeInformation nodeInfo, bool isSucceeded)
        {
            //<未改良>単純な追加と削除ではなく優先順位をつけるべき？
            if (isInitialized)
            {
                int distanceLevel = GetDistanceLevel(nodeInfo);

                if (isSucceeded)
                {
                    lock (kbucketsLocks[distanceLevel])
                        if (!kbuckets[distanceLevel].Contains(nodeInfo))
                            kbuckets[distanceLevel].Add(nodeInfo);
                }
                else if (kbuckets[distanceLevel].Count > 0)
                    lock (kbucketsLocks[distanceLevel])
                        if (kbuckets[distanceLevel].Contains(nodeInfo))
                            kbuckets[distanceLevel].Remove(nodeInfo);
            }
        }

        protected override void UpdateNodeState(IPAddress ipAddress, ushort portNumber, bool isSucceeded) { }

        protected override bool IsListenerCanContinue(NodeInformation nodeInfo)
        {
            return isInitialized && GetDistanceLevel(nodeInfo).Pipe((distanceLevel) => distanceLevel != -1 && inboundConnections[distanceLevel].Count < inboundConnectionsMax);
        }

        protected override bool IsWantToContinue(NodeInformation nodeInfo)
        {
            return isInitialized && GetDistanceLevel(nodeInfo).Pipe((distanceLevel) => distanceLevel != -1 && inboundConnections[distanceLevel].Count < inboundConnectionsMax);
        }

        protected override bool IsClientCanContinue(NodeInformation nodeInfo)
        {
            return isInitialized && GetDistanceLevel(nodeInfo).Pipe((distanceLevel) => distanceLevel != -1 && outboundConnections[distanceLevel].Count < outboundConnectionsMax);
        }

        protected override void InboundContinue(NodeInformation nodeInfo, SocketChannel sc, Action<string> _ConsoleWriteLine)
        {
            Connection connection = new Connection(nodeInfo, sc, _ConsoleWriteLine);
            int distanceLevel = GetDistanceLevel(nodeInfo);

            lock (inboundConnectionsLock[distanceLevel])
                inboundConnections[distanceLevel].Add(connection);

            sc.Closed += (sender, e) => RemoveAndRefillConnections(distanceLevel, connection, inboundConnectionsLock, inboundConnections, inboundConnectionsMax);
            sc.Failed += (sender, e) => RemoveAndRefillConnections(distanceLevel, connection, inboundConnectionsLock, inboundConnections, inboundConnectionsMax);

            Continue(nodeInfo, sc, _ConsoleWriteLine);
        }

        protected override void OutboundContinue(NodeInformation nodeInfo, SocketChannel sc, Action<string> _ConsoleWriteLine)
        {
            Connection connection = new Connection(nodeInfo, sc, _ConsoleWriteLine);
            int distanceLevel = GetDistanceLevel(nodeInfo);

            lock (outboundConnectionsLock[distanceLevel])
                outboundConnections[distanceLevel].Add(connection);

            sc.Closed += (sender, e) => RemoveAndRefillConnections(distanceLevel, connection, outboundConnectionsLock, outboundConnections, outboundConnectionsMax);
            sc.Failed += (sender, e) => RemoveAndRefillConnections(distanceLevel, connection, outboundConnectionsLock, outboundConnections, outboundConnectionsMax);

            Continue(nodeInfo, sc, _ConsoleWriteLine);
        }

        private void RemoveAndRefillConnections(int distanceLevel, Connection connection, object[] locks, List<Connection>[] connections, int max)
        {
            lock (locks[distanceLevel])
                connections[distanceLevel].Remove(connection);

            List<NodeInformation> nodeInfos;
            List<NodeInformation> nodeInfosConnected;
            lock (kbucketsLocks[distanceLevel])
                nodeInfos = new List<NodeInformation>(kbuckets[distanceLevel]);
            lock (outboundConnectionsLock[distanceLevel])
                nodeInfosConnected = new List<NodeInformation>(outboundConnections[distanceLevel].Select((elem) => elem.nodeInfo));
            lock (inboundConnectionsLock[distanceLevel])
                nodeInfosConnected.AddRange(inboundConnections[distanceLevel].Select((elem) => elem.nodeInfo));

            foreach (var nodeInfo in nodeInfos)
                if (connections[distanceLevel].Count < max)
                {
                    if (!nodeInfosConnected.Contains(nodeInfo))
                        Connect(nodeInfo, false, () => { }, null);
                }
                else
                    break;
        }

        private void Continue(NodeInformation nodeInfo, SocketChannel sc, Action<string> _ConsoleWriteLine)
        {
            _ConsoleWriteLine("常時接続");

            sc.Sessioned += (sender, e) =>
            {
                try
                {
                    _ConsoleWriteLine("新しいセッション");

                    InboundProtocol(nodeInfo, e, _ConsoleWriteLine);
                }
                catch (Exception ex)
                {
                    this.RaiseError("inbound_session", 5, ex);
                }
                finally
                {
                    e.Close();

                    _ConsoleWriteLine("セッション終わり");
                }
            };
        }

        protected override SHAREDDATA[] Request(NodeInformation nodeInfo, Message message, params SHAREDDATA[] datas)
        {
            Connection connection = null;
            if (isInitialized)
            {
                int distanceLevel = GetDistanceLevel(myNodeInfo);

                if (outboundConnections[distanceLevel].Count > 0)
                    lock (outboundConnectionsLock[distanceLevel])
                        connection = outboundConnections[distanceLevel].Where((elem) => elem.nodeInfo.Equals(nodeInfo)).FirstOrDefault();

                if (connection == null)
                    if (inboundConnections[distanceLevel].Count > 0)
                        lock (inboundConnectionsLock[distanceLevel])
                            connection = inboundConnections[distanceLevel].Where((elem) => elem.nodeInfo.Equals(nodeInfo)).FirstOrDefault();
            }

            if (connection == null)
                return Connect(nodeInfo, true, () => { }, message, datas);

            SessionChannel sc2 = null;
            try
            {
                sc2 = connection.sc.NewSession();

                connection._ConsoleWriteLine("新しいセッション");

                return OutboundProtocol(nodeInfo, message, datas, sc2, connection._ConsoleWriteLine);
            }
            catch (Exception ex)
            {
                this.RaiseError("outbound_session", 5, ex);
            }
            finally
            {
                if (sc2 != null)
                {
                    sc2.Close();

                    connection._ConsoleWriteLine("セッション終わり");
                }
            }

            return null;
        }

        protected override void Diffuse(NodeInformation source, Message message, params SHAREDDATA[] datas)
        {
            if (!isInitialized)
                throw new InvalidOperationException("not_yet_connections_keeped");

            List<Connection> connections = new List<Connection>();
            for (int i = 0; i < NodeIdSizeBit; i++)
            {
                if (outboundConnections[i].Count > 0)
                    lock (outboundConnectionsLock[i])
                        connections.AddRange(outboundConnections[i]);
                if (inboundConnections[i].Count > 0)
                    lock (inboundConnectionsLock[i])
                        connections.AddRange(inboundConnections[i]);
            }

            foreach (Connection connection in connections)
            {
                if (source != null && connection.nodeInfo.Equals(source))
                    continue;

                SessionChannel sc2 = null;
                try
                {
                    sc2 = connection.sc.NewSession();

                    connection._ConsoleWriteLine("新しいセッション");

                    OutboundProtocol(connection.nodeInfo, message, datas, sc2, connection._ConsoleWriteLine);
                }
                catch (Exception ex)
                {
                    this.RaiseError("diffuse", 5, ex);
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
        }

        private void Initialize()
        {
            kbuckets = new List<NodeInformation>[NodeIdSizeBit];
            kbucketsLocks = new object[NodeIdSizeBit];

            outboundConnections = new List<Connection>[NodeIdSizeBit];
            outboundConnectionsLock = new object[NodeIdSizeBit];
            inboundConnections = new List<Connection>[NodeIdSizeBit];
            inboundConnectionsLock = new object[NodeIdSizeBit];

            for (int i = 0; i < NodeIdSizeBit; i++)
            {
                kbuckets[i] = new List<NodeInformation>();
                kbucketsLocks[i] = new object();

                outboundConnections[i] = new List<Connection>();
                outboundConnectionsLock[i] = new object();
                inboundConnections[i] = new List<Connection>();
                inboundConnectionsLock[i] = new object();
            }

            isInitialized = true;
        }

        //<未実装>別スレッドで常時動かすべき？
        protected override void KeepConnections()
        {
            if (myNodeInfo != null)
            {
                dhtNodeInfo = myNodeInfo;

                Initialize();
            }

            if (firstNodeInfos.Length == 0)
            {
                //<未実装>初期ノード情報がない場合の処理
                //選択肢1 -> 10分くらい待って再度初期ノード情報取得
                //選択肢2 -> 使用者に任せる（使用者によって手動で初期ノード情報が追加された時に常時接続再実行）

                return;
            }

            List<NodeInformation> nodeInfos = new List<NodeInformation>();
            for (int i = 0; i < firstNodeInfos.Length && nodeInfos.Count < keepConnectionNodeInfosMin; i++)
            {
                SHAREDDATA[] resDatas = Connect(firstNodeInfos[i].ipAddress, firstNodeInfos[i].portNumber, true, () => { }, new Message(MessageName.reqNodeInfos, 0));
                ResNodeInfos resNodeInfos;
                if (resDatas != null && resDatas.Length == 1 && (resNodeInfos = resDatas[0] as ResNodeInfos) != null)
                    //<要検討>更新時間順に並び替えるべき？
                    nodeInfos.AddRange(resNodeInfos.nodeInfos);
            }
            if (nodeInfos.Count == 0)
            {
                //<未実装>初期ノードからノード情報を取得できなかった場合の処理
                //選択肢1 -> 10分くらい待って再度ノード情報取得
                //選択肢2 -> 使用者に任せる（使用者によって手動で初期ノード情報が追加された時に常時接続再実行）

                return;
            }

            if (myNodeInfo == null)
            {
                dhtNodeInfo = nodeInfos[0];

                Initialize();
            }

            foreach (var nodeInfo in nodeInfos)
            {
                int distanceLevel = GetDistanceLevel(nodeInfo);
                if (distanceLevel != -1)
                    lock (kbucketsLocks[distanceLevel])
                        kbuckets[distanceLevel].Add(nodeInfo);
            }

            for (int i = 0; i < NodeIdSizeBit; i++)
            {
                if (kbuckets[i].Count == 0 || outboundConnections[i].Count >= outboundConnectionsMax)
                    continue;

                lock (kbucketsLocks[i])
                    nodeInfos = new List<NodeInformation>(kbuckets[i]);

                foreach (var nodeInfo in nodeInfos)
                    if (outboundConnections[i].Count < outboundConnectionsMax)
                    {
                        if (!IsAlreadyConnected(nodeInfo))
                            Connect(nodeInfo, false, () => { }, null);
                    }
                    else
                        break;
            }

            this.RaiseNotification("keep_conn_completed", 5);
        }

        public class DistanceParameter
        {
            public DistanceParameter(int _hashByteMin, int _hashByteMax, int _minus)
            {
                hashByteMin = _hashByteMin;
                hashByteMax = _hashByteMax;
                minus = _minus;
            }

            public int hashByteMin { get; private set; }
            public int hashByteMax { get; private set; }
            public int minus { get; private set; }
        }

        private static readonly DistanceParameter[] distanceParameters = new DistanceParameter[]{
            new DistanceParameter(0, 0, 8), 
            new DistanceParameter(1, 1, 7), 
            new DistanceParameter(2, 3, 6), 
            new DistanceParameter(4, 7, 5), 
            new DistanceParameter(8, 15, 4), 
            new DistanceParameter(16, 31, 3), 
            new DistanceParameter(32, 63, 2), 
            new DistanceParameter(64, 127, 1), 
            new DistanceParameter(128, 255, 0), 
        };

        private int GetDistanceLevel(NodeInformation nodeInfo2)
        {
            Sha256Hash xor = dhtNodeInfo.Id.XOR(nodeInfo2.Id);

            int distanceLevel = NodeIdSizeBit - 1;

            int? minus = null;
            for (int i = 0, j = 0; i < NodeIdSizeByte && (!minus.HasValue || minus.Value == 8); i++)
                for (j = 0, minus = null; j < distanceParameters.Length && minus == null; j++)
                    if (xor.hash[i] >= distanceParameters[j].hashByteMin && xor.hash[i] <= distanceParameters[j].hashByteMax)
                        distanceLevel -= (minus = distanceParameters[j].minus).Value;

            return distanceLevel;
        }
    }

    #endregion

    public class FirstNodeInformation : SHAREDDATA, IEquatable<FirstNodeInformation>
    {
        public FirstNodeInformation() : this((int?)null) { }

        public FirstNodeInformation(int? _version) : base(_version) { }

        public FirstNodeInformation(IPAddress _ipAddress, ushort _port, Network _network) : this(null, _ipAddress, _port, _network) { }

        public FirstNodeInformation(int? _version, IPAddress _ipAddress, ushort _portNumber, Network _network)
            : base(_version)
        {
            if (_ipAddress.AddressFamily != AddressFamily.InterNetwork && _ipAddress.AddressFamily != AddressFamily.InterNetworkV6)
                throw new ArgumentException("first_node_info_ip_address");
            if (_portNumber == 0)
                throw new ArgumentException("first_node_info_port");

            ipAddress = _ipAddress;
            portNumber = _portNumber;
            network = _network;
        }

        public FirstNodeInformation(string _hex) { Hex = _hex; }

        public IPAddress ipAddress { get; private set; }
        public ushort portNumber { get; private set; }
        public Network network { get; private set; }

        public string Hex
        {
            get
            {
                byte[] plainBytes = ipAddress.GetAddressBytes().Combine(BitConverter.GetBytes(portNumber), BitConverter.GetBytes((int)network));
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
                using (MemoryStream ms = new MemoryStream(value.FromHexstringToBytes()))
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
                portNumber = BitConverter.ToUInt16(portBytes, 0);
                network = (Network)BitConverter.ToInt32(networkBytes, 0);

                if (portNumber == 0)
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

        public override bool Equals(object obj) { return (obj as FirstNodeInformation).Pipe((o) => o != null && Equals(o)); }

        public override int GetHashCode() { return ipAddress.GetHashCode() ^ portNumber.GetHashCode(); }

        public override string ToString() { return ipAddress + ":" + portNumber.ToString(); }

        public bool Equals(FirstNodeInformation other) { return ipAddress.ToString() == other.ipAddress.ToString() && portNumber == other.portNumber; }
    }

    public class NodeInformation : FirstNodeInformation, IEquatable<NodeInformation>
    {
        public NodeInformation()
            : base(0)
        {
            idCache = new CachedData<Sha256Hash>(() => new Sha256Hash(ipAddress.GetAddressBytes().Combine(BitConverter.GetBytes(portNumber), Encoding.UTF8.GetBytes(publicRSAParameters))));
        }

        public NodeInformation(IPAddress _ipAddress, ushort _portNumber, Network _network, string _publicRSAParameters)
            : base(0, _ipAddress, _portNumber, _network)
        {
            participation = DateTime.Now;
            publicRSAParameters = _publicRSAParameters;

            idCache = new CachedData<Sha256Hash>(() => new Sha256Hash(ipAddress.GetAddressBytes().Combine(BitConverter.GetBytes(portNumber), Encoding.UTF8.GetBytes(publicRSAParameters))));
        }

        public DateTime participation { get; private set; }
        public string publicRSAParameters { get; private set; }

        private CachedData<Sha256Hash> idCache;
        public Sha256Hash Id { get { return idCache.Data; } }

        //型変換ではなく新しいオブジェクトを作成しないとSHAREDDATA.ToBinaryで例外が発生する
        public FirstNodeInformation FirstNodeInfo { get { return new FirstNodeInformation(ipAddress, portNumber, network); } }

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

        public override bool IsVersioned { get { return true; } }
        public override bool IsCorruptionChecked
        {
            get
            {
                if (Version <= 0)
                    return false;
                else
                    throw new NotSupportedException("node_info_corruption_checked");
            }
        }

        public override bool Equals(object obj) { return (obj as NodeInformation).Pipe((o) => o != null && Equals(o)); }

        public override int GetHashCode() { return Id.GetHashCode(); }

        public override string ToString() { return Id.ToString(); }

        public bool Equals(NodeInformation other) { return Id.Equals(other.Id); }
    }

    #endregion

    #region Cremlia実装

    public abstract class CremliaIdFactory<T> : ICremliaIdFactory where T : HASHBASE
    {
        public virtual ICremliaId Create() { return new CremliaId<T>(); }
    }

    public class CremliaIdFactorySha256 : CremliaIdFactory<Sha256Hash> { }

    public class CremliaId<T> : ICremliaId, IComparable<CremliaId<T>>, IEquatable<CremliaId<T>>, IComparable where T : HASHBASE
    {
        public CremliaId() : this(Activator.CreateInstance(typeof(T)) as T) { }

        public CremliaId(T _hash) { hash = _hash; }

        public readonly T hash;

        public int Size { get { return hash.SizeBit; } }

        public byte[] Bytes { get { return hash.hash; } }

        public void FromBytes(byte[] bytes)
        {
            if (bytes.Length != Bytes.Length)
                throw new ArgumentException("cremlia_id_bytes_length");

            hash.FromHash(bytes);
        }

        public ICremliaId XOR(ICremliaId id)
        {
            if (id.Size != Size)
                throw new ArgumentException("not_equal_hash");

            byte[] xorBytes = new byte[Bytes.Length];
            for (int i = 0; i < xorBytes.Length; i++)
                xorBytes[i] = (byte)(Bytes[i] ^ id.Bytes[i]);
            return new CremliaId<T>(HASHBASE.FromHash<T>(xorBytes));
        }

        public override bool Equals(object obj)
        {
            if (!(obj is CremliaId<T>))
                return false;
            return hash.Equals((obj as CremliaId<T>).hash);
        }

        public bool Equals(CremliaId<T> other) { return hash.Equals(other); }

        public int CompareTo(object obj) { return hash.CompareTo((obj as CremliaId<T>).hash); }

        public int CompareTo(CremliaId<T> other) { return hash.CompareTo(other.hash); }

        public override int GetHashCode() { return hash.GetHashCode(); }

        public override string ToString() { return hash.ToString(); }
    }

    public class CremliaIdSha256 : CremliaId<Sha256Hash> { }

    public abstract class CremliaNodeInfomation<T> : ICremliaNodeInfomation, IEquatable<CremliaNodeInfomation<T>> where T : HASHBASE
    {
        public abstract ICremliaId Id { get; }

        public virtual bool Equals(CremliaNodeInfomation<T> other) { return Id.Equals(other.Id); }
    }

    public class CremliaNodeInfomationSha256 : CremliaNodeInfomation<Sha256Hash>
    {
        public CremliaNodeInfomationSha256(NodeInformation _nodeInfo) { nodeInfo = _nodeInfo; }

        public readonly NodeInformation nodeInfo;

        public override ICremliaId Id { get { return new CremliaId<Sha256Hash>(nodeInfo.Id); } }

        public override bool Equals(object obj) { return (obj as CremliaNodeInfomationSha256).Pipe((o) => o != null && Equals(o)); }

        public override int GetHashCode() { return Id.GetHashCode(); }

        public override string ToString() { return Id.ToString(); }
    }

    public class CremliaDatabaseIo : ICremliaDatabaseIo
    {
        private int tExpire;
        public int TExpire { set { tExpire = value; } }

        public byte[] Get(ICremliaId id) { throw new NotImplementedException(); }

        public Tuple<ICremliaId, byte[]>[] GetOriginals() { throw new NotImplementedException(); }

        public Tuple<ICremliaId, byte[]>[] GetCharges() { throw new NotImplementedException(); }

        public void Set(ICremliaId id, byte[] data, bool isOriginal, bool isCache) { throw new NotImplementedException(); }
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
                    this.RaiseError("find_table_already_added", 5, xor.ToString(), findTable[xor].Id.ToString(), nodeInfo.Id.ToString());

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

        public ICremliaNodeInfomation[] GetKbuckets()
        {
            List<ICremliaNodeInfomation> nodeInfos = new List<ICremliaNodeInfomation>();
            for (int i = 0; i < kbuckets.Length; i++)
                lock (kbuckets[i])
                    for (int j = 0; j < kbuckets[i].Count; j++)
                        nodeInfos.Add(kbuckets[i][j]);
            return nodeInfos.ToArray();
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
                this.RaiseWarning("my_node_info", 5);
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
                this.RaiseWarning("my_node_info", 5);
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
        public FindNodesReqMessage(ICremliaId _id) { id = _id; }

        public readonly ICremliaId id;
    }

    public class NeighborNodesMessage : CremliaMessageBase
    {
        public NeighborNodesMessage(ICremliaNodeInfomation[] _nodeInfos) { nodeInfos = _nodeInfos; }

        public readonly ICremliaNodeInfomation[] nodeInfos;
    }

    public class FindValueReqMessage : CremliaMessageBase
    {
        public FindValueReqMessage(ICremliaId _id) { id = _id; }

        public readonly ICremliaId id;
    }

    public class ValueMessage : CremliaMessageBase
    {
        public ValueMessage(byte[] _data) { data = _data; }

        public readonly byte[] data;
    }

    public class GetIdsAndValuesReqMessage : CremliaMessageBase { }

    public class IdsAndValuesMessage : CremliaMessageBase
    {
        public IdsAndValuesMessage(Tuple<ICremliaId, byte[]>[] _idsAndValues) { idsAndValues = _idsAndValues; }

        public readonly Tuple<ICremliaId, byte[]>[] idsAndValues;
    }

    #endregion
}