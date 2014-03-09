using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Xml.Linq;

namespace CREA2014
{
    #region 汎用

    public static class OneTime
    {
        public static Action GetOneTime()
        {
            object o = null;
            return () =>
            {
                if (o != null)
                    throw new InvalidOperationException("one_time"); //対応済
                o = new object();
            };
        }
    }

    public class EventArgs<T> : EventArgs
    {
        public EventArgs(T _value1)
        {
            value1 = _value1;
        }

        private T value1;
        public T Value1
        {
            get { return value1; }
        }
    }

    public class EventArgs<T, U> : EventArgs<T>
    {
        public EventArgs(T _value1, U _value2)
            : base(_value1)
        {
            value2 = _value2;
        }

        private U value2;
        public U Value2
        {
            get { return value2; }
        }
    }

    public class EventArgs<T, U, V> : EventArgs<T, U>
    {
        public EventArgs(T _value1, U _value2, V _value3)
            : base(_value1, _value2)
        {
            value3 = _value3;
        }

        private V value3;
        public V Value3
        {
            get { return value3; }
        }
    }

    #endregion

    #region 拡張メソッド

    public static class Extension
    {
        #region 一般

        //UIスレッドで処理を同期的に実行する（拡張：操作型）
        public static void ExecuteInUIThread(this Action action)
        {
            if (Application.Current == null)
                action();
            else
                if (Application.Current.Dispatcher.CheckAccess())
                    action();
                else
                    Application.Current.Dispatcher.Invoke(new Action(() => action()));
        }

        //UIスレッドで処理を同期的に実行する（拡張：関数型）
        public static T ExecuteInUIThread<T>(this Func<T> action)
        {
            if (Application.Current == null)
                return action();
            else
                if (Application.Current.Dispatcher.CheckAccess())
                    return action();
                else
                {
                    T result = default(T);
                    Application.Current.Dispatcher.Invoke(new Action(() => result = action()));
                    return result;
                }
        }

        //UIスレッドで処理を非同期的に実行する（拡張：操作型）
        public static void BeginExecuteInUIThread(Action action)
        {
            if (Application.Current == null)
                action();
            else
                if (Application.Current.Dispatcher.CheckAccess())
                    action();
                else
                    Application.Current.Dispatcher.BeginInvoke(new Action(() => action()));
        }

        //UIスレッドで処理を同期的に実行する（拡張：関数型）
        public static T BeginExecuteInUIThread<T>(this Func<T> action)
        {
            if (Application.Current == null)
                return action();
            else
                if (Application.Current.Dispatcher.CheckAccess())
                    return action();
                else
                {
                    T result = default(T);
                    Application.Current.Dispatcher.BeginInvoke(new Action(() => result = action()));
                    return result;
                }
        }

        //バイト配列から16進文字列に変換する（拡張：バイト配列型）
        public static string ToHexstring(this byte[] bytes)
        {
            StringBuilder sb = new StringBuilder(bytes.Length * 2);
            foreach (byte b in bytes)
            {
                if (b < 16)
                    sb.Append('0'); // 2桁になるよう0を追加
                sb.Append(Convert.ToString(b, 16));
            }
            return sb.ToString();
        }

        //16進文字列からバイト配列に変換する（拡張：文字列型）
        public static byte[] FromHexstring(this string str)
        {
            if (str.Length % 2 != 0)
                throw new ArgumentException("hexstring_length"); //対応済

            int length = str.Length / 2;
            byte[] bytes = new byte[length];
            string[] strs = str.SplitEqually(2);
            for (int i = 0; i < length; i++)
                bytes[i] = Convert.ToByte(strs[i], 16);

            return bytes;
        }

        //文字列を等間隔に分解する（拡張：文字列型）
        public static string[] SplitEqually(this string str, int interval)
        {
            if (str.Length % interval != 0)
                throw new ArgumentException("string_split_length"); //対応済

            int length = str.Length / interval;
            string[] strs = new string[length];
            for (int i = 0; i < length; i++)
                strs[i] = str.Substring(i * interval, interval);

            return strs;
        }

        //辞書に鍵が含まれている場合にはその値を返し、含まれていない場合には既定値を返す（拡張：辞書型）
        public static U GetValue<T, U>(this Dictionary<T, U> dict, T key, U def)
        {
            if (dict.ContainsKey(key))
                return dict[key];
            return def;
        }

        //二つのバイト配列の内容が等しいか判定する（拡張：バイト配列型）
        public static bool BytesEquals(this byte[] byte1, byte[] byte2)
        {
            if (byte1.Length != byte2.Length)
                return false;
            for (int i = 0; i < byte1.Length; i++)
                if (byte1[i] != byte2[i])
                    return false;
            return true;
        }

        //ループの回数を数える（拡張：任意型）
        public static int CountLoop<T>(this T first, Func<T, bool> condition, Func<T, T> next)
        {
            int i = 0;
            for (T p = first; condition(p); p = next(p))
                i++;
            return i;
        }

        //自分自身がnullか確認する（拡張：任意型）
        public static bool IsNull<T>(this T self)
        {
            return self == null;
        }

        //自分自身がnullでないか確認する（拡張：任意型）
        public static bool IsNotNull<T>(this T self)
        {
            return self != null;
        }

        //全ての真偽値配列型の要素が真であるか確認する（拡張：真偽値配列型）
        public static bool And(this bool[] conditions)
        {
            foreach (var condition in conditions)
                if (!condition)
                    return false;
            return true;
        }

        //処理を合成した処理を返す（拡張：処理型）
        public static Action AndThen(this Action action1, params Action[] actions)
        {
            return () =>
            {
                action1();
                foreach (var action in actions)
                    action();
            };
        }

        //イベントの前に処理を実行する（拡張：物件型）
        public static void ExecuteBeforeEvent(this object obj, Action action, params EventHandler[] ehs)
        {
            action();
            foreach (var eh in ehs)
                eh(obj, EventArgs.Empty);
        }

        //イベントの前に処理を実行する（拡張：物件型）
        public static void ExecuteBeforeEvent<T>(this object obj, Action action, T parameter, params EventHandler<T>[] ehs)
        {
            action();
            foreach (var eh in ehs)
                eh(obj, parameter);
        }

        //イベントの後に処理を実行する（拡張：物件型）
        public static void ExecuteAfterEvent(this object obj, Action action, params EventHandler[] ehs)
        {
            foreach (var eh in ehs)
                eh(obj, EventArgs.Empty);
            action();
        }

        //イベントの後に処理を実行する（拡張：物件型）
        public static void ExecuteAfterEvent<T>(this object obj, Action action, T parameter, params EventHandler<T>[] ehs)
        {
            foreach (var eh in ehs)
                eh(obj, parameter);
            action();
        }

        //自分自身を関数に渡してから返す（拡張：任意型）
        public static T Operate<T>(this T self, Action<T> operation)
        {
            operation(self);
            return self;
        }

        //自分自身を関数に渡した結果を返す（拡張：任意型）
        public static S Operate<T, S>(this T self, Func<T, S> operation)
        {
            return operation(self);
        }

        //自分自身を関数に渡した結果を永遠に返す（拡張：任意型）
        public static IEnumerable<S> OperateWhileTrue<T, S>(this T self, Func<T, S> operation)
        {
            while (true)
                yield return operation(self);
        }

        private static Random random = new Random();

        //0からiまでの整数が1回ずつ無作為な順番で含まれる配列を作成する（拡張：整数型）
        public static int[] RandomNum(this int i)
        {
            return random.OperateWhileTrue((r => r.Next(i))).Distinct().Take(i).ToArray();
        }

        //バイト配列の要素を無作為な順番で並べ直した新たなバイト配列を作成する（拡張：バイト配列型）
        public static byte[] BytesRandom(this byte[] bytes)
        {
            byte[] newbytes = new byte[bytes.Length];
            int[] ramdomNum = bytes.Length.RandomNum();
            for (int i = 0; i < bytes.Length; i++)
                newbytes[i] = bytes[ramdomNum[i]];
            return newbytes;
        }

