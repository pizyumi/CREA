﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;

namespace CREA2014
{
    #region 基底クラス

    public abstract class CREACOINDATA { }
    public abstract class CREACOININTERNALDATA : CREACOINDATA { }
    public abstract class CREACOINSHAREDDATA : CREACOINDATA
    {
        //<未実装>圧縮機能
        //<未実装>ジャグ配列に対応

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
            //2014/02/23
            //抽象クラスには対応しない
            //抽象クラスの変数に格納されている具象クラスを保存する場合には具象クラスとしてMainDataInfomationを作成する
            //具象クラスが複数ある場合には具象クラス別にMainDataInfomationを作成する

            //CREACOINSHAREDDATA（の派生クラス）の配列専用
            public MainDataInfomation(Type _type, int? _version, int? _length, Func<object> _getter, Action<object> _setter)
            {
                if (!_type.IsArray)
                    throw new ArgumentException("main_data_info_not_array");

                Type elementType = _type.GetElementType();
                if (!elementType.IsSubclassOf(typeof(CREACOINSHAREDDATA)))
                    throw new ArgumentException("main_data_info_not_ccsd_array");
                else if (elementType.IsAbstract)
                    throw new ArgumentException("main_data_info_ccsd_array_abstract");

                CREACOINSHAREDDATA ccsd = Activator.CreateInstance(elementType) as CREACOINSHAREDDATA;
                if ((!ccsd.IsVersioned && _version != null) || (ccsd.IsVersioned && _version == null))
                    throw new ArgumentException("main_data_info_not_is_versioned");

                version = _version;
                length = _length;

                Type = _type;
                Getter = _getter;
                Setter = _setter;
            }

            //CREACOINSHAREDDATA（の派生クラス）の配列以外の配列またはCREACOINSHAREDDATA（の派生クラス）専用
            public MainDataInfomation(Type _type, int? _lengthOrVersion, Func<object> _getter, Action<object> _setter)
            {
                if (_type.IsArray)
                {
                    Type elementType = _type.GetElementType();
                    if (elementType.IsSubclassOf(typeof(CREACOINSHAREDDATA)))
                        throw new ArgumentException("main_data_info_ccsd_array");
                    else if (elementType.IsAbstract)
                        throw new ArgumentException("main_data_info_array_abstract");
                    else
                        length = _lengthOrVersion;
                }
                else if (_type.IsSubclassOf(typeof(CREACOINSHAREDDATA)))
                {
                    if (_type.IsAbstract)
                        throw new ArgumentException("main_data_info_ccsd_abstract");

                    CREACOINSHAREDDATA ccsd = Activator.CreateInstance(_type) as CREACOINSHAREDDATA;
                    if ((!ccsd.IsVersioned && _lengthOrVersion != null) || (ccsd.IsVersioned && _lengthOrVersion == null))
                        throw new ArgumentException("main_data_info_not_is_versioned");

                    version = _lengthOrVersion;
                }
                else
                    throw new ArgumentException("main_data_info_not_bytes_ccsd");

                Type = _type;
                Getter = _getter;
                Setter = _setter;
            }

