using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

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
                    throw new InvalidOperationException("core_started"); //対応済

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
                    throw new InvalidOperationException("core_not_started"); //対応済

                File.WriteAllBytes(accountHolderDatabasePath, accountHolderDatabase.ToBinary());

                isSystemStarted = false;
            }
        }
    }

    public abstract class NetworkParameter
    {
        public static readonly int ProtocolVersion = 0;
    }

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
                throw new ArgumentException("Sha256_bytes_length"); //対応済

            bytes = _bytes;
        }

        public Sha256Hash(string value) : this(value.FromHexstring()) { }

        public Sha256Hash() { }

        protected override Func<MemoryStreamReaderWriter, IEnumerable<MainDataInfomation>> MainDataInfo
        {
            get
            {
                return (msrw) => new MainDataInfomation[]{
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
                throw new ArgumentException("Ripemd160_bytes_length"); //対応済

            bytes = _bytes;
        }

        public Ripemd160Hash(string value) : this(value.FromHexstring()) { }

        public Ripemd160Hash() { }

        protected override Func<MemoryStreamReaderWriter, IEnumerable<MainDataInfomation>> MainDataInfo
        {
            get
            {
                return (msrw) => new MainDataInfomation[]{
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
                throw new NotSupportedException("ecdsa_key_length_not_suppoeted"); //対応済

            CngKey ck = CngKey.Create(ca, null, new CngKeyCreationParameters { ExportPolicy = CngExportPolicies.AllowPlaintextExport });

            publicKey = ck.Export(CngKeyBlobFormat.EccPublicBlob);
            privateKey = ck.Export(CngKeyBlobFormat.EccPrivateBlob);
        }

        public EcdsaKey() : this(EcdsaKeyLength.Ecdsa256) { }

        protected override Func<MemoryStreamReaderWriter, IEnumerable<MainDataInfomation>> MainDataInfo
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
                    throw new NotSupportedException("ecdsa_key_main_data_info"); //対応済

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
                    throw new NotSupportedException("ecdsa_key_check"); //対応済
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
                            throw new InvalidDataException("base58_check"); //対応済

                        byte[] identifierBytes = new byte[3];
                        Array.Copy(mergedBytes, 0, identifierBytes, 0, identifierBytes.Length);
                        byte[] hashBytes = new byte[mergedBytes.Length - identifierBytes.Length];
                        Array.Copy(mergedBytes, identifierBytes.Length, hashBytes, 0, hashBytes.Length);

                        //base58表現の先頭がCREAになるようなバイト配列を使っている
                        byte[] correctIdentifierBytes = new byte[] { 84, 122, 143 };

                        if (!identifierBytes.BytesEquals(correctIdentifierBytes))
                            throw new InvalidDataException("base58_identifier"); //対応済

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

        protected override Func<MemoryStreamReaderWriter, IEnumerable<MainDataInfomation>> MainDataInfo
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
                    throw new NotSupportedException("account_main_data_info"); //対応済
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
                    throw new NotSupportedException("account_check"); //対応済
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

        protected override Func<MemoryStreamReaderWriter, IEnumerable<MainDataInfomation>> MainDataInfo
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

        protected override bool IsVersioned
        {
            get { return false; }
        }

        protected override bool IsCorruptionChecked
        {
            get { return false; }
        }

        public event EventHandler AccountAdded = delegate { };
        public EventHandler PAccountAdded
        {
            get { return AccountAdded; }
        }

        public event EventHandler AccountRemoved = delegate { };
        public EventHandler PAccountRemoved
        {
            get { return AccountRemoved; }
        }

        public event EventHandler AccountHolderChanged = delegate { };
        public EventHandler PAccountHolderChanged
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

        protected override Func<MemoryStreamReaderWriter, IEnumerable<MainDataInfomation>> MainDataInfo
        {
            get
            {
                if (Version == 0)
                    return base.MainDataInfo;
                else
                    throw new NotSupportedException("aah_main_data_info"); //対応済
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
                    throw new NotSupportedException("aah_check"); //対応済
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

        protected override Func<MemoryStreamReaderWriter, IEnumerable<MainDataInfomation>> MainDataInfo
        {
            get
            {
                if (Version == 0)
                    return (msrw) => base.MainDataInfo(msrw).Concat(new MainDataInfomation[]{
                        new MainDataInfomation(typeof(string), () => name, (o) => name = (string)o), 
                        new MainDataInfomation(typeof(EcdsaKey), 0, () => key, (o) => key = (EcdsaKey)o), 
                    });
                else
                    throw new NotSupportedException("pah_main_data_info"); //対応済
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
                    throw new NotSupportedException("pah_check"); //対応済
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

        protected override Func<MemoryStreamReaderWriter, IEnumerable<MainDataInfomation>> MainDataInfo
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
                    throw new NotSupportedException("account_holder_database_main_data_info"); //対応済
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
                    throw new NotSupportedException("account_holder_database_check"); //対応済
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
                    throw new InvalidOperationException("exist_account_holder"); //対応済

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
                    throw new InvalidOperationException("not_exist_account_holder"); //対応済

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
                    throw new InvalidOperationException("exist_candidate_account_holder"); //対応済

                candidateAccountHolders.Add(ah);
            }
        }

        public void DeleteCandidateAccountHolder(PseudonymousAccountHolder ah)
        {
            lock (cahsLock)
            {
                if (!candidateAccountHolders.Contains(ah))
                    throw new InvalidOperationException("not_exist_candidate_account_holder"); //対応済

                candidateAccountHolders.Remove(ah);
            }
        }

        public void ClearCandidateAccountHolders()
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

    #endregion
}