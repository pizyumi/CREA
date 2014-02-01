using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace CREA2014
{
    #region 基底クラス

    public abstract class CREACOINDATA { }
    public abstract class CREACOININTERNALDATA : CREACOINDATA { }
    public abstract class CREACOINSHAREDDATA : CREACOINDATA
    {
        //<未実装>圧縮機能

        private int? version;
        public int Version
        {
            get
            {
                if (!IsVersioned)
                    throw new NotSupportedException("version");
                else
                    return (int)version;
            }
        }

        public CREACOINSHAREDDATA(int? _version)
        {
            if ((IsVersioned && _version == null) || (!IsVersioned && _version != null))
                throw new ArgumentException("is_versioned_and_version");

            version = _version;
        }

        public CREACOINSHAREDDATA() : this(null) { }

        public class MainDataInfomation
        {
            //CREACOINSHAREDDATA（の派生クラス）の配列専用
            public MainDataInfomation(Type _type, int? _version, int? _length, Func<object> _getter, Action<object> _setter)
                : this(_type, _getter, _setter)
            {
                if (!Type.IsArray)
                    throw new ArgumentException("main_data_info_not_array");

                Type elementType = Type.GetElementType();
                if (!elementType.IsSubclassOf(typeof(CREACOINSHAREDDATA)))
                    throw new ArgumentException("main_data_info_not_ccsc_array");

                CREACOINSHAREDDATA ccsd = Activator.CreateInstance(elementType) as CREACOINSHAREDDATA;
                if ((!ccsd.IsVersioned && _version != null) || (ccsd.IsVersioned && _version == null))
                    throw new ArgumentException("main_data_info_not_is_versioned");

                version = _version;
                length = _length;
            }

            //CREACOINSHAREDDATA（の派生クラス）の配列以外の配列またはCREACOINSHAREDDATA（の派生クラス）専用
            public MainDataInfomation(Type _type, int? _lengthOrVersion, Func<object> _getter, Action<object> _setter)
                : this(_type, _getter, _setter)
            {
                if (Type.IsArray)
                    if (Type.GetElementType().IsSubclassOf(typeof(CREACOINSHAREDDATA)))
                        throw new ArgumentException("main_data_info_ccsc_array");
                    else
                        length = _lengthOrVersion;
                else if (Type.IsSubclassOf(typeof(CREACOINSHAREDDATA)))
                {
                    CREACOINSHAREDDATA ccsd = Activator.CreateInstance(Type) as CREACOINSHAREDDATA;
                    if ((!ccsd.IsVersioned && _lengthOrVersion != null) || (ccsd.IsVersioned && _lengthOrVersion == null))
                        throw new ArgumentException("main_data_info_not_is_versioned");

                    version = _lengthOrVersion;
                }
                else
                    throw new ArgumentException("main_data_info_not_bytes_ccsd");
            }

            public MainDataInfomation(Type _type, Func<object> _getter, Action<object> _setter)
            {
                Type = _type;
                Getter = _getter;
                Setter = _setter;
            }

            public readonly Type Type;
            private readonly int? length;
            public int? Length
            {
                get
                {
                    if (Type.IsArray)
                        return length;
                    else
                        throw new NotSupportedException("main_data_info_length");
                }
            }
            private readonly int? version;
            public int Version
            {
                get
                {
                    CREACOINSHAREDDATA ccsd;
                    if (Type.IsSubclassOf(typeof(CREACOINSHAREDDATA)))
                        ccsd = Activator.CreateInstance(Type) as CREACOINSHAREDDATA;
                    else if (Type.IsArray)
                    {
                        Type elementType = Type.GetElementType();
                        if (elementType.IsSubclassOf(typeof(CREACOINSHAREDDATA)))
                            ccsd = Activator.CreateInstance(elementType) as CREACOINSHAREDDATA;
                        else
                            throw new NotSupportedException("main_data_info_version");
                    }
                    else
                        throw new NotSupportedException("main_data_info_version");

                    if (!ccsd.IsVersioned)
                        throw new NotSupportedException("main_data_info_is_versioned");
                    else
                        return (int)version;
                }
            }
            public readonly Func<object> Getter;
            public readonly Action<object> Setter;
        }

        protected abstract MainDataInfomation[] MainDataInfo { get; }
        protected abstract bool IsVersioned { get; }
        protected abstract bool IsCorruptionChecked { get; }

        public int? Length
        {
            get
            {
                int length = 0;
                foreach (var mdi in MainDataInfo)
                {
                    Func<Type, MainDataInfomation, int?> _GetLength = (type, innerMdi) =>
                    {
                        if (type == typeof(bool) || type == typeof(byte))
                            return 1;
                        else if (type == typeof(int))
                            return 4;
                        else if (type == typeof(long) || type == typeof(double) || type == typeof(DateTime))
                            return 8;
                        else if (type == typeof(string))
                            return null;
                        else if (type.IsSubclassOf(typeof(CREACOINSHAREDDATA)))
                        {
                            CREACOINSHAREDDATA ccsd = Activator.CreateInstance(type) as CREACOINSHAREDDATA;
                            if (ccsd.IsVersioned)
                                ccsd.version = innerMdi.Version;

                            if (ccsd.Length == null)
                                return null;
                            else
                            {
                                int innerLength = 0;

                                if (ccsd.IsVersioned)
                                    innerLength += 4;
                                if (ccsd.IsCorruptionChecked)
                                    innerLength += 4;

                                return innerLength + (int)ccsd.Length;
                            }
                        }
                        else
                            throw new NotSupportedException("length_not_supported");
                    };

                    if (mdi.Type.IsArray)
                    {
                        if (mdi.Length == null)
                            return null;
                        else
                        {
                            int? innerLength = _GetLength(mdi.Type.GetElementType(), mdi);
                            if (innerLength == null)
                                return null;
                            else
                                length += (int)mdi.Length * (int)innerLength;
                        }
                    }
                    else
                    {
                        int? innerLength = _GetLength(mdi.Type, mdi);
                        if (innerLength == null)
                            return null;
                        else
                            length += (int)innerLength;
                    }
                }
                return length;
            }
        }

        public byte[] ToBinary()
        {
            byte[] mainDataBytes;
            using (MemoryStream ms = new MemoryStream())
            {
                foreach (var mdi in MainDataInfo)
                {
                    Action<Type, MainDataInfomation, object, MemoryStream> _Write = (type, innerMdi, innerObj, innerMs) =>
                    {
                        if (type == typeof(bool))
                            innerMs.Write(BitConverter.GetBytes((bool)innerObj), 0, 1);
                        else if (type == typeof(int))
                            innerMs.Write(BitConverter.GetBytes((int)innerObj), 0, 4);
                        else if (type == typeof(long))
                            innerMs.Write(BitConverter.GetBytes((long)innerObj), 0, 8);
                        else if (type == typeof(double))
                            innerMs.Write(BitConverter.GetBytes((double)innerObj), 0, 8);
                        else if (type == typeof(DateTime))
                            innerMs.Write(BitConverter.GetBytes(((DateTime)innerObj).ToBinary()), 0, 8);
                        else if (type == typeof(string))
                        {
                            byte[] bytes = Encoding.UTF8.GetBytes((string)innerObj);
                            innerMs.Write(BitConverter.GetBytes(bytes.Length), 0, 4);
                            innerMs.Write(bytes, 0, bytes.Length);
                        }
                        else if (type.IsSubclassOf(typeof(CREACOINSHAREDDATA)))
                        {
                            CREACOINSHAREDDATA ccsd = innerObj as CREACOINSHAREDDATA;
                            if (ccsd.IsVersioned)
                                ccsd.version = innerMdi.Version;

                            byte[] bytes = ccsd.ToBinary();
                            if (ccsd.Length == null)
                                innerMs.Write(BitConverter.GetBytes(bytes.Length), 0, 4);
                            innerMs.Write(bytes, 0, bytes.Length);
                        }
                        else
                            throw new NotSupportedException("to_binary_not_supported");
                    };

                    object o = mdi.Getter();

                    if (mdi.Type == typeof(byte[]))
                    {
                        if (mdi.Length == null)
                            ms.Write(BitConverter.GetBytes(((byte[])o).Length), 0, 4);
                        ms.Write((byte[])o, 0, ((byte[])o).Length);
                    }
                    else if (mdi.Type.IsArray)
                    {
                        object[] os = o as object[];
                        Type elementType = mdi.Type.GetElementType();

                        if (mdi.Length == null)
                            ms.Write(BitConverter.GetBytes(os.Length), 0, 4);
                        foreach (var innerObj in os)
                            _Write(elementType, mdi, innerObj, ms);
                    }
                    else
                        _Write(mdi.Type, mdi, o, ms);
                }

                mainDataBytes = ms.ToArray();
            }
            using (MemoryStream ms = new MemoryStream())
            {
                if (IsVersioned)
                    ms.Write(BitConverter.GetBytes((int)version), 0, 4);
                //破損検査のためのデータ（主データのハッシュ値の先頭4バイト）
                if (IsCorruptionChecked)
                    ms.Write(mainDataBytes.ComputeSha256(), 0, 4);
                ms.Write(mainDataBytes, 0, mainDataBytes.Length);

                return ms.ToArray();
            }
        }

        public void FromBinary(byte[] binary)
        {
            byte[] mainDataBytes;
            using (MemoryStream ms = new MemoryStream(binary))
            {
                if (IsVersioned)
                {
                    byte[] versionBytes = new byte[4];
                    ms.Read(versionBytes, 0, 4);
                    version = BitConverter.ToInt32(versionBytes, 0);
                }

                int? check = null;
                if (IsCorruptionChecked)
                {
                    byte[] checkBytes = new byte[4];
                    ms.Read(checkBytes, 0, 4);
                    check = BitConverter.ToInt32(checkBytes, 0);
                }

                int length = (int)(ms.Length - ms.Position);
                mainDataBytes = new byte[length];
                ms.Read(mainDataBytes, 0, length);

                if (IsCorruptionChecked && check != BitConverter.ToInt32(mainDataBytes.ComputeSha256(), 0))
                    throw new InvalidDataException("from_binary_check_inaccurate");
            }
            using (MemoryStream ms = new MemoryStream(mainDataBytes))
            {
                foreach (var mdi in MainDataInfo)
                {
                    Func<Type, MainDataInfomation, MemoryStream, object> _Read = (type, innerMdi, innerMs) =>
                    {
                        if (type == typeof(bool))
                        {
                            byte[] bytes = new byte[1];
                            innerMs.Read(bytes, 0, 1);
                            return BitConverter.ToBoolean(bytes, 0);
                        }
                        else if (type == typeof(int))
                        {
                            byte[] bytes = new byte[4];
                            innerMs.Read(bytes, 0, 4);
                            return BitConverter.ToInt32(bytes, 0);
                        }
                        else if (type == typeof(long))
                        {
                            byte[] bytes = new byte[8];
                            innerMs.Read(bytes, 0, 8);
                            return BitConverter.ToInt64(bytes, 0);
                        }
                        else if (type == typeof(double))
                        {
                            byte[] bytes = new byte[8];
                            innerMs.Read(bytes, 0, 8);
                            return BitConverter.ToDouble(bytes, 0);
                        }
                        else if (type == typeof(DateTime))
                        {
                            byte[] bytes = new byte[8];
                            innerMs.Read(bytes, 0, 8);
                            return DateTime.FromBinary(BitConverter.ToInt64(bytes, 0));
                        }
                        else if (type == typeof(string))
                        {
                            byte[] lengthBytes = new byte[4];
                            innerMs.Read(lengthBytes, 0, 4);
                            int length = BitConverter.ToInt32(lengthBytes, 0);

                            byte[] bytes = new byte[length];
                            innerMs.Read(bytes, 0, length);
                            return Encoding.UTF8.GetString(bytes);
                        }
                        else if (type.IsSubclassOf(typeof(CREACOINSHAREDDATA)))
                        {
                            CREACOINSHAREDDATA ccsd = Activator.CreateInstance(type) as CREACOINSHAREDDATA;
                            if (ccsd.IsVersioned)
                                ccsd.version = innerMdi.Version;

                            int length;
                            if (ccsd.Length == null)
                            {
                                byte[] lengthBytes = new byte[4];
                                innerMs.Read(lengthBytes, 0, 4);
                                length = BitConverter.ToInt32(lengthBytes, 0);
                            }
                            else
                            {
                                length = (int)ccsd.Length;
                                if (ccsd.IsVersioned)
                                    length += 4;
                                if (ccsd.IsCorruptionChecked)
                                    length += 4;
                            }

                            byte[] bytes = new byte[length];
                            innerMs.Read(bytes, 0, length);

                            ccsd.FromBinary(bytes);

                            return ccsd;
                        }
                        else
                            throw new NotSupportedException("from_binary_not_supported");
                    };

                    if (mdi.Type == typeof(byte[]))
                    {
                        int length;
                        if (mdi.Length == null)
                        {
                            byte[] lengthBytes = new byte[4];
                            ms.Read(lengthBytes, 0, 4);
                            length = BitConverter.ToInt32(lengthBytes, 0);
                        }
                        else
                            length = (int)mdi.Length;

                        byte[] bytes = new byte[length];
                        ms.Read(bytes, 0, length);
                        mdi.Setter(bytes);
                    }
                    else if (mdi.Type.IsArray)
                    {
                        Type elementType = mdi.Type.GetElementType();

                        int length;
                        if (mdi.Length == null)
                        {
                            byte[] lengthBytes = new byte[4];
                            ms.Read(lengthBytes, 0, 4);
                            length = BitConverter.ToInt32(lengthBytes, 0);
                        }
                        else
                            length = (int)mdi.Length;

                        object[] os = Array.CreateInstance(elementType, length) as object[];
                        for (int i = 0; i < os.Length; i++)
                            os[i] = _Read(elementType, mdi, ms);

                        mdi.Setter(os);
                    }
                    else
                        mdi.Setter(_Read(mdi.Type, mdi, ms));
                }
            }
        }

        public static T FromBin<T>(byte[] binary) where T : CREACOINSHAREDDATA, new()
        {
            return new T().Operate((o) => o.FromBinary(binary));
        }
    }

    #endregion

    public class CREACOINCore
    {
        private AccountHolderDatabase accountHolderDatabase;
        public AccountHolderDatabase AccountHolderDatabase
        {
            get { return accountHolderDatabase; }
        }

        public CREACOINCore(string _basepath)
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
                if (isSystemStarted.RaiseError(this.GetType(), "core_started", 5))
                    return;

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
                if (!isSystemStarted.NotRaiseError(this.GetType(), "core_not_started", 5))
                    return;

                File.WriteAllBytes(accountHolderDatabasePath, accountHolderDatabase.ToBinary());

                isSystemStarted = false;
            }
        }
    }

    public abstract class NetworkParameter
    {
        public static readonly int ProtocolVersion = 0;
    }

    public class Sha256Hash : CREACOINSHAREDDATA, IComparable<Sha256Hash>, IEquatable<Sha256Hash>, IComparable
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

        public Sha256Hash() { }

        protected override MainDataInfomation[] MainDataInfo
        {
            get
            {
                return new MainDataInfomation[]{
                    new MainDataInfomation(typeof(byte[]), 32, () => bytes, (o) => bytes = (byte[])o), 
                };
            }
        }

        protected override bool IsVersioned
        {
            get { return false; }
        }

        protected override bool IsCorruptionChecked
        {
            get { return false; }
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
            byte[] ramdomBytes = bytes.BytesRandom();
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

    public class Ripemd160Hash : CREACOINSHAREDDATA, IComparable<Ripemd160Hash>, IEquatable<Ripemd160Hash>, IComparable
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

        public Ripemd160Hash() { }

        protected override MainDataInfomation[] MainDataInfo
        {
            get
            {
                return new MainDataInfomation[]{
                    new MainDataInfomation(typeof(byte[]), 20, () => bytes, (o) => bytes = (byte[])o), 
                };
            }
        }

        protected override bool IsVersioned
        {
            get { return false; }
        }

        protected override bool IsCorruptionChecked
        {
            get { return false; }
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
            byte[] ramdomBytes = bytes.BytesRandom();
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

    public abstract class CREASIGNATUREDATA : CREACOINSHAREDDATA
    {
        protected int keyLength;
        public int KeyLength
        {
            get { return keyLength; }
        }

        protected byte[] publicKey;
        public byte[] PublicKey
        {
            get { return publicKey; }
        }

        protected byte[] privateKey;
        public byte[] PrivateKey
        {
            get { return privateKey; }
        }

        public CREASIGNATUREDATA(int _keyLength, int? _version)
            : base(_version)
        {
            keyLength = _keyLength;

            CngAlgorithm ca;
            if (keyLength == 256)
                ca = CngAlgorithm.ECDsaP256;
            else if (keyLength == 384)
                ca = CngAlgorithm.ECDsaP384;
            else if (keyLength == 521)
                ca = CngAlgorithm.ECDsaP521;
            else
                throw new NotSupportedException("ecdsa_key_length_not_suppoeted");

            CngKey ck = CngKey.Create(ca, null, new CngKeyCreationParameters { ExportPolicy = CngExportPolicies.AllowPlaintextExport });

            publicKey = ck.Export(CngKeyBlobFormat.EccPublicBlob);
            privateKey = ck.Export(CngKeyBlobFormat.EccPrivateBlob);
        }

        public CREASIGNATUREDATA(int _keyLength) : this(_keyLength, null) { }

        public CREASIGNATUREDATA() : this(256) { }

        protected override MainDataInfomation[] MainDataInfo
        {
            get
            {
                return new MainDataInfomation[]{
                    new MainDataInfomation(typeof(int), () => keyLength, (o) => keyLength = (int)o), 
                    new MainDataInfomation(typeof(byte[]), null, () => publicKey, (o) => publicKey = (byte[])o), 
                    new MainDataInfomation(typeof(byte[]), null, () => privateKey, (o) => privateKey = (byte[])o), 
                };
            }
        }

        protected override bool IsVersioned
        {
            get { return false; }
        }

        protected override bool IsCorruptionChecked
        {
            get { return false; }
        }
    }

    public class AccountHolder : CREASIGNATUREDATA
    {
        private string name;
        public string Name
        {
            get { return name; }
            set { name = value; }
        }

        private readonly object accountsLock = new object();
        private List<Account> accounts;
        public Account[] Accounts
        {
            get { return accounts.ToArray(); }
        }

        public AccountHolder(string _name, int _keyLength)
            : base(_keyLength, 0)
        {
            name = _name;
            accounts = new List<Account>();
        }

        public AccountHolder(string _name) : this(_name, 256) { }

        public AccountHolder(int _keyLength) : this(null, _keyLength) { }

        public AccountHolder() : this(null, 256) { }

        protected override MainDataInfomation[] MainDataInfo
        {
            get
            {
                if (Version == 0)
                {
                    return base.MainDataInfo.Combine(new MainDataInfomation[]{
                        new MainDataInfomation(typeof(string), () => name, (o) => name = (string)o), 
                        new MainDataInfomation(typeof(Account[]), 0, null, () => accounts.ToArray(), (o) => accounts = ((Account[])o).ToList()), 
                    });
                }
                else
                    throw new NotSupportedException("account_hilder_main_data_info");
            }
        }

        protected override bool IsVersioned
        {
            get { return true; }
        }

        protected override bool IsCorruptionChecked
        {
            get
            {
                if (Version <= 0)
                    return true;
                else
                    throw new NotSupportedException("account_holder_check");
            }
        }

        public string Trip
        {
            get { return Convert.ToBase64String(publicKey).Operate((s) => s.Substring(s.Length - 12, 12)); }
        }

        public string Sign
        {
            get { return name + "◆" + Trip; }
        }

        public void AddAccount(Account account)
        {
            lock (accountsLock)
                if (!accounts.Contains(account).RaiseError(this.GetType(), "exist_account", 5))
                    accounts.Add(account);
        }

        public void RemoveAccount(Account account)
        {
            lock (accountsLock)
                if (accounts.Contains(account).NotRaiseError(this.GetType(), "not_exist_account", 5))
                    accounts.Remove(account);
        }

        public override string ToString() { return Sign; }
    }

    public class Account : CREASIGNATUREDATA
    {
        private string name;
        public string Name
        {
            get { return name; }
            set { name = value; }
        }

        private string description;
        public string Description
        {
            get { return description; }
            set { description = value; }
        }

        public Account(string _name, string _description, int _keyLength)
            : base(_keyLength, 0)
        {
            name = _name;
            description = _description;
        }

        public Account(string _name, string _description) : this(_name, _description, 256) { }

        public Account(int _keyLength) : this(null, null, _keyLength) { }

        public Account() : this(null, null, 256) { }

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
                        //string identifier = "CREA";

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

        protected override MainDataInfomation[] MainDataInfo
        {
            get
            {
                if (Version == 0)
                {
                    return base.MainDataInfo.Combine(new MainDataInfomation[]{
                        new MainDataInfomation(typeof(string), () => name, (o) => name = (string)o), 
                        new MainDataInfomation(typeof(string), () => description, (o) => description = (string)o), 
                    });
                }
                else
                    throw new NotSupportedException("account_main_data_info");
            }
        }

        protected override bool IsVersioned
        {
            get { return true; }
        }

        protected override bool IsCorruptionChecked
        {
            get
            {
                if (Version <= 0)
                    return true;
                else
                    throw new NotSupportedException("account_check");
            }
        }

        public AccountAddress Address
        {
            get { return new AccountAddress(publicKey); }
        }

        public override string ToString() { return Address.Base58; }
    }

    public class AccountHolderDatabase : CREACOINSHAREDDATA
    {
        private readonly object ahsLock = new object();
        private List<AccountHolder> accountHolders;
        public AccountHolder[] AccountHolders
        {
            get { return accountHolders.ToArray(); }
        }

        private readonly object cahsLock = new object();
        private List<AccountHolder> candidateAccountHolders;
        public AccountHolder[] CandidateAccountHolders
        {
            get { return candidateAccountHolders.ToArray(); }
        }

        public AccountHolderDatabase()
            : base(0)
        {
            accountHolders = new List<AccountHolder>();
            candidateAccountHolders = new List<AccountHolder>();
        }

        protected override MainDataInfomation[] MainDataInfo
        {
            get
            {
                if (Version == 0)
                {
                    return new MainDataInfomation[]{
                        new MainDataInfomation(typeof(AccountHolder[]), () => accountHolders.ToArray(), (o) => accountHolders = ((AccountHolder[])o).ToList()), 
                        new MainDataInfomation(typeof(AccountHolder[]), () => candidateAccountHolders.ToArray(), (o) => candidateAccountHolders = ((AccountHolder[])o).ToList()), 
                    };
                }
                else
                    throw new NotSupportedException("account_holder_database_main_data_info");
            }
        }

        protected override bool IsVersioned
        {
            get { return true; }
        }

        protected override bool IsCorruptionChecked
        {
            get
            {
                if (Version <= 0)
                    return true;
                else
                    throw new NotSupportedException("account_holder_database_check");
            }
        }

        public void AddSignData(AccountHolder ah)
        {
            lock (ahsLock)
            {
                bool[] conditions = new bool[]{
                    accountHolders.Contains(ah).RaiseError(this.GetType(), "exist_account_holder", 5), 
                    accountHolders.Where((e) => e.Name == ah.Name).FirstOrDefault().IsNotNull().RaiseError(this.GetType(), "exist_same_name_account_holder", 5)
                };

                if (!conditions.And())
                    accountHolders.Add(ah);
            }
        }

        public void DeleteSignData(AccountHolder ah)
        {
            lock (ahsLock)
                if (accountHolders.Contains(ah).NotRaiseError(this.GetType(), "not_exist_account_holder", 5))
                    accountHolders.Remove(ah);
        }

        public void AddCandidateSignData(AccountHolder ah)
        {
            lock (cahsLock)
                if (!candidateAccountHolders.Contains(ah).RaiseError(this.GetType(), "exist_candidate_account_holder", 5))
                    candidateAccountHolders.Add(ah);
        }

        public void DeleteCandidateSignData(AccountHolder ah)
        {
            lock (cahsLock)
                if (candidateAccountHolders.Contains(ah).NotRaiseError(this.GetType(), "not_exist_candidate_account_holder", 5))
                    candidateAccountHolders.Remove(ah);
        }

        public void ClearCandidateSignDatas()
        {
            lock (cahsLock)
                candidateAccountHolders.Clear();
        }
    }

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