            public MainDataInfomation(Type _type, Func<object> _getter, Action<object> _setter)
            {
                if (_type.IsArray)
                    throw new ArgumentException("main_data_info_array");
                else if (_type.IsSubclassOf(typeof(CREACOINSHAREDDATA)))
                    throw new ArgumentException("main_data_info_ccsd");
                else if (_type.IsAbstract)
                    throw new ArgumentException("main_data_info_abstract");

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

        public class MemoryStreamReaderWriter
        {
            private readonly MemoryStream ms;
            private readonly MsrwMode mode;

            public MemoryStreamReaderWriter(MemoryStream _ms, MsrwMode _mode)
            {
                ms = _ms;
                mode = _mode;
            }

            public byte[] ReadOrWrite(byte[] input, int length)
            {
                if (mode == MsrwMode.read)
                {
                    byte[] output = new byte[length];
                    ms.Read(output, 0, length);
                    return output;
                }
                else if (mode == MsrwMode.write)
                {
                    ms.Write(input, 0, length);
                    return null;
                }
                else
                    throw new MsrwCantReadOrWriteException();
            }

            public enum MsrwMode { read, write, neither }
            public class MsrwCantReadOrWriteException : Exception { }
        }

        protected abstract Func<MemoryStreamReaderWriter, IEnumerable<MainDataInfomation>> MainDataInfo { get; }
        protected abstract bool IsVersioned { get; }
        protected abstract bool IsCorruptionChecked { get; }

        public int? Length
        {
            get
            {
                Func<Type, MainDataInfomation, int?> _GetLength = (type, mdi) =>
                {
                    if (type == typeof(bool) || type == typeof(byte))
                        return 1;
                    else if (type == typeof(int) || type == typeof(float))
                        return 4;
                    else if (type == typeof(long) || type == typeof(double) || type == typeof(DateTime))
                        return 8;
                    else if (type == typeof(string))
                        return null;
                    else if (type.IsSubclassOf(typeof(CREACOINSHAREDDATA)))
                    {
                        CREACOINSHAREDDATA ccsd = Activator.CreateInstance(type) as CREACOINSHAREDDATA;
                        if (ccsd.IsVersioned)
                            ccsd.version = mdi.Version;

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

                int length = 0;
                try
                {
                    foreach (var mdi in MainDataInfo(new MemoryStreamReaderWriter(null, MemoryStreamReaderWriter.MsrwMode.neither)))
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
                catch (MemoryStreamReaderWriter.MsrwCantReadOrWriteException)
                {
                    return null;
                }
                return length;
            }
        }

        public byte[] ToBinary()
        {
            byte[] mainDataBytes;
            using (MemoryStream ms = new MemoryStream())
            {
                Action<Type, MainDataInfomation, object> _Write = (type, mdi, o) =>
                {
                    if (type == typeof(bool))
                        ms.Write(BitConverter.GetBytes((bool)o), 0, 1);
                    else if (type == typeof(int))
                        ms.Write(BitConverter.GetBytes((int)o), 0, 4);
                    else if (type == typeof(float))
                        ms.Write(BitConverter.GetBytes((float)o), 0, 4);
                    else if (type == typeof(long))
                        ms.Write(BitConverter.GetBytes((long)o), 0, 8);
                    else if (type == typeof(double))
                        ms.Write(BitConverter.GetBytes((double)o), 0, 8);
                    else if (type == typeof(DateTime))
                        ms.Write(BitConverter.GetBytes(((DateTime)o).ToBinary()), 0, 8);
                    else if (type == typeof(string))
                    {
                        byte[] bytes = Encoding.UTF8.GetBytes((string)o);
                        ms.Write(BitConverter.GetBytes(bytes.Length), 0, 4);
                        ms.Write(bytes, 0, bytes.Length);
                    }
                    else if (type.IsSubclassOf(typeof(CREACOINSHAREDDATA)))
                    {
                        CREACOINSHAREDDATA ccsd = o as CREACOINSHAREDDATA;
                        if (ccsd.IsVersioned)
                            ccsd.version = mdi.Version;

                        byte[] bytes = ccsd.ToBinary();
                        if (ccsd.Length == null)
                            ms.Write(BitConverter.GetBytes(bytes.Length), 0, 4);
                        ms.Write(bytes, 0, bytes.Length);
                    }
                    else
                        throw new NotSupportedException("to_binary_not_supported");
                };

                foreach (var mdi in MainDataInfo(new MemoryStreamReaderWriter(ms, MemoryStreamReaderWriter.MsrwMode.write)))
                {
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
                        foreach (var innerObj in o as object[])
                            _Write(elementType, mdi, innerObj);
                    }
                    else
                        _Write(mdi.Type, mdi, o);
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
                Func<Type, MainDataInfomation, object> _Read = (type, mdi) =>
                {
                    if (type == typeof(bool))
                    {
                        byte[] bytes = new byte[1];
                        ms.Read(bytes, 0, 1);
                        return BitConverter.ToBoolean(bytes, 0);
                    }
                    else if (type == typeof(int))
                    {
                        byte[] bytes = new byte[4];
                        ms.Read(bytes, 0, 4);
                        return BitConverter.ToInt32(bytes, 0);
                    }
                    else if (type == typeof(float))
                    {
                        byte[] bytes = new byte[4];
                        ms.Read(bytes, 0, 4);
                        return BitConverter.ToSingle(bytes, 0);
                    }
                    else if (type == typeof(long))
                    {
                        byte[] bytes = new byte[8];
                        ms.Read(bytes, 0, 8);
                        return BitConverter.ToInt64(bytes, 0);
                    }
                    else if (type == typeof(double))
                    {
                        byte[] bytes = new byte[8];
                        ms.Read(bytes, 0, 8);
                        return BitConverter.ToDouble(bytes, 0);
                    }
                    else if (type == typeof(DateTime))
                    {
                        byte[] bytes = new byte[8];
                        ms.Read(bytes, 0, 8);
                        return DateTime.FromBinary(BitConverter.ToInt64(bytes, 0));
                    }
                    else if (type == typeof(string))
                    {
                        byte[] lengthBytes = new byte[4];
                        ms.Read(lengthBytes, 0, 4);
                        int length = BitConverter.ToInt32(lengthBytes, 0);

                        byte[] bytes = new byte[length];
                        ms.Read(bytes, 0, length);
                        return Encoding.UTF8.GetString(bytes);
                    }
                    else if (type.IsSubclassOf(typeof(CREACOINSHAREDDATA)))
                    {
                        CREACOINSHAREDDATA ccsd = Activator.CreateInstance(type) as CREACOINSHAREDDATA;
                        if (ccsd.IsVersioned)
                            ccsd.version = mdi.Version;

                        int length;
                        if (ccsd.Length == null)
                        {
                            byte[] lengthBytes = new byte[4];
                            ms.Read(lengthBytes, 0, 4);
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
                        ms.Read(bytes, 0, length);

                        ccsd.FromBinary(bytes);

                        return ccsd;
                    }
                    else
                        throw new NotSupportedException("from_binary_not_supported");
                };

                foreach (var mdi in MainDataInfo(new MemoryStreamReaderWriter(ms, MemoryStreamReaderWriter.MsrwMode.read)))
                {
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
                            os[i] = _Read(elementType, mdi);

                        mdi.Setter(os);
                    }
                    else
                        mdi.Setter(_Read(mdi.Type, mdi));
                }
            }
        }
    }
    public abstract class CREACOINSETTINGSDATA : CREACOINDATA
    {
        //<未実装>ジャグ配列に対応
        //<未改良>SetAndSaveの抽象化 結構難しいので後回し

        private string filename;
        public string Filename
        {
            get { return filename; }
        }

        public CREACOINSETTINGSDATA(string _filename)
        {
            filename = _filename;
        }

        public class MainDataInfomation
        {
            public MainDataInfomation(Type _type, string _xmlName, Func<object> _getter, Action<object> _setter)
            {
                Type = _type;
                XmlName = _xmlName;
                Getter = _getter;
                Setter = _setter;
            }

            public readonly Type Type;
            public readonly string XmlName;
            public readonly Func<object> Getter;
            public readonly Action<object> Setter;
        }

        protected abstract string XmlName { get; }
        protected abstract MainDataInfomation[] MainDataInfo { get; }

        public XElement ToXml()
        {
            XElement xElement = new XElement(XmlName);
            foreach (var mdi in MainDataInfo)
            {
                Action<Type, MainDataInfomation, object, XElement> _Write = (type, innerMdi, innerObj, innerXElement) =>
                {
                    if (type == typeof(bool))
                        innerXElement.Add(new XElement(innerMdi.XmlName, ((bool)innerObj).ToString()));
                    else if (type == typeof(int))
                        innerXElement.Add(new XElement(innerMdi.XmlName, ((int)innerObj).ToString()));
                    else if (type == typeof(float))
                        innerXElement.Add(new XElement(innerMdi.XmlName, ((float)innerObj).ToString()));
                    else if (type == typeof(long))
                        innerXElement.Add(new XElement(innerMdi.XmlName, ((long)innerObj).ToString()));
                    else if (type == typeof(double))
                        innerXElement.Add(new XElement(innerMdi.XmlName, ((double)innerObj).ToString()));
                    else if (type == typeof(DateTime))
                        innerXElement.Add(new XElement(innerMdi.XmlName, ((DateTime)innerObj).ToString()));
                    else if (type == typeof(string))
                        innerXElement.Add(new XElement(innerMdi.XmlName, (string)innerObj));
                    else if (type.IsSubclassOf(typeof(CREACOINSETTINGSDATA)))
                        innerXElement.Add(new XElement(innerMdi.XmlName, (innerObj as CREACOINSETTINGSDATA).ToXml()));
                    else
                        throw new NotSupportedException("to_xml_not_supported");
                };

                object o = mdi.Getter();

                if (mdi.Type.IsArray)
                {
                    Type elementType = mdi.Type.GetElementType();

                    XElement xElementArray = new XElement(XmlName + "s");
                    foreach (var innerObj in o as object[])
                        _Write(elementType, mdi, innerObj, xElementArray);

                    xElement.Add(xElementArray);
                }
                else
                    _Write(mdi.Type, mdi, o, xElement);
            }
            return xElement;
        }

        public void FromXml(XElement xElement)
        {
            if (xElement.Name.LocalName != XmlName)
                throw new ArgumentException("xml_name");

            foreach (var mdi in MainDataInfo)
            {
                Func<Type, MainDataInfomation, XElement, object> _Read = (type, innerMdi, innerXElement) =>
                {
                    if (type == typeof(bool))
                        return bool.Parse(innerXElement.Element(mdi.XmlName).Value);
                    else if (type == typeof(int))
                        return int.Parse(innerXElement.Element(mdi.XmlName).Value);
                    else if (type == typeof(float))
                        return float.Parse(innerXElement.Element(mdi.XmlName).Value);
                    else if (type == typeof(long))
                        return long.Parse(innerXElement.Element(mdi.XmlName).Value);
                    else if (type == typeof(double))
                        return double.Parse(innerXElement.Element(mdi.XmlName).Value);
                    else if (type == typeof(DateTime))
                        return DateTime.Parse(innerXElement.Element(mdi.XmlName).Value);
                    else if (type == typeof(string))
                        return innerXElement.Element(mdi.XmlName).Value;
                    else if (type.IsSubclassOf(typeof(CREACOINSETTINGSDATA)))
                    {
                        CREACOINSETTINGSDATA ccsd = Activator.CreateInstance(type) as CREACOINSETTINGSDATA;
                        ccsd.FromXml(innerXElement.Element(mdi.XmlName).Element(ccsd.XmlName));
                        return ccsd;
                    }
                    else
                        throw new NotSupportedException("from_xml_not_supported");
                };

                if (mdi.Type.IsArray)
                {
                    Type elementType = mdi.Type.GetElementType();

                    XElement[] xElements = xElement.Element(mdi.XmlName + "s").Elements(mdi.XmlName).ToArray();

                    object[] os = Array.CreateInstance(elementType, xElements.Length) as object[];
                    for (int i = 0; i < os.Length; i++)
                        os[i] = _Read(elementType, mdi, xElements[i]);

                    mdi.Setter(os);
                }
                else
                    mdi.Setter(_Read(mdi.Type, mdi, xElement));
            }
        }

        public void Load()
        {
            if (File.Exists(filename))
                FromXml(XElement.Load(filename));
        }

        public void Save() { ToXml().Save(filename); }
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

    public class EcdsaKey : CREACOINSHAREDDATA
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
                    throw new NotSupportedException("ecdsa_key_main_data_info");

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
                    throw new NotSupportedException("ecdsa_key_check");
            }
        }
    }

    public class Account : CREACOINSHAREDDATA
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

        public event EventHandler AccountChanged = delegate { };

        public AccountAddress Address
        {
            get { return new AccountAddress(key.PublicKey); }
        }

        public override string ToString() { return Address.Base58; }
    }

