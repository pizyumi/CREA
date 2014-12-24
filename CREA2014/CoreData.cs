//がをがを～！
//2014/11/03 分割

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace CREA2014
{
    #region データ

    //試験用？
    public class Chat : SHAREDDATA
    {
        public Chat() : base(0) { }

        public void LoadVersion0(string _name, string _message, Ecdsa256PubKey _pubKey)
        {
            this.Version = 0;

            this.Name = _name;
            this.Message = _message;
            this.PubKey = _pubKey;
            this.Id = Guid.NewGuid();
        }

        public string Name { get; private set; }
        public string Message { get; private set; }
        public Guid Id { get; private set; }
        public Ecdsa256PubKey PubKey { get; private set; }
        public Ecdsa256Signature signature { get; private set; }

        protected override Func<ReaderWriter, IEnumerable<MainDataInfomation>> StreamInfo
        {
            get
            {
                if (Version == 0)
                    return (msrw) => new MainDataInfomation[]{
                        new MainDataInfomation(typeof(string), () => this.Name, (o) => this.Name = (string)o),
                        new MainDataInfomation(typeof(string), () => this.Message, (o) => this.Message = (string)o),
                        new MainDataInfomation(typeof(byte[]), 16, () => this.Id.ToByteArray(), (o) => this.Id = new Guid((byte[])o)),
                        new MainDataInfomation(typeof(Ecdsa256PubKey), null, () => this.PubKey, (o) => this.PubKey = (Ecdsa256PubKey)o),
                        new MainDataInfomation(typeof(Ecdsa256Signature), null, () => this.signature, (o) => this.signature = (Ecdsa256Signature)o),
                    };
                else
                    throw new NotSupportedException();
            }
        }

        public override bool IsVersioned { get { return true; } }
        public override bool IsCorruptionChecked { get { return true; } }

        public string Trip
        {
            get
            {
                if (Version != 0)
                    throw new NotSupportedException();

                return PubKey.pubKey.ComputeTrip();
            }
        }

        public string NameWithTrip { get { return Name + Trip; } }

        public Func<ReaderWriter, IEnumerable<MainDataInfomation>> StreamInfoToSign
        {
            get
            {
                if (Version == 0)
                    return (msrw) => new MainDataInfomation[]{
                        new MainDataInfomation(typeof(string), () => this.Name, (o) => this.Name = (string)o),
                        new MainDataInfomation(typeof(string), () => this.Message, (o) => this.Message = (string)o),
                        new MainDataInfomation(typeof(byte[]), 16, () => this.Id.ToByteArray(), (o) => this.Id = new Guid((byte[])o))
                    };
                else
                    throw new NotSupportedException();
            }
        }

        public void Sign(Ecdsa256PrivKey privKey)
        {
            signature = privKey.Sign(ToBinary(StreamInfoToSign)) as Ecdsa256Signature;
        }

        public bool Verify()
        {
            return PubKey.Verify(ToBinary(StreamInfoToSign), signature.signature);
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

        public bool AddChat(Chat chat)
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

        public bool RemoveChat(Chat chat)
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
            byte[] randomBytes = hash.ArrayRandomCache();
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

    public class Creahash : HASHBASE
    {
        public Creahash() : base() { }

        public Creahash(string stringHash) : base(stringHash) { }

        public Creahash(byte[] data) : base(data) { }

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

            byte[] cubehash512 = data.ComputeCubehash512();
            byte[] shavite512 = data.ComputeShavite512();
            byte[] simd512 = data.ComputeSimd512();
            byte[] echo512 = data.ComputeEcho512();

            byte[] fugue512 = data.ComputeFugue512();
            byte[] hamsi512 = data.ComputeHamsi512();

            byte[] shabal512 = data.ComputeShabal512();

            byte[] hash = new byte[32];

            Array.Copy(sha1base64shiftjissha1, hash, 20);

            foreach (var item in new byte[][] { blake512, bmw512, groestl512, skein512, jh512, keccak512, luffa512
                    , cubehash512, shavite512, simd512, echo512, fugue512, hamsi512, shabal512 })
            {
                for (int i = 0; i < 32; i++)
                    hash[i] ^= item[i];
            }

            return hash;
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

        public override bool Verify(byte[] data, byte[] signature)
        {
            try
            {
                return data.VerifyEcdsa(signature, pubKey);
            }
            catch (Exception) { }

            return false;
        }
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
        DSAPRIVKEYBASE iPrivKey { get; }
        DSAPUBKEYBASE iPubKey { get; }

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
                return pubKey.pubKey.ComputeTrip();
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

        public DSAPUBKEYBASE iPubKey { get { return pubKey; } }
        public DSAPRIVKEYBASE iPrivKey { get { return privKey; } }

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
            return new Account().Pipe((account) => account.LoadVersion0(name, description));
        }

        public IPseudonymousAccountHolder CreatePseudonymousAccountHolder(string name)
        {
            return new PseudonymousAccountHolder().Pipe((pseudonymousAccountHolder) => pseudonymousAccountHolder.LoadVersion0(name));
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

        public static string Name = "CREA";
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

        public static string Name = "Yumina";
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

    //2014/12/02 試験済
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

    //2014/12/02 試験済
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

        public Sha256Ripemd160Hash Address { get { return receiverPubKeyHash; } }
        public CurrencyUnit Amount { get { return amount; } }

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

    //2014/12/02 試験済
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

    //2014/12/02 試験済
    public class CoinbaseTransaction : Transaction
    {
        public override void LoadVersion1(TransactionOutput[] _txOutputs) { throw new NotSupportedException(); }

        private const string guidString = "784aee51e677e6469ca2ae0d6c72d60e";
        private Guid guid;
        public override Guid Guid
        {
            get
            {
                if (guid == Guid.Empty)
                    guid = new Guid(guidString);

                return guid;
            }
        }

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

    //2014/12/02 試験済
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

        private const string guidString = "5c5493dff997774db351ae5018844b23";
        private Guid guid;
        public override Guid Guid
        {
            get
            {
                if (guid == Guid.Empty)
                    guid = new Guid(guidString);

                return guid;
            }
        }

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
                //<未改良>予め公開鍵を復元しておく？
                for (int i = 0; i < txInputs.Length; i++)
                    //if (!Secp256k1Utility.Recover<Sha256Hash>(bytesToSign, txInputs[i].SenderSignature.signature).Verify(bytesToSign, txInputs[i].SenderSignature.signature))
                    if (!Secp256k1Utility.RecoverAndVerify<Sha256Hash>(bytesToSign, txInputs[i].SenderSignature.signature))
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
                    if (!new Sha256Ripemd160Hash(txInputs[i].Ecdsa256PubKey.pubKey).Equals(prevTxOutputs[i].Address))
                        return false;
            }
            else if (Version == 1)
            {
                byte[] bytesToSign = GetBytesToSign(prevTxOutputs);

                //<未改良>予め公開鍵を復元しておく？
                for (int i = 0; i < txInputs.Length; i++)
                    if (!new Sha256Ripemd160Hash(Secp256k1Utility.Recover<Sha256Hash>(bytesToSign, txInputs[i].Secp256k1Signature.signature).pubKey).Equals(prevTxOutputs[i].Address))
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
        public Block(int? _version) : base(_version) { idCache = new CachedData<Creahash>(IdGenerator); }

        protected CachedData<Creahash> idCache;
        protected virtual Func<Creahash> IdGenerator { get { return () => new Creahash(ToBinary()); } }
        public virtual Creahash Id { get { return idCache.Data; } }

        public abstract long Index { get; }
        public abstract Creahash PrevId { get; }
        public abstract Difficulty<Creahash> Difficulty { get; }
        public abstract Transaction[] Transactions { get; }

        public virtual bool Verify() { return true; }
    }

    //2014/12/02 試験済
    public class GenesisBlock : Block
    {
        public GenesisBlock() : base(null) { }

        public readonly string genesisWord = "Bitstamp 2014/05/25 BTC/USD High 586.34 BTC to the moooooooon!!";

        public override long Index { get { return 0; } }
        public override Creahash PrevId { get { return null; } }
        public override Difficulty<Creahash> Difficulty { get { return new Difficulty<Creahash>(HASHBASE.FromHash<Creahash>(new byte[] { 0, 127, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255 })); } }
        public override Transaction[] Transactions { get { return new Transaction[] { }; } }

        private const string guidString = "86080fc3b48032489cf8c19e275fc185";
        private Guid guid;
        public override Guid Guid
        {
            get
            {
                if (guid == Guid.Empty)
                    guid = new Guid(guidString);

                return guid;
            }
        }

        protected override Func<ReaderWriter, IEnumerable<MainDataInfomation>> StreamInfo
        {
            get
            {
                return (msrw) => new MainDataInfomation[]{
                    new MainDataInfomation(typeof(string), () => genesisWord, (o) => 
                    {
                        if((string)o != genesisWord)
                            throw new NotSupportedException("genesis_word_mismatch");;
                    }),
                };
            }
        }
    }

    //2014/12/02 試験済
    public class BlockHeader : SHAREDDATA
    {
        public BlockHeader() : base(0) { }

        public void LoadVersion0(long _index, Creahash _prevBlockHash, DateTime _timestamp, Difficulty<Creahash> _difficulty, byte[] _nonce)
        {
            if (_index < 1)
                throw new ArgumentOutOfRangeException("block_header_index_out");
            if (_nonce.Length != nonceLength)
                throw new ArgumentOutOfRangeException("block_header_nonce_out");

            index = _index;
            prevBlockHash = _prevBlockHash;
            timestamp = _timestamp;
            difficulty = _difficulty;
            nonce = _nonce;
        }

        private static readonly int nonceLength = 10;

        public long index { get; private set; }
        public Creahash prevBlockHash { get; private set; }
        public Sha256Sha256Hash merkleRootHash { get; private set; }
        public DateTime timestamp { get; private set; }
        public Difficulty<Creahash> difficulty { get; private set; }
        public byte[] nonce { get; private set; }

        protected override Func<ReaderWriter, IEnumerable<MainDataInfomation>> StreamInfo
        {
            get
            {
                if (Version == 0)
                    return (msrw) => new MainDataInfomation[]{
                        new MainDataInfomation(typeof(long), () => index, (o) => index = (long)o),
                        new MainDataInfomation(typeof(Creahash), null, () => prevBlockHash, (o) => prevBlockHash = (Creahash)o),
                        new MainDataInfomation(typeof(Sha256Sha256Hash), null, () => merkleRootHash, (o) => merkleRootHash = (Sha256Sha256Hash)o),
                        new MainDataInfomation(typeof(DateTime), () => timestamp, (o) => timestamp = (DateTime)o),
                        new MainDataInfomation(typeof(byte[]), 4, () => difficulty.CompactTarget, (o) => difficulty = new Difficulty<Creahash>((byte[])o)),
                        new MainDataInfomation(typeof(byte[]), nonceLength, () => nonce, (o) => nonce = (byte[])o),
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
            if (newNonce.Length != nonceLength)
                throw new ArgumentOutOfRangeException("block_header_nonce_out");

            nonce = newNonce;
        }
    }

    //2014/12/03 試験済
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

#if TEST
        private static readonly long blockGenerationInterval = 1; //[sec]
#else
        private static readonly long blockGenerationInterval = 60; //[sec]
#endif
        private static readonly long cycle = 60 * 60 * 24 * 365; //[sec]=1[year]
        private static readonly int numberOfCycles = 8;
        private static readonly long rewardlessStart = cycle * numberOfCycles; //[sec]=8[year]
        private static readonly decimal rewardReductionRate = 0.8m;
        private static readonly CurrencyUnit initialReward = new Creacoin(1.0m); //[CREA/sec]
        private static readonly CurrencyUnit[] rewards; //[CREA/sec]
        private static readonly decimal foundationShare = 0.1m;
        private static readonly long foundationInterval = 60 * 60 * 24 / blockGenerationInterval; //[block]

        private static readonly long numberOfTimestamps = 11;
        private static readonly long targetTimespan = blockGenerationInterval * 1; //[sec]=60[sec]
        private static readonly long retargetInterval = targetTimespan / blockGenerationInterval; //[block]=1[block]

        private static readonly Difficulty<Creahash> minDifficulty = new Difficulty<Creahash>(HASHBASE.FromHash<Creahash>(new byte[] { 0, 127, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255 }));

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
        public override Creahash PrevId { get { return header.prevBlockHash; } }
        public override Difficulty<Creahash> Difficulty { get { return header.difficulty; } }

        protected override Func<Creahash> IdGenerator { get { return () => new Creahash(header.ToBinary()); } }

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

        //2014/12/03
        //prevTxOutssは全ての取引に対するものを含んでいなければならないことに注意
        //貨幣移動取引のみならず貨幣生成取引に対するもの（貨幣生成取引の場合取引入力は0個であるため空になる筈）も含んでいなければならない
        public virtual bool Verify(TransactionOutput[][] prevTxOutputss, Func<long, TransactionalBlock> indexToTxBlock)
        {
            if (prevTxOutputss.Length != Transactions.Length)
                throw new ArgumentException("txs_and_prev_outputs");

            //if (prevTxOutputss.Length != transferTxs.Length)
            //    throw new ArgumentException("transfet_txs_and_prev_outputs");

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

        //2014/12/03
        //prevTxOutssは全ての取引に対するものを含んでいなければならないことに注意
        //貨幣移動取引のみならず貨幣生成取引に対するもの（貨幣生成取引の場合取引入力は0個であるため空になる筈）も含んでいなければならない
        public bool VerifyTransferTransaction(TransactionOutput[][] prevTxOutputss)
        {
            if (prevTxOutputss.Length != Transactions.Length)
                throw new ArgumentException("txs_and_prev_outputs");

            if (Version != 0 && Version != 1)
                throw new NotSupportedException();

            for (int i = 0; i < Transactions.Length; i++)
                if (Transactions[i] is TransferTransaction && !(Transactions[i] as TransferTransaction).Verify(prevTxOutputss[i]))
                    return false;
            return true;

            //if (prevTxOutputss.Length != transferTxs.Length)
            //    throw new ArgumentException("transfet_txs_and_prev_outputs");

            //if (Version != 0 && Version != 1)
            //    throw new NotSupportedException();

            //for (int i = 0; i < transferTxs.Length; i++)
            //    if (!transferTxs[i].Verify(prevTxOutputss[i]))
            //        return false;
            //return true;
        }

        //2014/12/03
        //prevTxOutssは全ての取引に対するものを含んでいなければならないことに注意
        //貨幣移動取引のみならず貨幣生成取引に対するもの（貨幣生成取引の場合取引入力は0個であるため空になる筈）も含んでいなければならない
        public virtual bool VerifyRewardAndTxFee(TransactionOutput[][] prevTxOutputss)
        {
            if (prevTxOutputss.Length != Transactions.Length)
                throw new ArgumentException("txs_and_prev_outputs");

            //if (prevTxOutputss.Length != transferTxs.Length)
            //    throw new ArgumentException("transfet_txs_and_prev_outputs");

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

        //2014/12/03
        //prevTxOutssは全ての取引に対するものを含んでいなければならないことに注意
        //貨幣移動取引のみならず貨幣生成取引に対するもの（貨幣生成取引の場合取引入力は0個であるため空になる筈）も含んでいなければならない
        public CurrencyUnit GetValidTxFee(TransactionOutput[][] prevTxOutputss)
        {
            if (prevTxOutputss.Length != Transactions.Length)
                throw new ArgumentException("txs_and_prev_outputs");

            long rawTxFee = 0;
            for (int i = 0; i < Transactions.Length; i++)
                if (Transactions[i] is TransferTransaction)
                    rawTxFee += (Transactions[i] as TransferTransaction).GetFee(prevTxOutputss[i]).rawAmount;
            return new CurrencyUnit(rawTxFee);

            //if (prevTxOutputss.Length != transferTxs.Length)
            //    throw new ArgumentException("transfet_txs_and_prev_outputs");

            //long rawTxFee = 0;
            //for (int i = 0; i < transferTxs.Length; i++)
            //    rawTxFee += transferTxs[i].GetFee(prevTxOutputss[i]).rawAmount;
            //return new CurrencyUnit(rawTxFee);
        }

        //2014/12/03
        //prevTxOutssは全ての取引に対するものを含んでいなければならないことに注意
        //貨幣移動取引のみならず貨幣生成取引に対するもの（貨幣生成取引の場合取引入力は0個であるため空になる筈）も含んでいなければならない
        public CurrencyUnit GetValidRewardToMinerAndTxFee(TransactionOutput[][] prevTxOutputss)
        {
            if (prevTxOutputss.Length != Transactions.Length)
                throw new ArgumentException("txs_and_prev_outputs");

            //if (prevTxOutputss.Length != transferTxs.Length)
            //    throw new ArgumentException("transfet_txs_and_prev_outputs");

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
            if (GetBlockType(index, version) != typeof(FoundationalBlock))
                throw new ArgumentException("index_invalid");

            if (version == 0 || version == 1)
                return new Creacoin(GetRewardToFoundation(index, version).AmountInCreacoin.Amount * foundationInterval);
            else
                throw new NotSupportedException("tx_block_not_supported");
        }

        public static Difficulty<Creahash> GetWorkRequired(long index, Func<long, TransactionalBlock> indexToTxBlock, int version)
        {
            if (index < 1)
                throw new ArgumentOutOfRangeException("index_out");

            if (version == 0 || version == 1)
            {
                if (index == 1)
                    return minDifficulty;

                long lastIndex = index - 1;

                //常にfalseになる筈
                if (index % retargetInterval != 0)
                    return indexToTxBlock(lastIndex).header.difficulty;
                else
                {
                    //常に1になる筈
                    long blocksToGoBack = index != retargetInterval ? retargetInterval : retargetInterval - 1;
                    //ブロック2のとき1、ブロック3のとき1、ブロック4のとき2、・・・
                    long firstIndex = lastIndex - blocksToGoBack > 0 ? lastIndex - blocksToGoBack : 1;

                    TimeSpan actualTimespan = indexToTxBlock(lastIndex).header.timestamp - indexToTxBlock(firstIndex).header.timestamp;

                    //最少で75%
                    if (actualTimespan.TotalSeconds < targetTimespan - (targetTimespan / 4.0))
                        actualTimespan = TimeSpan.FromSeconds(targetTimespan - (targetTimespan / 4.0));
                    //最大で150%
                    else if (actualTimespan.TotalSeconds > targetTimespan + (targetTimespan / 2.0))
                        actualTimespan = TimeSpan.FromSeconds(targetTimespan + (targetTimespan / 2.0));

                    //最少で0.75、最大で1.5
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

                    Creahash hash = new Creahash();

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

                    Difficulty<Creahash> difficulty = new Difficulty<Creahash>(hash);

                    return (difficulty.Diff < minDifficulty.Diff ? minDifficulty : difficulty).Pipe((dif) => dif.RaiseNotification("difficulty", 3, difficulty.Diff.ToString()));
                }
            }
            else
                throw new NotSupportedException("tx_block_not_supported");
        }

        public static BlockHeader GetBlockHeaderTemplate(long index, Func<long, TransactionalBlock> indexToTxBlock, int version)
        {
            if (index < 1)
                throw new ArgumentOutOfRangeException("index_out");

            if (version == 0 || version == 1)
                return new BlockHeader().Pipe((bh) => bh.LoadVersion0(index, index - 1 == 0 ? new GenesisBlock().Id : indexToTxBlock(index - 1).Id, DateTime.Now, GetWorkRequired(index, indexToTxBlock, version), new byte[10]));
            else
                throw new NotSupportedException("tx_block_not_supported");
        }

        public static TransactionalBlock GetBlockTemplate(long index, CoinbaseTransaction coinbaseTxToMiner, TransferTransaction[] transferTxs, Func<long, TransactionalBlock> indexToTxBlock, int version)
        {
            if (index < 1)
                throw new ArgumentOutOfRangeException("index_out");

            if (version == 0)
            {
                BlockHeader header = GetBlockHeaderTemplate(index, indexToTxBlock, version);

                TransactionalBlock txBlock;
                if (GetBlockType(index, version) == typeof(NormalBlock))
                    txBlock = new NormalBlock().Pipe((normalBlock) => normalBlock.LoadVersion0(header, coinbaseTxToMiner, transferTxs));
                else
                {
                    TransactionOutput coinbaseTxOutToFoundation = new TransactionOutput();
                    coinbaseTxOutToFoundation.LoadVersion0(foundationPubKeyHash, GetRewardToFoundationInterval(index, version));
                    CoinbaseTransaction coinbaseTxToFoundation = new CoinbaseTransaction();
                    coinbaseTxToFoundation.LoadVersion0(new TransactionOutput[] { coinbaseTxOutToFoundation });

                    txBlock = new FoundationalBlock().Pipe((foundationalBlock) => foundationalBlock.LoadVersion0(header, coinbaseTxToMiner, coinbaseTxToFoundation, transferTxs));
                }

                txBlock.UpdateMerkleRootHash();

                return txBlock;
            }
            else if (version == 1)
            {
                BlockHeader header = GetBlockHeaderTemplate(index, indexToTxBlock, version);

                TransactionalBlock txBlock;
                if (GetBlockType(index, version) == typeof(NormalBlock))
                    txBlock = new NormalBlock().Pipe((normalBlock) => normalBlock.LoadVersion1(header, coinbaseTxToMiner, transferTxs));
                else
                {
                    TransactionOutput coinbaseTxOutToFoundation = new TransactionOutput();
                    coinbaseTxOutToFoundation.LoadVersion0(foundationPubKeyHash, GetRewardToFoundationInterval(index, version));
                    CoinbaseTransaction coinbaseTxToFoundation = new CoinbaseTransaction();
                    coinbaseTxToFoundation.LoadVersion0(new TransactionOutput[] { coinbaseTxOutToFoundation });

                    txBlock = new FoundationalBlock().Pipe((foundationalBlock) => foundationalBlock.LoadVersion1(header, coinbaseTxToMiner, coinbaseTxToFoundation, transferTxs));
                }

                txBlock.UpdateMerkleRootHash();

                return txBlock;
            }
            else
                throw new NotSupportedException("tx_block_not_supported");
        }

        public static TransactionalBlock GetBlockTemplate(long index, Sha256Ripemd160Hash minerPubKeyHash, TransferTransaction[] transferTxs, Func<long, TransactionalBlock> indexToTxBlock, int version)
        {
            if (index < 1)
                throw new ArgumentOutOfRangeException("index_out");

            if (version == 0 || version == 1)
            {
                TransactionOutput coinbaseTxOutToMiner = new TransactionOutput();
                coinbaseTxOutToMiner.LoadVersion0(minerPubKeyHash, GetRewardToMiner(index, version));
                CoinbaseTransaction coinbaseTxToMiner = new CoinbaseTransaction();
                coinbaseTxToMiner.LoadVersion0(new TransactionOutput[] { coinbaseTxOutToMiner });

                return GetBlockTemplate(index, coinbaseTxToMiner, transferTxs, indexToTxBlock, version);
            }
            else
                throw new NotSupportedException("tx_block_not_supported");
        }
    }

    //2014/12/03 試験済
    public class NormalBlock : TransactionalBlock
    {
        private const string guidString = "6bf78c27e25fe843bf354952848edd52";
        private Guid guid;
        public override Guid Guid
        {
            get
            {
                if (guid == Guid.Empty)
                    guid = new Guid(guidString);

                return guid;
            }
        }
    }

    //2014/12/03 試験済
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

        private const string guidString = "1a3fbbf05e672d41ba52ace089710fc1";
        private Guid guid;
        public override Guid Guid
        {
            get
            {
                if (guid == Guid.Empty)
                    guid = new Guid(guidString);

                return guid;
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

        //2014/12/03
        //prevTxOutssは全ての取引に対するものを含んでいなければならないことに注意
        //貨幣移動取引のみならず貨幣生成取引に対するもの（貨幣生成取引の場合取引入力は0個であるため空になる筈）も含んでいなければならない
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

            return coinbaseTxToFoundation.TxOutputs.All((e) => e.Address.Equals(foundationPubKeyHash));
        }

        //2014/12/03
        //prevTxOutssは全ての取引に対するものを含んでいなければならないことに注意
        //貨幣移動取引のみならず貨幣生成取引に対するもの（貨幣生成取引の場合取引入力は0個であるため空になる筈）も含んでいなければならない
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

    public class BlockCollection : SHAREDDATA
    {
        public BlockCollection()
            : base(null)
        {
            blocks = new List<Block>();
            blocksCache = new CachedData<Block[]>(() =>
            {
                lock (blocksLock)
                    return blocks.ToArray();
            });
        }

        private readonly object blocksLock = new object();
        private List<Block> blocks;
        private readonly CachedData<Block[]> blocksCache;
        public Block[] Blocks { get { return blocksCache.Data; } }

        public event EventHandler<Block> BlockAdded = delegate { };
        public event EventHandler<Block> BlockRemoved = delegate { };

        public bool Contains(Creahash id)
        {
            lock (blocksLock)
                return blocks.FirstOrDefault((elem) => elem.Id.Equals(id)) != null;
        }

        public bool AddBlock(Block block)
        {
            lock (blocksLock)
            {
                if (blocks.Contains(block))
                    return false;

                this.ExecuteBeforeEvent(() =>
                {
                    blocks.Add(block);
                    blocksCache.IsModified = true;
                }, block, BlockAdded);

                return true;
            }
        }

        public bool RemoveBlock(Block block)
        {
            lock (blocksLock)
            {
                if (!blocks.Contains(block))
                    return false;

                this.ExecuteBeforeEvent(() =>
                {
                    blocks.Remove(block);
                    blocksCache.IsModified = true;
                }, block, BlockRemoved);

                return true;
            }
        }

        protected override Func<ReaderWriter, IEnumerable<MainDataInfomation>> StreamInfo
        {
            get { throw new NotImplementedException(); }
        }
    }

    #region ブロック鎖

    //未試験項目
    //・ブロック鎖のExit/同時更新など
    //・取引履歴関連

    //2014/12/05 分岐が発生しない場合 試験済
    //2014/12/14 分岐が発生する場合 試験済
    public class BlockChain
    {
        public BlockChain(BlockchainAccessDB _bcadb, BlockManagerDB _bmdb, BlockDB _bdb, BlockFilePointersDB _bfpdb, UtxoFileAccessDB _ufadb, UtxoFilePointersDB _ufpdb, UtxoFilePointersTempDB _ufptempdb, UtxoDB _udb, long _maxBlockIndexMargin = 100, long _mainBlockFinalization = 300, int _mainBlocksRetain = 1000, int _oldBlocksRetain = 1000)
        {
            bcadb = _bcadb;
            bmdb = _bmdb;
            bdb = _bdb;
            bfpdb = _bfpdb;
            ufadb = _ufadb;
            ufpdb = _ufpdb;
            ufptempdb = _ufptempdb;
            udb = _udb;

            maxBlockIndexMargin = _maxBlockIndexMargin;
            mainBlockFinalization = _mainBlockFinalization;
            capacity = (maxBlockIndexMargin + mainBlockFinalization) * 2;

            mainBlocksRetain = _mainBlocksRetain;
            oldBlocksRetain = _oldBlocksRetain;

            blockManager = new BlockManager(bmdb, bdb, bfpdb, mainBlocksRetain, oldBlocksRetain, mainBlockFinalization);
            utxoManager = new UtxoManager(ufadb, ufpdb, ufptempdb, udb);

            pendingBlocks = new Dictionary<Creahash, Block>[capacity];
            rejectedBlocks = new Dictionary<Creahash, Block>[capacity];
            blocksCurrent = new CirculatedInteger((int)capacity);

            registeredAddresses = new Dictionary<AddressEvent, List<Utxo>>();

            are = new AutoResetEvent(true);
        }

        private static readonly long unusableConformation = 6;

        private long maxBlockIndexMargin = 100;
        private long mainBlockFinalization = 300;
        private long capacity;

        private int mainBlocksRetain = 1000;
        private int oldBlocksRetain = 1000;

        private readonly BlockchainAccessDB bcadb;
        private readonly BlockManagerDB bmdb;
        private readonly BlockDB bdb;
        private readonly BlockFilePointersDB bfpdb;
        private readonly UtxoFileAccessDB ufadb;
        private readonly UtxoFilePointersDB ufpdb;
        private readonly UtxoFilePointersTempDB ufptempdb;
        private readonly UtxoDB udb;

        private readonly BlockManager blockManager;
        private readonly UtxoManager utxoManager;

        public readonly Dictionary<Creahash, Block>[] pendingBlocks;
        public readonly Dictionary<Creahash, Block>[] rejectedBlocks;
        public readonly CirculatedInteger blocksCurrent;

        public readonly Dictionary<AddressEvent, List<Utxo>> registeredAddresses;

        private TransactionHistories ths;

        public bool isExited { get; private set; }

        public event EventHandler BalanceUpdated = delegate { };
        public event EventHandler Updated = delegate { };

        private readonly AutoResetEvent are;
        private readonly object lockObj = new object();

        public long headBlockIndex { get { return blockManager.headBlockIndex; } }
        public long finalizedBlockIndex { get { return blockManager.finalizedBlockIndex; } }

        public Utxo FindUtxo(Sha256Ripemd160Hash address, long blockIndex, int txIndex, int txOutIndex)
        {
            return utxoManager.FindUtxo(address, blockIndex, txIndex, txOutIndex);
        }

        public Block GetHeadBlock()
        {
            return blockManager.GetHeadBlock();
        }

        public Block GetMainBlock(long blockIndex)
        {
            return blockManager.GetMainBlock(blockIndex);
        }

        public enum UpdateChainReturnType { updated, invariable, pending, rejected, updatedAndRejected }

        public class UpdateChainInnerReturn
        {
            public UpdateChainInnerReturn(UpdateChainReturnType _type)
            {
                if (_type == UpdateChainReturnType.rejected || _type == UpdateChainReturnType.pending)
                    throw new ArgumentException();

                type = _type;
            }

            public UpdateChainInnerReturn(UpdateChainReturnType _type, int _position)
            {
                if (_type != UpdateChainReturnType.pending)
                    throw new ArgumentException();

                type = _type;
                position = _position;
            }

            public UpdateChainInnerReturn(UpdateChainReturnType _type, int _position, List<Block> _rejectedBlocks)
            {
                if (_type != UpdateChainReturnType.rejected && _type != UpdateChainReturnType.updatedAndRejected)
                    throw new ArgumentException();

                type = _type;
                position = _position;
                rejectedBlocks = _rejectedBlocks;
            }

            public UpdateChainReturnType type { get; set; }
            public int position { get; set; }
            public List<Block> rejectedBlocks { get; set; }
        }

        public class BlockNode
        {
            public BlockNode(Block _block, double _cumulativeDiff, CirculatedInteger _ci)
            {
                block = _block;
                cumulativeDiff = _cumulativeDiff;
                ci = _ci;
            }

            public Block block { get; set; }
            public double cumulativeDiff { get; set; }
            public CirculatedInteger ci { get; set; }
        }

        //<未改良>本当はコンストラクタで処理すべきではあるがテストコードも書き換えなければならなくなるので暫定的に別メソッドに
        public void LoadTransactionHistories(TransactionHistories transactionHistories)
        {
            ths = transactionHistories;
        }

        private Tuple<CurrencyUnit, CurrencyUnit> CalculateBalance(List<Utxo> utxosList)
        {
            long usableRawAmount = 0;
            long unusableRawAmount = 0;

            foreach (var utxo in utxosList)
                if (utxo.blockIndex + unusableConformation > headBlockIndex)
                    unusableRawAmount += utxo.amount.rawAmount;
                else
                    usableRawAmount += utxo.amount.rawAmount;

            return new Tuple<CurrencyUnit, CurrencyUnit>(new CurrencyUnit(usableRawAmount), new CurrencyUnit(unusableRawAmount));
        }

        public void AddAddressEvent(AddressEvent addressEvent)
        {
            if (registeredAddresses.Keys.FirstOrDefault((elem) => elem.address.Equals(addressEvent.address)) != null)
                throw new InvalidOperationException("already_added");

            List<Utxo> utxos = utxoManager.GetAllUtxosLatestFirst(addressEvent.address);

            registeredAddresses.Add(addressEvent, utxos);

            Tuple<CurrencyUnit, CurrencyUnit> balance = CalculateBalance(utxos);

            addressEvent.RaiseBalanceUpdated(balance);
            addressEvent.RaiseUsableBalanceUpdated(balance.Item1);
            addressEvent.RaiseUnusableBalanceUpdated(balance.Item2);
        }

        public AddressEvent RemoveAddressEvent(Sha256Ripemd160Hash address)
        {
            AddressEvent addressEvent = registeredAddresses.Keys.FirstOrDefault((elem) => elem.address.Equals(address));

            if (addressEvent == null)
                throw new ArgumentException("not_added");

            registeredAddresses.Remove(addressEvent);

            return addressEvent;
        }

        public void Exit()
        {
            if (isExited)
                throw new InvalidOperationException("blockchain_alredy_exited");

            if (are.WaitOne(30000))
                isExited = true;
            else
                throw new InvalidOperationException("fatal:blkchain_freeze");
        }

        public UpdateChainReturnType UpdateChain(Block block)
        {
            if (isExited)
                throw new InvalidOperationException("blockchain_already_exited");

            lock (lockObj)
            {
                //最後に確定されたブロックのブロック番号が最小ブロック番号である
                long minBlockIndex = blockManager.finalizedBlockIndex;
                //先頭ブロックのブロック番号に余裕を加えたものが最大ブロック番号である
                long maxBlockIndex = blockManager.headBlockIndex + maxBlockIndexMargin;

                //最小ブロック番号以下のブロック番号を有するブロックは認められない
                if (block.Index <= minBlockIndex)
                    throw new InvalidOperationException();
                //現在のブロックのブロック番号より大き過ぎるブロック番号（最大ブロック番号を超えるブロック番号）を有するブロックは認められない
                if (block.Index > maxBlockIndex)
                    throw new InvalidOperationException();

                int position = blocksCurrent.GetForward((int)(block.Index - blockManager.headBlockIndex));
                //既に阻却されているブロックの場合は何も変わらない
                if (rejectedBlocks[position] != null && rejectedBlocks[position].Keys.Contains(block.Id))
                    return UpdateChainReturnType.invariable;

                //既にブロック鎖の一部である場合は何も変わらない
                Block mainBlock = block.Index > blockManager.headBlockIndex ? null : blockManager.GetMainBlock(block.Index);
                if (mainBlock != null && mainBlock.Id.Equals(block.Id))
                    return UpdateChainReturnType.invariable;

                Node<BlockNode> root = new Node<BlockNode>(new BlockNode(block, block.Difficulty.Diff, new CirculatedInteger(position, (int)capacity)), null);

                Queue<Node<BlockNode>> queue = new Queue<Node<BlockNode>>();
                queue.Enqueue(root);

                while (queue.Count > 0)
                {
                    Node<BlockNode> current = queue.Dequeue();

                    int nextIndex = current.value.ci.GetForward(1);

                    if (pendingBlocks[nextIndex] == null)
                        continue;

                    foreach (var nextBlock in pendingBlocks[nextIndex].Values.Where((elem) => elem.PrevId.Equals(current.value.block.Id)))
                    {
                        Node<BlockNode> child = new Node<BlockNode>(new BlockNode(nextBlock, current.value.cumulativeDiff + nextBlock.Difficulty.Diff, new CirculatedInteger(nextIndex, (int)capacity)), current);

                        current.children.Add(child);

                        queue.Enqueue(child);
                    }
                }

                UpdateChainReturnType type = UpdateChainReturnType.rejected;
                double actualCumulativeDiff = 0.0;

                while (true)
                {
                    queue.Enqueue(root);

                    Node<BlockNode> maxCumulativeDiffNode = new Node<BlockNode>(new BlockNode(null, 0.0, null), null);
                    while (queue.Count > 0)
                    {
                        Node<BlockNode> current = queue.Dequeue();

                        if (current.children.Count == 0 && current.value.cumulativeDiff > maxCumulativeDiffNode.value.cumulativeDiff)
                            maxCumulativeDiffNode = current;
                        else
                            foreach (var child in current.children)
                                queue.Enqueue(child);
                    }

                    if (maxCumulativeDiffNode.value.cumulativeDiff <= actualCumulativeDiff)
                        return type;

                    //ブロック以後のブロックを格納する
                    //ブロックが分岐ブロック鎖のブロックの一部である場合は、ブロックの直前のブロックからブロック鎖から分岐するまでのブロックも格納する
                    List<Block> blocksList = new List<Block>();
                    //ブロック以後のブロックの被参照取引出力を格納する
                    //ブロックが分岐ブロック鎖のブロックの一部である場合は、ブロックの直前のブロックからブロック鎖から分岐するまでのブロックの被参照取引出力も格納する
                    List<TransactionOutput[][]> prevTxOutputssList = new List<TransactionOutput[][]>();

                    //ブロックが分岐ブロック鎖のブロックの一部である場合は、先頭ブロックから分岐が始まっているブロックと同一のブロック番号のブロックまでを格納する
                    List<Block> mainBlocksList = new List<Block>();
                    //ブロックが分岐ブロック鎖のブロックの一部である場合は、先頭ブロックから分岐が始まっているブロックと同一のブロック番号のブロックまでの被参照取引出力を格納する
                    List<TransactionOutput[][]> mainPrevTxOutputssList = new List<TransactionOutput[][]>();

                    Node<BlockNode> temp = maxCumulativeDiffNode;
                    while (temp != null)
                    {
                        blocksList.Insert(0, temp.value.block);

                        temp = temp.parent;
                    }

                    UpdateChainInnerReturn ret = UpdateChainInner(block, blocksList, prevTxOutputssList, mainBlocksList, mainPrevTxOutputssList, minBlockIndex, position, maxCumulativeDiffNode.value.cumulativeDiff);

                    if (ret.type == UpdateChainReturnType.updated)
                        return UpdateChainReturnType.updated;
                    else if (ret.type == UpdateChainReturnType.pending)
                    {
                        if (pendingBlocks[ret.position] == null)
                        {
                            pendingBlocks[ret.position] = new Dictionary<Creahash, Block>();
                            pendingBlocks[ret.position].Add(block.Id, block);
                        }
                        else if (!pendingBlocks[ret.position].Keys.Contains(block.Id))
                            pendingBlocks[ret.position].Add(block.Id, block);

                        return UpdateChainReturnType.pending;
                    }
                    else if (ret.type == UpdateChainReturnType.rejected || ret.type == UpdateChainReturnType.updatedAndRejected)
                    {
                        Block rejectedRootBlock = ret.rejectedBlocks[0];

                        queue.Enqueue(root);

                        Node<BlockNode> rejectedRootNode = null;
                        while (queue.Count > 0)
                        {
                            Node<BlockNode> current = queue.Dequeue();

                            if (current.value.block == rejectedRootBlock)
                            {
                                rejectedRootNode = current;

                                break;
                            }

                            foreach (var child in current.children)
                                queue.Enqueue(child);
                        }

                        queue = new Queue<Node<BlockNode>>();
                        queue.Enqueue(rejectedRootNode);

                        CirculatedInteger ci = new CirculatedInteger(ret.position, (int)capacity);
                        while (queue.Count > 0)
                        {
                            Node<BlockNode> current = queue.Dequeue();

                            int rejectedPosition = ci.GetForward((int)(current.value.block.Index - rejectedRootBlock.Index));
                            if (pendingBlocks[rejectedPosition] != null && pendingBlocks[rejectedPosition].Keys.Contains(current.value.block.Id))
                                pendingBlocks[rejectedPosition].Remove(current.value.block.Id);
                            if (rejectedBlocks[rejectedPosition] == null)
                                rejectedBlocks[rejectedPosition] = new Dictionary<Creahash, Block>();
                            rejectedBlocks[rejectedPosition].Add(current.value.block.Id, current.value.block);

                            foreach (var child in current.children)
                                queue.Enqueue(child);
                        }

                        if (rejectedRootNode.parent == null)
                            return ret.type;

                        rejectedRootNode.parent.children.Remove(rejectedRootNode);

                        type = ret.type;
                        actualCumulativeDiff = rejectedRootNode.parent.value.cumulativeDiff;
                    }
                }
            }
        }

        private double VerifyBlockChain(List<Block> blocksList, Dictionary<Sha256Ripemd160Hash, List<Utxo>> addedUtxos, Dictionary<Sha256Ripemd160Hash, List<Utxo>> removedUtxos, List<TransactionOutput[][]> prevTxOutputssList)
        {
            Func<long, TransactionalBlock> _bindexToTxBlock = (bindex) =>
            {
                if (bindex < blocksList[0].Index)
                    return blockManager.GetMainBlock(bindex) as TransactionalBlock;
                else if (bindex < blocksList[0].Index + blocksList.Count)
                    return blocksList[(int)(bindex - blocksList[0].Index)] as TransactionalBlock;
                else
                    return null;
            };

            double cumulativeDiff = 0.0;

            udb.Open();

            for (int i = 0; i < blocksList.Count; i++)
            {
                //ブロックの被参照取引出力を取得する
                TransactionOutput[][] prevTxOutputss = GetPrevTxOutputss(blocksList[i], _bindexToTxBlock);

                if (prevTxOutputss == null)
                {
                    udb.Close();

                    return cumulativeDiff;
                }

                //ブロック自体の検証を実行する
                //無効ブロックである場合は阻却ブロックとする
                if (blocksList[i] is GenesisBlock)
                {
                    if (!(blocksList[i] as GenesisBlock).Verify())
                    {
                        udb.Close();

                        return cumulativeDiff;
                    }
                }
                else if (blocksList[i] is TransactionalBlock)
                {
                    if (!(blocksList[i] as TransactionalBlock).Verify(prevTxOutputss, _bindexToTxBlock))
                    {
                        udb.Close();

                        return cumulativeDiff;
                    }
                }
                else
                    throw new NotSupportedException();

                //ブロック鎖の状態（＝未使用の取引出力の存否）と矛盾がないか検証する
                //矛盾がある場合は無効ブロックとなり、阻却ブロックとする
                if (!VerifyUtxo(blocksList[i], prevTxOutputss, addedUtxos, removedUtxos))
                {
                    udb.Close();

                    return cumulativeDiff;
                }

                prevTxOutputssList.Add(prevTxOutputss);

                RetrieveTransactionTransitionForward(blocksList[i], prevTxOutputss, addedUtxos, removedUtxos);

                cumulativeDiff += blocksList[i].Difficulty.Diff;
            }

            udb.Close();

            return cumulativeDiff;
        }

        private UpdateChainInnerReturn UpdateChainInner(Block block, List<Block> blocksList, List<TransactionOutput[][]> prevTxOutputssList, List<Block> mainBlocksList, List<TransactionOutput[][]> mainPrevTxOutputssList, long minBlockIndex, int position, double cumulativeDiff)
        {
            //ブロックが起源ブロックである場合と先頭ブロックの直後のブロックである場合
            if (block.Index == blockManager.headBlockIndex + 1 && (blockManager.headBlockIndex == -1 || block.PrevId.Equals(blockManager.GetHeadBlock().Id)))
            {
                VerifyBlockChain(blocksList, new Dictionary<Sha256Ripemd160Hash, List<Utxo>>(), new Dictionary<Sha256Ripemd160Hash, List<Utxo>>(), prevTxOutputssList);

                UpdateBlockChainDB(blocksList, prevTxOutputssList, mainBlocksList, mainPrevTxOutputssList);
            }
            else
            {
                double mainCumulativeDiff = 0.0;

                for (long i = block.Index; i <= blockManager.headBlockIndex; i++)
                {
                    Block nextBlock = blockManager.GetMainBlock(i);

                    mainCumulativeDiff += nextBlock.Difficulty.Diff;

                    mainBlocksList.Add(nextBlock);
                }

                Block prevBlockBrunch = block;
                Block prevBlockMain = block.Index > blockManager.headBlockIndex ? null : blockManager.GetMainBlock(block.Index);

                CirculatedInteger ci = new CirculatedInteger(position, (int)capacity);

                while (true)
                {
                    if (prevBlockBrunch.Index == 0)
                        break;
                    if (prevBlockMain != null && prevBlockMain.PrevId.Equals(prevBlockBrunch.PrevId))
                        break;

                    long prevIndex = prevBlockBrunch.Index - 1;

                    if (prevIndex <= minBlockIndex)
                        return new UpdateChainInnerReturn(UpdateChainReturnType.rejected, ci.value, blocksList);

                    ci.Previous();

                    if (pendingBlocks[ci.value] == null || !pendingBlocks[ci.value].Keys.Contains(prevBlockBrunch.PrevId))
                        if (rejectedBlocks[ci.value] != null && rejectedBlocks[ci.value].Keys.Contains(prevBlockBrunch.PrevId))
                            return new UpdateChainInnerReturn(UpdateChainReturnType.rejected, ci.GetForward(1), blocksList);
                        else
                            return new UpdateChainInnerReturn(UpdateChainReturnType.pending, position);

                    prevBlockBrunch = pendingBlocks[ci.value][prevBlockBrunch.PrevId];
                    prevBlockMain = prevIndex > blockManager.headBlockIndex ? null : blockManager.GetMainBlock(prevIndex);

                    blocksList.Insert(0, prevBlockBrunch);
                    if (prevBlockMain != null)
                        mainBlocksList.Insert(0, prevBlockMain);

                    cumulativeDiff += prevBlockBrunch.Difficulty.Diff;
                    if (prevBlockMain != null)
                        mainCumulativeDiff += prevBlockMain.Difficulty.Diff;
                }

                if (cumulativeDiff <= mainCumulativeDiff)
                    return new UpdateChainInnerReturn(UpdateChainReturnType.pending, position);

                Func<long, TransactionalBlock> _bindexToTxBlock = (bindex) =>
                {
                    if (bindex <= blockManager.headBlockIndex)
                        return blockManager.GetMainBlock(bindex) as TransactionalBlock;
                    else
                        return null;
                };

                Dictionary<Sha256Ripemd160Hash, List<Utxo>> addedUtxos = new Dictionary<Sha256Ripemd160Hash, List<Utxo>>();
                Dictionary<Sha256Ripemd160Hash, List<Utxo>> removedUtxos = new Dictionary<Sha256Ripemd160Hash, List<Utxo>>();

                for (int i = mainBlocksList.Count - 1; i >= 0; i--)
                {
                    TransactionOutput[][] prevTxOutputss = GetPrevTxOutputss(mainBlocksList[i], _bindexToTxBlock);

                    if (prevTxOutputss == null)
                        throw new InvalidOperationException("blockchain_fork_main_backward");

                    mainPrevTxOutputssList.Insert(0, prevTxOutputss);

                    RetrieveTransactionTransitionBackward(mainBlocksList[i], prevTxOutputss, addedUtxos, removedUtxos);
                }

                cumulativeDiff = VerifyBlockChain(blocksList, addedUtxos, removedUtxos, prevTxOutputssList);

                if (cumulativeDiff > mainCumulativeDiff)
                    UpdateBlockChainDB(blocksList, prevTxOutputssList, mainBlocksList, mainPrevTxOutputssList);
            }

            if (blocksList.Count != prevTxOutputssList.Count)
            {
                List<Block> rejecteds = new List<Block>();
                for (int i = prevTxOutputssList.Count; i < blocksList.Count; i++)
                    rejecteds.Add(blocksList[i]);

                Updated(this, EventArgs.Empty);

                return new UpdateChainInnerReturn(UpdateChainReturnType.updatedAndRejected, blocksCurrent.GetForward((int)(blocksList[prevTxOutputssList.Count].Index - blockManager.headBlockIndex)), rejecteds);
            }

            Updated(this, EventArgs.Empty);

            return new UpdateChainInnerReturn(UpdateChainReturnType.updated);
        }

        private void UpdateBlockChainDB(List<Block> blocksList, List<TransactionOutput[][]> prevTxOutputssList, List<Block> mainBlocksList, List<TransactionOutput[][]> mainPrevTxOutputssList)
        {
            long beforeIndex = headBlockIndex;

            udb.Open();

            are.Reset();

            bcadb.Create();

            List<AddressEvent> updatedAddressesList = new List<AddressEvent>();
            List<AddressEvent> notupdatedAddressesList = new List<AddressEvent>(registeredAddresses.Keys);

            Func<TransactionOutput, bool> _FindUpdatedAddressEvent = (txOut) =>
            {
                AddressEvent updatedAddressEvent = null;

                foreach (var addressEvent in notupdatedAddressesList)
                    if (txOut.Address.Equals(addressEvent.address))
                    {
                        updatedAddressEvent = addressEvent;

                        break;
                    }

                if (updatedAddressEvent != null)
                {
                    updatedAddressesList.Add(updatedAddressEvent);
                    notupdatedAddressesList.Remove(updatedAddressEvent);

                    return true;
                }

                foreach (var addressEvent in updatedAddressesList)
                    if (txOut.Address.Equals(addressEvent.address))
                        return true;

                return false;
            };

            Func<Transaction, List<TransactionOutput>, List<TransactionOutput>, TransactionHistoryType> _GetType = (transaction, receiversList, sendersList) =>
            {
                TransactionHistoryType type = TransactionHistoryType.mined;
                if (!(transaction is CoinbaseTransaction))
                {
                    if (receiversList.Count < transaction.TxOutputs.Length)
                        type = TransactionHistoryType.sent;
                    else if (sendersList.Count < transaction.TxInputs.Length)
                        type = TransactionHistoryType.received;
                    else
                        type = TransactionHistoryType.transfered;
                }
                return type;
            };

            Func<Block, Transaction, TransactionHistory, DateTime> _GetDatetime = (block, transaction, th) =>
            {
                DateTime datetime = DateTime.MinValue;
                if (th != null)
                    datetime = th.datetime;
                else if (transaction is CoinbaseTransaction)
                    datetime = (block as TransactionalBlock).header.timestamp;
                return datetime;
            };

            List<TransactionOutput> senders = new List<TransactionOutput>();
            List<TransactionOutput> receivers = new List<TransactionOutput>();

            for (int i = mainBlocksList.Count - 1; i >= 0; i--)
            {
                for (int j = 0; j < mainBlocksList[i].Transactions.Length; j++)
                {
                    senders.Clear();
                    receivers.Clear();

                    long sentAmount = 0;
                    long receivedAmount = 0;

                    for (int k = 0; k < mainBlocksList[i].Transactions[j].TxInputs.Length; k++)
                        if (_FindUpdatedAddressEvent(mainPrevTxOutputssList[i][j][k]))
                        {
                            sentAmount += mainPrevTxOutputssList[i][j][k].Amount.rawAmount;

                            senders.Add(mainPrevTxOutputssList[i][j][k]);
                        }
                    for (int k = 0; k < mainBlocksList[i].Transactions[j].TxOutputs.Length; k++)
                        if (_FindUpdatedAddressEvent(mainBlocksList[i].Transactions[j].TxOutputs[k]))
                        {
                            receivedAmount += mainBlocksList[i].Transactions[j].TxOutputs[k].Amount.rawAmount;

                            receivers.Add(mainBlocksList[i].Transactions[j].TxOutputs[k]);
                        }

                    if (senders.Count > 0 || receivers.Count > 0)
                    {
                        TransactionHistory th = ths.ContainsConformedTransactionHistory(mainBlocksList[i].Transactions[j].Id);

                        if (th != null)
                            ths.RemoveConfirmedTransactionHistory(mainBlocksList[i].Transactions[j].Id);

                        th = new TransactionHistory(true, false, _GetType(mainBlocksList[i].Transactions[j], receivers, senders), _GetDatetime(mainBlocksList[i], mainBlocksList[i].Transactions[j], th), 0, mainBlocksList[i].Transactions[j].Id, senders.ToArray(), receivers.ToArray(), mainBlocksList[i].Transactions[j], mainPrevTxOutputssList[i][j], new CurrencyUnit(receivedAmount - sentAmount));

                        ths.AddTransactionHistory(th);
                    }
                }

                blockManager.DeleteMainBlock(mainBlocksList[i].Index);
                utxoManager.RevertBlock(mainBlocksList[i], mainPrevTxOutputssList[i]);

                if (pendingBlocks[blocksCurrent.value] == null)
                    pendingBlocks[blocksCurrent.value] = new Dictionary<Creahash, Block>();
                pendingBlocks[blocksCurrent.value].Add(mainBlocksList[i].Id, mainBlocksList[i]);

                blocksCurrent.Previous();
            }

            for (int i = 0; i < prevTxOutputssList.Count; i++)
            {
                for (int j = 0; j < blocksList[i].Transactions.Length; j++)
                {
                    senders.Clear();
                    receivers.Clear();

                    long sentAmount = 0;
                    long receivedAmount = 0;

                    for (int k = 0; k < blocksList[i].Transactions[j].TxInputs.Length; k++)
                        if (_FindUpdatedAddressEvent(prevTxOutputssList[i][j][k]))
                        {
                            sentAmount += prevTxOutputssList[i][j][k].Amount.rawAmount;

                            senders.Add(prevTxOutputssList[i][j][k]);
                        }
                    for (int k = 0; k < blocksList[i].Transactions[j].TxOutputs.Length; k++)
                        if (_FindUpdatedAddressEvent(blocksList[i].Transactions[j].TxOutputs[k]))
                        {
                            receivedAmount += blocksList[i].Transactions[j].TxOutputs[k].Amount.rawAmount;

                            receivers.Add(blocksList[i].Transactions[j].TxOutputs[k]);
                        }

                    if (senders.Count > 0 || receivers.Count > 0)
                    {
                        TransactionHistory th = ths.ContainsUnconformedTransactionHistory(blocksList[i].Transactions[j].Id);

                        if (th != null)
                            ths.RemoveUnconfirmedTransactionHistory(blocksList[i].Transactions[j].Id);

                        th = new TransactionHistory(true, true, _GetType(blocksList[i].Transactions[j], receivers, senders), _GetDatetime(blocksList[i], blocksList[i].Transactions[j], th), blocksList[i].Index, blocksList[i].Transactions[j].Id, senders.ToArray(), receivers.ToArray(), blocksList[i].Transactions[j], prevTxOutputssList[i][j], new CurrencyUnit(receivedAmount - sentAmount));

                        ths.AddTransactionHistory(th);
                    }
                }

                blockManager.AddMainBlock(blocksList[i]);
                utxoManager.ApplyBlock(blocksList[i], prevTxOutputssList[i]);

                blocksCurrent.Next();

                if (pendingBlocks[blocksCurrent.value] != null && pendingBlocks[blocksCurrent.value].Keys.Contains(blocksList[i].Id))
                    pendingBlocks[blocksCurrent.value].Remove(blocksList[i].Id);

                int nextMaxPosition = blocksCurrent.GetForward((int)maxBlockIndexMargin);

                rejectedBlocks[nextMaxPosition] = null;
                pendingBlocks[nextMaxPosition] = null;
            }

            utxoManager.SaveUFPTemp();

            bcadb.Delete();

            are.Set();

            long minIndex = Math.Min(beforeIndex, headBlockIndex);

            foreach (var addressEvent in updatedAddressesList)
            {
                List<Utxo> utxos = utxoManager.GetAllUtxosLatestFirst(addressEvent.address);

                registeredAddresses[addressEvent] = utxos;

                Tuple<CurrencyUnit, CurrencyUnit> balance = CalculateBalance(utxos);

                addressEvent.RaiseBalanceUpdated(balance);
                addressEvent.RaiseUsableBalanceUpdated(balance.Item1);
                addressEvent.RaiseUnusableBalanceUpdated(balance.Item2);
            }

            bool flag = false;

            foreach (var addressEvent in notupdatedAddressesList)
            {
                List<Utxo> utxos = registeredAddresses[addressEvent];
                if (utxos.Count > 0 && utxos[0].blockIndex + unusableConformation > minIndex)
                {
                    Tuple<CurrencyUnit, CurrencyUnit> balance = CalculateBalance(utxos);

                    addressEvent.RaiseBalanceUpdated(balance);
                    addressEvent.RaiseUsableBalanceUpdated(balance.Item1);
                    addressEvent.RaiseUnusableBalanceUpdated(balance.Item2);

                    flag = true;
                }
            }

            udb.Close();

            if (updatedAddressesList.Count > 0 || flag)
                BalanceUpdated(this, EventArgs.Empty);
        }

        private void RetrieveTransactionTransitionForward(Block block, TransactionOutput[][] prevTxOutss, Dictionary<Sha256Ripemd160Hash, List<Utxo>> addedUtxos, Dictionary<Sha256Ripemd160Hash, List<Utxo>> removedUtxos)
        {
            for (int i = 0; i < block.Transactions.Length; i++)
            {
                for (int j = 0; j < block.Transactions[i].TxInputs.Length; j++)
                    AddedRemoveAndRemovedAdd(prevTxOutss[i][j].Address, block.Transactions[i].TxInputs[j].PrevTxBlockIndex, block.Transactions[i].TxInputs[j].PrevTxIndex, block.Transactions[i].TxInputs[j].PrevTxOutputIndex, prevTxOutss[i][j].Amount, addedUtxos, removedUtxos);
                for (int j = 0; j < block.Transactions[i].TxOutputs.Length; j++)
                    RemovedRemoveAndAddedAdd(block.Transactions[i].TxOutputs[j].Address, block.Index, i, j, block.Transactions[i].TxOutputs[j].Amount, addedUtxos, removedUtxos);
            }
        }

        private void RetrieveTransactionTransitionBackward(Block block, TransactionOutput[][] prevTxOutss, Dictionary<Sha256Ripemd160Hash, List<Utxo>> addedUtxos, Dictionary<Sha256Ripemd160Hash, List<Utxo>> removedUtxos)
        {
            for (int i = 0; i < block.Transactions.Length; i++)
            {
                for (int j = 0; j < block.Transactions[i].TxInputs.Length; j++)
                    RemovedRemoveAndAddedAdd(prevTxOutss[i][j].Address, block.Transactions[i].TxInputs[j].PrevTxBlockIndex, block.Transactions[i].TxInputs[j].PrevTxIndex, block.Transactions[i].TxInputs[j].PrevTxOutputIndex, prevTxOutss[i][j].Amount, addedUtxos, removedUtxos);
                for (int j = 0; j < block.Transactions[i].TxOutputs.Length; j++)
                    AddedRemoveAndRemovedAdd(block.Transactions[i].TxOutputs[j].Address, block.Index, i, j, block.Transactions[i].TxOutputs[j].Amount, addedUtxos, removedUtxos);
            }
        }

        //後退する場合の取引入力（被参照取引出力）は削除集合から削除 or 追加集合に追加
        //前進する場合の取引出力は削除集合から削除 or 追加集合に追加
        private void RemovedRemoveAndAddedAdd(Sha256Ripemd160Hash address, long bindex, int txindex, int txoutindex, CurrencyUnit amount, Dictionary<Sha256Ripemd160Hash, List<Utxo>> addedUtxos, Dictionary<Sha256Ripemd160Hash, List<Utxo>> removedUtxos)
        {
            if (removedUtxos.Keys.Contains(address))
            {
                Utxo utxo = removedUtxos[address].Where((elem) => elem.IsMatch(bindex, txindex, txoutindex)).FirstOrDefault();
                if (utxo != null)
                {
                    removedUtxos[address].Remove(utxo);

                    return;
                }
            }

            List<Utxo> utxos = null;
            if (addedUtxos.Keys.Contains(address))
                utxos = addedUtxos[address];
            else
                addedUtxos.Add(address, utxos = new List<Utxo>());
            utxos.Add(new Utxo(bindex, txindex, txoutindex, amount));
        }

        //後退する場合の取引出力は追加集合から削除 or 削除集合に追加
        //前進する場合の取引入力（被参照取引出力）は追加集合から削除 or 削除集合に追加
        private void AddedRemoveAndRemovedAdd(Sha256Ripemd160Hash address, long bindex, int txindex, int txoutindex, CurrencyUnit amount, Dictionary<Sha256Ripemd160Hash, List<Utxo>> addedUtxos, Dictionary<Sha256Ripemd160Hash, List<Utxo>> removedUtxos)
        {
            if (addedUtxos.Keys.Contains(address))
            {
                Utxo utxo = addedUtxos[address].Where((elem) => elem.IsMatch(bindex, txindex, txoutindex)).FirstOrDefault();
                if (utxo != null)
                {
                    addedUtxos[address].Remove(utxo);

                    return;
                }
            }

            List<Utxo> utxos = null;
            if (removedUtxos.Keys.Contains(address))
                utxos = removedUtxos[address];
            else
                removedUtxos.Add(address, utxos = new List<Utxo>());
            utxos.Add(new Utxo(bindex, txindex, txoutindex, amount));
        }

        private TransactionOutput[][] GetPrevTxOutputss(Block block, Func<long, TransactionalBlock> _bindexToBlock)
        {
            TransactionOutput[][] prevTxOutputss = new TransactionOutput[block.Transactions.Length][];
            for (int i = 0; i < block.Transactions.Length; i++)
            {
                prevTxOutputss[i] = new TransactionOutput[block.Transactions[i].TxInputs.Length];
                for (int j = 0; j < block.Transactions[i].TxInputs.Length; j++)
                {
                    Block prevBlock = _bindexToBlock(block.Transactions[i].TxInputs[j].PrevTxBlockIndex);

                    if (prevBlock == null)
                        return null;
                    if (block.Transactions[i].TxInputs[j].PrevTxIndex >= prevBlock.Transactions.Length)
                        return null;

                    Transaction prevTx = prevBlock.Transactions[block.Transactions[i].TxInputs[j].PrevTxIndex];

                    if (block.Transactions[i].TxInputs[j].PrevTxOutputIndex >= prevTx.TxOutputs.Length)
                        return null;

                    prevTxOutputss[i][j] = prevTx.TxOutputs[block.Transactions[i].TxInputs[j].PrevTxOutputIndex];
                }
            }
            return prevTxOutputss;
        }

        private bool VerifyUtxo(Block block, TransactionOutput[][] prevTxOutss, Dictionary<Sha256Ripemd160Hash, List<Utxo>> addedUtxos, Dictionary<Sha256Ripemd160Hash, List<Utxo>> removedUtxos)
        {
            Dictionary<Sha256Ripemd160Hash, List<Utxo>> removedUtxos2 = new Dictionary<Sha256Ripemd160Hash, List<Utxo>>();

            for (int i = 0; i < block.Transactions.Length; i++)
                for (int j = 0; j < block.Transactions[i].TxInputs.Length; j++)
                {
                    if (removedUtxos2.Keys.Contains(prevTxOutss[i][j].Address) && removedUtxos2[prevTxOutss[i][j].Address].Where((elem) => elem.IsMatch(block.Transactions[i].TxInputs[j].PrevTxBlockIndex, block.Transactions[i].TxInputs[j].PrevTxIndex, block.Transactions[i].TxInputs[j].PrevTxOutputIndex)).FirstOrDefault() != null)
                        return false;
                    if (removedUtxos.Keys.Contains(prevTxOutss[i][j].Address) && removedUtxos[prevTxOutss[i][j].Address].Where((elem) => elem.IsMatch(block.Transactions[i].TxInputs[j].PrevTxBlockIndex, block.Transactions[i].TxInputs[j].PrevTxIndex, block.Transactions[i].TxInputs[j].PrevTxOutputIndex)).FirstOrDefault() != null)
                        return false;

                    List<Utxo> utxos;
                    if (!removedUtxos2.Keys.Contains(prevTxOutss[i][j].Address))
                        removedUtxos2.Add(prevTxOutss[i][j].Address, utxos = new List<Utxo>());
                    else
                        utxos = removedUtxos2[prevTxOutss[i][j].Address];
                    utxos.Add(new Utxo(block.Transactions[i].TxInputs[j].PrevTxBlockIndex, block.Transactions[i].TxInputs[j].PrevTxIndex, block.Transactions[i].TxInputs[j].PrevTxOutputIndex, prevTxOutss[i][j].Amount));

                    if (addedUtxos.Keys.Contains(prevTxOutss[i][j].Address) && addedUtxos[prevTxOutss[i][j].Address].Where((elem) => elem.IsMatch(block.Transactions[i].TxInputs[j].PrevTxBlockIndex, block.Transactions[i].TxInputs[j].PrevTxIndex, block.Transactions[i].TxInputs[j].PrevTxOutputIndex)).FirstOrDefault() != null)
                        continue;

                    if (utxoManager.FindUtxo(prevTxOutss[i][j].Address, block.Transactions[i].TxInputs[j].PrevTxBlockIndex, block.Transactions[i].TxInputs[j].PrevTxIndex, block.Transactions[i].TxInputs[j].PrevTxOutputIndex) == null)
                        return false;
                }

            return true;
        }
    }

    //2014/12/04 試験済
    public class BlockManager
    {
        public BlockManager(BlockManagerDB _bmdb, BlockDB _bdb, BlockFilePointersDB _bfpdb, int _mainBlocksRetain, int _oldBlocksRetain, long _mainBlockFinalization)
        {
            if (_mainBlocksRetain < _mainBlockFinalization)
                throw new InvalidOperationException();

            bmdb = _bmdb;
            bdb = _bdb;
            bfpdb = _bfpdb;

            mainBlocksRetain = _mainBlocksRetain;
            oldBlocksRetain = _oldBlocksRetain;
            mainBlockFinalization = _mainBlockFinalization;

            bmd = bmdb.GetData().Pipe((bmdBytes) => bmdBytes.Length != 0 ? SHAREDDATA.FromBinary<BlockManagerData>(bmdBytes) : new BlockManagerData());

            mainBlocks = new Block[mainBlocksRetain];
            sideBlocks = new List<Block>[mainBlocksRetain];
            mainBlocksCurrent = new CirculatedInteger(mainBlocksRetain);

            oldBlocks = new Dictionary<long, Block>();
        }

        private static readonly long blockFileCapacity = 100000;

        public readonly int mainBlocksRetain;
        public readonly int oldBlocksRetain;
        public readonly long mainBlockFinalization;

        private readonly BlockManagerDB bmdb;
        private readonly BlockDB bdb;
        private readonly BlockFilePointersDB bfpdb;

        private readonly BlockManagerData bmd;

        public readonly Block[] mainBlocks;
        //<未改良>どこに保存したかも保持しておかないと意味がない
        public readonly List<Block>[] sideBlocks;
        public readonly CirculatedInteger mainBlocksCurrent;

        public readonly Dictionary<long, Block> oldBlocks;

        public long headBlockIndex { get { return bmd.headBlockIndex; } }
        public long finalizedBlockIndex { get { return bmd.finalizedBlockIndex; } }

        public void DeleteMainBlock(long blockIndex)
        {
            if (blockIndex != bmd.headBlockIndex)
                throw new InvalidOperationException();
            if (blockIndex <= bmd.finalizedBlockIndex)
                throw new InvalidOperationException();
            if (blockIndex < 0)
                throw new InvalidOperationException();

            if (sideBlocks[mainBlocksCurrent.value] == null)
                sideBlocks[mainBlocksCurrent.value] = new List<Block>();
            if (sideBlocks[mainBlocksCurrent.value].Where((elem) => elem.Id.Equals(mainBlocks[mainBlocksCurrent.value].Id)).FirstOrDefault() == null)
                sideBlocks[mainBlocksCurrent.value].Add(mainBlocks[mainBlocksCurrent.value]);

            mainBlocks[mainBlocksCurrent.value] = null;

            mainBlocksCurrent.Previous();

            bmd.headBlockIndex = blockIndex - 1;

            bmdb.UpdateData(bmd.ToBinary());
        }

        public void DeleteMainBlocks(long blockIndex)
        {

        }

        //<未改良>sideBlocksを使うように
        public void AddMainBlock(Block block)
        {
            if (block.Index != bmd.headBlockIndex + 1)
                throw new InvalidOperationException();

            mainBlocksCurrent.Next();

            mainBlocks[mainBlocksCurrent.value] = block;
            sideBlocks[mainBlocksCurrent.value] = new List<Block>();

            bmd.headBlockIndex = block.Index;
            bmd.finalizedBlockIndex = bmd.headBlockIndex < mainBlockFinalization ? 0 : bmd.headBlockIndex - mainBlockFinalization;

            bfpdb.UpdateBlockFilePointerData(block.Index, bdb.AddBlockData(block.Index / blockFileCapacity, SHAREDDATA.ToBinary<Block>(block)));
            bmdb.UpdateData(bmd.ToBinary());
        }

        public void AddMainBlocks(Block[] blocks)
        {

        }

        public Block GetHeadBlock() { return GetMainBlock(bmd.headBlockIndex); }

        public Block GetMainBlock(long blockIndex)
        {
            if (blockIndex > bmd.headBlockIndex)
                throw new InvalidOperationException();
            if (blockIndex < 0)
                throw new InvalidOperationException();

            if (blockIndex > bmd.headBlockIndex - mainBlocksRetain)
            {
                int index = mainBlocksCurrent.GetBackward((int)(bmd.headBlockIndex - blockIndex));
                if (mainBlocks[index] == null)
                    mainBlocks[index] = SHAREDDATA.FromBinary<Block>(bdb.GetBlockData(blockIndex / blockFileCapacity, bfpdb.GetBlockFilePointerData(blockIndex)));

                if (mainBlocks[index].Index != blockIndex)
                    throw new InvalidOperationException();

                return mainBlocks[index];
            }

            if (oldBlocks.Keys.Contains(blockIndex))
                return oldBlocks[blockIndex];

            Block block = SHAREDDATA.FromBinary<Block>(bdb.GetBlockData(blockIndex / blockFileCapacity, bfpdb.GetBlockFilePointerData(blockIndex)));

            if (block.Index != blockIndex)
                throw new InvalidOperationException();

            oldBlocks.Add(blockIndex, block);

            while (oldBlocks.Count > oldBlocksRetain)
                oldBlocks.Remove(oldBlocks.First().Key);

            return block;
        }

        //<未改良>一括取得
        //2014/12/01 試験除外　一括取得を実装したら試験する
        public Block[] GetMainBlocks(long[] blockIndexes)
        {
            Block[] blocks = new Block[blockIndexes.Length];
            for (int i = 0; i < blocks.Length; i++)
                blocks[i] = GetMainBlock(blockIndexes[i]);
            return blocks;
        }

        //<未改良>一括取得
        //2014/12/01 試験除外　一括取得を実装したら試験する
        public Block[] GetMainBlocks(long blockIndexFrom, long blockIndexThrough)
        {
            if (blockIndexFrom > blockIndexThrough)
                throw new InvalidOperationException();

            Block[] blocks = new Block[blockIndexThrough - blockIndexFrom + 1];
            for (int i = 0; i < blocks.Length; i++)
                blocks[i] = GetMainBlock(blockIndexFrom + i);
            return blocks;
        }
    }

    public class BlockManagerData : SHAREDDATA
    {
        public BlockManagerData()
            : base(0)
        {
            headBlockIndex = -1;
            finalizedBlockIndex = -1;
        }

        public long headBlockIndex { get; set; }
        public long finalizedBlockIndex { get; set; }

        protected override Func<ReaderWriter, IEnumerable<MainDataInfomation>> StreamInfo
        {
            get
            {
                if (Version == 0)
                    return (msrw) => new MainDataInfomation[]{
                        new MainDataInfomation(typeof(long), () => headBlockIndex, (o) => headBlockIndex = (long)o),
                        new MainDataInfomation(typeof(long), () => finalizedBlockIndex, (o) => finalizedBlockIndex = (long)o),
                };
                else
                    throw new NotSupportedException();
            }
        }
        public override bool IsVersioned { get { return true; } }
    }

    //2014/12/04 試験済
    public class UtxoManager
    {
        public UtxoManager(UtxoFileAccessDB _ufadb, UtxoFilePointersDB _ufpdb, UtxoFilePointersTempDB _ufptempdb, UtxoDB _udb)
        {
            ufadb = _ufadb;
            ufpdb = _ufpdb;
            ufptempdb = _ufptempdb;
            udb = _udb;

            ufp = ufpdb.GetData().Pipe((bytes) => bytes.Length == 0 ? new UtxoFilePointers() : SHAREDDATA.FromBinary<UtxoFilePointers>(bytes));
            ufptemp = ufptempdb.GetData().Pipe((bytes) => bytes.Length == 0 ? new UtxoFilePointers() : SHAREDDATA.FromBinary<UtxoFilePointers>(bytes));

            foreach (var ufpitem in ufptemp.GetAll())
                ufp.AddOrUpdate(ufpitem.Key, ufpitem.Value);

            ufadb.Create();
            ufpdb.UpdateData(ufp.ToBinary());
            ufptempdb.Delete();
            ufadb.Delete();

            ufptemp = new UtxoFilePointers();
        }

        private static readonly int FirstUtxoFileItemSize = 16;

        private readonly UtxoFileAccessDB ufadb;
        private readonly UtxoFilePointersDB ufpdb;
        private readonly UtxoFilePointersTempDB ufptempdb;
        private readonly UtxoDB udb;

        private readonly UtxoFilePointers ufp;
        private readonly UtxoFilePointers ufptemp;

        public void AddUtxo(Sha256Ripemd160Hash address, long blockIndex, int txIndex, int txOutIndex, CurrencyUnit amount)
        {
            bool isExistedInTemp = false;

            long? position = ufptemp.Get(address);
            if (position.HasValue)
                isExistedInTemp = true;
            else
                position = ufp.Get(address);

            bool isProcessed = false;

            bool isFirst = true;
            long? firstPosition = null;
            int? firstSize = null;

            UtxoFileItem ufi = null;
            while (true)
            {
                if (!position.HasValue)
                    ufi = new UtxoFileItem(FirstUtxoFileItemSize);
                else if (position == -1)
                    ufi = new UtxoFileItem(firstSize.Value * 2);
                else
                    ufi = SHAREDDATA.FromBinary<UtxoFileItem>(udb.GetUtxoData(position.Value));

                if (isFirst)
                {
                    firstPosition = position;
                    firstSize = ufi.Size;

                    isFirst = false;
                }

                for (int i = 0; i < ufi.Size; i++)
                    if (ufi.utxos[i].IsEmpty)
                    {
                        ufi.utxos[i].Reset(blockIndex, txIndex, txOutIndex, amount);

                        if (!position.HasValue)
                            ufptemp.Add(address, udb.AddUtxoData(ufi.ToBinary()));
                        else if (position == -1)
                        {
                            ufi.Update(firstPosition.Value);
                            if (isExistedInTemp)
                                ufptemp.Update(address, udb.AddUtxoData(ufi.ToBinary()));
                            else
                                ufptemp.Add(address, udb.AddUtxoData(ufi.ToBinary()));
                        }
                        else
                            udb.UpdateUtxoData(position.Value, ufi.ToBinary());

                        isProcessed = true;

                        break;
                    }

                if (isProcessed)
                    break;

                position = ufi.nextPosition;
            }
        }

        public void RemoveUtxo(Sha256Ripemd160Hash address, long blockIndex, int txIndex, int txOutIndex)
        {
            long? position = ufptemp.Get(address);
            if (!position.HasValue)
                position = ufp.Get(address);

            if (!position.HasValue)
                throw new InvalidOperationException("utxo_address_not_exist");

            bool isProcessed = false;

            while (position.Value != -1)
            {
                UtxoFileItem ufi = SHAREDDATA.FromBinary<UtxoFileItem>(udb.GetUtxoData(position.Value));

                for (int i = 0; i < ufi.Size; i++)
                    if (ufi.utxos[i].IsMatch(blockIndex, txIndex, txOutIndex))
                    {
                        ufi.utxos[i].Empty();

                        udb.UpdateUtxoData(position.Value, ufi.ToBinary());

                        isProcessed = true;

                        break;
                    }

                if (isProcessed)
                    break;

                position = ufi.nextPosition;
            }

            if (!isProcessed)
                throw new InvalidOperationException("utxo_not_exist");
        }

        public void SaveUFPTemp()
        {
            ufptempdb.UpdateData(ufptemp.ToBinary());
        }

        public Utxo FindUtxo(Sha256Ripemd160Hash address, long blockIndex, int txIndex, int txOutIndex)
        {
            long? position = ufptemp.Get(address);
            if (!position.HasValue)
                position = ufp.Get(address);

            if (!position.HasValue)
                return null;

            while (position.Value != -1)
            {
                UtxoFileItem ufi = SHAREDDATA.FromBinary<UtxoFileItem>(udb.GetUtxoData(position.Value));

                for (int i = 0; i < ufi.Size; i++)
                    if (ufi.utxos[i].IsMatch(blockIndex, txIndex, txOutIndex))
                        return ufi.utxos[i];

                position = ufi.nextPosition;
            }

            return null;
        }

        public List<Utxo> GetAllUtxosLatestFirst(Sha256Ripemd160Hash address)
        {
            Utxo latestUtxo = null;
            List<Utxo> utxos = new List<Utxo>();

            long? position = ufptemp.Get(address);
            if (!position.HasValue)
                position = ufp.Get(address);

            if (position.HasValue)
                while (position.Value != -1)
                {
                    UtxoFileItem ufi = SHAREDDATA.FromBinary<UtxoFileItem>(udb.GetUtxoData(position.Value));

                    for (int i = 0; i < ufi.Size; i++)
                        if (!ufi.utxos[i].IsEmpty)
                            if (latestUtxo == null)
                                latestUtxo = ufi.utxos[i];
                            else if (ufi.utxos[i].blockIndex > latestUtxo.blockIndex)
                            {
                                utxos.Add(latestUtxo);
                                latestUtxo = ufi.utxos[i];
                            }
                            else
                                utxos.Add(ufi.utxos[i]);

                    position = ufi.nextPosition;
                }

            if (latestUtxo != null)
                utxos.Insert(0, latestUtxo);

            return utxos;
        }

        //prevTxOutssは全ての取引に対するものを含んでいなければならないことに注意
        //貨幣移動取引のみならず貨幣生成取引に対するもの（貨幣生成取引の場合取引入力は0個であるため空になる筈）も含んでいなければならない
        public void ApplyBlock(Block block, TransactionOutput[][] prevTxOutss)
        {
            if (block.Transactions.Length != prevTxOutss.Length)
                throw new InvalidOperationException("apply_blk_num_of_txs_mismatch");

            block.Transactions.ForEach((i, tx) =>
            {
                if (tx.TxInputs.Length != prevTxOutss[i].Length)
                    throw new InvalidOperationException("apply_blk_num_of_txin_and_txout_mismatch");

                tx.TxInputs.ForEach((j, txIn) => RemoveUtxo(prevTxOutss[i][j].Address, txIn.PrevTxBlockIndex, txIn.PrevTxIndex, txIn.PrevTxOutputIndex));
                tx.TxOutputs.ForEach((j, txOut) => AddUtxo(txOut.Address, block.Index, i, j, txOut.Amount));
            });
        }

        //prevTxOutssは全ての取引に対するものを含んでいなければならないことに注意
        //貨幣移動取引のみならず貨幣生成取引に対するもの（貨幣生成取引の場合取引入力は0個であるため空になる筈）も含んでいなければならない
        public void RevertBlock(Block block, TransactionOutput[][] prevTxOutss)
        {
            if (block.Transactions.Length != prevTxOutss.Length)
                throw new InvalidOperationException("revert_blk_num_of_txs_mismatch");

            block.Transactions.ForEach((i, tx) =>
            {
                if (tx.TxInputs.Length != prevTxOutss[i].Length)
                    throw new InvalidOperationException("revert_blk_num_of_txin_and_txout_mismatch");

                tx.TxInputs.ForEach((j, txIn) => AddUtxo(prevTxOutss[i][j].Address, txIn.PrevTxBlockIndex, txIn.PrevTxIndex, txIn.PrevTxOutputIndex, prevTxOutss[i][j].Amount));
                tx.TxOutputs.ForEach((j, txOut) => RemoveUtxo(txOut.Address, block.Index, i, j));
            });
        }
    }

    //2014/11/27 試験済
    public class UtxoFilePointers : SHAREDDATA
    {
        public UtxoFilePointers() : base(null) { addressFilePointers = new Dictionary<Sha256Ripemd160Hash, long>(); }

        private Dictionary<Sha256Ripemd160Hash, long> addressFilePointers;

        protected override Func<ReaderWriter, IEnumerable<MainDataInfomation>> StreamInfo { get { return (msrw) => StreamInfoInner(msrw); } }
        private IEnumerable<MainDataInfomation> StreamInfoInner(ReaderWriter msrw)
        {
            Sha256Ripemd160Hash[] addresses = addressFilePointers.Keys.ToArray();
            long[] positions = addressFilePointers.Values.ToArray();

            yield return new MainDataInfomation(typeof(Sha256Ripemd160Hash[]), null, null, () => addresses, (o) => addresses = (Sha256Ripemd160Hash[])o);
            yield return new MainDataInfomation(typeof(long[]), null, () => positions, (o) =>
            {
                positions = (long[])o;

                if (addresses.Length != positions.Length)
                    throw new InvalidOperationException();

                addressFilePointers = new Dictionary<Sha256Ripemd160Hash, long>();
                for (int i = 0; i < addresses.Length; i++)
                    addressFilePointers.Add(addresses[i], positions[i]);
            });
        }

        public void Add(Sha256Ripemd160Hash address, long position)
        {
            if (addressFilePointers.Keys.Contains(address))
                throw new InvalidOperationException();

            addressFilePointers.Add(address, position);
        }

        public void Remove(Sha256Ripemd160Hash address)
        {
            if (!addressFilePointers.Keys.Contains(address))
                throw new InvalidOperationException();

            addressFilePointers.Remove(address);
        }

        public void Update(Sha256Ripemd160Hash address, long positionNew)
        {
            if (!addressFilePointers.Keys.Contains(address))
                throw new InvalidOperationException();

            addressFilePointers[address] = positionNew;
        }

        public void AddOrUpdate(Sha256Ripemd160Hash address, long position)
        {
            if (addressFilePointers.Keys.Contains(address))
                addressFilePointers[address] = position;
            else
                addressFilePointers.Add(address, position);
        }

        public long? Get(Sha256Ripemd160Hash address)
        {
            return addressFilePointers.Keys.Contains(address) ? (long?)addressFilePointers[address] : null;
        }

        public Dictionary<Sha256Ripemd160Hash, long> GetAll() { return addressFilePointers; }
    }

    public class Utxo : SHAREDDATA
    {
        public Utxo() : base(null) { amount = new CurrencyUnit(0); }

        public Utxo(long _blockIndex, int _txIndex, int _txOutIndex, CurrencyUnit _amount)
            : base(null)
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

        public bool IsEmpty { get { return blockIndex == 0; } }

        public void Empty()
        {
            blockIndex = 0;
            txIndex = 0;
            txOutIndex = 0;
            amount = CurrencyUnit.Zero;
        }

        public void Reset(long _blockIndex, int _txIndex, int _txOutIndex, CurrencyUnit _amount)
        {
            blockIndex = _blockIndex;
            txIndex = _txIndex;
            txOutIndex = _txOutIndex;
            amount = _amount;
        }

        public bool IsMatch(long _blockIndex, int _txIndex, int _txOutIndex)
        {
            return blockIndex == _blockIndex && txIndex == _txIndex && txOutIndex == _txOutIndex;
        }

        public bool IsMatch(long _blockIndex, int _txIndex, int _txOutIndex, CurrencyUnit _amount)
        {
            return blockIndex == _blockIndex && txIndex == _txIndex && txOutIndex == _txOutIndex && amount.Amount == _amount.Amount;
        }
    }

    //2014/11/26 試験済
    public class UtxoFileItem : SHAREDDATA
    {
        public UtxoFileItem() : base(null) { }

        public UtxoFileItem(int _size)
            : base(null)
        {
            utxos = new Utxo[_size];
            for (int i = 0; i < utxos.Length; i++)
                utxos[i] = new Utxo();
            nextPosition = -1;
        }

        public Utxo[] utxos { get; private set; }
        public long nextPosition { get; private set; }

        public int Size { get { return utxos.Length; } }

        protected override Func<ReaderWriter, IEnumerable<MainDataInfomation>> StreamInfo
        {
            get
            {
                return (msrw) => new MainDataInfomation[]{
                    new MainDataInfomation(typeof(Utxo[]), null, null, () => utxos, (o) => utxos = (Utxo[])o),
                    new MainDataInfomation(typeof(long), () => nextPosition, (o) => nextPosition = (long)o),
                };
            }
        }

        public void Update(long nextPositionNew) { nextPosition = nextPositionNew; }
    }

    #endregion

    public enum TransactionHistoryType { mined, transfered, sent, received }

    public class TransactionHistory : SHAREDDATA
    {
        public TransactionHistory() : base(0) { }

        public TransactionHistory(bool _isValid, bool _isConfirmed, TransactionHistoryType _type, DateTime _datetime, long _blockIndex, Sha256Sha256Hash _id, TransactionOutput[] _senders, TransactionOutput[] _receivers, Transaction _transaction, TransactionOutput[] _prevTxOuts, CurrencyUnit _amount)
            : base(0)
        {
            isValid = _isValid;
            isConfirmed = _isConfirmed;
            type = _type;
            datetime = _datetime;
            blockIndex = _blockIndex;
            id = _id;
            senders = _senders;
            receivers = _receivers;
            transaction = _transaction;
            prevTxOuts = _prevTxOuts;
            amount = _amount;
        }

        public bool isValid { get; private set; }
        public bool isConfirmed { get; private set; }
        public TransactionHistoryType type { get; private set; }
        public DateTime datetime { get; private set; }
        public long blockIndex { get; private set; }
        public Sha256Sha256Hash id { get; private set; }
        public TransactionOutput[] senders { get; private set; }
        public TransactionOutput[] receivers { get; private set; }
        public Transaction transaction { get; private set; }
        public TransactionOutput[] prevTxOuts { get; private set; }
        public CurrencyUnit amount { get; private set; }

        protected override Func<ReaderWriter, IEnumerable<MainDataInfomation>> StreamInfo
        {
            get
            {
                if (Version == 0)
                    return (msrw) => new MainDataInfomation[]{
                        new MainDataInfomation(typeof(bool), () => isValid, (o) => isValid = (bool)o),
                        new MainDataInfomation(typeof(bool), () => isConfirmed, (o) => isConfirmed = (bool)o),
                        new MainDataInfomation(typeof(int), () => (int)type, (o) => type = (TransactionHistoryType)o),
                        new MainDataInfomation(typeof(DateTime), () => datetime, (o) => datetime = (DateTime)o),
                        new MainDataInfomation(typeof(long), () => blockIndex, (o) => blockIndex = (long)o),
                        new MainDataInfomation(typeof(Sha256Sha256Hash), null, () => id, (o) => id = (Sha256Sha256Hash)o),
                        new MainDataInfomation(typeof(TransactionOutput[]), 0, null, () => senders, (o) => senders = (TransactionOutput[])o),
                        new MainDataInfomation(typeof(TransactionOutput[]), 0, null, () => receivers, (o) => receivers = (TransactionOutput[])o),
                        new MainDataInfomation(typeof(Transaction), 0, () => transaction, (o) => transaction = (Transaction)o),
                        new MainDataInfomation(typeof(TransactionOutput[]), 0, null, () => prevTxOuts, (o) => prevTxOuts = (TransactionOutput[])o),
                        new MainDataInfomation(typeof(long), () => amount.rawAmount, (o) => amount = new CurrencyUnit((long)o)),
                    };
                else
                    throw new NotSupportedException();
            }
        }

        public override bool IsVersioned { get { return true; } }
        public override bool IsCorruptionChecked { get { return false; } }
    }

    public class TransactionHistories : SHAREDDATA
    {
        public TransactionHistories()
            : base(0)
        {
            invalidTransactionHistories = new List<TransactionHistory>();
            unconfirmedTransactionHistories = new List<TransactionHistory>();
            confirmedTransactionHistories = new List<TransactionHistory>();
        }

        public List<TransactionHistory> invalidTransactionHistories { get; private set; }
        public List<TransactionHistory> unconfirmedTransactionHistories { get; private set; }
        public List<TransactionHistory> confirmedTransactionHistories { get; private set; }

        public event EventHandler<TransactionHistory> InvalidTransactionAdded = delegate { };
        public event EventHandler<TransactionHistory> InvalidTransactionRemoved = delegate { };
        public event EventHandler<TransactionHistory> UnconfirmedTransactionAdded = delegate { };
        public event EventHandler<TransactionHistory> UnconfirmedTransactionRemoved = delegate { };
        public event EventHandler<TransactionHistory> ConfirmedTransactionAdded = delegate { };
        public event EventHandler<TransactionHistory> ConfirmedTransactionRemoved = delegate { };

        public void AddTransactionHistory(TransactionHistory th)
        {
            if (!th.isValid)
            {
                invalidTransactionHistories.Insert(0, th);

                InvalidTransactionAdded(this, th);
            }
            else if (!th.isConfirmed)
            {
                unconfirmedTransactionHistories.Insert(0, th);

                UnconfirmedTransactionAdded(this, th);
            }
            else
            {
                confirmedTransactionHistories.Insert(0, th);

                ConfirmedTransactionAdded(this, th);
            }
        }

        public void RemoveInvalidTransactionHistory(Sha256Sha256Hash id)
        {
            TransactionHistory th = invalidTransactionHistories.FirstOrDefault((elem) => elem.id.Equals(id));

            if (th != null)
            {
                invalidTransactionHistories.Remove(th);

                InvalidTransactionRemoved(this, th);
            }
        }

        public void RemoveUnconfirmedTransactionHistory(Sha256Sha256Hash id)
        {
            TransactionHistory th = unconfirmedTransactionHistories.FirstOrDefault((elem) => elem.id.Equals(id));

            if (th != null)
            {
                unconfirmedTransactionHistories.Remove(th);

                UnconfirmedTransactionRemoved(this, th);
            }
        }

        public void RemoveConfirmedTransactionHistory(Sha256Sha256Hash id)
        {
            TransactionHistory th = confirmedTransactionHistories.FirstOrDefault((elem) => elem.id.Equals(id));

            if (th != null)
            {
                confirmedTransactionHistories.Remove(th);

                ConfirmedTransactionRemoved(this, th);
            }
        }

        public TransactionHistory ContainsInvalidTransactionHistory(Sha256Sha256Hash id)
        {
            return invalidTransactionHistories.FirstOrDefault((elem) => elem.id.Equals(id));
        }

        public TransactionHistory ContainsUnconformedTransactionHistory(Sha256Sha256Hash id)
        {
            return unconfirmedTransactionHistories.FirstOrDefault((elem) => elem.id.Equals(id));
        }

        public TransactionHistory ContainsConformedTransactionHistory(Sha256Sha256Hash id)
        {
            return confirmedTransactionHistories.FirstOrDefault((elem) => elem.id.Equals(id));
        }

        protected override Func<ReaderWriter, IEnumerable<MainDataInfomation>> StreamInfo
        {
            get
            {
                if (Version == 0)
                    return (msrw) => new MainDataInfomation[]{
                        new MainDataInfomation(typeof(TransactionHistory[]), 0, null, () => invalidTransactionHistories.ToArray(), (o) => invalidTransactionHistories = new List<TransactionHistory>((TransactionHistory[])o)),
                        new MainDataInfomation(typeof(TransactionHistory[]), 0, null, () => unconfirmedTransactionHistories.ToArray(), (o) => unconfirmedTransactionHistories = new List<TransactionHistory>((TransactionHistory[])o)),
                        new MainDataInfomation(typeof(TransactionHistory[]), 0, null, () => confirmedTransactionHistories.ToArray(), (o) => confirmedTransactionHistories = new List<TransactionHistory>((TransactionHistory[])o)),
                    };
                else
                    throw new NotSupportedException();
            }
        }

        public override bool IsVersioned { get { return true; } }
        public override bool IsCorruptionChecked { get { return true; } }
    }

    public class Mining
    {
        public Mining()
        {
            are = new AutoResetEvent(false);

            this.StartTask("mining", "mining", () =>
            {
                byte[] bytes = new byte[10];
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

                        are.Reset();
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

        public string GetPath() { return Path.Combine(pathBase, filenameBase); }
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

    public class TransactionHistoriesDatabase : SimpleDatabase
    {
        public TransactionHistoriesDatabase(string _pathBase) : base(_pathBase) { }

        protected override int version { get { return 0; } }

#if TEST
        protected override string filenameBase { get { return "th_test" + version.ToString(); } }
#else
        protected override string filenameBase { get { return "th"; } }
#endif
    }

    public class BlockchainAccessDB : DATABASEBASE
    {
        public BlockchainAccessDB(string _pathBase) : base(_pathBase) { }

        protected override int version { get { return 0; } }

#if TEST
        protected override string filenameBase { get { return "blkchain_access_test" + version.ToString(); } }
#else
        protected override string filenameBase { get { return "utxos" + version.ToString(); } }
#endif

        public void Create()
        {
            try
            {
                string path = GetPath();

                if (File.Exists(path))
                    throw new InvalidOperationException("blkchain_access_file_exist");

                File.WriteAllText(path, string.Empty);
            }
            catch (Exception ex)
            {
                throw new ApplicationException("fatal:blkchain_access", ex);
            }
        }

        public void Delete()
        {
            try
            {
                string path = GetPath();

                if (!File.Exists(path))
                    throw new InvalidOperationException("blkchain_access_file_not_exist");

                File.Delete(path);
            }
            catch (Exception ex)
            {
                throw new ApplicationException("fatal:blkchain_access", ex);
            }
        }

        public string GetPath() { return System.IO.Path.Combine(pathBase, filenameBase); }
    }

    public class BlockManagerDB : SimpleDatabase
    {
        public BlockManagerDB(string _pathBase) : base(_pathBase) { }

        protected override int version { get { return 0; } }

#if TEST
        protected override string filenameBase { get { return "blk_mng_test_" + version.ToString(); } }
#else
        protected override string filenameBase { get { return "blk_mng_" + version.ToString(); } }
#endif
    }

    //2014/12/01 試験済
    public class BlockDB : DATABASEBASE
    {
        public BlockDB(string _pathBase) : base(_pathBase) { }

        protected override int version { get { return 0; } }

#if TEST
        protected override string filenameBase { get { return "blks_test" + version.ToString() + "_"; } }
#else
        protected override string filenameBase { get { return "blks" + version.ToString() + "_"; } }
#endif

        public byte[] GetBlockData(long blockFileIndex, long position)
        {
            using (FileStream fs = new FileStream(GetPath(blockFileIndex), FileMode.OpenOrCreate, FileAccess.Read))
                return GetBlockData(fs, position);
        }

        public byte[][] GetBlockDatas(long blockFileIndex, long[] positions)
        {
            byte[][] blockDatas = new byte[positions.Length][];

            using (FileStream fs = new FileStream(GetPath(blockFileIndex), FileMode.OpenOrCreate, FileAccess.Read))
                for (int i = 0; i < positions.Length; i++)
                    blockDatas[i] = GetBlockData(fs, positions[i]);

            return blockDatas;
        }

        public long AddBlockData(long blockFileIndex, byte[] blockData)
        {
            using (FileStream fs = new FileStream(GetPath(blockFileIndex), FileMode.Append, FileAccess.Write))
                return AddBlockData(fs, blockFileIndex, blockData);
        }

        public long[] AddBlockDatas(long blockFileIndex, byte[][] blockDatas)
        {
            long[] positions = new long[blockDatas.Length];

            using (FileStream fs = new FileStream(GetPath(blockFileIndex), FileMode.Append, FileAccess.Write))
                for (int i = 0; i < blockDatas.Length; i++)
                    positions[i] = AddBlockData(fs, blockFileIndex, blockDatas[i]);

            return positions;
        }

        private byte[] GetBlockData(FileStream fs, long position)
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

        private long AddBlockData(FileStream fs, long blockFileIndex, byte[] blockData)
        {
            long position = fs.Position;

            fs.Write(BitConverter.GetBytes(blockData.Length), 0, 4);
            fs.Write(blockData, 0, blockData.Length);

            return position;
        }

        public string GetPath(long blockFileIndex) { return System.IO.Path.Combine(pathBase, filenameBase + blockFileIndex.ToString()); }
    }

    //2014/12/01 試験済
    public class BlockFilePointersDB : DATABASEBASE
    {
        public BlockFilePointersDB(string _pathBase) : base(_pathBase) { }

        protected override int version { get { return 0; } }

#if TEST
        protected override string filenameBase { get { return "blks_index_test" + version.ToString(); } }
#else
        protected override string filenameBase { get { return "blks_index_test" + version.ToString(); } }
#endif

        public long GetBlockFilePointerData(long blockIndex)
        {
            using (FileStream fs = new FileStream(GetPath(), FileMode.OpenOrCreate, FileAccess.Read))
            {
                if (fs.Length % 8 != 0)
                    throw new InvalidOperationException("fatal:bfpdb");

                long position = blockIndex * 8;

                if (position >= fs.Length)
                    return -1;

                fs.Seek(position, SeekOrigin.Begin);

                byte[] blockPointerData = new byte[8];
                fs.Read(blockPointerData, 0, 8);
                return BitConverter.ToInt64(blockPointerData, 0);
            }
        }

        public void UpdateBlockFilePointerData(long blockIndex, long blockFilePointerData)
        {
            using (FileStream fs = new FileStream(GetPath(), FileMode.OpenOrCreate, FileAccess.Write))
            {
                if (fs.Length % 8 != 0)
                    throw new InvalidOperationException("fatal:bfpdb");

                long position = blockIndex * 8;

                if (position >= fs.Length)
                {
                    fs.Seek(fs.Length, SeekOrigin.Begin);
                    while (position > fs.Length)
                        fs.Write(BitConverter.GetBytes((long)-1), 0, 8);
                    fs.Write(BitConverter.GetBytes(blockFilePointerData), 0, 8);
                }
                else
                {
                    fs.Seek(position, SeekOrigin.Begin);
                    fs.Write(BitConverter.GetBytes(blockFilePointerData), 0, 8);
                }
            }
        }

        public string GetPath() { return System.IO.Path.Combine(pathBase, filenameBase); }
    }

    public class UtxoFileAccessDB : DATABASEBASE
    {
        public UtxoFileAccessDB(string _pathBase) : base(_pathBase) { }

        protected override int version { get { return 0; } }

#if TEST
        protected override string filenameBase { get { return "utxos_access_test" + version.ToString(); } }
#else
        protected override string filenameBase { get { return "utxos" + version.ToString(); } }
#endif

        public void Create()
        {
            try
            {
                string path = GetPath();

                if (File.Exists(path))
                    throw new InvalidOperationException("utxos_access_file_exist");

                File.WriteAllText(path, string.Empty);
            }
            catch (Exception ex)
            {
                throw new ApplicationException("fatal:utxos_access", ex);
            }
        }

        public void Delete()
        {
            try
            {
                string path = GetPath();

                if (!File.Exists(path))
                    throw new InvalidOperationException("utxo_access_file_not_exist");

                File.Delete(path);
            }
            catch (Exception ex)
            {
                throw new ApplicationException("fatal:utxos_access", ex);
            }
        }

        public string GetPath() { return System.IO.Path.Combine(pathBase, filenameBase); }
    }

    public class UtxoFilePointersDB : SimpleDatabase
    {
        public UtxoFilePointersDB(string _pathBase) : base(_pathBase) { }

        protected override int version { get { return 0; } }

#if TEST
        protected override string filenameBase { get { return "utxos_index_test" + version.ToString(); } }
#else
        protected override string filenameBase { get { return "utxos_index" + version.ToString(); } }
#endif
    }

    public class UtxoFilePointersTempDB : SimpleDatabase
    {
        public UtxoFilePointersTempDB(string _pathBase) : base(_pathBase) { }

        protected override int version { get { return 0; } }

#if TEST
        protected override string filenameBase { get { return "utxos_index_temp_test" + version.ToString(); } }
#else
        protected override string filenameBase { get { return "utxos_index_tamp" + version.ToString(); } }
#endif

        public void Delete()
        {
            string path = GetPath();

            if (File.Exists(path))
                File.Delete(path);
        }
    }

    //2014/11/26 試験済
    public class UtxoDB : DATABASEBASE
    {
        public UtxoDB(string _pathBase) : base(_pathBase) { }

        private FileStream fs;

        protected override int version { get { return 0; } }

#if TEST
        protected override string filenameBase { get { return "utxos_test" + version.ToString(); } }
#else
        protected override string filenameBase { get { return "utxos" + version.ToString(); } }
#endif

        public void Open()
        {
            if (fs != null)
                throw new InvalidOperationException("utxodb_open_twice");

            fs = new FileStream(GetPath(), FileMode.OpenOrCreate, FileAccess.ReadWrite);
        }

        public void Close()
        {
            if (fs == null)
                throw new InvalidOperationException("utxodb_close_twice_or_first");

            fs.Close();
            fs = null;
        }

        public void Flush()
        {
            if (fs == null)
                throw new InvalidOperationException("utxodb_not_opened");

            fs.Flush();
        }

        public byte[] GetUtxoData(long position)
        {
            if (fs == null)
                throw new InvalidOperationException("utxodb_not_opened");

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

        public long AddUtxoData(byte[] utxoData)
        {
            if (fs == null)
                throw new InvalidOperationException("utxodb_not_opened");

            long position = fs.Length;

            fs.Seek(fs.Length, SeekOrigin.Begin);
            fs.Write(BitConverter.GetBytes(utxoData.Length), 0, 4);
            fs.Write(utxoData, 0, utxoData.Length);

            return position;
        }

        public void UpdateUtxoData(long position, byte[] utxoData)
        {
            if (fs == null)
                throw new InvalidOperationException("utxodb_not_opened");

            fs.Seek(position, SeekOrigin.Begin);

            byte[] lengthBytes = new byte[4];
            fs.Read(lengthBytes, 0, 4);
            int length = BitConverter.ToInt32(lengthBytes, 0);

            if (utxoData.Length != length)
                throw new InvalidOperationException();

            fs.Write(utxoData, 0, utxoData.Length);
        }

        public string GetPath() { return System.IO.Path.Combine(pathBase, filenameBase); }
    }

    #endregion
}

namespace old
{
    using CREA2014;

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
            isVerifieds = new Dictionary<Creahash, bool>();
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
        private Dictionary<Creahash, bool> isVerifieds;

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
                    addressEventDatas.Add(txi.tx.TxOutputs[i].Address, new AddressEventData(txBlock.header.index, txi.i, i, txi.tx.TxOutputs[i].Amount));
            }
        }

        private void GoBackwardAddressEventData(TransactionalBlock txBlock)
        {
            foreach (var txi in txBlock.Transactions.Select((tx, i) => new { tx, i }))
            {
                for (int i = 0; i < txi.tx.TxInputs.Length; i++)
                    addressEventDatas.Add(new Sha256Ripemd160Hash(txi.tx.TxInputs[i].SenderPubKey.pubKey), new AddressEventData(txi.tx.TxInputs[i].PrevTxBlockIndex, txi.tx.TxInputs[i].PrevTxIndex, txi.tx.TxInputs[i].PrevTxOutputIndex, GetMainBlock(txi.tx.TxInputs[i].PrevTxBlockIndex).Transactions[txi.tx.TxInputs[i].PrevTxIndex].TxOutputs[txi.tx.TxInputs[i].PrevTxOutputIndex].Amount));
                for (int i = 0; i < txi.tx.TxOutputs.Length; i++)
                    addressEventDatas.Remove(txi.tx.TxOutputs[i].Address, txBlock.header.index, txi.i, i);
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
                    UpdateUtxosTemp(removedUtxos, addedUtxos, txi.tx.TxOutputs[i].Address, txBlock.header.index, txi.i, i);
            }
        }

        private void GoBackwardUtxosInMemory(TransactionalBlock txBlock, Dictionary<Sha256Ripemd160Hash, List<Utxo>> addedUtxos, Dictionary<Sha256Ripemd160Hash, List<Utxo>> removedUtxos)
        {
            foreach (var txi in txBlock.Transactions.Select((tx, i) => new { tx, i }))
            {
                for (int i = 0; i < txi.tx.TxInputs.Length; i++)
                    UpdateUtxosTemp(removedUtxos, addedUtxos, new Sha256Ripemd160Hash(txi.tx.TxInputs[i].SenderPubKey.pubKey), txi.tx.TxInputs[i].PrevTxBlockIndex, txi.tx.TxInputs[i].PrevTxIndex, txi.tx.TxInputs[i].PrevTxOutputIndex);
                for (int i = 0; i < txi.tx.TxOutputs.Length; i++)
                    UpdateUtxosTemp(addedUtxos, removedUtxos, txi.tx.TxOutputs[i].Address, txBlock.header.index, txi.i, i);
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
}