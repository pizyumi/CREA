//がをがを～！
//作譜者：@pizyumi

using HashLib;
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
using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
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

    public class CachedData<T>
    {
        public CachedData(Func<T> _generator)
        {
            generator = _generator;
            isCached = false;
            isModified = false;
        }

        private Func<T> generator;

        public bool isCached { get; private set; }

        private bool isModified;
        public bool IsModified
        {
            get { return isModified; }
            set
            {
                if (!value)
                    throw new InvalidOperationException("is_modified_cant_set_false");

                isModified = value;
            }
        }

        private T cache;
        public T Data
        {
            get
            {
                if (isModified || !isCached)
                {
                    cache = generator();
                    isModified = false;
                    isCached = true;
                }
                return cache;
            }
        }
    }

    public class CirculatedReadCache<KeyType, T>
    {
        public CirculatedReadCache(int _cachesNum, Func<KeyType, T> _loader, Action<KeyType, T> _saver)
        {
            if (_cachesNum < 1)
                throw new ArgumentException();

            cachesNum = _cachesNum;
            loader = _loader;
            saver = _saver;
            caches = new T[cachesNum];
            keys = new KeyType[cachesNum];
            currentIndex = new CirculatedInteger(cachesNum);
        }

        private readonly int cachesNum;
        private readonly Func<KeyType, T> loader;
        private readonly Action<KeyType, T> saver;
        private readonly T[] caches;
        private readonly KeyType[] keys;
        private readonly CirculatedInteger currentIndex;

        public T Get(KeyType key)
        {
            foreach (int index in currentIndex.GetCircleBackward())
                if (caches[index] == null)
                    break;
                else if (keys[index].Equals(key))
                    return caches[index];

            currentIndex.Next();

            int index2 = currentIndex.value;

            if (caches[index2] != null)
                saver(keys[index2], caches[index2]);

            keys[index2] = key;
            return caches[index2] = loader(key);
        }

        public void SaveAll()
        {
            for (int i = 0; i < cachesNum; i++)
                if (caches[i] != null)
                    saver(keys[i], caches[i]);
        }
    }

    public class CirculatedWriteCache<KeyType, T>
    {
        public CirculatedWriteCache(int _cachesNum, int _saveNum, Action<KeyType[], T[]> _saver)
        {
            if (_cachesNum < 1)
                throw new ArgumentException();
            if (_saveNum < 1 || _saveNum > _cachesNum)
                throw new ArgumentException();

            cachesNum = _cachesNum;
            saveNum = _saveNum;
            saver = _saver;
            caches = new T[cachesNum];
            keys = new KeyType[cachesNum];
            currentIndex = new CirculatedInteger(cachesNum);
        }

        private readonly int cachesNum;
        private readonly int saveNum;
        private readonly Action<KeyType[], T[]> saver;
        private readonly T[] caches;
        private readonly KeyType[] keys;
        private readonly CirculatedInteger currentIndex;

        public T Get(KeyType key)
        {
            foreach (int index in currentIndex.GetCircleBackward())
                if (caches[index] != null && keys[index].Equals(key))
                    return caches[index];
            return default(T);
        }

        public void Set(KeyType key, T data)
        {
            currentIndex.Next();

            int index = currentIndex.value;

            if (caches[index] != null)
                Save(currentIndex.GetCircleForward().Take(saveNum));

            keys[index] = key;
            caches[index] = data;
        }

        public void Delete(KeyType key)
        {
            for (int i = 0; i < cachesNum; i++)
                if (caches[i] != null && keys[i].Equals(key))
                    caches[i] = default(T);
        }

        public void Delete(Func<KeyType, bool> predicate)
        {
            for (int i = 0; i < cachesNum; i++)
                if (caches[i] != null && predicate(keys[i]))
                    caches[i] = default(T);
        }

        public void SaveAll()
        {
            Save(Enumerable.Range(0, cachesNum));
        }

        private void Save(IEnumerable<int> indexes)
        {
            List<KeyType> keysNotNull = new List<KeyType>();
            List<T> dataNotNull = new List<T>();

            foreach (int index in indexes)
                if (caches[index] != null)
                {
                    keysNotNull.Add(keys[index]);
                    dataNotNull.Add(caches[index]);
                }

            saver(keysNotNull.ToArray(), dataNotNull.ToArray());
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

    public class CirculatedInteger
    {
        public CirculatedInteger(int _cycle) : this(0, _cycle) { }

        public CirculatedInteger(int _value, int _cycle)
        {
            if (_value >= _cycle)
                throw new ArgumentException();

            value = _value;
            cycle = _cycle;
        }

        private readonly int cycle;

        public int value { get; private set; }

        public void Next()
        {
            value++;
            if (value == cycle)
                value = 0;
        }

        public void Previous()
        {
            value--;
            if (value == -1)
                value = cycle - 1;
        }

        public int GetNext()
        {
            int temp = value + 1;
            if (temp == cycle)
                temp = 0;
            return temp;
        }

        public int GetPrevious()
        {
            int temp = value - 1;
            if (temp == -1)
                temp = cycle - 1;
            return temp;
        }

        public int GetForward(int delta)
        {
            return (value + delta) % cycle;
        }

        public int GetBackward(int delta)
        {
            return ((value - delta) % cycle).Pipe((i) => i < 0 ? i + cycle : i);
        }

        public IEnumerable<int> GetCircleForward()
        {
            for (int i = value; i < cycle; i++)
                yield return i;
            for (int i = 0; i < value; i++)
                yield return i;
        }

        public IEnumerable<int> GetCircleBackward()
        {
            for (int i = value; i >= 0; i--)
                yield return i;
            for (int i = cycle - 1; i > value; i--)
                yield return i;
        }

        public IEnumerable<int> GetCircleNext()
        {
            int next = GetNext();

            for (int i = next; i < cycle; i++)
                yield return i;
            for (int i = 0; i < next; i++)
                yield return i;
        }

        public IEnumerable<int> GetCirclePrevious()
        {
            int previous = GetPrevious();

            for (int i = previous; i >= 0; i--)
                yield return i;
            for (int i = cycle - 1; i > previous; i--)
                yield return i;
        }
    }

    #endregion

    #region 拡張メソッド

    public static class Extension
    {
        #region 一般

        //操作型を受け取ってそのまま返す（代用拡張：操作型）
        public static Action Lambda<T>(this T dummy, Action action)
        {
            return action;
        }

        //関数型を受け取ってそのまま返す（代用拡張：関数型）
        public static Func<U> Lambda<T, U>(this T dummy, Func<U> func)
        {
            return func;
        }

        public static void ForEach<T>(this IEnumerable<T> ienumerable, Action<T> action)
        {
            foreach (var i in ienumerable)
                action(i);
        }

        public static void ForEach<T>(this IEnumerable<T> ienumerable, Action<int, T> action)
        {
            int index = 0;
            foreach (var i in ienumerable)
                action(index, i);
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

        //UIスレッドで処理を同期的に実行する（代用拡張：操作型）
        public static void ExecuteInUIThread<T>(this T dummy, Action action)
        {
            ExecuteInUIThread(action);
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

        //UIスレッドで処理を同期的に実行する（代用拡張：関数型）
        public static T ExecuteInUIThread<T, U>(this U dummy, Func<T> action)
        {
            return ExecuteInUIThread(action);
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

        //UIスレッドで処理を非同期的に実行する（代用拡張：操作型）
        public static void BeginExecuteInUIThread<T>(this T dummy, Action action)
        {
            BeginExecuteInUIThread(action);
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

        //UIスレッドで処理を同期的に実行する（代用拡張：関数型）
        public static T BeginExecuteInUIThread<T, U>(this U dummy, Func<T> action)
        {
            return BeginExecuteInUIThread(action);
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

        public static string GetTagValue(this string tagName, string content)
        {
            return ("<" + tagName + ">").Pipe((tag) => content.Substring(content.IndexOf(tag, StringComparison.InvariantCultureIgnoreCase) + tag.Length)).Pipe((val) => ("</" + tagName + ">").Pipe((tag) => val.Substring(0, val.IndexOf(tag, StringComparison.InvariantCultureIgnoreCase))));
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

        //多倍長整数から16進文字列に変換する（拡張：多倍長整数型）
        public static string ToHexstring(this BigInteger bigInt)
        {
            return bigInt.ToByteArray().ToHexstring();
        }

        //16進文字列からバイト配列に変換する（拡張：文字列型）
        public static byte[] FromHexstringToBytes(this string str)
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

        //16進文字列から多倍長整数に変換する（拡張：多倍長整数型）
        public static BigInteger FromHexstringToBigInteger(this string str)
        {
            return new BigInteger(str.FromHexstringToBytes());
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

        public static bool False(this bool p, bool q) { return false; }
        public static bool True(this bool p, bool q) { return true; }
        public static bool P(this bool p, bool q) { return p; }
        public static bool NegP(this bool p, bool q) { return !p; }
        public static bool Q(this bool p, bool q) { return q; }
        public static bool NegQ(this bool p, bool q) { return !q; }
        public static bool And(this bool p, bool q) { return p && q; }
        public static bool Nand(this bool p, bool q) { return !(p && q); }
        public static bool Or(this bool p, bool q) { return p || q; }
        public static bool Nor(this bool p, bool q) { return !(p || q); }
        public static bool Imp(this bool p, bool q) { return !p ? true : q; }
        public static bool Nonimp(this bool p, bool q) { return !p ? false : !q; }
        public static bool Convimp(this bool p, bool q) { return p ? true : !q; }
        public static bool Convnonimp(this bool p, bool q) { return p ? false : q; }
        public static bool Xor(this bool p, bool q) { return p ^ q; }
        public static bool Iff(this bool p, bool q) { return p == q; }

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

        //2つのバイト配列の内容が等しいか判定する（拡張：バイト配列型）
        public static bool BytesEquals(this byte[] bytes1, byte[] bytes2)
        {
            if (bytes1.Length != bytes2.Length)
                return false;
            for (int i = 0; i < bytes1.Length; i++)
                if (bytes1[i] != bytes2[i])
                    return false;
            return true;
        }

        //2つのバイト配列の内容が等しいか判定する（拡張：バイト配列型）
        //2014/05/03 長さが異なる場合にどうするべきなのか良く分からない
        public static int BytesCompareTo(this byte[] bytes1, byte[] bytes2)
        {
            int returnValue = 0;
            for (int i = 0; i < bytes1.Length; i++)
                if ((returnValue = bytes1[i].CompareTo(bytes2[i])) != 0)
                    return returnValue;
            return returnValue;
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

        //イベントの前に処理を実行する（拡張：物件型）
        public static void ExecuteBeforeEvent<T>(this object obj, Action action, T parameter, EventHandler<T>[] ehs1, EventHandler[] ehs2)
        {
            action();
            foreach (var eh in ehs1)
                eh(obj, parameter);
            foreach (var eh in ehs2)
                eh(obj, EventArgs.Empty);
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

        //イベントの後に処理を実行する（拡張：物件型）
        public static void ExecuteAfterEvent<T>(this object obj, Action action, T parameter, EventHandler<T>[] ehs1, EventHandler[] ehs2)
        {
            foreach (var eh in ehs1)
                eh(obj, parameter);
            foreach (var eh in ehs2)
                eh(obj, EventArgs.Empty);
            action();
        }

        //関数を実行する（拡張：任意型）
        public static T Pipe<T>(this T self, Action operation)
        {
            operation();
            return self;
        }

        //自分自身を関数に渡してから返す（拡張：任意型）
        public static T Pipe<T>(this T self, Action<T> operation)
        {
            operation(self);
            return self;
        }

        //自分自身を関数に渡した結果を返す（拡張：任意型）
        public static S Pipe<T, S>(this T self, Func<T, S> operation)
        {
            return operation(self);
        }

        //自分自身を関数に渡した結果を永遠に返す（拡張：任意型）
        public static IEnumerable<S> PipeForever<T, S>(this T self, Func<T, S> operation)
        {
            while (true)
                yield return operation(self);
        }

        private static Random random = new Random();
        private static double[] cache = new double[] { };

        //指定された長さの無作為なバイト配列を返す
        public static byte[] RundomBytes(this int length)
        {
            byte[] bytes = new byte[length];
            random.NextBytes(bytes);
            return bytes;
        }

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
            return random.PipeForever(r => r.Next(i)).Distinct().Take(i).ToArray();
        }

        //常に同一の0から1までの無作為な浮動小数点数を返す
        public static IEnumerable<double> RandomDoublesCache()
        {
            for (int i = 0; ; i++)
            {
                if (i >= cache.Length)
                    cache = cache.Combine(random.PipeForever(r => r.NextDouble()).Take(100).ToArray());
                yield return cache[i];
            }
        }

        //常に同一の0からiまでの整数が1回ずつ無作為な順番で含まれる配列を作成する（拡張：整数型）
        public static int[] RandomNumsCache(this int i)
        {
            return RandomDoublesCache().Select((e) => (int)(e * i)).Distinct().Take(i).ToArray();
        }

        //配列の要素を指定された順番で並べ直した新たな配列を作成する（拡張：配列型）
        public static T[] ArrayRandom<T>(this T[] array, int[] order)
        {
            T[] newArray = new T[array.Length];
            for (int i = 0; i < array.Length; i++)
                newArray[i] = array[order[i]];
            return newArray;
        }

        //配列の要素を無作為な順番で並べ直した新たな配列を作成する（拡張：配列型）
        public static T[] ArrayRandom<T>(this T[] array)
        {
            return array.ArrayRandom(array.Length.RandomNums());
        }

        //配列の要素を常に同一の無作為な順番で並べ直した新たな配列を作成する（拡張：配列型）
        public static T[] ArrayRandomCache<T>(this T[] array)
        {
            return array.ArrayRandom(array.Length.RandomNumsCache());
        }

        public static string ComputeTrip(this byte[] bytes)
        {
            return "◆" + Convert.ToBase64String(bytes).Pipe((s) => s.Substring(s.Length - 12, 12));
        }

        private static HashAlgorithm haSha1 = null;
        private static HashAlgorithm haSha256 = null;
        private static HashAlgorithm haRipemd160 = null;
        private static IHash haBlake512 = null;
        private static IHash haBmw512 = null;
        private static IHash haGroestl512 = null;
        private static IHash haSkein512 = null;
        private static IHash haJh512 = null;
        private static IHash haKeccak512 = null;
        private static IHash haLuffa512 = null;
        private static IHash haCubehash512 = null;
        private static IHash haShavite512 = null;
        private static IHash haSimd512 = null;
        private static IHash haEcho512 = null;
        private static IHash haFugue512 = null;
        private static IHash haHamsi512 = null;
        private static IHash haShabal512 = null;

        //バイト配列のSHA1ハッシュ値を計算する（拡張：バイト配列型）
        public static byte[] ComputeSha1(this byte[] bytes)
        {
            if (haSha1 == null)
                haSha1 = new SHA1CryptoServiceProvider();

            return haSha1.ComputeHash(bytes);
        }

        //バイト配列のSHA256ハッシュ値を計算する（拡張：バイト配列型）
        public static byte[] ComputeSha256(this byte[] bytes)
        {
            if (haSha256 == null)
                haSha256 = HashAlgorithm.Create("SHA-256");

            return haSha256.ComputeHash(bytes);
        }

        public static byte[] ComputeSha256(this Stream stream)
        {
            if (haSha256 == null)
                haSha256 = HashAlgorithm.Create("SHA-256");

            return haSha256.ComputeHash(stream);
        }

        //バイト配列のRIPEMD160ハッシュ値を計算する（拡張：バイト配列型）
        public static byte[] ComputeRipemd160(this byte[] bytes)
        {
            if (haRipemd160 == null)
                haRipemd160 = HashAlgorithm.Create("RIPEMD-160");

            return haRipemd160.ComputeHash(bytes);
        }

        //バイト配列のBlake512ハッシュ値を計算する（拡張：バイト配列型）
        public static byte[] ComputeBlake512(this byte[] bytes)
        {
            if (haBlake512 == null)
                haBlake512 = HashFactory.Crypto.SHA3.CreateBlake512();

            return haBlake512.ComputeBytes(bytes).GetBytes();
        }

        //バイト配列のBmw512ハッシュ値を計算する（拡張：バイト配列型）
        public static byte[] ComputeBmw512(this byte[] bytes)
        {
            if (haBmw512 == null)
                haBmw512 = HashFactory.Crypto.SHA3.CreateBlueMidnightWish512();

            return haBmw512.ComputeBytes(bytes).GetBytes();
        }

        //バイト配列のGroestl512ハッシュ値を計算する（拡張：バイト配列型）
        public static byte[] ComputeGroestl512(this byte[] bytes)
        {
            if (haGroestl512 == null)
                haGroestl512 = HashFactory.Crypto.SHA3.CreateGroestl512();

            return haGroestl512.ComputeBytes(bytes).GetBytes();
        }

        //バイト配列のSkein512ハッシュ値を計算する（拡張：バイト配列型）
        public static byte[] ComputeSkein512(this byte[] bytes)
        {
            if (haSkein512 == null)
                haSkein512 = HashFactory.Crypto.SHA3.CreateSkein512();

            return haSkein512.ComputeBytes(bytes).GetBytes();
        }

        //バイト配列のJh512ハッシュ値を計算する（拡張：バイト配列型）
        public static byte[] ComputeJh512(this byte[] bytes)
        {
            if (haJh512 == null)
                haJh512 = HashFactory.Crypto.SHA3.CreateJH512();

            return haJh512.ComputeBytes(bytes).GetBytes();
        }

        //バイト配列のKeccak512ハッシュ値を計算する（拡張：バイト配列型）
        public static byte[] ComputeKeccak512(this byte[] bytes)
        {
            if (haKeccak512 == null)
                haKeccak512 = HashFactory.Crypto.SHA3.CreateKeccak512();

            return haKeccak512.ComputeBytes(bytes).GetBytes();
        }

        //バイト配列のLuffa512ハッシュ値を計算する（拡張：バイト配列型）
        public static byte[] ComputeLuffa512(this byte[] bytes)
        {
            if (haLuffa512 == null)
                haLuffa512 = HashFactory.Crypto.SHA3.CreateLuffa512();

            return haLuffa512.ComputeBytes(bytes).GetBytes();
        }

        //バイト配列のCubehash512ハッシュ値を計算する（拡張：バイト配列型）
        public static byte[] ComputeCubehash512(this byte[] bytes)
        {
            if (haCubehash512 == null)
                haCubehash512 = HashFactory.Crypto.SHA3.CreateCubeHash512();

            return haCubehash512.ComputeBytes(bytes).GetBytes();
        }

        //バイト配列のShavite512ハッシュ値を計算する（拡張：バイト配列型）
        public static byte[] ComputeShavite512(this byte[] bytes)
        {
            if (haShavite512 == null)
                haShavite512 = HashFactory.Crypto.SHA3.CreateSHAvite3_512();

            return haShavite512.ComputeBytes(bytes).GetBytes();
        }

        //バイト配列のSimd512ハッシュ値を計算する（拡張：バイト配列型）
        public static byte[] ComputeSimd512(this byte[] bytes)
        {
            if (haSimd512 == null)
                haSimd512 = HashFactory.Crypto.SHA3.CreateSIMD512();

            return haSimd512.ComputeBytes(bytes).GetBytes();
        }

        //バイト配列のEcho512ハッシュ値を計算する（拡張：バイト配列型）
        public static byte[] ComputeEcho512(this byte[] bytes)
        {
            if (haEcho512 == null)
                haEcho512 = HashFactory.Crypto.SHA3.CreateEcho512();

            return haEcho512.ComputeBytes(bytes).GetBytes();
        }

        //バイト配列のFugue512ハッシュ値を計算する（拡張：バイト配列型）
        public static byte[] ComputeFugue512(this byte[] bytes)
        {
            if (haFugue512 == null)
                haFugue512 = HashFactory.Crypto.SHA3.CreateFugue512();

            return haFugue512.ComputeBytes(bytes).GetBytes();
        }

        //バイト配列のHamsi512ハッシュ値を計算する（拡張：バイト配列型）
        public static byte[] ComputeHamsi512(this byte[] bytes)
        {
            if (haHamsi512 == null)
                haHamsi512 = HashFactory.Crypto.SHA3.CreateHamsi512();

            return haHamsi512.ComputeBytes(bytes).GetBytes();
        }

        //バイト配列のShabal512ハッシュ値を計算する（拡張：バイト配列型）
        public static byte[] ComputeShabal512(this byte[] bytes)
        {
            if (haShabal512 == null)
                haShabal512 = HashFactory.Crypto.SHA3.CreateShabal512();

            return haShabal512.ComputeBytes(bytes).GetBytes();
        }

        //バイト配列のECDSA署名（要約関数はSHA256）を計算する（拡張：バイト配列型）
        public static byte[] SignEcdsaSha256(this byte[] data, byte[] privKey)
        {
            using (ECDsaCng dsa = new ECDsaCng(CngKey.Import(privKey, CngKeyBlobFormat.EccPrivateBlob)))
            {
                dsa.HashAlgorithm = CngAlgorithm.Sha256;

                return dsa.SignData(data);
            }
        }

        //バイト配列のECDSA署名を検証する（拡張：バイト配列型）
        public static bool VerifyEcdsa(this byte[] data, byte[] signature, byte[] pubKey)
        {
            using (ECDsaCng dsa = new ECDsaCng(CngKey.Import(pubKey, CngKeyBlobFormat.EccPublicBlob)))
                return dsa.VerifyData(data, signature);
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

        //配列を分解する（拡張：任意の整数型）
        public static T[] Decompose<T>(this T[] self, int start, int length)
        {
            T[] decomposed = new T[length];
            Array.Copy(self, start, decomposed, 0, length);
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

        public static void ConsoleWriteLine<T>(this T dummy, string text)
        {
            dummy.Lambda(() => Console.WriteLine(text)).BeginExecuteInUIThread();
        }

        public class TaskInformation : INTERNALDATA
        {
            public readonly string Name;
            public readonly string Descption;
            public readonly Action Action;

            public TaskInformation(string _name, string _description, Action _action)
            {
                Action = _action;
                Name = _name;
                Descption = _description;
            }
        }

        public static event EventHandler<TaskInformation> Tasked = delegate { };

        public static void StartTask<T>(this T self, string name, string description, Action action)
        {
            Tasked(self.GetType(), new TaskInformation(name, description, action));
        }

        public class LogInfomation : INTERNALDATA
        {
            public readonly Type Type;
            public readonly string RawMessage;
            public readonly string Message;
            public readonly int Level;

            public LogInfomation(Type _type, string _rawMessage, string _message, int _level)
            {
                Type = _type;
                RawMessage = _rawMessage;
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

        //試験ログイベントを発生させる（拡張：任意型）
        public static void RaiseTest<T>(this T self, string rawMessage, int level) { Tested(self.GetType(), new LogInfomation(self.GetType(), rawMessage, GetLogMessage(rawMessage), level)); }
        public static void RaiseTest<T>(this T self, string rawMessage, int level, params string[] arguments) { Tested(self.GetType(), new LogInfomation(self.GetType(), rawMessage, GetLogMessage(rawMessage, arguments), level)); }
        public static void RaiseTest(this Type type, string rawMessage, int level) { Tested(type, new LogInfomation(type, rawMessage, GetLogMessage(rawMessage), level)); }
        public static void RaiseTest(this Type type, string rawMessage, int level, params string[] arguments) { Tested(type, new LogInfomation(type, rawMessage, GetLogMessage(rawMessage, arguments), level)); }

        //通知ログイベントを発生させる（拡張：任意型）
        public static void RaiseNotification<T>(this T self, string rawMessage, int level) { Notified(self.GetType(), new LogInfomation(self.GetType(), rawMessage, GetLogMessage(rawMessage), level)); }
        public static void RaiseNotification<T>(this T self, string rawMessage, int level, params string[] arguments) { Notified(self.GetType(), new LogInfomation(self.GetType(), rawMessage, GetLogMessage(rawMessage, arguments), level)); }
        public static void RaiseNotification(this Type type, string rawMessage, int level) { Notified(type, new LogInfomation(type, rawMessage, GetLogMessage(rawMessage), level)); }
        public static void RaiseNotification(this Type type, string rawMessage, int level, params string[] arguments) { Notified(type, new LogInfomation(type, rawMessage, GetLogMessage(rawMessage, arguments), level)); }

        //結果ログイベントを発生させる（拡張：任意型）
        public static void RaiseResult<T>(this T self, string rawMessage, int level) { Resulted(self.GetType(), new LogInfomation(self.GetType(), rawMessage, GetLogMessage(rawMessage), level)); }
        public static void RaiseResult<T>(this T self, string rawMessage, int level, params string[] arguments) { Resulted(self.GetType(), new LogInfomation(self.GetType(), rawMessage, GetLogMessage(rawMessage, arguments), level)); }
        public static void RaiseResult(this Type type, string rawMessage, int level) { Resulted(type, new LogInfomation(type, rawMessage, GetLogMessage(rawMessage), level)); }
        public static void RaiseResult(this Type type, string rawMessage, int level, params string[] arguments) { Resulted(type, new LogInfomation(type, rawMessage, GetLogMessage(rawMessage, arguments), level)); }

        //警告ログイベントを発生させる（拡張：任意型）
        public static void RaiseWarning<T>(this T self, string rawMessage, int level) { Warned(self.GetType(), new LogInfomation(self.GetType(), rawMessage, GetLogMessage(rawMessage), level)); }
        public static void RaiseWarning<T>(this T self, string rawMessage, int level, params string[] arguments) { Warned(self.GetType(), new LogInfomation(self.GetType(), rawMessage, GetLogMessage(rawMessage, arguments), level)); }
        public static void RaiseWarning(this Type type, string rawMessage, int level) { Warned(type, new LogInfomation(type, rawMessage, GetLogMessage(rawMessage), level)); }
        public static void RaiseWarning(this Type type, string rawMessage, int level, params string[] arguments) { Warned(type, new LogInfomation(type, rawMessage, GetLogMessage(rawMessage, arguments), level)); }

        //過誤ログイベントを発生させる（拡張：任意型）
        public static void RaiseError<T>(this T self, string rawMessage, int level) { Errored(self.GetType(), new LogInfomation(self.GetType(), rawMessage, GetLogMessage(rawMessage), level)); }
        public static void RaiseError<T>(this T self, string rawMessage, int level, params string[] arguments) { Errored(self.GetType(), new LogInfomation(self.GetType(), rawMessage, GetLogMessage(rawMessage, arguments), level)); }
        public static void RaiseError(this Type type, string rawMessage, int level) { Errored(type, new LogInfomation(type, rawMessage, GetLogMessage(rawMessage), level)); }
        public static void RaiseError(this Type type, string rawMessage, int level, params string[] arguments) { Errored(type, new LogInfomation(type, rawMessage, GetLogMessage(rawMessage, arguments), level)); }

        //例外過誤ログイベントを発生させる（拡張：任意型）
        public static void RaiseError<T>(this T self, string rawMessage, int level, Exception ex) { Errored(self.GetType(), new LogInfomation(self.GetType(), rawMessage, string.Join(Environment.NewLine, GetLogMessage(rawMessage), ex.CreateMessage(0)), level)); }
        public static void RaiseError<T>(this T self, string rawMessage, int level, Exception ex, params string[] arguments) { Errored(self.GetType(), new LogInfomation(self.GetType(), rawMessage, string.Join(Environment.NewLine, GetLogMessage(rawMessage, arguments), ex.CreateMessage(0)), level)); }
        public static void RaiseError(this Type type, string rawMessage, int level, Exception ex) { Errored(type, new LogInfomation(type, rawMessage, string.Join(Environment.NewLine, GetLogMessage(rawMessage), ex.CreateMessage(0)), level)); }
        public static void RaiseError(this Type type, string rawMessage, int level, Exception ex, params string[] arguments) { Errored(type, new LogInfomation(type, rawMessage, string.Join(Environment.NewLine, GetLogMessage(rawMessage, arguments), ex.CreateMessage(0)), level)); }

        //真偽値が真のときのみ各種ログイベントを発生させ、真偽値をそのまま返す（拡張：真偽型）
        public static bool RaiseTest(this bool flag, Type type, string message, int level) { if (flag) type.RaiseTest(message, level); return flag; }
        public static bool RaiseTest(this bool flag, Type type, string message, int level, params string[] arguments) { if (flag) type.RaiseTest(message, level, arguments); return flag; }
        public static bool RaiseNotification(this bool flag, Type type, string message, int level) { if (flag) type.RaiseNotification(message, level); return flag; }
        public static bool RaiseNotification(this bool flag, Type type, string message, int level, params string[] arguments) { if (flag) type.RaiseNotification(message, level, arguments); return flag; }
        public static bool RaiseResult(this bool flag, Type type, string message, int level) { if (flag) type.RaiseResult(message, level); return flag; }
        public static bool RaiseResult(this bool flag, Type type, string message, int level, params string[] arguments) { if (flag) type.RaiseResult(message, level, arguments); return flag; }
        public static bool RaiseWarning(this bool flag, Type type, string message, int level) { if (flag) type.RaiseWarning(message, level); return flag; }
        public static bool RaiseWarning(this bool flag, Type type, string message, int level, params string[] arguments) { if (flag) type.RaiseWarning(message, level, arguments); return flag; }
        public static bool RaiseError(this bool flag, Type type, string message, int level) { if (flag) type.RaiseError(message, level); return flag; }
        public static bool RaiseError(this bool flag, Type type, string message, int level, params string[] arguments) { if (flag) type.RaiseError(message, level, arguments); return flag; }

        //真偽値が偽のときのみ各種ログイベントを発生させ、真偽値をそのまま返す（拡張：真偽型）
        public static bool NotRaiseTest(this bool flag, Type type, string message, int level) { return !RaiseTest(!flag, type, message, level); }
        public static bool NotRaiseTest(this bool flag, Type type, string message, int level, params string[] arguments) { return !RaiseTest(!flag, type, message, level, arguments); }
        public static bool NotRaiseNotification(this bool flag, Type type, string message, int level) { return !RaiseNotification(!flag, type, message, level); }
        public static bool NotRaiseNotification(this bool flag, Type type, string message, int level, params string[] arguments) { return !RaiseNotification(!flag, type, message, level, arguments); }
        public static bool NotRaiseResult(this bool flag, Type type, string message, int level) { return !RaiseResult(!flag, type, message, level); }
        public static bool NotRaiseResult(this bool flag, Type type, string message, int level, params string[] arguments) { return !RaiseResult(!flag, type, message, level, arguments); }
        public static bool NotRaiseWarning(this bool flag, Type type, string message, int level) { return !RaiseWarning(!flag, type, message, level); }
        public static bool NotRaiseWarning(this bool flag, Type type, string message, int level, params string[] arguments) { return !RaiseWarning(!flag, type, message, level, arguments); }
        public static bool NotRaiseError(this bool flag, Type type, string message, int level) { return !RaiseError(!flag, type, message, level); }
        public static bool NotRaiseError(this bool flag, Type type, string message, int level, params string[] arguments) { return !RaiseError(!flag, type, message, level, arguments); }

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
        public static Program.LogData.LogGround GetLogGround(this string rawMessage)
        {
            return Program.GetLogGround(rawMessage);
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

    #region P/Invoke

    public static class API
    {
        [DllImport("kernel32.dll")]
        public static extern bool AllocConsole();
        [DllImport("kernel32.dll")]
        public static extern bool FreeConsole();

        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")]
        public static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);
        [DllImport("user32.dll")]
        public static extern bool IsIconic(IntPtr hWnd);
        public const int SW_RESTORE = 9;

        [DllImport("mscoree.dll", CharSet = CharSet.Unicode)]
        public static extern bool StrongNameSignatureVerificationEx(string wszFilePath, bool fForceVerification, ref bool pfWasVerified);
    }

    #endregion

    #region 基底クラス

    public abstract class DATA { }
    public abstract class INTERNALDATA : DATA { }
    //2014/10/26
    //SHAREDDATAの読み書きはSHAREDDATAクラスで行う
    //<未改良>最終的にはStreamInfomationでバージョン指定が必要なのはバージョンありだが、
    //バージョンが保存されない場合（外部からバージョンを指定してやる必要がある場合）のみにすべき
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
            public abstract void WriteUlong(ulong data);
            public abstract void WriteDouble(double data);
            public abstract void WriteDateTime(DateTime data);
            public abstract void WriteString(string data);
        }

        public abstract class STREAMREADER
        {
            public abstract byte[] ReadBytes(int? length);
            public abstract bool ReadBool();
            public abstract int ReadInt();
            public abstract uint ReadUint();
            public abstract float ReadFloat();
            public abstract long ReadLong();
            public abstract ulong ReadUlong();
            public abstract double ReadDouble();
            public abstract DateTime ReadDateTime();
            public abstract string ReadString();
        }

        public class ReaderWriter
        {
            public enum Mode { read, write, neither }

            public class CantReadOrWriteException : Exception { }

            public ReaderWriter(STREAMWRITER _writer, STREAMREADER _reader, Mode _mode)
            {
                writer = _writer;
                reader = _reader;
                mode = _mode;
            }

            private readonly STREAMWRITER writer;
            private readonly STREAMREADER reader;
            private readonly Mode mode;

            public byte[] ReadOrWrite(byte[] bytes, int? length)
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
            //SHAREDDATA（の派生クラス）の配列専用
            public StreamInfomation(Type _type, int? _version, int? _length, Func<object> _sender, Action<object> _receiver)
            {
                if (!_type.IsArray)
                    throw new ArgumentException("stream_info_not_array");

                Type elementType = _type.GetElementType();
                if (!elementType.IsSubclassOf(typeof(SHAREDDATA)))
                    throw new ArgumentException("stream_info_not_sd_array");
                //if (elementType.IsAbstract)
                //throw new ArgumentException("stream_info_sd_array_abstract");
                if (elementType.IsArray)
                    throw new ArgumentException("stream_info_array_of_array");

                if (!elementType.IsAbstract)
                {
                    SHAREDDATA sd = Activator.CreateInstance(elementType) as SHAREDDATA;
                    if ((!sd.IsVersioned && _version != null) || (sd.IsVersioned && _version == null))
                        throw new ArgumentException("stream_info_not_sd_array_is_versioned");
                }
                //<未実装>抽象型の場合はどうするか？

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
                    if (elementType.IsAbstract)
                        throw new ArgumentException("stream_info_array_abstract");
                    if (elementType.IsArray)
                        throw new ArgumentException("stream_info_array_of_array");

                    length = _lengthOrVersion;
                }
                else if (_type.IsSubclassOf(typeof(SHAREDDATA)))
                {
                    //if (_type.IsAbstract)
                    //throw new ArgumentException("stream_info_sd_abstract");

                    if (!_type.IsAbstract)
                    {
                        SHAREDDATA sd = Activator.CreateInstance(_type) as SHAREDDATA;
                        if ((!sd.IsVersioned && _lengthOrVersion != null) || (sd.IsVersioned && _lengthOrVersion == null))
                            throw new ArgumentException("stream_info_sd_is_versioned");
                    }
                    //<未実装>抽象型の場合はどうするか？

                    version = _lengthOrVersion;
                }
                else
                    throw new ArgumentException("stream_info_not_array_sd");

                Type = _type;
                Sender = _sender;
                Receiver = _receiver;
            }

            //配列以外かつSHAREDDATA（の派生クラス）以外専用
            public StreamInfomation(Type _type, Func<object> _sender, Action<object> _receiver)
            {
                if (_type.IsArray)
                    throw new ArgumentException("stream_info_array");
                if (_type.IsSubclassOf(typeof(SHAREDDATA)))
                    throw new ArgumentException("stream_info_sd");
                if (_type.IsAbstract)
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

                    return version.Value;
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
                else if (type == typeof(ulong))
                    writer.WriteUlong((ulong)o);
                else if (type == typeof(double))
                    writer.WriteDouble((double)o);
                else if (type == typeof(DateTime))
                    writer.WriteDateTime((DateTime)o);
                else if (type == typeof(string))
                    writer.WriteString((string)o);
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
                Array os = obj as Array;
                Type elementType = si.Type.GetElementType();

                if (si.Length == null)
                    writer.WriteInt(os.Length);
                foreach (var innerObj in os)
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
                else if (type == typeof(ulong))
                    return reader.ReadUlong();
                else if (type == typeof(double))
                    return reader.ReadDouble();
                else if (type == typeof(DateTime))
                    return reader.ReadDateTime();
                else if (type == typeof(string))
                    return reader.ReadString();
                else
                    throw new NotSupportedException("sd_read_not_supported");
            };

            if (si.Type == typeof(byte[]))
                si.Receiver(reader.ReadBytes(si.Length));
            else if (si.Type.IsArray)
            {
                Type elementType = si.Type.GetElementType();
                Array os = Array.CreateInstance(elementType, si.Length == null ? reader.ReadInt() : si.Length.Value) as Array;

                for (int i = 0; i < os.Length; i++)
                    os.SetValue(_Read(elementType), i);

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

            public CommunicationApparatusWriter(IChannel _ca) { ca = _ca; }

            public override void WriteBytes(byte[] data, int? length) { ca.WriteBytes(data); }

            public override void WriteBool(bool data) { ca.WriteBytes(BitConverter.GetBytes(data)); }

            public override void WriteInt(int data) { ca.WriteBytes(BitConverter.GetBytes(data)); }

            public override void WriteUint(uint data) { ca.WriteBytes(BitConverter.GetBytes(data)); }

            public override void WriteFloat(float data) { ca.WriteBytes(BitConverter.GetBytes(data)); }

            public override void WriteLong(long data) { ca.WriteBytes(BitConverter.GetBytes(data)); }

            public override void WriteUlong(ulong data) { ca.WriteBytes(BitConverter.GetBytes(data)); }

            public override void WriteDouble(double data) { ca.WriteBytes(BitConverter.GetBytes(data)); }

            public override void WriteDateTime(DateTime data) { ca.WriteBytes(BitConverter.GetBytes((data).ToBinary())); }

            public override void WriteString(string data) { ca.WriteBytes(Encoding.UTF8.GetBytes(data)); }
        }

        public class CommunicationApparatusReader : STREAMREADER
        {
            private readonly IChannel ca;

            public CommunicationApparatusReader(IChannel _ca) { ca = _ca; }

            public override byte[] ReadBytes(int? length) { return ca.ReadBytes(); }

            public override bool ReadBool() { return BitConverter.ToBoolean(ca.ReadBytes(), 0); }

            public override int ReadInt() { return BitConverter.ToInt32(ca.ReadBytes(), 0); }

            public override uint ReadUint() { return BitConverter.ToUInt32(ca.ReadBytes(), 0); }

            public override float ReadFloat() { return BitConverter.ToSingle(ca.ReadBytes(), 0); }

            public override long ReadLong() { return BitConverter.ToInt64(ca.ReadBytes(), 0); }

            public override ulong ReadUlong() { return BitConverter.ToUInt64(ca.ReadBytes(), 0); }

            public override double ReadDouble() { return BitConverter.ToDouble(ca.ReadBytes(), 0); }

            public override DateTime ReadDateTime() { return DateTime.FromBinary(BitConverter.ToInt64(ca.ReadBytes(), 0)); }

            public override string ReadString() { return Encoding.UTF8.GetString(ca.ReadBytes()); }
        }

        public class ProtocolInfomation : StreamInfomation
        {
            public ProtocolInfomation(Type _type, int? _version, int? _length, Func<object> _sender, Action<object> _receiver, Direction _forClient) : base(_type, _version, _length, _sender, _receiver) { ForClient = _forClient; }

            public ProtocolInfomation(Type _type, int? _lengthOrVersion, Func<object> _sender, Action<object> _receiver, Direction _forClient) : base(_type, _lengthOrVersion, _sender, _receiver) { ForClient = _forClient; }

            public ProtocolInfomation(Type _type, Func<object> _sender, Action<object> _receiver, Direction _forClient) : base(_type, _sender, _receiver) { ForClient = _forClient; }

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
        //<未実装>配列の配列に対応しない代わりに多次元配列には対応すべきかもしれない
        //<未実装>総称型への対応（抽象総称型だけでなく、普通の総称型にも対応した方が良い？）
        //2014/05/07
        //予約領域は廃止
        //データが無駄に大きくなるので、厳格にバージョン管理して対応するべき
        //2014/10/26
        //署名関連機能も廃止
        //別途署名用の抽象クラスでも作るべき
        //2014/02/23
        //抽象クラスには対応しない
        //抽象クラスの変数に格納されている具象クラスを保存する場合には具象クラスとしてStreamInfomationを作成する
        //具象クラスが複数ある場合には具象クラス別にStreamInfomationを作成する
        //2014/05/07
        //配列の配列には対応しない
        //対応することは可能だが、実装が複雑になる
        //又、基本的には配列をラップしたクラスを別途作るべき場合が多いように思える
        //2014/10/27
        //抽象クラスに対応する！
        //型名を直接保持したら型名が変わった場合に困るのでGuidを使うしかあるまい
        //そうすると、型とGuidの対応が予め分かっていなければならない
        //ただし、総称型をどうするかという問題がある
        //更に、現状ではこのアセンブリの型しか探査しない

        public class MyStreamWriter : STREAMWRITER
        {
            public MyStreamWriter(Stream _stream) { stream = _stream; }

            private readonly Stream stream;

            public override void WriteBytes(byte[] data, int? length)
            {
                if (length == null)
                    stream.Write(BitConverter.GetBytes(data.Length), 0, 4);
                stream.Write(data, 0, data.Length);
            }

            public override void WriteBool(bool data) { stream.Write(BitConverter.GetBytes(data), 0, 1); }

            public override void WriteInt(int data) { stream.Write(BitConverter.GetBytes(data), 0, 4); }

            public override void WriteUint(uint data) { stream.Write(BitConverter.GetBytes(data), 0, 4); }

            public override void WriteFloat(float data) { stream.Write(BitConverter.GetBytes(data), 0, 4); }

            public override void WriteLong(long data) { stream.Write(BitConverter.GetBytes(data), 0, 8); }

            public override void WriteUlong(ulong data) { stream.Write(BitConverter.GetBytes(data), 0, 8); }

            public override void WriteDouble(double data) { stream.Write(BitConverter.GetBytes(data), 0, 8); }

            public override void WriteDateTime(DateTime data) { WriteLong(data.ToBinary()); }

            public override void WriteString(string data)
            {
                byte[] bytes = Encoding.UTF8.GetBytes(data);
                stream.Write(BitConverter.GetBytes(bytes.Length), 0, 4);
                stream.Write(bytes, 0, bytes.Length);
            }
        }

        public class MyStreamReader : STREAMREADER
        {
            public MyStreamReader(Stream _stream) { stream = _stream; }

            private readonly Stream stream;

            public override byte[] ReadBytes(int? length)
            {
                if (length == null)
                {
                    byte[] lengthBytes = new byte[4];
                    stream.Read(lengthBytes, 0, 4);
                    length = BitConverter.ToInt32(lengthBytes, 0);
                }

                byte[] bytes = new byte[length.Value];
                stream.Read(bytes, 0, length.Value);
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

            public override ulong ReadUlong()
            {
                byte[] bytes = new byte[8];
                stream.Read(bytes, 0, 8);
                return BitConverter.ToUInt64(bytes, 0);
            }

            public override double ReadDouble()
            {
                byte[] bytes = new byte[8];
                stream.Read(bytes, 0, 8);
                return BitConverter.ToDouble(bytes, 0);
            }

            public override DateTime ReadDateTime() { return DateTime.FromBinary(ReadLong()); }

            public override string ReadString()
            {
                byte[] lengthBytes = new byte[4];
                stream.Read(lengthBytes, 0, 4);
                int length = BitConverter.ToInt32(lengthBytes, 0);

                byte[] bytes = new byte[length];
                stream.Read(bytes, 0, length);
                return Encoding.UTF8.GetString(bytes);
            }
        }

        public class MainDataInfomation : StreamInfomation
        {
            public MainDataInfomation(Type _type, int? _version, int? _length, Func<object> _getter, Action<object> _setter) : base(_type, _version, _length, _getter, _setter) { }

            public MainDataInfomation(Type _type, int? _lengthOrVersion, Func<object> _getter, Action<object> _setter) : base(_type, _lengthOrVersion, _getter, _setter) { }

            public MainDataInfomation(Type _type, Func<object> _getter, Action<object> _setter) : base(_type, _getter, _setter) { }

            public Func<object> Getter { get { return Sender; } }
            public Action<object> Setter { get { return Receiver; } }
        }

        private static readonly Dictionary<Guid, Type> guidsAndTypes = new Dictionary<Guid, Type>();

        static SHAREDDATA()
        {
            foreach (Type type in Assembly.GetExecutingAssembly().GetTypes())
                //<未実装>総称型への対応
                if (type.IsSubclassOf(typeof(SHAREDDATA)) && !type.ContainsGenericParameters && !type.IsAbstract)
                {
                    try
                    {
                        SHAREDDATA sd = Activator.CreateInstance(type) as SHAREDDATA;
                        if (sd.Guid != Guid.Empty)
                            guidsAndTypes.Add(sd.Guid, type);
                    }
                    catch (Exception ex)
                    {
                        throw ex;
                    }
                }
        }

        public SHAREDDATA() : this(null) { }

        public SHAREDDATA(int? _version)
        {
            if ((IsVersioned && _version == null) || (!IsVersioned && _version != null))
                throw new ArgumentException("sd_is_versioned_and_version");

            version = _version;
        }

        public virtual bool IsVersioned { get { return false; } }
        public virtual bool IsVersionSaved { get { return true; } }
        public virtual bool IsCorruptionChecked { get { return false; } }

        public virtual Guid Guid { get { return Guid.Empty; } }

        private int? version;
        public int Version
        {
            get
            {
                if (!IsVersioned)
                    throw new NotSupportedException("sd_version");

                return version.Value;
            }
            set
            {
                if (!IsVersioned)
                    throw new NotSupportedException("sd_version");

                version = value;
            }
        }

        protected void ToBinaryMainData(MemoryStream ms, ref bool isCorruptionCheckNeeded, List<Guid> typeList, Func<ReaderWriter, IEnumerable<MainDataInfomation>> si)
        {
            MyStreamWriter writer = new MyStreamWriter(ms);

            foreach (var mdi in si(new ReaderWriter(writer, new MyStreamReader(ms), ReaderWriter.Mode.write)))
                if (mdi.Type.IsSubclassOf(typeof(SHAREDDATA)))
                {
                    SHAREDDATA sd = mdi.Getter() as SHAREDDATA;

                    if (sd.IsVersioned && !sd.IsVersionSaved && sd.Version != mdi.Version)
                        throw new InvalidOperationException("version_mismatch");

                    if (mdi.Type.IsAbstract)
                    {
                        if (!sd.GetType().IsSubclassOf(mdi.Type))
                            throw new InvalidOperationException("type_mismatch");

                        int index = typeList.IndexOf(sd.Guid);
                        if (index == -1)
                        {
                            index = typeList.Count;

                            typeList.Add(sd.Guid);
                        }

                        writer.WriteInt(index);
                    }
                    else if (sd.GetType() != mdi.Type)
                        throw new InvalidOperationException("type_mismatch");

                    sd.ToBinary(ms, ref isCorruptionCheckNeeded, typeList);
                }
                else if (mdi.Type.IsArray && mdi.Type.GetElementType().IsSubclassOf(typeof(SHAREDDATA)))
                {
                    Type elementType = mdi.Type.GetElementType();

                    SHAREDDATA[] sds = mdi.Getter() as SHAREDDATA[];

                    //抽象型の配列が指定されているときに態々具象型の配列を渡すのはおかしくないか？ということで例外発生
                    if (sds.GetType() != mdi.Type)
                        throw new InvalidOperationException("type_mismatch");
                    if (mdi.Length != null && mdi.Length != sds.Length)
                        throw new InvalidOperationException("length_mismatch");

                    if (mdi.Length == null)
                        writer.WriteInt(sds.Length);

                    foreach (SHAREDDATA sd in sds)
                    {
                        if (sd.IsVersioned && !sd.IsVersionSaved && sd.Version != mdi.Version)
                            throw new InvalidOperationException("version_mismatch");

                        if (elementType.IsAbstract)
                        {
                            if (sd.Guid == Guid.Empty)
                                throw new NotSupportedException("guid_not_supported");

                            int index = typeList.IndexOf(sd.Guid);
                            if (index == -1)
                            {
                                index = typeList.Count;

                                typeList.Add(sd.Guid);
                            }

                            writer.WriteInt(index);
                        }

                        sd.ToBinary(ms, ref isCorruptionCheckNeeded, typeList);
                    }
                }
                else
                    Write(writer, mdi);
        }

        protected void ToBinaryMainData(MemoryStream ms, ref bool isCorruptionCheckNeeded, List<Guid> typeList) { ToBinaryMainData(ms, ref isCorruptionCheckNeeded, typeList, StreamInfo); }

        protected byte[] ToBinaryMainData(Func<ReaderWriter, IEnumerable<MainDataInfomation>> si)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                //未使用（ダミー）
                bool isCorruptionCheckNeeded = false;
                List<Guid> typeList = new List<System.Guid>();

                ToBinaryMainData(ms, ref isCorruptionCheckNeeded, typeList, si);

                return ms.ToArray();
            }
        }

        protected byte[] ToBinaryMainData() { return ToBinaryMainData(StreamInfo); }

        protected void ToBinary(MemoryStream ms, ref bool isCorruptionCheckNeeded, List<Guid> typeList, Func<ReaderWriter, IEnumerable<MainDataInfomation>> si)
        {
            if (IsCorruptionChecked)
                isCorruptionCheckNeeded = true;

            if (IsVersioned && IsVersionSaved)
                ms.Write(BitConverter.GetBytes(version.Value), 0, 4);

            ToBinaryMainData(ms, ref isCorruptionCheckNeeded, typeList, si);
        }

        protected void ToBinary(MemoryStream ms, ref bool isCorruptionCheckNeeded, List<Guid> typeList) { ToBinary(ms, ref isCorruptionCheckNeeded, typeList, StreamInfo); }

        protected byte[] ToBinary(Func<ReaderWriter, IEnumerable<MainDataInfomation>> si)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                bool isCorruptionCheckNeeded = false;
                List<Guid> typeList = new List<System.Guid>();

                ToBinary(ms, ref isCorruptionCheckNeeded, typeList, si);

                byte[] msBytes = ms.ToArray();

                using (MemoryStream ms2 = new MemoryStream())
                {
                    bool isTypeListNeeded = typeList.Count != 0;
                    ms2.Write(BitConverter.GetBytes(isTypeListNeeded), 0, 1);
                    if (isTypeListNeeded)
                    {
                        ms2.Write(BitConverter.GetBytes(typeList.Count), 0, 4);
                        foreach (Guid guid in typeList)
                            ms2.Write(guid.ToByteArray(), 0, 16);
                    }
                    ms2.Write(msBytes, 0, msBytes.Length);

                    if (isCorruptionCheckNeeded)
                        ms2.Write(msBytes.ComputeSha256(), 0, 4);

                    return ms2.ToArray();
                }
            }
        }

        public byte[] ToBinary() { return ToBinary(StreamInfo); }

        protected void FromBinaryMainData(MemoryStream ms, ref bool isCorruptionCheckNeeded, List<Guid> typeList, Func<ReaderWriter, IEnumerable<MainDataInfomation>> si)
        {
            MyStreamReader reader = new MyStreamReader(ms);

            foreach (var mdi in StreamInfo(new ReaderWriter(new MyStreamWriter(ms), reader, ReaderWriter.Mode.read)))
                if (mdi.Type.IsSubclassOf(typeof(SHAREDDATA)))
                {
                    Type type;
                    if (mdi.Type.IsAbstract)
                    {
                        int index = reader.ReadInt();

                        if (typeList.Count <= index)
                            throw new InvalidOperationException("type_index");
                        if (!guidsAndTypes.Keys.Contains(typeList[index]))
                            throw new InvalidOperationException("type_guid_not_found");

                        type = guidsAndTypes[typeList[index]];
                    }
                    else
                        type = mdi.Type;

                    SHAREDDATA sd = Activator.CreateInstance(type) as SHAREDDATA;

                    if (sd.IsVersioned && !sd.IsVersionSaved)
                        sd.Version = mdi.Version;

                    sd.FromBinary(ms, ref isCorruptionCheckNeeded, typeList);

                    mdi.Setter(sd);
                }
                else if (mdi.Type.IsArray && mdi.Type.GetElementType().IsSubclassOf(typeof(SHAREDDATA)))
                {
                    Type elementType = mdi.Type.GetElementType();

                    Array sds = Array.CreateInstance(elementType, mdi.Length ?? reader.ReadInt()) as Array;

                    for (int i = 0; i < sds.Length; i++)
                    {
                        Type type;
                        if (elementType.IsAbstract)
                        {
                            int index = reader.ReadInt();

                            if (typeList.Count <= index)
                                throw new InvalidOperationException("type_index");
                            if (!guidsAndTypes.Keys.Contains(typeList[index]))
                                throw new InvalidOperationException("type_guid_not_found");

                            type = guidsAndTypes[typeList[index]];
                        }
                        else
                            type = elementType;

                        SHAREDDATA sd = Activator.CreateInstance(type) as SHAREDDATA;

                        if (sd.IsVersioned && !sd.IsVersionSaved)
                            sd.Version = mdi.Version;

                        sd.FromBinary(ms, ref isCorruptionCheckNeeded, typeList);

                        sds.SetValue(sd, i);
                    }

                    mdi.Setter(sds);
                }
                else
                    Read(reader, mdi);
        }

        protected void FromBinaryMainData(MemoryStream ms, ref bool isCorruptionCheckNeeded, List<Guid> typeList) { FromBinaryMainData(ms, ref isCorruptionCheckNeeded, typeList, StreamInfo); }

        protected void FromBinaryMainData(byte[] binary, Func<ReaderWriter, IEnumerable<MainDataInfomation>> si)
        {
            using (MemoryStream ms = new MemoryStream(binary))
            {
                //未使用（ダミー）
                bool isCorruptionCheckNeeded = false;
                List<Guid> typeList = new List<System.Guid>();

                FromBinaryMainData(ms, ref isCorruptionCheckNeeded, typeList, si);
            }
        }

        protected void FromBinaryMainData(byte[] binary) { FromBinaryMainData(binary, StreamInfo); }

        protected void FromBinary(MemoryStream ms, ref bool isCorruptionCheckNeeded, List<Guid> typeList, Func<ReaderWriter, IEnumerable<MainDataInfomation>> si)
        {
            if (IsCorruptionChecked)
                isCorruptionCheckNeeded = true;

            if (IsVersioned && IsVersionSaved)
            {
                byte[] versionBytes = new byte[4];
                ms.Read(versionBytes, 0, 4);
                version = BitConverter.ToInt32(versionBytes, 0);
            }

            FromBinaryMainData(ms, ref isCorruptionCheckNeeded, typeList, si);
        }

        protected void FromBinary(MemoryStream ms, ref bool isCorruptionCheckNeeded, List<Guid> typeList) { FromBinary(ms, ref isCorruptionCheckNeeded, typeList, StreamInfo); }

        protected void FromBinary(byte[] binary, Func<ReaderWriter, IEnumerable<MainDataInfomation>> si)
        {
            using (MemoryStream ms = new MemoryStream(binary))
            {
                bool isCorruptionCheckNeeded = false;
                List<Guid> typeList = new List<System.Guid>();

                byte[] isTypeListNeededBytes = new byte[1];
                ms.Read(isTypeListNeededBytes, 0, 1);
                if (BitConverter.ToBoolean(isTypeListNeededBytes, 0))
                {
                    byte[] typeListLengthBytes = new byte[4];
                    ms.Read(typeListLengthBytes, 0, 4);
                    int typeListLength = BitConverter.ToInt32(typeListLengthBytes, 0);

                    for (int i = 0; i < typeListLength; i++)
                    {
                        byte[] typeGuidBytes = new byte[16];
                        ms.Read(typeGuidBytes, 0, 16);
                        typeList.Add(new Guid(typeGuidBytes));
                    }
                }

                int typeListBytesLength = (int)ms.Position;

                FromBinary(ms, ref isCorruptionCheckNeeded, typeList, si);

                if (isCorruptionCheckNeeded)
                {
                    byte[] checkBytes = new byte[4];
                    ms.Read(checkBytes, 0, 4);
                    int check = BitConverter.ToInt32(checkBytes, 0);

                    byte[] checkData = new byte[ms.Length - 4 - typeListBytesLength];
                    ms.Seek(typeListBytesLength, SeekOrigin.Begin);
                    ms.Read(checkData, 0, checkData.Length);

                    if (check != BitConverter.ToInt32(checkData.ComputeSha256(), 0))
                        throw new InvalidDataException("from_binary_check");
                }
            }
        }

        public void FromBinary(byte[] binary) { FromBinary(binary, StreamInfo); }

        public static byte[] ToBinary<T>(T sd) where T : SHAREDDATA
        {
            if (!typeof(T).IsAbstract)
                return sd.ToBinary();

            using (MemoryStream ms = new MemoryStream())
            {
                bool isCorruptionCheckNeeded = false;
                List<Guid> typeList = new List<System.Guid>() { sd.Guid };

                sd.ToBinary(ms, ref isCorruptionCheckNeeded, typeList);

                byte[] msBytes = ms.ToArray();

                using (MemoryStream ms2 = new MemoryStream())
                {
                    bool isTypeListNeeded = typeList.Count != 0;
                    ms2.Write(BitConverter.GetBytes(isTypeListNeeded), 0, 1);
                    if (isTypeListNeeded)
                    {
                        ms2.Write(BitConverter.GetBytes(typeList.Count), 0, 4);
                        foreach (Guid guid in typeList)
                            ms2.Write(guid.ToByteArray(), 0, 16);
                    }
                    ms2.Write(msBytes, 0, msBytes.Length);

                    if (isCorruptionCheckNeeded)
                        ms2.Write(msBytes.ComputeSha256(), 0, 4);

                    return ms2.ToArray();
                }
            }
        }

        public static byte[] ToBinary<T>(T sd, int version) where T : SHAREDDATA
        {
            if (sd.Version != version)
                throw new InvalidOperationException();

            return ToBinary(sd);
        }

        public static T FromBinary<T>(byte[] binary) where T : SHAREDDATA { return FromBinary<T>(binary, null); }

        public static T FromBinary<T>(byte[] binary, int? version) where T : SHAREDDATA
        {
            if (!typeof(T).IsAbstract)
            {
                T sd = Activator.CreateInstance(typeof(T)) as T;
                if (version.HasValue)
                    sd.Version = version.Value;
                sd.FromBinary(binary);
                return sd;
            }

            using (MemoryStream ms = new MemoryStream(binary))
            {
                bool isCorruptionCheckNeeded = false;
                List<Guid> typeList = new List<System.Guid>();

                byte[] isTypeListNeededBytes = new byte[1];
                ms.Read(isTypeListNeededBytes, 0, 1);
                if (BitConverter.ToBoolean(isTypeListNeededBytes, 0))
                {
                    byte[] typeListLengthBytes = new byte[4];
                    ms.Read(typeListLengthBytes, 0, 4);
                    int typeListLength = BitConverter.ToInt32(typeListLengthBytes, 0);

                    for (int i = 0; i < typeListLength; i++)
                    {
                        byte[] typeGuidBytes = new byte[16];
                        ms.Read(typeGuidBytes, 0, 16);
                        typeList.Add(new Guid(typeGuidBytes));
                    }
                }

                int typeListBytesLength = (int)ms.Position;

                if (typeList.Count <= 0)
                    throw new InvalidOperationException("type_index");
                if (!guidsAndTypes.Keys.Contains(typeList[0]))
                    throw new InvalidOperationException("type_guid_not_found");

                T sd = Activator.CreateInstance(guidsAndTypes[typeList[0]]) as T;
                if (version.HasValue)
                    sd.Version = version.Value;
                sd.FromBinary(ms, ref isCorruptionCheckNeeded, typeList);

                if (isCorruptionCheckNeeded)
                {
                    byte[] checkBytes = new byte[4];
                    ms.Read(checkBytes, 0, 4);
                    int check = BitConverter.ToInt32(checkBytes, 0);

                    byte[] checkData = new byte[ms.Length - 4 - typeListBytesLength];
                    ms.Seek(typeListBytesLength, SeekOrigin.Begin);
                    ms.Read(checkData, 0, checkData.Length);

                    if (check != BitConverter.ToInt32(checkData.ComputeSha256(), 0))
                        throw new InvalidDataException("from_binary_check");
                }

                return sd;
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

        public bool canSet { get; private set; }

        public event EventHandler SettingsChanged = delegate { };

        public virtual void StartSetting()
        {
            if (canSet)
                throw new InvalidOperationException("already_setting_started");

            canSet = true;
        }

        public virtual void EndSetting()
        {
            if (!canSet)
                throw new InvalidOperationException("not_yet_setting_started");

            canSet = false;

            SettingsChanged(this, EventArgs.Empty);
        }

        public XElement ToXml()
        {
            XElement xElement = new XElement(XmlName);
            foreach (var mdi in MainDataInfo)
            {
                Action<Type, MainDataInfomation, object, XElement> _Write = (type, innerMdi, innerObj, innerXElement) =>
                {
                    if (type == typeof(bool))
                        innerXElement.Add(new XElement(innerMdi.XmlName, ((bool)innerObj).ToString()));
                    else if (type == typeof(short))
                        innerXElement.Add(new XElement(innerMdi.XmlName, ((short)innerObj).ToString()));
                    else if (type == typeof(ushort))
                        innerXElement.Add(new XElement(innerMdi.XmlName, ((ushort)innerObj).ToString()));
                    else if (type == typeof(int))
                        innerXElement.Add(new XElement(innerMdi.XmlName, ((int)innerObj).ToString()));
                    else if (type == typeof(uint))
                        innerXElement.Add(new XElement(innerMdi.XmlName, ((uint)innerObj).ToString()));
                    else if (type == typeof(float))
                        innerXElement.Add(new XElement(innerMdi.XmlName, ((float)innerObj).ToString()));
                    else if (type == typeof(long))
                        innerXElement.Add(new XElement(innerMdi.XmlName, ((long)innerObj).ToString()));
                    else if (type == typeof(ulong))
                        innerXElement.Add(new XElement(innerMdi.XmlName, ((ulong)innerObj).ToString()));
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
                    else if (type == typeof(short))
                        return short.Parse(iiXElement.Value);
                    else if (type == typeof(ushort))
                        return ushort.Parse(iiXElement.Value);
                    else if (type == typeof(int))
                        return int.Parse(iiXElement.Value);
                    else if (type == typeof(uint))
                        return uint.Parse(iiXElement.Value);
                    else if (type == typeof(float))
                        return float.Parse(iiXElement.Value);
                    else if (type == typeof(long))
                        return long.Parse(iiXElement.Value);
                    else if (type == typeof(ulong))
                        return ulong.Parse(iiXElement.Value);
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
    public abstract class SETTABLESETTINGSDATA : SETTINGSDATA { }
    public abstract class SAVEABLESETTINGSDATA : SETTINGSDATA
    {
        public SAVEABLESETTINGSDATA(string _filename)
        {
            filename = _filename;

            Load();
        }

        public string filename { get; private set; }

        public void Load()
        {
            if (File.Exists(filename))
                FromXml(XElement.Load(filename));
        }

        public void Save() { ToXml().Save(filename); }
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
                : base(_taskInfo.Name, _taskInfo.Descption, _taskInfo.Action)
            {
                Number = _number;
                StartedTime = DateTime.Now;
            }
        }

        public class Tasker
        {
            public Tasker() { tasks = new List<TaskStatus>(); }

            private static readonly bool isOutputTaskStarted = true;
            private static readonly bool isOutputTaskEnded = true;

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

            public event EventHandler TaskStarted = delegate { };
            public event EventHandler TaskEnded = delegate { };

            public void New(TaskData task)
            {
                Thread thread = new Thread(() =>
                {
                    try
                    {
                        //2014/09/15
                        //もしこの前後に処理を追加するのならその処理で例外が発生した場合の対処のために
                        //TaskErroredのようなイベントを発生させるようにするか、
                        //例外対処処理を渡せるようにすべきかもしれない
                        task.Action();
                    }
                    catch (Exception ex)
                    {
                        this.RaiseError("task", 5, ex);
                    }
                    finally
                    {
                        TaskStatus status = null;
                        lock (tasksLock)
                        {
                            status = tasks.Where((e) => e.Data == task).FirstOrDefault();
                            if (status == null)
                                throw new InvalidOperationException("task_not_found");
                            tasks.Remove(status);
                        }

                        if (isOutputTaskEnded)
                            this.ConsoleWriteLine(string.Join(":", "tesk_end", status.Data.Name, tasks.Count.ToString()));

                        TaskEnded(this, EventArgs.Empty);
                    }
                });
                thread.IsBackground = true;
                thread.Name = task.Name;

                lock (tasksLock)
                    tasks.Add(new TaskStatus(task, thread));

                if (isOutputTaskStarted)
                    this.ConsoleWriteLine(string.Join(":", "tesk_start", task.Name, tasks.Count.ToString()));

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

                this.RaiseNotification("task_aborted", 5);
            }

            public void AbortAll()
            {
                lock (tasksLock)
                {
                    foreach (var task in tasks)
                        task.Thread.Abort();
                    tasks.Clear();
                }

                this.RaiseNotification("all_tasks_aborted", 5);
            }
        }

        public class LogData : Extension.LogInfomation
        {
            public readonly DateTime Time;
            public readonly LogKind Kind;

            public LogData(Extension.LogInfomation _logInfo, LogKind _kind)
                : base(_logInfo.Type, _logInfo.RawMessage, _logInfo.Message, _logInfo.Level)
            {
                Time = DateTime.Now;
                Kind = _kind;
            }

            public enum LogKind { test, notification, result, warning, error }
            public enum LogGround { foundation, core, common, networkBase, creaNetworkBase, cremlia, creaNetwork, data, ui, other }

            public LogGround Ground
            {
                get { return RawMessage.GetLogGround(); }
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
                    else if (Ground == LogGround.data)
                        return "データ".Multilanguage(86);
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

        public class ProgramSettings : SAVEABLESETTINGSDATA
        {
            public ProgramSettings() : base("ProgramSettings.xml") { logSettings = new LogSettings(); }

            public bool isNodePortAltered { get; private set; }
            private ushort nodePort = 7777;
            public ushort NodePort
            {
                get { return nodePort; }
                set
                {
                    if (!canSet)
                        throw new InvalidOperationException("cant_set");

                    if (value != nodePort)
                    {
                        nodePort = value;
                        isNodePortAltered = true;
                    }
                }
            }

            public bool isCultureAltered { get; private set; }
            private string culture = "ja-JP";
            public string Culture
            {
                get { return culture; }
                set
                {
                    if (!canSet)
                        throw new InvalidOperationException("cant_set");

                    if (value != culture)
                    {
                        culture = value;
                        isCultureAltered = true;
                    }
                }
            }

            public bool isErrorLogAltered { get; private set; }
            private string errorLog = "Error.txt";
            public string ErrorLog
            {
                get { return errorLog; }
                set
                {
                    if (!canSet)
                        throw new InvalidOperationException("cant_set");

                    if (value != errorLog)
                    {
                        errorLog = value;
                        isErrorLogAltered = true;
                    }
                }
            }

            public bool isErrorReportAltered { get; private set; }
            private string errorReport = "ErrorReport.txt";
            public string ErrorReport
            {
                get { return errorReport; }
                set
                {
                    if (!canSet)
                        throw new InvalidOperationException("cant_set");

                    if (value != errorReport)
                    {
                        errorReport = value;
                        isErrorReportAltered = true;
                    }
                }
            }

            public bool isIsLogAltered { get; private set; }
            private bool isLog = true;
            public bool IsLog
            {
                get { return isLog; }
                set
                {
                    if (!canSet)
                        throw new InvalidOperationException("cant_set");

                    if (value != isLog)
                    {
                        isLog = value;
                        isIsLogAltered = true;
                    }
                }
            }

            public bool isLogSettingsAltered { get; private set; }
            private LogSettings logSettings;
            public LogSettings LogSettings
            {
                get { return logSettings; }
                set
                {
                    if (!canSet)
                        throw new InvalidOperationException("cant_set");

                    if (value != logSettings)
                    {
                        logSettings = value;
                        isLogSettingsAltered = true;
                    }
                }
            }

            protected override string XmlName { get { return "ProgramSettings"; } }
            protected override MainDataInfomation[] MainDataInfo
            {
                get
                {
                    return new MainDataInfomation[]{
                        new MainDataInfomation(typeof(ushort), "NodePort", () => nodePort, (o) => nodePort = (ushort)o),
                        new MainDataInfomation(typeof(string), "Culture", () => culture, (o) => culture = (string)o),
                        new MainDataInfomation(typeof(string), "ErrorLog", () => errorLog, (o) => errorLog = (string)o),
                        new MainDataInfomation(typeof(string), "ErrorReport", () => errorReport, (o) => errorReport = (string)o),
                        new MainDataInfomation(typeof(bool), "IsLog", () => isLog, (o) => isLog = (bool)o),
                        new MainDataInfomation(typeof(LogSettings), "LogSettings", () => logSettings, (o) => logSettings = (LogSettings)o),
                    };
                }
            }

            public override void StartSetting()
            {
                base.StartSetting();

                isNodePortAltered = false;
                isCultureAltered = false;
                isErrorLogAltered = false;
                isErrorReportAltered = false;
                isIsLogAltered = false;
                isLogSettingsAltered = false;
            }
        }

        public class LogSettings : SETTABLESETTINGSDATA
        {
            public LogSettings() { filters = new List<LogFilter>(); }

            public enum SaveMethod { allInOne, monthByMonth, dayByDay }

            public bool isMinimalLevelAltered { get; private set; }
            private int minimalLevel = 0;
            public int MinimalLevel
            {
                get { return minimalLevel; }
                set
                {
                    if (!canSet)
                        throw new InvalidOperationException("cant_set");

                    if (value != minimalLevel)
                    {
                        minimalLevel = value;
                        isMinimalLevelAltered = true;
                    }
                }
            }

            public bool isMaximalHoldingCountAltered { get; private set; }
            private int maximalHoldingCount = 64;
            public int MaximalHoldingCount
            {
                get { return maximalHoldingCount; }
                set
                {
                    if (!canSet)
                        throw new InvalidOperationException("cant_set");

                    if (value != maximalHoldingCount)
                    {
                        maximalHoldingCount = value;
                        isMaximalHoldingCountAltered = true;
                    }
                }
            }

            public bool isIsSaveAltered { get; private set; }
            private bool isSave = true;
            public bool IsSave
            {
                get { return isSave; }
                set
                {
                    if (!canSet)
                        throw new InvalidOperationException("cant_set");

                    if (value != isSave)
                    {
                        isSave = value;
                        isIsSaveAltered = true;
                    }
                }
            }

            public bool isSavePathAltered { get; private set; }
            private string savePath = "Log.log";
            public string SavePath
            {
                get { return savePath; }
                set
                {
                    if (!canSet)
                        throw new InvalidOperationException("cant_set");

                    if (value != savePath)
                    {
                        savePath = value;
                        isSavePathAltered = true;
                    }
                }
            }

            public bool isSaveMethAltered { get; private set; }
            private SaveMethod saveMeth = SaveMethod.allInOne;
            public SaveMethod SaveMeth
            {
                get { return saveMeth; }
                set
                {
                    if (!canSet)
                        throw new InvalidOperationException("cant_set");

                    if (value != saveMeth)
                    {
                        saveMeth = value;
                        isSaveMethAltered = true;
                    }
                }
            }

            public bool isExpressionAltered { get; private set; }
            private string expression = string.Empty;
            public string Expression
            {
                get { return expression; }
                set
                {
                    if (!canSet)
                        throw new InvalidOperationException("cant_set");

                    if (value != expression)
                    {
                        expression = value;
                        isExpressionAltered = true;
                    }
                }
            }

            private readonly object filtersLock = new object();
            private List<LogFilter> filters;
            public LogFilter[] Filters { get { return filters.ToArray(); } }

            protected override string XmlName { get { return "LogSettings"; } }
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

            public override void StartSetting()
            {
                base.StartSetting();

                isMinimalLevelAltered = false;
                isMaximalHoldingCountAltered = false;
                isIsSaveAltered = false;
                isSavePathAltered = false;
                isSaveMethAltered = false;
                isExpressionAltered = false;
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

        public class LogFilter : SETTABLESETTINGSDATA
        {
            public bool isNameAltered { get; private set; }
            private string name = string.Empty;
            public string Name
            {
                get { return name; }
                set
                {
                    if (!canSet)
                        throw new InvalidOperationException("cant_set");

                    if (value != name)
                    {
                        name = value;
                        isNameAltered = true;
                    }
                }
            }

            public bool isIsEnabledAltered { get; private set; }
            private bool isEnabled = false;
            public bool IsEnabled
            {
                get { return isEnabled; }
                set
                {
                    if (!canSet)
                        throw new InvalidOperationException("cant_set");

                    if (value != isEnabled)
                    {
                        isEnabled = value;
                        isIsEnabledAltered = true;
                    }
                }
            }

            public bool isIsWordEnabledAltered { get; private set; }
            private bool isWordEnabled = false;
            public bool IsWordEnabled
            {
                get { return isWordEnabled; }
                set
                {
                    if (!canSet)
                        throw new InvalidOperationException("cant_set");

                    if (value != isWordEnabled)
                    {
                        isWordEnabled = value;
                        isIsWordEnabledAltered = true;
                    }
                }
            }

            public bool isWordAltered { get; private set; }
            private string word = string.Empty;
            public string Word
            {
                get { return word; }
                set
                {
                    if (!canSet)
                        throw new InvalidOperationException("cant_set");

                    if (value != word)
                    {
                        word = value;
                        isWordAltered = true;
                    }
                }
            }

            public bool isIsRegularExpressionEnabledAltered { get; private set; }
            private bool isRegularExpressionEnabled = false;
            public bool IsRegularExpressionEnabled
            {
                get { return isRegularExpressionEnabled; }
                set
                {
                    if (!canSet)
                        throw new InvalidOperationException("cant_set");

                    if (value != isRegularExpressionEnabled)
                    {
                        isRegularExpressionEnabled = value;
                        isIsRegularExpressionEnabledAltered = true;
                    }
                }
            }

            public bool isRegularExpressionAltered { get; private set; }
            private string regularExpression = string.Empty;
            public string RegularExpression
            {
                get { return regularExpression; }
                set
                {
                    if (!canSet)
                        throw new InvalidOperationException("cant_set");

                    if (value != regularExpression)
                    {
                        regularExpression = value;
                        isRegularExpressionAltered = true;
                    }
                }
            }

            public bool isIsLevelEnabledAltered { get; private set; }
            private bool isLevelEnabled = false;
            public bool IsLevelEnabled
            {
                get { return isLevelEnabled; }
                set
                {
                    if (!canSet)
                        throw new InvalidOperationException("cant_set");

                    if (value != isLevelEnabled)
                    {
                        isLevelEnabled = value;
                        isIsLevelEnabledAltered = true;
                    }
                }
            }

            public bool isMinimalLevelAltered { get; private set; }
            private int minimalLevel = 0;
            public int MinimalLevel
            {
                get { return minimalLevel; }
                set
                {
                    if (!canSet)
                        throw new InvalidOperationException("cant_set");

                    if (value != minimalLevel)
                    {
                        minimalLevel = value;
                        isMinimalLevelAltered = true;
                    }
                }
            }

            public bool isMaximalLevelAltered { get; private set; }
            private int maximalLevel = 5;
            public int MaximalLevel
            {
                get { return maximalLevel; }
                set
                {
                    if (!canSet)
                        throw new InvalidOperationException("cant_set");

                    if (value != maximalLevel)
                    {
                        maximalLevel = value;
                        isMaximalLevelAltered = true;
                    }
                }
            }

            public bool isIsKindEnabledAltered { get; private set; }
            private bool isKindEnabled = false;
            public bool IsKindEnabled
            {
                get { return isKindEnabled; }
                set
                {
                    if (!canSet)
                        throw new InvalidOperationException("cant_set");

                    if (value != isKindEnabled)
                    {
                        isKindEnabled = value;
                        isIsKindEnabledAltered = true;
                    }
                }
            }

            //<未改良>複数指定
            public bool isKindAltered { get; private set; }
            private LogData.LogKind kind = LogData.LogKind.error;
            public LogData.LogKind Kind
            {
                get { return kind; }
                set
                {
                    if (!canSet)
                        throw new InvalidOperationException("cant_set");

                    if (value != kind)
                    {
                        kind = value;
                        isKindAltered = true;
                    }
                }
            }

            public bool isIsGroundEnabledAltered { get; private set; }
            private bool isGroundEnabled = false;
            public bool IsGroundEnabled
            {
                get { return isGroundEnabled; }
                set
                {
                    if (!canSet)
                        throw new InvalidOperationException("cant_set");

                    if (value != isGroundEnabled)
                    {
                        isGroundEnabled = value;
                        isIsGroundEnabledAltered = true;
                    }
                }
            }

            public bool isGroundAltered { get; private set; }
            //<未改良>複数指定
            private LogData.LogGround ground = LogData.LogGround.core;
            public LogData.LogGround Ground
            {
                get { return ground; }
                set
                {
                    if (!canSet)
                        throw new InvalidOperationException("cant_set");

                    if (value != ground)
                    {
                        ground = value;
                        isGroundAltered = true;
                    }
                }
            }

            protected override string XmlName { get { return "LogFilter"; } }
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

            public override void StartSetting()
            {
                base.StartSetting();

                isNameAltered = false;
                isIsEnabledAltered = false;
                isIsWordEnabledAltered = false;
                isWordAltered = false;
                isIsRegularExpressionEnabledAltered = false;
                isRegularExpressionAltered = false;
                isIsLevelEnabledAltered = false;
                isMinimalLevelAltered = false;
                isMaximalLevelAltered = false;
                isIsKindEnabledAltered = false;
                isKindAltered = false;
                isIsGroundEnabledAltered = false;
                isGroundAltered = false;
            }
        }

        public class ProgramStatus : SHAREDDATA
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

            protected override Func<ReaderWriter, IEnumerable<MainDataInfomation>> StreamInfo
            {
                get
                {
                    return (msrw) => new MainDataInfomation[]{
                        new MainDataInfomation(typeof(bool), () => isFirst, (o) => isFirst = (bool)o),
                    };
                }
            }
        }

        private static string[] langResource;
        private static Dictionary<string, Func<string>> taskNames;
        private static Dictionary<string, Func<string>> taskDescriptions;
        private static Dictionary<string, LogData.LogGround> logGrounds;
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

        public static LogData.LogGround GetLogGround(string rawMessage)
        {
            return logGrounds.GetValue(rawMessage, LogData.LogGround.other);
        }

        public static string GetLogMessage(string rawMessage, string[] arguments)
        {
            return logMessages.GetValue(rawMessage, (arg) => rawMessage)(arguments);
        }

        public static string GetExceptionMessage(string rawMessage)
        {
            return exceptionMessages.GetValue(rawMessage, () => rawMessage)();
        }

        private static Assembly entryAssembly = Assembly.GetEntryAssembly();
        private static string basepath = Path.GetDirectoryName(entryAssembly.Location);
        private static string entryAssemblyFileName = Path.GetFileName(entryAssembly.Location);
        private static AssemblyName entryAssemblyName = entryAssembly.GetName();
        private static Version entryAssemblyVersion = entryAssemblyName.Version;

        private static string appname = "CREA2014";
        private static int verMaj = entryAssemblyVersion.Major;
        private static int verMin = entryAssemblyVersion.Minor;
        private static int verMMin = entryAssemblyVersion.Build;
        private static string verS = "α";
        private static int verR = 1; //リリース番号（リリース毎に増やす番号）
        private static int verC = 46; //コミット番号（コミット毎に増やす番号）
        private static string version = string.Join(".", verMaj.ToString(), verMin.ToString(), verMMin.ToString()) + "(" + verS + ")" + "(" + verR.ToString() + ")" + "(" + verC.ToString() + ")";
        private static string appnameWithVersion = string.Join(" ", appname, version);

        [STAThread]
        public static void Main(string[] args)
        {
            string argExtract = "extract";
            string argCopy = "copy";

            if (args.Length < 1)
            {
                AppDomain appDomain = AppDomain.CreateDomain(argExtract);
                appDomain.ExecuteAssembly(entryAssembly.Location, new string[] { argExtract });
                AppDomain.Unload(appDomain);

                string exeDirectoryName = "exe";

                //未だ存在しない（抽出が行われていない）アセンブリを読み込む可能性のないようにしなければならない
                //本来的なMainの処理を別メソッドにすれば問題ないと思われる
                Main2(exeDirectoryName, (data, assemblyVersion) =>
                {
                    string newAssemblyDiretory = Path.Combine(basepath, exeDirectoryName);
                    string newAssemblyPath = Path.Combine(newAssemblyDiretory, entryAssemblyFileName);

                    if (!Directory.Exists(newAssemblyDiretory))
                        Directory.CreateDirectory(newAssemblyDiretory);
                    File.WriteAllBytes(newAssemblyPath, data);

                    Assembly newAssembly = Assembly.LoadFrom(newAssemblyPath);
                    Version newAssemblyVersion = newAssembly.GetName().Version;

                    bool pfWasVerified = false;
                    bool ret = API.StrongNameSignatureVerificationEx(newAssemblyPath, false, ref pfWasVerified);

                    bool isValid = newAssemblyVersion.Major == assemblyVersion.Major && newAssemblyVersion.Minor == assemblyVersion.Minor && newAssemblyVersion.Build == assemblyVersion.Build && newAssemblyVersion.Revision == assemblyVersion.Revision && pfWasVerified && ret && newAssembly.GetName().GetPublicKeyToken().BytesEquals(entryAssembly.GetName().GetPublicKeyToken());

                    if (isValid)
                        Process.Start(newAssemblyPath, string.Join(" ", argCopy, Process.GetCurrentProcess().Id.ToString()));

                    return isValid;
                });
            }
            else if (args.Length > 0)
                if (args[0] == argCopy)
                {
                    API.AllocConsole();

                    Process process = null;
                    try
                    {
                        process = Process.GetProcessById(int.Parse(args[1]));
                    }
                    catch (ArgumentException) { }
                    if (process != null)
                        while (!process.HasExited)
                            Thread.Sleep(100);

                    string fromLocation = entryAssembly.Location;
                    string toLocation = Path.Combine(Path.GetDirectoryName(basepath), entryAssemblyFileName);
                    File.Copy(fromLocation, toLocation, true);

                    Process.Start(toLocation);

                    API.FreeConsole();
                }
                else if (args[0] == argExtract)
                {
                    Action<string, string> _CopyComponent = (location, filename) =>
                    {
                        using (Stream stream = entryAssembly.GetManifestResourceStream(string.Join(".", location, filename)))
                        {
                            byte[] bytes = new byte[stream.Length];
                            stream.Read(bytes, 0, bytes.Length);

                            File.WriteAllBytes(filename, bytes);
                        }
                    };

                    string[] filenames = new string[]
                    {
                        "Lisence.txt", 
                        "HashLib.dll", 
                        "log4net.dll", 
                        "Newtonsoft.Json.dll", 
                        "SuperSocket.Common.dll", 
                        "SuperSocket.SocketBase.dll", 
                        "SuperSocket.SocketEngine.dll", 
                        "SuperWebSocket.dll", 
                        "vtortola.WebSockets.dll", 
                        "vtortola.WebSockets.Rfc6455.dll", 
                    };

                    foreach (var filename in filenames)
                        _CopyComponent(string.Join(".", appname, "Component"), filename);
                }
                else
                    throw new NotSupportedException("not_supported_argument");
        }

        public static void Main2(string exeDirectoryName, Func<byte[], Version, bool> _UpVersion)
        {
            string lisenceTextFilename = "Lisence.txt";
            string pstatusFilename = "ps";

            string pstatusFilepath = Path.Combine(basepath, pstatusFilename);
            string exeDirectoryPath = Path.Combine(basepath, exeDirectoryName);

            ProgramSettings psettings = new ProgramSettings();
            ProgramStatus pstatus = new ProgramStatus();

            Logger logger = null;
            Tasker tasker = new Tasker();
            int taskNumber = 0;

            Core core = null;

            if (File.Exists(pstatusFilepath))
                pstatus.FromBinary(File.ReadAllBytes(pstatusFilepath));

            if (psettings.Culture == "ja-JP")
                using (Stream stream = entryAssembly.GetManifestResourceStream(@"CREA2014.Resources.langResouece_ja-JP.txt"))
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

            logGrounds = new Dictionary<string, LogData.LogGround>()
            {
                {"exist_same_name_account_holder", LogData.LogGround.data},
                {"outbound_chennel", LogData.LogGround.networkBase},
                {"inbound_channel", LogData.LogGround.networkBase},
                {"inbound_channels", LogData.LogGround.networkBase},
                {"socket_channel_write", LogData.LogGround.networkBase},
                {"socket_channel_read", LogData.LogGround.networkBase},
                {"ric", LogData.LogGround.networkBase},
                {"roc", LogData.LogGround.networkBase},
                {"inbound_session", LogData.LogGround.networkBase},
                {"outbound_session", LogData.LogGround.networkBase},
                {"connect", LogData.LogGround.creaNetworkBase},
                {"diffuse", LogData.LogGround.creaNetworkBase},
                {"keep_conn", LogData.LogGround.creaNetworkBase},
                {"task", LogData.LogGround.foundation},
                {"task_aborted", LogData.LogGround.foundation},
                {"all_tasks_aborted", LogData.LogGround.foundation},
                {"upnp_not_found", LogData.LogGround.creaNetworkBase},
                {"port0", LogData.LogGround.creaNetworkBase},
                {"rsa_key_cant_create", LogData.LogGround.creaNetworkBase},
                {"rsa_key_create", LogData.LogGround.creaNetworkBase},
                {"upnp_ipaddress", LogData.LogGround.creaNetworkBase},
                {"server_started", LogData.LogGround.creaNetworkBase},
                {"server_ended", LogData.LogGround.creaNetworkBase},
                {"server_restart", LogData.LogGround.creaNetworkBase},
                {"cant_decode_fni", LogData.LogGround.creaNetworkBase},
                {"aite_wrong_node_info", LogData.LogGround.creaNetwork},
                {"aite_wrong_network", LogData.LogGround.creaNetwork},
                {"aite_already_connected", LogData.LogGround.creaNetwork},
                {"wrong_network", LogData.LogGround.creaNetwork},
                {"already_connected", LogData.LogGround.creaNetwork}, 
                {"keep_conn_completed", LogData.LogGround.creaNetwork},
                {"find_table_already_added", LogData.LogGround.cremlia}, 
                {"find_nodes", LogData.LogGround.cremlia},
                {"my_node_info", LogData.LogGround.cremlia},
                {"blk_too_old", LogData.LogGround.data},
                {"blk_too_new", LogData.LogGround.data},
                {"blk_already_existed", LogData.LogGround.data},
                {"blk_mismatch_genesis_block_hash", LogData.LogGround.data},
                {"blk_not_connected", LogData.LogGround.data},
                {"blk_main_not_connected", LogData.LogGround.data},
                {"blk_too_deep", LogData.LogGround.data},
            };

            logMessages = new Dictionary<string, Func<string[], string>>() {
                {"test", (args) => "\'"},
                {"exist_same_name_account_holder", (args) => "同名の口座名義人が存在します。".Multilanguage(93)},
                {"outbound_chennel", (args) => "エラーが発生しました：outbound_chennel".Multilanguage(94)},
                {"inbound_channel", (args) => "エラーが発生しました：inbound_channel".Multilanguage(95)},
                {"inbound_channels", (args) => "エラーが発生しました：inbound_channels".Multilanguage(113)},
                {"socket_channel_write", (args) => "エラーが発生しました：socket_channel_write".Multilanguage(114)},
                {"socket_channel_read", (args) => "エラーが発生しました：socket_channel_read".Multilanguage(115)},
                {"ric", (args) => "エラーが発生しました：ric".Multilanguage(121)},
                {"roc", (args) => "エラーが発生しました：roc".Multilanguage(122)},
                {"inbound_session", (args) => "エラーが発生しました：inbound_session".Multilanguage(123)},
                {"outbound_session", (args) => "エラーが発生しました：outbound_session".Multilanguage(124)},
                {"connect", (args) => "接続失敗".Multilanguage(170)},
                {"diffuse", (args) => "エラーが発生しました：diffuse".Multilanguage(125)},
                {"keep_conn", (args) => "エラーが発生しました：keep_conn".Multilanguage(126)},
                {"task", (args) => "エラーが発生しました：task".Multilanguage(96)},
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
                {"cant_decode_fni", (args) => "初期ノード情報を解読できませんでした。".Multilanguage(171)},
                {"aite_wrong_node_info", (args) => string.Format("ノードが申告したIPアドレスと実際のIPアドレスが異なります：{0}:{1}".Multilanguage(107), args[0], args[1])},
                {"aite_wrong_network", (args) => string.Format("ノードが所属しているネットワークが異なります：{0}:{1}".Multilanguage(108), args[0], args[1])},
                {"aite_already_connected", (args) => string.Format("既に接続しているノードから再び接続が要求されました：{0}:{1}".Multilanguage(109), args[0], args[1])},
                {"wrong_network", (args) => string.Format("別のネットワークに所属しているノードに接続しました：{0}:{1}".Multilanguage(110), args[0], args[1])},
                {"already_connected", (args) => string.Format("既に接続しているノードに接続しました：{0}:{1}".Multilanguage(111), args[0], args[1])}, 
                {"keep_conn_completed", (args) => "常時接続が確立しました。".Multilanguage(112)},
                {"find_table_already_added", (args) => string.Format(string.Join(Environment.NewLine, "DHTの検索リスト項目は既に登録されています。".Multilanguage(116), "距離：{0}".Multilanguage(117), "ノード1：{1}".Multilanguage(118), "ノード2：{2}".Multilanguage(119)), args[0], args[1], args[3])}, 
                {"find_nodes", (args) => string.Format("{0}個の近接ノードを発見しました。".Multilanguage(120), args[0])},
                {"my_node_info", (args) => "自分自身のノード情報です。".Multilanguage(127)},
                {"blk_too_old", (args) => "古過ぎるブロックです。".Multilanguage(128)},
                {"blk_too_new", (args) => "新し過ぎるブロックです。".Multilanguage(129)},
                {"blk_already_existed", (args) => "既に存在するブロックです。".Multilanguage(130)},
                {"blk_mismatch_genesis_block_hash", (args) => "直前のブロックは起源ブロックでなければなりません。".Multilanguage(131)},
                {"blk_not_connected", (args) => "接続されていないブロックです。".Multilanguage(132)},
                {"blk_main_not_connected", (args) => "接続されていない主ブロックです。".Multilanguage(133)},
                {"blk_too_deep", (args) => "深過ぎるブロックです。".Multilanguage(134)},
                {"hash_rate", (args) => string.Format("要約値計算速度：{0}hash/s".Multilanguage(137), args[0])},
                {"found_block", (args) => "新しいブロックを発見しました。".Multilanguage(138)},
                {"difficulty", (args) => string.Format("難易度：{0}".Multilanguage(169), args[0])},
                {"alredy_processed_tx", (args) => "処理済みの取引を再び処理しようとしました。".Multilanguage(172)},
                {"alredy_processed_chat", (args) => "処理済みのチャット発言を再び処理しようとしました。".Multilanguage(173)},
                {"fail_network_interface", (args) => "ネットワークインターフェイスの取得に失敗しました。".Multilanguage(174)},
                {"succeed_network_interface", (args) => string.Format("ネットワークインターフェイスの取得に成功しました：{0}".Multilanguage(175), args[0])},
                {"fail_open_port", (args) => "ポートの開放に失敗しました。".Multilanguage(176)},
                {"succeed_open_port", (args) => "ポートの開放に成功しました。".Multilanguage(177)},
                {"fail_get_global_ip", (args) => "グローバルIPアドレスの取得に失敗しました。".Multilanguage(178)},
                {"succeed_get_global_ip", (args) => string.Format("グローバルIPアドレスの取得に成功しました：{0}".Multilanguage(179), args[0])},
                {"fail_msearch", (args) => string.Format("MSEARCH失敗：{0}".Multilanguage(180), args[0])},
                {"succeed_msearch", (args) => string.Format("MSEARCH成功：{0}".Multilanguage(181), args[0])},
                {"fail_device_description", (args) => string.Format("機器の説明失敗：{0}".Multilanguage(182), args[0])},
                {"succeed_device_description", (args) => string.Format("機器の説明成功：{0}".Multilanguage(183), args[0])},
                {"fail_soap", (args) => string.Format("SOAP失敗：{0}".Multilanguage(184), args[0])},
                {"succeed_soap", (args) => string.Format("SOAP成功：{0}".Multilanguage(185), args[0])},
                {"soap", (args) => string.Format("SOAP：{0}".Multilanguage(186), args[0])},
                {"start_open_port_search", (args) => "開放ポートの検索を開始しました。".Multilanguage(187)},
                {"already_port_opened", (args) => "既にポートが開放されています。".Multilanguage(188)},
                {"generic_port_mapping_entry", (args) => string.Format("開放ポート：{0}".Multilanguage(189), args[0])},
                {"add_fni", (args) => string.Format("初期ノード情報を追加しました：{0}".Multilanguage(190), args[0])},
                {"fail_upnp", (args) => "UPnP機器が見付かりませんでした。".Multilanguage(192)},
                {"not_server", (args) => "グローバルIPアドレスを取得できなかったため、サーバは起動されませんでした。".Multilanguage(193)},
                {"register_fni", (args) => "初期ノード情報を登録しました。".Multilanguage(194)},
                {"get_fnis", (args) => "初期ノード情報を取得しました。".Multilanguage(195)},
                {"keep_connection_fnis_zero", (args) => "初期ノード情報を取得できなかったため、常時接続を開始できませんでした。".Multilanguage(196)},
                {"keep_connection_nis_zero", (args) => "初期ノードからノード情報を取得できなかったため、常時接続を開始できませんでした。".Multilanguage(197)},
                {"update_keep_conn", (args) => "常時接続更新".Multilanguage(215)},
            };

            exceptionMessages = new Dictionary<string, Func<string>>() {
                {"already_starting", () => string.Format("{0}は既に起動しています。".Multilanguage(0), appname)},
                {"ie_not_existing", () => string.Format("{0}の動作には Internet Explorer 10 以上が必要です。".Multilanguage(1), appname)},
                {"ie_too_old", () => string.Format("{0}の動作には Internet Explorer 10 以上が必要です。".Multilanguage(2), appname)},
                {"require_administrator", () => string.Format("{0}は管理者として実行する必要があります。".Multilanguage(3), appname)},
                {"lisence_text_not_found", () => "ソフトウェア使用許諾契約書が見付かりません。".Multilanguage(90)},
                {"web_server_data", () => "内部ウェブサーバデータが存在しません。".Multilanguage(91)},
                {"wss_command", () => "内部ウェブソケット命令が存在しません。".Multilanguage(92)},
                {"http_listener_not_supported", () => "HTTP Listenerに対応していません。".Multilanguage(191)},
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

                File.WriteAllBytes(pstatusFilepath, pstatus.ToBinary());

                Environment.Exit(0);
            };

            List<UnhandledExceptionEventHandler> unhandledExceptionEventHandlers = new List<UnhandledExceptionEventHandler>();
            unhandledExceptionEventHandlers.Add((sender, e) =>
            {
                //2014/08/27
                //C#でeがExceptionでないことはあり得ないと思われる
                Exception ex = e.ExceptionObject as Exception;
                if (ex != null)
                    _OnException(ex, ExceptionKind.unhandled);
            });

            AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
            {
                foreach (var unhandledExceptionEventHandler in unhandledExceptionEventHandlers)
                    unhandledExceptionEventHandler(sender, e);
            };
            //<未実装>例外発生状況の記録など？
            AppDomain.CurrentDomain.FirstChanceException += (sender, e) =>
            {
            };

            //<未実装>各種統計情報の取得
            AppDomain.MonitoringIsEnabled = true;

            Thread.CurrentThread.CurrentCulture = new CultureInfo(psettings.Culture);
            Thread.CurrentThread.CurrentUICulture = new CultureInfo(psettings.Culture);

            TestApplication testApplication;
            bool isCanRunMultiple;
#if TEST
            testApplication = null;
            isCanRunMultiple = true;
            //testApplication = new CreaNetworkLocalTestApplication(logger);
#else
                testApplication = null;
            isCanRunMultiple = false;
#endif

            Action _Start = () =>
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

                Process currentProcess = Process.GetCurrentProcess();
                string fileName = Path.GetFileName(currentProcess.MainModule.FileName);
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

                //2014/10/01
                //設定しなければならない設定値などが設定されているかを確認する
                if (TransactionalBlock.foundationPubKeyHash == null)
                    throw new ApplicationException("not_setted_foundation_pubkey_hash");

                if (testApplication == null || testApplication.IsUseCore)
                {
                    core = new Core(basepath, verC, appnameWithVersion, psettings);
                    core.StartSystem();
                }

                App app = new App();

                List<DispatcherUnhandledExceptionEventHandler> dispatcherUnhandledExceptionEventHandlers = new List<DispatcherUnhandledExceptionEventHandler>();
                dispatcherUnhandledExceptionEventHandlers.Add((sender, e) => _OnException(e.Exception, ExceptionKind.wpf));

                app.DispatcherUnhandledException += (sender, e) =>
                {
                    foreach (var dispatcherUnhandledExceptionEventHandler in dispatcherUnhandledExceptionEventHandlers)
                        dispatcherUnhandledExceptionEventHandler(sender, e);
                };
                app.Startup += (sender, e) =>
                {
                    if (testApplication == null)
                    {
                        MainWindow mw = new MainWindow(core, logger, psettings, pstatus, appname, version, appnameWithVersion, lisenceTextFilename, entryAssembly, entryAssemblyName, basepath, currentProcess, _OnException, _UpVersion, unhandledExceptionEventHandlers, dispatcherUnhandledExceptionEventHandlers);
                        mw.Show();
                    }
                    else
                        testApplication.Execute();
                };
                app.InitializeComponent();
                app.Run();

                if (testApplication == null || testApplication.IsUseCore)
                    core.EndSystem();

                psettings.Save();

                File.WriteAllBytes(pstatusFilepath, pstatus.ToBinary());
            };

            if (!isCanRunMultiple)
            {
                // Windows 2000（NT 5.0）以降のみグローバル・ミューテックス利用可
                string appNameMutex = appname + " by Piz Yumina";
                OperatingSystem os = Environment.OSVersion;
                if ((os.Platform == PlatformID.Win32NT) && (os.Version.Major >= 5))
                    appNameMutex = @"Global\" + appNameMutex;

                Mutex mutex;
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
                    _Start();

                    mutex.ReleaseMutex();
                }
                else
                {
                    Process preveousProcess = null;
                    Process currentProcess = Process.GetCurrentProcess();
                    Process[] allProcesses = Process.GetProcessesByName(currentProcess.ProcessName);

                    foreach (var p in allProcesses)
                        if (p.Id != currentProcess.Id && string.Compare(p.MainModule.FileName, currentProcess.MainModule.FileName, true) == 0)
                        {
                            preveousProcess = p;
                            break;
                        }

                    if (preveousProcess != null && preveousProcess.MainWindowHandle != IntPtr.Zero)
                    {
                        if (API.IsIconic(preveousProcess.MainWindowHandle))
                            API.ShowWindowAsync(preveousProcess.MainWindowHandle, API.SW_RESTORE);

                        API.SetForegroundWindow(preveousProcess.MainWindowHandle);
                    }
                    else
                        throw new ApplicationException("already_starting");
                }

                mutex.Close();
            }
            else
                _Start();
        }
    }

    #endregion
}