    public abstract class AccountHolder : CREACOINSHAREDDATA
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
                if (!accounts.Contains(account).RaiseError(this.GetType(), "exist_account", 5))
                    this.ExecuteBeforeEvent(() =>
                    {
                        accounts.Add(account);
                        account.AccountChanged += account_changed;
                    }, AccountAdded, AccountHolderChanged);
        }

        public void RemoveAccount(Account account)
        {
            lock (accountsLock)
                if (accounts.Contains(account).NotRaiseError(this.GetType(), "not_exist_account", 5))
                    this.ExecuteBeforeEvent(() =>
                    {
                        accounts.Remove(account);
                        account.AccountChanged -= account_changed;
                    }, AccountRemoved, AccountHolderChanged);
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
                    throw new NotSupportedException("aah_main_data_info");
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
                    throw new NotSupportedException("pch_main_data_info");
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
                    throw new NotSupportedException("pch_check");
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

    public class AccountHolderDatabase : CREACOINSHAREDDATA
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

        public event EventHandler AccountHolderAdded = delegate { };
        public event EventHandler AccountHolderRemoved = delegate { };
        public event EventHandler AccountHoldersChanged = delegate { };

        private EventHandler accountHolders_changed;

        public void AddAccountHolder(PseudonymousAccountHolder ah)
        {
            lock (pahsLock)
            {
                bool[] conditions = new bool[]{
                    pseudonymousAccountHolders.Contains(ah).RaiseError(this.GetType(), "exist_account_holder", 5), 
                    pseudonymousAccountHolders.Where((e) => e.Name == ah.Name).FirstOrDefault().IsNotNull().RaiseError(this.GetType(), "exist_same_name_account_holder", 5)
                };

                if (!conditions.And())
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
                if (pseudonymousAccountHolders.Contains(ah).NotRaiseError(this.GetType(), "not_exist_account_holder", 5))
                    this.ExecuteBeforeEvent(() =>
                    {
                        pseudonymousAccountHolders.Remove(ah);
                        ah.AccountHolderChanged -= accountHolders_changed;
                    }, AccountHolderRemoved, AccountHoldersChanged);
        }

        public void AddCandidateAccountHolder(PseudonymousAccountHolder ah)
        {
            lock (cahsLock)
                if (!candidateAccountHolders.Contains(ah).RaiseError(this.GetType(), "exist_candidate_account_holder", 5))
                    candidateAccountHolders.Add(ah);
        }

        public void DeleteCandidateAccountHolder(PseudonymousAccountHolder ah)
        {
            lock (cahsLock)
                if (candidateAccountHolders.Contains(ah).NotRaiseError(this.GetType(), "not_exist_candidate_account_holder", 5))
                    candidateAccountHolders.Remove(ah);
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