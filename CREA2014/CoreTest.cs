namespace New
{
    using CREA2014;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Reflection;
    using System.Linq;
    using System.Diagnostics;
    using System.Threading;

    public static class BlockChainTest
    {
        //UtxoDBのテスト
        public static void Test1()
        {
            string basepath = System.IO.Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);
            UtxoDB utxodb = new UtxoDB(basepath);
            string path = utxodb.GetPath();

            if (File.Exists(path))
                File.Delete(path);

            utxodb.Open();

            byte[] emptyBytes = utxodb.GetUtxoData(0);

            if (emptyBytes.Length != 0)
                throw new Exception("test1_1");

            byte[] utxoBytesIn1 = new byte[1024];
            for (int i = 0; i < utxoBytesIn1.Length; i++)
                utxoBytesIn1[i] = (byte)256.RandomNum();

            int overallLangth = utxoBytesIn1.Length + 4;

            long position1 = utxodb.AddUtxoData(utxoBytesIn1);

            if (position1 != 0)
                throw new Exception("test1_2");

            long position2 = utxodb.AddUtxoData(utxoBytesIn1);

            if (position2 != overallLangth)
                throw new Exception("test1_3");

            byte[] utxoBytesOut1 = utxodb.GetUtxoData(position1);

            if (!utxoBytesIn1.BytesEquals(utxoBytesOut1))
                throw new Exception("test1_4");

            byte[] utxoBytesOut2 = utxodb.GetUtxoData(position2);

            if (!utxoBytesIn1.BytesEquals(utxoBytesOut2))
                throw new Exception("test1_5");

            byte[] utxoBytesIn2 = new byte[utxoBytesIn1.Length];
            for (int i = 0; i < utxoBytesIn2.Length; i++)
                utxoBytesIn2[i] = (byte)256.RandomNum();

            utxodb.UpdateUtxoData(position1, utxoBytesIn2);

            byte[] utxoBytesOut3 = utxodb.GetUtxoData(position1);

            if (!utxoBytesIn2.BytesEquals(utxoBytesOut3))
                throw new Exception("test1_6");

            byte[] utxoBytesOut4 = utxodb.GetUtxoData(position2);

            if (!utxoBytesIn1.BytesEquals(utxoBytesOut4))
                throw new Exception("test1_7");

            utxodb.UpdateUtxoData(position2, utxoBytesIn2);

            byte[] utxoBytesOut5 = utxodb.GetUtxoData(position1);

            if (!utxoBytesIn2.BytesEquals(utxoBytesOut5))
                throw new Exception("test1_8");

            byte[] utxoBytesOut6 = utxodb.GetUtxoData(position2);

            if (!utxoBytesIn2.BytesEquals(utxoBytesOut6))
                throw new Exception("test1_9");

            byte[] emptyBytes2 = utxodb.GetUtxoData(overallLangth * 2);

            utxodb.Close();

            Console.WriteLine("test1_succeeded");
        }

        //UtxoFileItemのテスト
        public static void Test2()
        {
            int size1 = 16;

            UtxoFileItem ufiIn = new UtxoFileItem(size1);

            for (int i = 0; i < ufiIn.utxos.Length; i++)
            {
                if (ufiIn.utxos[i].blockIndex != 0)
                    throw new Exception("test2_11");
                if (ufiIn.utxos[i].txIndex != 0)
                    throw new Exception("test2_12");
                if (ufiIn.utxos[i].txOutIndex != 0)
                    throw new Exception("test2_13");
                if (ufiIn.utxos[i].amount.rawAmount != 0)
                    throw new Exception("test2_14");
            }

            if (ufiIn.utxos.Length != size1)
                throw new Exception("test2_1");

            if (ufiIn.Size != size1)
                throw new Exception("test2_2");

            if (ufiIn.nextPosition != -1)
                throw new Exception("test2_3");

            for (int i = 0; i < ufiIn.utxos.Length; i++)
                ufiIn.utxos[i] = new Utxo(65536.RandomNum(), 65536.RandomNum(), 65536.RandomNum(), new Creacoin(65536.RandomNum()));

            byte[] ufiBytes = ufiIn.ToBinary();

            if (ufiBytes.Length != 397)
                throw new Exception("test2_16");

            UtxoFileItem ufiOut = SHAREDDATA.FromBinary<UtxoFileItem>(ufiBytes);

            if (ufiIn.utxos.Length != ufiOut.utxos.Length)
                throw new Exception("test2_4");

            if (ufiIn.Size != ufiOut.Size)
                throw new Exception("test2_5");

            if (ufiIn.nextPosition != ufiOut.nextPosition)
                throw new Exception("test2_6");

            for (int i = 0; i < ufiIn.utxos.Length; i++)
            {
                if (ufiIn.utxos[i].blockIndex != ufiOut.utxos[i].blockIndex)
                    throw new Exception("test2_7");
                if (ufiIn.utxos[i].txIndex != ufiOut.utxos[i].txIndex)
                    throw new Exception("test2_8");
                if (ufiIn.utxos[i].txOutIndex != ufiOut.utxos[i].txOutIndex)
                    throw new Exception("test2_9");
                if (ufiIn.utxos[i].amount.rawAmount != ufiOut.utxos[i].amount.rawAmount)
                    throw new Exception("test2_10");
            }

            for (int i = 0; i < ufiIn.utxos.Length; i++)
                ufiOut.utxos[i] = new Utxo(65536.RandomNum(), 65536.RandomNum(), 65536.RandomNum(), new Creacoin(65536.RandomNum()));

            byte[] ufiBytes2 = ufiOut.ToBinary();

            if (ufiBytes.Length != ufiBytes2.Length)
                throw new Exception("test2_15");

            Console.WriteLine("test2_succeeded");
        }

        //UtxoFilePointersのテスト
        public static void Test3()
        {
            UtxoFilePointers ufp = new UtxoFilePointers();

            Dictionary<Sha256Ripemd160Hash, long> afps = ufp.GetAll();

            if (afps.Count != 0)
                throw new Exception("test3_1");

            Sha256Ripemd160Hash hash1 = new Sha256Ripemd160Hash(new byte[] { (byte)256.RandomNum() });
            Sha256Ripemd160Hash hash2 = new Sha256Ripemd160Hash(new byte[] { (byte)256.RandomNum() });
            Sha256Ripemd160Hash hash3 = new Sha256Ripemd160Hash(new byte[] { (byte)256.RandomNum() });

            long position1 = 56636.RandomNum();
            long position2 = 56636.RandomNum();
            long position3 = 56636.RandomNum();

            long? positionNull = ufp.Get(hash1);

            if (positionNull.HasValue)
                throw new Exception("test3_2");

            ufp.Add(hash1, position1);
            ufp.Add(hash2, position2);
            ufp.Add(hash3, position3);

            bool flag = false;
            try
            {
                ufp.Add(hash1, position1);
            }
            catch (InvalidOperationException)
            {
                flag = true;
            }
            if (!flag)
                throw new Exception("test3_3");

            Dictionary<Sha256Ripemd160Hash, long> afps2 = ufp.GetAll();

            if (afps2.Count != 3)
                throw new Exception("test3_4");
            if (!afps2.Keys.Contains(hash1))
                throw new Exception("test3_5");
            if (!afps2.Keys.Contains(hash2))
                throw new Exception("test3_6");
            if (!afps2.Keys.Contains(hash3))
                throw new Exception("test3_7");
            if (afps2[hash1] != position1)
                throw new Exception("test3_8");
            if (afps2[hash2] != position2)
                throw new Exception("test3_9");
            if (afps2[hash3] != position3)
                throw new Exception("test3_10");

            long? position1Out = ufp.Get(hash1);
            long? position2Out = ufp.Get(hash2);
            long? position3Out = ufp.Get(hash3);

            if (!position1Out.HasValue || position1Out.Value != position1)
                throw new Exception("test3_11");
            if (!position2Out.HasValue || position2Out.Value != position2)
                throw new Exception("test3_12");
            if (!position3Out.HasValue || position3Out.Value != position3)
                throw new Exception("test3_13");

            ufp.Remove(hash1);

            bool flag2 = false;
            try
            {
                ufp.Remove(hash1);
            }
            catch (InvalidOperationException)
            {
                flag2 = true;
            }
            if (!flag2)
                throw new Exception("test3_14");

            Dictionary<Sha256Ripemd160Hash, long> afps3 = ufp.GetAll();

            if (afps3.Count != 2)
                throw new Exception("test3_15");

            ufp.Update(hash2, position1);

            long? position1Out2 = ufp.Get(hash2);

            if (!position1Out2.HasValue || position1Out2.Value != position1)
                throw new Exception("test3_16");

            bool flag3 = false;
            try
            {
                ufp.Update(hash1, position2);
            }
            catch (InvalidOperationException)
            {
                flag3 = true;
            }
            if (!flag3)
                throw new Exception("test3_17");

            Dictionary<Sha256Ripemd160Hash, long> afps4 = ufp.GetAll();

            if (afps4.Count != 2)
                throw new Exception("test3_18");

            ufp.AddOrUpdate(hash2, position3);

            long? position1Out3 = ufp.Get(hash2);

            if (!position1Out3.HasValue || position1Out3.Value != position3)
                throw new Exception("test3_19");

            Dictionary<Sha256Ripemd160Hash, long> afps5 = ufp.GetAll();

            if (afps5.Count != 2)
                throw new Exception("test3_20");

            ufp.AddOrUpdate(hash1, position3);

            long? position1Out4 = ufp.Get(hash1);

            if (!position1Out4.HasValue || position1Out4.Value != position3)
                throw new Exception("test3_21");

            Dictionary<Sha256Ripemd160Hash, long> afps6 = ufp.GetAll();

            if (afps5.Count != 3)
                throw new Exception("test3_22");

            byte[] ufpBytes = ufp.ToBinary();

            UtxoFilePointers ufp2 = SHAREDDATA.FromBinary<UtxoFilePointers>(ufpBytes);

            Dictionary<Sha256Ripemd160Hash, long> afps7 = ufp2.GetAll();

            if (afps6.Count != afps7.Count)
                throw new Exception("test3_23");

            foreach (var key in afps6.Keys)
            {
                if (!afps7.Keys.Contains(key))
                    throw new Exception("test3_24");
                if (afps6[key] != afps7[key])
                    throw new Exception("test3_25");
            }

            Console.WriteLine("test3_succeeded");
        }

        //UtxoManagerのテスト1
        public static void Test4()
        {
            string basepath = System.IO.Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);

            UtxoFileAccessDB ufadb = new UtxoFileAccessDB(basepath);
            string ufadbPath = ufadb.GetPath();

            if (File.Exists(ufadbPath))
                File.Delete(ufadbPath);

            UtxoFilePointersDB ufpdb = new UtxoFilePointersDB(basepath);
            string ufpdbPath = ufpdb.GetPath();

            if (File.Exists(ufpdbPath))
                File.Delete(ufpdbPath);

            UtxoFilePointersTempDB ufptempdb = new UtxoFilePointersTempDB(basepath);
            string ufptempdbPath = ufptempdb.GetPath();

            if (File.Exists(ufptempdbPath))
                File.Delete(ufptempdbPath);

            UtxoDB utxodb = new UtxoDB(basepath);
            string utxodbPath = utxodb.GetPath();

            if (File.Exists(utxodbPath))
                File.Delete(utxodbPath);

            UtxoManager utxom = new UtxoManager(ufadb, ufpdb, ufptempdb, utxodb);

            if (File.Exists(ufadbPath))
                throw new Exception("test4_9");

            utxodb.Open();

            Sha256Ripemd160Hash address1 = new Sha256Ripemd160Hash(new byte[] { 0 });
            Sha256Ripemd160Hash address2 = new Sha256Ripemd160Hash(new byte[] { 1 });
            Sha256Ripemd160Hash address3 = new Sha256Ripemd160Hash(new byte[] { 2 });

            Utxo utxoNull = utxom.FindUtxo(address1, 65536.RandomNum(), 65536.RandomNum(), 65536.RandomNum());

            if (utxoNull != null)
                throw new Exception("test4_1");

            long bi1 = 65536.RandomNum();
            int ti1 = 65536.RandomNum();
            int toi1 = 65536.RandomNum();
            Creacoin c1 = new Creacoin(65536.RandomNum());

            long bi2 = 65536.RandomNum();
            int ti2 = 65536.RandomNum();
            int toi2 = 65536.RandomNum();
            Creacoin c2 = new Creacoin(65536.RandomNum());

            long bi3 = 65536.RandomNum();
            int ti3 = 65536.RandomNum();
            int toi3 = 65536.RandomNum();
            Creacoin c3 = new Creacoin(65536.RandomNum());

            utxom.AddUtxo(address1, bi1, ti1, toi1, c1);
            utxom.AddUtxo(address2, bi2, ti1, toi1, c1);
            utxom.AddUtxo(address3, bi3, ti1, toi1, c1);
            utxom.AddUtxo(address1, bi1, ti2, toi2, c2);
            utxom.AddUtxo(address2, bi2, ti2, toi2, c2);
            utxom.AddUtxo(address3, bi3, ti2, toi2, c2);
            utxom.AddUtxo(address1, bi1, ti3, toi3, c3);
            utxom.AddUtxo(address2, bi2, ti3, toi3, c3);
            utxom.AddUtxo(address3, bi3, ti3, toi3, c3);

            Utxo utxo1 = utxom.FindUtxo(address1, bi1, ti1, toi1);

            if (utxo1 == null)
                throw new Exception("test4_2");
            if (utxo1.blockIndex != bi1)
                throw new Exception("test4_3");
            if (utxo1.txIndex != ti1)
                throw new Exception("test4_4");
            if (utxo1.txOutIndex != toi1)
                throw new Exception("test4_5");
            if (utxo1.amount.rawAmount != c1.rawAmount)
                throw new Exception("test4_6");

            utxom.AddUtxo(address1, bi1, ti1, toi1, c1);

            utxom.RemoveUtxo(address1, bi1, ti1, toi1);
            utxom.RemoveUtxo(address1, bi1, ti1, toi1);

            bool flag = false;
            try
            {
                utxom.RemoveUtxo(address1, bi1, ti1, toi1);
            }
            catch (InvalidOperationException)
            {
                flag = true;
            }
            if (!flag)
                throw new Exception("test4_7");

            Utxo utxoNull2 = utxom.FindUtxo(address1, bi1, ti1, toi1);

            if (utxoNull2 != null)
                throw new Exception("test4_8");

            utxom.SaveUFPTemp();

            utxodb.Close();

            if (!File.Exists(ufpdbPath))
                throw new Exception("test4_9");
            if (!File.Exists(ufptempdbPath))
                throw new Exception("test4_10");
            if (!File.Exists(utxodbPath))
                throw new Exception("test4_11");

            UtxoManager utxom2 = new UtxoManager(ufadb, ufpdb, ufptempdb, utxodb);

            if (File.Exists(ufptempdbPath))
                throw new Exception("test4_13");

            utxodb.Open();

            Utxo utxo2 = utxom.FindUtxo(address2, bi2, ti1, toi1);

            if (utxo2 == null)
                throw new Exception("test4_14");
            if (utxo2.blockIndex != bi2)
                throw new Exception("test4_15");
            if (utxo2.txIndex != ti1)
                throw new Exception("test4_16");
            if (utxo2.txOutIndex != toi1)
                throw new Exception("test4_17");
            if (utxo2.amount.rawAmount != c1.rawAmount)
                throw new Exception("test4_18");

            utxodb.Close();

            Console.WriteLine("test4_succeeded");
        }

        //UtxoManagerのテスト2
        public static void Test5()
        {
            string basepath = System.IO.Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);

            UtxoFileAccessDB ufadb = new UtxoFileAccessDB(basepath);
            string ufadbPath = ufadb.GetPath();

            if (File.Exists(ufadbPath))
                File.Delete(ufadbPath);

            UtxoFilePointersDB ufpdb = new UtxoFilePointersDB(basepath);
            string ufpdbPath = ufpdb.GetPath();

            if (File.Exists(ufpdbPath))
                File.Delete(ufpdbPath);

            UtxoFilePointersTempDB ufptempdb = new UtxoFilePointersTempDB(basepath);
            string ufptempdbPath = ufptempdb.GetPath();

            if (File.Exists(ufptempdbPath))
                File.Delete(ufptempdbPath);

            UtxoDB utxodb = new UtxoDB(basepath);
            string utxodbPath = utxodb.GetPath();

            if (File.Exists(utxodbPath))
                File.Delete(utxodbPath);

            UtxoManager utxom = new UtxoManager(ufadb, ufpdb, ufptempdb, utxodb);

            Sha256Ripemd160Hash address = new Sha256Ripemd160Hash(new byte[] { 0 });

            int length = 100;

            long[] bis = new long[length];
            int[] tis = new int[length];
            int[] tois = new int[length];
            Creacoin[] cs = new Creacoin[length];

            for (int i = 0; i < length; i++)
            {
                bis[i] = 65536.RandomNum();
                tis[i] = 65536.RandomNum();
                tois[i] = 65536.RandomNum();
                cs[i] = new Creacoin(65536.RandomNum());
            }

            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();

            utxodb.Open();

            for (int i = 0; i < length; i++)
                utxom.AddUtxo(address, bis[i], tis[i], tois[i], cs[i]);

            utxom.SaveUFPTemp();

            utxodb.Close();

            stopwatch.Stop();

            Console.WriteLine(string.Join(":", "test5_5", stopwatch.ElapsedMilliseconds.ToString() + "ms"));

            UtxoManager utxom2 = new UtxoManager(ufadb, ufpdb, ufptempdb, utxodb);

            Utxo[] utxos = new Utxo[length];

            stopwatch.Reset();
            stopwatch.Start();

            utxodb.Open();

            for (int i = 0; i < length; i++)
                utxos[i] = utxom.FindUtxo(address, bis[i], tis[i], tois[i]);

            utxodb.Close();

            stopwatch.Stop();

            Console.WriteLine(string.Join(":", "test5_6", stopwatch.ElapsedMilliseconds.ToString() + "ms"));

            for (int i = 0; i < length; i++)
            {
                if (utxos[i].blockIndex != bis[i])
                    throw new Exception("test5_1");
                if (utxos[i].txIndex != tis[i])
                    throw new Exception("test5_2");
                if (utxos[i].txOutIndex != tois[i])
                    throw new Exception("test5_3");
                if (utxos[i].amount.rawAmount != cs[i].rawAmount)
                    throw new Exception("test5_4");
            }
        }

        //UtxoManagerのテスト3
        public static void Test6()
        {
            string basepath = System.IO.Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);

            UtxoFileAccessDB ufadb = new UtxoFileAccessDB(basepath);
            string ufadbPath = ufadb.GetPath();

            if (File.Exists(ufadbPath))
                File.Delete(ufadbPath);

            UtxoFilePointersDB ufpdb = new UtxoFilePointersDB(basepath);
            string ufpdbPath = ufpdb.GetPath();

            if (File.Exists(ufpdbPath))
                File.Delete(ufpdbPath);

            UtxoFilePointersTempDB ufptempdb = new UtxoFilePointersTempDB(basepath);
            string ufptempdbPath = ufptempdb.GetPath();

            if (File.Exists(ufptempdbPath))
                File.Delete(ufptempdbPath);

            UtxoDB utxodb = new UtxoDB(basepath);
            string utxodbPath = utxodb.GetPath();

            if (File.Exists(utxodbPath))
                File.Delete(utxodbPath);

            UtxoManager utxom = new UtxoManager(ufadb, ufpdb, ufptempdb, utxodb);

            int length = 100;

            Sha256Ripemd160Hash[] addrs = new Sha256Ripemd160Hash[length];
            long[] bis = new long[length];
            int[] tis = new int[length];
            int[] tois = new int[length];
            Creacoin[] cs = new Creacoin[length];

            for (int i = 0; i < length; i++)
            {
                addrs[i] = new Sha256Ripemd160Hash(BitConverter.GetBytes(i));
                bis[i] = 65536.RandomNum();
                tis[i] = 65536.RandomNum();
                tois[i] = 65536.RandomNum();
                cs[i] = new Creacoin(65536.RandomNum());
            }

            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();

            utxodb.Open();

            for (int i = 0; i < length; i++)
                utxom.AddUtxo(addrs[i], bis[i], tis[i], tois[i], cs[i]);

            utxom.SaveUFPTemp();

            utxodb.Close();

            stopwatch.Stop();

            Console.WriteLine(string.Join(":", "test6_5", stopwatch.ElapsedMilliseconds.ToString() + "ms"));

            UtxoManager utxom2 = new UtxoManager(ufadb, ufpdb, ufptempdb, utxodb);

            Utxo[] utxos = new Utxo[length];

            stopwatch.Reset();
            stopwatch.Start();

            utxodb.Open();

            for (int i = 0; i < length; i++)
                utxos[i] = utxom.FindUtxo(addrs[i], bis[i], tis[i], tois[i]);

            utxodb.Close();

            stopwatch.Stop();

            Console.WriteLine(string.Join(":", "test6_6", stopwatch.ElapsedMilliseconds.ToString() + "ms"));

            for (int i = 0; i < length; i++)
            {
                if (utxos[i].blockIndex != bis[i])
                    throw new Exception("test6_1");
                if (utxos[i].txIndex != tis[i])
                    throw new Exception("test6_2");
                if (utxos[i].txOutIndex != tois[i])
                    throw new Exception("test6_3");
                if (utxos[i].amount.rawAmount != cs[i].rawAmount)
                    throw new Exception("test6_4");
            }
        }

        //BlockDBのテスト
        public static void Test7()
        {
            string basepath = System.IO.Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);
            BlockDB blkdb = new BlockDB(basepath);
            string path1 = blkdb.GetPath(0);
            string path2 = blkdb.GetPath(1);

            if (File.Exists(path1))
                File.Delete(path1);

            if (File.Exists(path2))
                File.Delete(path2);

            byte[] emptyBytes = blkdb.GetBlockData(0, 0);

            if (emptyBytes.Length != 0)
                throw new Exception("test1_1");

            byte[][] emptyBytess = blkdb.GetBlockDatas(0, new long[10]);

            for (int i = 0; i < emptyBytess.Length; i++)
                if (emptyBytess[i].Length != 0)
                    throw new Exception("test1_2");

            byte[] blkBytesIn1 = new byte[1024];
            for (int i = 0; i < blkBytesIn1.Length; i++)
                blkBytesIn1[i] = (byte)256.RandomNum();

            int overallLangth = blkBytesIn1.Length + 4;

            long position1 = blkdb.AddBlockData(0, blkBytesIn1);

            if (position1 != 0)
                throw new Exception("test1_3");

            long position2 = blkdb.AddBlockData(1, blkBytesIn1);

            if (position2 != 0)
                throw new Exception("test1_4");

            byte[][] blkBytessIn1 = new byte[10][];
            for (int i = 0; i < blkBytessIn1.Length; i++)
            {
                blkBytessIn1[i] = new byte[blkBytesIn1.Length];
                for (int j = 0; j < blkBytessIn1[i].Length; j++)
                    blkBytessIn1[i][j] = (byte)256.RandomNum();
            }

            long[] positions1 = blkdb.AddBlockDatas(0, blkBytessIn1);

            for (int i = 0; i < blkBytessIn1.Length; i++)
                if (positions1[i] != overallLangth * (i + 1))
                    throw new Exception("test1_5");

            long[] positions2 = blkdb.AddBlockDatas(1, blkBytessIn1);

            for (int i = 0; i < blkBytessIn1.Length; i++)
                if (positions2[i] != overallLangth * (i + 1))
                    throw new Exception("test1_6");

            byte[] blkBytesOut1 = blkdb.GetBlockData(0, position1);

            if (!blkBytesIn1.BytesEquals(blkBytesOut1))
                throw new Exception("test1_7");

            byte[] utxoBytesOut2 = blkdb.GetBlockData(1, position1);

            if (!blkBytesIn1.BytesEquals(utxoBytesOut2))
                throw new Exception("test1_8");

            byte[][] blkBytessOut1 = blkdb.GetBlockDatas(0, positions1);

            for (int i = 0; i < blkBytessIn1.Length; i++)
                if (!blkBytessIn1[i].BytesEquals(blkBytessOut1[i]))
                    throw new Exception("test1_9");

            byte[][] blkBytessOut2 = blkdb.GetBlockDatas(1, positions2);

            for (int i = 0; i < blkBytessIn1.Length; i++)
                if (!blkBytessIn1[i].BytesEquals(blkBytessOut2[i]))
                    throw new Exception("test1_10");

            byte[] emptyBytes2 = blkdb.GetBlockData(0, overallLangth * 11);

            if (emptyBytes2.Length != 0)
                throw new Exception("test1_11");

            byte[] emptyBytes3 = blkdb.GetBlockData(1, overallLangth * 11);

            if (emptyBytes3.Length != 0)
                throw new Exception("test1_12");

            Console.WriteLine("test7_succeeded");
        }

        //BlockFilePointersDBのテスト
        public static void Test8()
        {
            string basepath = System.IO.Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);
            BlockFilePointersDB bfpdb = new BlockFilePointersDB(basepath);
            string path = bfpdb.GetPath();

            if (File.Exists(path))
                File.Delete(path);

            for (int i = 0; i < 10; i++)
            {
                long position = bfpdb.GetBlockFilePointerData(256.RandomNum());

                if (position != -1)
                    throw new Exception("test8_1");
            }

            long[] bindexes = 256.RandomNums().Take(10).Select((elem) => (long)elem).ToArray();

            for (int i = 0; i < bindexes.Length; i++)
                bfpdb.UpdateBlockFilePointerData(bindexes[i], bindexes[i]);

            long[] bindexesSorted = bindexes.ToList().Pipe((list) => list.Sort()).ToArray();

            int pointer = 0;
            for (int i = 0; i < 256; i++)
            {
                long position = bfpdb.GetBlockFilePointerData(i);

                if (pointer < bindexesSorted.Length && i == bindexesSorted[pointer])
                {
                    if (position != bindexesSorted[pointer])
                        throw new Exception("test8_2");

                    pointer++;
                }
                else
                {
                    if (position != -1)
                        throw new Exception("test8_3");
                }
            }

            for (int i = 0; i < 10; i++)
            {
                int[] random = 256.RandomNums();

                long[] bindexes2 = random.Take(10).Select((elem) => (long)elem).ToArray();
                long[] positions2 = random.Skip(10).Take(10).Select((elem) => (long)elem).ToArray();

                for (int j = 0; j < bindexes2.Length; j++)
                    bfpdb.UpdateBlockFilePointerData(bindexes2[j], positions2[j]);

                for (int j = 0; j < bindexes2.Length; j++)
                {
                    long positionOut = bfpdb.GetBlockFilePointerData(bindexes2[j]);

                    if (positionOut != positions2[j])
                        throw new Exception("test8_4");
                }
            }

            Console.WriteLine("test8_succeeded");
        }

        //BlockManagerのテスト1
        public static void Test9()
        {
            string basepath = System.IO.Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);

            BlockManagerDB bmdb = new BlockManagerDB(basepath);
            string bmdbPath = bmdb.GetPath();

            if (File.Exists(bmdbPath))
                File.Delete(bmdbPath);

            BlockDB bdb = new BlockDB(basepath);
            string bdbPath = bdb.GetPath(0);

            if (File.Exists(bdbPath))
                File.Delete(bdbPath);

            BlockFilePointersDB bfpdb = new BlockFilePointersDB(basepath);
            string bfpPath = bfpdb.GetPath();

            if (File.Exists(bfpPath))
                File.Delete(bfpPath);

            BlockManager blkmanager = new BlockManager(bmdb, bdb, bfpdb, 10, 10, 3);

            if (blkmanager.headBlockIndex != -1)
                throw new InvalidOperationException("test9_1");
            if (blkmanager.finalizedBlockIndex != -1)
                throw new InvalidOperationException("test9_2");
            if (blkmanager.mainBlocksCurrent.value != 0)
                throw new InvalidOperationException("test9_16");
            if (blkmanager.mainBlocks[blkmanager.mainBlocksCurrent.value] != null)
                throw new InvalidOperationException("test9_3");

            bool flag = false;
            try
            {
                blkmanager.GetMainBlock(-1);
            }
            catch (InvalidOperationException)
            {
                flag = true;
            }
            if (!flag)
                throw new Exception("test9_5");

            bool flag2 = false;
            try
            {
                blkmanager.GetMainBlock(1);
            }
            catch (InvalidOperationException)
            {
                flag2 = true;
            }
            if (!flag2)
                throw new Exception("test9_6");

            bool flag9 = false;
            try
            {
                blkmanager.GetMainBlock(0);
            }
            catch (InvalidOperationException)
            {
                flag9 = true;
            }
            if (!flag9)
                throw new Exception("test9_28");

            blkmanager.AddMainBlock(new GenesisBlock());

            if (blkmanager.headBlockIndex != 0)
                throw new InvalidOperationException("test9_29");
            if (blkmanager.finalizedBlockIndex != 0)
                throw new InvalidOperationException("test9_30");
            if (blkmanager.mainBlocksCurrent.value != 1)
                throw new InvalidOperationException("test9_31");
            if (blkmanager.mainBlocks[blkmanager.mainBlocksCurrent.value] == null)
                throw new InvalidOperationException("test9_32");
            if (!(blkmanager.mainBlocks[blkmanager.mainBlocksCurrent.value] is GenesisBlock))
                throw new InvalidOperationException("test9_33");

            Block block1 = blkmanager.GetMainBlock(0);
            Block block2 = blkmanager.GetHeadBlock();

            if (!(block1 is GenesisBlock))
                throw new Exception("test9_10");
            if (!(block2 is GenesisBlock))
                throw new Exception("test9_11");

            bool flag3 = false;
            try
            {
                blkmanager.DeleteMainBlock(-1);
            }
            catch (InvalidOperationException)
            {
                flag3 = true;
            }
            if (!flag3)
                throw new Exception("test9_7");

            bool flag4 = false;
            try
            {
                blkmanager.DeleteMainBlock(1);
            }
            catch (InvalidOperationException)
            {
                flag4 = true;
            }
            if (!flag4)
                throw new Exception("test9_8");

            bool flag5 = false;
            try
            {
                blkmanager.DeleteMainBlock(0);
            }
            catch (InvalidOperationException)
            {
                flag5 = true;
            }
            if (!flag5)
                throw new Exception("test9_9");

            TestBlock testblk1 = new TestBlock(1);

            blkmanager.AddMainBlock(testblk1);

            if (blkmanager.headBlockIndex != 1)
                throw new InvalidOperationException("test9_12");
            if (blkmanager.finalizedBlockIndex != 0)
                throw new InvalidOperationException("test9_13");
            if (blkmanager.mainBlocksCurrent.value != 2)
                throw new InvalidOperationException("test9_17");
            if (blkmanager.mainBlocks[blkmanager.mainBlocksCurrent.value] == null)
                throw new InvalidOperationException("test9_14");
            if (!(blkmanager.mainBlocks[blkmanager.mainBlocksCurrent.value] is TestBlock))
                throw new InvalidOperationException("test9_15");

            blkmanager.DeleteMainBlock(1);

            if (blkmanager.headBlockIndex != 0)
                throw new InvalidOperationException("test9_18");
            if (blkmanager.finalizedBlockIndex != 0)
                throw new InvalidOperationException("test9_19");
            if (blkmanager.mainBlocksCurrent.value != 1)
                throw new InvalidOperationException("test9_20");
            if (blkmanager.mainBlocks[blkmanager.mainBlocksCurrent.value] == null)
                throw new InvalidOperationException("test9_21");
            if (!(blkmanager.mainBlocks[blkmanager.mainBlocksCurrent.value] is GenesisBlock))
                throw new InvalidOperationException("test9_22");

            TestBlock testblk2 = new TestBlock(2);

            bool flag6 = false;
            try
            {
                blkmanager.AddMainBlock(testblk2);
            }
            catch (InvalidOperationException)
            {
                flag6 = true;
            }
            if (!flag6)
                throw new Exception("test9_23");

            for (int i = 1; i < 18; i++)
            {
                TestBlock testblk = new TestBlock(i);

                blkmanager.AddMainBlock(testblk);
            }

            for (int i = 17; i > 0; i--)
            {
                if (i == 14)
                {
                    bool flag7 = false;
                    try
                    {
                        blkmanager.DeleteMainBlock(i);
                    }
                    catch (InvalidOperationException)
                    {
                        flag7 = true;
                    }
                    if (!flag7)
                        throw new Exception("test9_24");

                    break;
                }

                blkmanager.DeleteMainBlock(i);
            }

            Block block3 = blkmanager.GetHeadBlock();

            if (block3.Index != 14)
                throw new Exception("test9_25");

            for (int i = 17; i > 0; i--)
            {
                if (i > 14)
                {
                    bool flag8 = false;
                    try
                    {
                        blkmanager.GetMainBlock(i);
                    }
                    catch (InvalidOperationException)
                    {
                        flag8 = true;
                    }
                    if (!flag8)
                        throw new Exception("test9_26");

                    continue;
                }

                Block block4 = blkmanager.GetMainBlock(i);

                if (block4.Index != i)
                    throw new Exception("test9_27");
            }

            Console.WriteLine("test9_succeeded");
        }

        //TransactionOutput、TransactionInput、CoinbaseTransaction、TransferTransactionのテスト
        public static void Test10()
        {
            Ecdsa256KeyPair keypair1 = new Ecdsa256KeyPair(true);
            Ecdsa256KeyPair keypair2 = new Ecdsa256KeyPair(true);
            Ecdsa256KeyPair keypair3 = new Ecdsa256KeyPair(true);

            Sha256Ripemd160Hash address1 = new Sha256Ripemd160Hash(keypair1.pubKey.pubKey);
            CurrencyUnit amount1 = new Creacoin(50.0m);
            Sha256Ripemd160Hash address2 = new Sha256Ripemd160Hash(keypair2.pubKey.pubKey);
            CurrencyUnit amount2 = new Creacoin(25.0m);
            Sha256Ripemd160Hash address3 = new Sha256Ripemd160Hash(keypair3.pubKey.pubKey);
            CurrencyUnit amount3 = new Yumina(0.01m);

            TransactionOutput txOut1 = new TransactionOutput();
            txOut1.LoadVersion0(address1, amount1);
            TransactionOutput txOut2 = new TransactionOutput();
            txOut2.LoadVersion0(address2, amount2);
            TransactionOutput txOut3 = new TransactionOutput();
            txOut3.LoadVersion0(address3, amount3);

            if (txOut1.Address != address1)
                throw new Exception("test10_1");
            if (txOut1.Amount != amount1)
                throw new Exception("test10_2");

            byte[] txOutBytes = txOut1.ToBinary();

            if (txOutBytes.Length != 29)
                throw new Exception("test10_3");

            TransactionOutput txOutRestore = SHAREDDATA.FromBinary<TransactionOutput>(txOutBytes, 0);

            if (!txOut1.Address.Equals(txOutRestore.Address))
                throw new Exception("test10_4");
            if (txOut1.Amount.rawAmount != txOutRestore.Amount.rawAmount)
                throw new Exception("test10_5");

            TransactionInput txIn1 = new TransactionInput();
            txIn1.LoadVersion0(0, 0, 0, keypair1.pubKey);
            TransactionInput txIn2 = new TransactionInput();
            txIn2.LoadVersion0(1, 0, 0, keypair2.pubKey);
            TransactionInput txIn3 = new TransactionInput();
            txIn3.LoadVersion0(2, 0, 0, keypair3.pubKey);

            if (txIn1.PrevTxBlockIndex != 0)
                throw new Exception("test10_6");
            if (txIn1.PrevTxIndex != 0)
                throw new Exception("test10_7");
            if (txIn1.PrevTxOutputIndex != 0)
                throw new Exception("test10_8");
            if (txIn1.SenderPubKey != keypair1.pubKey)
                throw new Exception("test10_9");

            TransactionOutput[] txOuts = new TransactionOutput[] { txOut1, txOut2, txOut3 };

            CoinbaseTransaction cTx = new CoinbaseTransaction();
            cTx.LoadVersion0(txOuts);

            if (cTx.TxOutputs != txOuts)
                throw new Exception("test10_10");
            if (cTx.TxInputs.Length != 0)
                throw new Exception("test10_11");

            byte[] cTxBytes = cTx.ToBinary();

            if (cTxBytes.Length != 97)
                throw new Exception("test10_12");

            CoinbaseTransaction cTxRestore = SHAREDDATA.FromBinary<CoinbaseTransaction>(cTxBytes);

            if (!cTx.Id.Equals(cTxRestore.Id))
                throw new Exception("test10_13");

            if (cTx.Verify())
                throw new Exception("test10_14");
            if (cTx.VerifyNotExistDustTxOutput())
                throw new Exception("test10_15");
            if (!cTx.VerifyNumberOfTxInputs())
                throw new Exception("test10_16");
            if (!cTx.VerifyNumberOfTxOutputs())
                throw new Exception("test10_17");

            TransactionOutput[] txOuts2 = new TransactionOutput[11];
            for (int i = 0; i < txOuts2.Length; i++)
                txOuts2[i] = txOut1;

            CoinbaseTransaction cTx2 = new CoinbaseTransaction();
            cTx2.LoadVersion0(txOuts2);

            if (cTx2.Verify())
                throw new Exception("test10_18");
            if (!cTx2.VerifyNotExistDustTxOutput())
                throw new Exception("test10_19");
            if (!cTx2.VerifyNumberOfTxInputs())
                throw new Exception("test10_20");
            if (cTx2.VerifyNumberOfTxOutputs())
                throw new Exception("test10_21");

            TransactionOutput[] txOuts3 = new TransactionOutput[] { txOut1, txOut2 };

            CoinbaseTransaction cTx3 = new CoinbaseTransaction();
            cTx3.LoadVersion0(txOuts3);

            if (!cTx3.Verify())
                throw new Exception("test10_22");
            if (!cTx3.VerifyNotExistDustTxOutput())
                throw new Exception("test10_23");
            if (!cTx3.VerifyNumberOfTxInputs())
                throw new Exception("test10_24");
            if (!cTx3.VerifyNumberOfTxOutputs())
                throw new Exception("test10_25");

            TransactionInput[] txIns = new TransactionInput[] { txIn1, txIn2, txIn3 };

            TransferTransaction tTx1 = new TransferTransaction();
            tTx1.LoadVersion0(txIns, txOuts);
            tTx1.Sign(txOuts, new DSAPRIVKEYBASE[] { keypair1.privKey, keypair2.privKey, keypair3.privKey });

            if (tTx1.TxInputs != txIns)
                throw new Exception("test10_26");
            if (tTx1.TxOutputs != txOuts)
                throw new Exception("test10_27");

            byte[] txInBytes = txIn1.ToBinary();

            if (txInBytes.Length != 153)
                throw new Exception("test10_28");

            TransactionInput txInRestore = SHAREDDATA.FromBinary<TransactionInput>(txInBytes, 0);

            if (txIn1.PrevTxBlockIndex != txInRestore.PrevTxBlockIndex)
                throw new Exception("test10_29");
            if (txIn1.PrevTxIndex != txInRestore.PrevTxIndex)
                throw new Exception("test10_30");
            if (txIn1.PrevTxOutputIndex != txInRestore.PrevTxOutputIndex)
                throw new Exception("test10_31");
            if (!txIn1.SenderPubKey.pubKey.BytesEquals(txInRestore.SenderPubKey.pubKey))
                throw new Exception("test10_32");
            if (!txIn1.SenderSignature.signature.BytesEquals(txInRestore.SenderSignature.signature))
                throw new Exception("test10_33");

            byte[] tTxBytes = tTx1.ToBinary();

            if (tTxBytes.Length != 557)
                throw new Exception("test10_34");

            TransferTransaction tTxRestore = SHAREDDATA.FromBinary<TransferTransaction>(tTxBytes);

            if (!tTx1.Id.Equals(tTxRestore.Id))
                throw new Exception("test10_35");

            if (tTx1.Verify(txOuts))
                throw new Exception("test10_36");
            if (tTx1.VerifyNotExistDustTxOutput())
                throw new Exception("test10_37");
            if (!tTx1.VerifyNumberOfTxInputs())
                throw new Exception("test10_38");
            if (!tTx1.VerifyNumberOfTxOutputs())
                throw new Exception("test10_39");
            if (!tTx1.VerifySignature(txOuts))
                throw new Exception("test10_40");
            if (!tTx1.VerifyPubKey(txOuts))
                throw new Exception("test10_41");
            if (!tTx1.VerifyAmount(txOuts))
                throw new Exception("test10_42");
            if (tTx1.GetFee(txOuts).rawAmount != 0)
                throw new Exception("test10_43");

            TransactionOutput[] txOuts4 = new TransactionOutput[] { txOut2, txOut1, txOut3 };

            if (tTx1.Verify(txOuts4))
                throw new Exception("test10_44");
            if (tTx1.VerifySignature(txOuts4))
                throw new Exception("test10_45");
            if (tTx1.VerifyPubKey(txOuts4))
                throw new Exception("test10_46");

            byte temp2 = tTx1.TxInputs[0].SenderSignature.signature[0];

            tTx1.TxInputs[0].SenderSignature.signature[0] = 0;

            if (tTx1.Verify(txOuts))
                throw new Exception("test10_47");
            if (tTx1.VerifySignature(txOuts))
                throw new Exception("test10_48");
            if (!tTx1.VerifyPubKey(txOuts))
                throw new Exception("test10_49");

            tTx1.TxInputs[0].SenderSignature.signature[0] = temp2;

            TransferTransaction tTx2 = new TransferTransaction();
            tTx2.LoadVersion0(txIns, txOuts);
            tTx2.Sign(txOuts, new DSAPRIVKEYBASE[] { keypair2.privKey, keypair1.privKey, keypair3.privKey });

            if (tTx2.Verify(txOuts))
                throw new Exception("test10_50");
            if (tTx2.VerifySignature(txOuts))
                throw new Exception("test10_51");
            if (!tTx2.VerifyPubKey(txOuts))
                throw new Exception("test10_52");

            TransferTransaction tTx3 = new TransferTransaction();
            tTx3.LoadVersion0(txIns, txOuts);
            tTx3.Sign(txOuts, new DSAPRIVKEYBASE[] { keypair1.privKey, keypair2.privKey, keypair3.privKey });

            byte temp = tTx3.TxInputs[0].SenderPubKey.pubKey[0];

            tTx3.TxInputs[0].SenderPubKey.pubKey[0] = 0;

            if (tTx3.Verify(txOuts))
                throw new Exception("test10_50");
            if (tTx3.VerifySignature(txOuts))
                throw new Exception("test10_51");
            if (tTx3.VerifyPubKey(txOuts))
                throw new Exception("test10_52");

            tTx3.TxInputs[0].SenderPubKey.pubKey[0] = temp;

            TransferTransaction tTx4 = new TransferTransaction();
            tTx4.LoadVersion0(txIns, txOuts2);
            tTx4.Sign(txOuts, new DSAPRIVKEYBASE[] { keypair1.privKey, keypair2.privKey, keypair3.privKey });

            if (tTx4.Verify(txOuts))
                throw new Exception("test10_53");
            if (!tTx4.VerifyNotExistDustTxOutput())
                throw new Exception("test10_54");
            if (!tTx4.VerifyNumberOfTxInputs())
                throw new Exception("test10_55");
            if (tTx4.VerifyNumberOfTxOutputs())
                throw new Exception("test10_56");
            if (!tTx4.VerifySignature(txOuts))
                throw new Exception("test10_57");
            if (!tTx4.VerifyPubKey(txOuts))
                throw new Exception("test10_58");
            if (tTx4.VerifyAmount(txOuts))
                throw new Exception("test10_59");
            if (tTx4.GetFee(txOuts).rawAmount != -47499990000)
                throw new Exception("test10_60");

            TransferTransaction tTx5 = new TransferTransaction();
            tTx5.LoadVersion0(txIns, txOuts3);
            tTx5.Sign(txOuts, new DSAPRIVKEYBASE[] { keypair1.privKey, keypair2.privKey, keypair3.privKey });

            if (!tTx5.Verify(txOuts))
                throw new Exception("test10_61");
            if (!tTx5.VerifyNotExistDustTxOutput())
                throw new Exception("test10_62");
            if (!tTx5.VerifyNumberOfTxInputs())
                throw new Exception("test10_63");
            if (!tTx5.VerifyNumberOfTxOutputs())
                throw new Exception("test10_64");
            if (!tTx5.VerifySignature(txOuts))
                throw new Exception("test10_65");
            if (!tTx5.VerifyPubKey(txOuts))
                throw new Exception("test10_66");
            if (!tTx5.VerifyAmount(txOuts))
                throw new Exception("test10_67");
            if (tTx5.GetFee(txOuts).rawAmount != 10000)
                throw new Exception("test10_68");

            TransactionInput[] txIns2 = new TransactionInput[101];
            for (int i = 0; i < txIns2.Length; i++)
                txIns2[i] = txIn1;

            TransactionOutput[] txOuts5 = new TransactionOutput[txIns2.Length];
            for (int i = 0; i < txOuts5.Length; i++)
                txOuts5[i] = txOut1;

            Ecdsa256PrivKey[] privKeys = new Ecdsa256PrivKey[txIns2.Length];
            for (int i = 0; i < privKeys.Length; i++)
                privKeys[i] = keypair1.privKey;

            TransferTransaction tTx6 = new TransferTransaction();
            tTx6.LoadVersion0(txIns2, txOuts3);
            tTx6.Sign(txOuts5, privKeys);

            if (tTx6.Verify(txOuts5))
                throw new Exception("test10_61");
            if (!tTx6.VerifyNotExistDustTxOutput())
                throw new Exception("test10_62");
            if (tTx6.VerifyNumberOfTxInputs())
                throw new Exception("test10_63");
            if (!tTx6.VerifyNumberOfTxOutputs())
                throw new Exception("test10_64");
            if (!tTx6.VerifySignature(txOuts5))
                throw new Exception("test10_65");
            if (!tTx6.VerifyPubKey(txOuts5))
                throw new Exception("test10_66");
            if (!tTx6.VerifyAmount(txOuts5))
                throw new Exception("test10_67");
            if (tTx6.GetFee(txOuts5).rawAmount != 497500000000)
                throw new Exception("test10_68");

            byte[] cTxBytes2 = SHAREDDATA.ToBinary<Transaction>(cTx);

            if (cTxBytes2.Length != 117)
                throw new Exception("test10_69");

            CoinbaseTransaction cTxRestore2 = SHAREDDATA.FromBinary<Transaction>(cTxBytes2) as CoinbaseTransaction;

            if (!cTx.Id.Equals(cTxRestore2.Id))
                throw new Exception("test10_70");

            byte[] tTxBytes2 = SHAREDDATA.ToBinary<Transaction>(tTx6);

            if (tTxBytes2.Length != 15445)
                throw new Exception("test10_71");

            TransferTransaction tTxRestore2 = SHAREDDATA.FromBinary<Transaction>(tTxBytes2) as TransferTransaction;

            if (!tTx6.Id.Equals(tTxRestore2.Id))
                throw new Exception("test10_72");

            Sha256Sha256Hash ctxid = new Sha256Sha256Hash(cTxBytes);

            if (!ctxid.Equals(cTx.Id))
                throw new Exception("test10_73");

            Sha256Sha256Hash ttxid = new Sha256Sha256Hash(tTx6.ToBinary());

            if (!ttxid.Equals(tTx6.Id))
                throw new Exception("test10_74");

            Console.WriteLine("test10_succeeded");
        }

        //Blockのテスト1
        public static void Test11()
        {
            GenesisBlock gblk = new GenesisBlock();

            if (gblk.Index != 0)
                throw new Exception("test11_1");
            if (gblk.PrevId != null)
                throw new Exception("test11_2");
            if (gblk.Difficulty.Diff != 0.00000011)
                throw new Exception("test11_3");
            if (gblk.Transactions.Length != 0)
                throw new Exception("test11_4");

            byte[] gblkBytes = gblk.ToBinary();

            if (gblkBytes.Length != 68)
                throw new Exception("test11_5");

            GenesisBlock gblkRestore = SHAREDDATA.FromBinary<GenesisBlock>(gblkBytes);

            if (!gblk.Id.Equals(gblkRestore.Id))
                throw new Exception("test11_6");

            byte[] gblkBytes2 = SHAREDDATA.ToBinary<Block>(gblk);

            if (gblkBytes2.Length != 88)
                throw new Exception("test11_7");

            GenesisBlock gblkRestore2 = SHAREDDATA.FromBinary<Block>(gblkBytes2) as GenesisBlock;

            if (!gblk.Id.Equals(gblkRestore2.Id))
                throw new Exception("test11_8");

            BlockHeader bh = new BlockHeader();

            bool flag = false;
            try
            {
                bh.LoadVersion0(0, null, DateTime.Now, null, new byte[10]);
            }
            catch (ArgumentOutOfRangeException)
            {
                flag = true;
            }
            if (!flag)
                throw new Exception("test11_9");

            bool flag2 = false;
            try
            {
                bh.LoadVersion0(1, null, DateTime.Now, null, new byte[9]);
            }
            catch (ArgumentOutOfRangeException)
            {
                flag2 = true;
            }
            if (!flag2)
                throw new Exception("test11_10");

            Difficulty<Creahash> diff = new Difficulty<Creahash>(HASHBASE.FromHash<Creahash>(new byte[] { 0, 127, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255 }));
            Sha256Sha256Hash hash = new Sha256Sha256Hash(new byte[] { 1 });
            DateTime dt = DateTime.Now;
            byte[] nonce = new byte[10] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 };

            bh.LoadVersion0(1, gblk.Id, DateTime.Now, diff, new byte[10]);

            bool flag3 = false;
            try
            {
                bh.UpdateNonce(new byte[11]);
            }
            catch (ArgumentOutOfRangeException)
            {
                flag3 = true;
            }
            if (!flag3)
                throw new Exception("test11_11");

            bh.UpdateMerkleRootHash(hash);
            bh.UpdateTimestamp(dt);
            bh.UpdateNonce(nonce);

            if (bh.index != 1)
                throw new Exception("test11_12");
            if (bh.prevBlockHash != gblk.Id)
                throw new Exception("test11_13");
            if (bh.merkleRootHash != hash)
                throw new Exception("test11_14");
            if (bh.timestamp != dt)
                throw new Exception("test11_15");
            if (bh.difficulty != diff)
                throw new Exception("test11_16");
            if (bh.nonce != nonce)
                throw new Exception("test11_17");

            byte[] bhBytes = bh.ToBinary();

            if (bhBytes.Length != 95)
                throw new Exception("test11_18");

            BlockHeader bhRestore = SHAREDDATA.FromBinary<BlockHeader>(bhBytes);

            if (bh.index != bhRestore.index)
                throw new Exception("test11_19");
            if (!bh.prevBlockHash.Equals(bhRestore.prevBlockHash))
                throw new Exception("test11_20");
            if (!bh.merkleRootHash.Equals(bhRestore.merkleRootHash))
                throw new Exception("test11_21");
            if (bh.timestamp != bhRestore.timestamp)
                throw new Exception("test11_22");
            if (bh.difficulty.Diff != bhRestore.difficulty.Diff)
                throw new Exception("test11_23");
            if (!bh.nonce.BytesEquals(bhRestore.nonce))
                throw new Exception("test11_24");

            bool flag4 = false;
            try
            {
                TransactionalBlock.GetBlockType(0, 0);
            }
            catch (ArgumentOutOfRangeException)
            {
                flag4 = true;
            }
            if (!flag4)
                throw new Exception("test11_25");

            Type type1 = TransactionalBlock.GetBlockType(60 * 24 - 1, 0);
            Type type2 = TransactionalBlock.GetBlockType(60 * 24, 0);
            Type type3 = TransactionalBlock.GetBlockType(60 * 24 + 1, 0);

            if (type1 != typeof(NormalBlock))
                throw new Exception("test11_26");
            if (type2 != typeof(FoundationalBlock))
                throw new Exception("test11_27");
            if (type3 != typeof(NormalBlock))
                throw new Exception("test11_28");

            bool flag5 = false;
            try
            {
                TransactionalBlock.GetRewardToAll(0, 0);
            }
            catch (ArgumentOutOfRangeException)
            {
                flag5 = true;
            }
            if (!flag5)
                throw new Exception("test11_29");

            bool flag6 = false;
            try
            {
                TransactionalBlock.GetRewardToMiner(0, 0);
            }
            catch (ArgumentOutOfRangeException)
            {
                flag6 = true;
            }
            if (!flag6)
                throw new Exception("test11_30");

            bool flag7 = false;
            try
            {
                TransactionalBlock.GetRewardToFoundation(0, 0);
            }
            catch (ArgumentOutOfRangeException)
            {
                flag7 = true;
            }
            if (!flag7)
                throw new Exception("test11_31");

            bool flag8 = false;
            try
            {
                TransactionalBlock.GetRewardToFoundationInterval(0, 0);
            }
            catch (ArgumentOutOfRangeException)
            {
                flag8 = true;
            }
            if (!flag8)
                throw new Exception("test11_32");

            bool flag9 = false;
            try
            {
                TransactionalBlock.GetRewardToFoundationInterval(1, 0);
            }
            catch (ArgumentException)
            {
                flag9 = true;
            }
            if (!flag9)
                throw new Exception("test11_33");

            CurrencyUnit initial = new Creacoin(60.0m);
            decimal rate = 1.25m;
            for (int i = 0; i < 8; i++)
            {
                CurrencyUnit reward1 = i != 0 ? TransactionalBlock.GetRewardToAll((60 * 24 * 365 * i) - 1, 0) : null;
                CurrencyUnit reward2 = i != 0 ? TransactionalBlock.GetRewardToAll(60 * 24 * 365 * i, 0) : null;
                CurrencyUnit reward3 = TransactionalBlock.GetRewardToAll((60 * 24 * 365 * i) + 1, 0);

                CurrencyUnit reward7 = i != 0 ? TransactionalBlock.GetRewardToMiner((60 * 24 * 365 * i) - 1, 0) : null;
                CurrencyUnit reward8 = i != 0 ? TransactionalBlock.GetRewardToMiner(60 * 24 * 365 * i, 0) : null;
                CurrencyUnit reward9 = TransactionalBlock.GetRewardToMiner((60 * 24 * 365 * i) + 1, 0);

                CurrencyUnit reward10 = i != 0 ? TransactionalBlock.GetRewardToFoundation((60 * 24 * 365 * i) - 1, 0) : null;
                CurrencyUnit reward11 = i != 0 ? TransactionalBlock.GetRewardToFoundation(60 * 24 * 365 * i, 0) : null;
                CurrencyUnit reward12 = TransactionalBlock.GetRewardToFoundation((60 * 24 * 365 * i) + 1, 0);

                CurrencyUnit reward19 = i != 0 ? TransactionalBlock.GetRewardToFoundationInterval(((365 * i) - 1) * 60 * 24, 0) : null;
                CurrencyUnit reward20 = i != 0 ? TransactionalBlock.GetRewardToFoundationInterval(60 * 24 * 365 * i, 0) : null;
                CurrencyUnit reward21 = TransactionalBlock.GetRewardToFoundationInterval(((365 * i) + 1) * 60 * 24, 0);

                if (i != 0 && reward1.rawAmount != initial.rawAmount * rate)
                    throw new Exception("test11_34");
                if (i != 0 && reward7.rawAmount != initial.rawAmount * rate * 0.9m)
                    throw new Exception("test11_35");
                if (i != 0 && reward10.rawAmount != initial.rawAmount * rate * 0.1m)
                    throw new Exception("test11_36");
                if (i != 0 && reward19.rawAmount != initial.rawAmount * rate * 0.1m * 60m * 24m)
                    throw new Exception("test11_37");

                rate *= 0.8m;

                if (i != 0 && reward2.rawAmount != initial.rawAmount * rate)
                    throw new Exception("test11_38");
                if (i != 0 && reward8.rawAmount != initial.rawAmount * rate * 0.9m)
                    throw new Exception("test11_39");
                if (i != 0 && reward11.rawAmount != initial.rawAmount * rate * 0.1m)
                    throw new Exception("test11_40");
                if (i != 0 && reward20.rawAmount != initial.rawAmount * rate * 0.1m * 60m * 24m)
                    throw new Exception("test11_41");

                if (reward3.rawAmount != initial.rawAmount * rate)
                    throw new Exception("test11_42");
                if (reward9.rawAmount != initial.rawAmount * rate * 0.9m)
                    throw new Exception("test11_43");
                if (reward12.rawAmount != initial.rawAmount * rate * 0.1m)
                    throw new Exception("test11_44");
                if (reward21.rawAmount != initial.rawAmount * rate * 0.1m * 60m * 24m)
                    throw new Exception("test11_45");
            }

            CurrencyUnit reward4 = TransactionalBlock.GetRewardToAll((60 * 24 * 365 * 8) - 1, 0);
            CurrencyUnit reward5 = TransactionalBlock.GetRewardToAll(60 * 24 * 365 * 8, 0);
            CurrencyUnit reward6 = TransactionalBlock.GetRewardToAll((60 * 24 * 365 * 8) + 1, 0);

            CurrencyUnit reward13 = TransactionalBlock.GetRewardToMiner((60 * 24 * 365 * 8) - 1, 0);
            CurrencyUnit reward14 = TransactionalBlock.GetRewardToMiner(60 * 24 * 365 * 8, 0);
            CurrencyUnit reward15 = TransactionalBlock.GetRewardToMiner((60 * 24 * 365 * 8) + 1, 0);

            CurrencyUnit reward16 = TransactionalBlock.GetRewardToFoundation((60 * 24 * 365 * 8) - 1, 0);
            CurrencyUnit reward17 = TransactionalBlock.GetRewardToFoundation(60 * 24 * 365 * 8, 0);
            CurrencyUnit reward18 = TransactionalBlock.GetRewardToFoundation((60 * 24 * 365 * 8) + 1, 0);

            CurrencyUnit reward22 = TransactionalBlock.GetRewardToFoundationInterval(((365 * 8) - 1) * 60 * 24, 0);
            CurrencyUnit reward23 = TransactionalBlock.GetRewardToFoundationInterval(60 * 24 * 365 * 8, 0);
            CurrencyUnit reward24 = TransactionalBlock.GetRewardToFoundationInterval(((365 * 8) + 1) * 60 * 24, 0);

            if (reward4.rawAmount != initial.rawAmount * rate)
                throw new Exception("test11_46");
            if (reward13.rawAmount != initial.rawAmount * rate * 0.9m)
                throw new Exception("test11_47");
            if (reward16.rawAmount != initial.rawAmount * rate * 0.1m)
                throw new Exception("test11_48");
            if (reward22.rawAmount != initial.rawAmount * rate * 0.1m * 60m * 24m)
                throw new Exception("test11_49");

            if (reward5.rawAmount != 0)
                throw new Exception("test11_50");
            if (reward14.rawAmount != 0)
                throw new Exception("test11_51");
            if (reward17.rawAmount != 0)
                throw new Exception("test11_52");
            if (reward23.rawAmount != 0)
                throw new Exception("test11_53");

            if (reward6.rawAmount != 0)
                throw new Exception("test11_54");
            if (reward15.rawAmount != 0)
                throw new Exception("test11_55");
            if (reward18.rawAmount != 0)
                throw new Exception("test11_56");
            if (reward24.rawAmount != 0)
                throw new Exception("test11_57");

            Console.WriteLine("test11_succeeded");
        }

        //Blockのテスト2
        public static void Test12()
        {
            BlockGenerator bg = new BlockGenerator();

            Block[] blks = new Block[10];
            BlockContext[] blkCons = new BlockContext[blks.Length];
            for (int i = 0; i < blks.Length; i++)
            {
                blkCons[i] = bg.CreateNextValidBlock();
                blks[i] = blkCons[i].block;

                if (i > 0)
                {
                    NormalBlock nblk = blks[i] as NormalBlock;

                    byte[] nblkBytes = nblk.ToBinary();

                    NormalBlock nblkRestore = SHAREDDATA.FromBinary<NormalBlock>(nblkBytes);

                    if (!nblk.Id.Equals(nblkRestore.Id))
                        throw new Exception("test12_1");

                    byte[] nblkBytes2 = SHAREDDATA.ToBinary<Block>(blks[i]);

                    NormalBlock nblkRestore2 = SHAREDDATA.FromBinary<Block>(nblkBytes2) as NormalBlock;

                    if (!nblk.Id.Equals(nblkRestore2.Id))
                        throw new Exception("test12_2");
                }
            }

            GenesisBlock gblk = blks[0] as GenesisBlock;

            Creahash gblkid = new Creahash(gblk.ToBinary());

            if (!gblk.Id.Equals(gblkid))
                throw new Exception("test12_3");

            NormalBlock nblk2 = blks[1] as NormalBlock;

            Creahash nblkid = new Creahash(nblk2.header.ToBinary());

            if (!nblk2.Id.Equals(nblkid))
                throw new Exception("test12_4");

            nblk2.UpdateTimestamp(DateTime.Now);

            Creahash nblkid2 = new Creahash(nblk2.header.ToBinary());

            if (!nblk2.Id.Equals(nblkid2))
                throw new Exception("test12_5");

            nblk2.UpdateNonce(new byte[10]);

            Creahash nblkid3 = new Creahash(nblk2.header.ToBinary());

            if (!nblk2.Id.Equals(nblkid3))
                throw new Exception("test12_6");

            nblk2.UpdateMerkleRootHash();

            Creahash nblkid4 = new Creahash(nblk2.header.ToBinary());

            if (!nblk2.Id.Equals(nblkid4))
                throw new Exception("test12_7");

            if (!nblk2.VerifyBlockType())
                throw new Exception("test12_8");

            FoundationalBlock fblk = new FoundationalBlock();
            fblk.LoadVersion0(nblk2.header, nblk2.coinbaseTxToMiner, nblk2.coinbaseTxToMiner, nblk2.transferTxs);

            byte[] fblkBytes = fblk.ToBinary();

            FoundationalBlock fblkRestore = SHAREDDATA.FromBinary<FoundationalBlock>(fblkBytes);

            if (!fblk.Id.Equals(fblkRestore.Id))
                throw new Exception("test12_9");

            byte[] fblkBytes2 = SHAREDDATA.ToBinary<Block>(fblk);

            FoundationalBlock fblkRestore2 = SHAREDDATA.FromBinary<Block>(fblkBytes2) as FoundationalBlock;

            if (!fblk.Id.Equals(fblkRestore2.Id))
                throw new Exception("test12_10");

            if (fblk.VerifyBlockType())
                throw new Exception("test12_11");

            if (!nblk2.VerifyMerkleRootHash())
                throw new Exception("test12_12");

            byte[] nblkBytes3 = nblk2.ToBinary();

            NormalBlock nblk3 = SHAREDDATA.FromBinary<NormalBlock>(nblkBytes3);

            nblk3.header.merkleRootHash.hash[0] ^= 255;

            if (nblk3.VerifyMerkleRootHash())
                throw new Exception("test12_13");

            byte[] nonce = new byte[10];
            while (true)
            {
                nblk2.UpdateNonce(nonce);

                if (nblk2.Id.hash[0] == 0 && nblk2.Id.hash[1] <= 127)
                {
                    if (!nblk2.VerifyId())
                        throw new Exception("test12_14");

                    break;
                }

                if (nblk2.VerifyId())
                    throw new Exception("test12_15");

                int index = nonce.Length.RandomNum();
                int value = 256.RandomNum();

                nonce[index] = (byte)value;
            }

            TransferTransaction[] transferTxs1 = new TransferTransaction[99];
            for (int i = 0; i < transferTxs1.Length; i++)
                transferTxs1[i] = (blks[2] as TransactionalBlock).transferTxs[0];

            TransferTransaction[] transferTxs2 = new TransferTransaction[100];
            for (int i = 0; i < transferTxs2.Length; i++)
                transferTxs2[i] = (blks[2] as TransactionalBlock).transferTxs[0];

            NormalBlock nblk4 = new NormalBlock();
            nblk4.LoadVersion0(nblk2.header, nblk2.coinbaseTxToMiner, transferTxs1);

            NormalBlock nblk5 = new NormalBlock();
            nblk5.LoadVersion0(nblk2.header, nblk2.coinbaseTxToMiner, transferTxs2);

            if (!nblk4.VerifyNumberOfTxs())
                throw new Exception("test12_16");

            if (nblk5.VerifyNumberOfTxs())
                throw new Exception("test12_17");

            for (int i = 1; i < blks.Length; i++)
            {
                TransactionalBlock tblk = blks[i] as TransactionalBlock;

                CurrencyUnit amount = tblk.GetActualRewardToMinerAndTxFee();
                CurrencyUnit amount2 = tblk.GetValidRewardToMinerAndTxFee(blkCons[i].prevTxOutss);
                CurrencyUnit amount3 = tblk.GetValidTxFee(blkCons[i].prevTxOutss);

                if (amount.rawAmount != (long)5400000000 + blkCons[i].feeRawAmount)
                    throw new Exception("test12_18");
                if (amount2.rawAmount != amount.rawAmount)
                    throw new Exception("test12_19");
                if (amount3.rawAmount != blkCons[i].feeRawAmount)
                    throw new Exception("test12_20");

                if (!tblk.VerifyRewardAndTxFee(blkCons[i].prevTxOutss))
                    throw new Exception("test12_21");
                if (!tblk.VerifyTransferTransaction(blkCons[i].prevTxOutss))
                    throw new Exception("test12_22");

                bool flag = false;
                TransactionOutput[][] invalidPrevTxOutss = new TransactionOutput[blkCons[i].prevTxOutss.Length][];
                for (int j = 0; j < invalidPrevTxOutss.Length; j++)
                {
                    invalidPrevTxOutss[j] = new TransactionOutput[blkCons[i].prevTxOutss[j].Length];
                    for (int k = 0; k < invalidPrevTxOutss[j].Length; k++)
                    {
                        if (j == 1 && k == 0)
                        {
                            invalidPrevTxOutss[j][k] = new TransactionOutput();
                            invalidPrevTxOutss[j][k].LoadVersion0(new Sha256Ripemd160Hash(), new CurrencyUnit(0));

                            flag = true;
                        }
                        else
                            invalidPrevTxOutss[j][k] = blkCons[i].prevTxOutss[j][k];
                    }
                }

                if (flag)
                {
                    if (tblk.VerifyRewardAndTxFee(invalidPrevTxOutss))
                        throw new Exception("test12_23");
                    if (tblk.VerifyTransferTransaction(invalidPrevTxOutss))
                        throw new Exception("test12_24");
                }
            }

            Console.WriteLine("test12_succeeded");
        }

        //BlockManagerのテスト2
        public static void Test13()
        {
            BlockGenerator bg = new BlockGenerator();

            Block[] blks = new Block[100];
            BlockContext[] blkCons = new BlockContext[blks.Length];
            for (int i = 0; i < blks.Length; i++)
            {
                blkCons[i] = bg.CreateNextValidBlock();
                blks[i] = blkCons[i].block;
            }

            string basepath = System.IO.Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);

            BlockManagerDB bmdb = new BlockManagerDB(basepath);
            string bmdbPath = bmdb.GetPath();

            if (File.Exists(bmdbPath))
                File.Delete(bmdbPath);

            BlockDB bdb = new BlockDB(basepath);
            string bdbPath = bdb.GetPath(0);

            if (File.Exists(bdbPath))
                File.Delete(bdbPath);

            BlockFilePointersDB bfpdb = new BlockFilePointersDB(basepath);
            string bfpPath = bfpdb.GetPath();

            if (File.Exists(bfpPath))
                File.Delete(bfpPath);

            BlockManager blkmanager = new BlockManager(bmdb, bdb, bfpdb, 1000, 1000, 300);

            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();

            for (int i = 0; i < blks.Length; i++)
                blkmanager.AddMainBlock(blks[i]);

            stopwatch.Stop();

            Console.WriteLine(string.Join(":", "test13_1", stopwatch.ElapsedMilliseconds.ToString() + "ms"));

            FileInfo fi = new FileInfo(bdbPath);

            Console.WriteLine(string.Join(":", "test13_1", fi.Length.ToString() + "bytes"));

            Block[] blks2 = new Block[blks.Length];

            stopwatch.Reset();
            stopwatch.Start();

            for (int i = 0; i < blks.Length; i++)
                blks2[i] = blkmanager.GetMainBlock(i);

            stopwatch.Stop();

            Console.WriteLine(string.Join(":", "test13_2", stopwatch.ElapsedMilliseconds.ToString() + "ms"));

            for (int i = 0; i < blks.Length; i++)
                if (!blks2[i].Id.Equals(blks[i].Id))
                    throw new Exception("test13_3");

            Console.WriteLine("test13_succeeded");
        }

        //BlockManagerのテスト3
        public static void Test14()
        {
            BlockGenerator bg = new BlockGenerator();

            Block[] blks = new Block[100];
            BlockContext[] blkCons = new BlockContext[blks.Length];
            for (int i = 0; i < blks.Length; i++)
            {
                blkCons[i] = bg.CreateNextValidBlock();
                blks[i] = blkCons[i].block;
            }

            string basepath = System.IO.Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);

            BlockManagerDB bmdb = new BlockManagerDB(basepath);
            string bmdbPath = bmdb.GetPath();

            if (File.Exists(bmdbPath))
                File.Delete(bmdbPath);

            BlockDB bdb = new BlockDB(basepath);
            string bdbPath = bdb.GetPath(0);

            if (File.Exists(bdbPath))
                File.Delete(bdbPath);

            BlockFilePointersDB bfpdb = new BlockFilePointersDB(basepath);
            string bfpPath = bfpdb.GetPath();

            if (File.Exists(bfpPath))
                File.Delete(bfpPath);

            BlockManager blkmanager = new BlockManager(bmdb, bdb, bfpdb, 10, 10, 3);

            for (int i = 0; i < blks.Length; i++)
                blkmanager.AddMainBlock(blks[i]);

            Block[] blks2 = new Block[blks.Length];

            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();

            for (int i = 0; i < blks.Length; i++)
                blks2[i] = blkmanager.GetMainBlock(i);

            stopwatch.Stop();

            Console.WriteLine(string.Join(":", "test14_1", stopwatch.ElapsedMilliseconds.ToString() + "ms"));

            for (int i = 0; i < blks.Length; i++)
                if (!blks2[i].Id.Equals(blks[i].Id))
                    throw new Exception("test14_2");

            Console.WriteLine("test14_succeeded");
        }

        //UtxoManagerのテスト4
        public static void Test15()
        {
            string basepath = System.IO.Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);

            UtxoFileAccessDB ufadb = new UtxoFileAccessDB(basepath);
            string ufadbPath = ufadb.GetPath();

            if (File.Exists(ufadbPath))
                File.Delete(ufadbPath);

            UtxoFilePointersDB ufpdb = new UtxoFilePointersDB(basepath);
            string ufpdbPath = ufpdb.GetPath();

            if (File.Exists(ufpdbPath))
                File.Delete(ufpdbPath);

            UtxoFilePointersTempDB ufptempdb = new UtxoFilePointersTempDB(basepath);
            string ufptempdbPath = ufptempdb.GetPath();

            if (File.Exists(ufptempdbPath))
                File.Delete(ufptempdbPath);

            UtxoDB utxodb = new UtxoDB(basepath);
            string utxodbPath = utxodb.GetPath();

            if (File.Exists(utxodbPath))
                File.Delete(utxodbPath);

            UtxoManager utxom = new UtxoManager(ufadb, ufpdb, ufptempdb, utxodb);

            BlockGenerator bg = new BlockGenerator();

            Block[] blks = new Block[100];
            BlockContext[] blkCons = new BlockContext[blks.Length];
            for (int i = 0; i < blks.Length; i++)
            {
                blkCons[i] = bg.CreateNextValidBlock();
                blks[i] = blkCons[i].block;

                Console.WriteLine("block" + i.ToString() + " created.");
            }

            for (int i = 0; i < blks.Length; i++)
            {
                utxodb.Open();

                utxom.ApplyBlock(blks[i], blkCons[i].prevTxOutss);

                utxom.SaveUFPTemp();

                utxodb.Close();

                utxodb.Open();

                foreach (var address in blkCons[i].unspentTxOuts.Keys)
                    foreach (var toc in blkCons[i].unspentTxOuts[address])
                        if (utxom.FindUtxo(address, toc.bIndex, toc.txIndex, toc.txOutIndex) == null)
                            throw new Exception("test15_1");

                foreach (var address in blkCons[i].spentTxOuts.Keys)
                    foreach (var toc in blkCons[i].spentTxOuts[address])
                        if (utxom.FindUtxo(address, toc.bIndex, toc.txIndex, toc.txOutIndex) != null)
                            throw new Exception("test15_2");

                utxodb.Close();

                Console.WriteLine("block" + i.ToString() + " apply tested.");
            }

            for (int i = blks.Length - 1; i > 0; i--)
            {
                utxodb.Open();

                utxom.RevertBlock(blks[i], blkCons[i].prevTxOutss);

                utxom.SaveUFPTemp();

                utxodb.Close();

                utxodb.Open();

                foreach (var address in blkCons[i - 1].unspentTxOuts.Keys)
                    foreach (var toc in blkCons[i - 1].unspentTxOuts[address])
                        if (utxom.FindUtxo(address, toc.bIndex, toc.txIndex, toc.txOutIndex) == null)
                            throw new Exception("test15_3");

                foreach (var address in blkCons[i - 1].spentTxOuts.Keys)
                    foreach (var toc in blkCons[i - 1].spentTxOuts[address])
                        if (utxom.FindUtxo(address, toc.bIndex, toc.txIndex, toc.txOutIndex) != null)
                            throw new Exception("test15_4");

                utxodb.Close();

                Console.WriteLine("block" + i.ToString() + " revert tested.");
            }

            Console.WriteLine("test15_succeeded");
        }

        //UtxoManagerのテスト5
        public static void Test16()
        {
            string basepath = System.IO.Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);

            UtxoFileAccessDB ufadb = new UtxoFileAccessDB(basepath);
            string ufadbPath = ufadb.GetPath();

            if (File.Exists(ufadbPath))
                File.Delete(ufadbPath);

            UtxoFilePointersDB ufpdb = new UtxoFilePointersDB(basepath);
            string ufpdbPath = ufpdb.GetPath();

            if (File.Exists(ufpdbPath))
                File.Delete(ufpdbPath);

            UtxoFilePointersTempDB ufptempdb = new UtxoFilePointersTempDB(basepath);
            string ufptempdbPath = ufptempdb.GetPath();

            if (File.Exists(ufptempdbPath))
                File.Delete(ufptempdbPath);

            UtxoDB utxodb = new UtxoDB(basepath);
            string utxodbPath = utxodb.GetPath();

            if (File.Exists(utxodbPath))
                File.Delete(utxodbPath);

            UtxoManager utxom = new UtxoManager(ufadb, ufpdb, ufptempdb, utxodb);

            BlockGenerator bg = new BlockGenerator();

            Block[] blks = new Block[100];
            BlockContext[] blkCons = new BlockContext[blks.Length];
            for (int i = 0; i < blks.Length; i++)
            {
                blkCons[i] = bg.CreateNextValidBlock();
                blks[i] = blkCons[i].block;

                Console.WriteLine("block" + i.ToString() + " created.");
            }

            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();

            for (int i = 0; i < blks.Length; i++)
            {
                utxodb.Open();

                utxom.ApplyBlock(blks[i], blkCons[i].prevTxOutss);

                utxom.SaveUFPTemp();

                utxodb.Close();
            }

            stopwatch.Stop();

            Console.WriteLine(string.Join(":", "test16_1", stopwatch.ElapsedMilliseconds.ToString() + "ms"));

            Console.WriteLine("test16_succeeded");
        }

        //BlockChainのテスト（分岐がない場合・採掘）
        public static void Test17()
        {
            string basepath = System.IO.Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);

            BlockchainAccessDB bcadb = new BlockchainAccessDB(basepath);
            string bcadbPath = bcadb.GetPath();

            if (File.Exists(bcadbPath))
                File.Delete(bcadbPath);

            BlockManagerDB bmdb = new BlockManagerDB(basepath);
            string bmdbPath = bmdb.GetPath();

            if (File.Exists(bmdbPath))
                File.Delete(bmdbPath);

            BlockDB bdb = new BlockDB(basepath);
            string bdbPath = bdb.GetPath(0);

            if (File.Exists(bdbPath))
                File.Delete(bdbPath);

            BlockFilePointersDB bfpdb = new BlockFilePointersDB(basepath);
            string bfpPath = bfpdb.GetPath();

            if (File.Exists(bfpPath))
                File.Delete(bfpPath);

            UtxoFileAccessDB ufadb = new UtxoFileAccessDB(basepath);
            string ufadbPath = ufadb.GetPath();

            if (File.Exists(ufadbPath))
                File.Delete(ufadbPath);

            UtxoFilePointersDB ufpdb = new UtxoFilePointersDB(basepath);
            string ufpdbPath = ufpdb.GetPath();

            if (File.Exists(ufpdbPath))
                File.Delete(ufpdbPath);

            UtxoFilePointersTempDB ufptempdb = new UtxoFilePointersTempDB(basepath);
            string ufptempdbPath = ufptempdb.GetPath();

            if (File.Exists(ufptempdbPath))
                File.Delete(ufptempdbPath);

            UtxoDB utxodb = new UtxoDB(basepath);
            string utxodbPath = utxodb.GetPath();

            if (File.Exists(utxodbPath))
                File.Delete(utxodbPath);

            BlockChain blockchain = new BlockChain(bcadb, bmdb, bdb, bfpdb, ufadb, ufpdb, ufptempdb, utxodb);

            BlockGenerator bg = new BlockGenerator();

            Block[] blks = new Block[100];
            BlockContext[] blkCons = new BlockContext[blks.Length];
            for (int i = 0; i < blks.Length; i++)
            {
                blkCons[i] = bg.CreateNextValidBlock();
                blks[i] = blkCons[i].block;

                Console.WriteLine("block" + i.ToString() + " created.");
            }

            Block[] blks2 = new Block[blks.Length];
            byte[] nonce = null;

            Func<long, TransactionalBlock> _indexToBlock = (index) => blks2[index] as TransactionalBlock;

            for (int i = 0; i < blks.Length; i++)
            {
                if (i == 0)
                {
                    blks2[i] = blks[i];

                    continue;
                }

                TransactionalBlock tblk = blks[i] as TransactionalBlock;

                TransactionalBlock tblk2 = TransactionalBlock.GetBlockTemplate(tblk.Index, tblk.coinbaseTxToMiner, tblk.transferTxs, _indexToBlock, 0);

                nonce = new byte[10];

                while (true)
                {
                    tblk2.UpdateTimestamp(DateTime.Now);
                    tblk2.UpdateNonce(nonce);

                    if (tblk2.Id.CompareTo(tblk2.header.difficulty.Target) <= 0)
                    {
                        blks2[i] = tblk2;

                        Console.WriteLine("block" + i.ToString() + " mined.");

                        break;
                    }

                    int index = nonce.Length.RandomNum();
                    int value = 256.RandomNum();

                    nonce[index] = (byte)value;
                }
            }

            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();

            for (int i = 0; i < blks.Length; i++)
                blockchain.UpdateChain(blks2[i]);

            stopwatch.Stop();

            Console.WriteLine(string.Join(":", "test17_1", stopwatch.ElapsedMilliseconds.ToString() + "ms"));

            Console.WriteLine("test17_succeeded");
        }

        //BlockChainのテスト（分岐がない場合・無効ブロックなどを追加しようとした場合）
        public static void Test18()
        {
            string basepath = System.IO.Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);

            BlockchainAccessDB bcadb = new BlockchainAccessDB(basepath);
            string bcadbPath = bcadb.GetPath();

            if (File.Exists(bcadbPath))
                File.Delete(bcadbPath);

            BlockManagerDB bmdb = new BlockManagerDB(basepath);
            string bmdbPath = bmdb.GetPath();

            if (File.Exists(bmdbPath))
                File.Delete(bmdbPath);

            BlockDB bdb = new BlockDB(basepath);
            string bdbPath = bdb.GetPath(0);

            if (File.Exists(bdbPath))
                File.Delete(bdbPath);

            BlockFilePointersDB bfpdb = new BlockFilePointersDB(basepath);
            string bfpPath = bfpdb.GetPath();

            if (File.Exists(bfpPath))
                File.Delete(bfpPath);

            UtxoFileAccessDB ufadb = new UtxoFileAccessDB(basepath);
            string ufadbPath = ufadb.GetPath();

            if (File.Exists(ufadbPath))
                File.Delete(ufadbPath);

            UtxoFilePointersDB ufpdb = new UtxoFilePointersDB(basepath);
            string ufpdbPath = ufpdb.GetPath();

            if (File.Exists(ufpdbPath))
                File.Delete(ufpdbPath);

            UtxoFilePointersTempDB ufptempdb = new UtxoFilePointersTempDB(basepath);
            string ufptempdbPath = ufptempdb.GetPath();

            if (File.Exists(ufptempdbPath))
                File.Delete(ufptempdbPath);

            UtxoDB utxodb = new UtxoDB(basepath);
            string utxodbPath = utxodb.GetPath();

            if (File.Exists(utxodbPath))
                File.Delete(utxodbPath);

            BlockChain blockchain = new BlockChain(bcadb, bmdb, bdb, bfpdb, ufadb, ufpdb, ufptempdb, utxodb);

            BlockGenerator bg = new BlockGenerator();

            Block[] blks = new Block[10];
            BlockContext[] blkCons = new BlockContext[blks.Length];
            for (int i = 0; i < blks.Length; i++)
            {
                blkCons[i] = bg.CreateNextValidBlock();
                blks[i] = blkCons[i].block;

                Console.WriteLine("block" + i.ToString() + " created.");
            }

            Block[] blks2 = new Block[blks.Length];
            byte[] nonce = null;

            Func<long, TransactionalBlock> _indexToBlock = (index) => blks2[index] as TransactionalBlock;

            for (int i = 0; i < blks.Length; i++)
            {
                if (i == 0)
                {
                    blks2[i] = blks[i];

                    continue;
                }

                TransactionalBlock tblk = blks[i] as TransactionalBlock;

                TransactionalBlock tblk2 = TransactionalBlock.GetBlockTemplate(tblk.Index, tblk.coinbaseTxToMiner, tblk.transferTxs, _indexToBlock, 0);

                nonce = new byte[10];

                while (true)
                {
                    tblk2.UpdateTimestamp(DateTime.Now);
                    tblk2.UpdateNonce(nonce);

                    if (tblk2.Id.CompareTo(tblk2.header.difficulty.Target) <= 0)
                    {
                        blks2[i] = tblk2;

                        Console.WriteLine("block" + i.ToString() + " mined.");

                        break;
                    }

                    int index = nonce.Length.RandomNum();
                    int value = 256.RandomNum();

                    nonce[index] = (byte)value;
                }
            }

            if (blockchain.blocksCurrent.value != 0)
                throw new Exception("test18_1");
            for (int i = 0; i < blockchain.pendingBlocks.Length; i++)
            {
                if (blockchain.pendingBlocks[i] != null)
                    throw new Exception("test18_2");
                if (blockchain.rejectedBlocks[i] != null)
                    throw new Exception("test18_3");
            }

            TransactionalBlock blk1 = blks2[1] as TransactionalBlock;
            TransactionalBlock blk2 = blks2[2] as TransactionalBlock;

            Creahash hashzero = new Creahash();

            BlockHeader bh5 = new BlockHeader();
            bh5.LoadVersion0(100, hashzero, DateTime.Now, blk1.Difficulty, new byte[10]);

            BlockHeader bh6 = new BlockHeader();
            bh6.LoadVersion0(101, hashzero, DateTime.Now, blk1.Difficulty, new byte[10]);

            TransactionalBlock blk100 = new NormalBlock();
            blk100.LoadVersion0(bh5, blk1.coinbaseTxToMiner, blk1.transferTxs);
            blk100.UpdateMerkleRootHash();

            TransactionalBlock blk101 = new NormalBlock();
            blk101.LoadVersion0(bh6, blk1.coinbaseTxToMiner, blk1.transferTxs);
            blk101.UpdateMerkleRootHash();

            blockchain.pendingBlocks[101] = new Dictionary<Creahash, Block>();
            blockchain.rejectedBlocks[101] = new Dictionary<Creahash, Block>();

            BlockChain.UpdateChainReturnType type1 = blockchain.UpdateChain(blks[0]);

            if (type1 != BlockChain.UpdateChainReturnType.updated)
                throw new Exception("test18_5");

            if (blockchain.blocksCurrent.value != 1)
                throw new Exception("test18_6");
            for (int i = 0; i < blockchain.pendingBlocks.Length; i++)
            {
                if (blockchain.pendingBlocks[i] != null)
                    throw new Exception("test18_7");
                if (blockchain.rejectedBlocks[i] != null)
                    throw new Exception("test18_8");
            }

            bool flag2 = false;
            try
            {
                blockchain.UpdateChain(blk101);
            }
            catch (InvalidOperationException)
            {
                flag2 = true;
            }
            if (!flag2)
                throw new Exception("test18_9");

            BlockChain.UpdateChainReturnType type2 = blockchain.UpdateChain(blk100);

            if (type2 != BlockChain.UpdateChainReturnType.pending)
                throw new Exception("test18_10");

            if (blockchain.blocksCurrent.value != 1)
                throw new Exception("test18_11");
            for (int i = 0; i < blockchain.pendingBlocks.Length; i++)
            {
                if (i == 101)
                {
                    if (blockchain.pendingBlocks[i] == null)
                        throw new Exception("test18_12");
                    if (blockchain.pendingBlocks[i].Count != 1)
                        throw new Exception("test18_13");
                    if (!blockchain.pendingBlocks[i].Keys.Contains(blk100.Id))
                        throw new Exception("test18_14");
                }
                else
                {
                    if (blockchain.pendingBlocks[i] != null)
                        throw new Exception("test18_15");
                }

                if (blockchain.rejectedBlocks[i] != null)
                    throw new Exception("test18_16");
            }

            BlockHeader bh1 = new BlockHeader();
            bh1.LoadVersion0(1, hashzero, DateTime.Now, blk1.Difficulty, new byte[10]);

            TransactionalBlock blk1_2 = new NormalBlock();
            blk1_2.LoadVersion0(bh1, blk1.coinbaseTxToMiner, blk1.transferTxs);
            blk1_2.UpdateMerkleRootHash();

            BlockChain.UpdateChainReturnType type3 = blockchain.UpdateChain(blk1_2);

            if (type3 != BlockChain.UpdateChainReturnType.pending)
                throw new Exception("test18_17");

            if (blockchain.blocksCurrent.value != 1)
                throw new Exception("test18_18");
            for (int i = 0; i < blockchain.pendingBlocks.Length; i++)
            {
                if (i == 2)
                {
                    if (blockchain.pendingBlocks[i] == null)
                        throw new Exception("test18_19");
                    if (blockchain.pendingBlocks[i].Count != 1)
                        throw new Exception("test18_20");
                    if (!blockchain.pendingBlocks[i].Keys.Contains(blk1_2.Id))
                        throw new Exception("test18_21");
                }
                else if (i == 101)
                {
                    if (blockchain.pendingBlocks[i] == null)
                        throw new Exception("test18_22");
                    if (blockchain.pendingBlocks[i].Count != 1)
                        throw new Exception("test18_23");
                    if (!blockchain.pendingBlocks[i].Keys.Contains(blk100.Id))
                        throw new Exception("test18_24");
                }
                else
                {
                    if (blockchain.pendingBlocks[i] != null)
                        throw new Exception("test18_25");
                }

                if (blockchain.rejectedBlocks[i] != null)
                    throw new Exception("test18_26");
            }

            BlockHeader bh2 = new BlockHeader();
            bh2.LoadVersion0(1, blks[0].Id, DateTime.Now, blk1.Difficulty, new byte[10]);

            TransactionalBlock blk1_3 = new NormalBlock();
            blk1_3.LoadVersion0(bh2, blk1.coinbaseTxToMiner, blk1.transferTxs);
            blk1_3.UpdateMerkleRootHash();

            BlockChain.UpdateChainReturnType type4 = blockchain.UpdateChain(blk1_3);

            if (type4 != BlockChain.UpdateChainReturnType.rejected)
                throw new Exception("test18_27");

            if (blockchain.blocksCurrent.value != 1)
                throw new Exception("test18_28");
            for (int i = 0; i < blockchain.pendingBlocks.Length; i++)
            {
                if (i == 2)
                {
                    if (blockchain.pendingBlocks[i] == null)
                        throw new Exception("test18_29");
                    if (blockchain.pendingBlocks[i].Count != 1)
                        throw new Exception("test18_30");
                    if (!blockchain.pendingBlocks[i].Keys.Contains(blk1_2.Id))
                        throw new Exception("test18_31");

                    if (blockchain.rejectedBlocks[i] == null)
                        throw new Exception("test18_32");
                    if (blockchain.rejectedBlocks[i].Count != 1)
                        throw new Exception("test18_33");
                    if (!blockchain.rejectedBlocks[i].Keys.Contains(blk1_3.Id))
                        throw new Exception("test18_34");
                }
                else if (i == 101)
                {
                    if (blockchain.pendingBlocks[i] == null)
                        throw new Exception("test18_35");
                    if (blockchain.pendingBlocks[i].Count != 1)
                        throw new Exception("test18_36");
                    if (!blockchain.pendingBlocks[i].Keys.Contains(blk100.Id))
                        throw new Exception("test18_37");

                    if (blockchain.rejectedBlocks[i] != null)
                        throw new Exception("test18_38");
                }
                else
                {
                    if (blockchain.pendingBlocks[i] != null)
                        throw new Exception("test18_39");

                    if (blockchain.rejectedBlocks[i] != null)
                        throw new Exception("test18_40");
                }
            }

            TransactionalBlock blk1_4 = TransactionalBlock.GetBlockTemplate(1, blk1.coinbaseTxToMiner, blk2.transferTxs, _indexToBlock, 0);

            while (true)
            {
                blk1_4.UpdateTimestamp(DateTime.Now);
                blk1_4.UpdateNonce(nonce);

                if (blk1_4.Id.CompareTo(blk1_4.header.difficulty.Target) <= 0)
                {
                    Console.WriteLine("block1_4 mined.");

                    break;
                }

                int index = nonce.Length.RandomNum();
                int value = 256.RandomNum();

                nonce[index] = (byte)value;
            }

            BlockChain.UpdateChainReturnType type5 = blockchain.UpdateChain(blk1_4);

            if (type5 != BlockChain.UpdateChainReturnType.rejected)
                throw new Exception("test18_41");

            if (blockchain.blocksCurrent.value != 1)
                throw new Exception("test18_42");
            for (int i = 0; i < blockchain.pendingBlocks.Length; i++)
            {
                if (i == 2)
                {
                    if (blockchain.pendingBlocks[i] == null)
                        throw new Exception("test18_43");
                    if (blockchain.pendingBlocks[i].Count != 1)
                        throw new Exception("test18_44");
                    if (!blockchain.pendingBlocks[i].Keys.Contains(blk1_2.Id))
                        throw new Exception("test18_45");

                    if (blockchain.rejectedBlocks[i] == null)
                        throw new Exception("test18_46");
                    if (blockchain.rejectedBlocks[i].Count != 2)
                        throw new Exception("test18_47");
                    if (!blockchain.rejectedBlocks[i].Keys.Contains(blk1_3.Id))
                        throw new Exception("test18_48");
                    if (!blockchain.rejectedBlocks[i].Keys.Contains(blk1_4.Id))
                        throw new Exception("test18_49");
                }
                else if (i == 101)
                {
                    if (blockchain.pendingBlocks[i] == null)
                        throw new Exception("test18_50");
                    if (blockchain.pendingBlocks[i].Count != 1)
                        throw new Exception("test18_51");
                    if (!blockchain.pendingBlocks[i].Keys.Contains(blk100.Id))
                        throw new Exception("test18_52");

                    if (blockchain.rejectedBlocks[i] != null)
                        throw new Exception("test18_53");
                }
                else
                {
                    if (blockchain.pendingBlocks[i] != null)
                        throw new Exception("test18_54");

                    if (blockchain.rejectedBlocks[i] != null)
                        throw new Exception("test18_55");
                }
            }

            BlockChain.UpdateChainReturnType type8 = blockchain.UpdateChain(blk1_3);

            if (type8 != BlockChain.UpdateChainReturnType.invariable)
                throw new Exception("test18_56");

            if (blockchain.blocksCurrent.value != 1)
                throw new Exception("test18_57");
            for (int i = 0; i < blockchain.pendingBlocks.Length; i++)
            {
                if (i == 2)
                {
                    if (blockchain.pendingBlocks[i] == null)
                        throw new Exception("test18_58");
                    if (blockchain.pendingBlocks[i].Count != 1)
                        throw new Exception("test18_59");
                    if (!blockchain.pendingBlocks[i].Keys.Contains(blk1_2.Id))
                        throw new Exception("test18_60");

                    if (blockchain.rejectedBlocks[i] == null)
                        throw new Exception("test18_61");
                    if (blockchain.rejectedBlocks[i].Count != 2)
                        throw new Exception("test18_62");
                    if (!blockchain.rejectedBlocks[i].Keys.Contains(blk1_3.Id))
                        throw new Exception("test18_63");
                    if (!blockchain.rejectedBlocks[i].Keys.Contains(blk1_4.Id))
                        throw new Exception("test18_64");
                }
                else if (i == 101)
                {
                    if (blockchain.pendingBlocks[i] == null)
                        throw new Exception("test18_65");
                    if (blockchain.pendingBlocks[i].Count != 1)
                        throw new Exception("test18_66");
                    if (!blockchain.pendingBlocks[i].Keys.Contains(blk100.Id))
                        throw new Exception("test18_67");

                    if (blockchain.rejectedBlocks[i] != null)
                        throw new Exception("test18_68");
                }
                else
                {
                    if (blockchain.pendingBlocks[i] != null)
                        throw new Exception("test18_69");

                    if (blockchain.rejectedBlocks[i] != null)
                        throw new Exception("test18_70");
                }
            }

            BlockChain.UpdateChainReturnType type6 = blockchain.UpdateChain(blk1);

            if (type6 != BlockChain.UpdateChainReturnType.updated)
                throw new Exception("test18_71");

            BlockChain.UpdateChainReturnType type7 = blockchain.UpdateChain(blk1_3);

            if (type7 != BlockChain.UpdateChainReturnType.invariable)
                throw new Exception("test18_72");

            if (blockchain.blocksCurrent.value != 2)
                throw new Exception("test18_73");
            for (int i = 0; i < blockchain.pendingBlocks.Length; i++)
            {
                if (i == 2)
                {
                    if (blockchain.pendingBlocks[i] == null)
                        throw new Exception("test18_74");
                    if (blockchain.pendingBlocks[i].Count != 1)
                        throw new Exception("test18_75");
                    if (!blockchain.pendingBlocks[i].Keys.Contains(blk1_2.Id))
                        throw new Exception("test18_76");

                    if (blockchain.rejectedBlocks[i] == null)
                        throw new Exception("test18_77");
                    if (blockchain.rejectedBlocks[i].Count != 2)
                        throw new Exception("test18_78");
                    if (!blockchain.rejectedBlocks[i].Keys.Contains(blk1_3.Id))
                        throw new Exception("test18_79");
                    if (!blockchain.rejectedBlocks[i].Keys.Contains(blk1_4.Id))
                        throw new Exception("test18_80");
                }
                else if (i == 101)
                {
                    if (blockchain.pendingBlocks[i] == null)
                        throw new Exception("test18_81");
                    if (blockchain.pendingBlocks[i].Count != 1)
                        throw new Exception("test18_82");
                    if (!blockchain.pendingBlocks[i].Keys.Contains(blk100.Id))
                        throw new Exception("test18_83");

                    if (blockchain.rejectedBlocks[i] != null)
                        throw new Exception("test18_84");
                }
                else
                {
                    if (blockchain.pendingBlocks[i] != null)
                        throw new Exception("test18_85");

                    if (blockchain.rejectedBlocks[i] != null)
                        throw new Exception("test18_86");
                }
            }

            BlockHeader bh3 = new BlockHeader();
            bh3.LoadVersion0(2, hashzero, DateTime.Now, blk2.Difficulty, new byte[10]);

            TransactionalBlock blk2_2 = new NormalBlock();
            blk2_2.LoadVersion0(bh3, blk2.coinbaseTxToMiner, blk2.transferTxs);
            blk2_2.UpdateMerkleRootHash();

            BlockChain.UpdateChainReturnType type9 = blockchain.UpdateChain(blk2_2);

            if (type9 != BlockChain.UpdateChainReturnType.pending)
                throw new Exception("test18_87");

            BlockHeader bh4 = new BlockHeader();
            bh4.LoadVersion0(2, blk1_2.Id, DateTime.Now, blk2.Difficulty, new byte[10]);

            TransactionalBlock blk2_3 = new NormalBlock();
            blk2_3.LoadVersion0(bh4, blk2.coinbaseTxToMiner, blk2.transferTxs);
            blk2_3.UpdateMerkleRootHash();

            BlockChain.UpdateChainReturnType type10 = blockchain.UpdateChain(blk2_3);

            if (type10 != BlockChain.UpdateChainReturnType.pending)
                throw new Exception("test18_88");

            BlockHeader bh7 = new BlockHeader();
            bh7.LoadVersion0(2, blk1_3.Id, DateTime.Now, blk2.Difficulty, new byte[10]);

            TransactionalBlock blk2_4 = new NormalBlock();
            blk2_4.LoadVersion0(bh7, blk2.coinbaseTxToMiner, blk2.transferTxs);
            blk2_4.UpdateMerkleRootHash();

            BlockChain.UpdateChainReturnType type13 = blockchain.UpdateChain(blk2_4);

            if (type13 != BlockChain.UpdateChainReturnType.rejected)
                throw new Exception("test18_91");

            for (int i = 2; i < blks2.Length; i++)
            {
                BlockChain.UpdateChainReturnType type11 = blockchain.UpdateChain(blks2[i]);

                if (type11 != BlockChain.UpdateChainReturnType.updated)
                    throw new Exception("test18_89");
            }

            TransactionalBlock blk10 = TransactionalBlock.GetBlockTemplate(10, blk2.coinbaseTxToMiner, blk2.transferTxs, _indexToBlock, 0);

            while (true)
            {
                blk10.UpdateTimestamp(DateTime.Now);
                blk10.UpdateNonce(nonce);

                if (blk10.Id.CompareTo(blk10.header.difficulty.Target) <= 0)
                {
                    Console.WriteLine("block10 mined.");

                    break;
                }

                int index = nonce.Length.RandomNum();
                int value = 256.RandomNum();

                nonce[index] = (byte)value;
            }

            BlockChain.UpdateChainReturnType type12 = blockchain.UpdateChain(blk10);

            if (type12 != BlockChain.UpdateChainReturnType.rejected)
                throw new Exception("test18_90");

            Console.WriteLine("test18_succeeded");
        }

        //BlockChainのテスト（分岐がない場合・採掘・順番通りに追加されなかった場合）
        public static void Test19()
        {
            string basepath = System.IO.Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);

            BlockchainAccessDB bcadb = new BlockchainAccessDB(basepath);
            string bcadbPath = bcadb.GetPath();

            if (File.Exists(bcadbPath))
                File.Delete(bcadbPath);

            BlockManagerDB bmdb = new BlockManagerDB(basepath);
            string bmdbPath = bmdb.GetPath();

            if (File.Exists(bmdbPath))
                File.Delete(bmdbPath);

            BlockDB bdb = new BlockDB(basepath);
            string bdbPath = bdb.GetPath(0);

            if (File.Exists(bdbPath))
                File.Delete(bdbPath);

            BlockFilePointersDB bfpdb = new BlockFilePointersDB(basepath);
            string bfpPath = bfpdb.GetPath();

            if (File.Exists(bfpPath))
                File.Delete(bfpPath);

            UtxoFileAccessDB ufadb = new UtxoFileAccessDB(basepath);
            string ufadbPath = ufadb.GetPath();

            if (File.Exists(ufadbPath))
                File.Delete(ufadbPath);

            UtxoFilePointersDB ufpdb = new UtxoFilePointersDB(basepath);
            string ufpdbPath = ufpdb.GetPath();

            if (File.Exists(ufpdbPath))
                File.Delete(ufpdbPath);

            UtxoFilePointersTempDB ufptempdb = new UtxoFilePointersTempDB(basepath);
            string ufptempdbPath = ufptempdb.GetPath();

            if (File.Exists(ufptempdbPath))
                File.Delete(ufptempdbPath);

            UtxoDB utxodb = new UtxoDB(basepath);
            string utxodbPath = utxodb.GetPath();

            if (File.Exists(utxodbPath))
                File.Delete(utxodbPath);

            BlockChain blockchain = new BlockChain(bcadb, bmdb, bdb, bfpdb, ufadb, ufpdb, ufptempdb, utxodb);

            BlockGenerator bg = new BlockGenerator();

            Block[] blks = new Block[8];
            BlockContext[] blkCons = new BlockContext[blks.Length];
            for (int i = 0; i < blks.Length; i++)
            {
                blkCons[i] = bg.CreateNextValidBlock();
                blks[i] = blkCons[i].block;

                Console.WriteLine("block" + i.ToString() + " created.");
            }

            Block[] blks2 = new Block[blks.Length];
            byte[] nonce = null;

            Func<long, TransactionalBlock> _indexToBlock = (index) => blks2[index] as TransactionalBlock;

            for (int i = 0; i < blks.Length; i++)
            {
                if (i == 0)
                {
                    blks2[i] = blks[i];

                    continue;
                }

                TransactionalBlock tblk = blks[i] as TransactionalBlock;

                TransactionalBlock tblk2 = TransactionalBlock.GetBlockTemplate(tblk.Index, tblk.coinbaseTxToMiner, tblk.transferTxs, _indexToBlock, 0);

                nonce = new byte[10];

                while (true)
                {
                    tblk2.UpdateTimestamp(DateTime.Now);
                    tblk2.UpdateNonce(nonce);

                    if (tblk2.Id.CompareTo(tblk2.header.difficulty.Target) <= 0)
                    {
                        blks2[i] = tblk2;

                        Console.WriteLine("block" + i.ToString() + " mined.");

                        break;
                    }

                    int index = nonce.Length.RandomNum();
                    int value = 256.RandomNum();

                    nonce[index] = (byte)value;
                }
            }

            BlockChain.UpdateChainReturnType type = blockchain.UpdateChain(blks2[2]);

            if (type != BlockChain.UpdateChainReturnType.pending)
                throw new Exception("test19_1");

            if (blockchain.blocksCurrent.value != 0)
                throw new Exception("test19_2");
            for (int i = 0; i < blockchain.pendingBlocks.Length; i++)
            {
                if (i == 3)
                {
                    if (blockchain.pendingBlocks[i] == null)
                        throw new Exception("test19_3");
                    if (blockchain.pendingBlocks[i].Count != 1)
                        throw new Exception("test19_4");
                    if (!blockchain.pendingBlocks[i].Keys.Contains(blks2[2].Id))
                        throw new Exception("test19_5");
                }
                else
                {
                    if (blockchain.pendingBlocks[i] != null)
                        throw new Exception("test19_6");
                }

                if (blockchain.rejectedBlocks[i] != null)
                    throw new Exception("test19_7");
            }

            BlockChain.UpdateChainReturnType type2 = blockchain.UpdateChain(blks2[1]);

            if (type2 != BlockChain.UpdateChainReturnType.pending)
                throw new Exception("test19_8");

            if (blockchain.blocksCurrent.value != 0)
                throw new Exception("test19_9");
            for (int i = 0; i < blockchain.pendingBlocks.Length; i++)
            {
                if (i == 2)
                {
                    if (blockchain.pendingBlocks[i] == null)
                        throw new Exception("test19_10");
                    if (blockchain.pendingBlocks[i].Count != 1)
                        throw new Exception("test19_11");
                    if (!blockchain.pendingBlocks[i].Keys.Contains(blks2[1].Id))
                        throw new Exception("test19_12");
                }
                else if (i == 3)
                {
                    if (blockchain.pendingBlocks[i] == null)
                        throw new Exception("test19_13");
                    if (blockchain.pendingBlocks[i].Count != 1)
                        throw new Exception("test19_14");
                    if (!blockchain.pendingBlocks[i].Keys.Contains(blks2[2].Id))
                        throw new Exception("test19_15");
                }
                else
                {
                    if (blockchain.pendingBlocks[i] != null)
                        throw new Exception("test19_16");
                }

                if (blockchain.rejectedBlocks[i] != null)
                    throw new Exception("test19_17");
            }

            BlockChain.UpdateChainReturnType type3 = blockchain.UpdateChain(blks2[0]);

            if (type3 != BlockChain.UpdateChainReturnType.updated)
                throw new Exception("test19_18");
            if (blockchain.headBlockIndex != 2)
                throw new Exception("test19_19");

            if (blockchain.blocksCurrent.value != 3)
                throw new Exception("test19_20");
            for (int i = 0; i < blockchain.pendingBlocks.Length; i++)
            {
                if (i == 2 || i == 3)
                {
                    if (blockchain.pendingBlocks[i] == null)
                        throw new Exception("test19_21");
                    if (blockchain.pendingBlocks[i].Count != 0)
                        throw new Exception("test19_22");
                }
                else
                {
                    if (blockchain.pendingBlocks[i] != null)
                        throw new Exception("test19_23");
                }

                if (blockchain.rejectedBlocks[i] != null)
                    throw new Exception("test19_24");
            }

            BlockChain.UpdateChainReturnType type4 = blockchain.UpdateChain(blks2[6]);
            BlockChain.UpdateChainReturnType type5 = blockchain.UpdateChain(blks2[7]);
            BlockChain.UpdateChainReturnType type6 = blockchain.UpdateChain(blks2[4]);
            BlockChain.UpdateChainReturnType type7 = blockchain.UpdateChain(blks2[5]);
            BlockChain.UpdateChainReturnType type8 = blockchain.UpdateChain(blks2[3]);

            if (type4 != BlockChain.UpdateChainReturnType.pending)
                throw new Exception("test19_25");
            if (type5 != BlockChain.UpdateChainReturnType.pending)
                throw new Exception("test19_26");
            if (type6 != BlockChain.UpdateChainReturnType.pending)
                throw new Exception("test19_27");
            if (type7 != BlockChain.UpdateChainReturnType.pending)
                throw new Exception("test19_28");
            if (type8 != BlockChain.UpdateChainReturnType.updated)
                throw new Exception("test19_29");
            if (blockchain.headBlockIndex != 7)
                throw new Exception("test19_30");

            utxodb.Open();

            foreach (var address in blkCons[7].unspentTxOuts.Keys)
                foreach (var toc in blkCons[7].unspentTxOuts[address])
                    if (blockchain.FindUtxo(address, toc.bIndex, toc.txIndex, toc.txOutIndex) == null)
                        throw new Exception("test19_31");

            foreach (var address in blkCons[7].spentTxOuts.Keys)
                foreach (var toc in blkCons[7].spentTxOuts[address])
                    if (blockchain.FindUtxo(address, toc.bIndex, toc.txIndex, toc.txOutIndex) != null)
                        throw new Exception("test19_32");

            utxodb.Close();

            Console.WriteLine("test19_succeeded");
        }

        //BlockChainのテスト（分岐がある場合・採掘）
        public static void Test20()
        {
            string basepath = System.IO.Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);

            BlockchainAccessDB bcadb = new BlockchainAccessDB(basepath);
            string bcadbPath = bcadb.GetPath();

            if (File.Exists(bcadbPath))
                File.Delete(bcadbPath);

            BlockManagerDB bmdb = new BlockManagerDB(basepath);
            string bmdbPath = bmdb.GetPath();

            if (File.Exists(bmdbPath))
                File.Delete(bmdbPath);

            BlockDB bdb = new BlockDB(basepath);
            string bdbPath = bdb.GetPath(0);

            if (File.Exists(bdbPath))
                File.Delete(bdbPath);

            BlockFilePointersDB bfpdb = new BlockFilePointersDB(basepath);
            string bfpPath = bfpdb.GetPath();

            if (File.Exists(bfpPath))
                File.Delete(bfpPath);

            UtxoFileAccessDB ufadb = new UtxoFileAccessDB(basepath);
            string ufadbPath = ufadb.GetPath();

            if (File.Exists(ufadbPath))
                File.Delete(ufadbPath);

            UtxoFilePointersDB ufpdb = new UtxoFilePointersDB(basepath);
            string ufpdbPath = ufpdb.GetPath();

            if (File.Exists(ufpdbPath))
                File.Delete(ufpdbPath);

            UtxoFilePointersTempDB ufptempdb = new UtxoFilePointersTempDB(basepath);
            string ufptempdbPath = ufptempdb.GetPath();

            if (File.Exists(ufptempdbPath))
                File.Delete(ufptempdbPath);

            UtxoDB utxodb = new UtxoDB(basepath);
            string utxodbPath = utxodb.GetPath();

            if (File.Exists(utxodbPath))
                File.Delete(utxodbPath);

            BlockChain blockchain = new BlockChain(bcadb, bmdb, bdb, bfpdb, ufadb, ufpdb, ufptempdb, utxodb);

            BlockGenerator bg = new BlockGenerator();

            Block[] blks = new Block[10];
            BlockContext[] blkCons = new BlockContext[blks.Length];
            for (int i = 0; i < blks.Length; i++)
            {
                blkCons[i] = bg.CreateNextValidBlock();
                blks[i] = blkCons[i].block;

                Console.WriteLine("block" + i.ToString() + " created.");
            }

            Block[] blks2 = new Block[blks.Length];
            double cumulativeDiff1 = 0.0;
            byte[] nonce = null;

            Func<long, TransactionalBlock> _indexToBlock = (index) => blks2[index] as TransactionalBlock;

            for (int i = 0; i < blks.Length; i++)
            {
                if (i == 0)
                {
                    blks2[i] = blks[i];
                    cumulativeDiff1 += blks2[i].Difficulty.Diff;

                    Console.WriteLine("block" + i.ToString() + "_1 " + blks2[i].Difficulty.Diff.ToString() + " " + cumulativeDiff1.ToString());

                    continue;
                }

                TransactionalBlock tblk = blks[i] as TransactionalBlock;

                TransactionalBlock tblk2 = TransactionalBlock.GetBlockTemplate(tblk.Index, tblk.coinbaseTxToMiner, tblk.transferTxs, _indexToBlock, 0);

                nonce = new byte[10];

                while (true)
                {
                    tblk2.UpdateTimestamp(DateTime.Now);
                    tblk2.UpdateNonce(nonce);

                    if (tblk2.Id.CompareTo(tblk2.header.difficulty.Target) <= 0)
                    {
                        blks2[i] = tblk2;
                        cumulativeDiff1 += blks2[i].Difficulty.Diff;

                        Console.WriteLine("block" + i.ToString() + "_1 mined. " + blks2[i].Difficulty.Diff.ToString() + " " + cumulativeDiff1.ToString());

                        break;
                    }

                    int index = nonce.Length.RandomNum();
                    int value = 256.RandomNum();

                    nonce[index] = (byte)value;
                }
            }

            Console.WriteLine();

            Block[] blks3 = new Block[blks.Length];
            double cumulativeDiff2 = 0.0;

            Func<long, TransactionalBlock> _indexToBlock2 = (index) => blks3[index] as TransactionalBlock;

            for (int i = 0; i < blks.Length; i++)
            {
                if (i == 0)
                {
                    blks3[i] = blks[i];
                    cumulativeDiff2 += blks3[i].Difficulty.Diff;

                    Console.WriteLine("block" + i.ToString() + "_2 " + blks3[i].Difficulty.Diff.ToString() + " " + cumulativeDiff2.ToString());

                    continue;
                }

                TransactionalBlock tblk = blks[i] as TransactionalBlock;

                TransactionalBlock tblk3 = TransactionalBlock.GetBlockTemplate(tblk.Index, tblk.coinbaseTxToMiner, tblk.transferTxs, _indexToBlock2, 0);

                nonce = new byte[10];

                while (true)
                {
                    tblk3.UpdateTimestamp(DateTime.Now);
                    tblk3.UpdateNonce(nonce);

                    if (tblk3.Id.CompareTo(tblk3.header.difficulty.Target) <= 0)
                    {
                        blks3[i] = tblk3;
                        cumulativeDiff2 += blks3[i].Difficulty.Diff;

                        Console.WriteLine("block" + i.ToString() + "_2 mined. " + blks3[i].Difficulty.Diff.ToString() + " " + cumulativeDiff2.ToString());

                        break;
                    }

                    int index = nonce.Length.RandomNum();
                    int value = 256.RandomNum();

                    nonce[index] = (byte)value;

                    Thread.Sleep(1);
                }
            }

            if (cumulativeDiff2 >= cumulativeDiff1)
            {
                Console.WriteLine("test20_not_tested.");

                return;
            }

            cumulativeDiff1 = 0.0;

            int minIndex = 0;
            for (int i = 0; i < blks.Length; i++)
            {
                cumulativeDiff1 += blks2[i].Difficulty.Diff;

                if (cumulativeDiff1 > cumulativeDiff2)
                {
                    minIndex = i;

                    break;
                }
            }

            for (int i = 0; i < blks.Length; i++)
            {
                BlockChain.UpdateChainReturnType type = blockchain.UpdateChain(blks3[i]);

                if (type != BlockChain.UpdateChainReturnType.updated)
                    throw new Exception("test20_1");
            }

            for (int i = 1; i < minIndex; i++)
            {
                BlockChain.UpdateChainReturnType type = blockchain.UpdateChain(blks2[i]);

                if (type != BlockChain.UpdateChainReturnType.pending)
                    throw new Exception("test20_2");
            }

            BlockChain.UpdateChainReturnType type2 = blockchain.UpdateChain(blks2[minIndex]);

            if (type2 != BlockChain.UpdateChainReturnType.updated)
                throw new Exception("test20_3");

            utxodb.Open();

            foreach (var address in blkCons[minIndex].unspentTxOuts.Keys)
                foreach (var toc in blkCons[minIndex].unspentTxOuts[address])
                    if (blockchain.FindUtxo(address, toc.bIndex, toc.txIndex, toc.txOutIndex) == null)
                        throw new Exception("test20_4");

            foreach (var address in blkCons[minIndex].spentTxOuts.Keys)
                foreach (var toc in blkCons[minIndex].spentTxOuts[address])
                    if (blockchain.FindUtxo(address, toc.bIndex, toc.txIndex, toc.txOutIndex) != null)
                        throw new Exception("test20_5");

            utxodb.Close();

            Console.WriteLine("test20_succeeded");
        }

        //BlockChainのテスト（分岐がある場合・採掘・交互に追加していく場合）
        public static void Test21()
        {
            string basepath = System.IO.Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);

            BlockchainAccessDB bcadb = new BlockchainAccessDB(basepath);
            string bcadbPath = bcadb.GetPath();

            if (File.Exists(bcadbPath))
                File.Delete(bcadbPath);

            BlockManagerDB bmdb = new BlockManagerDB(basepath);
            string bmdbPath = bmdb.GetPath();

            if (File.Exists(bmdbPath))
                File.Delete(bmdbPath);

            BlockDB bdb = new BlockDB(basepath);
            string bdbPath = bdb.GetPath(0);

            if (File.Exists(bdbPath))
                File.Delete(bdbPath);

            BlockFilePointersDB bfpdb = new BlockFilePointersDB(basepath);
            string bfpPath = bfpdb.GetPath();

            if (File.Exists(bfpPath))
                File.Delete(bfpPath);

            UtxoFileAccessDB ufadb = new UtxoFileAccessDB(basepath);
            string ufadbPath = ufadb.GetPath();

            if (File.Exists(ufadbPath))
                File.Delete(ufadbPath);

            UtxoFilePointersDB ufpdb = new UtxoFilePointersDB(basepath);
            string ufpdbPath = ufpdb.GetPath();

            if (File.Exists(ufpdbPath))
                File.Delete(ufpdbPath);

            UtxoFilePointersTempDB ufptempdb = new UtxoFilePointersTempDB(basepath);
            string ufptempdbPath = ufptempdb.GetPath();

            if (File.Exists(ufptempdbPath))
                File.Delete(ufptempdbPath);

            UtxoDB utxodb = new UtxoDB(basepath);
            string utxodbPath = utxodb.GetPath();

            if (File.Exists(utxodbPath))
                File.Delete(utxodbPath);

            BlockChain blockchain = new BlockChain(bcadb, bmdb, bdb, bfpdb, ufadb, ufpdb, ufptempdb, utxodb);

            BlockGenerator bg = new BlockGenerator();

            Block[] blks = new Block[10];
            BlockContext[] blkCons = new BlockContext[blks.Length];
            for (int i = 0; i < blks.Length; i++)
            {
                blkCons[i] = bg.CreateNextValidBlock();
                blks[i] = blkCons[i].block;

                Console.WriteLine("block" + i.ToString() + " created.");
            }

            Block[] blks2 = new Block[blks.Length];
            double cumulativeDiff1 = 0.0;
            byte[] nonce = null;

            Func<long, TransactionalBlock> _indexToBlock = (index) => blks2[index] as TransactionalBlock;

            for (int i = 0; i < blks.Length; i++)
            {
                if (i == 0)
                {
                    blks2[i] = blks[i];
                    cumulativeDiff1 += blks2[i].Difficulty.Diff;

                    Console.WriteLine("block" + i.ToString() + "_1 " + blks2[i].Difficulty.Diff.ToString() + " " + cumulativeDiff1.ToString());

                    continue;
                }

                TransactionalBlock tblk = blks[i] as TransactionalBlock;

                TransactionalBlock tblk2 = TransactionalBlock.GetBlockTemplate(tblk.Index, tblk.coinbaseTxToMiner, tblk.transferTxs, _indexToBlock, 0);

                nonce = new byte[10];

                while (true)
                {
                    tblk2.UpdateTimestamp(DateTime.Now);
                    tblk2.UpdateNonce(nonce);

                    if (tblk2.Id.CompareTo(tblk2.header.difficulty.Target) <= 0)
                    {
                        blks2[i] = tblk2;
                        cumulativeDiff1 += blks2[i].Difficulty.Diff;

                        Console.WriteLine("block" + i.ToString() + "_1 mined. " + blks2[i].Difficulty.Diff.ToString() + " " + cumulativeDiff1.ToString());

                        break;
                    }

                    int index = nonce.Length.RandomNum();
                    int value = 256.RandomNum();

                    nonce[index] = (byte)value;
                }
            }

            Console.WriteLine();

            Block[] blks3 = new Block[blks.Length];
            double cumulativeDiff2 = 0.0;

            Func<long, TransactionalBlock> _indexToBlock2 = (index) => blks3[index] as TransactionalBlock;

            for (int i = 0; i < blks.Length; i++)
            {
                if (i == 0)
                {
                    blks3[i] = blks[i];
                    cumulativeDiff2 += blks3[i].Difficulty.Diff;

                    Console.WriteLine("block" + i.ToString() + "_2 " + blks3[i].Difficulty.Diff.ToString() + " " + cumulativeDiff2.ToString());

                    continue;
                }

                TransactionalBlock tblk = blks[i] as TransactionalBlock;

                TransactionalBlock tblk3 = TransactionalBlock.GetBlockTemplate(tblk.Index, tblk.coinbaseTxToMiner, tblk.transferTxs, _indexToBlock2, 0);

                nonce = new byte[10];

                while (true)
                {
                    tblk3.UpdateTimestamp(DateTime.Now);
                    tblk3.UpdateNonce(nonce);

                    if (tblk3.Id.CompareTo(tblk3.header.difficulty.Target) <= 0)
                    {
                        blks3[i] = tblk3;
                        cumulativeDiff2 += blks3[i].Difficulty.Diff;

                        Console.WriteLine("block" + i.ToString() + "_2 mined. " + blks3[i].Difficulty.Diff.ToString() + " " + cumulativeDiff2.ToString());

                        break;
                    }

                    int index = nonce.Length.RandomNum();
                    int value = 256.RandomNum();

                    nonce[index] = (byte)value;
                }
            }

            cumulativeDiff1 = 0.0;
            cumulativeDiff2 = 0.0;

            blockchain.UpdateChain(blks2[0]);

            for (int i = 1; i < blks.Length; i++)
            {
                cumulativeDiff1 += blks2[i].Difficulty.Diff;

                BlockChain.UpdateChainReturnType type1 = blockchain.UpdateChain(blks2[i]);

                if (cumulativeDiff1 > cumulativeDiff2)
                {
                    if (type1 != BlockChain.UpdateChainReturnType.updated)
                        throw new Exception("test21_1");
                }
                else
                {
                    if (type1 != BlockChain.UpdateChainReturnType.pending)
                        throw new Exception("test21_3");
                }

                cumulativeDiff2 += blks3[i].Difficulty.Diff;

                BlockChain.UpdateChainReturnType type2 = blockchain.UpdateChain(blks3[i]);

                if (cumulativeDiff2 > cumulativeDiff1)
                {
                    if (type2 != BlockChain.UpdateChainReturnType.updated)
                        throw new Exception("test21_2");
                }
                else
                {
                    if (type2 != BlockChain.UpdateChainReturnType.pending)
                        throw new Exception("test21_4");
                }
            }

            utxodb.Open();

            foreach (var address in blkCons[9].unspentTxOuts.Keys)
                foreach (var toc in blkCons[9].unspentTxOuts[address])
                    if (blockchain.FindUtxo(address, toc.bIndex, toc.txIndex, toc.txOutIndex) == null)
                        throw new Exception("test21_5");

            foreach (var address in blkCons[9].spentTxOuts.Keys)
                foreach (var toc in blkCons[9].spentTxOuts[address])
                    if (blockchain.FindUtxo(address, toc.bIndex, toc.txIndex, toc.txOutIndex) != null)
                        throw new Exception("test21_6");

            utxodb.Close();

            Console.WriteLine("test21_succeeded");
        }

        //BlockChainのテスト（分岐がある場合・採掘・順番通りに追加されなかった場合）
        public static void Test22()
        {
            string basepath = System.IO.Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);

            BlockchainAccessDB bcadb = new BlockchainAccessDB(basepath);
            string bcadbPath = bcadb.GetPath();

            if (File.Exists(bcadbPath))
                File.Delete(bcadbPath);

            BlockManagerDB bmdb = new BlockManagerDB(basepath);
            string bmdbPath = bmdb.GetPath();

            if (File.Exists(bmdbPath))
                File.Delete(bmdbPath);

            BlockDB bdb = new BlockDB(basepath);
            string bdbPath = bdb.GetPath(0);

            if (File.Exists(bdbPath))
                File.Delete(bdbPath);

            BlockFilePointersDB bfpdb = new BlockFilePointersDB(basepath);
            string bfpPath = bfpdb.GetPath();

            if (File.Exists(bfpPath))
                File.Delete(bfpPath);

            UtxoFileAccessDB ufadb = new UtxoFileAccessDB(basepath);
            string ufadbPath = ufadb.GetPath();

            if (File.Exists(ufadbPath))
                File.Delete(ufadbPath);

            UtxoFilePointersDB ufpdb = new UtxoFilePointersDB(basepath);
            string ufpdbPath = ufpdb.GetPath();

            if (File.Exists(ufpdbPath))
                File.Delete(ufpdbPath);

            UtxoFilePointersTempDB ufptempdb = new UtxoFilePointersTempDB(basepath);
            string ufptempdbPath = ufptempdb.GetPath();

            if (File.Exists(ufptempdbPath))
                File.Delete(ufptempdbPath);

            UtxoDB utxodb = new UtxoDB(basepath);
            string utxodbPath = utxodb.GetPath();

            if (File.Exists(utxodbPath))
                File.Delete(utxodbPath);

            BlockChain blockchain = new BlockChain(bcadb, bmdb, bdb, bfpdb, ufadb, ufpdb, ufptempdb, utxodb);

            BlockGenerator bg = new BlockGenerator();

            Block[] blks = new Block[10];
            double[] cumulativeDiffs0 = new double[blks.Length];
            BlockContext[] blkCons = new BlockContext[blks.Length];
            for (int i = 0; i < blks.Length; i++)
            {
                blkCons[i] = bg.CreateNextValidBlock();
                blks[i] = blkCons[i].block;
                cumulativeDiffs0[i] = i == 0 ? blks[i].Difficulty.Diff : cumulativeDiffs0[i - 1] + blks[i].Difficulty.Diff;

                Console.WriteLine("block" + i.ToString() + " created.");
            }

            Block[] blks2 = new Block[blks.Length];
            double[] cumulativeDiffs1 = new double[blks.Length];
            byte[] nonce = null;

            Func<long, TransactionalBlock> _indexToBlock = (index) => blks2[index] as TransactionalBlock;

            for (int i = 0; i < blks.Length; i++)
            {
                if (i == 0)
                {
                    blks2[i] = blks[i];
                    cumulativeDiffs1[i] = blks2[i].Difficulty.Diff;

                    Console.WriteLine("block" + i.ToString() + "_1 " + blks2[i].Difficulty.Diff.ToString() + " " + cumulativeDiffs1[i].ToString());

                    continue;
                }

                TransactionalBlock tblk = blks[i] as TransactionalBlock;

                TransactionalBlock tblk2 = TransactionalBlock.GetBlockTemplate(tblk.Index, tblk.coinbaseTxToMiner, tblk.transferTxs, _indexToBlock, 0);

                nonce = new byte[10];

                while (true)
                {
                    tblk2.UpdateTimestamp(DateTime.Now);
                    tblk2.UpdateNonce(nonce);

                    if (tblk2.Id.CompareTo(tblk2.header.difficulty.Target) <= 0)
                    {
                        blks2[i] = tblk2;
                        cumulativeDiffs1[i] = cumulativeDiffs1[i - 1] + blks2[i].Difficulty.Diff;

                        Console.WriteLine("block" + i.ToString() + "_1 mined. " + blks2[i].Difficulty.Diff.ToString() + " " + cumulativeDiffs1[i].ToString());

                        break;
                    }

                    int index = nonce.Length.RandomNum();
                    int value = 256.RandomNum();

                    nonce[index] = (byte)value;
                }
            }

            int forkIndex1 = 0;
            int forkIndex2 = 0;

            int[] forkIndexes = (blks.Length - 1).RandomNums();
            if (forkIndexes[0] < forkIndexes[1])
            {
                forkIndex1 = forkIndexes[0] + 1;
                forkIndex2 = forkIndexes[1] + 1;
            }
            else
            {
                forkIndex1 = forkIndexes[1] + 1;
                forkIndex2 = forkIndexes[0] + 1;
            }

            Block[] blks3 = new Block[blks.Length];
            double[] cumulativeDiffs2 = new double[blks.Length];

            Func<long, TransactionalBlock> _indexToBlock2 = (index) => index >= forkIndex1 ? blks3[index] as TransactionalBlock : blks2[index] as TransactionalBlock;

            for (int i = forkIndex1; i < blks.Length; i++)
            {
                TransactionalBlock tblk = blks[i] as TransactionalBlock;

                TransactionalBlock tblk3 = TransactionalBlock.GetBlockTemplate(tblk.Index, tblk.coinbaseTxToMiner, tblk.transferTxs, _indexToBlock2, 0);

                nonce = new byte[10];

                while (true)
                {
                    tblk3.UpdateTimestamp(DateTime.Now);
                    tblk3.UpdateNonce(nonce);

                    if (tblk3.Id.CompareTo(tblk3.header.difficulty.Target) <= 0)
                    {
                        blks3[i] = tblk3;
                        if (i == forkIndex1)
                            cumulativeDiffs2[i] = cumulativeDiffs1[i - 1] + blks3[i].Difficulty.Diff;
                        else
                            cumulativeDiffs2[i] = cumulativeDiffs2[i - 1] + blks3[i].Difficulty.Diff;

                        Console.WriteLine("block" + i.ToString() + "_2 mined. " + blks3[i].Difficulty.Diff.ToString() + " " + cumulativeDiffs2[i].ToString());

                        break;
                    }

                    int index = nonce.Length.RandomNum();
                    int value = 256.RandomNum();

                    nonce[index] = (byte)value;
                }
            }

            Block[] blks4 = new Block[blks.Length];
            double[] cumulativeDiffs3 = new double[blks.Length];

            Func<long, TransactionalBlock> _indexToBlock3 = (index) => index >= forkIndex2 ? blks4[index] as TransactionalBlock : blks2[index] as TransactionalBlock;

            for (int i = forkIndex2; i < blks.Length; i++)
            {
                TransactionalBlock tblk = blks[i] as TransactionalBlock;

                TransactionalBlock tblk3 = TransactionalBlock.GetBlockTemplate(tblk.Index, tblk.coinbaseTxToMiner, tblk.transferTxs, _indexToBlock3, 0);

                nonce = new byte[10];

                while (true)
                {
                    tblk3.UpdateTimestamp(DateTime.Now);
                    tblk3.UpdateNonce(nonce);

                    if (tblk3.Id.CompareTo(tblk3.header.difficulty.Target) <= 0)
                    {
                        blks4[i] = tblk3;
                        if (i == forkIndex2)
                            cumulativeDiffs3[i] = cumulativeDiffs1[i - 1] + blks4[i].Difficulty.Diff;
                        else
                            cumulativeDiffs3[i] = cumulativeDiffs3[i - 1] + blks4[i].Difficulty.Diff;

                        Console.WriteLine("block" + i.ToString() + "_3 mined. " + blks4[i].Difficulty.Diff.ToString() + " " + cumulativeDiffs3[i].ToString());

                        break;
                    }

                    int index = nonce.Length.RandomNum();
                    int value = 256.RandomNum();

                    nonce[index] = (byte)value;
                }
            }

            bool?[] map1 = new bool?[blks.Length];
            bool?[] map2 = new bool?[blks.Length];
            bool?[] map3 = new bool?[blks.Length];
            bool?[] map4 = new bool?[blks.Length];

            for (int i = 0; i < blks.Length; i++)
            {
                map1[i] = i == 0 ? (bool?)null : false;
                map2[i] = i == 0 ? (bool?)null : false;
                map3[i] = i < forkIndex1 ? (bool?)null : false;
                map4[i] = i < forkIndex2 ? (bool?)null : false;
            }

            blockchain.UpdateChain(blks[0]);

            int[] randomnums = (4 * blks.Length).RandomNums();

            double cumulativeDiff = 0.0;
            int main = 0;
            int rejectedIndex = 0;

            for (int i = 0; i < randomnums.Length; i++)
            {
                int keiretsu = randomnums[i] / blks.Length;
                int index = randomnums[i] % blks.Length;

                if ((keiretsu == 0 && map1[index] == null) || (keiretsu == 1 && map2[index] == null) || (keiretsu == 2 && map3[index] == null) || (keiretsu == 3 && map4[index] == null))
                    continue;

                if (keiretsu == 0)
                {
                    BlockChain.UpdateChainReturnType type = blockchain.UpdateChain(blks[index]);

                    bool flag = false;

                    for (int j = index - 1; j > 0; j--)
                        if (!map1[j].Value)
                        {
                            flag = true;

                            break;
                        }

                    if (!flag && index != 1)
                    {
                        if (type != BlockChain.UpdateChainReturnType.rejected)
                            throw new Exception("test22_1");
                    }
                    else
                    {
                        if (!flag)
                        {
                            int headIndex = index;
                            for (int j = index + 1; j < blks.Length; j++)
                                if (map1[j].Value)
                                    headIndex = j;
                                else
                                    break;

                            if (cumulativeDiff >= cumulativeDiffs0[headIndex])
                                flag = true;
                            else
                                rejectedIndex = headIndex;
                        }

                        if (flag && type != BlockChain.UpdateChainReturnType.pending)
                            throw new Exception("test22_2");
                        if (!flag && type != BlockChain.UpdateChainReturnType.updatedAndRejected)
                            throw new Exception("test22_3");
                    }

                    map1[index] = true;
                }
                else if (keiretsu == 1)
                {
                    BlockChain.UpdateChainReturnType type = blockchain.UpdateChain(blks2[index]);

                    bool flag = false;

                    for (int j = index - 1; j > 0; j--)
                        if (!map2[j].Value)
                        {
                            flag = true;

                            break;
                        }

                    if (!flag)
                    {
                        int headIndex1 = index;
                        for (int j = index + 1; j < blks.Length; j++)
                            if (map2[j].Value)
                                headIndex1 = j;
                            else
                                break;

                        double cdiff = cumulativeDiffs1[headIndex1];
                        int m = 1;

                        if (headIndex1 + 1 >= forkIndex1 && map3[forkIndex1].Value)
                        {
                            int headIndex2 = forkIndex1;
                            for (int j = forkIndex1 + 1; j < blks.Length; j++)
                                if (map3[j].Value)
                                    headIndex2 = j;
                                else
                                    break;

                            //<未実装>等しい場合の対処
                            if (cumulativeDiffs2[headIndex2] > cdiff)
                            {
                                cdiff = cumulativeDiffs2[headIndex2];
                                m = 2;
                            }
                            else if (cumulativeDiffs2[headIndex2] == cdiff)
                            {
                                Console.WriteLine("not_implemented_test_case");

                                return;
                            }
                        }

                        if (headIndex1 + 1 >= forkIndex2 && map4[forkIndex2].Value)
                        {
                            int headIndex3 = forkIndex2;
                            for (int j = forkIndex2 + 1; j < blks.Length; j++)
                                if (map4[j].Value)
                                    headIndex3 = j;
                                else
                                    break;

                            //<未実装>等しい場合の対処
                            if (cumulativeDiffs3[headIndex3] > cdiff)
                            {
                                cdiff = cumulativeDiffs3[headIndex3];
                                m = 3;
                            }
                            else if (cumulativeDiffs3[headIndex3] == cdiff)
                            {
                                Console.WriteLine("not_implemented_test_case");

                                return;
                            }
                        }

                        if (cumulativeDiff >= cdiff)
                            flag = true;
                        else
                        {
                            cumulativeDiff = cdiff;
                            main = m;
                        }
                    }

                    if (flag && type != BlockChain.UpdateChainReturnType.pending)
                        throw new Exception("test22_4");
                    if (!flag && type != BlockChain.UpdateChainReturnType.updated)
                        throw new Exception("test22_5");

                    map2[index] = true;
                }
                else if (keiretsu == 2)
                {
                    BlockChain.UpdateChainReturnType type = blockchain.UpdateChain(blks3[index]);

                    bool flag = false;

                    for (int j = index - 1; j >= forkIndex1; j--)
                        if (!map3[j].Value)
                        {
                            flag = true;

                            break;
                        }
                    if (!flag)
                        for (int j = forkIndex1 - 1; j > 0; j--)
                            if (!map2[j].Value)
                            {
                                flag = true;

                                break;
                            }

                    if (!flag)
                    {
                        int headIndex = index;
                        for (int j = index + 1; j < blks.Length; j++)
                            if (map3[j].Value)
                                headIndex = j;
                            else
                                break;

                        if (cumulativeDiff >= cumulativeDiffs2[headIndex])
                            flag = true;
                        else
                        {
                            cumulativeDiff = cumulativeDiffs2[headIndex];
                            main = 2;
                        }
                    }

                    if (flag && type != BlockChain.UpdateChainReturnType.pending)
                        throw new Exception("test22_6");
                    if (!flag && type != BlockChain.UpdateChainReturnType.updated)
                        throw new Exception("test22_7");

                    map3[index] = true;
                }
                else if (keiretsu == 3)
                {
                    BlockChain.UpdateChainReturnType type = blockchain.UpdateChain(blks4[index]);

                    bool flag = false;

                    for (int j = index - 1; j >= forkIndex2; j--)
                        if (!map4[j].Value)
                        {
                            flag = true;

                            break;
                        }
                    if (!flag)
                        for (int j = forkIndex2 - 1; j > 0; j--)
                            if (!map2[j].Value)
                            {
                                flag = true;

                                break;
                            }

                    if (!flag)
                    {
                        int headIndex = index;
                        for (int j = index + 1; j < blks.Length; j++)
                            if (map4[j].Value)
                                headIndex = j;
                            else
                                break;

                        if (cumulativeDiff >= cumulativeDiffs3[headIndex])
                            flag = true;
                        else
                        {
                            cumulativeDiff = cumulativeDiffs3[headIndex];
                            main = 3;
                        }
                    }

                    if (flag && type != BlockChain.UpdateChainReturnType.pending)
                        throw new Exception("test22_8");
                    if (!flag && type != BlockChain.UpdateChainReturnType.updated)
                        throw new Exception("test22_9");

                    map4[index] = true;
                }

                bool flag2 = true;
                for (int j = 1; j < blks.Length; j++)
                {
                    if (map1[j].Value)
                    {
                        if (flag2 && j <= rejectedIndex)
                        {
                            if (blockchain.rejectedBlocks[j + 1] == null || !blockchain.rejectedBlocks[j + 1].Keys.Contains(blks[j].Id))
                                throw new Exception("test22_10");
                            if (blockchain.pendingBlocks[j + 1] != null && blockchain.pendingBlocks[j + 1].Keys.Contains(blks[j].Id))
                                throw new Exception("test22_11");
                        }
                        else
                        {
                            if (blockchain.rejectedBlocks[j + 1] != null && blockchain.rejectedBlocks[j + 1].Keys.Contains(blks[j].Id))
                                throw new Exception("test22_12");
                            if (blockchain.pendingBlocks[j + 1] == null || !blockchain.pendingBlocks[j + 1].Keys.Contains(blks[j].Id))
                                throw new Exception("test22_13");
                        }
                    }
                    else
                    {
                        flag2 = false;

                        if (blockchain.rejectedBlocks[j + 1] != null && blockchain.rejectedBlocks[j + 1].Keys.Contains(blks[j].Id))
                            throw new Exception("test22_14");
                        if (blockchain.pendingBlocks[j + 1] != null && blockchain.pendingBlocks[j + 1].Keys.Contains(blks[j].Id))
                            throw new Exception("test22_15");
                    }
                }

                bool flag3 = true;
                for (int j = 1; j < blks.Length; j++)
                {
                    if (blockchain.rejectedBlocks[j + 1] != null && blockchain.rejectedBlocks[j + 1].Keys.Contains(blks2[j].Id))
                        throw new Exception("test22_16");

                    if (map2[j].Value)
                    {
                        if (flag3 && (main == 1 || (main == 2 && j < forkIndex1) || (main == 3 && j < forkIndex2)))
                        {
                            if (blockchain.pendingBlocks[j + 1] != null && blockchain.pendingBlocks[j + 1].Keys.Contains(blks2[j].Id))
                                throw new Exception("test22_17");
                        }
                        else
                        {
                            if (blockchain.pendingBlocks[j + 1] == null || !blockchain.pendingBlocks[j + 1].Keys.Contains(blks2[j].Id))
                                throw new Exception("test22_18");
                        }
                    }
                    else
                    {
                        flag3 = false;

                        if (blockchain.pendingBlocks[j + 1] != null && blockchain.pendingBlocks[j + 1].Keys.Contains(blks2[j].Id))
                            throw new Exception("test22_19");
                    }
                }

                bool flag4 = true;
                for (int j = forkIndex1; j < blks.Length; j++)
                {
                    if (blockchain.rejectedBlocks[j + 1] != null && blockchain.rejectedBlocks[j + 1].Keys.Contains(blks3[j].Id))
                        throw new Exception("test22_20");

                    if (map3[j].Value)
                    {
                        if (flag4 && main == 2)
                        {
                            if (blockchain.pendingBlocks[j + 1] != null && blockchain.pendingBlocks[j + 1].Keys.Contains(blks3[j].Id))
                                throw new Exception("test22_21");
                        }
                        else
                        {
                            if (blockchain.pendingBlocks[j + 1] == null || !blockchain.pendingBlocks[j + 1].Keys.Contains(blks3[j].Id))
                                throw new Exception("test22_22");
                        }
                    }
                    else
                    {
                        flag4 = false;

                        if (blockchain.pendingBlocks[j + 1] != null && blockchain.pendingBlocks[j + 1].Keys.Contains(blks3[j].Id))
                            throw new Exception("test22_23");
                    }
                }

                bool flag5 = true;
                for (int j = forkIndex2; j < blks.Length; j++)
                {
                    if (blockchain.rejectedBlocks[j + 1] != null && blockchain.rejectedBlocks[j + 1].Keys.Contains(blks4[j].Id))
                        throw new Exception("test22_24");

                    if (map4[j].Value)
                    {
                        if (flag5 && main == 3)
                        {
                            if (blockchain.pendingBlocks[j + 1] != null && blockchain.pendingBlocks[j + 1].Keys.Contains(blks4[j].Id))
                                throw new Exception("test22_25");
                        }
                        else
                        {
                            if (blockchain.pendingBlocks[j + 1] == null || !blockchain.pendingBlocks[j + 1].Keys.Contains(blks4[j].Id))
                                throw new Exception("test22_26");
                        }
                    }
                    else
                    {
                        flag5 = false;

                        if (blockchain.pendingBlocks[j + 1] != null && blockchain.pendingBlocks[j + 1].Keys.Contains(blks4[j].Id))
                            throw new Exception("test22_27");
                    }
                }
            }

            Block headBlock = blockchain.GetHeadBlock();

            if (main == 1)
            {
                if (!headBlock.Id.Equals(blks2[9].Id))
                    throw new Exception("test22_28");
            }
            else if (main == 2)
            {
                if (!headBlock.Id.Equals(blks3[9].Id))
                    throw new Exception("test22_29");
            }
            else if (main == 3)
            {
                if (!headBlock.Id.Equals(blks4[9].Id))
                    throw new Exception("test22_30");
            }
            else
                throw new InvalidOperationException();

            utxodb.Open();

            foreach (var address in blkCons[9].unspentTxOuts.Keys)
                foreach (var toc in blkCons[9].unspentTxOuts[address])
                    if (blockchain.FindUtxo(address, toc.bIndex, toc.txIndex, toc.txOutIndex) == null)
                        throw new Exception("test22_31");

            foreach (var address in blkCons[9].spentTxOuts.Keys)
                foreach (var toc in blkCons[9].spentTxOuts[address])
                    if (blockchain.FindUtxo(address, toc.bIndex, toc.txIndex, toc.txOutIndex) != null)
                        throw new Exception("test22_32");

            utxodb.Close();

            Console.WriteLine("test22_succeeded");
        }

        //BlockChainのテスト（分岐がある場合・採掘・長過ぎる場合）
        public static void Test23()
        {
            string basepath = System.IO.Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);

            BlockchainAccessDB bcadb = new BlockchainAccessDB(basepath);
            string bcadbPath = bcadb.GetPath();

            if (File.Exists(bcadbPath))
                File.Delete(bcadbPath);

            BlockManagerDB bmdb = new BlockManagerDB(basepath);
            string bmdbPath = bmdb.GetPath();

            if (File.Exists(bmdbPath))
                File.Delete(bmdbPath);

            BlockDB bdb = new BlockDB(basepath);
            string bdbPath = bdb.GetPath(0);

            if (File.Exists(bdbPath))
                File.Delete(bdbPath);

            BlockFilePointersDB bfpdb = new BlockFilePointersDB(basepath);
            string bfpPath = bfpdb.GetPath();

            if (File.Exists(bfpPath))
                File.Delete(bfpPath);

            UtxoFileAccessDB ufadb = new UtxoFileAccessDB(basepath);
            string ufadbPath = ufadb.GetPath();

            if (File.Exists(ufadbPath))
                File.Delete(ufadbPath);

            UtxoFilePointersDB ufpdb = new UtxoFilePointersDB(basepath);
            string ufpdbPath = ufpdb.GetPath();

            if (File.Exists(ufpdbPath))
                File.Delete(ufpdbPath);

            UtxoFilePointersTempDB ufptempdb = new UtxoFilePointersTempDB(basepath);
            string ufptempdbPath = ufptempdb.GetPath();

            if (File.Exists(ufptempdbPath))
                File.Delete(ufptempdbPath);

            UtxoDB utxodb = new UtxoDB(basepath);
            string utxodbPath = utxodb.GetPath();

            if (File.Exists(utxodbPath))
                File.Delete(utxodbPath);

            BlockChain blockchain = new BlockChain(bcadb, bmdb, bdb, bfpdb, ufadb, ufpdb, ufptempdb, utxodb, 100, 3, 1000, 1000);

            BlockGenerator bg = new BlockGenerator();

            Block[] blks = new Block[10];
            BlockContext[] blkCons = new BlockContext[blks.Length];
            for (int i = 0; i < blks.Length; i++)
            {
                blkCons[i] = bg.CreateNextValidBlock();
                blks[i] = blkCons[i].block;

                Console.WriteLine("block" + i.ToString() + " created.");
            }

            Block[] blks2 = new Block[blks.Length];
            double cumulativeDiff1 = 0.0;
            byte[] nonce = null;

            Func<long, TransactionalBlock> _indexToBlock = (index) => blks2[index] as TransactionalBlock;

            for (int i = 0; i < blks.Length; i++)
            {
                if (i == 0)
                {
                    blks2[i] = blks[i];
                    cumulativeDiff1 += blks2[i].Difficulty.Diff;

                    Console.WriteLine("block" + i.ToString() + "_1 " + blks2[i].Difficulty.Diff.ToString() + " " + cumulativeDiff1.ToString());

                    continue;
                }

                TransactionalBlock tblk = blks[i] as TransactionalBlock;

                TransactionalBlock tblk2 = TransactionalBlock.GetBlockTemplate(tblk.Index, tblk.coinbaseTxToMiner, tblk.transferTxs, _indexToBlock, 0);

                nonce = new byte[10];

                while (true)
                {
                    tblk2.UpdateTimestamp(DateTime.Now);
                    tblk2.UpdateNonce(nonce);

                    if (tblk2.Id.CompareTo(tblk2.header.difficulty.Target) <= 0)
                    {
                        blks2[i] = tblk2;
                        cumulativeDiff1 += blks2[i].Difficulty.Diff;

                        Console.WriteLine("block" + i.ToString() + "_1 mined. " + blks2[i].Difficulty.Diff.ToString() + " " + cumulativeDiff1.ToString());

                        break;
                    }

                    int index = nonce.Length.RandomNum();
                    int value = 256.RandomNum();

                    nonce[index] = (byte)value;
                }
            }

            Console.WriteLine();

            Block[] blks3 = new Block[blks.Length];
            double cumulativeDiff2 = 0.0;

            Func<long, TransactionalBlock> _indexToBlock2 = (index) => blks3[index] as TransactionalBlock;

            for (int i = 0; i < blks.Length; i++)
            {
                if (i == 0)
                {
                    blks3[i] = blks[i];
                    cumulativeDiff2 += blks3[i].Difficulty.Diff;

                    Console.WriteLine("block" + i.ToString() + "_2 " + blks3[i].Difficulty.Diff.ToString() + " " + cumulativeDiff2.ToString());

                    continue;
                }

                TransactionalBlock tblk = blks[i] as TransactionalBlock;

                TransactionalBlock tblk3 = TransactionalBlock.GetBlockTemplate(tblk.Index, tblk.coinbaseTxToMiner, tblk.transferTxs, _indexToBlock2, 0);

                nonce = new byte[10];

                while (true)
                {
                    tblk3.UpdateTimestamp(DateTime.Now);
                    tblk3.UpdateNonce(nonce);

                    if (tblk3.Id.CompareTo(tblk3.header.difficulty.Target) <= 0)
                    {
                        blks3[i] = tblk3;
                        cumulativeDiff2 += blks3[i].Difficulty.Diff;

                        Console.WriteLine("block" + i.ToString() + "_2 mined. " + blks3[i].Difficulty.Diff.ToString() + " " + cumulativeDiff2.ToString());

                        break;
                    }

                    int index = nonce.Length.RandomNum();
                    int value = 256.RandomNum();

                    nonce[index] = (byte)value;
                }
            }

            for (int i = 0; i < blks.Length; i++)
                blockchain.UpdateChain(blks2[i]);

            for (int i = blks.Length - 1; i >= blks.Length - 3; i--)
            {
                BlockChain.UpdateChainReturnType type = blockchain.UpdateChain(blks3[i]);

                if (i == blks.Length - 3)
                {
                    if (type != BlockChain.UpdateChainReturnType.rejected)
                        throw new Exception("test23_1");

                    for (int j = 0; j < blks.Length - 3; j++)
                        if (blockchain.rejectedBlocks[j + 1] != null)
                            throw new Exception("test23_2");
                    for (int j = blks.Length - 3; j < blks.Length; j++)
                        if (blockchain.rejectedBlocks[j + 1] == null || !blockchain.rejectedBlocks[j + 1].Keys.Contains(blks3[j].Id))
                            throw new Exception("test23_3");

                    for (int j = 0; j <= blks.Length - 3; j++)
                        if (blockchain.pendingBlocks[j + 1] != null)
                            throw new Exception("test23_4");
                    for (int j = blks.Length - 3 + 1; j < blks.Length; j++)
                        if (blockchain.pendingBlocks[j + 1] == null || blockchain.pendingBlocks[j + 1].Keys.Count != 0)
                            throw new Exception("test23_5");
                }
                else
                {
                    if (type != BlockChain.UpdateChainReturnType.pending)
                        throw new Exception("test23_6");
                }
            }

            Console.WriteLine("test23_succeeded");
        }

        //BlockChainのテスト（分岐がある場合・採掘・後ろが無効な場合）
        public static void Test24()
        {
            string basepath = System.IO.Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);

            BlockchainAccessDB bcadb = new BlockchainAccessDB(basepath);
            string bcadbPath = bcadb.GetPath();

            if (File.Exists(bcadbPath))
                File.Delete(bcadbPath);

            BlockManagerDB bmdb = new BlockManagerDB(basepath);
            string bmdbPath = bmdb.GetPath();

            if (File.Exists(bmdbPath))
                File.Delete(bmdbPath);

            BlockDB bdb = new BlockDB(basepath);
            string bdbPath = bdb.GetPath(0);

            if (File.Exists(bdbPath))
                File.Delete(bdbPath);

            BlockFilePointersDB bfpdb = new BlockFilePointersDB(basepath);
            string bfpPath = bfpdb.GetPath();

            if (File.Exists(bfpPath))
                File.Delete(bfpPath);

            UtxoFileAccessDB ufadb = new UtxoFileAccessDB(basepath);
            string ufadbPath = ufadb.GetPath();

            if (File.Exists(ufadbPath))
                File.Delete(ufadbPath);

            UtxoFilePointersDB ufpdb = new UtxoFilePointersDB(basepath);
            string ufpdbPath = ufpdb.GetPath();

            if (File.Exists(ufpdbPath))
                File.Delete(ufpdbPath);

            UtxoFilePointersTempDB ufptempdb = new UtxoFilePointersTempDB(basepath);
            string ufptempdbPath = ufptempdb.GetPath();

            if (File.Exists(ufptempdbPath))
                File.Delete(ufptempdbPath);

            UtxoDB utxodb = new UtxoDB(basepath);
            string utxodbPath = utxodb.GetPath();

            if (File.Exists(utxodbPath))
                File.Delete(utxodbPath);

            BlockChain blockchain = new BlockChain(bcadb, bmdb, bdb, bfpdb, ufadb, ufpdb, ufptempdb, utxodb, 100, 300, 1000, 1000);

            BlockGenerator bg = new BlockGenerator();

            Block[] blks = new Block[10];
            BlockContext[] blkCons = new BlockContext[blks.Length];
            for (int i = 0; i < blks.Length; i++)
            {
                blkCons[i] = bg.CreateNextValidBlock();
                blks[i] = blkCons[i].block;

                Console.WriteLine("block" + i.ToString() + " created.");
            }

            Block[] blks2 = new Block[blks.Length];
            double cumulativeDiff1 = 0.0;
            byte[] nonce = null;

            Func<long, TransactionalBlock> _indexToBlock = (index) => blks2[index] as TransactionalBlock;

            for (int i = 0; i < blks.Length; i++)
            {
                if (i == 0)
                {
                    blks2[i] = blks[i];
                    cumulativeDiff1 += blks2[i].Difficulty.Diff;

                    Console.WriteLine("block" + i.ToString() + "_1 " + blks2[i].Difficulty.Diff.ToString() + " " + cumulativeDiff1.ToString());

                    continue;
                }

                TransactionalBlock tblk = blks[i] as TransactionalBlock;

                TransactionalBlock tblk2 = TransactionalBlock.GetBlockTemplate(tblk.Index, tblk.coinbaseTxToMiner, tblk.transferTxs, _indexToBlock, 0);

                nonce = new byte[10];

                while (true)
                {
                    tblk2.UpdateTimestamp(DateTime.Now);
                    tblk2.UpdateNonce(nonce);

                    if (tblk2.Id.CompareTo(tblk2.header.difficulty.Target) <= 0)
                    {
                        blks2[i] = tblk2;
                        cumulativeDiff1 += blks2[i].Difficulty.Diff;

                        Console.WriteLine("block" + i.ToString() + "_1 mined. " + blks2[i].Difficulty.Diff.ToString() + " " + cumulativeDiff1.ToString());

                        break;
                    }

                    int index = nonce.Length.RandomNum();
                    int value = 256.RandomNum();

                    nonce[index] = (byte)value;
                }
            }

            TransactionalBlock tblk2_1 = blks2[1] as TransactionalBlock;
            TransactionalBlock tblk2_2 = blks2[2] as TransactionalBlock;

            TransactionalBlock blk3_2 = TransactionalBlock.GetBlockTemplate(2, tblk2_2.coinbaseTxToMiner, tblk2_2.transferTxs, _indexToBlock, 0);

            while (true)
            {
                blk3_2.UpdateTimestamp(DateTime.Now);
                blk3_2.UpdateNonce(nonce);

                if (blk3_2.Id.CompareTo(blk3_2.header.difficulty.Target) <= 0)
                {
                    Console.WriteLine("block3_2 mined. " + blk3_2.Difficulty.Diff.ToString());

                    break;
                }

                int index = nonce.Length.RandomNum();
                int value = 256.RandomNum();

                nonce[index] = (byte)value;
            }

            BlockHeader bh3 = new BlockHeader();
            bh3.LoadVersion0(3, blk3_2.Id, DateTime.Now, blks2[1].Difficulty, new byte[10]);
            NormalBlock blk3_3 = new NormalBlock();
            blk3_3.LoadVersion0(bh3, tblk2_1.coinbaseTxToMiner, tblk2_1.transferTxs);
            blk3_3.UpdateMerkleRootHash();

            int forkLength = (int)Math.Floor(cumulativeDiff1 / blks2[1].Difficulty.Diff + 10);

            TransactionalBlock[] blks3 = new TransactionalBlock[forkLength];

            blks3[0] = blk3_3;

            for (int i = 1; i < blks3.Length; i++)
            {
                BlockHeader bh = new BlockHeader();
                bh.LoadVersion0(i + 3, blks3[i - 1].Id, DateTime.Now, blks2[1].Difficulty, new byte[10]);
                NormalBlock blk3 = new NormalBlock();
                blk3.LoadVersion0(bh, tblk2_1.coinbaseTxToMiner, tblk2_1.transferTxs);
                blk3.UpdateMerkleRootHash();

                blks3[i] = blk3;
            }

            BlockHeader bh4 = new BlockHeader();
            bh4.LoadVersion0(4, blk3_3.Id, DateTime.Now, blks2[1].Difficulty, new byte[10]);
            NormalBlock blk4 = new NormalBlock();
            blk4.LoadVersion0(bh4, tblk2_1.coinbaseTxToMiner, tblk2_1.transferTxs);
            blk4.UpdateMerkleRootHash();

            BlockHeader bh10 = new BlockHeader();
            bh10.LoadVersion0(10, blks2[9].Id, DateTime.Now, blks2[1].Difficulty, new byte[10]);
            NormalBlock blk5_10 = new NormalBlock();
            blk5_10.LoadVersion0(bh10, tblk2_1.coinbaseTxToMiner, tblk2_1.transferTxs);
            blk5_10.UpdateMerkleRootHash();

            TransactionalBlock[] blks5 = new TransactionalBlock[5];

            blks5[0] = blk5_10;

            for (int i = 1; i < blks5.Length; i++)
            {
                BlockHeader bh = new BlockHeader();
                bh.LoadVersion0(i + 10, blks5[i - 1].Id, DateTime.Now, blks2[1].Difficulty, new byte[10]);
                NormalBlock blk5 = new NormalBlock();
                blk5.LoadVersion0(bh, tblk2_1.coinbaseTxToMiner, tblk2_1.transferTxs);
                blk5.UpdateMerkleRootHash();

                blks5[i] = blk5;
            }

            BlockChain.UpdateChainReturnType type5 = blockchain.UpdateChain(blks2[0]);

            if (type5 != BlockChain.UpdateChainReturnType.updated)
                throw new Exception("test24_5");

            BlockChain.UpdateChainReturnType type = blockchain.UpdateChain(blk4);

            if (type != BlockChain.UpdateChainReturnType.pending)
                throw new Exception("test24_1");

            for (int i = 2; i < blks2.Length; i++)
            {
                BlockChain.UpdateChainReturnType type2 = blockchain.UpdateChain(blks2[i]);

                if (type2 != BlockChain.UpdateChainReturnType.pending)
                    throw new Exception("test24_2");
            }

            for (int i = 0; i < blks5.Length; i++)
            {
                BlockChain.UpdateChainReturnType type2 = blockchain.UpdateChain(blks5[i]);

                if (type2 != BlockChain.UpdateChainReturnType.pending)
                    throw new Exception("test24_3");
            }

            BlockChain.UpdateChainReturnType type3 = blockchain.UpdateChain(blk3_2);

            if (type3 != BlockChain.UpdateChainReturnType.pending)
                throw new Exception("test24_1");

            for (int i = 0; i < blks3.Length; i++)
            {
                BlockChain.UpdateChainReturnType type2 = blockchain.UpdateChain(blks3[i]);

                if (type2 != BlockChain.UpdateChainReturnType.pending)
                    throw new Exception("test24_4");
            }

            BlockChain.UpdateChainReturnType type4 = blockchain.UpdateChain(blks2[1]);

            if (type4 != BlockChain.UpdateChainReturnType.updatedAndRejected)
                throw new Exception("test24_6");
            if (blockchain.blocksCurrent.value != 10)
                throw new Exception("test19_2");

            Console.WriteLine("test24_succeeded");
        }
    }

    public class TransactionOutputContext
    {
        public TransactionOutputContext(long _bIndex, int _txIndex, int _txOutIndex, CurrencyUnit _amount, Sha256Ripemd160Hash _address, Ecdsa256KeyPair _keyPair)
        {
            bIndex = _bIndex;
            txIndex = _txIndex;
            txOutIndex = _txOutIndex;
            amount = _amount;
            address = _address;
            keyPair = _keyPair;
        }

        public long bIndex { get; set; }
        public int txIndex { get; set; }
        public int txOutIndex { get; set; }
        public CurrencyUnit amount { get; set; }
        public Sha256Ripemd160Hash address { get; set; }
        public Ecdsa256KeyPair keyPair { get; set; }

        public TransactionOutput GenerateTrasactionOutput()
        {
            TransactionOutput txOut = new TransactionOutput();
            txOut.LoadVersion0(address, amount);
            return txOut;
        }

        public TransactionInput GenerateTransactionInput()
        {
            TransactionInput txIn = new TransactionInput();
            txIn.LoadVersion0(bIndex, txIndex, txOutIndex, keyPair.pubKey);
            return txIn;
        }
    }

    public class BlockContext
    {
        public BlockContext(Block _block, TransactionOutput[][] _prevTxOutss, long _feesRawAmount, Dictionary<Sha256Ripemd160Hash, List<TransactionOutputContext>> _unspentTxOuts, Dictionary<Sha256Ripemd160Hash, List<TransactionOutputContext>> _spentTxOuts)
        {
            block = _block;
            prevTxOutss = _prevTxOutss;
            feeRawAmount = _feesRawAmount;
            unspentTxOuts = _unspentTxOuts;
            spentTxOuts = _spentTxOuts;
        }

        public Block block { get; set; }
        public TransactionOutput[][] prevTxOutss { get; set; }
        public long feeRawAmount { get; set; }
        public Dictionary<Sha256Ripemd160Hash, List<TransactionOutputContext>> unspentTxOuts { get; set; }
        public Dictionary<Sha256Ripemd160Hash, List<TransactionOutputContext>> spentTxOuts { get; set; }
    }

    public class BlockGenerator
    {
        public BlockGenerator(int _numOfKeyPairs = 10, int _numOfCoinbaseTxOuts = 5, int _maxNumOfSpendTxs = 5, int _maxNumOfSpendTxOuts = 2, double _avgIORatio = 1.5)
        {
            numOfKeyPairs = _numOfKeyPairs;
            numOfCoinbaseTxOuts = _numOfCoinbaseTxOuts;
            maxNumOfSpendTxs = _maxNumOfSpendTxs;
            maxNumOfSpendTxOuts = _maxNumOfSpendTxOuts;
            avgIORatio = _avgIORatio;

            keyPairs = new Ecdsa256KeyPair[numOfKeyPairs];
            addresses = new Sha256Ripemd160Hash[numOfKeyPairs];
            for (int i = 0; i < keyPairs.Length; i++)
            {
                keyPairs[i] = new Ecdsa256KeyPair(true);
                addresses[i] = new Sha256Ripemd160Hash(keyPairs[i].pubKey.pubKey);
            }

            currentBIndex = -1;

            unspentTxOuts = new List<TransactionOutputContext>();
            spentTxOuts = new List<TransactionOutputContext>();
            blks = new List<Block>();

            unspentTxOutsDict = new Dictionary<Sha256Ripemd160Hash, List<TransactionOutputContext>>();
            spentTxOutsDict = new Dictionary<Sha256Ripemd160Hash, List<TransactionOutputContext>>();
            for (int i = 0; i < keyPairs.Length; i++)
            {
                unspentTxOutsDict.Add(addresses[i], new List<TransactionOutputContext>());
                spentTxOutsDict.Add(addresses[i], new List<TransactionOutputContext>());
            }
        }

        private readonly int numOfKeyPairs;
        private readonly int numOfCoinbaseTxOuts;
        private readonly int maxNumOfSpendTxs;
        private readonly int maxNumOfSpendTxOuts;
        private readonly double avgIORatio;

        private Ecdsa256KeyPair[] keyPairs;
        private Sha256Ripemd160Hash[] addresses;
        private long currentBIndex;
        private List<TransactionOutputContext> unspentTxOuts;
        private List<TransactionOutputContext> spentTxOuts;
        private List<Block> blks;
        private Dictionary<Sha256Ripemd160Hash, List<TransactionOutputContext>> unspentTxOutsDict;
        private Dictionary<Sha256Ripemd160Hash, List<TransactionOutputContext>> spentTxOutsDict;

        public BlockContext CreateNextValidBlock()
        {
            currentBIndex++;

            if (currentBIndex == 0)
            {
                Block blk = new GenesisBlock();

                blks.Add(blk);

                Dictionary<Sha256Ripemd160Hash, List<TransactionOutputContext>> unspentTxOutsDictClone2 = new Dictionary<Sha256Ripemd160Hash, List<TransactionOutputContext>>();
                Dictionary<Sha256Ripemd160Hash, List<TransactionOutputContext>> spentTxOutsDictClone2 = new Dictionary<Sha256Ripemd160Hash, List<TransactionOutputContext>>();

                for (int i = 0; i < keyPairs.Length; i++)
                {
                    unspentTxOutsDictClone2.Add(addresses[i], new List<TransactionOutputContext>());
                    foreach (var toc in unspentTxOutsDict[addresses[i]])
                        unspentTxOutsDictClone2[addresses[i]].Add(toc);
                    spentTxOutsDictClone2.Add(addresses[i], new List<TransactionOutputContext>());
                    foreach (var toc in spentTxOutsDict[addresses[i]])
                        spentTxOutsDictClone2[addresses[i]].Add(toc);
                }

                return new BlockContext(blk, new TransactionOutput[][] { }, 0, unspentTxOutsDictClone2, spentTxOutsDictClone2);
            }

            int numOfSpendTxs = maxNumOfSpendTxs.RandomNum() + 1;
            int[] numOfSpendTxOutss = new int[numOfSpendTxs];
            for (int i = 0; i < numOfSpendTxOutss.Length; i++)
                numOfSpendTxOutss[i] = maxNumOfSpendTxOuts.RandomNum() + 1;

            TransactionOutputContext[][] spendTxOutss = new TransactionOutputContext[numOfSpendTxs][];
            for (int i = 0; i < spendTxOutss.Length; i++)
            {
                if (unspentTxOuts.Count == 0)
                    break;

                spendTxOutss[i] = new TransactionOutputContext[numOfSpendTxOutss[i]];

                for (int j = 0; j < spendTxOutss[i].Length; j++)
                {
                    int index = unspentTxOuts.Count.RandomNum();

                    spendTxOutss[i][j] = unspentTxOuts[index];

                    spentTxOutsDict[unspentTxOuts[index].address].Add(unspentTxOuts[index]);
                    unspentTxOutsDict[unspentTxOuts[index].address].Remove(unspentTxOuts[index]);

                    spentTxOuts.Add(unspentTxOuts[index]);
                    unspentTxOuts.RemoveAt(index);

                    if (unspentTxOuts.Count == 0)
                        break;
                }
            }

            long fee = 0;
            List<TransferTransaction> transferTxs = new List<TransferTransaction>();
            List<TransactionOutput[]> prevTxOutsList = new List<TransactionOutput[]>();
            for (int i = 0; i < spendTxOutss.Length; i++)
            {
                if (spendTxOutss[i] == null)
                    break;

                long sumRawAmount = 0;

                List<TransactionInput> txInputsList = new List<TransactionInput>();
                for (int j = 0; j < spendTxOutss[i].Length; j++)
                {
                    if (spendTxOutss[i][j] == null)
                        break;

                    txInputsList.Add(spendTxOutss[i][j].GenerateTransactionInput());

                    sumRawAmount += spendTxOutss[i][j].amount.rawAmount;
                }

                TransactionInput[] txIns = txInputsList.ToArray();

                int num = sumRawAmount > 1000000 ? (int)Math.Ceiling(((avgIORatio - 1) * 2) * 1.RandomDouble() * txIns.Length) : 1;

                TransactionOutputContext[] txOutsCon = new TransactionOutputContext[num];
                TransactionOutput[] txOuts = new TransactionOutput[num];
                for (int j = 0; j < txOutsCon.Length; j++)
                {
                    long outAmount = 0;
                    if (sumRawAmount > 1000000)
                    {
                        long sumRawAmountDivided = sumRawAmount / 1000000;

                        int subtract = ((int)sumRawAmountDivided / 2).RandomNum() + 1;

                        outAmount = (long)subtract * 1000000;
                    }
                    else
                        outAmount = sumRawAmount;

                    sumRawAmount -= outAmount;

                    int index = numOfKeyPairs.RandomNum();

                    txOutsCon[j] = new TransactionOutputContext(currentBIndex, i + 1, j, new CurrencyUnit(outAmount), addresses[index], keyPairs[index]);
                    txOuts[j] = txOutsCon[j].GenerateTrasactionOutput();
                }

                fee += sumRawAmount;

                for (int j = 0; j < txOutsCon.Length; j++)
                {
                    unspentTxOutsDict[txOutsCon[j].address].Add(txOutsCon[j]);

                    unspentTxOuts.Add(txOutsCon[j]);
                }

                TransactionOutput[] prevTxOuts = new TransactionOutput[txIns.Length];
                Ecdsa256PrivKey[] privKeys = new Ecdsa256PrivKey[txIns.Length];
                for (int j = 0; j < prevTxOuts.Length; j++)
                {
                    prevTxOuts[j] = spendTxOutss[i][j].GenerateTrasactionOutput();
                    privKeys[j] = spendTxOutss[i][j].keyPair.privKey;
                }

                TransferTransaction tTx = new TransferTransaction();
                tTx.LoadVersion0(txIns, txOuts);
                tTx.Sign(prevTxOuts, privKeys);

                transferTxs.Add(tTx);
                prevTxOutsList.Add(prevTxOuts);
            }

            long rewardAndFee = TransactionalBlock.GetRewardToMiner(currentBIndex, 0).rawAmount + fee;

            TransactionOutputContext[] coinbaseTxOutsCon = new TransactionOutputContext[numOfCoinbaseTxOuts];
            TransactionOutput[] coinbaseTxOuts = new TransactionOutput[numOfCoinbaseTxOuts];
            for (int i = 0; i < coinbaseTxOutsCon.Length; i++)
            {
                long outAmount2 = 0;
                if (i != coinbaseTxOutsCon.Length - 1)
                {
                    long rewardAndFeeDevided = rewardAndFee / 1000000;

                    int subtract2 = ((int)rewardAndFeeDevided / 2).RandomNum() + 1;

                    outAmount2 = (long)subtract2 * 1000000;

                    rewardAndFee -= outAmount2;
                }
                else
                    outAmount2 = rewardAndFee;

                int index = numOfKeyPairs.RandomNum();

                coinbaseTxOutsCon[i] = new TransactionOutputContext(currentBIndex, 0, i, new CurrencyUnit(outAmount2), addresses[index], keyPairs[index]);
                coinbaseTxOuts[i] = coinbaseTxOutsCon[i].GenerateTrasactionOutput();
            }

            CoinbaseTransaction coinbaseTx = new CoinbaseTransaction();
            coinbaseTx.LoadVersion0(coinbaseTxOuts);

            for (int i = 0; i < coinbaseTxOutsCon.Length; i++)
            {
                unspentTxOutsDict[coinbaseTxOutsCon[i].address].Add(coinbaseTxOutsCon[i]);

                unspentTxOuts.Add(coinbaseTxOutsCon[i]);
            }

            prevTxOutsList.Insert(0, new TransactionOutput[] { });

            Difficulty<Creahash> diff = new Difficulty<Creahash>(HASHBASE.FromHash<Creahash>(new byte[] { 0, 127, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255 }));
            byte[] nonce = new byte[10] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 };

            BlockHeader bh = new BlockHeader();
            bh.LoadVersion0(currentBIndex, blks[blks.Count - 1].Id, DateTime.Now, diff, nonce);

            NormalBlock nblk = new NormalBlock();
            nblk.LoadVersion0(bh, coinbaseTx, transferTxs.ToArray());
            nblk.UpdateMerkleRootHash();

            blks.Add(nblk);

            Dictionary<Sha256Ripemd160Hash, List<TransactionOutputContext>> unspentTxOutsDictClone = new Dictionary<Sha256Ripemd160Hash, List<TransactionOutputContext>>();
            Dictionary<Sha256Ripemd160Hash, List<TransactionOutputContext>> spentTxOutsDictClone = new Dictionary<Sha256Ripemd160Hash, List<TransactionOutputContext>>();

            for (int i = 0; i < keyPairs.Length; i++)
            {
                unspentTxOutsDictClone.Add(addresses[i], new List<TransactionOutputContext>());
                foreach (var toc in unspentTxOutsDict[addresses[i]])
                    unspentTxOutsDictClone[addresses[i]].Add(toc);
                spentTxOutsDictClone.Add(addresses[i], new List<TransactionOutputContext>());
                foreach (var toc in spentTxOutsDict[addresses[i]])
                    spentTxOutsDictClone[addresses[i]].Add(toc);
            }

            BlockContext blkCon = new BlockContext(nblk, prevTxOutsList.ToArray(), fee, unspentTxOutsDictClone, spentTxOutsDictClone);

            return blkCon;
        }
    }
}