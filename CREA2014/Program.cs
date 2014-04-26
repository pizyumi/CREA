using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management;
using System.Net;
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
                    throw new InvalidOperationException("one_time");
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

    public class MultipleReturn<T, U>
    {
        public MultipleReturn(T _value1)
        {
            Value1 = _value1;
        }

        public MultipleReturn(U _value2)
        {
            Value2 = _value2;
        }

        public T Value1 { get; private set; }
        public U Value2 { get; private set; }

        public bool IsValue1
        {
            get { return Value1 != null; }
        }

        public bool IsValue2
        {
            get { return Value2 != null; }
        }
    }

    #endregion

    #region 拡張メソッド

    public static class Extension
    {
        #region 一般

        //操作型を受け取ってそのまま返す（拡張：操作型）
        public static Action Lambda<T>(this T dummy, Action action)
        {
            return action;
        }

        //関数型を受け取ってそのまま返す（拡張：関数型）
        public static Func<U> Lambda<T, U>(this T dummy, Func<U> func)
        {
            return func;
        }

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
        public static void BeginExecuteInUIThread(this Action action)
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

        //プライベートIPアドレスか（拡張：IPアドレス型）
        public static bool IsPrivate(this IPAddress ipAddress)
        {
            if (ipAddress.AddressFamily == AddressFamily.InterNetwork)
            {
                byte[] ipAddressBytes = ipAddress.GetAddressBytes();
                if (ipAddressBytes[0] == 10)
                    return true;
                else if (ipAddressBytes[0] == 172)
                    return ipAddressBytes[1] >= 16 && ipAddressBytes[1] <= 31;
                else if (ipAddressBytes[0] == 192)
                    return ipAddressBytes[1] == 168;
                else
                    return false;
            }
            else
                return false;
        }

        //グローバルIPアドレスか（拡張：IPアドレス型）
        public static bool IsGlobal(this IPAddress ipAddress)
        {
            if (ipAddress.AddressFamily == AddressFamily.InterNetwork)
            {
                byte[] ipAddressBytes = ipAddress.GetAddressBytes();
                if (ipAddressBytes[0] >= 1 && ipAddressBytes[0] <= 9)
                    return true;
                else if (ipAddressBytes[0] >= 11 && ipAddressBytes[0] <= 126)
                    return true;
                else if (ipAddressBytes[0] >= 128 && ipAddressBytes[0] <= 171)
                    return true;
                else if (ipAddressBytes[0] == 172)
                    return ipAddressBytes[1] <= 15 || ipAddressBytes[1] >= 32;
                else if (ipAddressBytes[0] >= 173 && ipAddressBytes[0] <= 191)
                    return true;
                else if (ipAddressBytes[0] == 192)
                    return ipAddressBytes[1] != 168;
                else if (ipAddressBytes[0] >= 193 && ipAddressBytes[0] <= 223)
                    return true;
                else
                    return false;
            }
            else
                return true;
        }

        //ローカルIPアドレスか（拡張：IPアドレス型）
        public static bool IsLocal(this IPAddress ipAddress)
        {
            byte[] ipAddressBytes = ipAddress.GetAddressBytes();
            if (ipAddress.AddressFamily == AddressFamily.InterNetwork)
                return ipAddressBytes[0] == 127 && ipAddressBytes[1] == 0 && ipAddressBytes[2] == 0 && ipAddressBytes[3] == 1;
            else if (ipAddress.AddressFamily == AddressFamily.InterNetworkV6)
            {
                for (int i = 0; i < 15; i++)
                    if (ipAddressBytes[i] != 0)
                        return false;
                return ipAddressBytes[15] == 1;
            }
            else
                return false;
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
                throw new ArgumentException("hexstring_length");

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
                throw new ArgumentException("string_split_length");

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

        //バイト配列の内容が0であるか判定する（拡張：バイト配列型）
        public static bool IsZeroBytes(this byte[] bytes)
        {
            for (int i = 0; i < bytes.Length; i++)
                if (bytes[i] != 0)
                    return false;
            return true;
        }

        //二つのバイト配列の内容が等しいか判定する（拡張：バイト配列型）
        public static bool BytesEquals(this byte[] bytes1, byte[] bytes2)
        {
            if (bytes1.Length != bytes2.Length)
                return false;
            for (int i = 0; i < bytes1.Length; i++)
                if (bytes1[i] != bytes2[i])
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

        //関数を実行する（拡張：任意型）
        public static T Operate<T>(this T self, Action operation)
        {
            operation();
            return self;
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
        private static double[] cache = new double[] { };

        //0からiまでの無作為な整数を返す（拡張：整数型）
        public static int RandomNum(this int i)
        {
            return random.Next(i);
        }

        //0からiまでの無作為な浮動小数点数を返す（拡張：整数型）
        public static double RandomDouble(this int i)
        {
            return random.NextDouble() * i;
        }

        //0からiまでの整数が1回ずつ無作為な順番で含まれる配列を作成する（拡張：整数型）
        public static int[] RandomNums(this int i)
        {
            return random.OperateWhileTrue(r => r.Next(i)).Distinct().Take(i).ToArray();
        }

        //常に同一の0から1までの無作為な浮動小数点数を返す
        public static IEnumerable<double> RandomDoublesCache()
        {
            for (int i = 0; ; i++)
            {
                if (i >= cache.Length)
                    cache = cache.Combine(random.OperateWhileTrue(r => r.NextDouble()).Take(100).ToArray());
                yield return cache[i];
            }
        }

        //常に同一の0からiまでの整数が1回ずつ無作為な順番で含まれる配列を作成する（拡張：整数型）
        public static int[] RandomNumsCache(this int i)
        {
            return RandomDoublesCache().Select((e) => (int)(e * i)).Distinct().Take(i).ToArray();
        }

        //バイト配列の要素を指定された順番で並べ直した新たなバイト配列を作成する（拡張：バイト配列型）
        public static byte[] BytesRandom(this byte[] bytes, int[] order)
        {
            byte[] newbytes = new byte[bytes.Length];
            for (int i = 0; i < bytes.Length; i++)
                newbytes[i] = bytes[order[i]];
            return newbytes;
        }

        //バイト配列の要素を無作為な順番で並べ直した新たなバイト配列を作成する（拡張：バイト配列型）
        public static byte[] BytesRandom(this byte[] bytes)
        {
            return bytes.BytesRandom(bytes.Length.RandomNums());
        }

        //バイト配列の要素を常に同一の無作為な順番で並べ直した新たなバイト配列を作成する（拡張：バイト配列型）
        public static byte[] BytesRandomCache(this byte[] bytes)
        {
            return bytes.BytesRandom(bytes.Length.RandomNumsCache());
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

        //配列を分解する（拡張：任意の配列型）
        public static T[] Decompose<T>(this T[] self, int start)
        {
            T[] decomposed = new T[self.Length - start];
            Array.Copy(self, start, decomposed, 0, decomposed.Length);
            return decomposed;
        }

        //配列を複製する（拡張：任意の配列型）
        public static T[] Reprecate<T>(this T[] self)
        {
            T[] reprecated = new T[self.Length];
            Array.Copy(self, reprecated, self.Length);
            return reprecated;
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

        public static void ConsoleWriteLine(this string text)
        {
            ((Action)(() => Console.WriteLine(text))).BeginExecuteInUIThread();
        }

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
            public readonly string[] Arguments;

            public LogInfomation(Type _type, string _message, int _level, string[] _arguments)
            {
                Type = _type;
                Message = _message;
                Level = _level;
                Arguments = _arguments;
            }

            public LogInfomation(Type _type, string _message, int _level) : this(_type, _message, _level, null) { }
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
        public static void RaiseTest<T>(this T self, string message, int level, string[] arguments)
        {
            Tested(self.GetType(), new LogInfomation(self.GetType(), message, level, arguments));
        }

        //通知ログイベントを発生させる（拡張：型表現型）
        public static void RaiseNotification<T>(this T self, string message, int level)
        {
            Notified(self.GetType(), new LogInfomation(self.GetType(), message, level));
        }
        public static void RaiseNotification<T>(this T self, string message, int level, string[] arguments)
        {
            Notified(self.GetType(), new LogInfomation(self.GetType(), message, level, arguments));
        }

        //結果ログイベントを発生させる（拡張：任意型）
        public static void RaiseResult<T>(this T self, string message, int level)
        {
            Resulted(self.GetType(), new LogInfomation(self.GetType(), message, level));
        }
        public static void RaiseResult<T>(this T self, string message, int level, string[] arguments)
        {
            Resulted(self.GetType(), new LogInfomation(self.GetType(), message, level, arguments));
        }

        //警告ログイベントを発生させる（拡張：任意型）
        public static void RaiseWarning<T>(this T self, string message, int level)
        {
            Warned(self.GetType(), new LogInfomation(self.GetType(), message, level));
        }
        public static void RaiseWarning<T>(this T self, string message, int level, string[] arguments)
        {
            Warned(self.GetType(), new LogInfomation(self.GetType(), message, level, arguments));
        }

        //過誤ログイベントを発生させる（拡張：任意型）
        public static void RaiseError<T>(this T self, string message, int level)
        {
            Errored(self.GetType(), new LogInfomation(self.GetType(), message, level));
        }
        public static void RaiseError<T>(this T self, string message, int level, string[] arguments)
        {
            Errored(self.GetType(), new LogInfomation(self.GetType(), message, level, arguments));
        }

        //例外過誤ログイベントを発生させる（拡張：任意型）
        public static void RaiseError<T>(this T self, string message, int level, Exception ex)
        {
            Errored(self.GetType(), new LogInfomation(self.GetType(), string.Join(Environment.NewLine, message, ex.CreateMessage(0)), level));
        }
        public static void RaiseError<T>(this T self, string message, int level, Exception ex, string[] arguments)
        {
            Errored(self.GetType(), new LogInfomation(self.GetType(), string.Join(Environment.NewLine, message, ex.CreateMessage(0)), level, arguments));
        }

        //真偽値が真のときのみ試験ログイベントを発生させ、真偽値をそのまま返す（拡張：真偽型）
        public static bool RaiseTest(this bool flag, Type type, string message, int level)
        {
            if (flag)
                type.RaiseTest(message, level);

            return flag;
        }

        //真偽値が真のときのみ通知ログイベントを発生させ、真偽値をそのまま返す（拡張：真偽型）
        public static bool RaiseNotification(this bool flag, Type type, string message, int level)
        {
            if (flag)
                type.RaiseNotification(message, level);

            return flag;
        }

        //真偽値が真のときのみ結果ログイベントを発生させ、真偽値をそのまま返す（拡張：真偽型）
        public static bool RaiseResult(this bool flag, Type type, string message, int level)
        {
            if (flag)
                type.RaiseResult(message, level);

            return flag;
        }

        //真偽値が真のときのみ警告ログイベントを発生させ、真偽値をそのまま返す（拡張：真偽型）
        public static bool RaiseWarning(this bool flag, Type type, string message, int level)
        {
            if (flag)
                type.RaiseWarning(message, level);

            return flag;
        }

        //真偽値が真のときのみ過誤ログイベントを発生させ、真偽値をそのまま返す（拡張：真偽型）
        public static bool RaiseError(this bool flag, Type type, string message, int level)
        {
            if (flag)
                type.RaiseError(message, level);

            return flag;
        }

        //真偽値が偽のときのみ試験ログイベントを発生させ、真偽値をそのまま返す（拡張：真偽型）
        public static bool NotRaiseTest(this bool flag, Type type, string message, int level)
        {
            if (!flag)
                type.RaiseTest(message, level);

            return flag;
        }

        //真偽値が偽のときのみ通知ログイベントを発生させ、真偽値をそのまま返す（拡張：真偽型）
        public static bool NotRaiseNotification(this bool flag, Type type, string message, int level)
        {
            if (!flag)
                type.RaiseNotification(message, level);

            return flag;
        }

        //真偽値が偽のときのみ結果ログイベントを発生させ、真偽値をそのまま返す（拡張：真偽型）
        public static bool NotRaiseResult(this bool flag, Type type, string message, int level)
        {
            if (!flag)
                type.RaiseResult(message, level);

            return flag;
        }

        //真偽値が偽のときのみ警告ログイベントを発生させ、真偽値をそのまま返す（拡張：真偽型）
        public static bool NotRaiseWarning(this bool flag, Type type, string message, int level)
        {
            if (!flag)
                type.RaiseWarning(message, level);

            return flag;
        }

        //真偽値が偽のときのみ過誤ログイベントを発生させ、真偽値をそのまま返す（拡張：真偽型）
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
            return Program.GetLogMessage(rawMessage, null);
        }
        public static string GetLogMessage(this string rawMessage, params string[] arguments)
        {
            return Program.GetLogMessage(rawMessage, arguments);
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
    public abstract class STREAMDATA<T> : DATA where T : STREAMDATA<T>.StreamInfomation
    {
        public abstract class STREAMWRITER
        {
            public abstract void WriteBytes(byte[] data, int? length);
            public abstract void WriteBool(bool data);
            public abstract void WriteInt(int data);
            public abstract void WriteUint(uint data);
            public abstract void WriteFloat(float data);
            public abstract void WriteLong(long data);
            public abstract void WriteDouble(double data);
            public abstract void WriteDateTime(DateTime data);
            public abstract void WriteString(string data);
            public abstract void WriteSHAREDDATA(SHAREDDATA data, int? version);
        }

        public abstract class STREAMREADER
        {
            public abstract byte[] ReadBytes(int? length);
            public abstract bool ReadBool();
            public abstract int ReadInt();
            public abstract uint ReadUint();
            public abstract float ReadFloat();
            public abstract long ReadLong();
            public abstract double ReadDouble();
            public abstract DateTime ReadDateTime();
            public abstract string ReadString();
            public abstract SHAREDDATA ReadSHAREDDATA(Type type, int? version);
        }

        public class ReaderWriter
        {
            private readonly STREAMWRITER writer;
            private readonly STREAMREADER reader;
            private readonly Mode mode;

            public ReaderWriter(STREAMWRITER _writer, STREAMREADER _reader, Mode _mode)
            {
                writer = _writer;
                reader = _reader;
                mode = _mode;
            }

            public enum Mode { read, write, neither }
            public class CantReadOrWriteException : Exception { }

            public byte[] ReadOrWrite(byte[] bytes, int length)
            {
                if (mode == Mode.read)
                    return reader.ReadBytes(length);
                else if (mode == Mode.write)
                {
                    writer.WriteBytes(bytes, length);
                    return null;
                }
                else
                    throw new CantReadOrWriteException();
            }
        }

        public abstract class StreamInfomation
        {
            //2014/02/23
            //抽象クラスには対応しない
            //抽象クラスの変数に格納されている具象クラスを保存する場合には具象クラスとしてStreamInfomationを作成する
            //具象クラスが複数ある場合には具象クラス別にStreamInfomationを作成する

            //SHAREDDATA（の派生クラス）の配列専用
            public StreamInfomation(Type _type, int? _version, int? _length, Func<object> _sender, Action<object> _receiver)
            {
                if (!_type.IsArray)
                    throw new ArgumentException("stream_info_not_array");

                Type elementType = _type.GetElementType();
                if (!elementType.IsSubclassOf(typeof(SHAREDDATA)))
                    throw new ArgumentException("stream_info_not_sd_array");
                else if (elementType.IsAbstract)
                    throw new ArgumentException("stream_info_sd_array_abstract");

                SHAREDDATA sd = Activator.CreateInstance(elementType) as SHAREDDATA;
                if ((!sd.IsVersioned && _version != null) || (sd.IsVersioned && _version == null))
                    throw new ArgumentException("stream_info_not_sd_array_is_versioned");

                version = _version;
                length = _length;

                Type = _type;
                Sender = _sender;
                Receiver = _receiver;
            }

            //SHAREDDATA（の派生クラス）の配列以外の配列またはSHAREDDATA（の派生クラス）専用
            public StreamInfomation(Type _type, int? _lengthOrVersion, Func<object> _sender, Action<object> _receiver)
            {
                if (_type.IsArray)
                {
                    Type elementType = _type.GetElementType();
                    if (elementType.IsSubclassOf(typeof(SHAREDDATA)))
                        throw new ArgumentException("stream_info_sd_array");
                    else if (elementType.IsAbstract)
                        throw new ArgumentException("stream_info_array_abstract");
                    else
                        length = _lengthOrVersion;
                }
                else if (_type.IsSubclassOf(typeof(SHAREDDATA)))
                {
                    if (_type.IsAbstract)
                        throw new ArgumentException("stream_info_sd_abstract");

                    SHAREDDATA sd = Activator.CreateInstance(_type) as SHAREDDATA;
                    if ((!sd.IsVersioned && _lengthOrVersion != null) || (sd.IsVersioned && _lengthOrVersion == null))
                        throw new ArgumentException("stream_info_sd_is_versioned");

                    version = _lengthOrVersion;
                }
                else
                    throw new ArgumentException("stream_info_not_array_sd");

                Type = _type;
                Sender = _sender;
                Receiver = _receiver;
            }

            public StreamInfomation(Type _type, Func<object> _sender, Action<object> _receiver)
            {
                if (_type.IsArray)
                    throw new ArgumentException("stream_info_array");
                else if (_type.IsSubclassOf(typeof(SHAREDDATA)))
                    throw new ArgumentException("stream_info_sd");
                else if (_type.IsAbstract)
                    throw new ArgumentException("stream_info_abstract");

                Type = _type;
                Sender = _sender;
                Receiver = _receiver;
            }

            public readonly Type Type;
            public readonly Func<object> Sender;
            public readonly Action<object> Receiver;

            private readonly int? length;
            public int? Length
            {
                get
                {
                    if (Type.IsArray)
                        return length;
                    else
                        throw new NotSupportedException("stream_info_length");
                }
            }

            private readonly int? version;
            public int Version
            {
                get
                {
                    SHAREDDATA sd;
                    if (Type.IsSubclassOf(typeof(SHAREDDATA)))
                        sd = Activator.CreateInstance(Type) as SHAREDDATA;
                    else if (Type.IsArray)
                    {
                        Type elementType = Type.GetElementType();
                        if (elementType.IsSubclassOf(typeof(SHAREDDATA)))
                            sd = Activator.CreateInstance(elementType) as SHAREDDATA;
                        else
                            throw new NotSupportedException("stream_info_version");
                    }
                    else
                        throw new NotSupportedException("stream_info_version");

                    if (!sd.IsVersioned)
                        throw new NotSupportedException("stream_info_version");
                    else
                        return (int)version;
                }
            }
        }

        protected abstract Func<ReaderWriter, IEnumerable<T>> StreamInfo { get; }

        protected void Write(STREAMWRITER writer, StreamInfomation si)
        {
            Action<Type, object> _Write = (type, o) =>
            {
                if (type == typeof(bool))
                    writer.WriteBool((bool)o);
                else if (type == typeof(int))
                    writer.WriteInt((int)o);
                else if (type == typeof(uint))
                    writer.WriteUint((uint)o);
                else if (type == typeof(float))
                    writer.WriteFloat((float)o);
                else if (type == typeof(long))
                    writer.WriteLong((long)o);
                else if (type == typeof(double))
                    writer.WriteDouble((double)o);
                else if (type == typeof(DateTime))
                    writer.WriteDateTime((DateTime)o);
                else if (type == typeof(string))
                    writer.WriteString((string)o);
                else if (type.IsSubclassOf(typeof(SHAREDDATA)))
                {
                    SHAREDDATA sd = o as SHAREDDATA;

                    writer.WriteSHAREDDATA(sd, sd.IsVersioned ? (int?)si.Version : null);
                }
                else
                    throw new NotSupportedException("sd_write_not_supported");
            };

            object obj = si.Sender();
            if (obj.GetType() != si.Type)
                throw new InvalidDataException("sd_writer_type_mismatch");

            if (si.Type == typeof(byte[]))
                writer.WriteBytes((byte[])obj, si.Length);
            else if (si.Type.IsArray)
            {
                object[] objs = obj as object[];
                Type elementType = si.Type.GetElementType();

                if (si.Length == null)
                    writer.WriteInt(objs.Length);
                foreach (var innerObj in obj as object[])
                    _Write(elementType, innerObj);
            }
            else
                _Write(si.Type, obj);
        }

        protected void Read(STREAMREADER reader, StreamInfomation si)
        {
            Func<Type, object> _Read = (type) =>
            {
                if (type == typeof(bool))
                    return reader.ReadBool();
                else if (type == typeof(int))
                    return reader.ReadInt();
                else if (type == typeof(uint))
                    return reader.ReadUint();
                else if (type == typeof(float))
                    return reader.ReadFloat();
                else if (type == typeof(long))
                    return reader.ReadLong();
                else if (type == typeof(double))
                    return reader.ReadDouble();
                else if (type == typeof(DateTime))
                    return reader.ReadDateTime();
                else if (type == typeof(string))
                    return reader.ReadString();
                else if (type.IsSubclassOf(typeof(SHAREDDATA)))
                {
                    SHAREDDATA sd = Activator.CreateInstance(type) as SHAREDDATA;

                    return reader.ReadSHAREDDATA(type, sd.IsVersioned ? (int?)si.Version : null);
                }
                else
                    throw new NotSupportedException("sd_read_not_supported");
            };

            if (si.Type == typeof(byte[]))
                si.Receiver(reader.ReadBytes(si.Length));
            else if (si.Type.IsArray)
            {
                int length = si.Length == null ? reader.ReadInt() : (int)si.Length;

                Type elementType = si.Type.GetElementType();
                object[] os = Array.CreateInstance(elementType, length) as object[];
                for (int i = 0; i < os.Length; i++)
                    os[i] = _Read(elementType);

                si.Receiver(os);
            }
            else
                si.Receiver(_Read(si.Type));
        }
    }
    public abstract class COMMUNICATIONPROTOCOL : STREAMDATA<COMMUNICATIONPROTOCOL.ProtocolInfomation>
    {
        public enum ClientOrServer { client, server }

        public class CommunicationApparatusWriter : STREAMWRITER
        {
            private readonly IChannel ca;

            public CommunicationApparatusWriter(IChannel _ca)
            {
                ca = _ca;
            }

            public override void WriteBytes(byte[] data, int? length)
            {
                ca.WriteBytes(data);
            }

            public override void WriteBool(bool data)
            {
                ca.WriteBytes(BitConverter.GetBytes(data));
            }

            public override void WriteInt(int data)
            {
                ca.WriteBytes(BitConverter.GetBytes(data));
            }

            public override void WriteUint(uint data)
            {
                ca.WriteBytes(BitConverter.GetBytes(data));
            }

            public override void WriteFloat(float data)
            {
                ca.WriteBytes(BitConverter.GetBytes(data));
            }

            public override void WriteLong(long data)
            {
                ca.WriteBytes(BitConverter.GetBytes(data));
            }

            public override void WriteDouble(double data)
            {
                ca.WriteBytes(BitConverter.GetBytes(data));
            }

            public override void WriteDateTime(DateTime data)
            {
                ca.WriteBytes(BitConverter.GetBytes((data).ToBinary()));
            }

            public override void WriteString(string data)
            {
                ca.WriteBytes(Encoding.UTF8.GetBytes(data));
            }

            public override void WriteSHAREDDATA(SHAREDDATA sd, int? version)
            {
                if (sd.IsVersioned)
                {
                    if (version == null)
                        throw new ArgumentException("write_sd_version_null");

                    sd.Version = (int)version;
                }

                ca.WriteBytes(sd.ToBinary());
            }
        }

        public class CommunicationApparatusReader : STREAMREADER
        {
            private readonly IChannel ca;

            public CommunicationApparatusReader(IChannel _ca)
            {
                ca = _ca;
            }

            public override byte[] ReadBytes(int? length)
            {
                return ca.ReadBytes();
            }

            public override bool ReadBool()
            {
                return BitConverter.ToBoolean(ca.ReadBytes(), 0);
            }

            public override int ReadInt()
            {
                return BitConverter.ToInt32(ca.ReadBytes(), 0);
            }

            public override uint ReadUint()
            {
                return BitConverter.ToUInt32(ca.ReadBytes(), 0);
            }

            public override float ReadFloat()
            {
                return BitConverter.ToSingle(ca.ReadBytes(), 0);
            }

            public override long ReadLong()
            {
                return BitConverter.ToInt64(ca.ReadBytes(), 0);
            }

            public override double ReadDouble()
            {
                return BitConverter.ToDouble(ca.ReadBytes(), 0);
            }

            public override DateTime ReadDateTime()
            {
                return DateTime.FromBinary(BitConverter.ToInt64(ca.ReadBytes(), 0));
            }

            public override string ReadString()
            {
                return Encoding.UTF8.GetString(ca.ReadBytes());
            }

            public override SHAREDDATA ReadSHAREDDATA(Type type, int? version)
            {
                SHAREDDATA sd = Activator.CreateInstance(type) as SHAREDDATA;
                if (sd.IsVersioned)
                {
                    if (version == null)
                        throw new ArgumentException("read_sd_version_null");

                    sd.Version = (int)version;
                }
                sd.FromBinary(ca.ReadBytes());

                return sd;
            }
        }

        public class ProtocolInfomation : StreamInfomation
        {
            public ProtocolInfomation(Type _type, int? _version, int? _length, Func<object> _sender, Action<object> _receiver, Direction _forClient)
                : base(_type, _version, _length, _sender, _receiver)
            {
                ForClient = _forClient;
            }

            public ProtocolInfomation(Type _type, int? _lengthOrVersion, Func<object> _sender, Action<object> _receiver, Direction _forClient)
                : base(_type, _lengthOrVersion, _sender, _receiver)
            {
                ForClient = _forClient;
            }

            public ProtocolInfomation(Type _type, Func<object> _sender, Action<object> _receiver, Direction _forClient)
                : base(_type, _sender, _receiver)
            {
                ForClient = _forClient;
            }

            public enum Direction { write, read }

            public readonly Direction ForClient;
            public Direction ForServer
            {
                get
                {
                    if (ForClient == Direction.read)
                        return Direction.write;
                    else
                        return Direction.read;
                }
            }
        }

        protected void Communicate(IChannel ca, ClientOrServer clientOrServer)
        {
            CommunicationApparatusWriter writer = new CommunicationApparatusWriter(ca);
            CommunicationApparatusReader reader = new CommunicationApparatusReader(ca);

            foreach (var pi in StreamInfo(new ReaderWriter(writer, reader, ReaderWriter.Mode.neither)))
                if ((clientOrServer == ClientOrServer.client && pi.ForClient == ProtocolInfomation.Direction.read) || (clientOrServer == ClientOrServer.server && pi.ForServer == ProtocolInfomation.Direction.read))
                    Read(reader, pi);
                else
                    Write(writer, pi);
        }
    }
    public abstract class SHAREDDATA : STREAMDATA<SHAREDDATA.MainDataInfomation>
    {
        //<未実装>圧縮機能
        //<未実装>ジャグ配列に対応
        //<未修正>ReaderのSHAREDDATAの読み込みが署名機能に対応していない
        //　　　　署名機能を使用する場合長さが可変になるので長さを保存する必要がある
        //<未修正>Lengthでversionなどの長さを加算しているのに、Readでも加算しているような

        private int? version;
        public int Version
        {
            get
            {
                if (!IsVersioned)
                    throw new NotSupportedException("sd_version");
                else
                    return (int)version;
            }
            set
            {
                if (!IsVersioned)
                    throw new NotSupportedException("sd_version");
                else
                    version = value;
            }
        }

        public SHAREDDATA(int? _version)
        {
            if ((IsVersioned && _version == null) || (!IsVersioned && _version != null))
                throw new ArgumentException("sd_is_versioned_and_version");

            version = _version;
        }

        public SHAREDDATA() : this(null) { }

        public class MyStreamWriter : STREAMWRITER
        {
            private readonly Stream stream;

            public MyStreamWriter(Stream _stream)
            {
                stream = _stream;
            }

            public override void WriteBytes(byte[] data, int? length)
            {
                if (length == null)
                    stream.Write(BitConverter.GetBytes(data.Length), 0, 4);
                stream.Write(data, 0, data.Length);
            }

            public override void WriteBool(bool data)
            {
                stream.Write(BitConverter.GetBytes(data), 0, 1);
            }

            public override void WriteInt(int data)
            {
                stream.Write(BitConverter.GetBytes(data), 0, 4);
            }

            public override void WriteUint(uint data)
            {
                stream.Write(BitConverter.GetBytes(data), 0, 4);
            }

            public override void WriteFloat(float data)
            {
                stream.Write(BitConverter.GetBytes(data), 0, 4);
            }

            public override void WriteLong(long data)
            {
                stream.Write(BitConverter.GetBytes(data), 0, 8);
            }

            public override void WriteDouble(double data)
            {
                stream.Write(BitConverter.GetBytes(data), 0, 8);
            }

            public override void WriteDateTime(DateTime data)
            {
                stream.Write(BitConverter.GetBytes((data).ToBinary()), 0, 8);
            }

            public override void WriteString(string data)
            {
                byte[] bytes = Encoding.UTF8.GetBytes(data);
                stream.Write(BitConverter.GetBytes(bytes.Length), 0, 4);
                stream.Write(bytes, 0, bytes.Length);
            }

            public override void WriteSHAREDDATA(SHAREDDATA data, int? version)
            {
                if (data.IsVersioned)
                {
                    if (version == null)
                        throw new ArgumentException("write_sd_version_null");

                    data.Version = (int)version;
                }

                byte[] bytes = data.ToBinary();
                if (data.Length == null)
                    stream.Write(BitConverter.GetBytes(bytes.Length), 0, 4);
                stream.Write(bytes, 0, bytes.Length);
            }
        }

        public class MyStreamReader : STREAMREADER
        {
            private readonly Stream stream;

            public MyStreamReader(Stream _stream)
            {
                stream = _stream;
            }

            public override byte[] ReadBytes(int? length)
            {
                if (length == null)
                {
                    byte[] lengthBytes = new byte[4];
                    stream.Read(lengthBytes, 0, 4);
                    length = BitConverter.ToInt32(lengthBytes, 0);
                }

                byte[] bytes = new byte[(int)length];
                stream.Read(bytes, 0, (int)length);
                return bytes;
            }

            public override bool ReadBool()
            {
                byte[] bytes = new byte[1];
                stream.Read(bytes, 0, 1);
                return BitConverter.ToBoolean(bytes, 0);
            }

            public override int ReadInt()
            {
                byte[] bytes = new byte[4];
                stream.Read(bytes, 0, 4);
                return BitConverter.ToInt32(bytes, 0);
            }

            public override uint ReadUint()
            {
                byte[] bytes = new byte[4];
                stream.Read(bytes, 0, 4);
                return BitConverter.ToUInt32(bytes, 0);
            }

            public override float ReadFloat()
            {
                byte[] bytes = new byte[4];
                stream.Read(bytes, 0, 4);
                return BitConverter.ToSingle(bytes, 0);
            }

            public override long ReadLong()
            {
                byte[] bytes = new byte[8];
                stream.Read(bytes, 0, 8);
                return BitConverter.ToInt64(bytes, 0);
            }

            public override double ReadDouble()
            {
                byte[] bytes = new byte[8];
                stream.Read(bytes, 0, 8);
                return BitConverter.ToDouble(bytes, 0);
            }

            public override DateTime ReadDateTime()
            {
                byte[] bytes = new byte[8];
                stream.Read(bytes, 0, 8);
                return DateTime.FromBinary(BitConverter.ToInt64(bytes, 0));
            }

            public override string ReadString()
            {
                byte[] lengthBytes = new byte[4];
                stream.Read(lengthBytes, 0, 4);
                int length = BitConverter.ToInt32(lengthBytes, 0);

                byte[] bytes = new byte[length];
                stream.Read(bytes, 0, length);
                return Encoding.UTF8.GetString(bytes);
            }

            public override SHAREDDATA ReadSHAREDDATA(Type type, int? version)
            {
                SHAREDDATA sd = Activator.CreateInstance(type) as SHAREDDATA;
                if (sd.IsVersioned)
                {
                    if (version == null)
                        throw new ArgumentException("read_sd_version_null");

                    sd.Version = (int)version;
                }

                int length;
                if (sd.Length == null)
                {
                    byte[] lengthBytes = new byte[4];
                    stream.Read(lengthBytes, 0, 4);
                    length = BitConverter.ToInt32(lengthBytes, 0);
                }
                else
                {
                    length = (int)sd.Length;
                    if (sd.IsVersioned)
                        length += 4;
                    if (sd.IsCorruptionChecked)
                        length += 4;
                }

                byte[] bytes = new byte[length];
                stream.Read(bytes, 0, length);

                sd.FromBinary(bytes);

                return sd;
            }
        }

        public class MainDataInfomation : StreamInfomation
        {
            public MainDataInfomation(Type _type, int? _version, int? _length, Func<object> _getter, Action<object> _setter) : base(_type, _version, _length, _getter, _setter) { }

            public MainDataInfomation(Type _type, int? _lengthOrVersion, Func<object> _getter, Action<object> _setter) : base(_type, _lengthOrVersion, _getter, _setter) { }

            public MainDataInfomation(Type _type, Func<object> _getter, Action<object> _setter) : base(_type, _getter, _setter) { }

            public Func<object> Getter
            {
                get { return Sender; }
            }

            public Action<object> Setter
            {
                get { return Receiver; }
            }
        }

        public virtual bool IsVersioned
        {
            get { return false; }
        }

        public virtual bool IsCorruptionChecked
        {
            get { return false; }
        }

        public virtual bool IsSigned
        {
            get { return false; }
        }

        public virtual byte[] PublicKey
        {
            get { throw new NotSupportedException("sd_pubkey"); }
            protected set { throw new NotSupportedException("sd_pubkey_set"); }
        }

        public virtual byte[] PrivateKey
        {
            get { throw new NotSupportedException("sd_privatekey"); }
        }

        public virtual bool IsSignatureChecked
        {
            get { return false; }
        }

        private byte[] signature;
        public bool IsValidSignature
        {
            get
            {
                if (!IsSigned)
                    throw new NotSupportedException("sd_is_valid_sig");
                if (signature == null)
                    throw new InvalidOperationException("sd_signature");

                return VerifySignature();
            }
        }

        protected virtual Action<byte[]> ReservedRead
        {
            get { throw new NotSupportedException("sd_reserved_write"); }
        }

        protected virtual Func<byte[]> ReservedWrite
        {
            get { return () => new byte[] { }; }
        }

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
                        SHAREDDATA sd = Activator.CreateInstance(type) as SHAREDDATA;
                        if (sd.IsVersioned)
                            sd.version = mdi.Version;

                        if (sd.Length == null)
                            return null;
                        else
                        {
                            int innerLength = 0;

                            if (sd.IsVersioned)
                                innerLength += 4;
                            if (sd.IsCorruptionChecked)
                                innerLength += 4;

                            return innerLength + (int)sd.Length;
                        }
                    }
                    else
                        throw new NotSupportedException("sd_length_not_supported");
                };

                int length = 0;
                try
                {
                    foreach (var mdi in StreamInfo(new ReaderWriter(null, null, ReaderWriter.Mode.neither)))
                        if (mdi.Type.IsArray)
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
                        else
                        {
                            int? innerLength = _GetLength(mdi.Type, mdi);
                            if (innerLength == null)
                                return null;
                            else
                                length += (int)innerLength;
                        }
                }
                catch (ReaderWriter.CantReadOrWriteException)
                {
                    return null;
                }
                return length;
            }
        }

        private byte[] ToBinaryMainData()
        {
            using (MemoryStream ms = new MemoryStream())
            {
                MyStreamWriter writer = new MyStreamWriter(ms);

                foreach (var mdi in StreamInfo(new ReaderWriter(writer, new MyStreamReader(ms), ReaderWriter.Mode.write)))
                    Write(writer, mdi);

                byte[] reservedBytes = ReservedWrite();
                ms.Write(BitConverter.GetBytes(reservedBytes.Length), 0, 4);
                ms.Write(reservedBytes, 0, reservedBytes.Length);

                return ms.ToArray();
            }
        }

        public byte[] ToBinary()
        {
            byte[] mainDataBytes = ToBinaryMainData();

            using (MemoryStream ms = new MemoryStream())
            {
                if (IsVersioned)
                    ms.Write(BitConverter.GetBytes((int)version), 0, 4);
                //破損検査のためのデータ（主データのハッシュ値の先頭4バイト）
                if (IsCorruptionChecked)
                    ms.Write(mainDataBytes.ComputeSha256(), 0, 4);
                if (IsSigned)
                {
                    ms.Write(BitConverter.GetBytes(PublicKey.Length), 0, 4);
                    ms.Write(PublicKey, 0, PublicKey.Length);

                    //一応主データに加えて公開鍵も結合したものに対して署名することにする
                    //公開鍵を結合しないと、後で別の鍵ペアで同一データに署名することができる
                    //だからといって、別人が署名した同一データができるだけで、
                    //本人が作成していないデータに対して別人が本人として署名することはできないが
                    byte[] signBytes = PublicKey.Combine(mainDataBytes);

                    using (ECDsaCng dsa = new ECDsaCng(CngKey.Import(PrivateKey, CngKeyBlobFormat.EccPrivateBlob)))
                    {
                        dsa.HashAlgorithm = CngAlgorithm.Sha256;

                        signature = dsa.SignData(signBytes);

                        //将来的にハッシュアルゴリズムが変更される可能性もあるので、
                        //署名は可変長ということにする
                        ms.Write(BitConverter.GetBytes(signature.Length), 0, 4);
                        ms.Write(signature, 0, signature.Length);
                    }
                }
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

                if (IsSigned)
                {
                    byte[] publicKeyLengthBytes = new byte[4];
                    ms.Read(publicKeyLengthBytes, 0, 4);
                    int publicKeyLength = BitConverter.ToInt32(publicKeyLengthBytes, 0);

                    byte[] publicKey = new byte[publicKeyLength];
                    ms.Read(publicKey, 0, publicKey.Length);
                    PublicKey = publicKey;

                    byte[] signatureLengthBytes = new byte[4];
                    ms.Read(signatureLengthBytes, 0, 4);
                    int signatureLength = BitConverter.ToInt32(signatureLengthBytes, 0);

                    signature = new byte[signatureLength];
                    ms.Read(signature, 0, signature.Length);
                }

                int length = (int)(ms.Length - ms.Position);
                mainDataBytes = new byte[length];
                ms.Read(mainDataBytes, 0, length);

                if (IsCorruptionChecked && check != BitConverter.ToInt32(mainDataBytes.ComputeSha256(), 0))
                    throw new InvalidDataException("from_binary_check");
                if (IsSigned && IsSignatureChecked && !VerifySignature())
                    throw new InvalidDataException("from_binary_signature");
            }
            using (MemoryStream ms = new MemoryStream(mainDataBytes))
            {
                MyStreamReader reader = new MyStreamReader(ms);

                foreach (var mdi in StreamInfo(new ReaderWriter(new MyStreamWriter(ms), reader, ReaderWriter.Mode.read)))
                    Read(reader, mdi);

                byte[] reservedLengthBytes = new byte[4];
                ms.Read(reservedLengthBytes, 0, 4);
                int reservedLength = BitConverter.ToInt32(reservedLengthBytes, 0);

                byte[] reservedBytes = new byte[reservedLength];
                ms.Read(reservedBytes, 0, reservedBytes.Length);

                if (reservedBytes.Length != 0)
                    ReservedRead(reservedBytes);
            }
        }

        public static T FromBinary<T>(byte[] binary) where T : SHAREDDATA
        {
            T sd = Activator.CreateInstance(typeof(T)) as T;
            sd.FromBinary(binary);
            return sd;
        }

        private bool VerifySignature()
        {
            using (ECDsaCng dsa = new ECDsaCng(CngKey.Import(PublicKey, CngKeyBlobFormat.EccPublicBlob)))
            {
                dsa.HashAlgorithm = CngAlgorithm.Sha256;

                return dsa.VerifyData(ToBinaryMainData(), signature);
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
                        throw new NotSupportedException("to_xml_not_supported");
                };

                object o = mdi.Getter();

                if (mdi.Type.IsArray)
                {
                    Type elementType = mdi.Type.GetElementType();

                    XElement xElementArray = new XElement(mdi.XmlName + "s");
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

    #region main

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
                        throw new InvalidOperationException("task_not_found");
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
                        return "過誤".Multilanguage(79);
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

            public event EventHandler<LogData> LogAdded = delegate { };
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

                LogAdded(this, log);

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

            private bool isSave = true;
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
        private static Dictionary<string, Func<string[], string>> logMessages;
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

        public static string GetLogMessage(string rawMessage, string[] arguments)
        {
            return logMessages.GetValue(rawMessage, (arg) => rawMessage)(arguments);
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

            Logger logger = null;
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
                { typeof(AccountHolderDatabase), LogData.LogGround.signData},
                { typeof(InboundChannelsBase), LogData.LogGround.networkBase},
                { typeof(OutboundChannelBase), LogData.LogGround.networkBase},
                { typeof(SocketChannel), LogData.LogGround.networkBase},
                { typeof(Cremlia), LogData.LogGround.cremlia},
            };

            logMessages = new Dictionary<string, Func<string[], string>>() {
                {"exist_same_name_account_holder", (args) => "同名の口座名義人が存在します。".Multilanguage(93)},
                {"outbound_chennel", (args) => "エラーが発生しました。".Multilanguage(94)},
                {"inbound_channel", (args) => "エラーが発生しました。".Multilanguage(95)},
                {"inbound_channels", (args) => "エラーが発生しました。".Multilanguage(113)},
                {"socket_channel_write", (args) => "エラーが発生しました。".Multilanguage(114)},
                {"socket_channel_read", (args) => "エラーが発生しました。".Multilanguage(115)},
                {"ric", (args) => "エラーが発生しました。".Multilanguage(121)},
                {"roc", (args) => "エラーが発生しました。".Multilanguage(122)},
                {"inbound_session", (args) => "エラーが発生しました。".Multilanguage(123)},
                {"outbound_session", (args) => "エラーが発生しました。".Multilanguage(124)},
                {"diffuse", (args) => "エラーが発生しました。".Multilanguage(125)},
                {"keep_conn", (args) => "エラーが発生しました。".Multilanguage(126)},
                {"task", (args) => "エラーが発生しました。".Multilanguage(96)},
                {"task_aborted", (args) => "作業が強制終了されました。".Multilanguage(97)},
                {"all_tasks_aborted", (args) => "全ての作業が強制終了されました。".Multilanguage(98)},
                {"upnp_not_found", (args) => "UPnPによるグローバルIPアドレスの取得に失敗しました。サーバは起動されませんでした。".Multilanguage(99)},
                {"port0", (args) => "ポート0です。サーバは起動されませんでした。".Multilanguage(100)},
                {"rsa_key_cant_create", (args) => "RSA鍵の生成に失敗しました。サーバは起動されませんでした。".Multilanguage(101)},
                {"rsa_key_create", (args) => "RSA鍵の生成に成功しました。".Multilanguage(102)},
                {"upnp_ipaddress", (args) => string.Format("グローバルIPアドレスを取得しました：{0}".Multilanguage(103), args[0])},
                {"server_started", (args) => string.Format("サーバのリッスンを開始しました：{0}:{1}".Multilanguage(104), args[0], args[1])},
                {"server_ended", (args) => string.Format("サーバのリッスンを終了しました：{0}:{1}".Multilanguage(105), args[0], args[1])},
                {"server_restart", (args) => string.Format("ポートが変更されました。現在起動しているサーバを停止し、新たなサーバを起動します：{0}:{1}".Multilanguage(106), args[0], args[1])},
                {"aite_wrong_node_info", (args) => string.Format("ノードが申告したIPアドレスと実際のIPアドレスが異なります：{0}:{1}".Multilanguage(107), args[0], args[1])},
                {"aite_wrong_network", (args) => string.Format("ノードが所属しているネットワークが異なります：{0}:{1}".Multilanguage(108), args[0], args[1])},
                {"aite_already_connected", (args) => string.Format("既に接続しているノードから再び接続が要求されました：{0}:{1}".Multilanguage(109), args[0], args[1])},
                {"wrong_network", (args) => string.Format("別のネットワークに所属しているノードに接続しました：{0}:{1}".Multilanguage(110), args[0], args[1])},
                {"already_connected", (args) => string.Format("既に接続しているノードに接続しました：{0}:{1}".Multilanguage(111), args[0], args[1])}, 
                { "keep_conn_completed", (args) => "常時接続が確立しました。".Multilanguage(112)},
                { "find_table_already_added", (args) => string.Format(string.Join(Environment.NewLine, "DHTの検索リスト項目は既に登録されています。".Multilanguage(116), "距離：{0}".Multilanguage(117), "ノード1：{1}".Multilanguage(118), "ノード2：{2}".Multilanguage(119)), args[0], args[1], args[3])}, 
                {"find_nodes", (args) => string.Format("{0}個の近接ノードを発見しました。".Multilanguage(120), args[0])},
                {"my_node_info", (args) => string.Format("自分自身のノード情報です。".Multilanguage(127), args[0])},
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
                        catch (Exception)
                        { }

                        string videoCard = string.Empty;
                        try
                        {
                            ManagementClass mc = new ManagementClass("Win32_VideoController");
                            ManagementObjectCollection moc = mc.GetInstances();
                            foreach (ManagementObject mo in moc)
                                if (mo["Name"] != null)
                                    videoCard = mo["Name"].ToString();
                        }
                        catch (Exception)
                        { }

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
                        catch (Exception)
                        { }

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
                throw new ApplicationException("already_starting");
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
                            throw new ApplicationException("ie_not_existing");
                    }
                    int.TryParse(v.ToString().Split('.')[0], out ieVersion);
                }
                if (ieVersion < 10)
                    throw new ApplicationException("ie_too_old");

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

                Test test = new Test(logger, _OnException);

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
                    throw new ApplicationException("already_starting");
            }

            mutex.Close();
        }
    }

    #endregion

    public class Test
    {
        private readonly Program.Logger logger;
        private readonly Action<Exception, Program.ExceptionKind> OnException;
        private readonly bool isTest = true;

        public Test(Program.Logger _logger, Action<Exception, Program.ExceptionKind> _OnException)
        {
            if (!isTest)
                return;

            logger = _logger;
            OnException = _OnException;

            CreaNetworkLocalTest();

            Environment.Exit(0);
        }

        private void CreaNetworkLocalTest()
        {
            CreaNetworkLocalTest cnlt = new CreaNetworkLocalTest(logger, OnException);
        }
    }
}