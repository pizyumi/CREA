//がをがを～！
//2014/11/03 分割

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
    #region データ

    //試験用？
    public class Chat : SHAREDDATA
    {
        public Chat() : base(0) { }

        public void LoadVersion0(String _name, String _message)
        {
            this.Version = 0;

            this.Name = _name;
            this.Message = _message;
            this.Id = Guid.NewGuid();
        }

        public String Name { get; private set; }
        public String Message { get; private set; }
        public Guid Id { get; private set; }
        public Secp256k1Signature signature { get; private set; }

        protected override Func<ReaderWriter, IEnumerable<MainDataInfomation>> StreamInfo
        {
            get
            {
                if (Version == 0)
                    return (msrw) => new MainDataInfomation[]{
                        new MainDataInfomation(typeof(string), () => this.Name, (o) => this.Name = (string)o),
                        new MainDataInfomation(typeof(string), () => this.Message, (o) => this.Message = (string)o),
                        new MainDataInfomation(typeof(Byte[]), 16, () => this.Id.ToByteArray(), (o) => this.Id = new Guid((byte[])o)),
                        new MainDataInfomation(typeof(Secp256k1Signature), null, () => this.signature, (o) => this.signature = (Secp256k1Signature)o),
                    };
                else
                    throw new NotSupportedException();
            }
        }

        public override bool IsVersioned { get { return true; } }
        public override bool IsCorruptionChecked { get { return true; } }

        public Func<ReaderWriter, IEnumerable<MainDataInfomation>> StreamInfoToSign
        {
            get
            {
                if (Version == 0)
                    return (msrw) => new MainDataInfomation[]{
                        new MainDataInfomation(typeof(string), () => this.Name, (o) => this.Name = (string)o),
                        new MainDataInfomation(typeof(string), () => this.Message, (o) => this.Message = (string)o),
                        new MainDataInfomation(typeof(Byte[]), 16, () => this.Id.ToByteArray(), (o) => this.Id = new Guid((byte[])o))
                    };
                else
                    throw new NotSupportedException();
            }
        }

        public void Sign(Secp256k1PrivKey<Sha256Hash> _privateKey)
        {
            signature = _privateKey.Sign(ToBinary(StreamInfoToSign)) as Secp256k1Signature;
        }

        public bool Verify()
        {
            var tmp = ToBinary(StreamInfoToSign);
            return Secp256k1Utility.Recover<Sha256Hash>(tmp, this.signature.signature).Verify(tmp, this.signature.signature);
        }
    }

    public class ChatCollection : SHAREDDATA
    {
        public ChatCollection()
            : base(null)
        {
            chats = new List<Chat>();
            chatsCache = new CachedData<Chat[]>(() =>
            {
                lock (chatsLock)
                    return chats.ToArray();
            });
        }

        private readonly object chatsLock = new object();
        private List<Chat> chats;
        private readonly CachedData<Chat[]> chatsCache;
        public Chat[] Chats { get { return chatsCache.Data; } }

        public event EventHandler<Chat> ChatAdded = delegate { };
        public event EventHandler<Chat> ChatRemoved = delegate { };

        public bool Contains(Guid id)
        {
            lock (chatsLock)
                return chats.FirstOrDefault((elem) => elem.Id.Equals(id)) != null;
        }

        public bool AddAccount(Chat chat)
        {
            lock (chatsLock)
            {
                if (chats.Contains(chat))
                    return false;

                this.ExecuteBeforeEvent(() =>
                {
                    chats.Add(chat);
                    chatsCache.IsModified = true;
                }, chat, ChatAdded);

                return true;
            }
        }

        public bool RemoveAccount(Chat chat)
        {
            lock (chatsLock)
            {
                if (!chats.Contains(chat))
                    return false;

                this.ExecuteBeforeEvent(() =>
                {
                    chats.Remove(chat);
                    chatsCache.IsModified = true;
                }, chat, ChatRemoved);

                return true;
            }
        }

        protected override Func<ReaderWriter, IEnumerable<MainDataInfomation>> StreamInfo
        {
            get { throw new NotImplementedException(); }
        }
    }

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

        public T XOR<T>(T other) where T : HASHBASE
        {
            byte[] xorBytes = new byte[SizeByte];
            for (int i = 0; i < SizeByte; i++)
                xorBytes[i] = (byte)(hash[i] ^ other.hash[i]);
            return HASHBASE.FromHash<T>(xorBytes);
        }

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

        public override bool Equals(object obj) { return (obj as HASHBASE).Pipe((o) => o != null && Equals(o)); }

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

    //<未修正>Signの戻り値をジェネリックに
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

        public abstract DSASIGNATUREBASE Sign(byte[] data);

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

    public abstract class DSASIGNATUREBASE : SHAREDDATA
    {
        public DSASIGNATUREBASE() : base(null) { }

        public DSASIGNATUREBASE(byte[] _signature)
            : base(null)
        {
            if (_signature.Length != SizeByte)
                throw new InvalidOperationException("invalid_length_signature");

            signature = _signature;
        }

        public byte[] signature { get; private set; }

        public abstract int SizeByte { get; }

        protected override Func<ReaderWriter, IEnumerable<MainDataInfomation>> StreamInfo
        {
            get
            {
                return (msrw) => new MainDataInfomation[]{
                    new MainDataInfomation(typeof(byte[]), SizeByte, () => signature, (o) => signature = (byte[])o),
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

        public override DSASIGNATUREBASE Sign(byte[] data) { return new Ecdsa256Signature(data.SignEcdsaSha256(privKey)); }
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

    public class Ecdsa256Signature : DSASIGNATUREBASE
    {
        public Ecdsa256Signature() : base() { }

        public Ecdsa256Signature(byte[] _signature) : base(_signature) { }

        public override int SizeByte { get { return 64; } }
    }

    public static class Secp256k1Utility
    {
        public static Secp256k1PubKey<HashType> Recover<HashType>(byte[] data, byte[] signature) where HashType : HASHBASE
        {
            var r = new byte[32];
            var s = new byte[32];
            Buffer.BlockCopy(signature, 1, r, 0, 32);
            Buffer.BlockCopy(signature, 33, s, 0, 32);
            var recId = signature[0] - 27;

            ECDsaSigner signer = new ECDsaSigner();

            byte[] hash = (Activator.CreateInstance(typeof(HashType), data) as HashType).hash;

            ECPoint publicKey = signer.RecoverFromSignature(hash, r.ToBigIntegerUnsigned(true), s.ToBigIntegerUnsigned(true), recId);

            return new Secp256k1PubKey<HashType>(publicKey.EncodePoint(false));
        }

        public static bool RecoverAndVerify<HashType>(byte[] data, byte[] signature) where HashType : HASHBASE
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

    public class Secp256k1PubKey<HashType> : DSAPUBKEYBASE where HashType : HASHBASE
    {
        public Secp256k1PubKey() : base() { }

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
    }

    public class Secp256k1PrivKey<HashType> : DSAPRIVKEYBASE where HashType : HASHBASE
    {
        public Secp256k1PrivKey() : base() { }

        public Secp256k1PrivKey(byte[] _pribKey) : base(_pribKey) { }

        public override int SizeByte { get { return 32; } }

        public override DSASIGNATUREBASE Sign(byte[] data)
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

            return new Secp256k1Signature(sig);
        }
    }

    public class Secp256k1KeyPair<HashType> : DSAKEYPAIRBASE<Secp256k1PubKey<HashType>, Secp256k1PrivKey<HashType>> where HashType : HASHBASE
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
                privKey = new Secp256k1PrivKey<HashType>(privKeyBytes);
            }
        }
    }

    public class Secp256k1Signature : DSASIGNATUREBASE
    {
        public Secp256k1Signature() : base() { }

        public Secp256k1Signature(byte[] _signature) : base(_signature) { }

        public override int SizeByte { get { return 65; } }
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

        event EventHandler iAccountChanged;
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

        void iAddAccountHolder(IPseudonymousAccountHolder iPseudonymousAccountHolder);
        void iDeleteAccountHolder(IPseudonymousAccountHolder iPseudonymousAccountHolder);
    }

    public interface IAccountHoldersFactory
    {
        IAccount CreateAccount(string name, string description);
        IPseudonymousAccountHolder CreatePseudonymousAccountHolder(string name);
    }

    public class Account : SHAREDDATA, IAccount
    {
        public Account() : base(0) { accountStatus = new AccountStatus(); }

        public void LoadVersion0(string _name, string _description)
        {
            Version = 0;

            LoadCommon(_name, _description);

            ecdsa256KeyPair = new Ecdsa256KeyPair(true);
        }

        public void LoadVersion1(string _name, string _description)
        {
            Version = 1;

            LoadCommon(_name, _description);

            secp256k1KeyPair = new Secp256k1KeyPair<Sha256Hash>(true);
        }

        private void LoadCommon(string _name, string _description)
        {
            name = _name;
            description = _description;
        }

        public AccountStatus accountStatus { get; private set; }

        private string name;
        private string description;
        private Ecdsa256KeyPair ecdsa256KeyPair;
        private Secp256k1KeyPair<Sha256Hash> secp256k1KeyPair;

        public string Name
        {
            get { return name; }
            set
            {
                if (name != value)
                    this.ExecuteBeforeEvent(() => name = value, AccountChanged);
            }
        }
        public string Description
        {
            get { return description; }
            set
            {
                if (description != value)
                    this.ExecuteBeforeEvent(() => description = value, AccountChanged);
            }
        }
        public Ecdsa256KeyPair Ecdsa256KeyPair
        {
            get
            {
                if (Version != 0)
                    throw new NotSupportedException();
                return ecdsa256KeyPair;
            }
        }
        public Secp256k1KeyPair<Sha256Hash> Secp256k1KeyPair
        {
            get
            {
                if (Version != 1)
                    throw new NotSupportedException();
                return secp256k1KeyPair;
            }
        }

        public DSAPUBKEYBASE pubKey
        {
            get
            {
                if (Version == 0)
                    return ecdsa256KeyPair.pubKey;
                else if (Version == 1)
                    return secp256k1KeyPair.pubKey;
                else
                    throw new NotSupportedException();
            }
        }
        public DSAPRIVKEYBASE privKey
        {
            get
            {
                if (Version == 0)
                    return ecdsa256KeyPair.privKey;
                else if (Version == 1)
                    return ecdsa256KeyPair.privKey;
                else
                    throw new NotSupportedException();
            }
        }
        public AccountAddress Address
        {
            get
            {
                if (Version != 0 && Version != 1)
                    throw new NotSupportedException();
                return new AccountAddress(pubKey.pubKey);
            }
        }
        public string AddressBase58
        {
            get
            {
                if (Version != 0 && Version != 1)
                    throw new NotSupportedException();
                return Address.Base58;
            }
        }

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
                        new MainDataInfomation(typeof(Ecdsa256KeyPair), null, () => ecdsa256KeyPair, (o) => ecdsa256KeyPair = (Ecdsa256KeyPair)o),
                    };
                else if (Version == 1)
                    return (msrw) => new MainDataInfomation[]{
                        new MainDataInfomation(typeof(string), () => name, (o) => name = (string)o),
                        new MainDataInfomation(typeof(string), () => description, (o) => description = (string)o),
                        new MainDataInfomation(typeof(Secp256k1KeyPair<Sha256Hash>), null, () => secp256k1KeyPair, (o) => secp256k1KeyPair = (Secp256k1KeyPair<Sha256Hash>)o),
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
                if (Version == 0 || Version == 1)
                    return true;
                else
                    throw new NotSupportedException("account_check");
            }
        }

        public event EventHandler AccountChanged = delegate { };

        public override string ToString() { return string.Join(":", Name, AddressBase58); }

        public string iName { get { return Name; } }
        public string iDescription { get { return Description; } }
        public string iAddress { get { return AddressBase58; } }

        public CurrencyUnit iUsableAmount { get { return accountStatus.usableAmount; } }
        public CurrencyUnit iUnusableAmount { get { return accountStatus.unusableAmount; } }

        public event EventHandler iAccountChanged
        {
            add { AccountChanged += value; }
            remove { AccountChanged -= value; }
        }
    }

    public abstract class AccountHolder : SHAREDDATA, IAccountHolder
    {
        public AccountHolder()
            : base(0)
        {
            accounts = new List<Account>();
            accountsCache = new CachedData<Account[]>(() =>
            {
                lock (accountsLock)
                    return accounts.ToArray();
            });
        }

        public virtual void LoadVersion0() { Version = 0; }
        public virtual void LoadVersion1() { Version = 1; }

        private readonly object accountsLock = new object();
        private List<Account> accounts;
        private readonly CachedData<Account[]> accountsCache;
        public Account[] Accounts { get { return accountsCache.Data; } }

        protected override Func<ReaderWriter, IEnumerable<MainDataInfomation>> StreamInfo
        {
            get
            {
                if (Version == 0)
                    return (msrw) => new MainDataInfomation[]{
                        new MainDataInfomation(typeof(Account[]), 0, null, () => accountsCache.Data, (o) => 
                        {
                            accounts = ((Account[])o).ToList();
                            foreach (var account in accounts)
                                if (account.Version != 0)
                                    throw new NotSupportedException();
                        }),
                    };
                else if (Version == 1)
                    return (msrw) => new MainDataInfomation[]{
                        new MainDataInfomation(typeof(Account[]), 1, null, () => accountsCache.Data, (o) => 
                        {
                            accounts = ((Account[])o).ToList();
                            foreach (var account in accounts)
                                if (account.Version != 1)
                                    throw new NotSupportedException();
                        }),
                    };
                else
                    throw new NotSupportedException();
            }
        }
        public override bool IsVersioned { get { return true; } }
        public override bool IsCorruptionChecked
        {
            get
            {
                if (Version == 0 || Version == 1)
                    return true;
                else
                    throw new NotSupportedException();
            }
        }

        public event EventHandler<Account> AccountAdded = delegate { };
        protected EventHandler<Account> PAccountAdded { get { return AccountAdded; } }

        public event EventHandler<Account> AccountRemoved = delegate { };
        protected EventHandler<Account> PAccountRemoved { get { return AccountRemoved; } }

        public event EventHandler AccountHolderChanged = delegate { };
        protected EventHandler PAccountHolderChanged { get { return AccountHolderChanged; } }

        public void AddAccount(Account account)
        {
            if (Version != 0 && Version != 1)
                throw new NotSupportedException();
            if (account.Version != Version)
                throw new ArgumentException();

            lock (accountsLock)
            {
                if (accounts.Contains(account))
                    throw new InvalidOperationException("exist_account");

                this.ExecuteBeforeEvent(() =>
                {
                    accounts.Add(account);
                    accountsCache.IsModified = true;
                }, account, AccountAdded);
            }
        }

        public void RemoveAccount(Account account)
        {
            if (Version != 0 && Version != 1)
                throw new NotSupportedException();
            if (account.Version != Version)
                throw new ArgumentException();

            lock (accountsLock)
            {
                if (!accounts.Contains(account))
                    throw new InvalidOperationException("not_exist_account");

                this.ExecuteBeforeEvent(() =>
                {
                    accounts.Remove(account);
                    accountsCache.IsModified = true;
                }, account, AccountRemoved);
            }
        }

        public IAccount[] iAccounts { get { return Accounts; } }

        private Dictionary<EventHandler<IAccount>, EventHandler<Account>> iAccountAddedDict = new Dictionary<EventHandler<IAccount>, EventHandler<Account>>();
        public event EventHandler<IAccount> iAccountAdded
        {
            add
            {
                EventHandler<Account> eh = (sender, e) => value(sender, e);

                iAccountAddedDict.Add(value, eh);

                AccountAdded += eh;
            }
            remove
            {
                EventHandler<Account> eh = iAccountAddedDict[value];

                iAccountAddedDict.Remove(value);

                AccountAdded -= eh;
            }
        }

        private Dictionary<EventHandler<IAccount>, EventHandler<Account>> iAccountRemovedDict = new Dictionary<EventHandler<IAccount>, EventHandler<Account>>();
        public event EventHandler<IAccount> iAccountRemoved
        {
            add
            {
                EventHandler<Account> eh = (sender, e) => value(sender, e);

                iAccountRemovedDict.Add(value, eh);

                AccountRemoved += eh;
            }
            remove
            {
                EventHandler<Account> eh = iAccountRemovedDict[value];

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
            if (!(iAccount is Account))
                throw new ArgumentException("type_mismatch");

            AddAccount(iAccount as Account);
        }

        public void iRemoveAccount(IAccount iAccount)
        {
            if (!(iAccount is Account))
                throw new ArgumentException("type_mismatch");

            RemoveAccount(iAccount as Account);
        }
    }

    public class AnonymousAccountHolder : AccountHolder, IAnonymousAccountHolder { }

    public class PseudonymousAccountHolder : AccountHolder, IPseudonymousAccountHolder
    {
        public override void LoadVersion0() { throw new NotSupportedException(); }

        public virtual void LoadVersion0(string _name)
        {
            base.LoadVersion0();

            LoadCommon(_name);

            ecdsa256KeyPair = new Ecdsa256KeyPair(true);
        }

        public override void LoadVersion1() { throw new NotSupportedException(); }

        public virtual void LoadVersion1(string _name)
        {
            base.LoadVersion1();

            LoadCommon(_name);

            secp256k1KeyPair = new Secp256k1KeyPair<Sha256Hash>(true);
        }

        private void LoadCommon(string _name) { name = _name; }

        private string name;
        private Ecdsa256KeyPair ecdsa256KeyPair;
        private Secp256k1KeyPair<Sha256Hash> secp256k1KeyPair;

        public string Name
        {
            get { return name; }
            set
            {
                if (name != value)
                    this.ExecuteBeforeEvent(() => name = value, PAccountHolderChanged);
            }
        }
        public Ecdsa256KeyPair Ecdsa256KeyPair
        {
            get
            {
                if (Version != 0)
                    throw new NotSupportedException();
                return ecdsa256KeyPair;
            }
        }
        public Secp256k1KeyPair<Sha256Hash> Secp256k1KeyPair
        {
            get
            {
                if (Version != 1)
                    throw new NotSupportedException();
                return secp256k1KeyPair;
            }
        }

        public DSAPUBKEYBASE pubKey
        {
            get
            {
                if (Version == 0)
                    return ecdsa256KeyPair.pubKey;
                else if (Version == 1)
                    return secp256k1KeyPair.pubKey;
                else
                    throw new NotSupportedException();
            }
        }
        public DSAPRIVKEYBASE privKey
        {
            get
            {
                if (Version == 0)
                    return ecdsa256KeyPair.privKey;
                else if (Version == 1)
                    return secp256k1KeyPair.privKey;
                else
                    throw new NotSupportedException();
            }
        }
        public string Trip
        {
            get
            {
                if (Version != 0 && Version != 1)
                    throw new NotSupportedException();
                return "◆" + Convert.ToBase64String(pubKey.pubKey).Pipe((s) => s.Substring(s.Length - 12, 12));
            }
        }
        public string Sign { get { return name + Trip; } }

        protected override Func<ReaderWriter, IEnumerable<MainDataInfomation>> StreamInfo
        {
            get
            {
                if (Version == 0)
                    return (msrw) => base.StreamInfo(msrw).Concat(new MainDataInfomation[]{
                        new MainDataInfomation(typeof(string), () => name, (o) => name = (string)o),
                        new MainDataInfomation(typeof(Ecdsa256KeyPair), null, () => ecdsa256KeyPair, (o) => ecdsa256KeyPair = (Ecdsa256KeyPair)o),
                    });
                else if (Version == 1)
                    return (msrw) => base.StreamInfo(msrw).Concat(new MainDataInfomation[]{
                        new MainDataInfomation(typeof(string), () => name, (o) => name = (string)o),
                        new MainDataInfomation(typeof(Secp256k1KeyPair<Sha256Hash>), null, () => secp256k1KeyPair, (o) => secp256k1KeyPair = (Secp256k1KeyPair<Sha256Hash>)o),
                    });
                else
                    throw new NotSupportedException("pah_main_data_info");
            }
        }

        public override string ToString() { return Sign; }

        public string iName { get { return Name; } }
        public string iSign { get { return Sign; } }
    }

    public class AccountHolders : SHAREDDATA, IAccountHolders
    {
        public AccountHolders()
            : base(0)
        {
            anonymousAccountHolder = new AnonymousAccountHolder();

            pseudonymousAccountHolders = new List<PseudonymousAccountHolder>();
            pseudonymousAccountHoldersCache = new CachedData<PseudonymousAccountHolder[]>(() =>
            {
                lock (pahsLock)
                    return pseudonymousAccountHolders.ToArray();
            });

            candidateAccountHolders = new List<PseudonymousAccountHolder>();
        }

        public virtual void LoadVersion0()
        {
            Version = 0;

            anonymousAccountHolder.LoadVersion0();
        }

        public virtual void LoadVersion1()
        {
            Version = 1;

            anonymousAccountHolder.LoadVersion1();
        }

        public AnonymousAccountHolder anonymousAccountHolder { get; private set; }

        private readonly object pahsLock = new object();
        private List<PseudonymousAccountHolder> pseudonymousAccountHolders;
        private readonly CachedData<PseudonymousAccountHolder[]> pseudonymousAccountHoldersCache;
        public PseudonymousAccountHolder[] PseudonymousAccountHolders { get { return pseudonymousAccountHoldersCache.Data; } }

        public IEnumerable<AccountHolder> AllAccountHolders
        {
            get
            {
                yield return anonymousAccountHolder;
                foreach (var pseudonymousAccountHolder in PseudonymousAccountHolders)
                    yield return pseudonymousAccountHolder;
            }
        }

        private readonly object cahsLock = new object();
        private List<PseudonymousAccountHolder> candidateAccountHolders;
        public PseudonymousAccountHolder[] CandidateAccountHolders
        {
            get { return candidateAccountHolders.ToArray(); }
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
                            if (anonymousAccountHolder.Version != 0)
                                throw new NotSupportedException();
                        }),
                        new MainDataInfomation(typeof(PseudonymousAccountHolder[]), 0, null, () => pseudonymousAccountHoldersCache.Data, (o) =>
                        {
                            pseudonymousAccountHolders = ((PseudonymousAccountHolder[])o).ToList();
                            foreach (var pseudonymousAccountHolder in pseudonymousAccountHolders)
                                if (pseudonymousAccountHolder.Version != 0)
                                    throw new NotSupportedException();
                        }),
                        new MainDataInfomation(typeof(PseudonymousAccountHolder[]), 0, null, () => candidateAccountHolders.ToArray(), (o) => 
                        {
                            candidateAccountHolders = ((PseudonymousAccountHolder[])o).ToList();
                            foreach (var candidateAccountHolder in candidateAccountHolders)
                                if (candidateAccountHolder.Version != 0)
                                    throw new NotSupportedException();
                        }),
                    };
                else if (Version == 1)
                    return (mswr) => new MainDataInfomation[]{
                        new MainDataInfomation(typeof(AnonymousAccountHolder), 1, () => anonymousAccountHolder, (o) =>
                        {
                            anonymousAccountHolder = (AnonymousAccountHolder)o;
                            foreach (var pseudonymousAccountHolder in pseudonymousAccountHolders)
                                if (pseudonymousAccountHolder.Version != 1)
                                    throw new NotSupportedException();
                        }),
                        new MainDataInfomation(typeof(PseudonymousAccountHolder[]), 1, null, () => pseudonymousAccountHoldersCache.Data, (o) =>
                        {
                            pseudonymousAccountHolders = ((PseudonymousAccountHolder[])o).ToList();
                            foreach (var pseudonymousAccountHolder in pseudonymousAccountHolders)
                                if (pseudonymousAccountHolder.Version != 1)
                                    throw new NotSupportedException();
                        }),
                        new MainDataInfomation(typeof(PseudonymousAccountHolder[]), 1, null, () => candidateAccountHolders.ToArray(), (o) => 
                        {
                            candidateAccountHolders = ((PseudonymousAccountHolder[])o).ToList();
                            foreach (var candidateAccountHolder in candidateAccountHolders)
                                if (candidateAccountHolder.Version != 1)
                                    throw new NotSupportedException();
                        }),
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
                if (Version == 0 || Version == 1)
                    return true;
                else
                    throw new NotSupportedException("account_holder_database_check");
            }
        }

        public event EventHandler<AccountHolder> AccountHolderAdded = delegate { };
        public event EventHandler<AccountHolder> AccountHolderRemoved = delegate { };

        public void AddAccountHolder(PseudonymousAccountHolder ah)
        {
            if (Version != 0 && Version != 1)
                throw new NotSupportedException();
            if (ah.Version != Version)
                throw new ArgumentException();

            lock (pahsLock)
            {
                if (pseudonymousAccountHolders.Contains(ah))
                    throw new InvalidOperationException("exist_account_holder");

                if (!pseudonymousAccountHolders.Where((e) => e.Name == ah.Name).FirstOrDefault().IsNotNull().RaiseError(this.GetType(), "exist_same_name_account_holder", 5))
                    this.ExecuteBeforeEvent(() =>
                    {
                        pseudonymousAccountHolders.Add(ah);
                        pseudonymousAccountHoldersCache.IsModified = true;
                    }, ah, AccountHolderAdded);
            }
        }

        public void DeleteAccountHolder(PseudonymousAccountHolder ah)
        {
            if (Version != 0 && Version != 1)
                throw new NotSupportedException();
            if (ah.Version != Version)
                throw new ArgumentException();

            lock (pahsLock)
            {
                if (!pseudonymousAccountHolders.Contains(ah))
                    throw new InvalidOperationException("not_exist_account_holder");

                this.ExecuteBeforeEvent(() =>
                {
                    pseudonymousAccountHolders.Remove(ah);
                    pseudonymousAccountHoldersCache.IsModified = true;
                }, ah, AccountHolderRemoved);
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

        public IAnonymousAccountHolder iAnonymousAccountHolder { get { return anonymousAccountHolder; } }
        public IPseudonymousAccountHolder[] iPseudonymousAccountHolders { get { return PseudonymousAccountHolders; } }

        private Dictionary<EventHandler<IAccountHolder>, EventHandler<AccountHolder>> iAccountHolderAddedDict = new Dictionary<EventHandler<IAccountHolder>, EventHandler<AccountHolder>>();
        public event EventHandler<IAccountHolder> iAccountHolderAdded
        {
            add
            {
                EventHandler<AccountHolder> eh = (sender, e) => value(sender, e);

                iAccountHolderAddedDict.Add(value, eh);

                AccountHolderAdded += eh;
            }
            remove
            {
                EventHandler<AccountHolder> eh = iAccountHolderAddedDict[value];

                iAccountHolderAddedDict.Remove(value);

                AccountHolderAdded -= eh;
            }
        }

        private Dictionary<EventHandler<IAccountHolder>, EventHandler<AccountHolder>> iAccountHolderRemovedDict = new Dictionary<EventHandler<IAccountHolder>, EventHandler<AccountHolder>>();
        public event EventHandler<IAccountHolder> iAccountHolderRemoved
        {
            add
            {
                EventHandler<AccountHolder> eh = (sender, e) => value(sender, e);

                iAccountHolderRemovedDict.Add(value, eh);

                AccountHolderRemoved += eh;
            }
            remove
            {
                EventHandler<AccountHolder> eh = iAccountHolderRemovedDict[value];

                iAccountHolderRemovedDict.Remove(value);

                AccountHolderAdded -= eh;
            }
        }

        public void iAddAccountHolder(IPseudonymousAccountHolder iPseudonymousAccountHolder)
        {
            if (!(iPseudonymousAccountHolder is PseudonymousAccountHolder))
                throw new ArgumentException("type_mismatch");

            AddAccountHolder(iPseudonymousAccountHolder as PseudonymousAccountHolder);
        }

        public void iDeleteAccountHolder(IPseudonymousAccountHolder iPseudonymousAccountHolder)
        {
            if (!(iPseudonymousAccountHolder is PseudonymousAccountHolder))
                throw new ArgumentException("type_mismatch");

            DeleteAccountHolder(iPseudonymousAccountHolder as PseudonymousAccountHolder);
        }
    }

    public class AccountHoldersFactory : IAccountHoldersFactory
    {
        public IAccount CreateAccount(string name, string description)
        {
            return new Account().Pipe((account) => account.LoadVersion1(name, description));
        }

        public IPseudonymousAccountHolder CreatePseudonymousAccountHolder(string name)
        {
            return new PseudonymousAccountHolder().Pipe((pseudonymousAccountHolder) => pseudonymousAccountHolder.LoadVersion1(name));
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

        public static CurrencyUnit Zero = new CurrencyUnit(0);

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

    #region 取引

    //<未実装>Load部分の抽象化

    public class TransactionInput : SHAREDDATA
    {
        public TransactionInput() : base(0) { }

        public void LoadVersion0(long _prevTxBlockIndex, int _prevTxIndex, int _prevTxOutputIndex, Ecdsa256PubKey _senderPubKey)
        {
            Version = 0;

            LoadCommon(_prevTxBlockIndex, _prevTxIndex, _prevTxOutputIndex);

            ecdsa256PubKey = _senderPubKey;
        }

        public void LoadVersion1(long _prevTxBlockIndex, int _prevTxIndex, int _prevTxOutputIndex)
        {
            Version = 1;

            LoadCommon(_prevTxBlockIndex, _prevTxIndex, _prevTxOutputIndex);
        }

        private void LoadCommon(long _prevTxBlockIndex, int _prevTxIndex, int _prevTxOutputIndex)
        {
            prevTxBlockIndex = _prevTxBlockIndex;
            prevTxIndex = _prevTxIndex;
            prevTxOutputIndex = _prevTxOutputIndex;
        }

        private long prevTxBlockIndex;
        private int prevTxIndex;
        private int prevTxOutputIndex;
        private Ecdsa256Signature ecdsa256Signature;
        private Secp256k1Signature secp256k1Signature;
        private Ecdsa256PubKey ecdsa256PubKey;

        public long PrevTxBlockIndex { get { return prevTxBlockIndex; } }
        public int PrevTxIndex { get { return prevTxIndex; } }
        public int PrevTxOutputIndex { get { return prevTxOutputIndex; } }
        public Ecdsa256Signature Ecdsa256Signature
        {
            get
            {
                if (Version != 0)
                    throw new NotSupportedException();
                return ecdsa256Signature;
            }
        }
        public Secp256k1Signature Secp256k1Signature
        {
            get
            {
                if (Version != 1)
                    throw new NotSupportedException();
                return secp256k1Signature;
            }
        }
        public Ecdsa256PubKey Ecdsa256PubKey
        {
            get
            {
                if (Version != 0)
                    throw new NotSupportedException();
                return ecdsa256PubKey;
            }
        }

        public DSASIGNATUREBASE SenderSignature
        {
            get
            {
                if (Version == 0)
                    return ecdsa256Signature;
                else if (Version == 1)
                    return secp256k1Signature;
                else
                    throw new NotSupportedException();
            }
        }
        public DSAPUBKEYBASE SenderPubKey
        {
            get
            {
                if (Version == 0)
                    return ecdsa256PubKey;
                //Secp256k1の場合、署名だけではなく署名対象のデータもなければ公開鍵を復元できない
                else if (Version == 1)
                    throw new InvalidOperationException();
                else
                    throw new NotSupportedException();
            }
        }

        protected override Func<ReaderWriter, IEnumerable<MainDataInfomation>> StreamInfo
        {
            get
            {
                if (Version == 0)
                    return (msrw) => new MainDataInfomation[]{
                        new MainDataInfomation(typeof(long), () => prevTxBlockIndex, (o) => prevTxBlockIndex = (long)o),
                        new MainDataInfomation(typeof(int), () => prevTxIndex, (o) => prevTxIndex = (int)o),
                        new MainDataInfomation(typeof(int), () => prevTxOutputIndex, (o) => prevTxOutputIndex = (int)o),
                        new MainDataInfomation(typeof(Ecdsa256Signature), null, () => ecdsa256Signature, (o) => ecdsa256Signature = (Ecdsa256Signature)o),
                        new MainDataInfomation(typeof(Ecdsa256PubKey), null, () => ecdsa256PubKey, (o) => ecdsa256PubKey = (Ecdsa256PubKey)o),
                    };
                else if (Version == 1)
                    return (msrw) => new MainDataInfomation[]{
                        new MainDataInfomation(typeof(long), () => prevTxBlockIndex, (o) => prevTxBlockIndex = (long)o),
                        new MainDataInfomation(typeof(int), () => prevTxIndex, (o) => prevTxIndex = (int)o),
                        new MainDataInfomation(typeof(int), () => prevTxOutputIndex, (o) => prevTxOutputIndex = (int)o),
                        new MainDataInfomation(typeof(Secp256k1Signature), null, () => secp256k1Signature, (o) => secp256k1Signature = (Secp256k1Signature)o),
                    };
                else
                    throw new NotSupportedException();
            }
        }
        public override bool IsVersioned { get { return true; } }
        public override bool IsVersionSaved { get { return false; } }

        public Func<ReaderWriter, IEnumerable<MainDataInfomation>> StreamInfoToSign
        {
            get
            {
                if (Version == 0 || Version == 1)
                    return (msrw) => new MainDataInfomation[]{
                        new MainDataInfomation(typeof(long), () => prevTxBlockIndex, (o) => { throw new NotSupportedException(); }),
                        new MainDataInfomation(typeof(int), () => prevTxIndex, (o) => { throw new NotSupportedException(); }),
                        new MainDataInfomation(typeof(int), () => prevTxOutputIndex, (o) => { throw new NotSupportedException(); }),
                    };
                else
                    throw new NotSupportedException();
            }
        }

        public void SetSenderSig(DSASIGNATUREBASE signature)
        {
            if (Version == 0)
            {
                if (!(signature is Ecdsa256Signature))
                    throw new ArgumentException();
                ecdsa256Signature = signature as Ecdsa256Signature;
            }
            else if (Version == 1)
            {
                if (!(signature is Secp256k1Signature))
                    throw new ArgumentException();
                secp256k1Signature = signature as Secp256k1Signature;
            }
            else
                throw new NotSupportedException();
        }
    }

    public class TransactionOutput : SHAREDDATA
    {
        public TransactionOutput() : base(0) { }

        public void LoadVersion0(Sha256Ripemd160Hash _receiverPubKeyHash, CurrencyUnit _amount)
        {
            Version = 0;

            receiverPubKeyHash = _receiverPubKeyHash;
            amount = _amount;
        }

        private Sha256Ripemd160Hash receiverPubKeyHash;
        private CurrencyUnit amount;

        public Sha256Ripemd160Hash Sha256Ripemd160Hash { get { return receiverPubKeyHash; } }
        public CurrencyUnit Amount { get { return amount; } }

        //<未改良>本来HASHBASEにすべきだが・・・
        public Sha256Ripemd160Hash ReceiverPubKeyHash { get { return receiverPubKeyHash; } }
        public Sha256Ripemd160Hash Address { get { return receiverPubKeyHash; } }

        protected override Func<ReaderWriter, IEnumerable<MainDataInfomation>> StreamInfo
        {
            get
            {
                if (Version == 0)
                    return (msrw) => new MainDataInfomation[]{
                        new MainDataInfomation(typeof(Sha256Ripemd160Hash), null, () => receiverPubKeyHash, (o) => receiverPubKeyHash = (Sha256Ripemd160Hash)o),
                        new MainDataInfomation(typeof(long), () => amount.rawAmount, (o) => amount = new CurrencyUnit((long)o)),
                    };
                else
                    throw new NotSupportedException();
            }
        }
        public override bool IsVersioned { get { return true; } }
        public override bool IsVersionSaved { get { return false; } }

        public Func<ReaderWriter, IEnumerable<MainDataInfomation>> StreamInfoToSign
        {
            get
            {
                if (Version == 0)
                    return (msrw) => new MainDataInfomation[]{
                        new MainDataInfomation(typeof(Sha256Ripemd160Hash), null, () => receiverPubKeyHash, (o) => { throw new NotSupportedException(); }),
                        new MainDataInfomation(typeof(long), () => amount.rawAmount, (o) => { throw new NotSupportedException(); }),
                };
                else
                    throw new NotSupportedException();
            }
        }

        public Func<ReaderWriter, IEnumerable<MainDataInfomation>> StreamInfoToSignPrev
        {
            get
            {
                if (Version == 0)
                    return (msrw) => new MainDataInfomation[]{
                        new MainDataInfomation(typeof(Sha256Ripemd160Hash), null, () => receiverPubKeyHash, (o) => { throw new NotSupportedException(); }),
                };
                else
                    throw new NotSupportedException();
            }
        }
    }

    //<未実装>再度行う必要のない検証は行わない
    public abstract class Transaction : SHAREDDATA
    {
        public Transaction() : base(0) { idCache = new CachedData<Sha256Sha256Hash>(() => new Sha256Sha256Hash(ToBinary())); }

        public virtual void LoadVersion0(TransactionOutput[] _txOutputs)
        {
            foreach (var txOutput in _txOutputs)
                if (txOutput.Version != 0)
                    throw new ArgumentException();

            Version = 0;

            LoadCommon(_txOutputs);
        }

        public virtual void LoadVersion1(TransactionOutput[] _txOutputs)
        {
            foreach (var txOutput in _txOutputs)
                if (txOutput.Version != 0)
                    throw new ArgumentException();

            Version = 1;

            LoadCommon(_txOutputs);
        }

        private void LoadCommon(TransactionOutput[] _txOutputs)
        {
            if (_txOutputs.Length == 0)
                throw new ArgumentException();

            txOutputs = _txOutputs;
        }

        private static readonly CurrencyUnit dustTxoutput = new Yumina(0.1m);
        private static readonly int maxTxInputs = 100;
        private static readonly int maxTxOutputs = 10;

        private TransactionOutput[] _txOutputs;
        private TransactionOutput[] txOutputs
        {
            get { return _txOutputs; }
            set
            {
                if (value != _txOutputs)
                {
                    _txOutputs = value;
                    idCache.IsModified = true;
                }
            }
        }

        public virtual TransactionInput[] TxInputs { get { return new TransactionInput[] { }; } }
        public virtual TransactionOutput[] TxOutputs { get { return txOutputs; } }

        protected readonly CachedData<Sha256Sha256Hash> idCache;
        public virtual Sha256Sha256Hash Id { get { return idCache.Data; } }

        protected override Func<ReaderWriter, IEnumerable<MainDataInfomation>> StreamInfo
        {
            get
            {
                if (Version == 0 || Version == 1)
                    return (msrw) => new MainDataInfomation[]{
                        new MainDataInfomation(typeof(TransactionOutput[]), 0, null, () => txOutputs, (o) => txOutputs = (TransactionOutput[])o),
                    };
                else
                    throw new NotSupportedException();
            }
        }
        public override bool IsVersioned { get { return true; } }
        public override bool IsCorruptionChecked
        {
            get
            {
                if (Version == 0 || Version == 1)
                    return true;
                else
                    throw new NotSupportedException();
            }
        }

        public virtual bool Verify()
        {
            if (Version == 0 || Version == 1)
                return VerifyNotExistDustTxOutput() && VerifyNumberOfTxInputs() && VerifyNumberOfTxOutputs();
            else
                throw new NotSupportedException();
        }

        public bool VerifyNotExistDustTxOutput()
        {
            if (Version == 0 || Version == 1)
                return txOutputs.All((elem) => elem.Amount.rawAmount >= dustTxoutput.rawAmount);
            else
                throw new NotSupportedException();
        }

        public bool VerifyNumberOfTxInputs()
        {
            if (Version == 0 || Version == 1)
                return TxInputs.Length <= maxTxInputs;
            else
                throw new NotSupportedException();
        }

        public bool VerifyNumberOfTxOutputs()
        {
            if (Version == 0 || Version == 1)
                return TxOutputs.Length <= maxTxOutputs;
            else
                throw new NotSupportedException();
        }
    }

    public class CoinbaseTransaction : Transaction
    {
        public override void LoadVersion1(TransactionOutput[] _txOutputs) { throw new NotSupportedException(); }

        public const string guidString = "784aee51e677e6469ca2ae0d6c72d60e";
        public override Guid Guid { get { return new Guid(guidString); } }

        protected override Func<ReaderWriter, IEnumerable<MainDataInfomation>> StreamInfo
        {
            get
            {
                if (Version == 0)
                    return base.StreamInfo;
                else
                    throw new NotSupportedException();
            }
        }
    }

    //<未実装>再度行う必要のない検証は行わない
    public class TransferTransaction : Transaction
    {
        public override void LoadVersion0(TransactionOutput[] _txOutputs) { throw new NotSupportedException(); }

        public virtual void LoadVersion0(TransactionInput[] _txInputs, TransactionOutput[] _txOutputs)
        {
            foreach (var txInput in _txInputs)
                if (txInput.Version != 0)
                    throw new ArgumentException();

            base.LoadVersion0(_txOutputs);

            LoadCommon(_txInputs);
        }

        public override void LoadVersion1(TransactionOutput[] _txOutputs) { throw new NotSupportedException(); }

        public virtual void LoadVersion1(TransactionInput[] _txInputs, TransactionOutput[] _txOutputs)
        {
            foreach (var txInput in _txInputs)
                if (txInput.Version != 1)
                    throw new ArgumentException();

            base.LoadVersion1(_txOutputs);

            LoadCommon(_txInputs);
        }

        private void LoadCommon(TransactionInput[] _txInputs)
        {
            if (_txInputs.Length == 0)
                throw new ArgumentException();

            txInputs = _txInputs;
        }

        private TransactionInput[] _txInputs;
        public TransactionInput[] txInputs
        {
            get { return _txInputs; }
            private set
            {
                if (value != _txInputs)
                {
                    _txInputs = value;
                    idCache.IsModified = true;
                }
            }
        }

        public override TransactionInput[] TxInputs { get { return txInputs; } }

        public const string guidString = "5c5493dff997774db351ae5018844b23";
        public override Guid Guid { get { return new Guid(guidString); } }

        protected override Func<ReaderWriter, IEnumerable<MainDataInfomation>> StreamInfo
        {
            get
            {
                if (Version == 0)
                    return (msrw) => base.StreamInfo(msrw).Concat(new MainDataInfomation[]{
                        new MainDataInfomation(typeof(TransactionInput[]), 0, null, () => txInputs, (o) => txInputs = (TransactionInput[])o),
                    });
                else if (Version == 1)
                    return (msrw) => base.StreamInfo(msrw).Concat(new MainDataInfomation[]{
                        new MainDataInfomation(typeof(TransactionInput[]), 1, null, () => txInputs, (o) => txInputs = (TransactionInput[])o),
                    });
                else
                    throw new NotSupportedException();
            }
        }

        private Func<ReaderWriter, IEnumerable<MainDataInfomation>> StreamInfoToSign(TransactionOutput[] prevTxOutputs) { return (msrw) => StreamInfoToSignInner(prevTxOutputs); }
        private IEnumerable<MainDataInfomation> StreamInfoToSignInner(TransactionOutput[] prevTxOutputs)
        {
            if (Version == 0 || Version == 1)
            {
                for (int i = 0; i < txInputs.Length; i++)
                {
                    foreach (var mdi in txInputs[i].StreamInfoToSign(null))
                        yield return mdi;
                    foreach (var mdi in prevTxOutputs[i].StreamInfoToSignPrev(null))
                        yield return mdi;
                }
                for (int i = 0; i < TxOutputs.Length; i++)
                    foreach (var mdi in TxOutputs[i].StreamInfoToSign(null))
                        yield return mdi;
            }
            else
                throw new NotSupportedException();
        }

        public byte[] GetBytesToSign(TransactionOutput[] prevTxOutputs)
        {
            if (prevTxOutputs.Length != txInputs.Length)
                throw new ArgumentException();

            return ToBinaryMainData(StreamInfoToSign(prevTxOutputs));
        }

        public void Sign(TransactionOutput[] prevTxOutputs, DSAPRIVKEYBASE[] privKeys)
        {
            if (prevTxOutputs.Length != txInputs.Length)
                throw new ArgumentException();
            if (privKeys.Length != txInputs.Length)
                throw new ArgumentException();

            byte[] bytesToSign = GetBytesToSign(prevTxOutputs);

            for (int i = 0; i < txInputs.Length; i++)
                txInputs[i].SetSenderSig(privKeys[i].Sign(bytesToSign));

            //取引入力の内容が変更された
            idCache.IsModified = true;
        }

        public override bool Verify() { throw new NotSupportedException(); }

        public virtual bool Verify(TransactionOutput[] prevTxOutputs)
        {
            if (prevTxOutputs.Length != txInputs.Length)
                throw new ArgumentException();

            if (Version == 0 || Version == 1)
                return base.Verify() && VerifySignature(prevTxOutputs) && VerifyPubKey(prevTxOutputs) && VerifyAmount(prevTxOutputs);
            else
                throw new NotSupportedException();
        }

        public bool VerifySignature(TransactionOutput[] prevTxOutputs)
        {
            if (prevTxOutputs.Length != txInputs.Length)
                throw new ArgumentException();

            byte[] bytesToSign = GetBytesToSign(prevTxOutputs);

            if (Version == 0)
            {
                for (int i = 0; i < txInputs.Length; i++)
                    if (!txInputs[i].Ecdsa256PubKey.Verify(bytesToSign, txInputs[i].SenderSignature.signature))
                        return false;
            }
            else if (Version == 1)
            {
                for (int i = 0; i < txInputs.Length; i++)
                    if (!Secp256k1Utility.Recover<Sha256Hash>(bytesToSign, txInputs[i].SenderSignature.signature).Verify(bytesToSign, txInputs[i].SenderSignature.signature))
                        return false;
            }
            else
                throw new NotSupportedException();
            return true;
        }

        public bool VerifyPubKey(TransactionOutput[] prevTxOutputs)
        {
            if (prevTxOutputs.Length != txInputs.Length)
                throw new ArgumentException();

            if (Version == 0)
            {
                for (int i = 0; i < txInputs.Length; i++)
                    if (!new Sha256Ripemd160Hash(txInputs[i].Ecdsa256PubKey.pubKey).Equals(prevTxOutputs[i].ReceiverPubKeyHash))
                        return false;
            }
            else if (Version == 1)
            {
                byte[] bytesToSign = GetBytesToSign(prevTxOutputs);

                for (int i = 0; i < txInputs.Length; i++)
                    if (!new Sha256Ripemd160Hash(Secp256k1Utility.Recover<Sha256Hash>(bytesToSign, txInputs[i].Secp256k1Signature.signature).pubKey).Equals(prevTxOutputs[i].ReceiverPubKeyHash))
                        return false;
            }
            else
                throw new NotSupportedException();
            return true;
        }

        public bool VerifyAmount(TransactionOutput[] prevTxOutputs)
        {
            if (prevTxOutputs.Length != txInputs.Length)
                throw new ArgumentException();

            return GetFee(prevTxOutputs).rawAmount >= 0;
        }

        public CurrencyUnit GetFee(TransactionOutput[] prevTxOutputs)
        {
            if (prevTxOutputs.Length != txInputs.Length)
                throw new ArgumentException();

            long totalPrevOutputs = 0;
            for (int i = 0; i < prevTxOutputs.Length; i++)
                totalPrevOutputs += prevTxOutputs[i].Amount.rawAmount;
            long totalOutpus = 0;
            for (int i = 0; i < TxOutputs.Length; i++)
                totalOutpus += TxOutputs[i].Amount.rawAmount;

            return new CurrencyUnit(totalPrevOutputs - totalOutpus);
        }
    }

    #endregion

    #region ブロック

    public abstract class Block : SHAREDDATA
    {
        public Block(int? _version) : base(_version) { idCache = new CachedData<X15Hash>(IdGenerator); }

        protected CachedData<X15Hash> idCache;
        protected virtual Func<X15Hash> IdGenerator { get { return () => new X15Hash(ToBinary()); } }
        public virtual X15Hash Id { get { return idCache.Data; } }

        public abstract long Index { get; }
        public abstract X15Hash PrevId { get; }
        public abstract Difficulty<X15Hash> Difficulty { get; }
        public abstract Transaction[] Transactions { get; }

        public virtual bool Verify() { return true; }
    }

    public class GenesisBlock : Block
    {
        public GenesisBlock() : base(null) { }

        public readonly string genesisWord = "Bitstamp 2014/05/25 BTC/USD High 586.34 BTC to the moooooooon!!";

        public override long Index { get { return 0; } }
        public override X15Hash PrevId { get { return null; } }
        public override Difficulty<X15Hash> Difficulty { get { return new Difficulty<X15Hash>(HASHBASE.FromHash<X15Hash>(new byte[] { 0, 127, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255 })); } }
        public override Transaction[] Transactions { get { return new Transaction[] { }; } }

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

    public class BlockHeader : SHAREDDATA
    {
        public BlockHeader() : base(0) { }

        public void LoadVersion0(long _index, X15Hash _prevBlockHash, DateTime _timestamp, Difficulty<X15Hash> _difficulty, byte[] _nonce)
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

        private static readonly int maxNonceLength = 10;

        public long index { get; private set; }
        public X15Hash prevBlockHash { get; private set; }
        public Sha256Sha256Hash merkleRootHash { get; private set; }
        public DateTime timestamp { get; private set; }
        public Difficulty<X15Hash> difficulty { get; private set; }
        public byte[] nonce { get; private set; }

        protected override Func<ReaderWriter, IEnumerable<MainDataInfomation>> StreamInfo
        {
            get
            {
                if (Version == 0)
                    return (msrw) => new MainDataInfomation[]{
                        new MainDataInfomation(typeof(long), () => index, (o) => index = (long)o),
                        new MainDataInfomation(typeof(X15Hash), null, () => prevBlockHash, (o) => prevBlockHash = (X15Hash)o),
                        new MainDataInfomation(typeof(Sha256Sha256Hash), null, () => merkleRootHash, (o) => merkleRootHash = (Sha256Sha256Hash)o),
                        new MainDataInfomation(typeof(DateTime), () => timestamp, (o) => timestamp = (DateTime)o),
                        new MainDataInfomation(typeof(byte[]), 4, () => difficulty.CompactTarget, (o) => difficulty = new Difficulty<X15Hash>((byte[])o)),
                        new MainDataInfomation(typeof(byte[]), null, () => nonce, (o) => nonce = (byte[])o),
                    };
                else
                    throw new NotSupportedException();
            }
        }
        public override bool IsVersioned { get { return true; } }
        public override bool IsVersionSaved { get { return false; } }

        public void UpdateMerkleRootHash(Sha256Sha256Hash newMerkleRootHash) { merkleRootHash = newMerkleRootHash; }
        public void UpdateTimestamp(DateTime newTimestamp) { timestamp = newTimestamp; }
        public void UpdateNonce(byte[] newNonce)
        {
            if (newNonce.Length > maxNonceLength)
                throw new ArgumentOutOfRangeException("block_header_nonce_out");

            nonce = newNonce;
        }
    }

    //<未実装>再度行う必要のない検証は行わない
    public abstract class TransactionalBlock : Block
    {
        static TransactionalBlock()
        {
            rewards = new CurrencyUnit[numberOfCycles];
            rewards[0] = initialReward;
            for (int i = 1; i < numberOfCycles; i++)
                rewards[i] = new Creacoin(rewards[i - 1].Amount * rewardReductionRate);
        }

        public TransactionalBlock()
            : base(0)
        {
            transactionsCache = new CachedData<Transaction[]>(TransactionsGenerator);
            merkleTreeCache = new CachedData<MerkleTree<Sha256Sha256Hash>>(() => new MerkleTree<Sha256Sha256Hash>(Transactions.Select((e) => e.Id).ToArray()));
        }

        public virtual void LoadVersion0(BlockHeader _header, CoinbaseTransaction _coinbaseTxToMiner, TransferTransaction[] _transferTxs)
        {
            if (_header.Version != 0)
                throw new ArgumentException();
            if (_coinbaseTxToMiner.Version != 0)
                throw new ArgumentException();
            foreach (var transferTx in _transferTxs)
                if (transferTx.Version != 0)
                    throw new ArgumentException();

            Version = 0;

            LoadCommon(_header, _coinbaseTxToMiner, _transferTxs);
        }

        public virtual void LoadVersion1(BlockHeader _header, CoinbaseTransaction _coinbaseTxToMiner, TransferTransaction[] _transferTxs)
        {
            if (_header.Version != 0)
                throw new ArgumentException();
            if (_coinbaseTxToMiner.Version != 0)
                throw new ArgumentException();
            foreach (var transferTx in _transferTxs)
                if (transferTx.Version != 1)
                    throw new ArgumentException();

            Version = 1;

            LoadCommon(_header, _coinbaseTxToMiner, _transferTxs);
        }

        private void LoadCommon(BlockHeader _header, CoinbaseTransaction _coinbaseTxToMiner, TransferTransaction[] _transferTxs)
        {
            header = _header;
            coinbaseTxToMiner = _coinbaseTxToMiner;
            transferTxs = _transferTxs;
        }

        private static readonly int maxTxs = 100;

        private static readonly long blockGenerationInterval = 60; //[sec]
        private static readonly long cycle = 60 * 60 * 24 * 365; //[sec]=1[year]
        private static readonly int numberOfCycles = 8;
        private static readonly long rewardlessStart = cycle * numberOfCycles; //[sec]=8[year]
        private static readonly decimal rewardReductionRate = 0.8m;
        private static readonly CurrencyUnit initialReward = new Creacoin(1.0m); //[CREA/sec]
        private static readonly CurrencyUnit[] rewards; //[CREA/sec]
        private static readonly decimal foundationShare = 0.1m;
        private static readonly long foundationInterval = 60 * 60 * 24; //[block]

        private static readonly long numberOfTimestamps = 11;
        private static readonly long targetTimespan = blockGenerationInterval * 1; //[sec]

        private static readonly Difficulty<X15Hash> minDifficulty = new Difficulty<X15Hash>(HASHBASE.FromHash<X15Hash>(new byte[] { 0, 127, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255 }));

#if TEST
        public static readonly Sha256Ripemd160Hash foundationPubKeyHash = new Sha256Ripemd160Hash(new byte[] { 69, 67, 83, 49, 32, 0, 0, 0, 16, 31, 116, 194, 127, 71, 154, 183, 50, 198, 23, 17, 129, 220, 25, 98, 4, 30, 93, 45, 53, 252, 176, 145, 108, 20, 226, 233, 36, 7, 35, 198, 98, 239, 109, 66, 206, 41, 162, 179, 255, 189, 126, 72, 97, 140, 165, 139, 118, 107, 137, 103, 76, 238, 125, 62, 163, 205, 108, 62, 189, 240, 124, 71 });
#else
        public static readonly Sha256Ripemd160Hash foundationPubKeyHash = null;
#endif

        private BlockHeader _header;
        public BlockHeader header
        {
            get { return _header; }
            private set
            {
                if (value != _header)
                {
                    _header = value;
                    idCache.IsModified = true;
                }
            }
        }

        private CoinbaseTransaction _coinbaseTxToMiner;
        public CoinbaseTransaction coinbaseTxToMiner
        {
            get { return _coinbaseTxToMiner; }
            private set
            {
                if (value != _coinbaseTxToMiner)
                {
                    _coinbaseTxToMiner = value;
                    idCache.IsModified = true;
                    transactionsCache.IsModified = true;
                    merkleTreeCache.IsModified = true;
                }
            }
        }

        private TransferTransaction[] _transferTxs;
        public TransferTransaction[] transferTxs
        {
            get { return _transferTxs; }
            private set
            {
                if (value != _transferTxs)
                {
                    _transferTxs = value;
                    idCache.IsModified = true;
                    transactionsCache.IsModified = true;
                    merkleTreeCache.IsModified = true;
                }
            }
        }

        public override long Index { get { return header.index; } }
        public override X15Hash PrevId { get { return header.prevBlockHash; } }
        public override Difficulty<X15Hash> Difficulty { get { return header.difficulty; } }

        protected override Func<X15Hash> IdGenerator { get { return () => new X15Hash(header.ToBinary()); } }

        protected CachedData<Transaction[]> transactionsCache;
        protected virtual Func<Transaction[]> TransactionsGenerator
        {
            get
            {
                return () =>
                {
                    Transaction[] transactions = new Transaction[transferTxs.Length + 1];
                    transactions[0] = coinbaseTxToMiner;
                    for (int i = 0; i < transferTxs.Length; i++)
                        transactions[i + 1] = transferTxs[i];
                    return transactions;
                };
            }
        }
        public override Transaction[] Transactions { get { return transactionsCache.Data; } }

        protected CachedData<MerkleTree<Sha256Sha256Hash>> merkleTreeCache;
        public MerkleTree<Sha256Sha256Hash> MerkleTree { get { return merkleTreeCache.Data; } }

        protected override Func<ReaderWriter, IEnumerable<MainDataInfomation>> StreamInfo
        {
            get
            {
                if (Version == 0)
                    return (msrw) => new MainDataInfomation[]{
                        new MainDataInfomation(typeof(BlockHeader), 0, () => header, (o) => header = (BlockHeader)o),
                        new MainDataInfomation(typeof(CoinbaseTransaction), 0, () => coinbaseTxToMiner, (o) => 
                        {
                            coinbaseTxToMiner = (CoinbaseTransaction)o;
                            if (coinbaseTxToMiner.Version != 0)
                                throw new NotSupportedException();
                        }),
                        new MainDataInfomation(typeof(TransferTransaction[]), 0, null, () => transferTxs, (o) => 
                        {
                            transferTxs = (TransferTransaction[])o;
                            foreach (var transaferTx in transferTxs)
                                if (transaferTx.Version != 0)
                                    throw new NotSupportedException();
                        }),
                    };
                else if (Version == 1)
                    return (msrw) => new MainDataInfomation[]{
                        new MainDataInfomation(typeof(BlockHeader), 0, () => header, (o) => header = (BlockHeader)o),
                        new MainDataInfomation(typeof(CoinbaseTransaction), 0, () => coinbaseTxToMiner, (o) => 
                        {
                            coinbaseTxToMiner = (CoinbaseTransaction)o;
                            if (coinbaseTxToMiner.Version != 0)
                                throw new NotSupportedException();
                        }),
                        new MainDataInfomation(typeof(TransferTransaction[]), 1, null, () => transferTxs, (o) => 
                        {
                            transferTxs = (TransferTransaction[])o;
                            foreach (var transaferTx in transferTxs)
                                if (transaferTx.Version != 1)
                                    throw new NotSupportedException();
                        }),
                    };
                else
                    throw new NotSupportedException();
            }
        }
        public override bool IsVersioned { get { return true; } }
        public override bool IsCorruptionChecked
        {
            get
            {
                if (Version == 0 || Version == 1)
                    return true;
                else
                    throw new NotSupportedException();
            }
        }

        public void UpdateMerkleRootHash()
        {
            header.UpdateMerkleRootHash(MerkleTree.Root);

            idCache.IsModified = true;
        }

        public void UpdateTimestamp(DateTime newTimestamp)
        {
            header.UpdateTimestamp(newTimestamp);

            idCache.IsModified = true;
        }

        public void UpdateNonce(byte[] newNonce)
        {
            header.UpdateNonce(newNonce);

            idCache.IsModified = true;
        }

        public override bool Verify() { throw new NotSupportedException(); }

        public virtual bool Verify(TransactionOutput[][] prevTxOutputss, Func<long, TransactionalBlock> indexToTxBlock)
        {
            if (prevTxOutputss.Length != transferTxs.Length)
                throw new ArgumentException("transfet_txs_and_prev_outputs");

            if (Version != 0 && Version != 1)
                throw new NotSupportedException();

            return VerifyBlockType() && VerifyMerkleRootHash() && VerifyId() && VerifyNumberOfTxs() && VerifyTransferTransaction(prevTxOutputss) && VerifyRewardAndTxFee(prevTxOutputss) & VerifyTimestamp(indexToTxBlock) && VerifyDifficulty(indexToTxBlock);
        }

        public bool VerifyBlockType()
        {
            if (Version != 0 && Version != 1)
                throw new NotSupportedException();

            return this.GetType() == GetBlockType(header.index, Version);
        }

        public bool VerifyMerkleRootHash()
        {
            if (Version != 0 && Version != 1)
                throw new NotSupportedException();

            return header.merkleRootHash.Equals(MerkleTree.Root);
        }

        public bool VerifyId()
        {
            if (Version != 0 && Version != 1)
                throw new NotSupportedException();

            return Id.CompareTo(header.difficulty.Target) <= 0;
        }

        public bool VerifyNumberOfTxs()
        {
            if (Version != 0 && Version != 1)
                throw new NotSupportedException();

            return Transactions.Length <= maxTxs;
        }

        public bool VerifyTransferTransaction(TransactionOutput[][] prevTxOutputss)
        {
            if (prevTxOutputss.Length != transferTxs.Length)
                throw new ArgumentException("transfet_txs_and_prev_outputs");

            if (Version != 0 && Version != 1)
                throw new NotSupportedException();

            for (int i = 0; i < transferTxs.Length; i++)
                if (!transferTxs[i].Verify(prevTxOutputss[i]))
                    return false;
            return true;
        }

        public virtual bool VerifyRewardAndTxFee(TransactionOutput[][] prevTxOutputss)
        {
            if (prevTxOutputss.Length != transferTxs.Length)
                throw new ArgumentException("transfet_txs_and_prev_outputs");

            if (Version != 0 && Version != 1)
                throw new NotSupportedException();

            return GetActualRewardToMinerAndTxFee().rawAmount == GetValidRewardToMinerAndTxFee(prevTxOutputss).rawAmount;
        }

        public bool VerifyTimestamp(Func<long, TransactionalBlock> indexToTxBlock)
        {
            if (Version != 0 && Version != 1)
                throw new NotSupportedException();

            List<DateTime> timestamps = new List<DateTime>();
            for (long i = 1; i < numberOfTimestamps + 1 && header.index - i > 0; i++)
                timestamps.Add(indexToTxBlock(header.index - i).header.timestamp);
            timestamps.Sort();

            if (timestamps.Count == 0)
                return true;

            return (timestamps.Count / 2).Pipe((index) => header.timestamp > (timestamps.Count % 2 == 0 ? timestamps[index - 1] + new TimeSpan((timestamps[index] - timestamps[index - 1]).Ticks / 2) : timestamps[index]));
        }

        public bool VerifyDifficulty(Func<long, TransactionalBlock> indexToTxBlock)
        {
            if (Version != 0 && Version != 1)
                throw new NotSupportedException();

            return header.difficulty.Diff == GetWorkRequired(header.index, indexToTxBlock, 0).Diff;
        }

        public CurrencyUnit GetValidRewardToMiner() { return GetRewardToMiner(header.index, Version); }

        public CurrencyUnit GetValidTxFee(TransactionOutput[][] prevTxOutputss)
        {
            if (prevTxOutputss.Length != transferTxs.Length)
                throw new ArgumentException("transfet_txs_and_prev_outputs");

            long rawTxFee = 0;
            for (int i = 0; i < transferTxs.Length; i++)
                rawTxFee += transferTxs[i].GetFee(prevTxOutputss[i]).rawAmount;
            return new CurrencyUnit(rawTxFee);
        }

        public CurrencyUnit GetValidRewardToMinerAndTxFee(TransactionOutput[][] prevTxOutputss)
        {
            if (prevTxOutputss.Length != transferTxs.Length)
                throw new ArgumentException("transfet_txs_and_prev_outputs");

            return new CurrencyUnit(GetValidRewardToMiner().rawAmount + GetValidTxFee(prevTxOutputss).rawAmount);
        }

        public CurrencyUnit GetActualRewardToMinerAndTxFee()
        {
            long rawTxFee = 0;
            for (int i = 0; i < coinbaseTxToMiner.TxOutputs.Length; i++)
                rawTxFee += coinbaseTxToMiner.TxOutputs[i].Amount.rawAmount;
            return new CurrencyUnit(rawTxFee);
        }

        public static Type GetBlockType(long index, int version)
        {
            if (index < 1)
                throw new ArgumentOutOfRangeException("index_out");

            if (version == 0 || version == 1)
                return index % foundationInterval == 0 ? typeof(FoundationalBlock) : typeof(NormalBlock);
            else
                throw new NotSupportedException("tx_block_not_supported");
        }

        public static CurrencyUnit GetRewardToAll(long index, int version)
        {
            if (index < 1)
                throw new ArgumentOutOfRangeException("index_out");

            if (version == 0 || version == 1)
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

            if (version == 0 || version == 1)
                return new Creacoin(GetRewardToAll(index, version).AmountInCreacoin.Amount * (1.0m - foundationShare));
            else
                throw new NotSupportedException("tx_block_not_supported");
        }

        public static CurrencyUnit GetRewardToFoundation(long index, int version)
        {
            if (index < 1)
                throw new ArgumentOutOfRangeException("index_out");

            if (version == 0 || version == 1)
                return new Creacoin(GetRewardToAll(index, version).AmountInCreacoin.Amount * foundationShare);
            else
                throw new NotSupportedException("tx_block_not_supported");
        }

        public static CurrencyUnit GetRewardToFoundationInterval(long index, int version)
        {
            if (index < 1)
                throw new ArgumentOutOfRangeException("index_out");

            if (version == 0 || version == 1)
                return new Creacoin(GetRewardToFoundation(index, version).AmountInCreacoin.Amount * foundationInterval);
            else
                throw new NotSupportedException("tx_block_not_supported");
        }

        public static Difficulty<X15Hash> GetWorkRequired(long index, Func<long, TransactionalBlock> indexToTxBlock, int version)
        {
            if (index < 1)
                throw new ArgumentOutOfRangeException("index_out");

            if (version == 0 || version == 1)
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

                    X15Hash hash = new X15Hash();

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

                    Difficulty<X15Hash> difficulty = new Difficulty<X15Hash>(hash);

                    return (difficulty.Diff < minDifficulty.Diff ? minDifficulty : difficulty).Pipe((dif) => dif.RaiseNotification("difficulty", 3, difficulty.Diff.ToString()));
                }
            }
            else
                throw new NotSupportedException("tx_block_not_supported");
        }

        private static TransactionalBlock GetBlockTemplate(long index, Sha256Ripemd160Hash minerPubKeyHash, Func<long, TransactionalBlock> indexToTxBlock, int version)
        {
            if (index < 1)
                throw new ArgumentOutOfRangeException("index_out");

            if (version == 0)
            {
                TransactionOutput coinbaseTxOutToMiner = new TransactionOutput();
                coinbaseTxOutToMiner.LoadVersion0(minerPubKeyHash, GetRewardToMiner(index, version));
                CoinbaseTransaction coinbaseTxToMiner = new CoinbaseTransaction();
                coinbaseTxToMiner.LoadVersion0(new TransactionOutput[] { coinbaseTxOutToMiner });

                BlockHeader header = new BlockHeader();
                header.LoadVersion0(index, index == 1 ? new GenesisBlock().Id : indexToTxBlock(index - 1).Id, DateTime.Now, GetWorkRequired(index, indexToTxBlock, version), new byte[] { });

                TransactionalBlock txBlock;
                if (GetBlockType(index, version) == typeof(NormalBlock))
                    txBlock = new NormalBlock().Pipe((normalBlock) => normalBlock.LoadVersion0(header, coinbaseTxToMiner, new TransferTransaction[] { }));
                else
                {
                    TransactionOutput coinbaseTxOutToFoundation = new TransactionOutput();
                    coinbaseTxOutToFoundation.LoadVersion0(foundationPubKeyHash, GetRewardToFoundationInterval(index, version));
                    CoinbaseTransaction coinbaseTxToFoundation = new CoinbaseTransaction();
                    coinbaseTxToFoundation.LoadVersion0(new TransactionOutput[] { coinbaseTxOutToFoundation });

                    txBlock = new FoundationalBlock().Pipe((foundationalBlock) => foundationalBlock.LoadVersion0(header, coinbaseTxToMiner, coinbaseTxToFoundation, new TransferTransaction[] { }));
                }

                txBlock.UpdateMerkleRootHash();

                return txBlock;
            }
            else if (version == 1)
            {
                TransactionOutput coinbaseTxOutToMiner = new TransactionOutput();
                coinbaseTxOutToMiner.LoadVersion0(minerPubKeyHash, GetRewardToMiner(index, version));
                CoinbaseTransaction coinbaseTxToMiner = new CoinbaseTransaction();
                coinbaseTxToMiner.LoadVersion0(new TransactionOutput[] { coinbaseTxOutToMiner });

                BlockHeader header = new BlockHeader();
                header.LoadVersion0(index, index == 1 ? new GenesisBlock().Id : indexToTxBlock(index - 1).Id, DateTime.Now, GetWorkRequired(index, indexToTxBlock, version), new byte[] { });

                TransactionalBlock txBlock;
                if (GetBlockType(index, version) == typeof(NormalBlock))
                    txBlock = new NormalBlock().Pipe((normalBlock) => normalBlock.LoadVersion1(header, coinbaseTxToMiner, new TransferTransaction[] { }));
                else
                {
                    TransactionOutput coinbaseTxOutToFoundation = new TransactionOutput();
                    coinbaseTxOutToFoundation.LoadVersion0(foundationPubKeyHash, GetRewardToFoundationInterval(index, version));
                    CoinbaseTransaction coinbaseTxToFoundation = new CoinbaseTransaction();
                    coinbaseTxToFoundation.LoadVersion0(new TransactionOutput[] { coinbaseTxOutToFoundation });

                    txBlock = new FoundationalBlock().Pipe((foundationalBlock) => foundationalBlock.LoadVersion1(header, coinbaseTxToMiner, coinbaseTxToFoundation, new TransferTransaction[] { }));
                }

                txBlock.UpdateMerkleRootHash();

                return txBlock;
            }
            else
                throw new NotSupportedException("tx_block_not_supported");
        }

        public static TransactionalBlock GetBlockTemplate(long index, Sha256Ripemd160Hash minerPubKeyHash, Func<long, TransactionalBlock> indexToTxBlock)
        {
            return GetBlockTemplate(index, minerPubKeyHash, indexToTxBlock, 1);
        }
    }

    public class NormalBlock : TransactionalBlock { }

    //<未実装>再度行う必要のない検証は行わない
    public class FoundationalBlock : TransactionalBlock
    {
        public override void LoadVersion0(BlockHeader _header, CoinbaseTransaction _coinbaseTxToMiner, TransferTransaction[] _transferTxs) { throw new NotSupportedException(); }

        public virtual void LoadVersion0(BlockHeader _header, CoinbaseTransaction _coinbaseTxToMiner, CoinbaseTransaction _coinbaseTxToFoundation, TransferTransaction[] _transferTxs)
        {
            if (_coinbaseTxToFoundation.Version != 0)
                throw new ArgumentException();

            base.LoadVersion0(_header, _coinbaseTxToMiner, _transferTxs);

            LoadCommon(_coinbaseTxToFoundation);
        }

        public override void LoadVersion1(BlockHeader _header, CoinbaseTransaction _coinbaseTxToMiner, TransferTransaction[] _transferTxs) { throw new NotSupportedException(); }

        public virtual void LoadVersion1(BlockHeader _header, CoinbaseTransaction _coinbaseTxToMiner, CoinbaseTransaction _coinbaseTxToFoundation, TransferTransaction[] _transferTxs)
        {
            if (_coinbaseTxToFoundation.Version != 0)
                throw new ArgumentException();

            base.LoadVersion1(_header, _coinbaseTxToMiner, _transferTxs);

            LoadCommon(_coinbaseTxToFoundation);
        }

        private void LoadCommon(CoinbaseTransaction _coinbaseTxToFoundation) { coinbaseTxToFoundation = _coinbaseTxToFoundation; }

        private CoinbaseTransaction _coinbaseTxToFoundation;
        public CoinbaseTransaction coinbaseTxToFoundation
        {
            get { return _coinbaseTxToFoundation; }
            private set
            {
                if (value != _coinbaseTxToFoundation)
                {
                    _coinbaseTxToFoundation = value;
                    idCache.IsModified = true;
                    transactionsCache.IsModified = true;
                    merkleTreeCache.IsModified = true;
                }
            }
        }

        protected override Func<Transaction[]> TransactionsGenerator
        {
            get
            {
                return () =>
                {
                    Transaction[] transactions = new Transaction[transferTxs.Length + 2];
                    transactions[0] = coinbaseTxToMiner;
                    transactions[1] = coinbaseTxToFoundation;
                    for (int i = 0; i < transferTxs.Length; i++)
                        transactions[i + 2] = transferTxs[i];
                    return transactions;
                };
            }
        }

        protected override Func<ReaderWriter, IEnumerable<MainDataInfomation>> StreamInfo
        {
            get
            {
                if (Version == 0)
                    return (msrw) => base.StreamInfo(msrw).Concat(new MainDataInfomation[]{
                        new MainDataInfomation(typeof(CoinbaseTransaction), 0, () => coinbaseTxToFoundation, (o) => 
                        {
                            coinbaseTxToFoundation = (CoinbaseTransaction)o;
                            if (coinbaseTxToFoundation.Version != 0)
                                throw new NotSupportedException();
                        }),
                    });
                else if (Version == 1)
                    return (msrw) => base.StreamInfo(msrw).Concat(new MainDataInfomation[]{
                        new MainDataInfomation(typeof(CoinbaseTransaction), 0, () => coinbaseTxToFoundation, (o) => 
                        {
                            coinbaseTxToFoundation = (CoinbaseTransaction)o;
                            if (coinbaseTxToFoundation.Version != 0)
                                throw new NotSupportedException();
                        }),
                    });
                else
                    throw new NotSupportedException();
            }
        }

        public override bool Verify(TransactionOutput[][] prevTxOutputss, Func<long, TransactionalBlock> indexToTxBlock)
        {
            if (Version != 0 && Version != 1)
                throw new NotSupportedException();

            return base.Verify(prevTxOutputss, indexToTxBlock) && VerifyCoinbaseTxToFoundationPubKey();
        }

        public bool VerifyCoinbaseTxToFoundationPubKey()
        {
            if (Version != 0 && Version != 1)
                throw new NotSupportedException();

            return coinbaseTxToFoundation.TxOutputs.All((e) => e.ReceiverPubKeyHash.Equals(foundationPubKeyHash));
        }

        public override bool VerifyRewardAndTxFee(TransactionOutput[][] prevTxOutputss)
        {
            if (!base.VerifyRewardAndTxFee(prevTxOutputss))
                return false;

            return GetActualRewardToFoundation().rawAmount == GetValidRewardToFoundation().rawAmount;
        }

        public CurrencyUnit GetValidRewardToFoundation() { return GetRewardToFoundationInterval(header.index, Version); }

        public CurrencyUnit GetActualRewardToFoundation()
        {
            long rawTxFee = 0;
            for (int i = 0; i < coinbaseTxToFoundation.TxOutputs.Length; i++)
                rawTxFee += coinbaseTxToFoundation.TxOutputs[i].Amount.rawAmount;
            return new CurrencyUnit(rawTxFee);
        }
    }

    #endregion

    public class TransactionCollection : SHAREDDATA
    {
        public TransactionCollection()
            : base(null)
        {
            transactions = new List<Transaction>();
            transactionsCache = new CachedData<Transaction[]>(() =>
            {
                lock (transactionsLock)
                    return transactions.ToArray();
            });
        }

        private readonly object transactionsLock = new object();
        private List<Transaction> transactions;
        private readonly CachedData<Transaction[]> transactionsCache;
        public Transaction[] Transactions { get { return transactionsCache.Data; } }

        public event EventHandler<Transaction> TransactionAdded = delegate { };
        public event EventHandler<Transaction> TransactionRemoved = delegate { };

        public bool Contains(Sha256Sha256Hash id)
        {
            lock (transactionsLock)
                return transactions.FirstOrDefault((elem) => elem.Id.Equals(id)) != null;
        }

        public bool AddTransaction(Transaction transaction)
        {
            lock (transactionsLock)
            {
                if (transactions.Contains(transaction))
                    return false;

                this.ExecuteBeforeEvent(() =>
                {
                    transactions.Add(transaction);
                    transactionsCache.IsModified = true;
                }, transaction, TransactionAdded);

                return true;
            }
        }

        public bool RemoveTransaction(Transaction transaction)
        {
            lock (transactionsLock)
            {
                if (!transactions.Contains(transaction))
                    return false;

                this.ExecuteBeforeEvent(() =>
                {
                    transactions.Remove(transaction);
                    transactionsCache.IsModified = true;
                }, transaction, TransactionRemoved);

                return true;
            }
        }

        protected override Func<ReaderWriter, IEnumerable<MainDataInfomation>> StreamInfo
        {
            get { throw new NotImplementedException(); }
        }
    }

    public class Mining
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
                    TransactionalBlock txBlockCopy = txBlock;

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

        private TransactionalBlock txBlock;
        private AutoResetEvent are;

        public bool isStarted { get; private set; }

        public event EventHandler<TransactionalBlock> FoundNonce = delegate { };

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

        public void NewMiningBlock(TransactionalBlock newTxBlock)
        {
            txBlock = newTxBlock;

            are.Set();
        }
    }

    //2014/08/18
    //何とかしてデータ操作部分を分離できないか？
    //データ操作の汎用的な仕組みは作れないか？
    public class BlockChain : SHAREDDATA
    {
        public BlockChain() : base(null) { }

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

            blockGroups = new SortedDictionary<long, List<TransactionalBlock>>();
            mainBlocks = new SortedDictionary<long, TransactionalBlock>();
            isVerifieds = new Dictionary<X15Hash, bool>();
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

            Dictionary<Sha256Ripemd160Hash, List<Utxo>> utxosDict = new Dictionary<Sha256Ripemd160Hash, List<Utxo>>();
            byte[] utxosBytes = utxoDatabase.GetData();
            if (utxosBytes.Length != 0)
            {
                int addressLength = new Sha256Ripemd160Hash().SizeByte;
                int utxoLength = new Utxo().ToBinary().Length;
                int pointer = 0;

                int length1 = BitConverter.ToInt32(utxosBytes, pointer);

                pointer += 4;

                for (int i = 0; i < length1; i++)
                {
                    byte[] addressBytes = new byte[addressLength];
                    Array.Copy(utxosBytes, pointer, addressBytes, 0, addressLength);

                    pointer += addressLength;

                    Sha256Ripemd160Hash address = new Sha256Ripemd160Hash();
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

            utxos = new Utxos(utxosDict);

            addedUtxosInMemory = new List<Dictionary<Sha256Ripemd160Hash, List<Utxo>>>();
            removedUtxosInMemory = new List<Dictionary<Sha256Ripemd160Hash, List<Utxo>>>();

            Dictionary<Sha256Ripemd160Hash, List<AddressEventData>> addressEventDataDict = new Dictionary<Sha256Ripemd160Hash, List<AddressEventData>>();
            byte[] addressEventDatasBytes = addressEventDatabase.GetData();
            if (addressEventDatasBytes.Length != 0)
            {
                int addressLength = new Sha256Ripemd160Hash().SizeByte;
                int addressEventDataLength = new AddressEventData().ToBinary().Length;
                int pointer = 0;

                int length1 = BitConverter.ToInt32(addressEventDatasBytes, pointer);

                pointer += 4;

                for (int i = 0; i < length1; i++)
                {
                    byte[] addressBytes = new byte[addressLength];
                    Array.Copy(addressEventDatasBytes, pointer, addressBytes, 0, addressLength);

                    pointer += addressLength;

                    Sha256Ripemd160Hash address = new Sha256Ripemd160Hash();
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

            addressEventDatas = new AddressEventDatas(addressEventDataDict);
            addressEvents = new Dictionary<AddressEvent, Tuple<CurrencyUnit, CurrencyUnit>>();

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
        private SortedDictionary<long, List<TransactionalBlock>> blockGroups;
        //未保存の主ブロックの集まり
        //保存したら削除しなければならない
        //鍵：bIndex（ブロックの高さ）
        private SortedDictionary<long, TransactionalBlock> mainBlocks;
        //検証したら結果を格納する
        //<未実装>保存したら（或いは、参照される可能性が低くなったら）削除しなければならない
        private Dictionary<X15Hash, bool> isVerifieds;

        private long cacheBgIndex;
        //bgPosition1（ブロック群のファイル上の位置）
        //bgPosition2（ブロック群の何番目のブロックか）
        private long cacheBgPosition1;
        //性能向上のためのブロック群の一時的な保持
        //必要なものだけ逆直列化する
        private byte[][] cacheBgBytes;
        private TransactionalBlock[] cacheBg;

        //更新された主ブロックがこれ以上溜まると1度保存が試行される
        private int numOfMainBlocksWhenSaveNext;

        //bngIndex（ブロック節群の番号） = bIndex / blockNodesGroupDiv
        //保存されているブロック節群の中で最新のもの
        private long currentBngIndex;
        private BlockNodesGroup currentBng;
        //性能向上のためのブロック節群の一時的な保持
        private long cacheBngIndex;
        private BlockNodesGroup cacheBng;

        private Utxos utxos;
        private List<Dictionary<Sha256Ripemd160Hash, List<Utxo>>> addedUtxosInMemory;
        private List<Dictionary<Sha256Ripemd160Hash, List<Utxo>>> removedUtxosInMemory;
        private Dictionary<Sha256Ripemd160Hash, List<Utxo>> currentAddedUtxos;
        private Dictionary<Sha256Ripemd160Hash, List<Utxo>> currentRemovedUtxos;

        private AddressEventDatas addressEventDatas;
        private Dictionary<AddressEvent, Tuple<CurrencyUnit, CurrencyUnit>> addressEvents;

        private BlockChainDatabase bcDatabase;
        private BlockNodesGroupDatabase bngDatabase;
        private BlockGroupDatabase bgDatabase;
        private UtxoDatabase utxoDatabase;
        private AddressEventDatabase addressEventDatabase;

        private GenesisBlock genesisBlock = new GenesisBlock();

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

        private TransactionalBlock GetSavedOrCachedBlock(long bgIndex, long bgPosition1, long bgPosition2)
        {
            Func<TransactionalBlock> _GetBlockFromCache = () =>
            {
                byte[] txBlockTypeBytes = new byte[4];
                byte[] txBlockBytes = new byte[cacheBgBytes[bgPosition2].Length - 4];

                Array.Copy(cacheBgBytes[bgPosition2], 0, txBlockTypeBytes, 0, txBlockTypeBytes.Length);
                Array.Copy(cacheBgBytes[bgPosition2], 4, txBlockBytes, 0, txBlockBytes.Length);

                TransactionType txType = (TransactionType)BitConverter.ToInt32(txBlockTypeBytes, 0);

                TransactionalBlock txBlock;
                if (txType == TransactionType.normal)
                    txBlock = new NormalBlock();
                else if (txType == TransactionType.foundational)
                    txBlock = new FoundationalBlock();
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
                    cacheBg = new TransactionalBlock[cacheBgBytes.Length];
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

        private TransactionalBlock[] GetBlocksAtBIndex(long bIndex)
        {
            List<TransactionalBlock> blocks = new List<TransactionalBlock>();

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

        private TransactionalBlock GetMainBlockAtBIndex(long bIndex)
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

        public TransactionalBlock[] GetHeadBlocks()
        {
            if (!isInitialized)
                throw new InvalidOperationException("not_initialized");
            if (head == 0)
                throw new ArgumentOutOfRangeException("blk_genesis_bindex");

            return GetBlocksAtBIndex(head);
        }

        public TransactionalBlock GetHeadMainBlock()
        {
            if (!isInitialized)
                throw new InvalidOperationException("not_initialized");
            if (head == 0)
                throw new ArgumentOutOfRangeException("blk_genesis_bindex");

            return GetMainBlockAtBIndex(head);
        }

        public TransactionalBlock[] GetBlocks(long bIndex)
        {
            if (!isInitialized)
                throw new InvalidOperationException("not_initialized");
            if (bIndex == 0)
                throw new ArgumentOutOfRangeException("blk_genesis_bindex");

            return GetBlocksAtBIndex(bIndex);
        }

        public TransactionalBlock GetMainBlock(long bIndex)
        {
            if (!isInitialized)
                throw new InvalidOperationException("not_initialized");
            if (bIndex == 0)
                throw new ArgumentOutOfRangeException("blk_genesis_bindex");
            if (bIndex > head)
                throw new ArgumentException("blk_bindex");

            return GetMainBlockAtBIndex(bIndex);
        }

        public void AddBlock(TransactionalBlock txBlock)
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

            List<TransactionalBlock> list;
            if (blockGroups.ContainsKey(txBlock.header.index))
                list = blockGroups[txBlock.header.index];
            else
                blockGroups.Add(txBlock.header.index, list = new List<TransactionalBlock>());

            list.Add(txBlock);

            //<未改良>若干無駄がある
            TransactionalBlock target = txBlock;
            TransactionalBlock main = null;
            while (true)
            {
                //<未改良>連続した番号のブロックを一気に取得する
                TransactionalBlock next = GetBlocks(target.header.index + 1).Where((e) => e.header.prevBlockHash.Equals(target.Id)).FirstOrDefault();
                if (next != null)
                    target = next;
                else
                    break;
            }

            double branchCumulativeDifficulty = 0.0;
            double mainCumulativeDifficulty = 0.0;
            Stack<TransactionalBlock> stack = new Stack<TransactionalBlock>();
            Dictionary<Sha256Ripemd160Hash, List<Utxo>> addedBranchUtxos = new Dictionary<Sha256Ripemd160Hash, List<Utxo>>();
            Dictionary<Sha256Ripemd160Hash, List<Utxo>> removedBranchUtxos = new Dictionary<Sha256Ripemd160Hash, List<Utxo>>();
            if (target.header.index > head)
            {
                while (target.header.index > head)
                {
                    branchCumulativeDifficulty += target.header.difficulty.Diff;
                    stack.Push(target);

                    if (target.header.index == 1)
                        break;

                    //<未改良>連続した番号のブロックを一気に取得する
                    TransactionalBlock prev = GetBlocks(target.header.index - 1).Where((e) => e.Id.Equals(target.header.prevBlockHash)).FirstOrDefault();
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
                    Dictionary<AddressEvent, bool> balanceUpdatedFlag1 = new Dictionary<AddressEvent, bool>();
                    Dictionary<AddressEvent, long?> balanceUpdatedBefore = new Dictionary<AddressEvent, long?>();

                    UpdateBalanceBefore(balanceUpdatedFlag1, balanceUpdatedBefore);

                    foreach (var newBlock in stack)
                    {
                        bool isValid;
                        if (isVerifieds.ContainsKey(newBlock.Id)) //常に偽が返るはず
                            isValid = isVerifieds[newBlock.Id];
                        else
                            isVerifieds.Add(newBlock.Id, isValid = VerifyBlock(newBlock, addedUtxosInMemory.Concat(new List<Dictionary<Sha256Ripemd160Hash, List<Utxo>>>() { currentAddedUtxos }), removedUtxosInMemory.Concat(new List<Dictionary<Sha256Ripemd160Hash, List<Utxo>>>() { currentRemovedUtxos })));

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
                        TransactionalBlock prev = GetMainBlock(main.header.index - 1);
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
                    TransactionalBlock prev = GetBlocks(target.header.index - 1).Where((e) => e.Id.Equals(target.header.prevBlockHash)).FirstOrDefault();
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
                    TransactionalBlock validHead = null;
                    foreach (var newBlock in stack)
                    {
                        bool isValid;
                        if (isVerifieds.ContainsKey(newBlock.Id))
                            isValid = isVerifieds[newBlock.Id];
                        else
                            isVerifieds.Add(newBlock.Id, isValid = VerifyBlock(newBlock, addedUtxosInMemory.Concat(new List<Dictionary<Sha256Ripemd160Hash, List<Utxo>>>() { currentAddedUtxos, addedBranchUtxos }), removedUtxosInMemory.Concat(new List<Dictionary<Sha256Ripemd160Hash, List<Utxo>>>() { currentRemovedUtxos, removedBranchUtxos })));

                        if (!isValid)
                            break;

                        cumulativeDifficulty += newBlock.header.difficulty.Diff;
                        validHead = newBlock;

                        GoForwardUtxosInMemory(newBlock, addedBranchUtxos, removedBranchUtxos);
                    }

                    if (cumulativeDifficulty > mainCumulativeDifficulty)
                    {
                        Dictionary<AddressEvent, bool> balanceUpdatedFlag1 = new Dictionary<AddressEvent, bool>();
                        Dictionary<AddressEvent, long?> balanceUpdatedBefore = new Dictionary<AddressEvent, long?>();

                        UpdateBalanceBefore(balanceUpdatedFlag1, balanceUpdatedBefore);

                        TransactionalBlock fork = GetHeadMainBlock();
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
                SortedDictionary<long, List<TransactionalBlock>> tobeSavedBlockss = GetToBeSavedBlockss();

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

                SortedDictionary<long, List<TransactionalBlock>> newBlockGroups = new SortedDictionary<long, List<TransactionalBlock>>();
                foreach (var blockGroup in blockGroups)
                    if (blockGroup.Key >= head - discardOldBlock)
                        newBlockGroups.Add(blockGroup.Key, blockGroup.Value);
                blockGroups = newBlockGroups;

                SortedDictionary<long, TransactionalBlock> newMainBlocks = new SortedDictionary<long, TransactionalBlock>();
                foreach (var mainBlock in mainBlocks)
                    if (mainBlock.Key > last.Value)
                        newMainBlocks.Add(mainBlock.Key, mainBlock.Value);
                mainBlocks = newMainBlocks;

                numOfMainBlocksWhenSaveNext = numOfMainBlocksWhenSave;
            }
        }

        private void GoForwardAddressEventdata(TransactionalBlock txBlock)
        {
            foreach (var txi in txBlock.Transactions.Select((tx, i) => new { tx, i }))
            {
                for (int i = 0; i < txi.tx.TxInputs.Length; i++)
                    addressEventDatas.Remove(new Sha256Ripemd160Hash(txi.tx.TxInputs[i].SenderPubKey.pubKey), txi.tx.TxInputs[i].PrevTxBlockIndex, txi.tx.TxInputs[i].PrevTxIndex, txi.tx.TxInputs[i].PrevTxOutputIndex);
                for (int i = 0; i < txi.tx.TxOutputs.Length; i++)
                    addressEventDatas.Add(txi.tx.TxOutputs[i].ReceiverPubKeyHash, new AddressEventData(txBlock.header.index, txi.i, i, txi.tx.TxOutputs[i].Amount));
            }
        }

        private void GoBackwardAddressEventData(TransactionalBlock txBlock)
        {
            foreach (var txi in txBlock.Transactions.Select((tx, i) => new { tx, i }))
            {
                for (int i = 0; i < txi.tx.TxInputs.Length; i++)
                    addressEventDatas.Add(new Sha256Ripemd160Hash(txi.tx.TxInputs[i].SenderPubKey.pubKey), new AddressEventData(txi.tx.TxInputs[i].PrevTxBlockIndex, txi.tx.TxInputs[i].PrevTxIndex, txi.tx.TxInputs[i].PrevTxOutputIndex, GetMainBlock(txi.tx.TxInputs[i].PrevTxBlockIndex).Transactions[txi.tx.TxInputs[i].PrevTxIndex].TxOutputs[txi.tx.TxInputs[i].PrevTxOutputIndex].Amount));
                for (int i = 0; i < txi.tx.TxOutputs.Length; i++)
                    addressEventDatas.Remove(txi.tx.TxOutputs[i].ReceiverPubKeyHash, txBlock.header.index, txi.i, i);
            }
        }

        public void AddAddressEvent(AddressEvent addressEvent)
        {
            if (addressEvents.ContainsKey(addressEvent))
                throw new ArgumentException("already_added");

            List<AddressEventData> listAddressEventData = null;
            if (addressEventDatas.ContainsAddress(addressEvent.address))
                listAddressEventData = addressEventDatas.GetAddressEventDatas(addressEvent.address);
            else
            {
                List<Utxo> listUtxo = utxos.ContainsAddress(addressEvent.address) ? utxos.GetAddressUtxos(addressEvent.address) : new List<Utxo>() { };

                foreach (var added in addedUtxosInMemory.Concat(new List<Dictionary<Sha256Ripemd160Hash, List<Utxo>>>() { currentAddedUtxos }))
                    if (added != null && added.ContainsKey(addressEvent.address))
                        foreach (var utxo in added[addressEvent.address])
                            listUtxo.Add(utxo);

                foreach (var removed in removedUtxosInMemory.Concat(new List<Dictionary<Sha256Ripemd160Hash, List<Utxo>>>() { currentRemovedUtxos }))
                    if (removed != null && removed.ContainsKey(addressEvent.address))
                        foreach (var utxo in removed[addressEvent.address])
                        {
                            Utxo removedUtxo = listUtxo.FirstOrDefault((elem) => elem.blockIndex == utxo.blockIndex && elem.txIndex == utxo.txIndex && elem.txOutIndex == utxo.txOutIndex);
                            if (removedUtxo == null)
                                throw new InvalidOperationException("not_found");
                            listUtxo.Remove(removedUtxo);
                        }

                listAddressEventData = new List<AddressEventData>();
                foreach (var utxo in listUtxo)
                    listAddressEventData.Add(new AddressEventData(utxo.blockIndex, utxo.txIndex, utxo.txOutIndex, GetMainBlock(utxo.blockIndex).Transactions[utxo.txIndex].TxOutputs[utxo.txOutIndex].Amount));

                addressEventDatas.Add(addressEvent.address, listAddressEventData);
            }

            Tuple<CurrencyUnit, CurrencyUnit> balance = CalculateBalance(listAddressEventData);

            addressEvents.Add(addressEvent, balance);

            addressEvent.RaiseBalanceUpdated(balance);
            addressEvent.RaiseUsableBalanceUpdated(balance.Item1);
            addressEvent.RaiseUnusableBalanceUpdated(balance.Item2);
        }

        public AddressEvent RemoveAddressEvent(Sha256Ripemd160Hash address)
        {
            AddressEvent addressEvent;
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

        private void UpdateBalanceBefore(Dictionary<AddressEvent, bool> balanceUpdatedFlag1, Dictionary<AddressEvent, long?> balanceUpdatedBefore)
        {
            foreach (var addressEvent in addressEvents)
            {
                AddressEventData addressEventData = addressEventDatas.GetAddressEventDatas(addressEvent.Key.address).LastOrDefault();
                balanceUpdatedFlag1.Add(addressEvent.Key, addressEventData != null && addressEventData.blockIndex + unusableConformation > head);
                balanceUpdatedBefore.Add(addressEvent.Key, addressEventData == null ? null : (long?)addressEventData.blockIndex);
            }
        }

        private void UpdateBalanceAfter(Dictionary<AddressEvent, bool> balanceUpdatedFlag1, Dictionary<AddressEvent, long?> balanceUpdatedBefore)
        {
            bool flag = false;

            List<AddressEvent> addressEventsCopy = new List<AddressEvent>(addressEvents.Keys);
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

        private void GoForwardUtxosCurrent(TransactionalBlock txBlock)
        {
            if (txBlock.header.index == 1 || txBlock.header.index % utxosInMemoryDiv == 0)
            {
                if (currentAddedUtxos != null)
                {
                    addedUtxosInMemory.Add(currentAddedUtxos);
                    removedUtxosInMemory.Add(currentRemovedUtxos);
                }

                currentAddedUtxos = new Dictionary<Sha256Ripemd160Hash, List<Utxo>>();
                currentRemovedUtxos = new Dictionary<Sha256Ripemd160Hash, List<Utxo>>();
            }

            GoForwardUtxosInMemory(txBlock, currentAddedUtxos, currentRemovedUtxos);
        }

        private void GoBackwardUtxosCurrent(TransactionalBlock txBlock)
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

        private void UpdateUtxosTemp(Dictionary<Sha256Ripemd160Hash, List<Utxo>> utxos1, Dictionary<Sha256Ripemd160Hash, List<Utxo>> utxos2, Sha256Ripemd160Hash address, long blockIndex, int txIndex, int txOutputIndex)
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

        private void GoForwardUtxosInMemory(TransactionalBlock txBlock, Dictionary<Sha256Ripemd160Hash, List<Utxo>> addedUtxos, Dictionary<Sha256Ripemd160Hash, List<Utxo>> removedUtxos)
        {
            foreach (var txi in txBlock.Transactions.Select((tx, i) => new { tx, i }))
            {
                for (int i = 0; i < txi.tx.TxInputs.Length; i++)
                    UpdateUtxosTemp(addedUtxos, removedUtxos, new Sha256Ripemd160Hash(txi.tx.TxInputs[i].SenderPubKey.pubKey), txi.tx.TxInputs[i].PrevTxBlockIndex, txi.tx.TxInputs[i].PrevTxIndex, txi.tx.TxInputs[i].PrevTxOutputIndex);
                for (int i = 0; i < txi.tx.TxOutputs.Length; i++)
                    UpdateUtxosTemp(removedUtxos, addedUtxos, txi.tx.TxOutputs[i].ReceiverPubKeyHash, txBlock.header.index, txi.i, i);
            }
        }

        private void GoBackwardUtxosInMemory(TransactionalBlock txBlock, Dictionary<Sha256Ripemd160Hash, List<Utxo>> addedUtxos, Dictionary<Sha256Ripemd160Hash, List<Utxo>> removedUtxos)
        {
            foreach (var txi in txBlock.Transactions.Select((tx, i) => new { tx, i }))
            {
                for (int i = 0; i < txi.tx.TxInputs.Length; i++)
                    UpdateUtxosTemp(removedUtxos, addedUtxos, new Sha256Ripemd160Hash(txi.tx.TxInputs[i].SenderPubKey.pubKey), txi.tx.TxInputs[i].PrevTxBlockIndex, txi.tx.TxInputs[i].PrevTxIndex, txi.tx.TxInputs[i].PrevTxOutputIndex);
                for (int i = 0; i < txi.tx.TxOutputs.Length; i++)
                    UpdateUtxosTemp(addedUtxos, removedUtxos, txi.tx.TxOutputs[i].ReceiverPubKeyHash, txBlock.header.index, txi.i, i);
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

                int addressEventDataLength = new AddressEventData().ToBinary().Length;

                foreach (var addressEventDatasDict in addressEventDatas.addressEventDatas)
                {
                    ms.Write(addressEventDatasDict.Key.hash, 0, addressEventDatasDict.Key.SizeByte);
                    ms.Write(BitConverter.GetBytes(addressEventDatasDict.Value.Count), 0, 4);
                    foreach (var addressEventData in addressEventDatasDict.Value)
                        ms.Write(addressEventData.ToBinary(), 0, addressEventDataLength);
                }

                addressEventDatabase.UpdateData(ms.ToArray());
            }

            using (MemoryStream ms = new MemoryStream())
            {
                ms.Write(BitConverter.GetBytes(utxos.utxos.Count), 0, 4);

                int utxoLength = new Utxo().ToBinary().Length;

                foreach (var utxosDict in utxos.utxos)
                {
                    ms.Write(utxosDict.Key.hash, 0, utxosDict.Key.SizeByte);
                    ms.Write(BitConverter.GetBytes(utxosDict.Value.Count), 0, 4);
                    foreach (var utxo in utxosDict.Value)
                        ms.Write(utxo.ToBinary(), 0, utxoLength);
                }

                utxoDatabase.UpdateData(ms.ToArray());
            }

            bcDatabase.UpdateData(ToBinary());
        }

        private SortedDictionary<long, List<TransactionalBlock>> GetToBeSavedBlockss()
        {
            SortedDictionary<long, List<TransactionalBlock>> tobeSavedBlockss = new SortedDictionary<long, List<TransactionalBlock>>();

            foreach (var mainBlock in mainBlocks)
            {
                if (blockGroups.ContainsKey(mainBlock.Key) && blockGroups[mainBlock.Key].Contains(mainBlock.Value))
                {
                    long bgIndex = mainBlock.Value.header.index / blockGroupDiv;

                    if (tobeSavedBlockss.ContainsKey(bgIndex))
                        tobeSavedBlockss[bgIndex].Add(mainBlock.Value);
                    else
                        tobeSavedBlockss.Add(bgIndex, new List<TransactionalBlock>() { mainBlock.Value });
                }
            }

            return tobeSavedBlockss;
        }

        private void SaveBgAndBng(SortedDictionary<long, List<TransactionalBlock>> tobeSavedBlockss, long last)
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

                    bgDatas.Add(BitConverter.GetBytes((int)(tobeSavedBlock is NormalBlock ? TransactionType.normal : TransactionType.foundational)).Combine(tobeSavedBlock.ToBinary()));
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
                        TransactionalBlock block = GetSavedOrCachedBlock(bgIndex, blockNode.position1, blockNode.position2);
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

        public bool VerifyBlock(TransactionalBlock txBlock, IEnumerable<Dictionary<Sha256Ripemd160Hash, List<Utxo>>> addeds, IEnumerable<Dictionary<Sha256Ripemd160Hash, List<Utxo>>> removeds)
        {
            if (!isInitialized)
                throw new InvalidOperationException("not_initialized");

            TransactionOutput[][] prevTxOutputs = new TransactionOutput[txBlock.transferTxs.Length][];
            foreach (var transrferTx in txBlock.transferTxs.Select((v, i) => new { v, i }))
            {
                prevTxOutputs[transrferTx.i] = new TransactionOutput[transrferTx.v.txInputs.Length];
                foreach (var txInput in transrferTx.v.txInputs.Select((v, i) => new { v, i }))
                {
                    Sha256Ripemd160Hash address = new Sha256Ripemd160Hash(txInput.v.SenderPubKey.pubKey);
                    if (utxos.Contains(address, txInput.v.PrevTxBlockIndex, txInput.v.PrevTxIndex, txInput.v.PrevTxOutputIndex))
                    {
                        foreach (var removed in removeds)
                            if (removed.ContainsKey(address))
                                foreach (var removedUtxo in removed[address])
                                    if (removedUtxo.blockIndex == txInput.v.PrevTxBlockIndex && removedUtxo.txIndex == txInput.v.PrevTxIndex && removedUtxo.txOutIndex == txInput.v.PrevTxOutputIndex)
                                        return false;
                        prevTxOutputs[transrferTx.i][txInput.i] = GetMainBlock(txInput.v.PrevTxBlockIndex).Transactions[txInput.v.PrevTxIndex].TxOutputs[txInput.v.PrevTxOutputIndex];
                    }
                    else
                    {
                        foreach (var added in addeds)
                            if (added != null && added.ContainsKey(address))
                                foreach (var addedUtxo in added[address])
                                    if (addedUtxo.blockIndex == txInput.v.PrevTxBlockIndex && addedUtxo.txIndex == txInput.v.PrevTxIndex && addedUtxo.txOutIndex == txInput.v.PrevTxOutputIndex)
                                        prevTxOutputs[transrferTx.i][txInput.i] = GetMainBlock(txInput.v.PrevTxBlockIndex).Transactions[txInput.v.PrevTxIndex].TxOutputs[txInput.v.PrevTxOutputIndex];
                        if (prevTxOutputs[transrferTx.i][txInput.i] == null)
                            return false;
                        foreach (var removed in removeds)
                            if (removed != null && removed.ContainsKey(address))
                                foreach (var removedUtxo in removed[address])
                                    if (removedUtxo.blockIndex == txInput.v.PrevTxBlockIndex && removedUtxo.txIndex == txInput.v.PrevTxIndex && removedUtxo.txOutIndex == txInput.v.PrevTxOutputIndex)
                                        return false;
                    }
                }
            }

            txBlock.Verify(prevTxOutputs, (index) => GetMainBlock(index));

            return true;
        }
    }

    public class AddressEventData : SHAREDDATA
    {
        public AddressEventData() { amount = CurrencyUnit.Zero; }

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

    public class AddressEventDatas
    {
        public AddressEventDatas() { }

        public AddressEventDatas(Dictionary<Sha256Ripemd160Hash, List<AddressEventData>> _addressEventData) { addressEventDatas = _addressEventData; }

        public Dictionary<Sha256Ripemd160Hash, List<AddressEventData>> addressEventDatas { get; private set; }

        public void Add(Sha256Ripemd160Hash address, AddressEventData addressEventData)
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

        public void Add(Sha256Ripemd160Hash address, List<AddressEventData> list)
        {
            if (addressEventDatas.Keys.Contains(address))
                throw new InvalidOperationException("already_existed");

            addressEventDatas.Add(address, list);
        }

        public void Remove(Sha256Ripemd160Hash address, long blockIndex, int txIndex, int txOutIndex)
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

        public void Remove(Sha256Ripemd160Hash address)
        {
            if (!addressEventDatas.Keys.Contains(address))
                throw new InvalidOperationException("not_existed");

            addressEventDatas.Remove(address);
        }

        public void Update(Dictionary<Sha256Ripemd160Hash, List<AddressEventData>> addedUtxos, Dictionary<Sha256Ripemd160Hash, List<AddressEventData>> removedUtxos)
        {
            foreach (var addedUtxos2 in addedUtxos)
                foreach (var addedUtxo in addedUtxos2.Value)
                    Add(addedUtxos2.Key, addedUtxo);
            foreach (var removedUtxos2 in removedUtxos)
                foreach (var removedUtxo in removedUtxos2.Value)
                    Remove(removedUtxos2.Key, removedUtxo.blockIndex, removedUtxo.txIndex, removedUtxo.txOutIndex);
        }

        public bool Contains(Sha256Ripemd160Hash address, long blockIndex, int txIndex, int txOutIndex)
        {
            if (!addressEventDatas.Keys.Contains(address))
                return false;

            List<AddressEventData> list = addressEventDatas[address];

            return list.FirstOrDefault((elem) => elem.blockIndex == blockIndex && elem.txIndex == txIndex && elem.txOutIndex == txOutIndex) != null;
        }

        public bool ContainsAddress(Sha256Ripemd160Hash address) { return addressEventDatas.Keys.Contains(address); }

        public List<AddressEventData> GetAddressEventDatas(Sha256Ripemd160Hash address)
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

    public class Utxos
    {
        public Utxos() { }

        public Utxos(Dictionary<Sha256Ripemd160Hash, List<Utxo>> _utxos) { utxos = _utxos; }

        public Dictionary<Sha256Ripemd160Hash, List<Utxo>> utxos { get; private set; }

        public void Add(Sha256Ripemd160Hash address, Utxo utxo)
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

        public void Remove(Sha256Ripemd160Hash address, long blockIndex, int txIndex, int txOutIndex)
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

        public void Update(Dictionary<Sha256Ripemd160Hash, List<Utxo>> addedUtxos, Dictionary<Sha256Ripemd160Hash, List<Utxo>> removedUtxos)
        {
            foreach (var addedUtxos2 in addedUtxos)
                foreach (var addedUtxo in addedUtxos2.Value)
                    Add(addedUtxos2.Key, addedUtxo);
            foreach (var removedUtxos2 in removedUtxos)
                foreach (var removedUtxo in removedUtxos2.Value)
                    Remove(removedUtxos2.Key, removedUtxo.blockIndex, removedUtxo.txIndex, removedUtxo.txOutIndex);
        }

        public bool Contains(Sha256Ripemd160Hash address, long blockIndex, int txIndex, int txOutIndex)
        {
            if (!utxos.Keys.Contains(address))
                return false;

            List<Utxo> list = utxos[address];

            return list.FirstOrDefault((elem) => elem.blockIndex == blockIndex && elem.txIndex == txIndex && elem.txOutIndex == txOutIndex) != null;
        }

        public bool ContainsAddress(Sha256Ripemd160Hash address) { return utxos.Keys.Contains(address); }

        public List<Utxo> GetAddressUtxos(Sha256Ripemd160Hash address)
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
        public BlockNodesGroup() : base(null) { }

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
        protected abstract int version { get; }
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

    public class FirstNodeInfosDatabase : SimpleDatabase
    {
        public FirstNodeInfosDatabase(string _pathBase) : base(_pathBase) { }

        protected override string filenameBase { get { return "nodes" + version.ToString() + ".txt"; } }
        protected override int version { get { return 0; } }

        public string[] GetFirstNodeInfosData() { return Encoding.UTF8.GetString(GetData()).Split(new string[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries); }

        public void UpdateFirstNodeInfosData(string[] nodes) { UpdateData(Encoding.UTF8.GetBytes(string.Join(Environment.NewLine, nodes))); }
    }

    public class AccountHoldersDatabase : SimpleDatabase
    {
        public AccountHoldersDatabase(string _pathBase) : base(_pathBase) { }

        protected override int version { get { return 0; } }

#if TEST
        protected override string filenameBase { get { return "acc_test" + version.ToString(); } }
#else
        protected override string filenameBase { get { return "acc"; } }
#endif
    }

    public class BlockChainDatabase : SimpleDatabase
    {
        public BlockChainDatabase(string _pathBase) : base(_pathBase) { }

        protected override int version { get { return 0; } }

#if TEST
        protected override string filenameBase { get { return "blkchn_test" + version.ToString(); } }
#else
        protected override string filenameBase { get { return "blkchn"; } }
#endif
    }

    public class AddressEventDatabase : SimpleDatabase
    {
        public AddressEventDatabase(string _pathBase) : base(_pathBase) { }

        protected override int version { get { return 0; } }

#if TEST
        protected override string filenameBase { get { return "address_event_test" + version.ToString(); } }
#else
        protected override string filenameBase { get { return "address_event"; } }
#endif
    }

    public class UtxoDatabase : SimpleDatabase
    {
        public UtxoDatabase(string _pathBase) : base(_pathBase) { }

        protected override int version { get { return 0; } }

#if TEST
        protected override string filenameBase { get { return "utxo_test" + version.ToString(); } }
#else
        protected override string filenameBase { get { return "utxo"; } }
#endif
    }

    public class BlockNodesGroupDatabase : DATABASEBASE
    {
        public BlockNodesGroupDatabase(string _pathBase) : base(_pathBase) { }

        protected override int version { get { return 0; } }

#if TEST
        protected override string filenameBase { get { return "blkng_test" + version.ToString(); } }
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

        protected override int version { get { return 0; } }

#if TEST
        protected override string filenameBase { get { return "blkg_test" + version.ToString(); } }
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
}