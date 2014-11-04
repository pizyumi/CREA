﻿//がをがを～！
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
using System.Windows.Input;
using System.Windows.Media;

namespace CREA2014
{
    public class Core
    {
        public Core(string _basePath, int _creaVersion, string _appnameWithVersion)
        {
            //Coreが2回以上実体化されないことを保証する
            //2回以上呼ばれた際には例外が発生する
            Instantiate();

            basepath = _basePath;
            databaseBasepath = Path.Combine(basepath, databaseDirectory);
            creaVersion = _creaVersion;
            appnameWithVersion = _appnameWithVersion;
        }

        private static readonly Action Instantiate = OneTime.GetOneTime();

        private static readonly string databaseDirectory = "database";
        private static readonly string p2pDirectory = "p2p";

        private readonly string basepath;
        private readonly string databaseBasepath;
        private readonly int creaVersion;
        private readonly string appnameWithVersion;

        //試験用
        private CreaNodeLocalTestContinueDHT creaNode;

        private AccountHoldersDatabase ahDatabase;
        private BlockChainDatabase bcDatabase;
        private BlockNodesGroupDatabase bngDatabase;
        private BlockGroupDatabase bgDatabase;
        private UtxoDatabase utxoDatabase;
        private AddressEventDatabase addressEventDatabase;

        public AccountHolders accountHolders { get; private set; }
        public IAccountHolders iAccountHolders { get { return accountHolders; } }

        private AccountHoldersFactory accountHoldersFactory;
        public IAccountHoldersFactory iAccountHoldersFactory { get { return accountHoldersFactory; } }

        private BlockChain blockChain;
        private Mining mining;

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

            accountHolders = new AccountHolders();
            accountHoldersFactory = new AccountHoldersFactory();

            byte[] ahDataBytes = ahDatabase.GetData();
            if (ahDataBytes.Length != 0)
                accountHolders.FromBinary(ahDataBytes);
            else
                accountHolders.LoadVersion1();

            usableBalanceCache = new CachedData<CurrencyUnit>(() =>
            {
                CurrencyUnit cu = new CurrencyUnit(0);
                foreach (var accountHolder in accountHolders.AllAccountHolders)
                    foreach (var account in accountHolder.Accounts)
                        cu = new CurrencyUnit(cu.rawAmount + account.accountStatus.usableAmount.rawAmount);
                return cu;
            });
            unusableBalanceCache = new CachedData<CurrencyUnit>(() =>
            {
                CurrencyUnit cu = new CurrencyUnit(0);
                foreach (var accountHolder in accountHolders.AllAccountHolders)
                    foreach (var account in accountHolder.Accounts)
                        cu = new CurrencyUnit(cu.rawAmount + account.accountStatus.unusableAmount.rawAmount);
                return cu;
            });

            blockChain = new BlockChain(bcDatabase, bngDatabase, bgDatabase, utxoDatabase, addressEventDatabase);
            blockChain.Initialize();

            Dictionary<Account, EventHandler<Tuple<CurrencyUnit, CurrencyUnit>>> changeAmountDict = new Dictionary<Account, EventHandler<Tuple<CurrencyUnit, CurrencyUnit>>>();

            Action _UpdateBalance = () =>
            {
                usableBalanceCache.IsModified = true;
                unusableBalanceCache.IsModified = true;

                BalanceUpdated(this, EventArgs.Empty);
            };

            Action<Account> _AddAddressEvent = (account) =>
            {
                EventHandler<Tuple<CurrencyUnit, CurrencyUnit>> eh = (sender, e) =>
                {
                    account.accountStatus.usableAmount = e.Item1;
                    account.accountStatus.unusableAmount = e.Item2;
                };

                changeAmountDict.Add(account, eh);

                AddressEvent addressEvent = new AddressEvent(account.Address.Hash);
                addressEvent.BalanceUpdated += eh;

                blockChain.AddAddressEvent(addressEvent);

                _UpdateBalance();
            };

