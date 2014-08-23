//がをがを～！
//作譜者：@pizyumi

#define TEST

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Numerics;
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
    public abstract class COREBASE<KeyPairType, DsaPubKeyType, DsaPrivKeyType, BlockidHashType, TxidHashType, PubKeyHashType>
        where KeyPairType : DSAKEYPAIRBASE<DsaPubKeyType, DsaPrivKeyType>
        where DsaPubKeyType : DSAPUBKEYBASE
        where DsaPrivKeyType : DSAPRIVKEYBASE
        where BlockidHashType : HASHBASE
        where TxidHashType : HASHBASE
        where PubKeyHashType : HASHBASE
    {
        public COREBASE(string _basePath)
        {
            //Coreが2回以上実体化されないことを保証する
            //2回以上呼ばれた際には例外が発生する
            Instantiate();

            basepath = _basePath;
            databaseBasepath = Path.Combine(basepath, databaseDirectory);
        }

        private static readonly Action Instantiate = OneTime.GetOneTime();

        private static readonly string databaseDirectory = "database";

        private AccountHoldersDatabase ahDatabase;
        private BlockChainDatabase bcDatabase;
        private BlockNodesGroupDatabase bngDatabase;
        private BlockGroupDatabase bgDatabase;
        private UtxoDatabase utxoDatabase;
        private AddressEventDatabase addressEventDatabase;

        public AccountHolders<KeyPairType, DsaPubKeyType, DsaPrivKeyType> accountHolders { get; private set; }
        public IAccountHolders iAccountHolders { get { return accountHolders; } }

        private AccountHoldersFactory<KeyPairType, DsaPubKeyType, DsaPrivKeyType> accountHoldersFactory;
        public IAccountHoldersFactory iAccountHoldersFactory { get { return accountHoldersFactory; } }

        private BlockChain<BlockidHashType, TxidHashType, PubKeyHashType, DsaPubKeyType> blockChain;

        private Mining<BlockidHashType, TxidHashType, PubKeyHashType, DsaPubKeyType> mining;

        private string basepath;
        private string databaseBasepath;
        private bool isSystemStarted;

        private CachedData<CurrencyUnit> usableBalanceCache;
        public CurrencyUnit UsableBalance { get { return usableBalanceCache.Data; } }

        private CachedData<CurrencyUnit> unusableBalanceCache;
        public CurrencyUnit UnusableBalance { get { return unusableBalanceCache.Data; } }

        public CurrencyUnit Balance { get { return new CurrencyUnit(UsableBalance.rawAmount + UnusableBalance.rawAmount); } }

        public event EventHandler BalanceUpdated = delegate { };

        public void StartSystem()
        {
            if (isSystemStarted)
                throw new InvalidOperationException("core_started");

            ahDatabase = new AccountHoldersDatabase(databaseBasepath);
            bcDatabase = new BlockChainDatabase(databaseBasepath);
            bngDatabase = new BlockNodesGroupDatabase(databaseBasepath);
            bgDatabase = new BlockGroupDatabase(databaseBasepath);
            utxoDatabase = new UtxoDatabase(databaseBasepath);
            addressEventDatabase = new AddressEventDatabase(databaseBasepath);

            accountHolders = new AccountHolders<KeyPairType, DsaPubKeyType, DsaPrivKeyType>();
            accountHoldersFactory = new AccountHoldersFactory<KeyPairType, DsaPubKeyType, DsaPrivKeyType>();

            byte[] ahDataBytes = ahDatabase.GetData();
            if (ahDataBytes.Length != 0)
                accountHolders.FromBinary(ahDataBytes);

            usableBalanceCache = new CachedData<CurrencyUnit>(() =>
            {
                CurrencyUnit cu = new CurrencyUnit(0);
                foreach (var accountHolder in accountHolders.AllAccountHolders)
                    foreach (var account in accountHolder.Accounts)
                        cu = new CurrencyUnit(cu.rawAmount + account.usableAmount.rawAmount);
                return cu;
            });
            unusableBalanceCache = new CachedData<CurrencyUnit>(() =>
            {
                CurrencyUnit cu = new CurrencyUnit(0);
                foreach (var accountHolder in accountHolders.AllAccountHolders)
                    foreach (var account in accountHolder.Accounts)
                        cu = new CurrencyUnit(cu.rawAmount + account.unusableAmount.rawAmount);
                return cu;
            });

            blockChain = new BlockChain<BlockidHashType, TxidHashType, PubKeyHashType, DsaPubKeyType>(bcDatabase, bngDatabase, bgDatabase, utxoDatabase, addressEventDatabase);
            blockChain.Initialize();

            Dictionary<Account<KeyPairType, DsaPubKeyType, DsaPrivKeyType>, EventHandler<Tuple<CurrencyUnit, CurrencyUnit>>> changeAmountDict = new Dictionary<Account<KeyPairType, DsaPubKeyType, DsaPrivKeyType>, EventHandler<Tuple<CurrencyUnit, CurrencyUnit>>>();

            Action _UpdateBalance = () =>
            {
                usableBalanceCache.IsModified = true;
                unusableBalanceCache.IsModified = true;

                BalanceUpdated(this, EventArgs.Empty);
            };

            Action<Account<KeyPairType, DsaPubKeyType, DsaPrivKeyType>> _AddAddressEvent = (account) =>
            {
                EventHandler<Tuple<CurrencyUnit, CurrencyUnit>> eh = (sender, e) => account.ChangeAmount(e.Item1, e.Item2);

                changeAmountDict.Add(account, eh);

                AddressEvent<PubKeyHashType> addressEvent = new AddressEvent<PubKeyHashType>(Activator.CreateInstance(typeof(PubKeyHashType), account.keyPair.pubKey.pubKey) as PubKeyHashType);
                addressEvent.BalanceUpdated += eh;

                blockChain.AddAddressEvent(addressEvent);

                _UpdateBalance();
            };

            EventHandler<Account<KeyPairType, DsaPubKeyType, DsaPrivKeyType>> _AccountAdded = (sender, e) => _AddAddressEvent(e);
            EventHandler<Account<KeyPairType, DsaPubKeyType, DsaPrivKeyType>> _AccountRemoved = (sender, e) =>
            {
                EventHandler<Tuple<CurrencyUnit, CurrencyUnit>> eh = changeAmountDict[e];

                changeAmountDict.Remove(e);

                AddressEvent<PubKeyHashType> addressEvent = blockChain.RemoveAddressEvent(Activator.CreateInstance(typeof(PubKeyHashType), e.keyPair.pubKey.pubKey) as PubKeyHashType);
                addressEvent.BalanceUpdated -= eh;

                _UpdateBalance();
            };

            foreach (var accountHolder in accountHolders.AllAccountHolders)
            {
                foreach (var account in accountHolder.Accounts)
                    _AddAddressEvent(account);

                accountHolder.AccountAdded += _AccountAdded;
                accountHolder.AccountRemoved += _AccountRemoved;
            }
            accountHolders.AccountHolderAdded += (sender, e) =>
            {
                e.AccountAdded += _AccountAdded;
                e.AccountRemoved += _AccountRemoved;
            };
            accountHolders.AccountHolderRemoved += (semder, e) =>
            {
                e.AccountAdded -= _AccountAdded;
                e.AccountRemoved -= _AccountRemoved;
            };

            blockChain.BalanceUpdated += (sender, e) => _UpdateBalance();

            _UpdateBalance();

            mining = new Mining<BlockidHashType, TxidHashType, PubKeyHashType, DsaPubKeyType>();

            isSystemStarted = true;
        }

        public void EndSystem()
        {
            if (!isSystemStarted)
                throw new InvalidOperationException("core_not_started");

            blockChain.SaveWhenExit();

            ahDatabase.UpdateData(accountHolders.ToBinary());

            isSystemStarted = false;
        }

        private EventHandler<TransactionalBlock<BlockidHashType, TxidHashType, PubKeyHashType, DsaPubKeyType>> _ContinueMine;

        public void StartMining(IAccount iAccount)
        {
            Account<KeyPairType, DsaPubKeyType, DsaPrivKeyType> account = iAccount as Account<KeyPairType, DsaPubKeyType, DsaPrivKeyType>;
            if (account == null)
                throw new ArgumentException("iaccount_type");

            Action _Mine = () =>
            {
                mining.NewMiningBlock(TransactionalBlock<BlockidHashType, TxidHashType, PubKeyHashType, DsaPubKeyType>.GetBlockTemplate(blockChain.head + 1, Activator.CreateInstance(typeof(PubKeyHashType), account.keyPair.pubKey.pubKey) as PubKeyHashType, (index) => blockChain.GetMainBlock(index)));
            };

            _ContinueMine = (sender, e) =>
            {
                blockChain.AddBlock(e);

                _Mine();
            };

            mining.FoundNonce += _ContinueMine;
            mining.Start();

            _Mine();
        }

        public void EndMining()
        {
            mining.End();
            mining.FoundNonce -= _ContinueMine;
        }
    }

    public class Core : COREBASE<Ecdsa256KeyPair, Ecdsa256PubKey, Ecdsa256PrivKey, X15Hash, Sha256Sha256Hash, Sha256Ripemd160Hash>
    {
        public Core(string _basePath) : base(_basePath) { }
    }

    public class AddressEvent<PubKeyHashType> where PubKeyHashType : HASHBASE
    {
        public AddressEvent(PubKeyHashType _address) { address = _address; }

        public PubKeyHashType address { get; private set; }

        public event EventHandler<Tuple<CurrencyUnit, CurrencyUnit>> BalanceUpdated = delegate { };
        public event EventHandler<CurrencyUnit> UsableBalanceUpdated = delegate { };
        public event EventHandler<CurrencyUnit> UnusableBalanceUpdated = delegate { };

        public void RaiseBalanceUpdated(Tuple<CurrencyUnit, CurrencyUnit> cus) { BalanceUpdated(this, cus); }
        public void RaiseUsableBalanceUpdated(CurrencyUnit cu) { UsableBalanceUpdated(this, cu); }
        public void RaiseUnusableBalanceUpdated(CurrencyUnit cu) { UnusableBalanceUpdated(this, cu); }
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
                    this.RaiseError("socket_channel_write", 5, ex);

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
                    this.RaiseError("socket_channel_read", 5, ex);

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
                    this.RaiseError("outbound_chennel", 5, ex);

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
                                this.RaiseError("inbound_channel", 5, ex);

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
                    this.RaiseNotification("port0", 5);
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
                                this.RaiseError("ric", 5, ex);

                                e.Close();
                            }
                        };
                        ric.AcceptanceFailed += (sender, e) => RegisterResult(e, false);
                        ric.Failed += (sender, e) => { };
                        ric.RequestAcceptanceStart();

                        this.RaiseNotification("server_started", 5, ipAddress.ToString(), portNumber.ToString());

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
                    this.RaiseError("roc", 5, ex);

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

    public class Message<U> : SHAREDDATA where U : HASHBASE
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
                if (messageBase is Inv<U>)
                    return MessageName.inv;
                else if (messageBase is Getdata<U>)
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
                        messageBase = new Inv<U>();
                    else if (mn == MessageName.getdata)
                        messageBase = new Getdata<U>();
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

    public abstract class MessageHash<U> : MessageBase where U : HASHBASE
    {
        public MessageHash(U _hash) : this(null, _hash) { }

        public MessageHash(int? _version, U _hash)
            : base(_version)
        {
            hash = _hash;
        }

        public U hash { get; private set; }

        protected override Func<ReaderWriter, IEnumerable<MainDataInfomation>> StreamInfo
        {
            get
            {
                return (msrw) => new MainDataInfomation[]{
                    new MainDataInfomation(typeof(U), null, () => hash, (o) => hash = (U)o),
                };
            }
        }
    }

    public class Inv<U> : MessageHash<U> where U : HASHBASE
    {
        public Inv() : this(Activator.CreateInstance(typeof(U)) as U) { }

        public Inv(U _hash) : base(0, _hash) { }

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

    public class Getdata<U> : MessageHash<U> where U : HASHBASE
    {
        public Getdata() : this(Activator.CreateInstance(typeof(U)) as U) { }

        public Getdata(U _hash) : base(0, _hash) { }

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

    public class Header<T> : SHAREDDATA where T : HASHBASE
    {
        public Header() : base(0) { }

        public Header(NodeInformation<T> _nodeInfo, int _creaVersion, int _protocolVersion, string _client, bool _isTemporary)
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

        public NodeInformation<T> nodeInfo { get; private set; }
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
                        yield return new MainDataInfomation(typeof(NodeInformation<T>), 0, () => nodeInfo, (o) => nodeInfo = (NodeInformation<T>)o);
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

    public class HeaderResponse<T> : SHAREDDATA where T : HASHBASE
    {
        public HeaderResponse() : base(0) { }

        public HeaderResponse(NodeInformation<T> _nodeInfo, bool _isSameNetwork, bool _isAlreadyConnected, NodeInformation<T> _correctNodeInfo, bool _isOldCreaVersion, int _protocolVersion, string _client)
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

        public NodeInformation<T> nodeInfo { get; private set; }
        public bool isSameNetwork { get; private set; }
        public bool isAlreadyConnected { get; private set; }
        public NodeInformation<T> correctNodeInfo { get; private set; }
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
                    yield return new MainDataInfomation(typeof(NodeInformation<T>), 0, () => nodeInfo, (o) => nodeInfo = (NodeInformation<T>)o);
                    yield return new MainDataInfomation(typeof(bool), () => isSameNetwork, (o) => isSameNetwork = (bool)o);
                    bool isCorrectNodeInfo = correctNodeInfo == null;
                    yield return new MainDataInfomation(typeof(bool), () => isCorrectNodeInfo, (o) => isCorrectNodeInfo = (bool)o);
                    if (!isCorrectNodeInfo)
                        yield return new MainDataInfomation(typeof(NodeInformation<T>), 0, () => correctNodeInfo, (o) => correctNodeInfo = (NodeInformation<T>)o);
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

    public class StoreReq<IdType> : MessageBase where IdType : HASHBASE
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

    public class FindNodesReq<IdType> : MessageBase where IdType : HASHBASE
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

    public class NeighborNodes<T> : MessageBase where T : HASHBASE
    {
        public NeighborNodes(NodeInformation<T>[] _nodeInfos)
            : base(0)
        {
            nodeInfos = _nodeInfos;
        }

        private NodeInformation<T>[] nodeInfos;
        public NodeInformation<T>[] NodeInfos
        {
            get { return nodeInfos.ToArray(); }
        }

        protected override Func<ReaderWriter, IEnumerable<MainDataInfomation>> StreamInfo
        {
            get
            {
                if (Version == 0)
                    return (msrw) => new MainDataInfomation[]{
                        new MainDataInfomation(typeof(NodeInformation<T>[]), 0, null, () => nodeInfos, (o) => nodeInfos = (NodeInformation<T>[])o),
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

    public class FindValueReq<IdType> : MessageBase where IdType : HASHBASE
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

    public class IdsAndValues<IdType> : MessageBase where IdType : HASHBASE
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

    public abstract class CREANODEBASE2<T> : P2PNODE2 where T : HASHBASE
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

        public NodeInformation<T> nodeInfo { get; private set; }

        protected abstract bool IsContinue { get; }

        protected abstract bool IsAlreadyConnected(NodeInformation<T> nodeInfo);
        protected abstract void UpdateNodeState(NodeInformation<T> nodeInfo, bool isSucceeded);
        protected abstract bool IsListenerCanContinue(NodeInformation<T> nodeInfo);
        protected abstract bool IsWantToContinue(NodeInformation<T> nodeInfo);
        protected abstract bool IsClientCanContinue(NodeInformation<T> nodeInfo);
        protected abstract void InboundProtocol(IChannel sc, Action<string> _ConsoleWriteLine);
        protected abstract void OutboundProtocol(MessageBase[] messages, IChannel sc, Action<string> _ConsoleWriteLine);
        protected abstract void InboundContinue(NodeInformation<T> nodeInfo, SocketChannel sc, Action<string> _ConsoleWriteLine);
        protected abstract void OutboundContinue(NodeInformation<T> nodeInfo, SocketChannel sc, Action<string> _ConsoleWriteLine);
        protected abstract void Request(NodeInformation<T> nodeinfo, params MessageBase[] messages);
        protected abstract void Diffuse(params MessageBase[] messages);

        protected override void CreateNodeInfo()
        {
            using (RSACryptoServiceProvider rsacsp = new RSACryptoServiceProvider(2048))
            {
                rsacsp.FromXmlString(privateRsaParameters);
                nodeInfo = new NodeInformation<T>(ipAddress, portNumber, Network, rsacsp.ToXmlString(false));
            }
        }

        protected override void OnAccepted(SocketChannel sc)
        {
            Header<T> header = SHAREDDATA.FromBinary<Header<T>>(sc.ReadBytes());

            NodeInformation<T> aiteNodeInfo = null;
            if (!header.nodeInfo.IpAddress.Equals(sc.aiteIpAddress))
            {
                this.RaiseNotification("aite_wrong_node_info", 5, sc.aiteIpAddress.ToString(), header.nodeInfo.PortNumber.ToString());

                aiteNodeInfo = new NodeInformation<T>(sc.aiteIpAddress, header.nodeInfo.PortNumber, header.nodeInfo.Network, header.nodeInfo.PublicRSAParameters);
            }

            HeaderResponse<T> headerResponse = new HeaderResponse<T>(nodeInfo, header.nodeInfo.Network == Network, IsAlreadyConnected(header.nodeInfo), aiteNodeInfo, header.creaVersion < creaVersion, protocolVersion, appnameWithVersion);

            if (aiteNodeInfo == null)
                aiteNodeInfo = header.nodeInfo;

            if ((!headerResponse.isSameNetwork).RaiseNotification(GetType(), "aite_wrong_network", 5, aiteNodeInfo.IpAddress.ToString(), aiteNodeInfo.PortNumber.ToString()))
            {
                sc.Close();

                return;
            }
            if (headerResponse.isAlreadyConnected.RaiseNotification(GetType(), "aite_already_connected", 5, aiteNodeInfo.IpAddress.ToString(), aiteNodeInfo.PortNumber.ToString()))
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

            sc.WriteBytes(new Header<T>(nodeInfo, creaVersion, protocolVersion, appnameWithVersion, isTemporary).ToBinary());
            HeaderResponse<T> headerResponse = SHAREDDATA.FromBinary<HeaderResponse<T>>(sc.ReadBytes());

            if ((!headerResponse.isSameNetwork).RaiseNotification(GetType(), "wrong_network", 5, aiteIpAddress.ToString(), aitePortNumber.ToString()))
            {
                sc.Close();

                return;
            }
            if (headerResponse.isAlreadyConnected.RaiseNotification(GetType(), "already_connected", 5, aiteIpAddress.ToString(), aitePortNumber.ToString()))
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

    public abstract class CreaNodeLocalTest2<T, U> : CREANODEBASE2<T>
        where T : HASHBASE
        where U : HASHBASE
    {
        public CreaNodeLocalTest2(ushort _portNumber, int _creaVersion, string _appnameWithVersion) : base(_portNumber, _creaVersion, _appnameWithVersion) { }

        private static readonly object fnisLock = new object();
        private static readonly List<FirstNodeInformation> fnis = new List<FirstNodeInformation>();
        private static readonly string testPrivateRsaParameters;

        //試験用
        private readonly Dictionary<U, byte[]> txtests = new Dictionary<U, byte[]>();
        private readonly object txtestsLock = new object();

        public event EventHandler<NodeInformation<T>> TxtestReceived = delegate { };
        public event EventHandler<NodeInformation<T>> TxtestAlreadyExisted = delegate { };

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
            Message<U> message = SHAREDDATA.FromBinary<Message<U>>(sc.ReadBytes());
            if (message.name == MessageName.inv)
            {
                Inv<U> inv = message.messageBase as Inv<U>;
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
            Message<U> message = new Message<U>(messages[0]);

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
        public void DiffuseInv(TxTest txtest, Inv<U> inv)
        {
            if (txtest == null && inv == null)
            {
                txtest = new TxTest();
                inv = new Inv<U>(Activator.CreateInstance(typeof(U), txtest.data) as U);

                lock (txtestsLock)
                    txtests.Add(inv.hash, txtest.data);

                (string.Join(":", ipAddress.ToString(), portNumber.ToString()) + " txtest作成").ConsoleWriteLine();
            }

            Diffuse(inv, txtest);
        }
    }

    public class CreaNodeLocalTestNotContinue2<T, U> : CreaNodeLocalTest2<T, U>
        where T : HASHBASE
        where U : HASHBASE
    {
        public CreaNodeLocalTestNotContinue2(ushort _portNumber, int _creaVersion, string _appnameWithVersion) : base(_portNumber, _creaVersion, _appnameWithVersion) { }

        protected override bool IsContinue
        {
            get { return false; }
        }

        protected override bool IsAlreadyConnected(NodeInformation<T> nodeInfo) { return false; }

        protected override void UpdateNodeState(NodeInformation<T> nodeInfo, bool isSucceeded) { }

        protected override bool IsListenerCanContinue(NodeInformation<T> nodeInfo) { return false; }

        protected override bool IsWantToContinue(NodeInformation<T> nodeInfo) { return false; }

        protected override bool IsClientCanContinue(NodeInformation<T> nodeInfo) { return false; }

        protected override void InboundContinue(NodeInformation<T> nodeInfo, SocketChannel sc, Action<string> _ConsoleWriteLine) { }

        protected override void OutboundContinue(NodeInformation<T> nodeInfo, SocketChannel sc, Action<string> _ConsoleWriteLine) { }

        protected override void Request(NodeInformation<T> nodeinfo, params MessageBase[] messages)
        {
            Connect(nodeinfo.IpAddress, nodeinfo.PortNumber, true, () => { }, messages);
        }

        protected override void Diffuse(params MessageBase[] messages)
        {
            for (int i = 0; i < 16 && i < firstNodeInfos.Length; i++)
                Connect(firstNodeInfos[i].IpAddress, firstNodeInfos[i].PortNumber, true, () => { }, messages);
        }

        protected override void KeepConnections() { }
    }

    public class CreaNodeLocalTestContinue2<T, U> : CreaNodeLocalTest2<T, U>
        where T : HASHBASE
        where U : HASHBASE
    {
        public CreaNodeLocalTestContinue2(ushort _portNumber, int _creaVersion, string _appnameWithVersion) : base(_portNumber, _creaVersion, _appnameWithVersion) { }

        private readonly int maxInboundConnection = 16;
        private readonly int maxOutboundConnection = 8;

        private readonly object clientNodesLock = new object();
        private Dictionary<NodeInformation<T>, Connection> clientNodes = new Dictionary<NodeInformation<T>, Connection>();
        private readonly object listenerNodesLock = new object();
        private Dictionary<NodeInformation<T>, Connection> listenerNodes = new Dictionary<NodeInformation<T>, Connection>();

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

        protected override bool IsAlreadyConnected(NodeInformation<T> nodeInfo)
        {
            lock (clientNodesLock)
                if (clientNodes.Keys.Contains(nodeInfo))
                    return true;
            lock (listenerNodesLock)
                if (listenerNodes.Keys.Contains(nodeInfo))
                    return true;
            return false;
        }

        protected override void UpdateNodeState(NodeInformation<T> nodeInfo, bool isSucceeded) { }

        protected override bool IsListenerCanContinue(NodeInformation<T> nodeInfo)
        {
            lock (listenerNodesLock)
                return listenerNodes.Count < maxInboundConnection;
        }

        protected override bool IsWantToContinue(NodeInformation<T> nodeInfo)
        {
            lock (listenerNodesLock)
                return listenerNodes.Count < maxInboundConnection;
        }

        protected override bool IsClientCanContinue(NodeInformation<T> nodeInfo)
        {
            lock (clientNodesLock)
                return clientNodes.Count < maxOutboundConnection;
        }

        protected override void InboundContinue(NodeInformation<T> nodeInfo, SocketChannel sc, Action<string> _ConsoleWriteLine)
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

        protected override void OutboundContinue(NodeInformation<T> nodeInfo, SocketChannel sc, Action<string> _ConsoleWriteLine)
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

        private void Continue(NodeInformation<T> nodeInfo, SocketChannel sc, Action<string> _ConsoleWriteLine)
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
                    this.RaiseError("inbound_session", 5, ex);
                }
                finally
                {
                    e.Close();

                    _ConsoleWriteLine("セッション終わり");
                }
            };
        }

        protected override void Request(NodeInformation<T> nodeinfo, params MessageBase[] messages)
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
            }
            else
                try
                {
                    Connect(nodeInfo.IpAddress, nodeInfo.PortNumber, true, () => { }, messages);
                }
                catch (Exception ex)
                {
                    this.RaiseError("outbound_session", 5, ex);
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
                    this.RaiseError("diffuse", 5, ex);
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
                            Connect(firstNodeInfos[i].IpAddress, firstNodeInfos[i].PortNumber, false, () => are.Set());
                        }
                        catch (Exception ex)
                        {
                            this.RaiseError("keep_conn", 5, ex);

                            are.Set();
                        }

                    are.WaitOne();
                }

                this.RaiseNotification("keep_conn_completed", 5);
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

                    //SimulationWindow sw = new SimulationWindow();
                    //sw.ShowDialog();

                    this.StartTask(string.Empty, string.Empty, () =>
                    {
                        Test10NodesInv();
                    });
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
                CreaNodeLocalTestContinue2<Sha256Hash, Sha256Hash>[] cnlts = new CreaNodeLocalTestContinue2<Sha256Hash, Sha256Hash>[numOfNodes];
                for (int i = 0; i < numOfNodes; i++)
                {
                    cnlts[i] = new CreaNodeLocalTestContinue2<Sha256Hash, Sha256Hash>((ushort)(7777 + i), 0, "test");
                    cnlts[i].TxtestReceived += (sender2, e2) =>
                    {
                        counter++;
                        (string.Join(":", e2.IpAddress.ToString(), e2.PortNumber.ToString()) + " " + counter.ToString() + " " + ((double)counter / (double)numOfNodes).ToString() + " " + stopwatch.Elapsed.ToString()).ConsoleWriteLine();

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
                CreaNodeLocalTestContinue2<Sha256Hash, Sha256Hash> cnlt1 = new CreaNodeLocalTestContinue2<Sha256Hash, Sha256Hash>(7777, 0, "test");
                cnlt1.Start();
                while (!cnlt1.isStartCompleted)
                    Thread.Sleep(100);
                CreaNodeLocalTestContinue2<Sha256Hash, Sha256Hash> cnlt2 = new CreaNodeLocalTestContinue2<Sha256Hash, Sha256Hash>(7778, 0, "test");
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

        protected FirstNodeInformation(int? _version, IPAddress _ipAddress, ushort _portNumber, Network _network)
            : base(_version)
        {
            ipAddress = _ipAddress;
            portNumber = _portNumber;
            network = _network;

            if (ipAddress.AddressFamily != AddressFamily.InterNetwork && ipAddress.AddressFamily != AddressFamily.InterNetworkV6)
                throw new ArgumentException("first_node_info_ip_address");
            if (portNumber == 0)
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

        private ushort portNumber;
        public ushort PortNumber
        {
            get { return portNumber; }
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

        public override bool Equals(object obj) { return (obj as FirstNodeInformation).Operate((o) => o != null && Equals(o)); }

        public override int GetHashCode() { return ipAddress.GetHashCode() ^ portNumber.GetHashCode(); }

        public override string ToString() { return ipAddress + ":" + portNumber.ToString(); }

        public bool Equals(FirstNodeInformation other) { return ipAddress.ToString() == other.ipAddress.ToString() && portNumber == other.portNumber; }
    }

    public class NodeInformation<T> : FirstNodeInformation, IEquatable<NodeInformation<T>> where T : HASHBASE
    {
        public NodeInformation() : base(0) { }

        public NodeInformation(IPAddress _ipAddress, ushort _portNumber, Network _network, string _publicRSAParameters)
            : base(0, _ipAddress, _portNumber, _network)
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

        private T idCache;
        public T Id
        {
            get
            {
                if (idCache == null)
                    return idCache = Activator.CreateInstance(typeof(T), IpAddress.GetAddressBytes().Combine(BitConverter.GetBytes(PortNumber), Encoding.UTF8.GetBytes(publicRSAParameters))) as T;
                else
                    return idCache;
            }
        }

        public FirstNodeInformation FirstNodeInfo
        {
            //型変換ではなく新しいオブジェクトを作成しないとSHAREDDATA.ToBinaryで例外が発生する
            get { return new FirstNodeInformation(IpAddress, PortNumber, Network); }
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

        public override bool Equals(object obj) { return (obj as NodeInformation<T>).Operate((o) => o != null && Equals(o)); }

        public override int GetHashCode() { return Id.GetHashCode(); }

        public override string ToString() { return Id.ToString(); }

        public bool Equals(NodeInformation<T> other) { return Id.Equals(other.Id); }
    }

    #endregion

    #region Cremlia実装

    public class CremliaIdFactory<T> : ICremliaIdFactory where T : HASHBASE
    {
        public CremliaIdFactory() { }

        public ICremliaId Create() { return new CremliaId<T>(); }
    }

    public class CremliaId<T> : ICremliaId, IComparable<CremliaId<T>>, IEquatable<CremliaId<T>>, IComparable where T : HASHBASE
    {
        public CremliaId() : this(Activator.CreateInstance(typeof(T)) as T) { }

        public CremliaId(T _hash)
        {
            hash = _hash;
        }

        public readonly T hash;

        public int Size
        {
            get { return hash.SizeBit; }
        }

        public byte[] Bytes
        {
            get { return hash.hash; }
        }

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

    public class CremliaNodeInfomation<T> : ICremliaNodeInfomation, IEquatable<CremliaNodeInfomation<T>> where T : HASHBASE
    {
        public CremliaNodeInfomation(NodeInformation<T> _nodeInfo)
        {
            nodeInfo = _nodeInfo;
        }

        public readonly NodeInformation<T> nodeInfo;

        public ICremliaId Id
        {
            get { return new CremliaId<T>(nodeInfo.Id); }
        }

        public override bool Equals(object obj) { return (obj as CremliaNodeInfomation<T>).Operate((o) => o != null && Equals(o)); }

        public override int GetHashCode() { return Id.GetHashCode(); }

        public override string ToString() { return Id.ToString(); }

        public bool Equals(CremliaNodeInfomation<T> other) { return Id.Equals(other.Id); }
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

    #region 要約関数

    public abstract class HASHBASE : SHAREDDATA, IComparable<HASHBASE>, IEquatable<HASHBASE>, IComparable
    {
        public HASHBASE()
        {
            if (SizeBit % 8 != 0)
                throw new InvalidDataException("invalid_size_bit");

            FromHash(new byte[SizeByte]);
        }

        public HASHBASE(string stringHash)
        {
            if (SizeBit % 8 != 0)
                throw new InvalidDataException("invalid_size_bit");

            FromHash(stringHash.FromHexstringToBytes());

            if (hash.Length != SizeByte)
                throw new InvalidOperationException("invalid_length_hash");
        }

        public HASHBASE(byte[] data)
        {
            if (SizeBit % 8 != 0)
                throw new InvalidDataException("invalid_size_bit");

            hash = ComputeHash(data);

            if (hash.Length != SizeByte)
                throw new InvalidOperationException("invalid_length_hash");
        }

        public byte[] hash { get; private set; }

        public abstract int SizeBit { get; }

        protected abstract byte[] ComputeHash(byte[] data);

        public int SizeByte { get { return SizeBit / 8; } }

        public void FromHash(byte[] _hash)
        {
            if (_hash.Length != SizeByte)
                throw new InvalidOperationException("invalid_length_hash");

            hash = _hash;
        }

        public static T FromHash<T>(byte[] hash) where T : HASHBASE
        {
            T t = Activator.CreateInstance(typeof(T)) as T;
            t.FromHash(hash);
            return t;
        }

        protected override Func<ReaderWriter, IEnumerable<MainDataInfomation>> StreamInfo
        {
            get
            {
                return (msrw) => new MainDataInfomation[]{
                    new MainDataInfomation(typeof(byte[]), SizeByte, () => hash, (o) => hash = (byte[])o),
                };
            }
        }

        public override bool Equals(object obj) { return (obj as HASHBASE).Operate((o) => o != null && Equals(o)); }

        public bool Equals(HASHBASE other) { return hash.BytesEquals(other.hash); }

        public int CompareTo(object obj) { return hash.BytesCompareTo((obj as HASHBASE).hash); }

        public int CompareTo(HASHBASE other) { return hash.BytesCompareTo(other.hash); }

        public override int GetHashCode()
        {
            //暗号通貨におけるハッシュ値は先頭に0が並ぶことがあるので
            //ビットの並びをばらばらにしてから計算することにした
            //この実装でも0の数は変わらないので値が偏るのかもしれない
            //先頭の0を取り除いたものから計算するべきなのかもしれない
            //2014/04/06 常に同一の並びでなければ値が毎回変わってしまう
            byte[] randomBytes = hash.BytesRandomCache();
            byte[] intByte = new byte[4];
            CirculatedInteger c = new CirculatedInteger(0, intByte.Length);
            for (int i = 0; i < randomBytes.Length; i++, c.Next())
                intByte[c.value] = intByte[c.value] ^= randomBytes[i];
            int h = 0;
            for (int i = 0; i < 4; i++)
                h |= intByte[i] << (i * 8);
            return h;
        }

        public override string ToString() { return hash.ToHexstring(); }
    }

    public class Sha256Hash : HASHBASE
    {
        public Sha256Hash() : base() { }

        public Sha256Hash(string stringHash) : base(stringHash) { }

        public Sha256Hash(byte[] data) : base(data) { }

        public override int SizeBit { get { return 256; } }

        protected override byte[] ComputeHash(byte[] data) { return data.ComputeSha256(); }
    }

    public class Sha256Sha256Hash : HASHBASE
    {
        public Sha256Sha256Hash() : base() { }

        public Sha256Sha256Hash(string stringHash) : base(stringHash) { }

        public Sha256Sha256Hash(byte[] data) : base(data) { }

        public override int SizeBit { get { return 256; } }

        protected override byte[] ComputeHash(byte[] data) { return data.ComputeSha256().ComputeSha256(); }
    }

    public class Sha256Ripemd160Hash : HASHBASE
    {
        public Sha256Ripemd160Hash() : base() { }

        public Sha256Ripemd160Hash(string stringHash) : base(stringHash) { }

        public Sha256Ripemd160Hash(byte[] data) : base(data) { }

        public override int SizeBit { get { return 160; } }

        protected override byte[] ComputeHash(byte[] data) { return data.ComputeSha256().ComputeRipemd160(); }
    }

    public class X14Hash : HASHBASE
    {
        public X14Hash() : base() { }

        public X14Hash(string stringHash) : base(stringHash) { }

        public X14Hash(byte[] data) : base(data) { }

        public override int SizeBit { get { return 256; } }

        protected override byte[] ComputeHash(byte[] data)
        {
            return data.ComputeBlake512().ComputeBmw512().ComputeGroestl512().ComputeSkein512().ComputeJh512().ComputeKeccak512().ComputeLuffa512().ComputeCubehash512().ComputeShavite512().ComputeSimd512().ComputeEcho512().ComputeFugue512().ComputeHamsi512().ComputeShabal512().Decompose(0, SizeByte);
        }
    }

    public class X15Hash : HASHBASE
    {
        public X15Hash() : base() { }

        public X15Hash(string stringHash) : base(stringHash) { }

        public X15Hash(byte[] data) : base(data) { }

        public string tripKey { get; private set; }
        public string trip { get; private set; }

        public override int SizeBit { get { return 256; } }

        protected override byte[] ComputeHash(byte[] data)
        {
            tripKey = Convert.ToBase64String(data.ComputeSha1());

            byte[] sha1base64shiftjissha1 = Encoding.GetEncoding("Shift-JIS").GetBytes(tripKey).ComputeSha1();

            tripKey += "#";
            trip = "◆" + Convert.ToBase64String(sha1base64shiftjissha1).Substring(0, 12).Replace('+', '.');

            byte[] blake512 = data.ComputeBlake512();
            byte[] bmw512 = data.ComputeBmw512();
            byte[] groestl512 = data.ComputeGroestl512();
            byte[] skein512 = data.ComputeSkein512();
            byte[] jh512 = data.ComputeJh512();
            byte[] keccak512 = data.ComputeKeccak512();
            byte[] luffa512 = data.ComputeLuffa512();

            byte[] cubehash512 = sha1base64shiftjissha1.Combine(blake512).ComputeCubehash512();
            byte[] shavite512 = bmw512.Combine(groestl512).ComputeShavite512();
            byte[] simd512 = skein512.Combine(jh512).ComputeSimd512();
            byte[] echo512 = keccak512.Combine(luffa512).ComputeEcho512();

            byte[] fugue512 = cubehash512.Combine(shavite512).ComputeFugue512();
            byte[] hamsi512 = simd512.Combine(echo512).ComputeHamsi512();

            byte[] shabal512 = fugue512.Combine(hamsi512).ComputeShabal512();

            return shavite512.Decompose(0, SizeByte);
        }
    }

    #endregion

    #region 電子署名

    public abstract class DSAPUBKEYBASE : SHAREDDATA
    {
        public DSAPUBKEYBASE() : base(null) { }

        public DSAPUBKEYBASE(byte[] _pubKey)
            : base(null)
        {
            if (_pubKey.Length != SizeByte)
                throw new InvalidOperationException("invalid_length_pub_key");

            pubKey = _pubKey;
        }

        public byte[] pubKey { get; private set; }

        public abstract int SizeByte { get; }

        public abstract bool Verify(byte[] data, byte[] signature);

        protected override Func<ReaderWriter, IEnumerable<MainDataInfomation>> StreamInfo
        {
            get
            {
                return (msrw) => new MainDataInfomation[]{
                    new MainDataInfomation(typeof(byte[]), SizeByte, () => pubKey, (o) => pubKey = (byte[])o),
                };
            }
        }
    }

    public abstract class DSAPRIVKEYBASE : SHAREDDATA
    {
        public DSAPRIVKEYBASE() : base(null) { }

        public DSAPRIVKEYBASE(byte[] _privKey)
            : base(null)
        {
            if (_privKey.Length != SizeByte)
                throw new InvalidOperationException("invalid_length_priv_key");

            privKey = _privKey;
        }

        public byte[] privKey { get; private set; }

        public abstract int SizeByte { get; }

        public abstract byte[] Sign(byte[] data);

        protected override Func<ReaderWriter, IEnumerable<MainDataInfomation>> StreamInfo
        {
            get
            {
                return (msrw) => new MainDataInfomation[]{
                    new MainDataInfomation(typeof(byte[]), SizeByte, () => privKey, (o) => privKey = (byte[])o),
                };
            }
        }
    }

    public abstract class DSAKEYPAIRBASE<DsaPubKeyType, DsaPrivKeyType> : SHAREDDATA
        where DsaPubKeyType : DSAPUBKEYBASE
        where DsaPrivKeyType : DSAPRIVKEYBASE
    {
        public DSAKEYPAIRBASE() : base(null) { }

        public DsaPubKeyType pubKey { get; protected set; }
        public DsaPrivKeyType privKey { get; protected set; }

        protected override Func<ReaderWriter, IEnumerable<MainDataInfomation>> StreamInfo
        {
            get
            {
                return (msrw) => new MainDataInfomation[]{
                    new MainDataInfomation(typeof(DsaPubKeyType), null, () => pubKey, (o) => pubKey = (DsaPubKeyType)o),
                    new MainDataInfomation(typeof(DsaPrivKeyType), null, () => privKey, (o) => privKey = (DsaPrivKeyType)o),
                };
            }
        }
    }

    public class Ecdsa256PubKey : DSAPUBKEYBASE
    {
        public Ecdsa256PubKey() : base() { }

        public Ecdsa256PubKey(byte[] _pubKey) : base(_pubKey) { }

        public override int SizeByte { get { return 72; } }

        public override bool Verify(byte[] data, byte[] signature) { return data.VerifyEcdsa(signature, pubKey); }
    }

    public class Ecdsa256PrivKey : DSAPRIVKEYBASE
    {
        public Ecdsa256PrivKey() : base() { }

        public Ecdsa256PrivKey(byte[] _privKey) : base(_privKey) { }

        public override int SizeByte { get { return 104; } }

        public override byte[] Sign(byte[] data) { return data.SignEcdsaSha256(privKey); }
    }

    public class Ecdsa256KeyPair : DSAKEYPAIRBASE<Ecdsa256PubKey, Ecdsa256PrivKey>
    {
        public Ecdsa256KeyPair() : this(false) { }

        public Ecdsa256KeyPair(bool isCreate)
        {
            if (isCreate)
            {
                CngKey ck = CngKey.Create(CngAlgorithm.ECDsaP256, null, new CngKeyCreationParameters { ExportPolicy = CngExportPolicies.AllowPlaintextExport });

                pubKey = new Ecdsa256PubKey(ck.Export(CngKeyBlobFormat.EccPublicBlob));
                privKey = new Ecdsa256PrivKey(ck.Export(CngKeyBlobFormat.EccPrivateBlob));
            }
        }
    }

    public class Secp256k1PubKey<HashType> : DSAPUBKEYBASE where HashType : HASHBASE
    {
        public Secp256k1PubKey(byte[] _pubKey) : base(_pubKey) { }

        public override int SizeByte { get { return 65; } }

        public override bool Verify(byte[] data, byte[] signature)
        {
            var r = new byte[32];
            var s = new byte[32];
            Buffer.BlockCopy(signature, 1, r, 0, 32);
            Buffer.BlockCopy(signature, 33, s, 0, 32);
            var recId = signature[0] - 27;

            ECDsaSigner signer = new ECDsaSigner();

            byte[] hash = (Activator.CreateInstance(typeof(HashType), data) as HashType).hash;

            ECPoint publicKey = ECPoint.DecodePoint(pubKey);

            return signer.VerifySignature(publicKey, hash, r.ToBigIntegerUnsigned(true), s.ToBigIntegerUnsigned(true));
        }

        public static bool RecoverAndVerify(byte[] data, byte[] signature)
        {
            var r = new byte[32];
            var s = new byte[32];
            Buffer.BlockCopy(signature, 1, r, 0, 32);
            Buffer.BlockCopy(signature, 33, s, 0, 32);
            var recId = signature[0] - 27;

            ECDsaSigner signer = new ECDsaSigner();

            byte[] hash = (Activator.CreateInstance(typeof(HashType), data) as HashType).hash;

            ECPoint publicKey = signer.RecoverFromSignature(hash, r.ToBigIntegerUnsigned(true), s.ToBigIntegerUnsigned(true), recId);

            return signer.VerifySignature(publicKey, hash, r.ToBigIntegerUnsigned(true), s.ToBigIntegerUnsigned(true));
        }
    }

    public class Secp256k1PribKey<HashType> : DSAPRIVKEYBASE where HashType : HASHBASE
    {
        public Secp256k1PribKey(byte[] _pribKey) : base(_pribKey) { }

        public override int SizeByte { get { return 32; } }

        public override byte[] Sign(byte[] data)
        {
            ECDsaSigner signer = new ECDsaSigner();

            byte[] hash = (Activator.CreateInstance(typeof(HashType), data) as HashType).hash;
            BigInteger privateKey = privKey.ToBigIntegerUnsigned(true);
            BigInteger[] signature = signer.GenerateSignature(privateKey, hash);

            int? recId = null;
            ECPoint publicKey = Secp256k1.G.Multiply(privateKey);

            for (var i = 0; i < 4 && recId == null; i++)
            {
                ECPoint Q = signer.RecoverFromSignature(hash, signature[0], signature[1], i);

                if (Q.X == publicKey.X && Q.Y == publicKey.Y)
                    recId = i;
            }
            if (recId == null)
                throw new Exception("Did not find proper recid");

            byte[] sig = new byte[65];

            sig[0] = (byte)(27 + recId);
            byte[] rByteArray = signature[0].ToByteArrayUnsigned(true);
            byte[] sByteArray = signature[1].ToByteArrayUnsigned(true);

            Buffer.BlockCopy(rByteArray, 0, sig, 1 + (32 - rByteArray.Length), rByteArray.Length);
            Buffer.BlockCopy(sByteArray, 0, sig, 33 + (32 - sByteArray.Length), sByteArray.Length);

            return sig;
        }
    }

    public class Secp256k1KeyPair<HashType> : DSAKEYPAIRBASE<Secp256k1PubKey<HashType>, Secp256k1PribKey<HashType>> where HashType : HASHBASE
    {
        public Secp256k1KeyPair() : this(false) { }

        public Secp256k1KeyPair(bool isCreate)
        {
            if (isCreate)
            {
                byte[] privKeyBytes = new byte[32];
                using (RNGCryptoServiceProvider rngcsp = new RNGCryptoServiceProvider())
                    rngcsp.GetBytes(privKeyBytes);
                BigInteger privateKey = privKeyBytes.ToBigIntegerUnsigned(true);
                ECPoint publicKey = Secp256k1.G.Multiply(privateKey);

                pubKey = new Secp256k1PubKey<HashType>(publicKey.EncodePoint(false));
                privKey = new Secp256k1PribKey<HashType>(privKeyBytes);
            }
        }
    }

    #endregion

    #region 口座

    public interface IAccount
    {
        string iName { get; }
        string iDescription { get; }
        string iAddress { get; }
        CurrencyUnit iUsableAmount { get; }
        CurrencyUnit iUnusableAmount { get; }
        CurrencyUnit iAmount { get; }
    }

    public interface IAccountHolder
    {
        IAccount[] iAccounts { get; }

        event EventHandler<IAccount> iAccountAdded;
        event EventHandler<IAccount> iAccountRemoved;
        event EventHandler iAccountHolderChanged;

        void iAddAccount(IAccount iAccount);
        void iRemoveAccount(IAccount iAccount);
    }

    public interface IAnonymousAccountHolder : IAccountHolder { }

    public interface IPseudonymousAccountHolder : IAccountHolder
    {
        string iName { get; }
        string iSign { get; }
    }

    public interface IAccountHolders
    {
        IAnonymousAccountHolder iAnonymousAccountHolder { get; }
        IPseudonymousAccountHolder[] iPseudonymousAccountHolders { get; }

        event EventHandler<IAccountHolder> iAccountHolderAdded;
        event EventHandler<IAccountHolder> iAccountHolderRemoved;
        event EventHandler iAccountHoldersChanged;

        void iAddAccountHolder(IPseudonymousAccountHolder iPseudonymousAccountHolder);
        void iDeleteAccountHolder(IPseudonymousAccountHolder iPseudonymousAccountHolder);
    }

    public interface IAccountHoldersFactory
    {
        IAccount CreateAccount(string name, string description);
        IPseudonymousAccountHolder CreatePseudonymousAccountHolder(string name);
    }

    public class Account<KeyPairType, DsaPubKeyType, DsaPrivKeyType> : SHAREDDATA, IAccount
        where KeyPairType : DSAKEYPAIRBASE<DsaPubKeyType, DsaPrivKeyType>
        where DsaPubKeyType : DSAPUBKEYBASE
        where DsaPrivKeyType : DSAPRIVKEYBASE
    {
        public Account() : base(0) { }

        public Account(string _name, string _description)
            : base(0)
        {
            name = _name;
            description = _description;
            keyPair = Activator.CreateInstance(typeof(KeyPairType), new object[] { true }) as KeyPairType;
        }

        private string name;
        public string Name
        {
            get { return name; }
            set
            {
                if (name != value)
                    this.ExecuteBeforeEvent(() => name = value, AccountChanged);
            }
        }

        private string description;
        public string Description
        {
            get { return description; }
            set
            {
                if (description != value)
                    this.ExecuteBeforeEvent(() => description = value, AccountChanged);
            }
        }

        public CurrencyUnit usableAmount { get; private set; }

        public CurrencyUnit unusableAmount { get; private set; }

        public DSAKEYPAIRBASE<DsaPubKeyType, DsaPrivKeyType> keyPair { get; private set; }

        public class AccountAddress
        {
            public AccountAddress(byte[] _publicKey) { hash = new Sha256Ripemd160Hash(_publicKey); }

            public AccountAddress(Sha256Ripemd160Hash _hash) { hash = _hash; }

            public AccountAddress(string _base58) { base58 = _base58; }

            private Sha256Ripemd160Hash hash;
            public Sha256Ripemd160Hash Hash
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

                        return hash = HASHBASE.FromHash<Sha256Ripemd160Hash>(hashBytes);
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
                        byte[] hashBytes = hash.hash;

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
                        new MainDataInfomation(typeof(KeyPairType), null, () => keyPair, (o) => keyPair = (KeyPairType)o),
                    };
                else
                    throw new NotSupportedException("account_main_data_info");
            }
        }

        public override bool IsVersioned { get { return true; } }

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

        public AccountAddress Address { get { return new AccountAddress(keyPair.pubKey.pubKey); } }

        public string AddressBase58 { get { return Address.Base58; } }

        public CurrencyUnit Amount { get { return new CurrencyUnit(usableAmount.rawAmount + unusableAmount.rawAmount); } }

        public void ChangeAmount(CurrencyUnit newUsableAmount, CurrencyUnit newUnusableAmount)
        {
            //2014/08/18
            //状態が変化した場合にはAccountChangedを生起すべきではないのではないだろうか
            usableAmount = newUsableAmount;
            unusableAmount = newUnusableAmount;
        }

        public override string ToString() { return string.Join(":", Name, AddressBase58); }

        public string iName { get { return Name; } }

        public string iDescription { get { return Description; } }

        public string iAddress { get { return AddressBase58; } }

        public CurrencyUnit iUsableAmount { get { return usableAmount; } }

        public CurrencyUnit iUnusableAmount { get { return unusableAmount; } }

        public CurrencyUnit iAmount { get { return Amount; } }
    }

    public abstract class AccountHolder<KeyPairType, DsaPubKeyType, DsaPrivKeyType> : SHAREDDATA, IAccountHolder
        where KeyPairType : DSAKEYPAIRBASE<DsaPubKeyType, DsaPrivKeyType>
        where DsaPubKeyType : DSAPUBKEYBASE
        where DsaPrivKeyType : DSAPRIVKEYBASE
    {
        public AccountHolder() { }

        public AccountHolder(int? _version)
            : base(_version)
        {
            accounts = new List<Account<KeyPairType, DsaPubKeyType, DsaPrivKeyType>>();
            accountsCache = new CachedData<Account<KeyPairType, DsaPubKeyType, DsaPrivKeyType>[]>(() =>
            {
                lock (accountsLock)
                    return accounts.ToArray();
            });

            account_changed = (sender, e) => AccountHolderChanged(this, EventArgs.Empty);
        }

        private readonly object accountsLock = new object();
        private List<Account<KeyPairType, DsaPubKeyType, DsaPrivKeyType>> accounts;
        private CachedData<Account<KeyPairType, DsaPubKeyType, DsaPrivKeyType>[]> accountsCache;
        public Account<KeyPairType, DsaPubKeyType, DsaPrivKeyType>[] Accounts { get { return accountsCache.Data; } }

        protected override Func<ReaderWriter, IEnumerable<MainDataInfomation>> StreamInfo
        {
            get
            {
                return (msrw) => new MainDataInfomation[]{
                    new MainDataInfomation(typeof(Account<KeyPairType, DsaPubKeyType, DsaPrivKeyType>[]), 0, null, () => accounts.ToArray(), (o) =>
                    {
                        foreach (var account in accounts)
                            account.AccountChanged -= account_changed;
                        accounts = ((Account<KeyPairType, DsaPubKeyType, DsaPrivKeyType>[])o).ToList();
                        foreach (var account in accounts)
                            account.AccountChanged += account_changed;
                    }),
                };
            }
        }

        public event EventHandler<Account<KeyPairType, DsaPubKeyType, DsaPrivKeyType>> AccountAdded = delegate { };
        protected EventHandler<Account<KeyPairType, DsaPubKeyType, DsaPrivKeyType>> PAccountAdded { get { return AccountAdded; } }

        public event EventHandler<Account<KeyPairType, DsaPubKeyType, DsaPrivKeyType>> AccountRemoved = delegate { };
        protected EventHandler<Account<KeyPairType, DsaPubKeyType, DsaPrivKeyType>> PAccountRemoved { get { return AccountRemoved; } }

        public event EventHandler AccountHolderChanged = delegate { };
        protected EventHandler PAccountHolderChanged { get { return AccountHolderChanged; } }

        private EventHandler account_changed;

        public void AddAccount(Account<KeyPairType, DsaPubKeyType, DsaPrivKeyType> account)
        {
            lock (accountsLock)
            {
                if (accounts.Contains(account))
                    throw new InvalidOperationException("exist_account");

                this.ExecuteBeforeEvent(() =>
                {
                    accounts.Add(account);
                    accountsCache.IsModified = true;

                    account.AccountChanged += account_changed;
                }, account, new EventHandler<Account<KeyPairType, DsaPubKeyType, DsaPrivKeyType>>[] { AccountAdded }, new EventHandler[] { AccountHolderChanged });
            }
        }

        public void RemoveAccount(Account<KeyPairType, DsaPubKeyType, DsaPrivKeyType> account)
        {
            lock (accountsLock)
            {
                if (!accounts.Contains(account))
                    throw new InvalidOperationException("not_exist_account");

                this.ExecuteBeforeEvent(() =>
                {
                    accounts.Remove(account);
                    accountsCache.IsModified = true;

                    account.AccountChanged -= account_changed;
                }, account, new EventHandler<Account<KeyPairType, DsaPubKeyType, DsaPrivKeyType>>[] { AccountRemoved }, new EventHandler[] { AccountHolderChanged });
            }
        }

        public IAccount[] iAccounts { get { return Accounts; } }

        private Dictionary<EventHandler<IAccount>, EventHandler<Account<KeyPairType, DsaPubKeyType, DsaPrivKeyType>>> iAccountAddedDict = new Dictionary<EventHandler<IAccount>, EventHandler<Account<KeyPairType, DsaPubKeyType, DsaPrivKeyType>>>();
        public event EventHandler<IAccount> iAccountAdded
        {
            add
            {
                EventHandler<Account<KeyPairType, DsaPubKeyType, DsaPrivKeyType>> eh = (sender, e) => value(sender, e);

                iAccountAddedDict.Add(value, eh);

                AccountAdded += eh;
            }
            remove
            {
                EventHandler<Account<KeyPairType, DsaPubKeyType, DsaPrivKeyType>> eh = iAccountAddedDict[value];

                iAccountAddedDict.Remove(value);

                AccountAdded -= eh;
            }
        }

        private Dictionary<EventHandler<IAccount>, EventHandler<Account<KeyPairType, DsaPubKeyType, DsaPrivKeyType>>> iAccountRemovedDict = new Dictionary<EventHandler<IAccount>, EventHandler<Account<KeyPairType, DsaPubKeyType, DsaPrivKeyType>>>();
        public event EventHandler<IAccount> iAccountRemoved
        {
            add
            {
                EventHandler<Account<KeyPairType, DsaPubKeyType, DsaPrivKeyType>> eh = (sender, e) => value(sender, e);

                iAccountRemovedDict.Add(value, eh);

                AccountRemoved += eh;
            }
            remove
            {
                EventHandler<Account<KeyPairType, DsaPubKeyType, DsaPrivKeyType>> eh = iAccountRemovedDict[value];

                iAccountRemovedDict.Remove(value);

                AccountRemoved -= eh;
            }
        }

        public event EventHandler iAccountHolderChanged
        {
            add { AccountHolderChanged += value; }
            remove { AccountHolderChanged -= value; }
        }

        public void iAddAccount(IAccount iAccount)
        {
            if (!(iAccount is Account<KeyPairType, DsaPubKeyType, DsaPrivKeyType>))
                throw new ArgumentException("type_mismatch");

            AddAccount(iAccount as Account<KeyPairType, DsaPubKeyType, DsaPrivKeyType>);
        }

        public void iRemoveAccount(IAccount iAccount)
        {
            if (!(iAccount is Account<KeyPairType, DsaPubKeyType, DsaPrivKeyType>))
                throw new ArgumentException("type_mismatch");

            RemoveAccount(iAccount as Account<KeyPairType, DsaPubKeyType, DsaPrivKeyType>);
        }
    }

    public class AnonymousAccountHolder<KeyPairType, DsaPubKeyType, DsaPrivKeyType> : AccountHolder<KeyPairType, DsaPubKeyType, DsaPrivKeyType>, IAnonymousAccountHolder
        where KeyPairType : DSAKEYPAIRBASE<DsaPubKeyType, DsaPrivKeyType>
        where DsaPubKeyType : DSAPUBKEYBASE
        where DsaPrivKeyType : DSAPRIVKEYBASE
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

        public override bool IsVersioned { get { return true; } }

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

    public class PseudonymousAccountHolder<KeyPairType, DsaPubKeyType, DsaPrivKeyType> : AccountHolder<KeyPairType, DsaPubKeyType, DsaPrivKeyType>, IPseudonymousAccountHolder
        where KeyPairType : DSAKEYPAIRBASE<DsaPubKeyType, DsaPrivKeyType>
        where DsaPubKeyType : DSAPUBKEYBASE
        where DsaPrivKeyType : DSAPRIVKEYBASE
    {
        public PseudonymousAccountHolder() : base(0) { }

        public PseudonymousAccountHolder(string _name)
            : base(0)
        {
            name = _name;
            keyPair = Activator.CreateInstance(typeof(KeyPairType), new object[] { true }) as KeyPairType;
        }

        private string name;
        public string Name
        {
            get { return name; }
            set
            {
                if (name != value)
                    this.ExecuteBeforeEvent(() => name = value, PAccountHolderChanged);
            }
        }

        public KeyPairType keyPair { get; private set; }

        protected override Func<ReaderWriter, IEnumerable<MainDataInfomation>> StreamInfo
        {
            get
            {
                if (Version == 0)
                    return (msrw) => base.StreamInfo(msrw).Concat(new MainDataInfomation[]{
                        new MainDataInfomation(typeof(string), () => name, (o) => name = (string)o),
                        new MainDataInfomation(typeof(KeyPairType), null, () => keyPair, (o) => keyPair = (KeyPairType)o),
                    });
                else
                    throw new NotSupportedException("pah_main_data_info");
            }
        }

        public override bool IsVersioned { get { return true; } }

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

        public string Trip { get { return "◆" + Convert.ToBase64String(keyPair.pubKey.pubKey).Operate((s) => s.Substring(s.Length - 12, 12)); } }

        public string Sign { get { return name + Trip; } }

        public override string ToString() { return Sign; }

        public string iName { get { return Name; } }

        public string iSign { get { return Sign; } }
    }

    public class AccountHolders<KeyPairType, DsaPubKeyType, DsaPrivKeyType> : SHAREDDATA, IAccountHolders
        where KeyPairType : DSAKEYPAIRBASE<DsaPubKeyType, DsaPrivKeyType>
        where DsaPubKeyType : DSAPUBKEYBASE
        where DsaPrivKeyType : DSAPRIVKEYBASE
    {
        public AccountHolders()
            : base(0)
        {
            accountHolders_changed = (sender, e) => AccountHoldersChanged(this, EventArgs.Empty);

            anonymousAccountHolder = new AnonymousAccountHolder<KeyPairType, DsaPubKeyType, DsaPrivKeyType>();
            anonymousAccountHolder.AccountHolderChanged += accountHolders_changed;

            pseudonymousAccountHolders = new List<PseudonymousAccountHolder<KeyPairType, DsaPubKeyType, DsaPrivKeyType>>();
            pseudonymousAccountHoldersCache = new CachedData<PseudonymousAccountHolder<KeyPairType, DsaPubKeyType, DsaPrivKeyType>[]>(() =>
            {
                lock (pahsLock)
                    return pseudonymousAccountHolders.ToArray();
            });

            candidateAccountHolders = new List<PseudonymousAccountHolder<KeyPairType, DsaPubKeyType, DsaPrivKeyType>>();
        }

        public AnonymousAccountHolder<KeyPairType, DsaPubKeyType, DsaPrivKeyType> anonymousAccountHolder { get; private set; }

        private readonly object pahsLock = new object();
        private List<PseudonymousAccountHolder<KeyPairType, DsaPubKeyType, DsaPrivKeyType>> pseudonymousAccountHolders;
        private CachedData<PseudonymousAccountHolder<KeyPairType, DsaPubKeyType, DsaPrivKeyType>[]> pseudonymousAccountHoldersCache;
        public PseudonymousAccountHolder<KeyPairType, DsaPubKeyType, DsaPrivKeyType>[] PseudonymousAccountHolders { get { return pseudonymousAccountHoldersCache.Data; } }

        public IEnumerable<AccountHolder<KeyPairType, DsaPubKeyType, DsaPrivKeyType>> AllAccountHolders
        {
            get
            {
                yield return anonymousAccountHolder;
                foreach (var pseudonymousAccountHolder in PseudonymousAccountHolders)
                    yield return pseudonymousAccountHolder;
            }
        }

        private readonly object cahsLock = new object();
        private List<PseudonymousAccountHolder<KeyPairType, DsaPubKeyType, DsaPrivKeyType>> candidateAccountHolders;
        public PseudonymousAccountHolder<KeyPairType, DsaPubKeyType, DsaPrivKeyType>[] CandidateAccountHolders
        {
            get { return candidateAccountHolders.ToArray(); }
        }

        protected override Func<ReaderWriter, IEnumerable<MainDataInfomation>> StreamInfo
        {
            get
            {
                if (Version == 0)
                    return (mswr) => new MainDataInfomation[]{
                        new MainDataInfomation(typeof(AnonymousAccountHolder<KeyPairType, DsaPubKeyType, DsaPrivKeyType>), 0, () => anonymousAccountHolder, (o) =>
                        {
                            anonymousAccountHolder.AccountHolderChanged -= accountHolders_changed;
                            anonymousAccountHolder = (AnonymousAccountHolder<KeyPairType, DsaPubKeyType, DsaPrivKeyType>)o;
                            anonymousAccountHolder.AccountHolderChanged += accountHolders_changed;
                        }),
                        new MainDataInfomation(typeof(PseudonymousAccountHolder<KeyPairType, DsaPubKeyType, DsaPrivKeyType>[]), 0, null, () => pseudonymousAccountHolders.ToArray(), (o) =>
                        {
                            foreach (var pah in pseudonymousAccountHolders)
                                pah.AccountHolderChanged -= accountHolders_changed;
                            pseudonymousAccountHolders = ((PseudonymousAccountHolder<KeyPairType, DsaPubKeyType, DsaPrivKeyType>[])o).ToList();
                            foreach (var pah in pseudonymousAccountHolders)
                                pah.AccountHolderChanged += accountHolders_changed;
                        }),
                        new MainDataInfomation(typeof(PseudonymousAccountHolder<KeyPairType, DsaPubKeyType, DsaPrivKeyType>[]), 0, null, () => candidateAccountHolders.ToArray(), (o) => candidateAccountHolders = ((PseudonymousAccountHolder<KeyPairType, DsaPubKeyType, DsaPrivKeyType>[])o).ToList()),
                    };
                else
                    throw new NotSupportedException("account_holder_database_main_data_info");
            }
        }

        public override bool IsVersioned { get { return true; } }

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

        public event EventHandler<AccountHolder<KeyPairType, DsaPubKeyType, DsaPrivKeyType>> AccountHolderAdded = delegate { };
        public event EventHandler<AccountHolder<KeyPairType, DsaPubKeyType, DsaPrivKeyType>> AccountHolderRemoved = delegate { };
        //<要検討>2014/08/18
        //本当に必要か？イベントの連鎖は無駄ではないか？
        public event EventHandler AccountHoldersChanged = delegate { };

        private EventHandler accountHolders_changed;

        public void AddAccountHolder(PseudonymousAccountHolder<KeyPairType, DsaPubKeyType, DsaPrivKeyType> ah)
        {
            lock (pahsLock)
            {
                if (pseudonymousAccountHolders.Contains(ah))
                    throw new InvalidOperationException("exist_account_holder");

                if (!pseudonymousAccountHolders.Where((e) => e.Name == ah.Name).FirstOrDefault().IsNotNull().RaiseError(this.GetType(), "exist_same_name_account_holder", 5))
                    this.ExecuteBeforeEvent(() =>
                    {
                        pseudonymousAccountHolders.Add(ah);
                        pseudonymousAccountHoldersCache.IsModified = true;

                        ah.AccountHolderChanged += accountHolders_changed;
                    }, ah, new EventHandler<AccountHolder<KeyPairType, DsaPubKeyType, DsaPrivKeyType>>[] { AccountHolderAdded }, new EventHandler[] { AccountHoldersChanged });
            }
        }

        public void DeleteAccountHolder(PseudonymousAccountHolder<KeyPairType, DsaPubKeyType, DsaPrivKeyType> ah)
        {
            lock (pahsLock)
            {
                if (!pseudonymousAccountHolders.Contains(ah))
                    throw new InvalidOperationException("not_exist_account_holder");

                this.ExecuteBeforeEvent(() =>
                {
                    pseudonymousAccountHolders.Remove(ah);
                    pseudonymousAccountHoldersCache.IsModified = true;

                    ah.AccountHolderChanged -= accountHolders_changed;
                }, ah, new EventHandler<AccountHolder<KeyPairType, DsaPubKeyType, DsaPrivKeyType>>[] { AccountHolderRemoved }, new EventHandler[] { AccountHoldersChanged });
            }
        }

        public void AddCandidateAccountHolder(PseudonymousAccountHolder<KeyPairType, DsaPubKeyType, DsaPrivKeyType> ah)
        {
            lock (cahsLock)
            {
                if (candidateAccountHolders.Contains(ah))
                    throw new InvalidOperationException("exist_candidate_account_holder");

                candidateAccountHolders.Add(ah);
            }
        }

        public void DeleteCandidateAccountHolder(PseudonymousAccountHolder<KeyPairType, DsaPubKeyType, DsaPrivKeyType> ah)
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

        public IAnonymousAccountHolder iAnonymousAccountHolder { get { return anonymousAccountHolder; } }

        public IPseudonymousAccountHolder[] iPseudonymousAccountHolders { get { return PseudonymousAccountHolders; } }

        private Dictionary<EventHandler<IAccountHolder>, EventHandler<AccountHolder<KeyPairType, DsaPubKeyType, DsaPrivKeyType>>> iAccountHolderAddedDict = new Dictionary<EventHandler<IAccountHolder>, EventHandler<AccountHolder<KeyPairType, DsaPubKeyType, DsaPrivKeyType>>>();
        public event EventHandler<IAccountHolder> iAccountHolderAdded
        {
            add
            {
                EventHandler<AccountHolder<KeyPairType, DsaPubKeyType, DsaPrivKeyType>> eh = (sender, e) => value(sender, e);

                iAccountHolderAddedDict.Add(value, eh);

                AccountHolderAdded += eh;
            }
            remove
            {
                EventHandler<AccountHolder<KeyPairType, DsaPubKeyType, DsaPrivKeyType>> eh = iAccountHolderAddedDict[value];

                iAccountHolderAddedDict.Remove(value);

                AccountHolderAdded -= eh;
            }
        }

        private Dictionary<EventHandler<IAccountHolder>, EventHandler<AccountHolder<KeyPairType, DsaPubKeyType, DsaPrivKeyType>>> iAccountHolderRemovedDict = new Dictionary<EventHandler<IAccountHolder>, EventHandler<AccountHolder<KeyPairType, DsaPubKeyType, DsaPrivKeyType>>>();
        public event EventHandler<IAccountHolder> iAccountHolderRemoved
        {
            add
            {
                EventHandler<AccountHolder<KeyPairType, DsaPubKeyType, DsaPrivKeyType>> eh = (sender, e) => value(sender, e);

                iAccountHolderRemovedDict.Add(value, eh);

                AccountHolderRemoved += eh;
            }
            remove
            {
                EventHandler<AccountHolder<KeyPairType, DsaPubKeyType, DsaPrivKeyType>> eh = iAccountHolderRemovedDict[value];

                iAccountHolderRemovedDict.Remove(value);

                AccountHolderAdded -= eh;
            }
        }

        public event EventHandler iAccountHoldersChanged
        {
            add { AccountHoldersChanged += value; }
            remove { AccountHoldersChanged -= value; }
        }

        public void iAddAccountHolder(IPseudonymousAccountHolder iPseudonymousAccountHolder)
        {
            if (!(iPseudonymousAccountHolder is PseudonymousAccountHolder<KeyPairType, DsaPubKeyType, DsaPrivKeyType>))
                throw new ArgumentException("type_mismatch");

            AddAccountHolder(iPseudonymousAccountHolder as PseudonymousAccountHolder<KeyPairType, DsaPubKeyType, DsaPrivKeyType>);
        }

        public void iDeleteAccountHolder(IPseudonymousAccountHolder iPseudonymousAccountHolder)
        {
            if (!(iPseudonymousAccountHolder is PseudonymousAccountHolder<KeyPairType, DsaPubKeyType, DsaPrivKeyType>))
                throw new ArgumentException("type_mismatch");

            DeleteAccountHolder(iPseudonymousAccountHolder as PseudonymousAccountHolder<KeyPairType, DsaPubKeyType, DsaPrivKeyType>);
        }
    }

    public class AccountHoldersFactory<KeyPairType, DsaPubKeyType, DsaPrivKeyType> : IAccountHoldersFactory
        where KeyPairType : DSAKEYPAIRBASE<DsaPubKeyType, DsaPrivKeyType>
        where DsaPubKeyType : DSAPUBKEYBASE
        where DsaPrivKeyType : DSAPRIVKEYBASE
    {
        public AccountHoldersFactory() { }

        public IAccount CreateAccount(string name, string description)
        {
            return new Account<KeyPairType, DsaPubKeyType, DsaPrivKeyType>(name, description);
        }

        public IPseudonymousAccountHolder CreatePseudonymousAccountHolder(string name)
        {
            return new PseudonymousAccountHolder<KeyPairType, DsaPubKeyType, DsaPrivKeyType>(name);
        }
    }

    #endregion

    public class MerkleTree<U> where U : HASHBASE
    {
        public MerkleTree(U[] _hashes)
        {
            hashes = new List<U>();
            tree = new U[1][];
            tree[0] = new U[0];

            Add(_hashes);
        }

        public List<U> hashes { get; private set; }
        public U[][] tree { get; private set; }

        public U Root
        {
            get
            {
                if (hashes.Count == 0)
                    throw new InvalidOperationException("Merkle_tree_empty");

                return tree[tree.Length - 1][0];
            }
        }

        public void Add(U[] hs)
        {
            int start = hashes.Count;
            hashes.AddRange(hs);
            int end = hashes.Count;

            if (tree[0].Length < hashes.Count)
            {
                int newLength = tree[0].Length;
                int newHeight = tree.Length;
                while (newLength < hashes.Count)
                    if (newLength == 0)
                        newLength++;
                    else
                    {
                        newLength *= 2;
                        newHeight++;
                    }

                U[][] newTree = new U[newHeight][];
                for (int i = 0; i < newTree.Length; i++, newLength /= 2)
                    newTree[i] = new U[newLength];

                for (int i = 0; i < tree.Length; i++)
                    for (int j = 0; j < tree[i].Length; j++)
                        newTree[i][j] = tree[i][j];

                tree = newTree;

                end = tree[0].Length;
            }

            for (int j = start; j < end; j++)
                if (j < hashes.Count)
                    tree[0][j] = hashes[j];
                else
                    tree[0][j] = hashes[hashes.Count - 1];
            start /= 2;
            end /= 2;

            for (int i = 1; i < tree.Length; i++)
            {
                for (int j = start; j < end; j++)
                    tree[i][j] = Activator.CreateInstance(typeof(U), (tree[i - 1][j * 2] as HASHBASE).hash.Combine((tree[i - 1][j * 2 + 1] as HASHBASE).hash)) as U;
                start /= 2;
                end /= 2;
            }
        }

        public MerkleProof<U> GetProof(U target)
        {
            int? index = null;
            for (int i = 0; i < tree[0].Length; i++)
                if (tree[0][i].Equals(target))
                {
                    index = i;
                    break;
                }

            if (index == null)
                throw new InvalidOperationException("merkle_tree_target");

            int index2 = index.Value;
            U[] proof = new U[tree.Length];
            for (int i = 0; i < proof.Length - 1; i++, index /= 2)
                proof[i] = index % 2 == 0 ? tree[i][index.Value + 1] : tree[i][index.Value - 1];
            proof[proof.Length - 1] = tree[proof.Length - 1][0];

            return new MerkleProof<U>(index2, proof);
        }

        public static bool Verify(U target, MerkleProof<U> proof)
        {
            U cal = Activator.CreateInstance(typeof(U)) as U;
            cal.FromHash(target.hash);
            int index = proof.index;
            for (int i = 0; i < proof.proof.Length - 1; i++, index /= 2)
                cal = Activator.CreateInstance(typeof(U), index % 2 == 0 ? cal.hash.Combine(proof.proof[i].hash) : proof.proof[i].hash.Combine(cal.hash)) as U;
            return cal.Equals(proof.proof[proof.proof.Length - 1]);
        }
    }

    public class MerkleProof<U> : SHAREDDATA where U : HASHBASE
    {
        public MerkleProof() { }

        public MerkleProof(int _index, U[] _proof) { index = _index; proof = _proof; }

        public int index { get; private set; }
        public U[] proof { get; private set; }

        protected override Func<ReaderWriter, IEnumerable<MainDataInfomation>> StreamInfo
        {
            get
            {
                return (msrw) => new MainDataInfomation[]{
                    new MainDataInfomation(typeof(int), () => index, (o) => index = (int)o),
                    new MainDataInfomation(typeof(U[]), null, null, () => proof, (o) => proof = (U[])o),
                };
            }
        }
    }

    public class CurrencyUnit
    {
        protected CurrencyUnit() { }

        public CurrencyUnit(long _rawAmount) { rawAmount = _rawAmount; }

        public long rawAmount { get; protected set; }

        public virtual decimal Amount { get { throw new NotImplementedException("currency_unit_amount"); } }
        public virtual Creacoin AmountInCreacoin { get { return new Creacoin(rawAmount); } }
        public virtual Yumina AmountInYumina { get { return new Yumina(rawAmount); } }
    }

    public class Creacoin : CurrencyUnit
    {
        public Creacoin(long _rawAmount) : base(_rawAmount) { }

        public Creacoin(decimal _amountInCreacoin)
        {
            if (_amountInCreacoin < 0.0m)
                throw new ArgumentException("creacoin_out_of_range");

            decimal amountInMinimumUnit = _amountInCreacoin * CreacoinInMinimumUnit;
            if (amountInMinimumUnit != Math.Floor(amountInMinimumUnit))
                throw new InvalidDataException("creacoin_precision");

            rawAmount = (long)amountInMinimumUnit;
        }

        public static decimal CreacoinInMinimumUnit = 100000000.0m;

        public override decimal Amount { get { return rawAmount / CreacoinInMinimumUnit; } }
        public override Creacoin AmountInCreacoin { get { return this; } }
    }

    public class Yumina : CurrencyUnit
    {
        public Yumina(long _rawAmount) : base(_rawAmount) { }

        public Yumina(decimal _amountInYumina)
        {
            if (_amountInYumina < 0.0m)
                throw new ArgumentException("yumina_out_of_range");

            decimal amountInMinimumUnit = _amountInYumina * YuminaInMinimumUnit;
            if (amountInMinimumUnit != Math.Floor(amountInMinimumUnit))
                throw new InvalidDataException("yumina_precision");

            rawAmount = (long)amountInMinimumUnit;
        }

        public static decimal YuminaInMinimumUnit = 1000000.0m;

        public override decimal Amount { get { return rawAmount / YuminaInMinimumUnit; } }
        public override Yumina AmountInYumina { get { return this; } }
    }

    public class Difficulty<BlockidHashType> where BlockidHashType : HASHBASE
    {
        public Difficulty(byte[] _compactTarget)
        {
            if (_compactTarget.Length != 4)
                throw new ArgumentException("ill-formed_compact_target");

            compactTarget = _compactTarget;
        }

        public Difficulty(BlockidHashType _target)
        {
            if (_target.SizeByte > 254)
                throw new ArgumentException("too_long_target");

            target = _target;
        }

        private byte[] compactTarget;
        public byte[] CompactTarget
        {
            get
            {
                if (compactTarget == null)
                {
                    byte[] bytes = target.hash;

                    int numOfHead0 = 0;
                    for (int i = 0; i < bytes.Length && bytes[i] == 0; i++)
                        numOfHead0++;
                    if (numOfHead0 != 0)
                        bytes = bytes.Decompose(numOfHead0);

                    if (bytes.Length == 0)
                        return new byte[] { 0, 0, 0, 0 };

                    if (bytes[0] > 127)
                        bytes = new byte[] { 0 }.Combine(bytes);
                    bytes = new byte[] { (byte)bytes.Length }.Combine(bytes);
                    if (bytes.Length == 2)
                        bytes = bytes.Combine(new byte[] { 0, 0 });
                    else if (bytes.Length == 3)
                        bytes = bytes.Combine(new byte[] { 0 });
                    else if (bytes.Length > 4)
                        bytes = bytes.Decompose(0, 4);

                    compactTarget = bytes;
                }

                return compactTarget;
            }
        }

        private BlockidHashType target;
        public BlockidHashType Target
        {
            get
            {
                if (target == null)
                {
                    byte[] bytes;
                    if (compactTarget[0] > 2)
                        bytes = new byte[] { compactTarget[1], compactTarget[2], compactTarget[3] };
                    else if (compactTarget[0] == 2)
                        bytes = new byte[] { compactTarget[1], compactTarget[2] };
                    else if (compactTarget[0] == 1)
                        bytes = new byte[] { compactTarget[1] };
                    else
                        return target = Activator.CreateInstance(typeof(BlockidHashType)) as BlockidHashType;

                    int numOfTail0 = compactTarget[0] - 3;
                    if (numOfTail0 > 0)
                        bytes = bytes.Combine(new byte[numOfTail0]);

                    BlockidHashType newTarget = Activator.CreateInstance(typeof(BlockidHashType)) as BlockidHashType;

                    int numOfHead0 = newTarget.SizeByte - bytes.Length;
                    if (numOfHead0 > 0)
                        bytes = new byte[numOfHead0].Combine(bytes);

                    newTarget.FromHash(bytes);

                    target = newTarget;
                }

                return target;
            }
        }

        public double Diff { get { return BDiff; } }

        private double? pDifficulty;
        public double PDiff
        {
            get
            {
                if (pDifficulty == null)
                {
                    BlockidHashType tag = Target;

                    byte[] difficulty1TargetBytes = new byte[tag.SizeByte];
                    for (int i = 4; i < difficulty1TargetBytes.Length; i++)
                        difficulty1TargetBytes[i] = 255;

                    pDifficulty = CalculateDifficulty(difficulty1TargetBytes, tag.hash);
                }

                return pDifficulty.Value;
            }
        }

        private double? bDifficulty;
        public double BDiff
        {
            get
            {
                if (bDifficulty == null)
                {
                    BlockidHashType tag = Target;

                    byte[] difficulty1TargetBytes = new byte[tag.SizeByte];
                    if (difficulty1TargetBytes.Length > 4)
                        difficulty1TargetBytes[4] = 255;
                    if (difficulty1TargetBytes.Length > 5)
                        difficulty1TargetBytes[5] = 255;

                    bDifficulty = CalculateDifficulty(difficulty1TargetBytes, tag.hash);
                }

                return bDifficulty.Value;
            }
        }

        private double? averageHash;
        public double AverageHash
        {
            get
            {
                if (averageHash == null)
                {
                    BlockidHashType tag = Target;

                    byte[] maxBytes = Enumerable.Repeat((byte)255, tag.SizeByte).ToArray();

                    BigInteger maxBigInt = new BigInteger(maxBytes.Reverse().ToArray().Combine(new byte[] { 0 }));
                    BigInteger targetBigInt = new BigInteger(tag.hash.Reverse().ToArray().Combine(new byte[] { 0 }));

                    BigInteger averageHashBigInt100000000 = (maxBigInt * 100000000) / targetBigInt;

                    averageHash = (double)averageHashBigInt100000000 / 100000000.0;
                }

                return averageHash.Value;
            }
        }

        public double GetAverageHashrate(double averageInterval) { return AverageHash / averageInterval; }

        public double GetAverageTime(double averageHashrate) { return AverageHash / averageHashrate; }

        private double CalculateDifficulty(byte[] difficulty1TargetBytes, byte[] targetBytes)
        {
            BigInteger difficulty1TargetBigInt = new BigInteger(difficulty1TargetBytes.Reverse().ToArray().Combine(new byte[] { 0 }));
            BigInteger targetBigInt = new BigInteger(targetBytes.Reverse().ToArray().Combine(new byte[] { 0 }));

            BigInteger difficultyBigInt100000000 = (difficulty1TargetBigInt * 100000000) / targetBigInt;

            return (double)difficultyBigInt100000000 / 100000000.0;
        }
    }

    public abstract class TXBLOCKBASE<TxidBlockidHashType> : SHAREDDATA
        where TxidBlockidHashType : HASHBASE
    {
        public TXBLOCKBASE(int? _version) : base(_version) { }

        protected bool isModified;

        protected TxidBlockidHashType idCache;
        public virtual TxidBlockidHashType Id
        {
            get
            {
                if (isModified || idCache == null)
                {
                    idCache = Activator.CreateInstance(typeof(TxidBlockidHashType), ToBinary()) as TxidBlockidHashType;
                    isModified = false;
                }
                return idCache;
            }
        }

        public virtual bool IsValid { get { return true; } }
    }

    #region 取引

    public abstract class Transaction<TxidHashType, PubKeyHashType, PubKeyType> : TXBLOCKBASE<TxidHashType>
        where TxidHashType : HASHBASE
        where PubKeyHashType : HASHBASE
        where PubKeyType : DSAPUBKEYBASE
    {
        public Transaction(int? _version) : base(_version) { }

        public Transaction(int? _version, TransactionOutput<PubKeyHashType>[] _outputs)
            : base(_version)
        {
            if (_outputs.Length == 0)
                throw new InvalidDataException("tx_outputs_empty");

            outputs = _outputs;
        }

        private static readonly CurrencyUnit dustTxout = new Yumina(0.1m);
        private static readonly int maxSize = 65536;

        public TransactionOutput<PubKeyHashType>[] outputs { get; private set; }

        public override bool IsValid
        {
            get
            {
                if (!base.IsValid)
                    return false;

                if (Version == 0)
                {
                    if (!outputs.All((e) => e.amount.rawAmount >= dustTxout.rawAmount))
                        return false;

                    if (ToBinary().Length > maxSize)
                        return false;

                    return true;
                }
                else
                    throw new NotSupportedException("tx_is_valid_not_supported");
            }
        }

        protected override Func<ReaderWriter, IEnumerable<MainDataInfomation>> StreamInfo
        {
            get
            {
                return (msrw) => new MainDataInfomation[]{
                    new MainDataInfomation(typeof(TransactionOutput<PubKeyHashType>[]), null, null, () => outputs, (o) => outputs = (TransactionOutput<PubKeyHashType>[])o),
                };
            }
        }

        public virtual TransactionInput<TxidHashType, PubKeyType>[] Inputs { get { return new TransactionInput<TxidHashType, PubKeyType>[] { }; } }

        public virtual TransactionOutput<PubKeyHashType>[] Outputs { get { return outputs; } }
    }

    public class CoinbaseTransaction<TxidHashType, PubKeyHashType, PubKeyType> : Transaction<TxidHashType, PubKeyHashType, PubKeyType>
        where TxidHashType : HASHBASE
        where PubKeyHashType : HASHBASE
        where PubKeyType : DSAPUBKEYBASE
    {
        public CoinbaseTransaction() : base(0) { }

        public CoinbaseTransaction(TransactionOutput<PubKeyHashType>[] _outputs) : base(0, _outputs) { }

        protected override Func<ReaderWriter, IEnumerable<MainDataInfomation>> StreamInfo
        {
            get
            {
                if (Version == 0)
                    return base.StreamInfo;
                else
                    throw new NotSupportedException("coinbase_tx_main_data_info");
            }
        }

        public override bool IsVersioned { get { return true; } }
    }

    public class TransferTransaction<TxidHashType, PubKeyHashType, PubKeyType> : Transaction<TxidHashType, PubKeyHashType, PubKeyType>
        where TxidHashType : HASHBASE
        where PubKeyHashType : HASHBASE
        where PubKeyType : DSAPUBKEYBASE
    {
        public TransferTransaction() : base(0) { }

        public TransferTransaction(TransactionInput<TxidHashType, PubKeyType>[] _inputs, TransactionOutput<PubKeyHashType>[] _outputs)
            : base(0, _outputs)
        {
            if (_inputs.Length == 0)
                throw new InvalidDataException("tx_inputs_empty");

            inputs = _inputs;
        }

        public TransactionInput<TxidHashType, PubKeyType>[] inputs { get; private set; }

        protected override Func<ReaderWriter, IEnumerable<MainDataInfomation>> StreamInfo
        {
            get
            {
                if (Version == 0)
                    return (msrw) => base.StreamInfo(msrw).Concat(new MainDataInfomation[]{
                        new MainDataInfomation(typeof(TransactionInput<TxidHashType, PubKeyType>[]), null, null, () => inputs, (o) => inputs = (TransactionInput<TxidHashType, PubKeyType>[])o),
                    });
                else
                    throw new NotSupportedException("transfer_tx_main_data_info");
            }
        }

        public override bool IsVersioned { get { return true; } }

        public override TransactionInput<TxidHashType, PubKeyType>[] Inputs { get { return inputs; } }

        private Func<ReaderWriter, IEnumerable<MainDataInfomation>> StreamInfoToSign(TransactionOutput<PubKeyHashType>[] prevTxOutputs) { return (msrw) => StreamInfoToSignInner(prevTxOutputs); }
        private IEnumerable<MainDataInfomation> StreamInfoToSignInner(TransactionOutput<PubKeyHashType>[] prevTxOutputs)
        {
            if (Version == 0)
            {
                for (int i = 0; i < inputs.Length; i++)
                {
                    foreach (var mdi in inputs[i].StreamInfoToSign(null))
                        yield return mdi;
                    foreach (var mdi in prevTxOutputs[i].StreamInfoToSignPrev(null))
                        yield return mdi;
                }
                for (int i = 0; i < outputs.Length; i++)
                    foreach (var mdi in outputs[i].StreamInfoToSign(null))
                        yield return mdi;
            }
            else
                throw new NotSupportedException("transfer_tx_mdi_sign");
        }

        public byte[] GetBytesToSign(TransactionOutput<PubKeyHashType>[] prevTxOutputs)
        {
            if (prevTxOutputs.Length != inputs.Length)
                throw new ArgumentException("inputs_and_prev_outputs");

            return ToBinaryMainData(StreamInfoToSign(prevTxOutputs));
        }

        public void Sign(TransactionOutput<PubKeyHashType>[] prevTxOutputs, DSAPRIVKEYBASE[] privKeys)
        {
            if (prevTxOutputs.Length != inputs.Length)
                throw new ArgumentException("inputs_and_prev_outputs");
            if (privKeys.Length != inputs.Length)
                throw new ArgumentException("inputs_and_priv_keys");

            byte[] bytesToSign = GetBytesToSign(prevTxOutputs);

            for (int i = 0; i < inputs.Length; i++)
                inputs[i].SetSenderSig(privKeys[i].Sign(bytesToSign));

            //取引入力の内容が変更された
            isModified = true;
        }

        public bool VerifySignature(TransactionOutput<PubKeyHashType>[] prevTxOutputs)
        {
            if (prevTxOutputs.Length != inputs.Length)
                throw new ArgumentException("inputs_and_prev_outputs");

            byte[] bytesToSign = GetBytesToSign(prevTxOutputs);

            for (int i = 0; i < inputs.Length; i++)
                if (!inputs[i].senderPubKey.Verify(bytesToSign, inputs[i].senderSig))
                    return false;
            return true;
        }

        public bool VerifyPubKey(TransactionOutput<PubKeyHashType>[] prevTxOutputs)
        {
            if (prevTxOutputs.Length != inputs.Length)
                throw new ArgumentException("inputs_and_prev_outputs");

            for (int i = 0; i < inputs.Length; i++)
                if (!(Activator.CreateInstance(typeof(PubKeyHashType), inputs[i].senderPubKey.pubKey) as PubKeyHashType).Equals(prevTxOutputs[i].receiverPubKeyHash))
                    return false;
            return true;
        }

        public bool VerifyAmount(TransactionOutput<PubKeyHashType>[] prevTxOutputs)
        {
            if (prevTxOutputs.Length != inputs.Length)
                throw new ArgumentException("inputs_and_prev_outputs");

            return GetFee(prevTxOutputs).rawAmount >= 0;
        }

        public bool VerifyAll(TransactionOutput<PubKeyHashType>[] prevTxOutputs)
        {
            if (prevTxOutputs.Length != inputs.Length)
                throw new ArgumentException("inputs_and_prev_outputs");

            if (Version == 0)
                return VerifySignature(prevTxOutputs) && VerifyPubKey(prevTxOutputs) && VerifyAmount(prevTxOutputs);
            else
                throw new NotSupportedException("transfer_tx_verify_all");
        }

        public CurrencyUnit GetFee(TransactionOutput<PubKeyHashType>[] prevTxOutputs)
        {
            if (prevTxOutputs.Length != inputs.Length)
                throw new ArgumentException("inputs_and_prev_outputs");

            long totalPrevOutputs = 0;
            for (int i = 0; i < prevTxOutputs.Length; i++)
                totalPrevOutputs += prevTxOutputs[i].amount.rawAmount;
            long totalOutpus = 0;
            for (int i = 0; i < outputs.Length; i++)
                totalOutpus += outputs[i].amount.rawAmount;

            return new CurrencyUnit(totalPrevOutputs - totalOutpus);
        }
    }

    public class TransactionInput<TxidHashType, PubKeyType> : SHAREDDATA
        where TxidHashType : HASHBASE
        where PubKeyType : DSAPUBKEYBASE
    {
        public TransactionInput() : base(null) { }

        public TransactionInput(long _prevTxBlockIndex, int _prevTxIndex, int _prevTxOutputIndex, PubKeyType _senderPubKey)
            : base(null)
        {
            prevTxBlockIndex = _prevTxBlockIndex;
            prevTxIndex = _prevTxIndex;
            //prevTxHash = _prevTxHash;
            prevTxOutputIndex = _prevTxOutputIndex;
            senderPubKey = _senderPubKey;
            //amount = _amount;
        }

        public static readonly int senderSigLength = 64;

        public long prevTxBlockIndex { get; private set; }
        public int prevTxIndex { get; private set; }
        //public TxidHashType prevTxHash { get; private set; }
        public int prevTxOutputIndex { get; private set; }
        public byte[] senderSig { get; private set; }
        public PubKeyType senderPubKey { get; private set; }
        //public CurrencyUnit amount { get; private set; }

        protected override Func<ReaderWriter, IEnumerable<MainDataInfomation>> StreamInfo
        {
            get
            {
                return (msrw) => new MainDataInfomation[]{
                    new MainDataInfomation(typeof(long), () => prevTxBlockIndex, (o) => prevTxBlockIndex = (long)o),
                    new MainDataInfomation(typeof(int), () => prevTxIndex, (o) => prevTxIndex = (int)o),
                    //new MainDataInfomation(typeof(TxidHashType), null, () => prevTxHash, (o) => prevTxHash = (TxidHashType)o),
                    new MainDataInfomation(typeof(int), () => prevTxOutputIndex, (o) => prevTxOutputIndex = (int)o),
                    new MainDataInfomation(typeof(byte[]), senderSigLength, () => senderSig, (o) => senderSig = (byte[])o),
                    new MainDataInfomation(typeof(PubKeyType), null, () => senderPubKey, (o) => senderPubKey = (PubKeyType)o),
                    //new MainDataInfomation(typeof(long), () => amount.rawAmount, (o) => amount = new CurrencyUnit((long)o)),
                };
            }
        }

        public Func<ReaderWriter, IEnumerable<MainDataInfomation>> StreamInfoToSign
        {
            get
            {
                return (msrw) => new MainDataInfomation[]{
                    new MainDataInfomation(typeof(long), () => prevTxBlockIndex, (o) => { throw new NotSupportedException("tx_in_si_to_sign"); }),
                    new MainDataInfomation(typeof(int), () => prevTxIndex, (o) => { throw new NotSupportedException("tx_in_si_to_sign"); }),
                    //new MainDataInfomation(typeof(TxidHashType), null, () => prevTxHash, (o) => { throw new NotSupportedException("tx_in_si_to_sign"); }),
                    new MainDataInfomation(typeof(int), () => prevTxOutputIndex, (o) => { throw new NotSupportedException("tx_in_si_to_sign"); }),
                    //new MainDataInfomation(typeof(long), () => amount.rawAmount, (o) => { throw new NotSupportedException("tx_out_si_to_sign"); }),
                };
            }
        }

        public void SetSenderSig(byte[] sig)
        {
            if (sig.Length != senderSigLength)
                throw new ArgumentException("tx_in_sender_sig_length");

            senderSig = sig;
        }
    }

    public class TransactionOutput<PubKeyHashType> : SHAREDDATA where PubKeyHashType : HASHBASE
    {
        public TransactionOutput() : base(null) { }

        public TransactionOutput(PubKeyHashType _receiverPubKeyHash, CurrencyUnit _amount)
            : base(null)
        {
            receiverPubKeyHash = _receiverPubKeyHash;
            amount = _amount;
        }

        public PubKeyHashType receiverPubKeyHash { get; private set; }
        public CurrencyUnit amount { get; private set; }

        protected override Func<ReaderWriter, IEnumerable<MainDataInfomation>> StreamInfo
        {
            get
            {
                return (msrw) => new MainDataInfomation[]{
                    new MainDataInfomation(typeof(PubKeyHashType), null, () => receiverPubKeyHash, (o) => receiverPubKeyHash = (PubKeyHashType)o),
                    new MainDataInfomation(typeof(long), () => amount.rawAmount, (o) => amount = new CurrencyUnit((long)o)),
                };
            }
        }

        public Func<ReaderWriter, IEnumerable<MainDataInfomation>> StreamInfoToSign
        {
            get
            {
                return (msrw) => new MainDataInfomation[]{
                    new MainDataInfomation(typeof(PubKeyHashType), null, () => receiverPubKeyHash, (o) => { throw new NotSupportedException("tx_out_si_to_sign"); }),
                    new MainDataInfomation(typeof(long), () => amount.rawAmount, (o) => { throw new NotSupportedException("tx_out_si_to_sign"); }),
                };
            }
        }

        public Func<ReaderWriter, IEnumerable<MainDataInfomation>> StreamInfoToSignPrev
        {
            get
            {
                return (msrw) => new MainDataInfomation[]{
                    new MainDataInfomation(typeof(PubKeyHashType), null, () => receiverPubKeyHash, (o) => { throw new NotSupportedException("tx_out_si_to_sign_prev"); }),
                };
            }
        }
    }

    #endregion

    #region ブロック

    public abstract class Block<BlockidHashType> : TXBLOCKBASE<BlockidHashType>
        where BlockidHashType : HASHBASE
    {
        public Block(int? _version) : base(_version) { }
    }

    public class GenesisBlock<BlockidHashType> : Block<BlockidHashType>
        where BlockidHashType : HASHBASE
    {
        public GenesisBlock() : base(null) { }

        public readonly string genesisWord = "Bitstamp 2014/05/25 BTC/USD High 586.34 BTC to the moooooooon!!";

        protected override Func<ReaderWriter, IEnumerable<MainDataInfomation>> StreamInfo
        {
            get
            {
                return (msrw) => new MainDataInfomation[]{
                    new MainDataInfomation(typeof(string), () => genesisWord, (o) => { throw new NotSupportedException("genesis_block_cant_read"); }),
                };
            }
        }
    }

    public abstract class TransactionalBlock<BlockidHashType, TxidHashType, PubKeyHashType, PubKeyType> : Block<BlockidHashType>
        where BlockidHashType : HASHBASE
        where TxidHashType : HASHBASE
        where PubKeyHashType : HASHBASE
        where PubKeyType : DSAPUBKEYBASE
    {
        static TransactionalBlock()
        {
            rewards = new CurrencyUnit[numberOfCycles];
            rewards[0] = initialReward;
            for (int i = 1; i < numberOfCycles; i++)
                rewards[i] = new Creacoin(rewards[i - 1].Amount * rewardReductionRate);
        }

        public TransactionalBlock(int? _version) : base(_version) { }

        public TransactionalBlock(int? _version, BlockHeader<BlockidHashType, TxidHashType> _header, CoinbaseTransaction<TxidHashType, PubKeyHashType, PubKeyType> _coinbaseTxToMiner, TransferTransaction<TxidHashType, PubKeyHashType, PubKeyType>[] _transferTxs)
            : base(_version)
        {
            header = _header;
            coinbaseTxToMiner = _coinbaseTxToMiner;
            transferTxs = _transferTxs;
        }

        private static readonly long blockGenerationInterval = 5; //[sec]
        private static readonly long cycle = 60 * 60 * 24 * 365; //[sec]
        private static readonly int numberOfCycles = 8;
        private static readonly long rewardlessStart = cycle * numberOfCycles; //[sec]
        private static readonly decimal rewardReductionRate = 0.8m;
        private static readonly CurrencyUnit initialReward = new Creacoin(1.0m); //[CREA/sec]
        private static readonly CurrencyUnit[] rewards; //[CREA/sec]
        private static readonly decimal foundationShare = 0.1m;
        private static readonly long foundationInterval = 60 * 60 * 24; //[block]

        private static readonly long numberOfTimestamps = 11;
        private static readonly long targetTimespan = blockGenerationInterval * 1; //[sec]

        private static readonly int maxSize = 1048576;

        public static readonly Difficulty<BlockidHashType> minDifficulty = new Difficulty<BlockidHashType>(HASHBASE.FromHash<BlockidHashType>(new byte[] { 0, 127, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255 }));

#if TEST
        public static readonly PubKeyHashType foundationPubKeyHash = Activator.CreateInstance(typeof(PubKeyHashType), new byte[] { 69, 67, 83, 49, 32, 0, 0, 0, 16, 31, 116, 194, 127, 71, 154, 183, 50, 198, 23, 17, 129, 220, 25, 98, 4, 30, 93, 45, 53, 252, 176, 145, 108, 20, 226, 233, 36, 7, 35, 198, 98, 239, 109, 66, 206, 41, 162, 179, 255, 189, 126, 72, 97, 140, 165, 139, 118, 107, 137, 103, 76, 238, 125, 62, 163, 205, 108, 62, 189, 240, 124, 71 }) as PubKeyHashType;
#endif

        public BlockHeader<BlockidHashType, TxidHashType> header { get; private set; }
        public CoinbaseTransaction<TxidHashType, PubKeyHashType, PubKeyType> coinbaseTxToMiner { get; private set; }
        public TransferTransaction<TxidHashType, PubKeyHashType, PubKeyType>[] transferTxs { get; private set; }

        public override BlockidHashType Id
        {
            get
            {
                if (isModified || idCache == null)
                {
                    idCache = Activator.CreateInstance(typeof(BlockidHashType), header.ToBinary()) as BlockidHashType;
                    isModified = false;
                }
                return idCache;
            }
        }

        public Transaction<TxidHashType, PubKeyHashType, PubKeyType>[] transactionsCache;
        public virtual Transaction<TxidHashType, PubKeyHashType, PubKeyType>[] Transactions
        {
            get
            {
                if (isTransactionsModified || transactionsCache == null)
                {
                    transactionsCache = new Transaction<TxidHashType, PubKeyHashType, PubKeyType>[transferTxs.Length + 1];
                    transactionsCache[0] = coinbaseTxToMiner;
                    for (int i = 0; i < transferTxs.Length; i++)
                        transactionsCache[i + 1] = transferTxs[i];
                    isTransactionsModified = false;
                }
                return transactionsCache;
            }
        }

        protected bool isTransactionsModified;

        protected MerkleTree<TxidHashType> merkleTreeCache;
        public MerkleTree<TxidHashType> MerkleTree
        {
            get
            {
                if (isTransactionsModified || merkleTreeCache == null)
                {
                    merkleTreeCache = new MerkleTree<TxidHashType>(Transactions.Select((e) => e.Id).ToArray());
                    isTransactionsModified = false;
                }
                return merkleTreeCache;
            }
        }

        public override bool IsValid
        {
            get
            {
                if (!base.IsValid)
                    return false;

                if (Version == 0)
                {
                    if (this.GetType() != GetBlockType(header.index, Version))
                        return false;

                    if (!Transactions.All((e) => e.IsValid))
                        return false;

                    if (!header.merkleRootHash.Equals(MerkleTree.Root))
                        return false;

                    if (Id.CompareTo(header.difficulty.Target) > 0)
                        return false;

                    if (ToBinary().Length > maxSize)
                        return false;

                    return true;
                }
                else
                    throw new NotSupportedException("tx_block_is_valid_not_supported");
            }
        }

        protected override Func<ReaderWriter, IEnumerable<MainDataInfomation>> StreamInfo
        {
            get
            {
                return (msrw) => new MainDataInfomation[]{
                    new MainDataInfomation(typeof(BlockHeader<BlockidHashType, TxidHashType>), null, () => header, (o) => header = (BlockHeader<BlockidHashType, TxidHashType>)o),
                    new MainDataInfomation(typeof(CoinbaseTransaction<TxidHashType, PubKeyHashType, PubKeyType>), 0, () => coinbaseTxToMiner, (o) => coinbaseTxToMiner = (CoinbaseTransaction<TxidHashType, PubKeyHashType, PubKeyType>)o),
                    new MainDataInfomation(typeof(TransferTransaction<TxidHashType, PubKeyHashType, PubKeyType>[]), 0, null, () => transferTxs, (o) => transferTxs = (TransferTransaction<TxidHashType, PubKeyHashType, PubKeyType>[])o),
                };
            }
        }

        public void UpdateMerkleRootHash()
        {
            header.UpdateMerkleRootHash(MerkleTree.Root);

            isModified = true;
        }

        public void UpdateTimestamp(DateTime newTimestamp)
        {
            header.UpdateTimestamp(newTimestamp);

            isModified = true;
        }

        public void UpdateNonce(byte[] newNonce)
        {
            header.UpdateNonce(newNonce);

            isModified = true;
        }

        public bool VerifyTransferTransaction(TransactionOutput<PubKeyHashType>[][] prevTxOutputss)
        {
            if (prevTxOutputss.Length != transferTxs.Length)
                throw new ArgumentException("transfet_txs_and_prev_outputs");

            for (int i = 0; i < transferTxs.Length; i++)
                if (!transferTxs[i].VerifyAll(prevTxOutputss[i]))
                    return false;
            return true;
        }

        public virtual bool VerifyRewardAndTxFee(TransactionOutput<PubKeyHashType>[][] prevTxOutputss)
        {
            if (prevTxOutputss.Length != transferTxs.Length)
                throw new ArgumentException("transfet_txs_and_prev_outputs");

            return GetActualRewardToMinerAndTxFee().rawAmount == GetValidRewardToMinerAndTxFee(prevTxOutputss).rawAmount;
        }

        public bool VerifyTimestamp(Func<long, TransactionalBlock<BlockidHashType, TxidHashType, PubKeyHashType, PubKeyType>> indexToTxBlock)
        {
            List<DateTime> timestamps = new List<DateTime>();
            for (long i = 1; i < numberOfTimestamps + 1 && header.index - i > 0; i++)
                timestamps.Add(indexToTxBlock(header.index - i).header.timestamp);
            timestamps.Sort();

            if (timestamps.Count == 0)
                return true;

            return (timestamps.Count / 2).Operate((index) => header.timestamp > (timestamps.Count % 2 == 0 ? timestamps[index - 1] + new TimeSpan((timestamps[index] - timestamps[index - 1]).Ticks / 2) : timestamps[index]));
        }

        public bool VerifyDifficulty(Func<long, TransactionalBlock<BlockidHashType, TxidHashType, PubKeyHashType, PubKeyType>> indexToTxBlock)
        {
            return header.difficulty.Diff == GetWorkRequired(header.index, indexToTxBlock, 0).Diff;
        }

        public virtual bool VerifyAll(TransactionOutput<PubKeyHashType>[][] prevTxOutputss, Func<long, TransactionalBlock<BlockidHashType, TxidHashType, PubKeyHashType, PubKeyType>> indexToTxBlock)
        {
            if (prevTxOutputss.Length != transferTxs.Length)
                throw new ArgumentException("transfet_txs_and_prev_outputs");

            if (Version == 0)
                return VerifyTransferTransaction(prevTxOutputss) && VerifyRewardAndTxFee(prevTxOutputss) & VerifyTimestamp(indexToTxBlock) && VerifyDifficulty(indexToTxBlock);
            else
                throw new NotSupportedException("tx_block_not_supported");
        }

        public CurrencyUnit GetValidRewardToMiner() { return GetRewardToMiner(header.index, Version); }

        public CurrencyUnit GetValidTxFee(TransactionOutput<PubKeyHashType>[][] prevTxOutputss)
        {
            if (prevTxOutputss.Length != transferTxs.Length)
                throw new ArgumentException("transfet_txs_and_prev_outputs");

            long rawTxFee = 0;
            for (int i = 0; i < transferTxs.Length; i++)
                rawTxFee += transferTxs[i].GetFee(prevTxOutputss[i]).rawAmount;
            return new CurrencyUnit(rawTxFee);
        }

        public CurrencyUnit GetValidRewardToMinerAndTxFee(TransactionOutput<PubKeyHashType>[][] prevTxOutputss)
        {
            if (prevTxOutputss.Length != transferTxs.Length)
                throw new ArgumentException("transfet_txs_and_prev_outputs");

            return new CurrencyUnit(GetValidRewardToMiner().rawAmount + GetValidTxFee(prevTxOutputss).rawAmount);
        }

        public CurrencyUnit GetActualRewardToMinerAndTxFee()
        {
            long rawTxFee = 0;
            for (int i = 0; i < coinbaseTxToMiner.outputs.Length; i++)
                rawTxFee += coinbaseTxToMiner.outputs[i].amount.rawAmount;
            return new CurrencyUnit(rawTxFee);
        }

        public static Type GetBlockType(long index, int version)
        {
            if (index < 1)
                throw new ArgumentOutOfRangeException("index_out");

            if (version == 0)
            {
                return index % foundationInterval == 0 ? typeof(FoundationalBlock<BlockidHashType, TxidHashType, PubKeyHashType, PubKeyType>) : typeof(NormalBlock<BlockidHashType, TxidHashType, PubKeyHashType, PubKeyType>);
            }
            else
                throw new NotSupportedException("tx_block_not_supported");
        }

        public static CurrencyUnit GetRewardToAll(long index, int version)
        {
            if (index < 1)
                throw new ArgumentOutOfRangeException("index_out");

            if (version == 0)
            {
                long sec = index * blockGenerationInterval;
                for (int i = 0; i < numberOfCycles; i++)
                    if (sec < cycle * (i + 1))
                        return new Creacoin(rewards[i].rawAmount * (long)blockGenerationInterval);
                return new Creacoin(0.0m);
            }
            else
                throw new NotSupportedException("tx_block_not_supported");
        }

        public static CurrencyUnit GetRewardToMiner(long index, int version)
        {
            if (index < 1)
                throw new ArgumentOutOfRangeException("index_out");

            if (version == 0)
                return new Creacoin(GetRewardToAll(index, version).AmountInCreacoin.Amount * (1.0m - foundationShare));
            else
                throw new NotSupportedException("tx_block_not_supported");
        }

        public static CurrencyUnit GetRewardToFoundation(long index, int version)
        {
            if (index < 1)
                throw new ArgumentOutOfRangeException("index_out");

            if (version == 0)
                return new Creacoin(GetRewardToAll(index, version).AmountInCreacoin.Amount * foundationShare);
            else
                throw new NotSupportedException("tx_block_not_supported");
        }

        public static CurrencyUnit GetRewardToFoundationInterval(long index, int version)
        {
            if (index < 1)
                throw new ArgumentOutOfRangeException("index_out");

            if (version == 0)
                return new Creacoin(GetRewardToFoundation(index, version).AmountInCreacoin.Amount * foundationInterval);
            else
                throw new NotSupportedException("tx_block_not_supported");
        }

        public static Difficulty<BlockidHashType> GetWorkRequired(long index, Func<long, TransactionalBlock<BlockidHashType, TxidHashType, PubKeyHashType, PubKeyType>> indexToTxBlock, int version)
        {
            if (index < 1)
                throw new ArgumentOutOfRangeException("index_out");

            if (version == 0)
            {
                if (index == 1)
                    return minDifficulty;

                long retargetInterval = targetTimespan / blockGenerationInterval;
                long lastIndex = index - 1;

                if (index % retargetInterval != 0)
                    return indexToTxBlock(lastIndex).header.difficulty;
                else
                {
                    long blocksToGoBack = index != retargetInterval ? retargetInterval : retargetInterval - 1;
                    long firstIndex = lastIndex - blocksToGoBack > 0 ? lastIndex - blocksToGoBack : 1;

                    TimeSpan actualTimespan = indexToTxBlock(lastIndex).header.timestamp - indexToTxBlock(firstIndex).header.timestamp;

                    if (actualTimespan.TotalSeconds < targetTimespan - (targetTimespan / 4.0))
                        actualTimespan = TimeSpan.FromSeconds(targetTimespan - (targetTimespan / 4.0));
                    else if (actualTimespan.TotalSeconds > targetTimespan + (targetTimespan / 2.0))
                        actualTimespan = TimeSpan.FromSeconds(targetTimespan + (targetTimespan / 2.0));

                    double ratio = (double)actualTimespan.TotalSeconds / (double)targetTimespan;

                    byte[] prevTargetBytes = indexToTxBlock(lastIndex).header.difficulty.Target.hash;

                    int? pos = null;
                    for (int i = 0; i < prevTargetBytes.Length && pos == null; i++)
                        if (prevTargetBytes[i] != 0)
                            pos = i;

                    double prevtarget2BytesDouble;
                    if (pos != prevTargetBytes.Length - 1)
                        prevtarget2BytesDouble = prevTargetBytes[pos.Value] * 256 + prevTargetBytes[pos.Value + 1];
                    else
                        prevtarget2BytesDouble = prevTargetBytes[pos.Value];

                    prevtarget2BytesDouble *= ratio;

                    List<byte> target3Bytes = new List<byte>();
                    while (prevtarget2BytesDouble > 255)
                    {
                        target3Bytes.Add((byte)(prevtarget2BytesDouble % 256));
                        prevtarget2BytesDouble /= 256;
                    }
                    target3Bytes.Add((byte)prevtarget2BytesDouble);

                    BlockidHashType hash = Activator.CreateInstance(typeof(BlockidHashType)) as BlockidHashType;

                    byte[] targetBytes = new byte[hash.SizeByte];
                    if (pos != prevTargetBytes.Length - 1)
                    {
                        if (target3Bytes.Count == 3)
                            if (pos == 0)
                            {
                                targetBytes[pos.Value] = 255;
                                targetBytes[pos.Value + 1] = target3Bytes[0];
                            }
                            else
                            {
                                targetBytes[pos.Value - 1] = target3Bytes[2];
                                targetBytes[pos.Value] = target3Bytes[1];
                                targetBytes[pos.Value + 1] = target3Bytes[0];
                            }
                        else
                        {
                            targetBytes[pos.Value] = target3Bytes[1];
                            targetBytes[pos.Value + 1] = target3Bytes[0];
                        }
                    }
                    else
                    {
                        if (target3Bytes.Count == 2)
                        {
                            targetBytes[pos.Value - 1] = target3Bytes[1];
                            targetBytes[pos.Value] = target3Bytes[0];
                        }
                        else
                            targetBytes[pos.Value] = target3Bytes[0];
                    }

                    hash.FromHash(targetBytes);

                    Difficulty<BlockidHashType> difficulty = new Difficulty<BlockidHashType>(hash);

                    return (difficulty.Diff < minDifficulty.Diff ? minDifficulty : difficulty).Operate((dif) => dif.RaiseNotification("difficulty", 3, difficulty.Diff.ToString()));
                }
            }
            else
                throw new NotSupportedException("tx_block_not_supported");
        }

        private static TransactionalBlock<BlockidHashType, TxidHashType, PubKeyHashType, PubKeyType> GetBlockTemplate(long index, PubKeyHashType minerPubKeyHash, Func<long, TransactionalBlock<BlockidHashType, TxidHashType, PubKeyHashType, PubKeyType>> indexToTxBlock, int version)
        {
            if (index < 1)
                throw new ArgumentOutOfRangeException("index_out");

            if (version == 0)
            {
                CoinbaseTransaction<TxidHashType, PubKeyHashType, PubKeyType> coinbaseTxToMiner = new CoinbaseTransaction<TxidHashType, PubKeyHashType, PubKeyType>(new TransactionOutput<PubKeyHashType>[] { new TransactionOutput<PubKeyHashType>(minerPubKeyHash, GetRewardToMiner(index, version)) });

                BlockHeader<BlockidHashType, TxidHashType> header = new BlockHeader<BlockidHashType, TxidHashType>(index, index == 1 ? new GenesisBlock<BlockidHashType>().Id : indexToTxBlock(index - 1).Id, DateTime.Now, GetWorkRequired(index, indexToTxBlock, version), new byte[] { });

                TransactionalBlock<BlockidHashType, TxidHashType, PubKeyHashType, PubKeyType> txBlock;
                if (GetBlockType(index, version) == typeof(NormalBlock<BlockidHashType, TxidHashType, PubKeyHashType, PubKeyType>))
                    txBlock = new NormalBlock<BlockidHashType, TxidHashType, PubKeyHashType, PubKeyType>(header, coinbaseTxToMiner, new TransferTransaction<TxidHashType, PubKeyHashType, PubKeyType>[] { });
                else
                {
                    CoinbaseTransaction<TxidHashType, PubKeyHashType, PubKeyType> coinbaseTxToFoundation = new CoinbaseTransaction<TxidHashType, PubKeyHashType, PubKeyType>(new TransactionOutput<PubKeyHashType>[] { new TransactionOutput<PubKeyHashType>(foundationPubKeyHash, GetRewardToFoundationInterval(index, version)) });

                    txBlock = new FoundationalBlock<BlockidHashType, TxidHashType, PubKeyHashType, PubKeyType>(header, coinbaseTxToMiner, coinbaseTxToFoundation, new TransferTransaction<TxidHashType, PubKeyHashType, PubKeyType>[] { });
                }

                txBlock.UpdateMerkleRootHash();

                return txBlock;
            }
            else
                throw new NotSupportedException("tx_block_not_supported");
        }

        public static TransactionalBlock<BlockidHashType, TxidHashType, PubKeyHashType, PubKeyType> GetBlockTemplate(long index, PubKeyHashType minerPubKeyHash, Func<long, TransactionalBlock<BlockidHashType, TxidHashType, PubKeyHashType, PubKeyType>> indexToTxBlock)
        {
            return GetBlockTemplate(index, minerPubKeyHash, indexToTxBlock, 0);
        }
    }

    public class NormalBlock<BlockidHashType, TxidHashType, PubKeyHashType, PubKeyType> : TransactionalBlock<BlockidHashType, TxidHashType, PubKeyHashType, PubKeyType>
        where BlockidHashType : HASHBASE
        where TxidHashType : HASHBASE
        where PubKeyHashType : HASHBASE
        where PubKeyType : DSAPUBKEYBASE
    {
        public NormalBlock() : base(0) { }

        public NormalBlock(BlockHeader<BlockidHashType, TxidHashType> _header, CoinbaseTransaction<TxidHashType, PubKeyHashType, PubKeyType> _coinbaseTxToMiner, TransferTransaction<TxidHashType, PubKeyHashType, PubKeyType>[] _transferTransactions) : base(0, _header, _coinbaseTxToMiner, _transferTransactions) { }

        protected override Func<ReaderWriter, IEnumerable<MainDataInfomation>> StreamInfo
        {
            get
            {
                if (Version == 0)
                    return base.StreamInfo;
                else
                    throw new NotSupportedException("normal_block_main_data_info");
            }
        }

        public override bool IsVersioned { get { return true; } }

        public override bool IsCorruptionChecked
        {
            get
            {
                if (Version <= 0)
                    return true;
                else
                    throw new NotSupportedException("normal_block_check");
            }
        }
    }

    public class FoundationalBlock<BlockidHashType, TxidHashType, PubKeyHashType, PubKeyType> : TransactionalBlock<BlockidHashType, TxidHashType, PubKeyHashType, PubKeyType>
        where BlockidHashType : HASHBASE
        where TxidHashType : HASHBASE
        where PubKeyHashType : HASHBASE
        where PubKeyType : DSAPUBKEYBASE
    {
        public FoundationalBlock() : base(0) { }

        public FoundationalBlock(BlockHeader<BlockidHashType, TxidHashType> _header, CoinbaseTransaction<TxidHashType, PubKeyHashType, PubKeyType> _coinbaseTxToMiner, CoinbaseTransaction<TxidHashType, PubKeyHashType, PubKeyType> _coinbaseTxToFoundation, TransferTransaction<TxidHashType, PubKeyHashType, PubKeyType>[] _transferTransactions)
            : base(0, _header, _coinbaseTxToMiner, _transferTransactions)
        {
            coinbaseTxToFoundation = _coinbaseTxToFoundation;
        }

        public CoinbaseTransaction<TxidHashType, PubKeyHashType, PubKeyType> coinbaseTxToFoundation { get; private set; }

        public override Transaction<TxidHashType, PubKeyHashType, PubKeyType>[] Transactions
        {
            get
            {
                if (isTransactionsModified || transactionsCache == null)
                {
                    transactionsCache = new Transaction<TxidHashType, PubKeyHashType, PubKeyType>[transferTxs.Length + 2];
                    transactionsCache[0] = coinbaseTxToMiner;
                    transactionsCache[1] = coinbaseTxToFoundation;
                    for (int i = 0; i < transferTxs.Length; i++)
                        transactionsCache[i + 2] = transferTxs[i];
                    isTransactionsModified = false;
                }
                return transactionsCache;
            }
        }

        public override bool IsValid
        {
            get
            {
                if (!base.IsValid)
                    return false;

                if (Version == 0)
                {
                    if (!coinbaseTxToFoundation.outputs.All((e) => e.receiverPubKeyHash.Equals(foundationPubKeyHash)))
                        return false;

                    return true;
                }
                else
                    throw new NotSupportedException("foundation_block_is_valid_not_supported");
            }
        }

        protected override Func<ReaderWriter, IEnumerable<MainDataInfomation>> StreamInfo
        {
            get
            {
                if (Version == 0)
                    return (msrw) => base.StreamInfo(msrw).Concat(new MainDataInfomation[]{
                        new MainDataInfomation(typeof(CoinbaseTransaction<TxidHashType, PubKeyHashType, PubKeyType>), 0, () => coinbaseTxToFoundation, (o) => coinbaseTxToFoundation = (CoinbaseTransaction<TxidHashType, PubKeyHashType, PubKeyType>)o),
                    });
                else
                    throw new NotSupportedException("foundational_block_main_data_info");
            }
        }

        public override bool IsVersioned { get { return true; } }

        public override bool IsCorruptionChecked
        {
            get
            {
                if (Version <= 0)
                    return true;
                else
                    throw new NotSupportedException("normal_block_check");
            }
        }

        public override bool VerifyRewardAndTxFee(TransactionOutput<PubKeyHashType>[][] prevTxOutputss)
        {
            if (!base.VerifyRewardAndTxFee(prevTxOutputss))
                return false;

            return GetActualRewardToFoundation().rawAmount == GetValidRewardToFoundation().rawAmount;
        }

        public CurrencyUnit GetValidRewardToFoundation()
        {
            return GetRewardToFoundationInterval(header.index, Version);
        }

        public CurrencyUnit GetActualRewardToFoundation()
        {
            long rawTxFee = 0;
            for (int i = 0; i < coinbaseTxToFoundation.outputs.Length; i++)
                rawTxFee += coinbaseTxToFoundation.outputs[i].amount.rawAmount;
            return new CurrencyUnit(rawTxFee);
        }
    }

    public class BlockHeader<BlockidHashType, TxidHashType> : SHAREDDATA
        where BlockidHashType : HASHBASE
        where TxidHashType : HASHBASE
    {
        public BlockHeader() : base(null) { }

        public BlockHeader(long _index, BlockidHashType _prevBlockHash, DateTime _timestamp, Difficulty<BlockidHashType> _difficulty, byte[] _nonce)
            : base(null)
        {
            if (_index < 1)
                throw new ArgumentOutOfRangeException("block_header_index_out");
            if (_nonce.Length > maxNonceLength)
                throw new ArgumentOutOfRangeException("block_header_nonce_out");

            index = _index;
            prevBlockHash = _prevBlockHash;
            timestamp = _timestamp;
            difficulty = _difficulty;
            nonce = _nonce;
        }

        public static readonly int maxNonceLength = 10;

        public long index { get; private set; }
        public BlockidHashType prevBlockHash { get; private set; }
        public TxidHashType merkleRootHash { get; private set; }
        public DateTime timestamp { get; private set; }
        public Difficulty<BlockidHashType> difficulty { get; private set; }
        public byte[] nonce { get; private set; }

        protected override Func<ReaderWriter, IEnumerable<MainDataInfomation>> StreamInfo
        {
            get
            {
                return (msrw) => new MainDataInfomation[]{
                    new MainDataInfomation(typeof(long), () => index, (o) => index = (long)o),
                    new MainDataInfomation(typeof(BlockidHashType), null, () => prevBlockHash, (o) => prevBlockHash = (BlockidHashType)o),
                    new MainDataInfomation(typeof(TxidHashType), null, () => merkleRootHash, (o) => merkleRootHash = (TxidHashType)o),
                    new MainDataInfomation(typeof(DateTime), () => timestamp, (o) => timestamp = (DateTime)o),
                    new MainDataInfomation(typeof(byte[]), 4, () => difficulty.CompactTarget, (o) => difficulty = new Difficulty<BlockidHashType>((byte[])o)),
                    new MainDataInfomation(typeof(byte[]), null, () => nonce, (o) => nonce = (byte[])o),
                };
            }
        }

        public void UpdateMerkleRootHash(TxidHashType newmerkleRootHash)
        {
            merkleRootHash = newmerkleRootHash;
        }

        public void UpdateTimestamp(DateTime newTimestamp)
        {
            timestamp = newTimestamp;
        }

        public void UpdateNonce(byte[] newNonce)
        {
            if (newNonce.Length > maxNonceLength)
                throw new ArgumentOutOfRangeException("block_header_nonce_out");

            nonce = newNonce;
        }
    }

    #endregion

    public class Mining<BlockidHashType, TxidHashType, PubKeyHashType, PubKeyType>
        where BlockidHashType : HASHBASE
        where TxidHashType : HASHBASE
        where PubKeyHashType : HASHBASE
        where PubKeyType : DSAPUBKEYBASE
    {
        public Mining()
        {
            are = new AutoResetEvent(false);

            this.StartTask("mining", "mining", () =>
            {
                byte[] bytes = new byte[] { 0, 0, 0, 0 };
                int counter = 0;
                DateTime datetime1 = DateTime.Now;
                DateTime datetime2 = DateTime.Now;

                while (true)
                {
                    TransactionalBlock<BlockidHashType, TxidHashType, PubKeyHashType, PubKeyType> txBlockCopy = txBlock;

                    while (!isStarted || txBlockCopy == null)
                    {
                        are.WaitOne(30000);

                        txBlockCopy = txBlock;
                    }

                    txBlockCopy.UpdateTimestamp(datetime1 = DateTime.Now);
                    txBlockCopy.UpdateNonce(bytes);

                    if (datetime1.Second != datetime2.Second)
                    {
                        this.RaiseNotification("hash_rate", 5, counter.ToString());

                        datetime2 = datetime1;
                        counter = 0;
                    }
                    else
                        counter++;

                    if (txBlockCopy.Id.CompareTo(txBlockCopy.header.difficulty.Target) < 0)
                    {
                        this.RaiseNotification("found_block", 5);

                        txBlock = null;

                        FoundNonce(this, txBlockCopy);
                    }

                    if (bytes[0] != 255)
                        bytes[0]++;
                    else if (bytes[1] != 255)
                    {
                        bytes[1]++;
                        bytes[0] = 0;
                    }
                    else if (bytes[2] != 255)
                    {
                        bytes[2]++;
                        bytes[0] = bytes[1] = 0;
                    }
                    else if (bytes[3] != 255)
                    {
                        bytes[3]++;
                        bytes[0] = bytes[1] = bytes[2] = 0;
                    }
                    else
                        bytes[0] = bytes[1] = bytes[2] = bytes[3] = 0;
                }
            });
        }

        private TransactionalBlock<BlockidHashType, TxidHashType, PubKeyHashType, PubKeyType> txBlock;
        private AutoResetEvent are;

        public bool isStarted { get; private set; }

        public event EventHandler<TransactionalBlock<BlockidHashType, TxidHashType, PubKeyHashType, PubKeyType>> FoundNonce = delegate { };

        public void Start()
        {
            if (isStarted)
                throw new InvalidOperationException("already_started");

            isStarted = true;
        }

        public void End()
        {
            if (!isStarted)
                throw new InvalidOperationException("not_yet_started");

            isStarted = false;
        }

        public void NewMiningBlock(TransactionalBlock<BlockidHashType, TxidHashType, PubKeyHashType, PubKeyType> newTxBlock)
        {
            txBlock = newTxBlock;

            are.Set();
        }
    }

    //2014/08/18
    //何とかしてデータ操作部分を分離できないか？
    //データ操作の汎用的な仕組みは作れないか？
    public class BlockChain<BlockidHashType, TxidHashType, PubKeyHashType, PubKeyType> : SHAREDDATA
        where BlockidHashType : HASHBASE
        where TxidHashType : HASHBASE
        where PubKeyHashType : HASHBASE
        where PubKeyType : DSAPUBKEYBASE
    {
        public BlockChain(BlockChainDatabase _bcDatabase, BlockNodesGroupDatabase _bngDatabase, BlockGroupDatabase _bgDatabase, UtxoDatabase _utxoDatabase, AddressEventDatabase _addressEventDatabase)
        {
            bcDatabase = _bcDatabase;
            bngDatabase = _bngDatabase;
            bgDatabase = _bgDatabase;
            utxoDatabase = _utxoDatabase;
            addressEventDatabase = _addressEventDatabase;
        }

        public void Initialize()
        {
            byte[] bcDataBytes = bcDatabase.GetData();
            if (bcDataBytes.Length != 0)
                FromBinary(bcDataBytes);

            blockGroups = new SortedDictionary<long, List<TransactionalBlock<BlockidHashType, TxidHashType, PubKeyHashType, PubKeyType>>>();
            mainBlocks = new SortedDictionary<long, TransactionalBlock<BlockidHashType, TxidHashType, PubKeyHashType, PubKeyType>>();
            isVerifieds = new Dictionary<BlockidHashType, bool>();
            numOfMainBlocksWhenSaveNext = numOfMainBlocksWhenSave;

            currentBngIndex = head / blockNodesGroupDiv;
            currentBng = new BlockNodesGroup(blockNodesGroupDiv);
            if (head == 0)
                currentBng.AddBlockNodes(new BlockNodes());
            else
            {
                byte[] currentBngBytes = bngDatabase.GetBlockNodesGroupData(currentBngIndex);
                if (currentBngBytes.Length != 0)
                    currentBng.FromBinary(currentBngBytes);
            }

            Dictionary<PubKeyHashType, List<Utxo>> utxosDict = new Dictionary<PubKeyHashType, List<Utxo>>();
            byte[] utxosBytes = utxoDatabase.GetData();
            if (utxosBytes.Length != 0)
            {
                int addressLength = (Activator.CreateInstance(typeof(PubKeyHashType)) as PubKeyHashType).SizeByte;
                int utxoLength = new Utxo().LengthOfAll.Value;
                int pointer = 0;

                int length1 = BitConverter.ToInt32(utxosBytes, pointer);

                pointer += 4;

                for (int i = 0; i < length1; i++)
                {
                    byte[] addressBytes = new byte[addressLength];
                    Array.Copy(utxosBytes, pointer, addressBytes, 0, addressLength);

                    pointer += addressLength;

                    PubKeyHashType address = Activator.CreateInstance(typeof(PubKeyHashType)) as PubKeyHashType;
                    address.FromHash(addressBytes);

                    int length2 = BitConverter.ToInt32(utxosBytes, pointer);

                    pointer += 4;

                    List<Utxo> list = new List<Utxo>();
                    for (int j = 0; j < length2; j++)
                    {
                        byte[] utxoBytes = new byte[utxoLength];
                        Array.Copy(utxosBytes, pointer, utxoBytes, 0, utxoLength);

                        pointer += utxoLength;

                        list.Add(SHAREDDATA.FromBinary<Utxo>(utxoBytes));
                    }

                    utxosDict.Add(address, list);
                }
            }

            utxos = new Utxos<PubKeyHashType>(utxosDict);

            addedUtxosInMemory = new List<Dictionary<PubKeyHashType, List<Utxo>>>();
            removedUtxosInMemory = new List<Dictionary<PubKeyHashType, List<Utxo>>>();

            Dictionary<PubKeyHashType, List<AddressEventData>> addressEventDataDict = new Dictionary<PubKeyHashType, List<AddressEventData>>();
            byte[] addressEventDatasBytes = addressEventDatabase.GetData();
            if (addressEventDatasBytes.Length != 0)
            {
                int addressLength = (Activator.CreateInstance(typeof(PubKeyHashType)) as PubKeyHashType).SizeByte;
                int addressEventDataLength = new AddressEventData().LengthOfAll.Value;
                int pointer = 0;

                int length1 = BitConverter.ToInt32(addressEventDatasBytes, pointer);

                pointer += 4;

                for (int i = 0; i < length1; i++)
                {
                    byte[] addressBytes = new byte[addressLength];
                    Array.Copy(addressEventDatasBytes, pointer, addressBytes, 0, addressLength);

                    pointer += addressLength;

                    PubKeyHashType address = Activator.CreateInstance(typeof(PubKeyHashType)) as PubKeyHashType;
                    address.FromHash(addressBytes);

                    int length2 = BitConverter.ToInt32(addressEventDatasBytes, pointer);

                    pointer += 4;

                    List<AddressEventData> list = new List<AddressEventData>();
                    for (int j = 0; j < length2; j++)
                    {
                        byte[] addressEventDataBytes = new byte[addressEventDataLength];
                        Array.Copy(addressEventDatasBytes, pointer, addressEventDataBytes, 0, addressEventDataLength);

                        pointer += addressEventDataLength;

                        list.Add(SHAREDDATA.FromBinary<AddressEventData>(addressEventDataBytes));
                    }

                    addressEventDataDict.Add(address, list);
                }
            }

            addressEventDatas = new AddressEventDatas<PubKeyHashType>(addressEventDataDict);
            addressEvents = new Dictionary<AddressEvent<PubKeyHashType>, Tuple<CurrencyUnit, CurrencyUnit>>();

            Initialized += (sender, e) =>
            {
                for (long i = utxoDividedHead == 0 ? 1 : utxoDividedHead * utxosInMemoryDiv; i < head + 1; i++)
                    GoForwardUtxosCurrent(GetMainBlock(i));
            };

            isInitialized = true;

            Initialized(this, EventArgs.Empty);
        }

        private static readonly long blockGroupDiv = 100000;
        private static readonly long blockNodesGroupDiv = 10000;
        //Utxoは100ブロック毎に記録する
        private static readonly long utxosInMemoryDiv = 100;

        //初期化されていない場合において初期化以外のことを実行しようとすると例外が発生する
        private bool isInitialized = false;

        public event EventHandler Initialized = delegate { };

        public event EventHandler BalanceUpdated = delegate { };

        //未保存のブロックの集まり
        //保存したら削除しなければならない
        //鍵：bIndex（ブロックの高さ）
        private SortedDictionary<long, List<TransactionalBlock<BlockidHashType, TxidHashType, PubKeyHashType, PubKeyType>>> blockGroups;
        //未保存の主ブロックの集まり
        //保存したら削除しなければならない
        //鍵：bIndex（ブロックの高さ）
        private SortedDictionary<long, TransactionalBlock<BlockidHashType, TxidHashType, PubKeyHashType, PubKeyType>> mainBlocks;
        //検証したら結果を格納する
        //<未実装>保存したら（或いは、参照される可能性が低くなったら）削除しなければならない
        private Dictionary<BlockidHashType, bool> isVerifieds;

        private long cacheBgIndex;
        //bgPosition1（ブロック群のファイル上の位置）
        //bgPosition2（ブロック群の何番目のブロックか）
        private long cacheBgPosition1;
        //性能向上のためのブロック群の一時的な保持
        //必要なものだけ逆直列化する
        private byte[][] cacheBgBytes;
        private TransactionalBlock<BlockidHashType, TxidHashType, PubKeyHashType, PubKeyType>[] cacheBg;

        //更新された主ブロックがこれ以上溜まると1度保存が試行される
        private int numOfMainBlocksWhenSaveNext;

        //bngIndex（ブロック節群の番号） = bIndex / blockNodesGroupDiv
        //保存されているブロック節群の中で最新のもの
        private long currentBngIndex;
        private BlockNodesGroup currentBng;
        //性能向上のためのブロック節群の一時的な保持
        private long cacheBngIndex;
        private BlockNodesGroup cacheBng;

        private Utxos<PubKeyHashType> utxos;
        private List<Dictionary<PubKeyHashType, List<Utxo>>> addedUtxosInMemory;
        private List<Dictionary<PubKeyHashType, List<Utxo>>> removedUtxosInMemory;
        private Dictionary<PubKeyHashType, List<Utxo>> currentAddedUtxos;
        private Dictionary<PubKeyHashType, List<Utxo>> currentRemovedUtxos;

        private AddressEventDatas<PubKeyHashType> addressEventDatas;
        private Dictionary<AddressEvent<PubKeyHashType>, Tuple<CurrencyUnit, CurrencyUnit>> addressEvents;

        private BlockChainDatabase bcDatabase;
        private BlockNodesGroupDatabase bngDatabase;
        private BlockGroupDatabase bgDatabase;
        private UtxoDatabase utxoDatabase;
        private AddressEventDatabase addressEventDatabase;

        private GenesisBlock<BlockidHashType> genesisBlock = new GenesisBlock<BlockidHashType>();

        //2014/07/08
        //許容されるブロック鎖分岐は最大でも200ブロック（それより長い分岐は拒否される）
        //但し、ブロック鎖分岐によってブロック鎖の先頭のブロック番号が後退した場合において、
        //更にブロック鎖分岐によってブロック鎖の先頭のブロック番号が後退することも、
        //現実的には先ず起こらないだろうが、理論的にはあり得る

        //古過ぎるか、新し過ぎるブロックは無条件に拒否
        public static readonly long rejectedBIndexDif = 100;

        //現在の主ブロック鎖と分岐ブロック鎖の先頭の内、何れか前のものから
        //更にこれより前に分岐点がある場合には、たとえ分岐ブロック鎖の方が難易度的に長くても
        //主ブロック鎖が切り替わることはない
        public static readonly int maxBrunchDeep = 100;
        //更新された主ブロックがこれ以上溜まると1度保存が試行される
        public static readonly int numOfMainBlocksWhenSave = 200;
        //更新された主ブロックが↑の数以上溜まっている状態で更にこれ以上増えると1度保存が試行される
        public static readonly int numOfMainBlocksWhenSaveDifference = 50;
        //更新された主ブロックがこれ以上溜まっているブロック群以前のブロック群は保存が実行される
        public static readonly int numOfNewMainBlocksInGroupWhenSave = 150;
        //最大でもこれ以下のブロックしか1つのブロック群には保存されない
        public static readonly int blockGroupCapacity = 100;
        //保存が実行されると、現在の主ブロック鎖の先頭ブロックのブロック番号から離れているブロックは破棄される
        public static readonly long discardOldBlock = 200;

        public static readonly int maxUtxosGroup = 2;

        public static readonly long unusableConformation = 6;

        public enum TransactionType { normal, foundational }

        public long head { get; private set; }
        //1から。0は何も保存されていない状態を表す
        public long utxoDividedHead { get; private set; }

        protected override Func<ReaderWriter, IEnumerable<MainDataInfomation>> StreamInfo
        {
            get
            {
                return (msrw) => new MainDataInfomation[]{
                    new MainDataInfomation(typeof(long), () => head, (o) => head = (long)o),
                    new MainDataInfomation(typeof(long), () => utxoDividedHead, (o) => utxoDividedHead = (long)o),
                };
            }
        }

        private TransactionalBlock<BlockidHashType, TxidHashType, PubKeyHashType, PubKeyType> GetSavedOrCachedBlock(long bgIndex, long bgPosition1, long bgPosition2)
        {
            Func<TransactionalBlock<BlockidHashType, TxidHashType, PubKeyHashType, PubKeyType>> _GetBlockFromCache = () =>
            {
                byte[] txBlockTypeBytes = new byte[4];
                byte[] txBlockBytes = new byte[cacheBgBytes[bgPosition2].Length - 4];

                Array.Copy(cacheBgBytes[bgPosition2], 0, txBlockTypeBytes, 0, txBlockTypeBytes.Length);
                Array.Copy(cacheBgBytes[bgPosition2], 4, txBlockBytes, 0, txBlockBytes.Length);

                TransactionType txType = (TransactionType)BitConverter.ToInt32(txBlockTypeBytes, 0);

                TransactionalBlock<BlockidHashType, TxidHashType, PubKeyHashType, PubKeyType> txBlock;
                if (txType == TransactionType.normal)
                    txBlock = new NormalBlock<BlockidHashType, TxidHashType, PubKeyHashType, PubKeyType>();
                else if (txType == TransactionType.foundational)
                    txBlock = new FoundationalBlock<BlockidHashType, TxidHashType, PubKeyHashType, PubKeyType>();
                else
                    throw new NotSupportedException("not_supported_tx_block_type");

                txBlock.FromBinary(txBlockBytes);

                return cacheBg[bgPosition2] = txBlock;
            };

            if (cacheBgIndex == bgIndex && cacheBgPosition1 == bgPosition1 && cacheBgBytes != null)
                if (cacheBg != null && cacheBg[bgPosition2] != null)
                    return cacheBg[bgPosition2];
                else
                    return _GetBlockFromCache();
            else
            {
                using (MemoryStream ms = new MemoryStream(bgDatabase.GetBlockGroupData(bgIndex, (long)bgPosition1)))
                {
                    byte[] bgLengthBytes = new byte[4];
                    ms.Read(bgLengthBytes, 0, 4);
                    int bgLength = BitConverter.ToInt32(bgLengthBytes, 0);

                    byte[][] bgDatasBytes = new byte[bgLength][];
                    for (int i = 0; i < bgLength; i++)
                    {
                        byte[] bLengthBytes = new byte[4];
                        ms.Read(bLengthBytes, 0, 4);
                        int bLength = BitConverter.ToInt32(bLengthBytes, 0);

                        bgDatasBytes[i] = new byte[bLength];
                        ms.Read(bgDatasBytes[i], 0, bLength);
                    }

                    cacheBgBytes = bgDatasBytes;
                    cacheBg = new TransactionalBlock<BlockidHashType, TxidHashType, PubKeyHashType, PubKeyType>[cacheBgBytes.Length];
                    cacheBgIndex = bgIndex;
                    cacheBgPosition1 = bgPosition1;
                }

                return _GetBlockFromCache();
            }
        }

        private long SaveBlockGroup(long bgIndex, byte[][] bgDataBytes)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                byte[] bgLengthBytes = BitConverter.GetBytes(bgDataBytes.Length);
                ms.Write(bgLengthBytes, 0, 4);

                for (int i = 0; i < bgDataBytes.Length; i++)
                {
                    byte[] bLengthBytes = BitConverter.GetBytes(bgDataBytes[i].Length);
                    ms.Write(bLengthBytes, 0, 4);

                    ms.Write(bgDataBytes[i], 0, bgDataBytes[i].Length);
                }

                return bgDatabase.AddBlockGroupData(ms.ToArray(), bgIndex);
            }
        }

        private void LoadCacheBng(long bngIndex)
        {
            cacheBngIndex = bngIndex;
            cacheBng = new BlockNodesGroup(blockGroupDiv);
            byte[] cacheBngBytes = bngDatabase.GetBlockNodesGroupData(bngIndex);
            if (cacheBngBytes.Length != 0)
                cacheBng.FromBinary(cacheBngBytes);
        }

        private TransactionalBlock<BlockidHashType, TxidHashType, PubKeyHashType, PubKeyType>[] GetBlocksAtBIndex(long bIndex)
        {
            List<TransactionalBlock<BlockidHashType, TxidHashType, PubKeyHashType, PubKeyType>> blocks = new List<TransactionalBlock<BlockidHashType, TxidHashType, PubKeyHashType, PubKeyType>>();

            if (blockGroups.ContainsKey(bIndex))
                foreach (var block in blockGroups[bIndex])
                    blocks.Add(block);

            long bgIndex = bIndex / blockGroupDiv;
            long bngIndex = bIndex / blockNodesGroupDiv;
            long bngPosition = bIndex % blockNodesGroupDiv;

            if (currentBngIndex == bngIndex)
            {
                //<未改良>position1が同一のブロック取得を纏める（連続させる）
                if (currentBng.nodess[bngPosition] != null)
                    foreach (var blockNode in currentBng.nodess[bngPosition].nodes)
                        blocks.Add(GetSavedOrCachedBlock(bgIndex, blockNode.position1, blockNode.position2));
            }
            else
            {
                if (cacheBngIndex != bngIndex || cacheBng == null)
                    LoadCacheBng(bngIndex);

                //<未改良>position1が同一のブロック取得を纏める（連続させる）
                if (cacheBng.nodess[bngPosition] != null)
                    foreach (var blockNode in cacheBng.nodess[bngPosition].nodes)
                        blocks.Add(GetSavedOrCachedBlock(bgIndex, blockNode.position1, blockNode.position2));
            }

            return blocks.ToArray();
        }

        private TransactionalBlock<BlockidHashType, TxidHashType, PubKeyHashType, PubKeyType> GetMainBlockAtBIndex(long bIndex)
        {
            if (mainBlocks.ContainsKey(bIndex))
                return mainBlocks[bIndex];

            long bgIndex = bIndex / blockGroupDiv;
            long bngIndex = bIndex / blockNodesGroupDiv;
            long bngPosition = bIndex % blockNodesGroupDiv;

            if (currentBngIndex == bngIndex)
            {
                if (currentBng.nodess[bngPosition] != null)
                    foreach (var blockNode in currentBng.nodess[bngPosition].nodes)
                        if (blockNode.isMain)
                            return GetSavedOrCachedBlock(bgIndex, blockNode.position1, blockNode.position2);
            }
            else
            {
                if (cacheBngIndex != bngIndex || cacheBng == null)
                    LoadCacheBng(bngIndex);

                if (cacheBng.nodess[bngPosition] != null)
                    foreach (var blockNode in cacheBng.nodess[bngPosition].nodes)
                        if (blockNode.isMain)
                            return GetSavedOrCachedBlock(bgIndex, blockNode.position1, blockNode.position2);
            }

            return null;
        }

        public TransactionalBlock<BlockidHashType, TxidHashType, PubKeyHashType, PubKeyType>[] GetHeadBlocks()
        {
            if (!isInitialized)
                throw new InvalidOperationException("not_initialized");
            if (head == 0)
                throw new ArgumentOutOfRangeException("blk_genesis_bindex");

            return GetBlocksAtBIndex(head);
        }

        public TransactionalBlock<BlockidHashType, TxidHashType, PubKeyHashType, PubKeyType> GetHeadMainBlock()
        {
            if (!isInitialized)
                throw new InvalidOperationException("not_initialized");
            if (head == 0)
                throw new ArgumentOutOfRangeException("blk_genesis_bindex");

            return GetMainBlockAtBIndex(head);
        }

        public TransactionalBlock<BlockidHashType, TxidHashType, PubKeyHashType, PubKeyType>[] GetBlocks(long bIndex)
        {
            if (!isInitialized)
                throw new InvalidOperationException("not_initialized");
            if (bIndex == 0)
                throw new ArgumentOutOfRangeException("blk_genesis_bindex");

            return GetBlocksAtBIndex(bIndex);
        }

        public TransactionalBlock<BlockidHashType, TxidHashType, PubKeyHashType, PubKeyType> GetMainBlock(long bIndex)
        {
            if (!isInitialized)
                throw new InvalidOperationException("not_initialized");
            if (bIndex == 0)
                throw new ArgumentOutOfRangeException("blk_genesis_bindex");
            if (bIndex > head)
                throw new ArgumentException("blk_bindex");

            return GetMainBlockAtBIndex(bIndex);
        }

        public void AddBlock(TransactionalBlock<BlockidHashType, TxidHashType, PubKeyHashType, PubKeyType> txBlock)
        {
            if (!isInitialized)
                throw new InvalidOperationException("not_initialized");

            if (txBlock.header.index < head - rejectedBIndexDif)
            {
                this.RaiseError("blk_too_old", 3);
                return;
            }
            if (txBlock.header.index > head + rejectedBIndexDif)
            {
                this.RaiseError("blk_too_new", 3);
                return;
            }

            foreach (var block in GetBlocks(txBlock.header.index))
                if (block.Id.Equals(txBlock.Id))
                {
                    this.RaiseWarning("blk_already_existed", 3);
                    return;
                }

            if (txBlock.header.index == 1 && !genesisBlock.Id.Equals(txBlock.header.prevBlockHash))
            {
                this.RaiseError("blk_mismatch_genesis_block_hash", 3);
                return;
            }

            List<TransactionalBlock<BlockidHashType, TxidHashType, PubKeyHashType, PubKeyType>> list;
            if (blockGroups.ContainsKey(txBlock.header.index))
                list = blockGroups[txBlock.header.index];
            else
                blockGroups.Add(txBlock.header.index, list = new List<TransactionalBlock<BlockidHashType, TxidHashType, PubKeyHashType, PubKeyType>>());

            list.Add(txBlock);

            //<未改良>若干無駄がある
            TransactionalBlock<BlockidHashType, TxidHashType, PubKeyHashType, PubKeyType> target = txBlock;
            TransactionalBlock<BlockidHashType, TxidHashType, PubKeyHashType, PubKeyType> main = null;
            while (true)
            {
                //<未改良>連続した番号のブロックを一気に取得する
                TransactionalBlock<BlockidHashType, TxidHashType, PubKeyHashType, PubKeyType> next = GetBlocks(target.header.index + 1).Where((e) => e.header.prevBlockHash.Equals(target.Id)).FirstOrDefault();
                if (next != null)
                    target = next;
                else
                    break;
            }

            double branchCumulativeDifficulty = 0.0;
            double mainCumulativeDifficulty = 0.0;
            Stack<TransactionalBlock<BlockidHashType, TxidHashType, PubKeyHashType, PubKeyType>> stack = new Stack<TransactionalBlock<BlockidHashType, TxidHashType, PubKeyHashType, PubKeyType>>();
            Dictionary<PubKeyHashType, List<Utxo>> addedBranchUtxos = new Dictionary<PubKeyHashType, List<Utxo>>();
            Dictionary<PubKeyHashType, List<Utxo>> removedBranchUtxos = new Dictionary<PubKeyHashType, List<Utxo>>();
            if (target.header.index > head)
            {
                while (target.header.index > head)
                {
                    branchCumulativeDifficulty += target.header.difficulty.Diff;
                    stack.Push(target);

                    if (target.header.index == 1)
                        break;

                    //<未改良>連続した番号のブロックを一気に取得する
                    TransactionalBlock<BlockidHashType, TxidHashType, PubKeyHashType, PubKeyType> prev = GetBlocks(target.header.index - 1).Where((e) => e.Id.Equals(target.header.prevBlockHash)).FirstOrDefault();
                    if (prev == null)
                    {
                        this.RaiseWarning("blk_not_connected", 3);
                        return;
                    }
                    else
                        target = prev;
                }

                if (head == 0 || target.Id.Equals((main = GetHeadMainBlock()).Id))
                {
                    Dictionary<AddressEvent<PubKeyHashType>, bool> balanceUpdatedFlag1 = new Dictionary<AddressEvent<PubKeyHashType>, bool>();
                    Dictionary<AddressEvent<PubKeyHashType>, long?> balanceUpdatedBefore = new Dictionary<AddressEvent<PubKeyHashType>, long?>();

                    UpdateBalanceBefore(balanceUpdatedFlag1, balanceUpdatedBefore);

                    foreach (var newBlock in stack)
                    {
                        bool isValid;
                        if (isVerifieds.ContainsKey(newBlock.Id)) //常に偽が返るはず
                            isValid = isVerifieds[newBlock.Id];
                        else
                            isVerifieds.Add(newBlock.Id, isValid = VerifyBlock(newBlock, addedUtxosInMemory.Concat(new List<Dictionary<PubKeyHashType, List<Utxo>>>() { currentAddedUtxos }), removedUtxosInMemory.Concat(new List<Dictionary<PubKeyHashType, List<Utxo>>>() { currentRemovedUtxos })));

                        if (!isValid)
                            break;

                        head++;

                        if (mainBlocks.ContainsKey(head)) //常に偽が返るはず
                            mainBlocks[head] = newBlock;
                        else
                            mainBlocks.Add(head, newBlock);

                        GoForwardUtxosCurrent(newBlock);
                        GoForwardAddressEventdata(newBlock);
                    }

                    main = null;

                    UpdateBalanceAfter(balanceUpdatedFlag1, balanceUpdatedBefore);
                }
            }
            else
            {
                main = GetHeadMainBlock();

                if (target.header.index < head)
                    while (target.header.index < main.header.index)
                    {
                        mainCumulativeDifficulty += main.header.difficulty.Diff;

                        //<未改良>連続した番号のブロックを一気に取得する
                        TransactionalBlock<BlockidHashType, TxidHashType, PubKeyHashType, PubKeyType> prev = GetMainBlock(main.header.index - 1);
                        if (prev == null)
                        {
                            this.RaiseError("blk_main_not_connected", 3);
                            return;
                        }
                        else
                        {
                            GoBackwardUtxosInMemory(main, addedBranchUtxos, removedBranchUtxos);

                            main = prev;
                        }
                    }
            }

            if (main != null)
            {
                //この時点でtargetとmainは同じ高さの異なるブロックを参照しているはず
                for (int i = 0; !target.Id.Equals(main.Id); i++)
                {
                    if (i >= maxBrunchDeep)
                    {
                        this.RaiseWarning("blk_too_deep", 3);
                        return;
                    }

                    branchCumulativeDifficulty += target.header.difficulty.Diff;
                    mainCumulativeDifficulty += main.header.difficulty.Diff;

                    stack.Push(target);

                    if (target.header.index == 1)
                        break;

                    //<未改良>連続した番号のブロックを一気に取得する
                    TransactionalBlock<BlockidHashType, TxidHashType, PubKeyHashType, PubKeyType> prev = GetBlocks(target.header.index - 1).Where((e) => e.Id.Equals(target.header.prevBlockHash)).FirstOrDefault();
                    if (prev == null)
                    {
                        this.RaiseWarning("blk_not_connected", 3);
                        return;
                    }
                    else
                        target = prev;

                    //<未改良>連続した番号のブロックを一気に取得する
                    prev = GetMainBlock(main.header.index - 1);
                    if (prev == null)
                    {
                        this.RaiseError("blk_main_not_connected", 3);
                        return;
                    }
                    else
                    {
                        GoBackwardUtxosInMemory(main, addedBranchUtxos, removedBranchUtxos);

                        main = prev;
                    }
                }

                if (branchCumulativeDifficulty > mainCumulativeDifficulty)
                {
                    double cumulativeDifficulty = 0.0;
                    TransactionalBlock<BlockidHashType, TxidHashType, PubKeyHashType, PubKeyType> validHead = null;
                    foreach (var newBlock in stack)
                    {
                        bool isValid;
                        if (isVerifieds.ContainsKey(newBlock.Id))
                            isValid = isVerifieds[newBlock.Id];
                        else
                            isVerifieds.Add(newBlock.Id, isValid = VerifyBlock(newBlock, addedUtxosInMemory.Concat(new List<Dictionary<PubKeyHashType, List<Utxo>>>() { currentAddedUtxos, addedBranchUtxos }), removedUtxosInMemory.Concat(new List<Dictionary<PubKeyHashType, List<Utxo>>>() { currentRemovedUtxos, removedBranchUtxos })));

                        if (!isValid)
                            break;

                        cumulativeDifficulty += newBlock.header.difficulty.Diff;
                        validHead = newBlock;

                        GoForwardUtxosInMemory(newBlock, addedBranchUtxos, removedBranchUtxos);
                    }

                    if (cumulativeDifficulty > mainCumulativeDifficulty)
                    {
                        Dictionary<AddressEvent<PubKeyHashType>, bool> balanceUpdatedFlag1 = new Dictionary<AddressEvent<PubKeyHashType>, bool>();
                        Dictionary<AddressEvent<PubKeyHashType>, long?> balanceUpdatedBefore = new Dictionary<AddressEvent<PubKeyHashType>, long?>();

                        UpdateBalanceBefore(balanceUpdatedFlag1, balanceUpdatedBefore);

                        TransactionalBlock<BlockidHashType, TxidHashType, PubKeyHashType, PubKeyType> fork = GetHeadMainBlock();
                        while (!fork.Id.Equals(main.Id))
                        {
                            GoBackwardUtxosCurrent(fork);
                            GoBackwardAddressEventData(fork);

                            fork = GetMainBlock(fork.header.index - 1);
                        }

                        foreach (var newBlock in stack)
                        {
                            if (mainBlocks.ContainsKey(newBlock.header.index))
                                mainBlocks[newBlock.header.index] = newBlock;
                            else
                                mainBlocks.Add(newBlock.header.index, newBlock);

                            GoForwardUtxosCurrent(newBlock);
                            GoForwardAddressEventdata(newBlock);

                            if (newBlock == validHead)
                                break;
                        }

                        for (long i = validHead.header.index + 1; i < head + 1; i++)
                            if (mainBlocks.ContainsKey(i))
                                mainBlocks.Remove(i);

                        head = validHead.header.index;

                        UpdateBalanceAfter(balanceUpdatedFlag1, balanceUpdatedBefore);
                    }
                }
            }

            if (mainBlocks.Count > numOfMainBlocksWhenSaveNext)
            {
                SortedDictionary<long, List<TransactionalBlock<BlockidHashType, TxidHashType, PubKeyHashType, PubKeyType>>> tobeSavedBlockss = GetToBeSavedBlockss();

                long? last = null;
                foreach (var tobeSavedBlocks in tobeSavedBlockss)
                    if (tobeSavedBlocks.Value.Count >= numOfNewMainBlocksInGroupWhenSave)
                        last = tobeSavedBlocks.Value[blockGroupCapacity - 1].header.index;

                if (last == null)
                {
                    numOfMainBlocksWhenSaveNext += numOfMainBlocksWhenSaveDifference;
                    return;
                }

                SaveBgAndBng(tobeSavedBlockss, last.Value);

                foreach (var mainBlock in mainBlocks)
                    if (mainBlock.Key <= last.Value && blockGroups.ContainsKey(mainBlock.Key) && blockGroups[mainBlock.Key].Contains(mainBlock.Value))
                        blockGroups[mainBlock.Key].Remove(mainBlock.Value);

                SortedDictionary<long, List<TransactionalBlock<BlockidHashType, TxidHashType, PubKeyHashType, PubKeyType>>> newBlockGroups = new SortedDictionary<long, List<TransactionalBlock<BlockidHashType, TxidHashType, PubKeyHashType, PubKeyType>>>();
                foreach (var blockGroup in blockGroups)
                    if (blockGroup.Key >= head - discardOldBlock)
                        newBlockGroups.Add(blockGroup.Key, blockGroup.Value);
                blockGroups = newBlockGroups;

                SortedDictionary<long, TransactionalBlock<BlockidHashType, TxidHashType, PubKeyHashType, PubKeyType>> newMainBlocks = new SortedDictionary<long, TransactionalBlock<BlockidHashType, TxidHashType, PubKeyHashType, PubKeyType>>();
                foreach (var mainBlock in mainBlocks)
                    if (mainBlock.Key > last.Value)
                        newMainBlocks.Add(mainBlock.Key, mainBlock.Value);
                mainBlocks = newMainBlocks;

                numOfMainBlocksWhenSaveNext = numOfMainBlocksWhenSave;
            }
        }

        private void GoForwardAddressEventdata(TransactionalBlock<BlockidHashType, TxidHashType, PubKeyHashType, PubKeyType> txBlock)
        {
            foreach (var txi in txBlock.Transactions.Select((tx, i) => new { tx, i }))
            {
                for (int i = 0; i < txi.tx.Inputs.Length; i++)
                    addressEventDatas.Remove(Activator.CreateInstance(typeof(PubKeyHashType), txi.tx.Inputs[i].senderPubKey) as PubKeyHashType, txi.tx.Inputs[i].prevTxBlockIndex, txi.tx.Inputs[i].prevTxIndex, txi.tx.Inputs[i].prevTxOutputIndex);
                for (int i = 0; i < txi.tx.Outputs.Length; i++)
                    addressEventDatas.Add(txi.tx.Outputs[i].receiverPubKeyHash, new AddressEventData(txBlock.header.index, txi.i, i, txi.tx.Outputs[i].amount));
            }
        }

        private void GoBackwardAddressEventData(TransactionalBlock<BlockidHashType, TxidHashType, PubKeyHashType, PubKeyType> txBlock)
        {
            foreach (var txi in txBlock.Transactions.Select((tx, i) => new { tx, i }))
            {
                for (int i = 0; i < txi.tx.Inputs.Length; i++)
                    addressEventDatas.Add(Activator.CreateInstance(typeof(PubKeyHashType), txi.tx.Inputs[i].senderPubKey) as PubKeyHashType, new AddressEventData(txi.tx.Inputs[i].prevTxBlockIndex, txi.tx.Inputs[i].prevTxIndex, txi.tx.Inputs[i].prevTxOutputIndex, GetMainBlock(txi.tx.Inputs[i].prevTxBlockIndex).Transactions[txi.tx.Inputs[i].prevTxIndex].Outputs[txi.tx.Inputs[i].prevTxOutputIndex].amount));
                for (int i = 0; i < txi.tx.Outputs.Length; i++)
                    addressEventDatas.Remove(txi.tx.Outputs[i].receiverPubKeyHash, txBlock.header.index, txi.i, i);
            }
        }

        public void AddAddressEvent(AddressEvent<PubKeyHashType> addressEvent)
        {
            if (addressEvents.ContainsKey(addressEvent))
                throw new ArgumentException("already_added");

            List<AddressEventData> listAddressEventData = null;
            if (addressEventDatas.ContainsAddress(addressEvent.address))
                listAddressEventData = addressEventDatas.GetAddressEventDatas(addressEvent.address);
            else
            {
                List<Utxo> listUtxo = utxos.ContainsAddress(addressEvent.address) ? utxos.GetAddressUtxos(addressEvent.address) : new List<Utxo>() { };

                foreach (var added in addedUtxosInMemory.Concat(new List<Dictionary<PubKeyHashType, List<Utxo>>>() { currentAddedUtxos }))
                    if (added.ContainsKey(addressEvent.address))
                        foreach (var utxo in added[addressEvent.address])
                            listUtxo.Add(utxo);

                foreach (var removed in removedUtxosInMemory.Concat(new List<Dictionary<PubKeyHashType, List<Utxo>>>() { currentRemovedUtxos }))
                    if (removed.ContainsKey(addressEvent.address))
                        foreach (var utxo in removed[addressEvent.address])
                        {
                            Utxo removedUtxo = listUtxo.FirstOrDefault((elem) => elem.blockIndex == utxo.blockIndex && elem.txIndex == utxo.txIndex && elem.txOutIndex == utxo.txOutIndex);
                            if (removedUtxo == null)
                                throw new InvalidOperationException("not_found");
                            listUtxo.Remove(removedUtxo);
                        }

                listAddressEventData = new List<AddressEventData>();
                foreach (var utxo in listUtxo)
                    listAddressEventData.Add(new AddressEventData(utxo.blockIndex, utxo.txIndex, utxo.txOutIndex, GetMainBlock(utxo.blockIndex).Transactions[utxo.txIndex].Outputs[utxo.txOutIndex].amount));

                addressEventDatas.Add(addressEvent.address, listAddressEventData);
            }

            Tuple<CurrencyUnit, CurrencyUnit> balance = CalculateBalance(listAddressEventData);

            addressEvents.Add(addressEvent, balance);

            addressEvent.RaiseBalanceUpdated(balance);
            addressEvent.RaiseUsableBalanceUpdated(balance.Item1);
            addressEvent.RaiseUnusableBalanceUpdated(balance.Item2);
        }

        public AddressEvent<PubKeyHashType> RemoveAddressEvent(PubKeyHashType address)
        {
            AddressEvent<PubKeyHashType> addressEvent;
            if ((addressEvent = addressEvents.Keys.FirstOrDefault((elem) => elem.address.Equals(address))) == null)
                throw new ArgumentException("not_added");

            if (addressEventDatas.ContainsAddress(address))
                addressEventDatas.Remove(address);

            addressEvents.Remove(addressEvent);

            return addressEvent;
        }

        private Tuple<CurrencyUnit, CurrencyUnit> CalculateBalance(List<AddressEventData> listAddressEventData)
        {
            CurrencyUnit usable = new CurrencyUnit(0);
            CurrencyUnit unusable = new CurrencyUnit(0);

            foreach (var addressEventData in listAddressEventData)
                if (addressEventData.blockIndex + unusableConformation > head)
                    unusable = new CurrencyUnit(unusable.rawAmount + addressEventData.amount.rawAmount);
                else
                    usable = new CurrencyUnit(usable.rawAmount + addressEventData.amount.rawAmount);

            return new Tuple<CurrencyUnit, CurrencyUnit>(usable, unusable);
        }

        private void UpdateBalanceBefore(Dictionary<AddressEvent<PubKeyHashType>, bool> balanceUpdatedFlag1, Dictionary<AddressEvent<PubKeyHashType>, long?> balanceUpdatedBefore)
        {
            foreach (var addressEvent in addressEvents)
            {
                AddressEventData addressEventData = addressEventDatas.GetAddressEventDatas(addressEvent.Key.address).LastOrDefault();
                balanceUpdatedFlag1.Add(addressEvent.Key, addressEventData != null && addressEventData.blockIndex + unusableConformation > head);
                balanceUpdatedBefore.Add(addressEvent.Key, addressEventData == null ? null : (long?)addressEventData.blockIndex);
            }
        }

        private void UpdateBalanceAfter(Dictionary<AddressEvent<PubKeyHashType>, bool> balanceUpdatedFlag1, Dictionary<AddressEvent<PubKeyHashType>, long?> balanceUpdatedBefore)
        {
            bool flag = false;

            List<AddressEvent<PubKeyHashType>> addressEventsCopy = new List<AddressEvent<PubKeyHashType>>(addressEvents.Keys);
            foreach (var addressEvent in addressEventsCopy)
            {
                List<AddressEventData> listAddressEventData = addressEventDatas.GetAddressEventDatas(addressEvent.address);

                AddressEventData addressEventData = listAddressEventData.LastOrDefault();
                if (balanceUpdatedFlag1[addressEvent] || balanceUpdatedBefore[addressEvent] != (addressEventData == null ? null : (long?)addressEventData.blockIndex) || (addressEventData != null && addressEventData.blockIndex + unusableConformation > head))
                {
                    Tuple<CurrencyUnit, CurrencyUnit> balanceBefore = addressEvents[addressEvent];
                    Tuple<CurrencyUnit, CurrencyUnit> balanceAfter = CalculateBalance(listAddressEventData);

                    bool flag1 = balanceBefore.Item1.rawAmount != balanceAfter.Item1.rawAmount;
                    bool flag2 = balanceBefore.Item2.rawAmount != balanceAfter.Item2.rawAmount;

                    flag |= flag1;
                    flag |= flag2;

                    if (flag1 || flag2)
                        addressEvent.RaiseBalanceUpdated(balanceAfter);
                    if (flag1)
                        addressEvent.RaiseUsableBalanceUpdated(balanceAfter.Item1);
                    if (flag2)
                        addressEvent.RaiseUnusableBalanceUpdated(balanceAfter.Item2);

                    if (flag1 || flag2)
                        addressEvents[addressEvent] = balanceAfter;
                }
            }

            if (flag)
                BalanceUpdated(this, EventArgs.Empty);
        }

        private void GoForwardUtxosCurrent(TransactionalBlock<BlockidHashType, TxidHashType, PubKeyHashType, PubKeyType> txBlock)
        {
            if (txBlock.header.index == 1 || txBlock.header.index % utxosInMemoryDiv == 0)
            {
                if (currentAddedUtxos != null)
                {
                    addedUtxosInMemory.Add(currentAddedUtxos);
                    removedUtxosInMemory.Add(currentRemovedUtxos);
                }

                currentAddedUtxos = new Dictionary<PubKeyHashType, List<Utxo>>();
                currentRemovedUtxos = new Dictionary<PubKeyHashType, List<Utxo>>();
            }

            GoForwardUtxosInMemory(txBlock, currentAddedUtxos, currentRemovedUtxos);
        }

        private void GoBackwardUtxosCurrent(TransactionalBlock<BlockidHashType, TxidHashType, PubKeyHashType, PubKeyType> txBlock)
        {
            GoBackwardUtxosInMemory(txBlock, currentAddedUtxos, currentRemovedUtxos);

            if (txBlock.header.index == 1 || txBlock.header.index % utxosInMemoryDiv == 0)
            {
                foreach (var currentAddedUtxo in currentAddedUtxos)
                    if (currentAddedUtxo.Value.Count != 0)
                        throw new InvalidOperationException("current_added_utxos_not_empty");
                foreach (var currentRemovedUtxo in currentRemovedUtxos)
                    if (currentRemovedUtxo.Value.Count != 0)
                        throw new InvalidOperationException("current_removed__utxos_not_empty");

                if (txBlock.header.index == 1)
                {
                    currentAddedUtxos = null;
                    currentRemovedUtxos = null;
                }
                else
                {
                    if (addedUtxosInMemory.Count > 0)
                    {
                        currentAddedUtxos = addedUtxosInMemory[addedUtxosInMemory.Count - 1];
                        currentRemovedUtxos = removedUtxosInMemory[removedUtxosInMemory.Count - 1];

                        addedUtxosInMemory.RemoveAt(addedUtxosInMemory.Count - 1);
                        removedUtxosInMemory.RemoveAt(removedUtxosInMemory.Count - 1);
                    }
                    else
                        //2014/07/20 既に保存されているUTXOは戻せないものとする
                        throw new InvalidOperationException("disallowed_go_backward_further");
                }
            }
        }

        private void UpdateUtxosTemp(Dictionary<PubKeyHashType, List<Utxo>> utxos1, Dictionary<PubKeyHashType, List<Utxo>> utxos2, PubKeyHashType address, long blockIndex, int txIndex, int txOutputIndex)
        {
            if (utxos1.ContainsKey(address))
            {
                List<Utxo> list = utxos1[address];
                Utxo utxo = list.Where((elem) => elem.blockIndex == blockIndex && elem.txIndex == txIndex && elem.txOutIndex == txOutputIndex).FirstOrDefault();

                if (utxo != null)
                {
                    list.Remove(utxo);
                    return;
                }
            }

            if (utxos2.ContainsKey(address))
                utxos2[address].Add(new Utxo(blockIndex, txIndex, txOutputIndex));
            else
                utxos2.Add(address, new List<Utxo>() { new Utxo(blockIndex, txIndex, txOutputIndex) });
        }

        private void GoForwardUtxosInMemory(TransactionalBlock<BlockidHashType, TxidHashType, PubKeyHashType, PubKeyType> txBlock, Dictionary<PubKeyHashType, List<Utxo>> addedUtxos, Dictionary<PubKeyHashType, List<Utxo>> removedUtxos)
        {
            foreach (var txi in txBlock.Transactions.Select((tx, i) => new { tx, i }))
            {
                for (int i = 0; i < txi.tx.Inputs.Length; i++)
                    UpdateUtxosTemp(addedUtxos, removedUtxos, Activator.CreateInstance(typeof(PubKeyHashType), txi.tx.Inputs[i].senderPubKey) as PubKeyHashType, txi.tx.Inputs[i].prevTxBlockIndex, txi.tx.Inputs[i].prevTxIndex, txi.tx.Inputs[i].prevTxOutputIndex);
                for (int i = 0; i < txi.tx.Outputs.Length; i++)
                    UpdateUtxosTemp(removedUtxos, addedUtxos, txi.tx.Outputs[i].receiverPubKeyHash, txBlock.header.index, txi.i, i);
            }
        }

        private void GoBackwardUtxosInMemory(TransactionalBlock<BlockidHashType, TxidHashType, PubKeyHashType, PubKeyType> txBlock, Dictionary<PubKeyHashType, List<Utxo>> addedUtxos, Dictionary<PubKeyHashType, List<Utxo>> removedUtxos)
        {
            foreach (var txi in txBlock.Transactions.Select((tx, i) => new { tx, i }))
            {
                for (int i = 0; i < txi.tx.Inputs.Length; i++)
                    UpdateUtxosTemp(removedUtxos, addedUtxos, Activator.CreateInstance(typeof(PubKeyHashType), txi.tx.Inputs[i].senderPubKey) as PubKeyHashType, txi.tx.Inputs[i].prevTxBlockIndex, txi.tx.Inputs[i].prevTxIndex, txi.tx.Inputs[i].prevTxOutputIndex);
                for (int i = 0; i < txi.tx.Outputs.Length; i++)
                    UpdateUtxosTemp(addedUtxos, removedUtxos, txi.tx.Outputs[i].receiverPubKeyHash, txBlock.header.index, txi.i, i);
            }
        }

        //終了時にしか呼ばない
        public void SaveWhenExit()
        {
            if (!isInitialized)
                throw new InvalidOperationException("not_initialized");

            SaveBgAndBng(GetToBeSavedBlockss(), long.MaxValue);

            while (addedUtxosInMemory.Count > maxUtxosGroup)
            {
                utxos.Update(addedUtxosInMemory[0], removedUtxosInMemory[0]);

                utxoDividedHead++;

                addedUtxosInMemory.RemoveAt(0);
                removedUtxosInMemory.RemoveAt(0);
            }

            using (MemoryStream ms = new MemoryStream())
            {
                ms.Write(BitConverter.GetBytes(addressEventDatas.addressEventDatas.Count), 0, 4);

                foreach (var addressEventDatasDict in addressEventDatas.addressEventDatas)
                {
                    ms.Write(addressEventDatasDict.Key.hash, 0, addressEventDatasDict.Key.SizeByte);
                    ms.Write(BitConverter.GetBytes(addressEventDatasDict.Value.Count), 0, 4);
                    foreach (var addressEventData in addressEventDatasDict.Value)
                        ms.Write(addressEventData.ToBinary(), 0, addressEventData.LengthOfAll.Value);
                }

                addressEventDatabase.UpdateData(ms.ToArray());
            }

            using (MemoryStream ms = new MemoryStream())
            {
                ms.Write(BitConverter.GetBytes(utxos.utxos.Count), 0, 4);

                foreach (var utxosDict in utxos.utxos)
                {
                    ms.Write(utxosDict.Key.hash, 0, utxosDict.Key.SizeByte);
                    ms.Write(BitConverter.GetBytes(utxosDict.Value.Count), 0, 4);
                    foreach (var utxo in utxosDict.Value)
                        ms.Write(utxo.ToBinary(), 0, utxo.LengthOfAll.Value);
                }

                utxoDatabase.UpdateData(ms.ToArray());
            }

            bcDatabase.UpdateData(ToBinary());
        }

        private SortedDictionary<long, List<TransactionalBlock<BlockidHashType, TxidHashType, PubKeyHashType, PubKeyType>>> GetToBeSavedBlockss()
        {
            SortedDictionary<long, List<TransactionalBlock<BlockidHashType, TxidHashType, PubKeyHashType, PubKeyType>>> tobeSavedBlockss = new SortedDictionary<long, List<TransactionalBlock<BlockidHashType, TxidHashType, PubKeyHashType, PubKeyType>>>();

            foreach (var mainBlock in mainBlocks)
            {
                if (blockGroups.ContainsKey(mainBlock.Key) && blockGroups[mainBlock.Key].Contains(mainBlock.Value))
                {
                    long bgIndex = mainBlock.Value.header.index / blockGroupDiv;

                    if (tobeSavedBlockss.ContainsKey(bgIndex))
                        tobeSavedBlockss[bgIndex].Add(mainBlock.Value);
                    else
                        tobeSavedBlockss.Add(bgIndex, new List<TransactionalBlock<BlockidHashType, TxidHashType, PubKeyHashType, PubKeyType>>() { mainBlock.Value });
                }
            }

            return tobeSavedBlockss;
        }

        private void SaveBgAndBng(SortedDictionary<long, List<TransactionalBlock<BlockidHashType, TxidHashType, PubKeyHashType, PubKeyType>>> tobeSavedBlockss, long last)
        {
            SortedDictionary<long, BlockNode> newBlockNodes = new SortedDictionary<long, BlockNode>();
            long lastBgIndex = last / blockGroupDiv;
            foreach (var tobeSavedBlocks in tobeSavedBlockss)
            {
                if (tobeSavedBlocks.Key > lastBgIndex)
                    break;

                List<byte[]> bgDatas = new List<byte[]>();
                foreach (var tobeSavedBlock in tobeSavedBlocks.Value)
                {
                    if (tobeSavedBlock.header.index > last)
                        break;

                    bgDatas.Add(BitConverter.GetBytes((int)(tobeSavedBlock is NormalBlock<BlockidHashType, TxidHashType, PubKeyHashType, PubKeyType> ? TransactionType.normal : TransactionType.foundational)).Combine(tobeSavedBlock.ToBinary()));
                }

                long bgPosition1 = SaveBlockGroup(tobeSavedBlocks.Key, bgDatas.ToArray());

                long bgPosition2 = 0;
                foreach (var tobeSavedBlock in tobeSavedBlocks.Value)
                {
                    if (tobeSavedBlock.header.index > last)
                        break;

                    newBlockNodes.Add(tobeSavedBlock.header.index, new BlockNode(0, 0, bgPosition1, bgPosition2++, true));
                }
            }

            foreach (var mainBlock in mainBlocks)
            {
                if (mainBlock.Value.header.index > last)
                    break;

                long bgIndex = mainBlock.Value.header.index / blockGroupDiv;
                long bngIndex = mainBlock.Value.header.index / blockNodesGroupDiv;
                long bngPosition = mainBlock.Value.header.index % blockNodesGroupDiv;

                if (currentBngIndex != bngIndex)
                {
                    bngDatabase.UpdateBlockNodesGroupData(currentBng.ToBinary(), bngIndex);

                    currentBngIndex = bngIndex;
                    currentBng = new BlockNodesGroup(blockGroupDiv);
                    byte[] currentBngBytes = bngDatabase.GetBlockNodesGroupData(bngIndex);
                    if (currentBngBytes.Length != 0)
                        currentBng.FromBinary(currentBngBytes);
                }

                if (blockGroups.ContainsKey(mainBlock.Key) && blockGroups[mainBlock.Key].Contains(mainBlock.Value))
                {
                    if (currentBng.nodess[bngPosition] == null)
                    {
                        if (currentBng.position != bngPosition)
                            throw new InvalidOleVariantTypeException("bng_position_mismatch");

                        currentBng.AddBlockNodes(new BlockNodes(new BlockNode[] { newBlockNodes[mainBlock.Key] }));
                    }
                    else
                        currentBng.nodess[bngPosition].AddBlockNode(newBlockNodes[mainBlock.Key]);
                }
                else
                {
                    if (currentBng.nodess[bngPosition] == null)
                        throw new InvalidOperationException("bng_null");

                    bool flag = false;
                    foreach (var blockNode in currentBng.nodess[bngPosition].nodes)
                    {
                        TransactionalBlock<BlockidHashType, TxidHashType, PubKeyHashType, PubKeyType> block = GetSavedOrCachedBlock(bgIndex, blockNode.position1, blockNode.position2);
                        if (block.Id.Equals(mainBlock.Value.Id))
                            blockNode.isMain = flag = true;
                        else
                            blockNode.isMain = false;
                    }

                    if (!flag)
                        throw new InvalidOperationException("block_not_found");
                }
            }

            bngDatabase.UpdateBlockNodesGroupData(currentBng.ToBinary(), currentBngIndex);
        }

        public bool VerifyBlock(TransactionalBlock<BlockidHashType, TxidHashType, PubKeyHashType, PubKeyType> txBlock, IEnumerable<Dictionary<PubKeyHashType, List<Utxo>>> addeds, IEnumerable<Dictionary<PubKeyHashType, List<Utxo>>> removeds)
        {
            if (!isInitialized)
                throw new InvalidOperationException("not_initialized");

            if (!txBlock.IsValid)
                return false;

            TransactionOutput<PubKeyHashType>[][] prevTxOutputs = new TransactionOutput<PubKeyHashType>[txBlock.transferTxs.Length][];
            foreach (var transrferTx in txBlock.transferTxs.Select((v, i) => new { v, i }))
            {
                prevTxOutputs[transrferTx.i] = new TransactionOutput<PubKeyHashType>[transrferTx.v.Inputs.Length];
                foreach (var txInput in transrferTx.v.Inputs.Select((v, i) => new { v, i }))
                {
                    PubKeyHashType address = Activator.CreateInstance(typeof(PubKeyHashType), txInput.v.senderPubKey.pubKey) as PubKeyHashType;
                    if (utxos.Contains(address, txInput.v.prevTxBlockIndex, txInput.v.prevTxIndex, txInput.v.prevTxOutputIndex))
                    {
                        foreach (var removed in removeds)
                            if (removed.ContainsKey(address))
                                foreach (var removedUtxo in removed[address])
                                    if (removedUtxo.blockIndex == txInput.v.prevTxBlockIndex && removedUtxo.txIndex == txInput.v.prevTxIndex && removedUtxo.txOutIndex == txInput.v.prevTxOutputIndex)
                                        return false;
                        prevTxOutputs[transrferTx.i][txInput.i] = GetMainBlock(txInput.v.prevTxBlockIndex).Transactions[txInput.v.prevTxIndex].Outputs[txInput.v.prevTxOutputIndex];
                    }
                    else
                    {
                        foreach (var added in addeds)
                            if (added.ContainsKey(address))
                                foreach (var addedUtxo in added[address])
                                    if (addedUtxo.blockIndex == txInput.v.prevTxBlockIndex && addedUtxo.txIndex == txInput.v.prevTxIndex && addedUtxo.txOutIndex == txInput.v.prevTxOutputIndex)
                                        prevTxOutputs[transrferTx.i][txInput.i] = GetMainBlock(txInput.v.prevTxBlockIndex).Transactions[txInput.v.prevTxIndex].Outputs[txInput.v.prevTxOutputIndex];
                        if (prevTxOutputs[transrferTx.i][txInput.i] == null)
                            return false;
                        foreach (var removed in removeds)
                            if (removed.ContainsKey(address))
                                foreach (var removedUtxo in removed[address])
                                    if (removedUtxo.blockIndex == txInput.v.prevTxBlockIndex && removedUtxo.txIndex == txInput.v.prevTxIndex && removedUtxo.txOutIndex == txInput.v.prevTxOutputIndex)
                                        return false;
                    }
                }
            }

            txBlock.VerifyAll(prevTxOutputs, (index) => GetMainBlock(index));

            return true;
        }
    }

    public class AddressEventData : SHAREDDATA
    {
        public AddressEventData() { }

        public AddressEventData(long _blockIndex, int _txIndex, int _txOutIndex, CurrencyUnit _amount)
        {
            blockIndex = _blockIndex;
            txIndex = _txIndex;
            txOutIndex = _txOutIndex;
            amount = _amount;
        }

        public long blockIndex { get; private set; }
        public int txIndex { get; private set; }
        public int txOutIndex { get; private set; }
        public CurrencyUnit amount { get; private set; }

        protected override Func<ReaderWriter, IEnumerable<MainDataInfomation>> StreamInfo
        {
            get
            {
                return (msrw) => new MainDataInfomation[]{
                    new MainDataInfomation(typeof(long), () => blockIndex, (o) => blockIndex = (long)o),
                    new MainDataInfomation(typeof(int), () => txIndex, (o) => txIndex = (int)o),
                    new MainDataInfomation(typeof(int), () => txOutIndex, (o) => txOutIndex = (int)o),
                    new MainDataInfomation(typeof(long), () => amount.rawAmount, (o) => amount = new CurrencyUnit((long)o)),
                };
            }
        }
    }

    public class AddressEventDatas<PubKeyHashType>
    {
        public AddressEventDatas() { }

        public AddressEventDatas(Dictionary<PubKeyHashType, List<AddressEventData>> _addressEventData) { addressEventDatas = _addressEventData; }

        public Dictionary<PubKeyHashType, List<AddressEventData>> addressEventDatas { get; private set; }

        public void Add(PubKeyHashType address, AddressEventData addressEventData)
        {
            List<AddressEventData> list = null;
            if (addressEventDatas.Keys.Contains(address))
                list = addressEventDatas[address];
            else
                addressEventDatas.Add(address, list = new List<AddressEventData>());

            if (list.FirstOrDefault((elem) => elem.blockIndex == addressEventData.blockIndex && elem.txIndex == addressEventData.txIndex && elem.txOutIndex == addressEventData.txOutIndex) != null)
                throw new InvalidOperationException("already_existed");

            list.Add(addressEventData);
        }

        public void Add(PubKeyHashType address, List<AddressEventData> list)
        {
            if (addressEventDatas.Keys.Contains(address))
                throw new InvalidOperationException("already_existed");

            addressEventDatas.Add(address, list);
        }

        public void Remove(PubKeyHashType address, long blockIndex, int txIndex, int txOutIndex)
        {
            if (!addressEventDatas.Keys.Contains(address))
                throw new InvalidOperationException("not_existed");

            List<AddressEventData> list = addressEventDatas[address];

            AddressEventData utxo = null;
            if ((utxo = list.FirstOrDefault((elem) => elem.blockIndex == blockIndex && elem.txIndex == txIndex && elem.txOutIndex == txOutIndex)) == null)
                throw new InvalidOperationException("not_existed");

            list.Remove(utxo);

            if (list.Count == 0)
                addressEventDatas.Remove(address);
        }

        public void Remove(PubKeyHashType address)
        {
            if (!addressEventDatas.Keys.Contains(address))
                throw new InvalidOperationException("not_existed");

            addressEventDatas.Remove(address);
        }

        public void Update(Dictionary<PubKeyHashType, List<AddressEventData>> addedUtxos, Dictionary<PubKeyHashType, List<AddressEventData>> removedUtxos)
        {
            foreach (var addedUtxos2 in addedUtxos)
                foreach (var addedUtxo in addedUtxos2.Value)
                    Add(addedUtxos2.Key, addedUtxo);
            foreach (var removedUtxos2 in removedUtxos)
                foreach (var removedUtxo in removedUtxos2.Value)
                    Remove(removedUtxos2.Key, removedUtxo.blockIndex, removedUtxo.txIndex, removedUtxo.txOutIndex);
        }

        public bool Contains(PubKeyHashType address, long blockIndex, int txIndex, int txOutIndex)
        {
            if (!addressEventDatas.Keys.Contains(address))
                return false;

            List<AddressEventData> list = addressEventDatas[address];

            return list.FirstOrDefault((elem) => elem.blockIndex == blockIndex && elem.txIndex == txIndex && elem.txOutIndex == txOutIndex) != null;
        }

        public bool ContainsAddress(PubKeyHashType address) { return addressEventDatas.Keys.Contains(address); }

        public List<AddressEventData> GetAddressEventDatas(PubKeyHashType address)
        {
            if (!addressEventDatas.Keys.Contains(address))
                throw new InvalidOperationException("not_existed");

            return addressEventDatas[address];
        }
    }

    public class Utxo : SHAREDDATA
    {
        public Utxo() { }

        public Utxo(long _blockIndex, int _txIndex, int _txOutIndex)
        {
            blockIndex = _blockIndex;
            txIndex = _txIndex;
            txOutIndex = _txOutIndex;
        }

        public long blockIndex { get; private set; }
        public int txIndex { get; private set; }
        public int txOutIndex { get; private set; }

        protected override Func<ReaderWriter, IEnumerable<MainDataInfomation>> StreamInfo
        {
            get
            {
                return (msrw) => new MainDataInfomation[]{
                    new MainDataInfomation(typeof(long), () => blockIndex, (o) => blockIndex = (long)o),
                    new MainDataInfomation(typeof(int), () => txIndex, (o) => txIndex = (int)o),
                    new MainDataInfomation(typeof(int), () => txOutIndex, (o) => txOutIndex = (int)o),
                };
            }
        }
    }

    public class Utxos<PubKeyHashType>
    {
        public Utxos() { }

        public Utxos(Dictionary<PubKeyHashType, List<Utxo>> _utxos) { utxos = _utxos; }

        public Dictionary<PubKeyHashType, List<Utxo>> utxos { get; private set; }

        public void Add(PubKeyHashType address, Utxo utxo)
        {
            List<Utxo> list = null;
            if (utxos.Keys.Contains(address))
                list = utxos[address];
            else
                utxos.Add(address, list = new List<Utxo>());

            if (list.FirstOrDefault((elem) => elem.blockIndex == utxo.blockIndex && elem.txIndex == utxo.txIndex && elem.txOutIndex == utxo.txOutIndex) != null)
                throw new InvalidOperationException("already_existed");

            list.Add(utxo);
        }

        public void Remove(PubKeyHashType address, long blockIndex, int txIndex, int txOutIndex)
        {
            if (!utxos.Keys.Contains(address))
                throw new InvalidOperationException("not_existed");

            List<Utxo> list = utxos[address];

            Utxo utxo = null;
            if ((utxo = list.FirstOrDefault((elem) => elem.blockIndex == blockIndex && elem.txIndex == txIndex && elem.txOutIndex == txOutIndex)) == null)
                throw new InvalidOperationException("not_existed");

            list.Remove(utxo);

            if (list.Count == 0)
                utxos.Remove(address);
        }

        public void Update(Dictionary<PubKeyHashType, List<Utxo>> addedUtxos, Dictionary<PubKeyHashType, List<Utxo>> removedUtxos)
        {
            foreach (var addedUtxos2 in addedUtxos)
                foreach (var addedUtxo in addedUtxos2.Value)
                    Add(addedUtxos2.Key, addedUtxo);
            foreach (var removedUtxos2 in removedUtxos)
                foreach (var removedUtxo in removedUtxos2.Value)
                    Remove(removedUtxos2.Key, removedUtxo.blockIndex, removedUtxo.txIndex, removedUtxo.txOutIndex);
        }

        public bool Contains(PubKeyHashType address, long blockIndex, int txIndex, int txOutIndex)
        {
            if (!utxos.Keys.Contains(address))
                return false;

            List<Utxo> list = utxos[address];

            return list.FirstOrDefault((elem) => elem.blockIndex == blockIndex && elem.txIndex == txIndex && elem.txOutIndex == txOutIndex) != null;
        }

        public bool ContainsAddress(PubKeyHashType address) { return utxos.Keys.Contains(address); }

        public List<Utxo> GetAddressUtxos(PubKeyHashType address)
        {
            if (!utxos.Keys.Contains(address))
                throw new InvalidOperationException("not_existed");

            return utxos[address];
        }
    }

    public class BlockNode : SHAREDDATA
    {
        public BlockNode() : this(0, 0, 0, 0, false) { }

        public BlockNode(long _parentPosition1, long _parentPosition2, long _position1, long _position2, bool _isMain)
        {
            parentPosition1 = _parentPosition1;
            parentPosition2 = _parentPosition2;
            position1 = _position1;
            position2 = _position2;
            isMain = _isMain;

            childrenPositions1 = new long[] { };
            childrenPositions2 = new long[] { };
        }

        public long[] childrenPositions1 { get; private set; }
        public long[] childrenPositions2 { get; private set; }
        public long parentPosition1 { get; private set; }
        public long parentPosition2 { get; private set; }
        public long position1 { get; private set; }
        public long position2 { get; private set; }
        public bool isMain { get; set; }

        protected override Func<ReaderWriter, IEnumerable<MainDataInfomation>> StreamInfo { get { return (msrw) => StreamInfoInner(msrw); } }
        private IEnumerable<MainDataInfomation> StreamInfoInner(ReaderWriter msrw)
        {
            yield return new MainDataInfomation(typeof(long[]), null, () => childrenPositions1, (o) => childrenPositions1 = (long[])o);
            yield return new MainDataInfomation(typeof(long[]), null, () => childrenPositions2, (o) => childrenPositions2 = (long[])o);

            if (childrenPositions1.Length != childrenPositions2.Length)
                throw new InvalidDataException("blknd_children_positions");

            yield return new MainDataInfomation(typeof(long), () => parentPosition1, (o) => parentPosition1 = (long)o);
            yield return new MainDataInfomation(typeof(long), () => parentPosition2, (o) => parentPosition2 = (long)o);
            yield return new MainDataInfomation(typeof(long), () => position1, (o) => position1 = (long)o);
            yield return new MainDataInfomation(typeof(long), () => position2, (o) => position2 = (long)o);
            yield return new MainDataInfomation(typeof(bool), () => isMain, (o) => isMain = (bool)o);
        }

        public void AddChildPositions(long childPosition1, long childPosition2)
        {
            long[] newChildrenPositions1 = new long[childrenPositions1.Length + 1];
            long[] newChildrenPositions2 = new long[childrenPositions2.Length + 1];

            Array.Copy(childrenPositions1, 0, newChildrenPositions1, 0, childrenPositions1.Length);
            Array.Copy(childrenPositions2, 0, newChildrenPositions2, 0, childrenPositions2.Length);

            newChildrenPositions1[newChildrenPositions1.Length - 1] = childPosition1;
            newChildrenPositions2[newChildrenPositions2.Length - 1] = childPosition2;

            childrenPositions1 = newChildrenPositions1;
            childrenPositions2 = newChildrenPositions2;
        }
    }

    public class BlockNodes : SHAREDDATA
    {
        public BlockNodes() : this(new BlockNode[] { }) { }

        public BlockNodes(BlockNode[] _nodes) { nodes = _nodes; }

        public BlockNode[] nodes { get; private set; }

        protected override Func<ReaderWriter, IEnumerable<MainDataInfomation>> StreamInfo
        {
            get
            {
                return (msrw) => new MainDataInfomation[]{
                    new MainDataInfomation(typeof(BlockNode[]), null, null, () => nodes, (o) => nodes = (BlockNode[])o),
                };
            }
        }

        public void AddBlockNode(BlockNode blockNode)
        {
            BlockNode[] newNodes = new BlockNode[nodes.Length + 1];
            Array.Copy(nodes, 0, newNodes, 0, nodes.Length);
            newNodes[newNodes.Length - 1] = blockNode;
        }
    }

    public class BlockNodesGroup : SHAREDDATA
    {
        public BlockNodesGroup(long _div) : this(new BlockNodes[] { }, _div) { }

        public BlockNodesGroup(BlockNodes[] _nodess, long _div)
        {
            if (_nodess.Length > (int)_div)
                throw new ArgumentException("blknds_group_too_many_nodes");

            div = _div;

            nodess = new BlockNodes[div];
            position = _nodess.Length;

            Array.Copy(_nodess, 0, nodess, 0, _nodess.Length);
        }

        public readonly long div;

        public BlockNodes[] nodess { get; private set; }
        public long position { get; private set; }

        protected override Func<ReaderWriter, IEnumerable<MainDataInfomation>> StreamInfo
        {
            get
            {
                return (msrw) => new MainDataInfomation[]{
                    new MainDataInfomation(typeof(BlockNodes[]), null, null, () => 
                    {
                        BlockNodes[] saveBlockNodess = new BlockNodes[position];
                        Array.Copy(nodess, 0, saveBlockNodess, 0, (int)position);
                        return saveBlockNodess;
                    }, (o) => 
                    {
                        BlockNodes[] loadBlockNodess = (BlockNodes[])o;

                        if (loadBlockNodess.Length > (int)div)
                            throw new ArgumentException("blknds_group_too_many_nodes");

                        position = loadBlockNodess.Length;
                        Array.Copy(loadBlockNodess, 0, nodess, 0, loadBlockNodess.Length);
                    }),
                };
            }
        }

        public void AddBlockNodes(BlockNodes blockNodes)
        {
            if (position >= div)
                throw new InvalidOperationException("blknds_group_full");

            nodess[position] = blockNodes;

            position++;
        }
    }

    #endregion

    #region データベース

    public abstract class DATABASEBASE
    {
        public DATABASEBASE(string _pathBase)
        {
            pathBase = _pathBase;

            if (!Directory.Exists(pathBase))
                Directory.CreateDirectory(pathBase);
        }

        public readonly string pathBase;

        protected abstract string filenameBase { get; }
    }

    public abstract class SimpleDatabase : DATABASEBASE
    {
        public SimpleDatabase(string _pathBase) : base(_pathBase) { }

        public virtual byte[] GetData()
        {
            using (FileStream fs = new FileStream(GetPath(), FileMode.OpenOrCreate, FileAccess.Read))
            {
                byte[] data = new byte[fs.Length];
                fs.Read(data, 0, data.Length);
                return data;
            }
        }

        public virtual void UpdateData(byte[] data)
        {
            using (FileStream fs = new FileStream(GetPath(), FileMode.Create, FileAccess.Write))
                fs.Write(data, 0, data.Length);
        }

        private string GetPath() { return Path.Combine(pathBase, filenameBase); }
    }

    public class AccountHoldersDatabase : SimpleDatabase
    {
        public AccountHoldersDatabase(string _pathBase) : base(_pathBase) { }

#if TEST
        protected override string filenameBase { get { return "acc_test"; } }
#else
        protected override string filenameBase { get { return "acc"; } }
#endif
    }

    public class BlockChainDatabase : SimpleDatabase
    {
        public BlockChainDatabase(string _pathBase) : base(_pathBase) { }

#if TEST
        protected override string filenameBase { get { return "blkchn_test"; } }
#else
        protected override string filenameBase { get { return "blkchn"; } }
#endif
    }

    public class AddressEventDatabase : SimpleDatabase
    {
        public AddressEventDatabase(string _pathBase) : base(_pathBase) { }

#if TEST
        protected override string filenameBase { get { return "address_event_test"; } }
#else
        protected override string filenameBase { get { return "address_event"; } }
#endif
    }

    public class UtxoDatabase : SimpleDatabase
    {
        public UtxoDatabase(string _pathBase) : base(_pathBase) { }

#if TEST
        protected override string filenameBase { get { return "utxo_test"; } }
#else
        protected override string filenameBase { get { return "utxo"; } }
#endif
    }

    public class BlockNodesGroupDatabase : DATABASEBASE
    {
        public BlockNodesGroupDatabase(string _pathBase) : base(_pathBase) { }

#if TEST
        protected override string filenameBase { get { return "blkng_test"; } }
#else
        protected override string filenameBase { get { return "blkng"; } }
#endif

        public byte[] GetBlockNodesGroupData(long bngIndex)
        {
            using (FileStream fs = new FileStream(GetPath(bngIndex), FileMode.OpenOrCreate, FileAccess.Read))
            {
                byte[] data = new byte[fs.Length];
                fs.Read(data, 0, data.Length);
                return data;
            }
        }

        public void UpdateBlockNodesGroupData(byte[] data, long bngIndex)
        {
            using (FileStream fs = new FileStream(GetPath(bngIndex), FileMode.Create, FileAccess.Write))
                fs.Write(data, 0, data.Length);
        }

        private string GetPath(long bngIndex) { return Path.Combine(pathBase, filenameBase + bngIndex.ToString()); }
    }

    public class BlockGroupDatabase : DATABASEBASE
    {
        public BlockGroupDatabase(string _pathBase) : base(_pathBase) { }

#if TEST
        protected override string filenameBase { get { return "blkg_test"; } }
#else
        protected override string filenameBase { get { return "blkg"; } }
#endif

        public byte[] GetBlockGroupData(long bgIndex, long position)
        {
            using (FileStream fs = new FileStream(GetPath(bgIndex), FileMode.OpenOrCreate, FileAccess.Read))
            {
                if (position >= fs.Length)
                    return new byte[] { };

                fs.Seek(position, SeekOrigin.Begin);

                byte[] lengthBytes = new byte[4];
                fs.Read(lengthBytes, 0, 4);
                int length = BitConverter.ToInt32(lengthBytes, 0);

                byte[] data = new byte[length];
                fs.Read(data, 0, length);
                return data;
            }
        }

        public long AddBlockGroupData(byte[] data, long bgIndex)
        {
            using (FileStream fs = new FileStream(GetPath(bgIndex), FileMode.Append, FileAccess.Write))
            {
                long position = fs.Position;

                fs.Write(BitConverter.GetBytes(data.Length), 0, 4);
                fs.Write(data, 0, data.Length);

                return position;
            }
        }

        private string GetPath(long bgIndex)
        {
            return Path.Combine(pathBase, filenameBase + bgIndex.ToString());
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

    #region secp256k1

    public static class Secp256k1
    {
        public static readonly BigInteger P = "FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFEFFFFFC2F".HexToBigInteger();
        public static readonly ECPoint G = ECPoint.DecodePoint("0479BE667EF9DCBBAC55A06295CE870B07029BFCDB2DCE28D959F2815B16F81798483ADA7726A3C4655DA4FBFC0E1108A8FD17B448A68554199C47D08FFB10D4B8".HexToBytes());
        public static readonly BigInteger N = "FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFEBAAEDCE6AF48A03BBFD25E8CD0364141".HexToBigInteger();
    }

    public class ECDsaSigner
    {
        private RNGCryptoServiceProvider rngCsp = new RNGCryptoServiceProvider();

        public ECPoint RecoverFromSignature(byte[] hash, BigInteger r, BigInteger s, int recId)
        {
            var x = r;
            if (recId > 1 && recId < 4)
            {
                x += Secp256k1.N;
                x = x % Secp256k1.P;
            }

            if (x >= Secp256k1.P)
            {
                return null;
            }

            byte[] xBytes = x.ToByteArrayUnsigned(true);
            byte[] compressedPoint = new Byte[33];
            compressedPoint[0] = (byte)(0x02 + (recId % 2));
            Buffer.BlockCopy(xBytes, 0, compressedPoint, 33 - xBytes.Length, xBytes.Length);

            ECPoint publicKey = ECPoint.DecodePoint(compressedPoint);

            if (!publicKey.Multiply(Secp256k1.N).IsInfinity) return null;

            var z = -hash.ToBigIntegerUnsigned(true) % Secp256k1.N;
            if (z < 0)
            {
                z += Secp256k1.N;
            }

            var rr = r.ModInverse(Secp256k1.N);
            var u1 = (z * rr) % Secp256k1.N;
            var u2 = (s * rr) % Secp256k1.N;

            var Q = Secp256k1.G.Multiply(u1).Add(publicKey.Multiply(u2));

            return Q;
        }

        public BigInteger[] GenerateSignature(BigInteger privateKey, byte[] hash)
        {
            return GenerateSignature(privateKey, hash, null);
        }

        public BigInteger[] GenerateSignature(BigInteger privateKey, byte[] hash, BigInteger? k)
        {
            for (int i = 0; i < 100; i++)
            {
                if (k == null)
                {
                    byte[] kBytes = new byte[33];
                    rngCsp.GetBytes(kBytes);
                    kBytes[32] = 0;

                    k = new BigInteger(kBytes);
                }
                var z = hash.ToBigIntegerUnsigned(true);

                if (k.Value.IsZero || k >= Secp256k1.N) continue;

                var r = Secp256k1.G.Multiply(k.Value).X % Secp256k1.N;

                if (r.IsZero) continue;

                var ss = (z + r * privateKey);
                var s = (ss * (k.Value.ModInverse(Secp256k1.N))) % Secp256k1.N;

                if (s.IsZero) continue;

                return new BigInteger[] { r, s };
            }

            throw new Exception("Unable to generate signature");
        }

        public bool VerifySignature(ECPoint publicKey, byte[] hash, BigInteger r, BigInteger s)
        {
            if (r >= Secp256k1.N || r.IsZero || s >= Secp256k1.N || s.IsZero)
            {
                return false;
            }

            var z = hash.ToBigIntegerUnsigned(true);
            var w = s.ModInverse(Secp256k1.N);
            var u1 = (z * w) % Secp256k1.N;
            var u2 = (r * w) % Secp256k1.N;
            var pt = Secp256k1.G.Multiply(u1).Add(publicKey.Multiply(u2));
            var pmod = pt.X % Secp256k1.N;

            return pmod == r;
        }
    }

    public class ECPoint : ICloneable
    {
        private readonly bool _isInfinity;
        private readonly BigInteger _x;
        private BigInteger _y;

        public ECPoint(BigInteger x, BigInteger y)
            : this(x, y, false)
        {
        }

        public ECPoint(BigInteger x, BigInteger y, bool isInfinity)
        {
            _x = x;
            _y = y;
            _isInfinity = isInfinity;
        }

        private ECPoint()
        {
            _isInfinity = true;
        }

        public BigInteger X
        {
            get { return _x; }
        }

        public BigInteger Y
        {
            get { return _y; }
        }

        public static ECPoint Infinity
        {
            get { return new ECPoint(); }
        }

        public bool IsInfinity
        {
            get { return _isInfinity; }
        }

        public object Clone()
        {
            return new ECPoint(_x, _y, _isInfinity);
        }

        //TODO: Rename to Encode (point is implied)
        public byte[] EncodePoint(bool compressed)
        {
            if (IsInfinity)
                return new byte[1];

            byte[] x = X.ToByteArrayUnsigned(true);
            byte[] encoded;
            if (!compressed)
            {
                byte[] y = Y.ToByteArrayUnsigned(true);
                encoded = new byte[65];
                encoded[0] = 0x04;
                Buffer.BlockCopy(y, 0, encoded, 33 + (32 - y.Length), y.Length);
            }
            else
            {
                encoded = new byte[33];
                encoded[0] = (byte)(Y.TestBit(0) ? 0x03 : 0x02);
            }

            Buffer.BlockCopy(x, 0, encoded, 1 + (32 - x.Length), x.Length);
            return encoded;
        }

        //TODO: Rename to Decode (point is implied)
        public static ECPoint DecodePoint(byte[] encoded)
        {
            if (encoded == null || ((encoded.Length != 33 && encoded[0] != 0x02 && encoded[0] != 0x03) && (encoded.Length != 65 && encoded[0] != 0x04)))
                throw new FormatException("Invalid encoded point");

            var unsigned = new byte[32];
            Buffer.BlockCopy(encoded, 1, unsigned, 0, 32);
            BigInteger x = unsigned.ToBigIntegerUnsigned(true);
            BigInteger y;
            byte prefix = encoded[0];

            if (prefix == 0x04) //uncompressed PubKey
            {
                Buffer.BlockCopy(encoded, 33, unsigned, 0, 32);
                y = unsigned.ToBigIntegerUnsigned(true);
            }
            else // compressed PubKey
            {
                // solve y
                y = ((x * x * x + 7) % Secp256k1.P).ShanksSqrt(Secp256k1.P);

                if (y.IsEven ^ prefix == 0x02) // negate y for prefix (0x02 indicates y is even, 0x03 indicates y is odd)
                    y = -y + Secp256k1.P;      // TODO:  DRY replace this and body of Negate() with call to static method
            }
            return new ECPoint(x, y);
        }

        public ECPoint Negate()
        {
            var r = (ECPoint)Clone();
            r._y = -r._y + Secp256k1.P;
            return r;
        }

        public ECPoint Subtract(ECPoint b)
        {
            return Add(b.Negate());
        }

        public ECPoint Add(ECPoint b)
        {
            BigInteger m;
            //[Resharper unused local variable] BigInteger r = 0;

            if (IsInfinity)
                return b;
            if (b.IsInfinity)
                return this;

            if (X - b.X == 0)
            {
                if (Y - b.Y == 0)
                    m = 3 * X * X * (2 * Y).ModInverse(Secp256k1.P);
                else
                    return Infinity;
            }
            else
            {
                var mx = (X - b.X);
                if (mx < 0)
                    mx += Secp256k1.P;
                m = (Y - b.Y) * mx.ModInverse(Secp256k1.P);
            }

            m = m % Secp256k1.P;

            var v = Y - m * X;
            var x3 = (m * m - X - b.X);
            x3 = x3 % Secp256k1.P;
            if (x3 < 0)
                x3 += Secp256k1.P;
            var y3 = -(m * x3 + v);
            y3 = y3 % Secp256k1.P;
            if (y3 < 0)
                y3 += Secp256k1.P;

            return new ECPoint(x3, y3);
        }

        public ECPoint Twice()
        {
            return Add(this);
        }

        public ECPoint Multiply(BigInteger b)
        {
            if (b.Sign == -1)
                throw new FormatException("The multiplicator cannot be negative");

            b = b % Secp256k1.N;

            ECPoint result = Infinity;
            ECPoint temp = null;

            //[Resharper local variable only assigned not used] int bit = 0;
            do
            {
                temp = temp == null ? this : temp.Twice();

                if (!b.IsEven)
                    result = result.Add(temp);
                //bit++;
            }
            while ((b >>= 1) != 0);

            return result;
        }
    }

    public static class Hex
    {
        private static readonly string[] _byteToHex = new[]
        {
            "00", "01", "02", "03", "04", "05", "06", "07", 
            "08", "09", "0a", "0b", "0c", "0d", "0e", "0f",
            "10", "11", "12", "13", "14", "15", "16", "17", 
            "18", "19", "1a", "1b", "1c", "1d", "1e", "1f",
            "20", "21", "22", "23", "24", "25", "26", "27", 
            "28", "29", "2a", "2b", "2c", "2d", "2e", "2f",
            "30", "31", "32", "33", "34", "35", "36", "37", 
            "38", "39", "3a", "3b", "3c", "3d", "3e", "3f",
            "40", "41", "42", "43", "44", "45", "46", "47", 
            "48", "49", "4a", "4b", "4c", "4d", "4e", "4f",
            "50", "51", "52", "53", "54", "55", "56", "57", 
            "58", "59", "5a", "5b", "5c", "5d", "5e", "5f",
            "60", "61", "62", "63", "64", "65", "66", "67", 
            "68", "69", "6a", "6b", "6c", "6d", "6e", "6f",
            "70", "71", "72", "73", "74", "75", "76", "77", 
            "78", "79", "7a", "7b", "7c", "7d", "7e", "7f",
            "80", "81", "82", "83", "84", "85", "86", "87", 
            "88", "89", "8a", "8b", "8c", "8d", "8e", "8f",
            "90", "91", "92", "93", "94", "95", "96", "97", 
            "98", "99", "9a", "9b", "9c", "9d", "9e", "9f",
            "a0", "a1", "a2", "a3", "a4", "a5", "a6", "a7", 
            "a8", "a9", "aa", "ab", "ac", "ad", "ae", "af",
            "b0", "b1", "b2", "b3", "b4", "b5", "b6", "b7", 
            "b8", "b9", "ba", "bb", "bc", "bd", "be", "bf",
            "c0", "c1", "c2", "c3", "c4", "c5", "c6", "c7", 
            "c8", "c9", "ca", "cb", "cc", "cd", "ce", "cf",
            "d0", "d1", "d2", "d3", "d4", "d5", "d6", "d7", 
            "d8", "d9", "da", "db", "dc", "dd", "de", "df",
            "e0", "e1", "e2", "e3", "e4", "e5", "e6", "e7", 
            "e8", "e9", "ea", "eb", "ec", "ed", "ee", "ef",
            "f0", "f1", "f2", "f3", "f4", "f5", "f6", "f7", 
            "f8", "f9", "fa", "fb", "fc", "fd", "fe", "ff"
        };

        private static readonly Dictionary<string, byte> _hexToByte = new Dictionary<string, byte>();

        static Hex()
        {
            for (byte b = 0; b < 255; b++)
            {
                _hexToByte[_byteToHex[b]] = b;
            }

            _hexToByte["ff"] = 255;
        }

        public static string BigIntegerToHex(BigInteger value)
        {
            return BytesToHex(value.ToByteArrayUnsigned(true));
        }

        public static BigInteger HexToBigInteger(string hex)
        {
            byte[] bytes = HexToBytes(hex);
            Array.Reverse(bytes);
            Array.Resize(ref bytes, bytes.Length + 1);
            bytes[bytes.Length - 1] = 0x00;
            return new BigInteger(bytes);
        }

        public static string BytesToHex(byte[] bytes)
        {
            var hex = new StringBuilder(bytes.Length * 2);
            foreach (byte b in bytes)
            {
                hex.Append(_byteToHex[b]);
            }

            return hex.ToString();
        }

        public static byte[] HexToBytes(string hex)
        {
            if (hex.Length % 2 != 0)
            {
                hex = "0" + hex;
            }

            hex = hex.ToLower();

            var bytes = new byte[hex.Length / 2];
            for (int i = 0; i < hex.Length / 2; i++)
            {
                bytes[i] = _hexToByte[hex.Substring(i * 2, 2)];
            }

            return bytes;
        }

        public static string AsciiToHex(string ascii)
        {
            char[] chars = ascii.ToCharArray();
            var hex = new StringBuilder(ascii.Length);

            foreach (var currentChar in chars)
            {
                hex.Append(String.Format("{0:X}", Convert.ToInt32(currentChar)));
            }

            return hex.ToString();
        }
    }

    public static class BigIntExtensions
    {
        public static BigInteger ModInverse(this BigInteger n, BigInteger p)
        {
            BigInteger x = 1;
            BigInteger y = 0;
            BigInteger a = p;
            BigInteger b = n;

            while (b != 0)
            {
                BigInteger t = b;
                BigInteger q = BigInteger.Divide(a, t);
                b = a - q * t;
                a = t;
                t = x;
                x = y - q * t;
                y = t;
            }

            if (y < 0)
                return y + p;
            //else
            return y;
        }

        public static bool TestBit(this BigInteger i, int n)
        {
            //[resharper:unused local variable] int bitLength = i.BitLength();
            return !(i >> n).IsEven;
        }

        public static int BitLength(this BigInteger i)
        {
            int bitLength = 0;
            do
            {
                bitLength++;
            }
            while ((i >>= 1) != 0);
            return bitLength;
        }

        public static byte[] ToByteArrayUnsigned(this BigInteger i, bool bigEndian)
        {
            byte[] bytes = i.ToByteArray();
            if (bytes[bytes.Length - 1] == 0x00)
                Array.Resize(ref bytes, bytes.Length - 1);
            if (bigEndian)
                Array.Reverse(bytes, 0, bytes.Length);

            return bytes;
        }

        public static BigInteger Order(this BigInteger b, BigInteger p)
        {
            BigInteger m = 1;
            BigInteger e = 0;

            while (BigInteger.ModPow(b, m, p) != 1)
            {
                m *= 2;
                e++;
            }

            return e;
        }

        private static BigInteger FindS(BigInteger p)
        {
            BigInteger s = p - 1;
            BigInteger e = 0;

            while (s % 2 == 0)
            {
                s /= 2;
                e += 1;
            }

            return s;
        }

        private static BigInteger FindE(BigInteger p)
        {
            BigInteger s = p - 1;
            BigInteger e = 0;

            while (s % 2 == 0)
            {
                s /= 2;
                e += 1;
            }

            return e;
        }

        private static BigInteger TwoExp(BigInteger e)
        {
            BigInteger a = 1;

            while (e > 0)
            {
                a *= 2;
                e--;
            }

            return a;
        }

        public static string ToHex(this BigInteger b)
        {
            return Hex.BigIntegerToHex(b);
        }

        public static string ToHex(this byte[] bytes)
        {
            return Hex.BytesToHex(bytes);
        }

        public static BigInteger HexToBigInteger(this string s)
        {
            return Hex.HexToBigInteger(s);
        }

        public static byte[] HexToBytes(this string s)
        {
            return Hex.HexToBytes(s);
        }

        public static BigInteger ToBigInteger(this byte[] bytes, bool bigEndian)
        {
            byte[] clone = new byte[bytes.Length];
            Buffer.BlockCopy(bytes, 0, clone, 0, bytes.Length);
            Array.Reverse(clone);

            return new BigInteger(bytes);
        }

        public static BigInteger ToBigIntegerUnsigned(this byte[] bytes, bool bigEndian)
        {
            byte[] clone;
            if (bigEndian)
            {
                if (bytes[0] != 0x00)
                {
                    clone = new byte[bytes.Length + 1];
                    Buffer.BlockCopy(bytes, 0, clone, 1, bytes.Length);
                    Array.Reverse(clone);
                    return new BigInteger(clone);
                }
                clone = new byte[bytes.Length];
                Buffer.BlockCopy(bytes, 0, clone, 0, bytes.Length);
                Array.Reverse(clone);
                return new BigInteger(clone);
            }

            if (bytes[bytes.Length - 1] == 0x00)
                return new BigInteger(bytes);

            clone = new byte[bytes.Length + 1];
            Buffer.BlockCopy(bytes, 0, clone, 0, bytes.Length);
            return new BigInteger(clone);
        }

        public static BigInteger ShanksSqrt(this BigInteger a, BigInteger p)
        {
            if (BigInteger.ModPow(a, (p - 1) / 2, p) == (p - 1))
                return -1;

            if (p % 4 == 3)
                return BigInteger.ModPow(a, (p + 1) / 4, p);

            //Initialize 
            BigInteger s = FindS(p);
            BigInteger e = FindE(p);
            BigInteger n = 2;

            while (BigInteger.ModPow(n, (p - 1) / 2, p) == 1)
                n++;

            BigInteger x = BigInteger.ModPow(a, (s + 1) / 2, p);
            BigInteger b = BigInteger.ModPow(a, s, p);
            BigInteger g = BigInteger.ModPow(n, s, p);
            BigInteger r = e;
            BigInteger m = b.Order(p);

#if(DEBUG)
            Debug.WriteLine("{0}, {1}, {2}, {3}, {4}", m, x, b, g, r);
#endif
            while (m > 0)
            {
                x = (x * BigInteger.ModPow(g, TwoExp(r - m - 1), p)) % p;
                b = (b * BigInteger.ModPow(g, TwoExp(r - m), p)) % p;
                g = BigInteger.ModPow(g, TwoExp(r - m), p);
                r = m;
                m = b.Order(p);

#if(DEBUG)
                Debug.WriteLine("{0}, {1}, {2}, {3}, {4}", m, x, b, g, r);
#endif
            }

            return x;
        }
    }

    #endregion
}