        //バイト配列のSHA256ハッシュ値を計算する（拡張：バイト配列型）
        public static byte[] ComputeSha256(this byte[] bytes)
        {
            using (HashAlgorithm ha = HashAlgorithm.Create("SHA-256"))
                return ha.ComputeHash(bytes);
        }

        //バイト配列のRIPEMD160ハッシュ値を計算する（拡張：バイト配列型）
        public static byte[] ComputeRipemd160(this byte[] bytes)
        {
            using (HashAlgorithm ha = HashAlgorithm.Create("RIPEMD-160"))
                return ha.ComputeHash(bytes);
        }

        //配列を結合する（拡張：任意の配列型）
        public static T[] Combine<T>(this T[] self, params T[][] array)
        {
            T[] combined = new T[self.Length + array.Sum((a) => a.Length)];

            Array.Copy(self, 0, combined, 0, self.Length);
            for (int i = 0, index = self.Length; i < array.Length; index += array[i].Length, i++)
                Array.Copy(array[i], 0, combined, index, array[i].Length);

            return combined;
        }

        //繰り返し文字列を作る（拡張：文字列型）
        public static string Repeat(this string str, int count)
        {
            return string.Concat(Enumerable.Repeat(str, count).ToArray());
        }

        //例外メッセージを作る（拡張：例外型）
        public static string CreateMessage(this Exception ex, int level)
        {
            string space = " ".Repeat(level * 4);
            string exception = space + "Exception: " + ex.GetType().ToString();
            string message = space + "Message: " + ex.Message;
            string stacktrace = space + "StackTrace: " + Environment.NewLine + ex.StackTrace;

            string thisException;
            if (ex is SocketException)
            {
                SocketException sex = ex as SocketException;

                string errorCode = "ErrorCode: " + sex.ErrorCode.ToString();
                string socketErrorCode = "SocketErrorCode: " + sex.SocketErrorCode.ToString();

                thisException = string.Join(Environment.NewLine, exception, message, errorCode, socketErrorCode, stacktrace);
            }
            else
                thisException = string.Join(Environment.NewLine, exception, message, stacktrace);

            if (ex.InnerException == null)
                return thisException;
            else
            {
                string splitter = "-".Repeat(80);
                string innerexception = ex.InnerException.CreateMessage(level + 1);

                return string.Join(Environment.NewLine, thisException, splitter, innerexception);
            }
        }

        //拡張子データを作る（拡張：文字列型）
        public static string ExtensionsData(this string type)
        {
            string data = string.Empty;
            string name = string.Empty;
            string extensions = string.Empty;
            if (type == "all")
            {
                name = "すべてのファイル".Multilanguage(13);
                extensions = "*.*";
            }
            else if (type == "image")
            {
                name = "画像ファイル".Multilanguage(14);
                extensions = "*.bmp;*.gif;*.jpg;*.jpeg;*.png;*.tif;*.tiff";
            }
            else
                return data;

            data = name + "(" + extensions + ")" + "|" + extensions;

            return data;
        }

        #endregion

        public class TaskInformation : INTERNALDATA
        {
            public readonly Action Action;
            public readonly string Name;
            public readonly string Descption;

            public TaskInformation(Action _action, string _name, string _description)
            {
                Action = _action;
                Name = _name;
                Descption = _description;
            }
        }

        public static event EventHandler<TaskInformation> Tasked = delegate { };

        public static void StartTask<T>(this T self, Action action, string name, string description)
        {
            Tasked(self.GetType(), new TaskInformation(action, name, description));
        }

        public class LogInfomation : INTERNALDATA
        {
            public readonly Type Type;
            public readonly string Message;
            public readonly int Level;

            public LogInfomation(Type _type, string _message, int _level)
            {
                Type = _type;
                Message = _message;
                Level = _level;
            }
        }

        //ログイベントはProgram静的クラスのログ機能を介してより具体的なイベントに変換して、具体的なイベントをUIで処理する
        public static event EventHandler<LogInfomation> Tested = delegate { };
        public static event EventHandler<LogInfomation> Notified = delegate { };
        public static event EventHandler<LogInfomation> Resulted = delegate { };
        public static event EventHandler<LogInfomation> Warned = delegate { };
        public static event EventHandler<LogInfomation> Errored = delegate { };

        //試験ログイベントを発生させる（拡張：型表現型）
        public static void RaiseTest<T>(this T self, string message, int level)
        {
            Tested(self.GetType(), new LogInfomation(self.GetType(), message, level));
        }

        //通知ログイベントを発生させる（拡張：型表現型）
        public static void RaiseNotification<T>(this T self, string message, int level)
        {
            Notified(self.GetType(), new LogInfomation(self.GetType(), message, level));
        }

        //結果ログイベントを発生させる（拡張：任意型）
        public static void RaiseResult<T>(this T self, string message, int level)
        {
            Resulted(self.GetType(), new LogInfomation(self.GetType(), message, level));
        }

        //警告ログイベントを発生させる（拡張：任意型）
        public static void RaiseWarning<T>(this T self, string message, int level)
        {
            Warned(self.GetType(), new LogInfomation(self.GetType(), message, level));
        }

        //エラーログイベントを発生させる（拡張：任意型）
        public static void RaiseError<T>(this T self, string message, int level)
        {
            Errored(self.GetType(), new LogInfomation(self.GetType(), message, level));
        }

        //例外エラーログイベントを発生させる（拡張：任意型）
        public static void RaiseError<T>(this T self, string message, int level, Exception ex)
        {
            Errored(self.GetType(), new LogInfomation(self.GetType(), string.Join(Environment.NewLine, message, ex.CreateMessage(0)), level));
        }

        //真偽値が真のときのみエラーイベントを発生させ、真偽値をそのまま返す（拡張：真偽型）
        public static bool RaiseError(this bool flag, Type type, string message, int level)
        {
            if (flag)
                type.RaiseError(message, level);

            return flag;
        }

        //真偽値が偽のときのみエラーイベントを発生させ、真偽値をそのまま返す（拡張：真偽型）
        public static bool NotRaiseError(this bool flag, Type type, string message, int level)
        {
            if (!flag)
                type.RaiseError(message, level);

            return flag;
        }

        //多言語化対応（拡張：文字列型）
        public static string Multilanguage(this string text, int id)
        {
            return Program.Multilanguage(text, id);
        }

        //タスクの名称を取得する（拡張：文字列型）
        public static string GetTaskName(this string rawName)
        {
            return Program.GetTaskName(rawName);
        }

        //タスクの説明を取得する（拡張：文字列型）
        public static string GetTaskDescription(this string rawDescription)
        {
            return Program.GetTaskDescription(rawDescription);
        }

        //ログが発生した領域を取得する（拡張：型型）
        public static Program.LogData.LogGround GetLogGround(this Type type)
        {
            return Program.GetLogGround(type);
        }

        //ログの文章を取得する（拡張：文字列型）
        public static string GetLogMessage(this string rawMessage)
        {
            return Program.GetLogMessage(rawMessage);
        }

        //例外の説明を取得する（拡張：文字列型）
        public static string GetExceptionMessage(this string rawMessage)
        {
            return Program.GetExceptionMessage(rawMessage);
        }
    }

    #endregion

    #region WIN32API