            EventHandler<Account> _AccountAdded = (sender, e) => _AddAddressEvent(e);
            EventHandler<Account> _AccountRemoved = (sender, e) =>
            {
                EventHandler<Tuple<CurrencyUnit, CurrencyUnit>> eh = changeAmountDict[e];

                changeAmountDict.Remove(e);

                AddressEvent addressEvent = blockChain.RemoveAddressEvent(e.Address.Hash);
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

            mining = new Mining();

            //試験用（ポート番号は暫定）
            creaNode = new CreaNodeLocalTestContinueDHT(7777, creaVersion, appnameWithVersion);
            creaNode.ReceivedNewTransaction += (sender, e) =>
            {
            };

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

        private EventHandler<TransactionalBlock> _ContinueMine;

        public void StartMining(IAccount iAccount)
        {
            Account account = iAccount as Account;
            if (account == null)
                throw new ArgumentException("iaccount_type");

            Action _Mine = () =>
            {
                mining.NewMiningBlock(TransactionalBlock.GetBlockTemplate(blockChain.head + 1, account.Address.Hash, (index) => blockChain.GetMainBlock(index)));
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

    public class AddressEvent
    {
        public AddressEvent(Sha256Ripemd160Hash _address) { address = _address; }

        public Sha256Ripemd160Hash address { get; private set; }

        public event EventHandler<Tuple<CurrencyUnit, CurrencyUnit>> BalanceUpdated = delegate { };
        public event EventHandler<CurrencyUnit> UsableBalanceUpdated = delegate { };
        public event EventHandler<CurrencyUnit> UnusableBalanceUpdated = delegate { };

        public void RaiseBalanceUpdated(Tuple<CurrencyUnit, CurrencyUnit> cus) { BalanceUpdated(this, cus); }
        public void RaiseUsableBalanceUpdated(CurrencyUnit cu) { UsableBalanceUpdated(this, cu); }
        public void RaiseUnusableBalanceUpdated(CurrencyUnit cu) { UnusableBalanceUpdated(this, cu); }
    }

    public class AccountStatus
    {
        public CurrencyUnit usableAmount { get; set; }
        public CurrencyUnit unusableAmount { get; set; }
    }

    #region test

    public abstract class TestApplication
    {
        public TestApplication(Program.Logger _logger) { logger = _logger; }

        protected Program.Logger logger;

        public virtual bool IsUseCore { get { return false; } }

        protected abstract Action ExecuteAction { get; }

        public void Execute() { ExecuteAction(); }
    }

    public class CreaNetworkLocalTestApplication : TestApplication
    {
        public CreaNetworkLocalTestApplication(Program.Logger _logger) : base(_logger) { }

        protected override Action ExecuteAction
        {
            get
            {
                return () =>
                {
                    TestWindow tw = new TestWindow(logger);
                    tw.Show();
                };
            }
        }

        public class TestWindow : Window
        {
            public TestWindow(Program.Logger _logger)
            {
                StackPanel sp1 = null;
                StackPanel sp2 = null;

                EventHandler<Program.LogData> _LoggerLogAdded = (sender, e) => ((Action)(() =>
                {
                    TextBlock tb = new TextBlock();
                    tb.Text = e.Text;
                    tb.Foreground = e.Kind == Program.LogData.LogKind.error ? Brushes.Red : Brushes.White;
                    tb.Margin = new Thickness(0.0, 10.0, 0.0, 10.0);

                    sp2.Children.Add(tb);
                })).BeginExecuteInUIThread();

                Loaded += (sender, e) =>
                {
                    Grid grid = new Grid();
                    grid.RowDefinitions.Add(new RowDefinition());
                    grid.RowDefinitions.Add(new RowDefinition());
                    grid.RowDefinitions.Add(new RowDefinition() { Height = GridLength.Auto });
                    grid.ColumnDefinitions.Add(new ColumnDefinition());

                    ScrollViewer sv1 = new ScrollViewer();
                    sv1.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
                    sv1.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
                    sv1.SetValue(Grid.RowProperty, 0);
                    sv1.SetValue(Grid.ColumnProperty, 0);

                    sp1 = new StackPanel();
                    sp1.Background = Brushes.Black;

                    ScrollViewer sv2 = new ScrollViewer();
                    sv2.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
                    sv2.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
                    sv2.SetValue(Grid.RowProperty, 1);
                    sv2.SetValue(Grid.ColumnProperty, 0);

                    sp2 = new StackPanel();
                    sp2.Background = Brushes.Black;

                    sv1.Content = sp1;
                    sv2.Content = sp2;

                    TextBox tb = new TextBox();
                    tb.SetValue(Grid.RowProperty, 2);
                    tb.SetValue(Grid.ColumnProperty, 0);

                    grid.Children.Add(sv1);
                    grid.Children.Add(sv2);
                    grid.Children.Add(tb);

                    Content = grid;

                    Console.SetOut(new TextBlockStreamWriter(sp1));

                    _logger.LogAdded += _LoggerLogAdded;

                    //SimulationWindow sw = new SimulationWindow();
                    //sw.ShowDialog();

                    this.StartTask(string.Empty, string.Empty, () =>
                    {
                        CirculatedInteger ci = new CirculatedInteger(5);

                        Console.WriteLine(ci.GetForward(0));
                        Console.WriteLine(ci.GetForward(1));
                        Console.WriteLine(ci.GetForward(2));
                        Console.WriteLine(ci.GetForward(3));
                        Console.WriteLine(ci.GetForward(4));
                        Console.WriteLine(ci.GetForward(5));
                        Console.WriteLine(ci.GetForward(6));

                        Console.WriteLine(ci.GetBackward(0));
                        Console.WriteLine(ci.GetBackward(1));
                        Console.WriteLine(ci.GetBackward(2));
                        Console.WriteLine(ci.GetBackward(3));
                        Console.WriteLine(ci.GetBackward(4));
                        Console.WriteLine(ci.GetBackward(5));
                        Console.WriteLine(ci.GetBackward(6));

                        Secp256k1KeyPair<Sha256Hash> secp256k1KeyPair = new Secp256k1KeyPair<Sha256Hash>(true);

                        Sha256Ripemd160Hash address = new Sha256Ripemd160Hash(secp256k1KeyPair.pubKey.pubKey);

                        TransactionInput ti1 = new TransactionInput();
                        ti1.LoadVersion1(0, 0, 0, address);

                        TransactionOutput to1 = new TransactionOutput();
                        to1.LoadVersion0(address, new Creacoin(50m));

                        CoinbaseTransaction ct1 = new CoinbaseTransaction();
                        ct1.LoadVersion0(new TransactionOutput[] { to1 });

                        byte[] ctBytes1 = ct1.ToBinary();

                        CoinbaseTransaction ct2 = SHAREDDATA.FromBinary<CoinbaseTransaction>(ctBytes1);

                        TransferTransaction tt1 = new TransferTransaction();
                        tt1.LoadVersion1(new TransactionInput[] { ti1 }, new TransactionOutput[] { to1 });
                        tt1.Sign(new TransactionOutput[] { to1 }, new DSAPRIVKEYBASE[] { secp256k1KeyPair.privKey });

                        byte[] ttBytes1 = tt1.ToBinary();

                        TransferTransaction tt2 = SHAREDDATA.FromBinary<TransferTransaction>(ttBytes1);

                        ResTransactions rt1 = new ResTransactions(new Transaction[] { ct1, tt1 });

                        byte[] rtBytes1 = rt1.ToBinary();

                        ResTransactions rt2 = SHAREDDATA.FromBinary<ResTransactions>(rtBytes1);


                        byte[] test1 = SHAREDDATA.ToBinary<Transaction>(ct2);

                        CoinbaseTransaction ct3 = SHAREDDATA.FromBinary<Transaction>(test1) as CoinbaseTransaction;

                        byte[] test2 = SHAREDDATA.ToBinary<Transaction>(tt2);

                        TransferTransaction tt3 = SHAREDDATA.FromBinary<Transaction>(test2) as TransferTransaction;

                        //string pathBase = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

                        //New.BlockManagerDB bmdb = new New.BlockManagerDB(pathBase);
                        //New.BlockDB blkdb = new New.BlockDB(pathBase);
                        //New.BlockFilePointersDB bfpdb = new New.BlockFilePointersDB(pathBase);

                        //New.BlockManager bm = new New.BlockManager(bmdb, blkdb, bfpdb);

                        //New.TestBlock block1 = new New.TestBlock(1);

                        //bm.AddMainBlock(block1);
                        //bm.AddMainBlock(block1);


                        //Test10NodesInv();

                        //TestDHT();

                        bool isFirst = true;
                        int portNumber = 0;
                        CreaNodeLocalTestContinueDHT cnlt = null;
                        tb.KeyDown += (sender2, e2) =>
                        {
                            if (e2.Key != Key.Enter)
                                return;

                            if (isFirst)
                            {
                                portNumber = int.Parse(tb.Text);

                                cnlt = new CreaNodeLocalTestContinueDHT((ushort)portNumber, 0, "test");
                                cnlt.Start();

                                cnlt.ReceivedNewChat += (sender3, e3) =>
                                {
                                    MessageBox.Show(e3.Message);
                                };

                                isFirst = false;

                                return;
                            }

                            Chat chat = new Chat();
                            chat.LoadVersion0(portNumber.ToString(), tb.Text);
                            chat.Sign(secp256k1KeyPair.privKey);

                            cnlt.DiffuseNewChat(chat);
                        };
                    });
                };

                Closed += (sender, e) =>
                {
                    _logger.LogAdded -= _LoggerLogAdded;

                    string fileText = string.Empty;
                    foreach (var child in sp1.Children)
                        fileText += (child as TextBlock).Text + Environment.NewLine;

                    File.AppendAllText(Path.Combine(new FileInfo(Assembly.GetEntryAssembly().Location).DirectoryName, "LogTest.txt"), fileText);
                };
            }

            private void TestDHT()
            {
                int numOfNodes = 5;
                CreaNodeLocalTestContinueDHT[] cnlts = new CreaNodeLocalTestContinueDHT[numOfNodes];
                for (int i = 0; i < numOfNodes; i++)
                {
                    cnlts[i] = new CreaNodeLocalTestContinueDHT((ushort)(7777 + i), 0, "test");
                    cnlts[i].Start();
                    while (!cnlts[i].isStartCompleted)
                        Thread.Sleep(100);
                }
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
}