    public static class WIN32API
    {
        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")]
        public static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);
        [DllImport("user32.dll")]
        public static extern bool IsIconic(IntPtr hWnd);
        public const int SW_RESTORE = 9;
    }

    #endregion

    #region 基底クラス

    public abstract class DATA { }
    public abstract class INTERNALDATA : DATA { }
    public abstract class SHAREDDATA : DATA
    {
        //<未実装>圧縮機能
        //<未実装>ジャグ配列に対応

        private int? version;
        public int Version
        {
            get
            {
                if (!IsVersioned)
                    throw new NotSupportedException("sd_version"); //対応済
                else
                    return (int)version;
            }
        }

        public SHAREDDATA(int? _version)
        {
            if ((IsVersioned && _version == null) || (!IsVersioned && _version != null))
                throw new ArgumentException("sd_is_versioned_and_version"); //対応済

            version = _version;
        }

        public SHAREDDATA() : this(null) { }

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
                    throw new ArgumentException("sd_main_data_info_not_array"); //対応済

                Type elementType = _type.GetElementType();
                if (!elementType.IsSubclassOf(typeof(SHAREDDATA)))
                    throw new ArgumentException("sd_main_data_info_not_ccsd_array"); //対応済
                else if (elementType.IsAbstract)
                    throw new ArgumentException("sd_main_data_info_ccsd_array_abstract"); //対応済

                SHAREDDATA ccsd = Activator.CreateInstance(elementType) as SHAREDDATA;
                if ((!ccsd.IsVersioned && _version != null) || (ccsd.IsVersioned && _version == null))
                    throw new ArgumentException("sd_main_data_info_not_is_versioned"); //対応済

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
                    if (elementType.IsSubclassOf(typeof(SHAREDDATA)))
                        throw new ArgumentException("sd_main_data_info_ccsd_array"); //対応済
                    else if (elementType.IsAbstract)
                        throw new ArgumentException("sd_main_data_info_array_abstract"); //対応済
                    else
                        length = _lengthOrVersion;
                }
                else if (_type.IsSubclassOf(typeof(SHAREDDATA)))
                {
                    if (_type.IsAbstract)
                        throw new ArgumentException("sd_main_data_info_ccsd_abstract"); //対応済

                    SHAREDDATA ccsd = Activator.CreateInstance(_type) as SHAREDDATA;
                    if ((!ccsd.IsVersioned && _lengthOrVersion != null) || (ccsd.IsVersioned && _lengthOrVersion == null))
                        throw new ArgumentException("sd_main_data_info_not_is_versioned"); //対応済

                    version = _lengthOrVersion;
                }
                else
                    throw new ArgumentException("sd_main_data_info_not_bytes_ccsd"); //対応済

                Type = _type;
                Getter = _getter;
                Setter = _setter;
            }

            public MainDataInfomation(Type _type, Func<object> _getter, Action<object> _setter)
            {
                if (_type.IsArray)
                    throw new ArgumentException("sd_main_data_info_array"); //対応済
                else if (_type.IsSubclassOf(typeof(SHAREDDATA)))
                    throw new ArgumentException("sd_main_data_info_ccsd"); //対応済
                else if (_type.IsAbstract)
                    throw new ArgumentException("sd_main_data_info_abstract"); //対応済

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
                        throw new NotSupportedException("sd_main_data_info_length"); //対応済
                }
            }
            private readonly int? version;
            public int Version
            {
                get
                {
                    SHAREDDATA ccsd;
                    if (Type.IsSubclassOf(typeof(SHAREDDATA)))
                        ccsd = Activator.CreateInstance(Type) as SHAREDDATA;
                    else if (Type.IsArray)
                    {
                        Type elementType = Type.GetElementType();
                        if (elementType.IsSubclassOf(typeof(SHAREDDATA)))
                            ccsd = Activator.CreateInstance(elementType) as SHAREDDATA;
                        else
                            throw new NotSupportedException("sd_main_data_info_version"); //対応済
                    }
                    else
                        throw new NotSupportedException("sd_main_data_info_version"); //対応済

                    if (!ccsd.IsVersioned)
                        throw new NotSupportedException("sd_main_data_info_is_versioned"); //対応済
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
                    else if (type.IsSubclassOf(typeof(SHAREDDATA)))
                    {
                        SHAREDDATA ccsd = Activator.CreateInstance(type) as SHAREDDATA;
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
                        throw new NotSupportedException("sd_length_not_supported"); //対応済
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
                    else if (type.IsSubclassOf(typeof(SHAREDDATA)))
                    {
                        SHAREDDATA ccsd = o as SHAREDDATA;
                        if (ccsd.IsVersioned)
                            ccsd.version = mdi.Version;

                        byte[] bytes = ccsd.ToBinary();
                        if (ccsd.Length == null)
                            ms.Write(BitConverter.GetBytes(bytes.Length), 0, 4);
                        ms.Write(bytes, 0, bytes.Length);
                    }
                    else
                        throw new NotSupportedException("to_binary_not_supported"); //対応済
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
                    throw new InvalidDataException("from_binary_check_inaccurate"); //対応済
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
                    else if (type.IsSubclassOf(typeof(SHAREDDATA)))
                    {
                        SHAREDDATA ccsd = Activator.CreateInstance(type) as SHAREDDATA;
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
                        throw new NotSupportedException("from_binary_not_supported"); //対応済
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
    public abstract class SETTINGSDATA : DATA
    {
        //<未実装>ジャグ配列に対応

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
                    else if (type.IsSubclassOf(typeof(SETTINGSDATA)))
                        innerXElement.Add(new XElement(innerMdi.XmlName, (innerObj as SETTINGSDATA).ToXml()));
                    else
                        throw new NotSupportedException("to_xml_not_supported"); //対応済
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
                throw new ArgumentException("xml_name"); //対応済

            foreach (var mdi in MainDataInfo)
            {
                Func<Type, MainDataInfomation, XElement, object> _Read = (type, innerMdi, innerXElement) =>
                {
                    XElement iiXElement = innerXElement.Element(mdi.XmlName);
                    if (iiXElement == null)
                        return null;

                    if (type == typeof(bool))
                        return bool.Parse(iiXElement.Value);
                    else if (type == typeof(int))
                        return int.Parse(iiXElement.Value);
                    else if (type == typeof(float))
                        return float.Parse(iiXElement.Value);
                    else if (type == typeof(long))
                        return long.Parse(iiXElement.Value);
                    else if (type == typeof(double))
                        return double.Parse(iiXElement.Value);
                    else if (type == typeof(DateTime))
                        return DateTime.Parse(iiXElement.Value);
                    else if (type == typeof(string))
                        return iiXElement.Value;
                    else if (type.IsSubclassOf(typeof(SETTINGSDATA)))
                    {
                        SETTINGSDATA ccsd = Activator.CreateInstance(type) as SETTINGSDATA;
                        ccsd.FromXml(iiXElement.Element(ccsd.XmlName));
                        return ccsd;
                    }
                    else
                        throw new NotSupportedException("from_xml_not_supported"); //対応済
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
                {
                    object obj = _Read(mdi.Type, mdi, xElement);
                    if (obj != null)
                        mdi.Setter(obj);
                }
            }
        }
    }
    public abstract class SETTABLESETTINGSDATA<T> : SETTINGSDATA
    {
        protected abstract T Setters { get; }

        private readonly object setLock = new object();
        public virtual void Set(Action<T> setAction)
        {
            lock (setLock)
                setAction(Setters);
        }
    }
    public abstract class SAVEABLESETTINGSDATA<T> : SETTABLESETTINGSDATA<T>
    {
        private readonly string filename;
        public string Filename
        {
            get { return filename; }
        }

        public SAVEABLESETTINGSDATA(string _filename)
        {
            filename = _filename;

            Load();
        }

        public void Load()
        {
            if (File.Exists(filename))
                FromXml(XElement.Load(filename));
        }

        public void Save() { ToXml().Save(filename); }

        //基底クラスのSetと同時に実行される可能性はある
        private readonly object setAndSaveLock = new object();
        public virtual void SetAndSave(Action<T> setAction)
        {
            lock (setAndSaveLock)
            {
                setAction(Setters);
                Save();
            }
        }
    }

    #endregion

    public static class Program
    {
        public enum ExceptionKind { wpf, unhandled }

        public class TaskData : Extension.TaskInformation
        {
            public readonly int Number;
            public readonly DateTime StartedTime;

            public TaskData(Extension.TaskInformation _taskInfo, int _number)
                : base(_taskInfo.Action, _taskInfo.Name, _taskInfo.Descption)
            {
                Number = _number;
                StartedTime = DateTime.Now;
            }
        }

        public class Tasker
        {
            private readonly object tasksLock = new object();
            private readonly List<TaskStatus> tasks;
            public TaskData[] Tasks
            {
                get
                {
                    lock (tasksLock)
                        return tasks.Select((e) => e.Data).ToArray();
                }
            }

            public class TaskStatus
            {
                public readonly TaskData Data;
                public readonly Thread Thread;

                public TaskStatus(TaskData _data, Thread _thread)
                {
                    Data = _data;
                    Thread = _thread;
                }
            }

            public Tasker()
            {
                tasks = new List<TaskStatus>();
            }

            public event EventHandler TaskStarted = delegate { };
            public event EventHandler TaskEnded = delegate { };

            public void New(TaskData task)
            {
                Thread thread = new Thread(() =>
                {
                    try
                    {
                        task.Action();
                    }
                    catch (Exception ex)
                    {
                        this.RaiseError("task".GetLogMessage(), 5, ex);
                    }
                    finally
                    {
                        TaskEnded(this, EventArgs.Empty);
                    }
                });
                thread.IsBackground = true;
                thread.Name = task.Name;

                lock (tasksLock)
                    tasks.Add(new TaskStatus(task, thread));

                TaskStarted(this, EventArgs.Empty);

                thread.Start();
            }

            public void Abort(TaskData abortTask)
            {
                TaskStatus status = null;
                lock (tasksLock)
                {
                    if ((status = tasks.Where((e) => e.Data == abortTask).FirstOrDefault()) == null)
                        throw new InvalidOperationException("task_not_found"); //対応済
                    tasks.Remove(status);
                }
                status.Thread.Abort();

                this.RaiseNotification("task_aborted".GetLogMessage(), 5);
            }

            public void AbortAll()
            {
                lock (tasksLock)
                {
                    foreach (var task in tasks)
                        task.Thread.Abort();
                    tasks.Clear();
                }

                this.RaiseNotification("all_tasks_aborted".GetLogMessage(), 5);
            }
        }

        public class LogData : Extension.LogInfomation
        {
            public readonly DateTime Time;
            public readonly LogKind Kind;

            public LogData(Extension.LogInfomation _logInfo, LogKind _kind)
                : base(_logInfo.Type, _logInfo.Message, _logInfo.Level)
            {
                Time = DateTime.Now;
                Kind = _kind;
            }

            public enum LogKind { test, notification, result, warning, error }
            public enum LogGround { foundation, core, common, networkBase, creaNetworkBase, cremlia, creaNetwork, signData, ui, other }

            public LogGround Ground
            {
                get { return Type.GetLogGround(); }
            }

            public string FriendlyKind
            {
                get
                {
                    if (Kind == LogKind.test)
                        return "試験".Multilanguage(75);
                    else if (Kind == LogKind.notification)
                        return "通知".Multilanguage(76);
                    else if (Kind == LogKind.result)
                        return "結果".Multilanguage(77);
                    else if (Kind == LogKind.warning)
                        return "警告".Multilanguage(78);
                    else
                        return "エラー".Multilanguage(79);
                }
            }

            public string FriendlyGround
            {
                get
                {
                    if (Ground == LogGround.foundation)
                        return "基礎".Multilanguage(89);
                    else if (Ground == LogGround.core)
                        return "核".Multilanguage(80);
                    else if (Ground == LogGround.common)
                        return "共通".Multilanguage(81);
                    else if (Ground == LogGround.networkBase)
                        return "ネットワーク基礎".Multilanguage(82);
                    else if (Ground == LogGround.creaNetworkBase)
                        return "CREAネットワーク基礎".Multilanguage(83);
                    else if (Ground == LogGround.cremlia)
                        return "Cremlia".Multilanguage(84);
                    else if (Ground == LogGround.creaNetwork)
                        return "CREAネットワーク".Multilanguage(85);
                    else if (Ground == LogGround.signData)
                        return "署名データ".Multilanguage(86);
                    else if (Ground == LogGround.ui)
                        return "UI".Multilanguage(87);
                    else
                        return "その他".Multilanguage(88);
                }
            }

            public string Text
            {
                get { return FriendlyKind + "[" + FriendlyGround + "]: " + Time.ToString() + " " + Message; }
            }

            public override string ToString() { return Text; }
        }

        public class Logger
        {
            private readonly LogSettings settings;
            public LogSettings Settings
            {
                get { return settings; }
            }

            private readonly List<LogData> logs;
            public LogData[] Logs
            {
                get { return logs.ToArray(); }
            }

            private readonly Dictionary<LogFilter, List<LogData>> filteredLogs;

            private readonly object filterLock = new object();
            public Logger(LogSettings _settings)
            {
                settings = _settings;
                logs = new List<LogData>();
                filteredLogs = new Dictionary<LogFilter, List<LogData>>();

                foreach (var filter in settings.Filters)
                    filteredLogs.Add(filter, new List<LogData>());

                settings.FilterAdded += (sender, e) =>
                {
                    lock (filterLock)
                        filteredLogs.Add(e, new List<LogData>());
                };
                settings.FilterRemoved += (sender, e) =>
                {
                    lock (filterLock)
                        filteredLogs.Remove(e);
                };
            }

            public event EventHandler LogAdded = delegate { };
            public event EventHandler<LogFilter> FilterLogAdded = delegate { };

            private readonly object addLogLockSave = new object();
            private readonly object addLogLockFilter = new object();
            public void AddLog(LogData log)
            {
                if (log.Level < settings.MinimalLevel)
                    return;

                if (settings.MaximalHoldingCount > 0)
                {
                    while (logs.Count >= settings.MaximalHoldingCount)
                        logs.RemoveAt(logs.Count - 1);
                    logs.Insert(0, log);
                }

                LogAdded(this, EventArgs.Empty);

                if (settings.IsSave)
                {
                    string path;
                    if (settings.SaveMeth == LogSettings.SaveMethod.allInOne)
                        path = settings.SavePath;
                    else
                    {
                        string directory = Path.GetDirectoryName(settings.SavePath);
                        string baseFileName = Path.GetFileNameWithoutExtension(settings.SavePath);
                        string extension = Path.GetExtension(settings.SavePath);

                        string[] datesM = new[] { DateTime.Now.Year.ToString(), DateTime.Now.Month.ToString() };
                        string[] datesD = datesM.Combine(new[] { DateTime.Now.Day.ToString() });

                        string date = string.Join("-", settings.SaveMeth == LogSettings.SaveMethod.monthByMonth ? datesM : datesD);

                        path = Path.Combine(directory, baseFileName + date + extension);
                    }

                    string text;
                    if (settings.Expression == string.Empty)
                        text = log.FriendlyKind + "[" + log.FriendlyGround + "]" + ": " + log.Time.ToString() + " " + log.Message;
                    else
                        text = settings.Expression.Replace("%%Message%%", log.Message).Replace("%%Datetime%%", log.Time.ToString()).Replace("%%Level%%", log.Level.ToString()).Replace("%%Kind%%", log.FriendlyKind).Replace("%%Ground%%", log.FriendlyGround);

                    lock (addLogLockSave)
                        File.AppendAllText(path, text + Environment.NewLine);
                }

                lock (addLogLockFilter)
                    foreach (var filteredLog in filteredLogs)
                        if (settings.MaximalHoldingCount > 0)
                        {
                            if (!filteredLog.Key.IsEnabled)
                                continue;
                            if (filteredLog.Key.IsWordEnabled && !log.Message.Contains(filteredLog.Key.Word))
                                continue;
                            if (filteredLog.Key.IsRegularExpressionEnabled && !Regex.Match(log.Message, filteredLog.Key.RegularExpression).Success)
                                continue;
                            if (filteredLog.Key.IsLevelEnabled && (log.Level < filteredLog.Key.MinimalLevel || log.Level > filteredLog.Key.MaximalLevel))
                                continue;
                            if (filteredLog.Key.IsKindEnabled && log.Kind != filteredLog.Key.Kind)
                                continue;
                            if (filteredLog.Key.IsGroundEnabled && log.Ground != filteredLog.Key.Ground)
                                continue;

                            while (filteredLog.Value.Count >= settings.MaximalHoldingCount)
                                filteredLog.Value.RemoveAt(filteredLog.Value.Count - 1);
                            filteredLog.Value.Insert(0, log);

                            FilterLogAdded(this, filteredLog.Key);
                        }
            }
        }

        public class ProgramSettings : SAVEABLESETTINGSDATA<ProgramSettings.Setter>
        {
            public class Setter
            {
                public Setter(Action<string> _cultureSetter, Action<string> _errorLogSetter, Action<string> _errorReportSetter, Action<bool> _isLogSetter)
                {
                    cultureSetter = _cultureSetter;
                    errorLogSetter = _errorLogSetter;
                    errorReportSetter = _errorReportSetter;
                    isLogSetter = _isLogSetter;
                }

                private readonly Action<string> cultureSetter;
                public string Culture
                {
                    set { cultureSetter(value); }
                }

                private readonly Action<string> errorLogSetter;
                public string ErrorLog
                {
                    set { errorLogSetter(value); }
                }

                private readonly Action<string> errorReportSetter;
                public string ErrorReport
                {
                    set { errorReportSetter(value); }
                }

                private readonly Action<bool> isLogSetter;
                public bool IsLog
                {
                    set { isLogSetter(value); }
                }
            }

            private string culture = "ja-JP";
            public string Culture
            {
                get { return culture; }
            }

            private string errorLog = "Error.txt";
            public string ErrorLog
            {
                get { return errorLog; }
            }

            private string errorReport = "ErrorReport.txt";
            public string ErrorReport
            {
                get { return errorReport; }
            }

            private bool isLog = true;
            public bool IsLog
            {
                get { return isLog; }
            }

            private LogSettings logSettings;
            public LogSettings LogSettings
            {
                get { return logSettings; }
            }

            public ProgramSettings()
                : base("ProgramSettings.xml")
            {
                logSettings = new LogSettings();
            }

            protected override string XmlName
            {
                get { return "ProgramSettings"; }
            }

            protected override MainDataInfomation[] MainDataInfo
            {
                get
                {
                    return new MainDataInfomation[]{
                        new MainDataInfomation(typeof(string), "Culture", () => culture, (o) => culture = (string)o), 
                        new MainDataInfomation(typeof(string), "ErrorLog", () => errorLog, (o) => errorLog = (string)o), 
                        new MainDataInfomation(typeof(string), "ErrorReport", () => errorReport, (o) => errorReport = (string)o), 
                        new MainDataInfomation(typeof(bool), "IsLog", () => isLog, (o) => isLog = (bool)o), 
                        new MainDataInfomation(typeof(LogSettings), "LogSettings", () => logSettings, (o) => logSettings = (LogSettings)o), 
                    };
                }
            }

            protected override Setter Setters
            {
                get
                {
                    return new Setter(
                        (_culture) => culture = _culture,
                        (_errorLog) => errorLog = _errorLog,
                        (_errorReport) => errorReport = _errorReport,
                        (_isLog) => isLog = _isLog);
                }
            }
        }

        public class LogSettings : SETTABLESETTINGSDATA<LogSettings.Setter>
        {
            public enum SaveMethod { allInOne, monthByMonth, dayByDay }

            public class Setter
            {
                public Setter(Action<int> _minimalLevel, Action<int> _maximalHoldingCount, Action<bool> _isSave, Action<string> _savePath, Action<SaveMethod> _saveMeth, Action<string> _expression)
                {
                    minimalLevel = _minimalLevel;
                    maximalholdingCount = _maximalHoldingCount;
                    isSave = _isSave;
                    savePath = _savePath;
                    saveMeth = _saveMeth;
                    expression = _expression;
                }

                private readonly Action<int> minimalLevel;
                public int MinimalLevel
                {
                    set { minimalLevel(value); }
                }

                private readonly Action<int> maximalholdingCount;
                public int MaximalHoldingCount
                {
                    set { maximalholdingCount(value); }
                }

                private readonly Action<bool> isSave;
                public bool IsSave
                {
                    set { isSave(value); }
                }

                private readonly Action<string> savePath;
                public string SavePath
                {
                    set { savePath(value); }
                }

                private readonly Action<SaveMethod> saveMeth;
                public SaveMethod SaveMeth
                {
                    set { saveMeth(value); }
                }

                private readonly Action<string> expression;
                public string Expression
                {
                    set { expression(value); }
                }
            }

            private int minimalLevel = 0;
            public int MinimalLevel
            {
                get { return minimalLevel; }
            }

            private int maximalHoldingCount = 64;
            public int MaximalHoldingCount
            {
                get { return maximalHoldingCount; }
            }

            private bool isSave = false;
            public bool IsSave
            {
                get { return isSave; }
            }

            private string savePath = "ErrorLog.log";
            public string SavePath
            {
                get { return savePath; }
            }

            private SaveMethod saveMeth = SaveMethod.allInOne;
            public SaveMethod SaveMeth
            {
                get { return saveMeth; }
            }

            private string expression = string.Empty;
            public string Expression
            {
                get { return expression; }
            }

            private readonly object filtersLock = new object();
            private List<LogFilter> filters;
            public LogFilter[] Filters
            {
                get { return filters.ToArray(); }
            }

            public LogSettings()
            {
                filters = new List<LogFilter>();
            }

            protected override string XmlName
            {
                get { return "LogSettings"; }
            }

            protected override SETTINGSDATA.MainDataInfomation[] MainDataInfo
            {
                get
                {
                    return new MainDataInfomation[]{
                        new MainDataInfomation(typeof(int), "MinimalLevel", () => minimalLevel, (o) => minimalLevel = (int)o), 
                        new MainDataInfomation(typeof(int), "MaximalHoldingCount", () => maximalHoldingCount, (o) => maximalHoldingCount = (int)o), 
                        new MainDataInfomation(typeof(bool), "IsSave", () => isSave, (o) => isSave = (bool)o), 
                        new MainDataInfomation(typeof(string), "SavePath", () => savePath, (o) => savePath = (string)o), 
                        new MainDataInfomation(typeof(int), "SaveMeth", () => (int)saveMeth, (o) => saveMeth = (SaveMethod)o), 
                        new MainDataInfomation(typeof(string), "Expression", () => expression, (o) => expression = (string)o), 
                        new MainDataInfomation(typeof(LogFilter[]), "Filters", () => filters.ToArray(), (o) => filters = ((LogFilter[])o).ToList()), 
                    };
                }
            }

            protected override LogSettings.Setter Setters
            {
                get
                {
                    return new Setter(
                        (_minimalLevel) => minimalLevel = _minimalLevel,
                        (_maximalHoldingCount) => maximalHoldingCount = _maximalHoldingCount,
                        (_isSave) => isSave = _isSave,
                        (_savePath) => savePath = _savePath,
                        (_saveMeth) => saveMeth = _saveMeth,
                        (_expression) => expression = _expression);
                }
            }

            public event EventHandler<LogFilter> FilterAdded = delegate { };
            public event EventHandler<LogFilter> FilterRemoved = delegate { };

            public void AddFilter(LogFilter filter)
            {
                lock (filtersLock)
                {
                    if (filters.Contains(filter))
                        throw new InvalidOperationException("exist_log_filter");

                    this.ExecuteBeforeEvent(() => filters.Add(filter), FilterAdded);
                }
            }

            public void RemoveFilter(LogFilter filter)
            {
                lock (filtersLock)
                {
                    if (!filters.Contains(filter))
                        throw new InvalidOperationException("not_exist_log_filter");

                    this.ExecuteBeforeEvent(() => filters.Remove(filter), FilterRemoved);
                }
            }
        }

        public class LogFilter : SETTABLESETTINGSDATA<LogFilter.Setter>
        {
            public class Setter
            {
                public Setter(Action<string> _name, Action<bool> _isEnabled, Action<bool> _isWordEnabled, Action<string> _word, Action<bool> _isRegularExpressionEnabled, Action<string> _regularExpression, Action<bool> _isLevelEnabled, Action<int> _minimalLevel, Action<int> _maximalLevel, Action<bool> _isKindEnabled, Action<LogData.LogKind> _kind, Action<bool> _isGroundEnabled, Action<LogData.LogGround> _ground)
                {
                    name = _name;
                    isEnabled = _isEnabled;
                    isWordEnabled = _isWordEnabled;
                    word = _word;
                    isRegularExpressionEnabled = _isRegularExpressionEnabled;
                    regularExpression = _regularExpression;
                    isLevelEnabled = _isLevelEnabled;
                    minimalLevel = _minimalLevel;
                    maximalLevel = _maximalLevel;
                    isKindEnabled = _isKindEnabled;
                    kind = _kind;
                    isGroundEnabled = _isGroundEnabled;
                    ground = _ground;
                }

                private readonly Action<string> name;
                public string Name
                {
                    set { name(value); }
                }

                private readonly Action<bool> isEnabled;
                public bool IsEnabled
                {
                    set { isEnabled(value); }
                }

                private readonly Action<bool> isWordEnabled;
                public bool IsWordEnabled
                {
                    set { isWordEnabled(value); }
                }

                private readonly Action<string> word;
                public string Word
                {
                    set { word(value); }
                }

                private readonly Action<bool> isRegularExpressionEnabled;
                public bool IsRegularExpressionEnabled
                {
                    set { isRegularExpressionEnabled(value); }
                }

                private readonly Action<string> regularExpression;
                public string RegularExpression
                {
                    set { regularExpression(value); }
                }

                private readonly Action<bool> isLevelEnabled;
                public bool IsLevelEnabled
                {
                    set { isLevelEnabled(value); }
                }

                private readonly Action<int> minimalLevel;
                public int MinimalLevel
                {
                    set { minimalLevel(value); }
                }

                private readonly Action<int> maximalLevel;
                public int MaximalLevel
                {
                    set { maximalLevel(value); }
                }

                private readonly Action<bool> isKindEnabled;
                public bool IsKindEnabled
                {
                    set { isKindEnabled(value); }
                }

                private readonly Action<LogData.LogKind> kind;
                public LogData.LogKind Kind
                {
                    set { kind(value); }
                }

                private readonly Action<bool> isGroundEnabled;
                public bool IsGroundEnabled
                {
                    set { isGroundEnabled(value); }
                }

                private readonly Action<LogData.LogGround> ground;
                public LogData.LogGround Ground
                {
                    set { ground(value); }
                }
            }

            private string name = string.Empty;
            public string Name
            {
                get { return name; }
            }

            private bool isEnabled = false;
            public bool IsEnabled
            {
                get { return isEnabled; }
            }

            private bool isWordEnabled = false;
            public bool IsWordEnabled
            {
                get { return isWordEnabled; }
            }

            private string word = string.Empty;
            public string Word
            {
                get { return word; }
            }

            private bool isRegularExpressionEnabled = false;
            public bool IsRegularExpressionEnabled
            {
                get { return isRegularExpressionEnabled; }
            }

            private string regularExpression = string.Empty;
            public string RegularExpression
            {
                get { return regularExpression; }
            }

            private bool isLevelEnabled = false;
            public bool IsLevelEnabled
            {
                get { return isLevelEnabled; }
            }

            private int minimalLevel = 0;
            public int MinimalLevel
            {
                get { return minimalLevel; }
            }

            private int maximalLevel = 5;
            public int MaximalLevel
            {
                get { return maximalLevel; }
            }

            private bool isKindEnabled = false;
            public bool IsKindEnabled
            {
                get { return isKindEnabled; }
            }

            //<未改良>複数指定
            private LogData.LogKind kind = LogData.LogKind.error;
            public LogData.LogKind Kind
            {
                get { return kind; }
            }

            private bool isGroundEnabled = false;
            public bool IsGroundEnabled
            {
                get { return isGroundEnabled; }
            }

            //<未改良>複数指定
            private LogData.LogGround ground = LogData.LogGround.core;
            public LogData.LogGround Ground
            {
                get { return ground; }
            }

            protected override string XmlName
            {
                get { return "LogFilter"; }
            }

            protected override MainDataInfomation[] MainDataInfo
            {
                get
                {
                    return new MainDataInfomation[]{
                        new MainDataInfomation(typeof(string), "Name", () => name, (o) => name = (string)o), 
                        new MainDataInfomation(typeof(bool), "IsEnabled", () => isEnabled, (o) => isEnabled = (bool)o), 
                        new MainDataInfomation(typeof(bool), "IsWordEnabled", () => isWordEnabled, (o) => isWordEnabled = (bool)o), 
                        new MainDataInfomation(typeof(string), "Word", () => word, (o) => word = (string)o), 
                        new MainDataInfomation(typeof(bool), "IsRegularExpressionEnabled", () => isRegularExpressionEnabled, (o) => isRegularExpressionEnabled = (bool)o), 
                        new MainDataInfomation(typeof(string), "RegularExpression", () => regularExpression, (o) => regularExpression = (string)o), 
                        new MainDataInfomation(typeof(bool), "IsLevelEnabled", () => isLevelEnabled, (o) => isLevelEnabled = (bool)o), 
                        new MainDataInfomation(typeof(int), "MinimalLevel", () => minimalLevel, (o) => minimalLevel = (int)o), 
                        new MainDataInfomation(typeof(int), "MaximalLevel", () => maximalLevel, (o) => maximalLevel = (int)o), 
                        new MainDataInfomation(typeof(bool), "IsKindEnabled", () => isKindEnabled, (o) => isKindEnabled = (bool)o), 
                        new MainDataInfomation(typeof(int), "Kind", () => (int)kind, (o) => kind = (LogData.LogKind)o), 
                        new MainDataInfomation(typeof(bool), "IsGroundEnabled", () => isGroundEnabled, (o) => isGroundEnabled = (bool)o), 
                        new MainDataInfomation(typeof(int), "Ground", () => (int)ground, (o) => ground = (LogData.LogGround)o), 
                    };
                }
            }

            protected override LogFilter.Setter Setters
            {
                get
                {
                    return new Setter(
                        (_name) => name = _name,
                        (_isEnabled) => isEnabled = _isEnabled,
                        (_isWordEnabled) => isWordEnabled = _isWordEnabled,
                        (_word) => word = _word,
                        (_isRegularExpressionEnabled) => isRegularExpressionEnabled = _isRegularExpressionEnabled,
                        (_regularExpression) => regularExpression = _regularExpression,
                        (_isLevelEnabled) => isLevelEnabled = _isLevelEnabled,
                        (_minimalLevel) => minimalLevel = _minimalLevel,
                        (_maximalLevel) => maximalLevel = _maximalLevel,
                        (_isKindEnabled) => isKindEnabled = _isKindEnabled,
                        (_kind) => kind = _kind,
                        (_isGroundEnabled) => isGroundEnabled = _isGroundEnabled,
                        (_ground) => ground = _ground);
                }
            }
        }

        public class ProgramStatus
        {
            private bool isFirst = true;
            public bool IsFirst
            {
                get { return isFirst; }
                set { isFirst = value; }
            }

            private bool isWrong = false;
            public bool IsWrong
            {
                get { return isWrong; }
                set { isWrong = value; }
            }
        }

        private static string[] langResource;
        private static Dictionary<string, Func<string>> taskNames;
        private static Dictionary<string, Func<string>> taskDescriptions;
        private static Dictionary<Type, LogData.LogGround> logGrounds;
        private static Dictionary<string, Func<string>> logMessages;
        private static Dictionary<string, Func<string>> exceptionMessages;

        public static string Multilanguage(string text, int id)
        {
            //<未実装>機械翻訳への対応
            return langResource == null || id >= langResource.Length ? text : langResource[id];
        }

        public static string GetTaskName(string rawName)
        {
            return taskNames.GetValue(rawName, () => rawName)();
        }

        public static string GetTaskDescription(string rawDescription)
        {
            return taskDescriptions.GetValue(rawDescription, () => rawDescription)();
        }

        public static LogData.LogGround GetLogGround(Type type)
        {
            return logGrounds.GetValue(type, LogData.LogGround.other);
        }

        public static string GetLogMessage(string rawMessage)
        {
            return logMessages.GetValue(rawMessage, () => rawMessage)();
        }

        public static string GetExceptionMessage(string rawMessage)
        {
            return exceptionMessages.GetValue(rawMessage, () => rawMessage)();
        }

        [STAThread]
        public static void Main()
        {
            string appname = "CREA2014";
            int verMaj = 0;
            int verMin = 0;
            int verMMin = 1;
            string verS = "α";
            int verR = 1; //リリース番号（リリース毎に増やす番号）
            int verC = 21; //コミット番号（コミット毎に増やす番号）
            string version = string.Join(".", verMaj.ToString(), verMin.ToString(), verMMin.ToString()) + "(" + verS + ")" + "(" + verR.ToString() + ")" + "(" + verC.ToString() + ")";
            string appnameWithVersion = string.Join(" ", appname, version);

            string lisenceTextFilename = "Lisence.txt";

            ProgramSettings psettings = new ProgramSettings();
            ProgramStatus pstatus = new ProgramStatus();

            Logger logger;
            Tasker tasker = new Tasker();
            int taskNumber = 0;

            Assembly assembly = Assembly.GetEntryAssembly();
            string basepath = new FileInfo(assembly.Location).DirectoryName;

            Mutex mutex;

            Core core = null;

            if (psettings.Culture == "ja-JP")
                using (Stream stream = assembly.GetManifestResourceStream(@"CREA2014.Resources.langResouece_ja-JP.txt"))
                using (StreamReader sr = new StreamReader(stream))
                    langResource = sr.ReadToEnd().Split(new string[] { Environment.NewLine }, StringSplitOptions.None);
            else
            {
                string path = Path.Combine(basepath, "lang", "langResource_" + psettings.Culture + ".txt");
                if (File.Exists(path))
                    langResource = File.ReadAllLines(path);
                else
                    langResource = new string[] { };
            }

            taskNames = new Dictionary<string, Func<string>>() { };

            taskDescriptions = new Dictionary<string, Func<string>>() { };

            logGrounds = new Dictionary<Type, LogData.LogGround>(){
                {typeof(AccountHolderDatabase), LogData.LogGround.signData}, 
                {typeof(Client), LogData.LogGround.networkBase}, 
                {typeof(Listener), LogData.LogGround.networkBase}, 
            };

            logMessages = new Dictionary<string, Func<string>>() { 
                {"exist_same_name_account_holder", () => "同名の口座名義人が存在します。".Multilanguage(93)}, 
                {"client_socket", () => "エラーが発生しました。".Multilanguage(94)}, 
                {"listener_socket", () => "エラーが発生しました。".Multilanguage(95)}, 
                {"task", () => "エラーが発生しました。".Multilanguage(96)}, 
                {"task_aborted", () => "作業が強制終了されました。".Multilanguage(97)}, 
                {"all_tasks_aborted", () => "全ての作業が強制終了されました。".Multilanguage(98)}, 
            };

            exceptionMessages = new Dictionary<string, Func<string>>() {
                {"already_starting", () => string.Format("{0}は既に起動しています。".Multilanguage(0), appname)}, 
                {"ie_not_existing", () => string.Format("{0}の動作には Internet Explorer 10 以上が必要です。".Multilanguage(1), appname)}, 
                {"ie_too_old", () => string.Format("{0}の動作には Internet Explorer 10 以上が必要です。".Multilanguage(2), appname)}, 
                {"require_administrator", () => string.Format("{0}は管理者として実行する必要があります。".Multilanguage(3), appname)}, 
                {"lisence_text_not_found", () => "ソフトウェア使用許諾契約書が見付かりません。".Multilanguage(90)}, 
                {"web_server_data", () => "内部ウェブサーバデータが存在しません。".Multilanguage(91)}, 
                {"wss_command", () => "内部ウェブソケット命令が存在しません。".Multilanguage(92)}, 
            };

            Extension.Tasked += (sender, e) => tasker.New(new TaskData(e, taskNumber++));

            if (psettings.IsLog)
            {
                logger = new Logger(psettings.LogSettings);

                Extension.Tested += (sender, e) => logger.AddLog(new LogData(e, LogData.LogKind.test));
                Extension.Notified += (sender, e) => logger.AddLog(new LogData(e, LogData.LogKind.notification));
                Extension.Resulted += (sender, e) => logger.AddLog(new LogData(e, LogData.LogKind.result));
                Extension.Warned += (sender, e) => logger.AddLog(new LogData(e, LogData.LogKind.warning));
                Extension.Errored += (sender, e) => logger.AddLog(new LogData(e, LogData.LogKind.error));
            }

            //Listener listener = new Listener(7777, RsaKeySize.rsa2048, (ca, ip) =>
            //{
            //    string message = Encoding.UTF8.GetString(ca.ReadCompressedBytes());

            //    MessageBox.Show(message);
            //});
            //listener.StartListener();

            //Thread.Sleep(1000);

            //string privateRSAParameters;
            //using (RSACryptoServiceProvider rsacsp = new RSACryptoServiceProvider(2048))
            //    privateRSAParameters = rsacsp.ToXmlString(true);

            //Client client = new Client("127.0.0.1", 7777, RsaKeySize.rsa2048, privateRSAParameters, (ca, ip) =>
            //{
            //    ca.WriteCompreddedBytes(Encoding.UTF8.GetBytes("テストだよ～"));
            //});
            //client.StartClient();

            //Thread.Sleep(10000);

            //Console.ReadLine();

            Action<Exception, ExceptionKind> _OnException = (ex, exKind) =>
            {
                string exceptionMessage = ex.Message.GetExceptionMessage();

                if (exceptionMessage != ex.Message)
                    MessageBox.Show(exceptionMessage);
                else
                {
                    string kind = "Kind: " + (exKind == ExceptionKind.wpf ? "ThreadException" : "UnhandledException");
                    string message = ex.CreateMessage(0);

                    message = string.Join(Environment.NewLine, kind, message);


                    string errlogPath = Path.Combine(basepath, psettings.ErrorLog);

                    using (FileStream fs = new FileStream(errlogPath, FileMode.Append))
                    using (StreamWriter sr = new StreamWriter(fs))
                    {
                        string date = DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss.fff");
                        string log = string.Join(Environment.NewLine, date, message);

                        sr.Write(log);
                        sr.Write(Environment.NewLine.Repeat(2));
                    }


                    string messageText = "未知の問題が発生しました。この問題を開発者に報告しますか？".Multilanguage(4);
                    if (MessageBox.Show(messageText, appname, MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                    {
                        string cpu = string.Empty;
                        try
                        {
                            ManagementClass mc = new ManagementClass("Win32_Processor");
                            ManagementObjectCollection moc = mc.GetInstances();
                            foreach (ManagementObject mo in moc)
                                if (mo["Name"] != null)
                                    cpu = mo["Name"].ToString();
                        }
                        catch (Exception) { }

                        string videoCard = string.Empty;
                        try
                        {
                            ManagementClass mc = new ManagementClass("Win32_VideoController");
                            ManagementObjectCollection moc = mc.GetInstances();
                            foreach (ManagementObject mo in moc)
                                if (mo["Name"] != null)
                                    videoCard = mo["Name"].ToString();
                        }
                        catch (Exception) { }

                        string memory = string.Empty;
                        string architecture = string.Empty;
                        try
                        {
                            ManagementClass mc = new ManagementClass("Win32_OperatingSystem");
                            ManagementObjectCollection moc = mc.GetInstances();
                            foreach (ManagementObject mo in moc)
                            {
                                if (mo["TotalVisibleMemorySize"] != null)
                                    memory = mo["TotalVisibleMemorySize"].ToString();
                                if (mo["OSArchitecture"] != null)
                                    architecture = mo["OSArchitecture"].ToString() == "" ? "x86" : mo["OSArchitecture"].ToString();
                            }
                        }
                        catch (Exception) { }

                        string dotnetVerAll = string.Empty;
                        List<string> versions = new List<string>();
                        string dotnetRegPath = @"SOFTWARE\Microsoft\NET Framework Setup\NDP";
                        string dotnetRegPath2 = Path.Combine(dotnetRegPath, @"v4\Full");

                        using (RegistryKey dotnetKey = Registry.LocalMachine.OpenSubKey(dotnetRegPath, false))
                            if (dotnetKey != null)
                                foreach (var subkey in dotnetKey.GetSubKeyNames())
                                    if (subkey.StartsWith("v"))
                                        versions.Add(subkey.Substring(1));

                        using (RegistryKey dotnetKey = Registry.LocalMachine.OpenSubKey(dotnetRegPath2, false))
                            if (dotnetKey != null)
                            {
                                object release = dotnetKey.GetValue("Release");
                                if (release != null)
                                {
                                    int dotnetver;
                                    if (int.TryParse(release.ToString(), out dotnetver))
                                        if (dotnetver >= 378389)
                                            versions.Add("4.5");
                                        else if (dotnetver >= 378681)
                                            versions.AddRange(new string[] { "4.5", "4.5.1" });
                                }
                            }

                        dotnetVerAll = string.Join(" ", versions);

                        string ver = "【バージョン】".Multilanguage(5) + version;
                        string winVer = "【Windowsのバージョン】".Multilanguage(6) + Environment.OSVersion.ToString() + " (" + architecture + ")";
                        string dotnetVerRun = "【.NET Frameworkのバージョン（実行中）】".Multilanguage(7) + Environment.Version;
                        string dotnetVerAll2 = "【.NET Frameworkのバージョン（全て）】".Multilanguage(8) + dotnetVerAll;
                        string cpu2 = "【CPU】".Multilanguage(9) + cpu;
                        string videoCard2 = "【ビデオカード】".Multilanguage(10) + videoCard;
                        string memory2 = "【メモリ】".Multilanguage(11) + memory + "KB";

                        string text = string.Join(Environment.NewLine, ver, winVer, dotnetVerRun, dotnetVerAll2, cpu2, videoCard2, memory2, message);

                        //<未改良>ExceptionWindow
                        //<未改良>pizyumi.com内のWebサービスに送信
                        string setumei = "以下の内容を開発者に報告してください。".Multilanguage(12);
                        string splitter = "-".Repeat(80);

                        text = string.Join(Environment.NewLine, setumei, splitter, text);

                        string errreportPath = Path.Combine(basepath, psettings.ErrorReport);
                        File.WriteAllText(errreportPath, text);
                        Process.Start("notepad.exe", errreportPath);
                    }
                }

                if (core != null)
                    core.EndSystem();
                psettings.Save();

                Environment.Exit(0);
            };

            AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
            {
                Exception ex = e.ExceptionObject as Exception;
                if (ex != null)
                    _OnException(ex, ExceptionKind.unhandled);
            };

            Thread.CurrentThread.CurrentCulture = new CultureInfo(psettings.Culture);
            Thread.CurrentThread.CurrentUICulture = new CultureInfo(psettings.Culture);

            // Windows 2000（NT 5.0）以降のみグローバル・ミューテックス利用可
            string appNameMutex = appname + " by Piz Yumina";
            OperatingSystem os = Environment.OSVersion;
            if ((os.Platform == PlatformID.Win32NT) && (os.Version.Major >= 5))
                appNameMutex = @"Global\" + appNameMutex;

            try
            {
                mutex = new Mutex(false, appNameMutex);
            }
            catch (ApplicationException)
            {
                throw new ApplicationException("already_starting"); //対応済
            }

            if (mutex.WaitOne(0, false))
            {
                string ieRegPath = @"SOFTWARE\Microsoft\Internet Explorer";
                int ieVersion;
                using (RegistryKey ieKey = Registry.LocalMachine.OpenSubKey(ieRegPath, RegistryKeyPermissionCheck.ReadSubTree, RegistryRights.QueryValues))
                {
                    object v = ieKey.GetValue("svcVersion");
                    if (v == null)
                    {
                        v = ieKey.GetValue("Version");
                        if (v == null)
                            throw new ApplicationException("ie_not_existing"); //対応済
                    }
                    int.TryParse(v.ToString().Split('.')[0], out ieVersion);
                }
                if (ieVersion < 10)
                    throw new ApplicationException("ie_too_old"); //対応済

                Process process = Process.GetCurrentProcess();
                string fileName = Path.GetFileName(process.MainModule.FileName);
                if (String.Compare(fileName, "devenv.exe", true) != 0 && String.Compare(fileName, "XDesProc.exe", true) != 0)
                {
                    string basepathFC = @"Software\Microsoft\Internet Explorer\Main\FeatureControl";

                    Action<string, uint> _FeatureControl = (feature, value) =>
                    {
                        string path = Path.Combine(basepathFC, feature);

                        using (RegistryKey key = Registry.CurrentUser.CreateSubKey(path, RegistryKeyPermissionCheck.ReadWriteSubTree))
                            key.SetValue(fileName, (UInt32)value, RegistryValueKind.DWord);
                    };

                    _FeatureControl("FEATURE_BROWSER_EMULATION", (uint)(ieVersion == 10 ? 10000 : 11001));
                    _FeatureControl("FEATURE_AJAX_CONNECTIONEVENTS", 1);
                    _FeatureControl("FEATURE_ENABLE_CLIPCHILDREN_OPTIMIZATION", 1);
                    _FeatureControl("FEATURE_MANAGE_SCRIPT_CIRCULAR_REFS", 1);
                    _FeatureControl("FEATURE_DOMSTORAGE ", 1);
                    _FeatureControl("FEATURE_GPU_RENDERING ", 1);
                    _FeatureControl("FEATURE_IVIEWOBJECTDRAW_DMLT9_WITH_GDI  ", 0);
                    _FeatureControl("FEATURE_NINPUT_LEGACYMODE", 0);
                    _FeatureControl("FEATURE_DISABLE_LEGACY_COMPRESSION", 1);
                    _FeatureControl("FEATURE_LOCALMACHINE_LOCKDOWN", 0);
                    _FeatureControl("FEATURE_BLOCK_LMZ_OBJECT", 0);
                    _FeatureControl("FEATURE_BLOCK_LMZ_SCRIPT", 0);
                    _FeatureControl("FEATURE_DISABLE_NAVIGATION_SOUNDS", 1);
                    _FeatureControl("FEATURE_SCRIPTURL_MITIGATION", 1);
                    _FeatureControl("FEATURE_SPELLCHECKING", 0);
                    _FeatureControl("FEATURE_STATUS_BAR_THROTTLING", 1);
                    _FeatureControl("FEATURE_TABBED_BROWSING", 1);
                    _FeatureControl("FEATURE_VALIDATE_NAVIGATE_URL", 1);
                    _FeatureControl("FEATURE_WEBOC_DOCUMENT_ZOOM", 1);
                    _FeatureControl("FEATURE_WEBOC_POPUPMANAGEMENT", 0);
                    _FeatureControl("FEATURE_WEBOC_MOVESIZECHILD", 1);
                    _FeatureControl("FEATURE_ADDON_MANAGEMENT", 0);
                    _FeatureControl("FEATURE_WEBSOCKET", 1);
                    _FeatureControl("FEATURE_WINDOW_RESTRICTIONS ", 0);
                    _FeatureControl("FEATURE_XMLHTTP", 1);
                }

                core = new Core(basepath);
                core.StartSystem();

                App app = new App();
                app.DispatcherUnhandledException += (sender, e) =>
                {
                    _OnException(e.Exception, ExceptionKind.wpf);
                };
                app.Startup += (sender, e) =>
                {
                    MainWindow mw = new MainWindow(core, psettings, pstatus, appname, version, appnameWithVersion, lisenceTextFilename, assembly, basepath, _OnException);
                    mw.Show();
                };
                app.InitializeComponent();
                app.Run();

                core.EndSystem();
                psettings.Save();

                mutex.ReleaseMutex();
            }
            else
            {
                Process prevProcess = null;
                Process currentProcess = Process.GetCurrentProcess();
                Process[] allProcesses = Process.GetProcessesByName(currentProcess.ProcessName);

                foreach (var p in allProcesses)
                    if (p.Id != currentProcess.Id && string.Compare(p.MainModule.FileName, currentProcess.MainModule.FileName, true) == 0)
                    {
                        prevProcess = p;
                        break;
                    }

                if (prevProcess != null && prevProcess.MainWindowHandle != IntPtr.Zero)
                {
                    if (WIN32API.IsIconic(prevProcess.MainWindowHandle))
                        WIN32API.ShowWindowAsync(prevProcess.MainWindowHandle, WIN32API.SW_RESTORE);

                    WIN32API.SetForegroundWindow(prevProcess.MainWindowHandle);
                }
                else
                    throw new ApplicationException("already_starting"); //対応済
            }

            mutex.Close();
        }
    }
}