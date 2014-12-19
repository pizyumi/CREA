//がをがを～！
//2014/11/03 分割

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
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
        public Core(string _basePath, int _creaVersion, string _appnameWithVersion, Program.ProgramSettings _ps)
        {
            //Coreが2回以上実体化されないことを保証する
            //2回以上呼ばれた際には例外が発生する
            Instantiate();

            basepath = _basePath;
            databaseBasepath = Path.Combine(basepath, databaseDirectory);
            creaVersion = _creaVersion;
            appnameWithVersion = _appnameWithVersion;
            ps = _ps;
        }

        private static readonly Action Instantiate = OneTime.GetOneTime();

        private static readonly string databaseDirectory = "database";
        private static readonly string p2pDirectory = "p2p";

        private readonly string basepath;
        private readonly string databaseBasepath;
        private readonly int creaVersion;
        private readonly string appnameWithVersion;
        private readonly Program.ProgramSettings ps;

        //試験用
        private CREANODEBASE creaNodeTest;
        public CREANODEBASE iCreaNodeTest { get { return creaNodeTest; } }

        private AccountHoldersDatabase ahDatabase;
        private BlockchainAccessDB bcadb;
        private BlockManagerDB bmdb;
        private BlockDB bdb;
        private BlockFilePointersDB bfpdb;
        private UtxoFileAccessDB ufadb;
        private UtxoFilePointersDB ufpdb;
        private UtxoFilePointersTempDB ufptempdb;
        private UtxoDB utxodb;

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

        public bool canMine { get { return blockChain.headBlockIndex >= 0; } }

        public event EventHandler BalanceUpdated = delegate { };

        public void StartSystem()
        {
            if (isSystemStarted)
                throw new InvalidOperationException("core_started");

            ahDatabase = new AccountHoldersDatabase(databaseBasepath);
            bcadb = new BlockchainAccessDB(databaseBasepath);
            bmdb = new BlockManagerDB(databaseBasepath);
            bdb = new BlockDB(databaseBasepath);
            bfpdb = new BlockFilePointersDB(databaseBasepath);
            ufadb = new UtxoFileAccessDB(databaseBasepath);
            ufpdb = new UtxoFilePointersDB(databaseBasepath);
            ufptempdb = new UtxoFilePointersTempDB(databaseBasepath);
            utxodb = new UtxoDB(databaseBasepath);

            accountHolders = new AccountHolders();
            accountHoldersFactory = new AccountHoldersFactory();

            byte[] ahDataBytes = ahDatabase.GetData();
            if (ahDataBytes.Length != 0)
                accountHolders.FromBinary(ahDataBytes);
            else
                accountHolders.LoadVersion0();

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

            blockChain = new BlockChain(bcadb, bmdb, bdb, bfpdb, ufadb, ufpdb, ufptempdb, utxodb);

            //<未改良>暫定？
            if (blockChain.headBlockIndex == -1)
            {
                blockChain.UpdateChain(new GenesisBlock());

                this.RaiseNotification("genesis_block_generated", 5);
            }

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

            EventHandler<Account> _AccountAdded = (sender, e) =>
            {
                utxodb.Open();

                _AddAddressEvent(e);

                utxodb.Close();
            };
            EventHandler<Account> _AccountRemoved = (sender, e) =>
            {
                EventHandler<Tuple<CurrencyUnit, CurrencyUnit>> eh = changeAmountDict[e];

                changeAmountDict.Remove(e);

                AddressEvent addressEvent = blockChain.RemoveAddressEvent(e.Address.Hash);
                addressEvent.BalanceUpdated -= eh;

                _UpdateBalance();
            };

            utxodb.Open();

            foreach (var accountHolder in accountHolders.AllAccountHolders)
            {
                foreach (var account in accountHolder.Accounts)
                    _AddAddressEvent(account);

                accountHolder.AccountAdded += _AccountAdded;
                accountHolder.AccountRemoved += _AccountRemoved;
            }

            utxodb.Close();

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

            //creaNodeTest = new CreaNode(ps.NodePort, creaVersion, appnameWithVersion, new FirstNodeInfosDatabase(p2pDirectory));
            creaNodeTest = new CreaNodeTest(ps.NodePort, creaVersion, appnameWithVersion);
            //creaNodeTest.ConnectionKeeped += (sender, e) => creaNodeTest.SyncronizeBlockchain(blockChain);
            creaNodeTest.ReceivedNewTransaction += (sender, e) =>
            {
            };
            creaNodeTest.ReceivedNewBlock += (sender, e) => blockChain.UpdateChain(e);
            creaNodeTest.Start();

            isSystemStarted = true;
        }

        public void EndSystem()
        {
            if (!isSystemStarted)
                throw new InvalidOperationException("core_not_started");

            blockChain.Exit();

            ahDatabase.UpdateData(accountHolders.ToBinary());

            isSystemStarted = false;
        }

        private EventHandler<TransactionalBlock> _FoundNonce;
        private EventHandler _UpdatedBlockchain;

        public void StartMining(IAccount iAccount)
        {
            if (!isSystemStarted)
                throw new InvalidOperationException("core_not_started");
            if (!canMine)
                throw new InvalidOperationException("cant_mine");

            Account account = iAccount as Account;
            if (account == null)
                throw new ArgumentException("iaccount_type");

            Action _Mine = () =>
            {
                mining.NewMiningBlock(TransactionalBlock.GetBlockTemplate(blockChain.headBlockIndex + 1, account.Address.Hash, new TransferTransaction[] { }, (index) => blockChain.GetMainBlock(index) as TransactionalBlock, 0));
            };

            _FoundNonce = (sender, e) => creaNodeTest.DiffuseNewBlock(e);
            _UpdatedBlockchain = (sender, e) => _Mine();

            blockChain.Updated += _UpdatedBlockchain;

            mining.FoundNonce += _FoundNonce;
            mining.Start();

            _Mine();
        }

        public void EndMining()
        {
            if (!isSystemStarted)
                throw new InvalidOperationException("core_not_started");

            mining.End();
            mining.FoundNonce -= _FoundNonce;

            blockChain.Updated -= _UpdatedBlockchain;
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


                        //string testPrivateRsaParameters;
                        //using (RSACryptoServiceProvider rsacsp = new RSACryptoServiceProvider(2048))
                        //    testPrivateRsaParameters = rsacsp.ToXmlString(true);


                        //RealInboundChennel ric = new RealInboundChennel(7777, RsaKeySize.rsa2048, 100);
                        //ric.Accepted += (sender2, e2) =>
                        //{
                        //    this.StartTask("", "", () =>
                        //    {
                        //        e2.WriteBytes(BitConverter.GetBytes(true));

                        //        bool b = BitConverter.ToBoolean(e2.ReadBytes(), 0);

                        //        SessionChannel sc = e2.NewSession();
                        //        sc.WriteBytes(BitConverter.GetBytes(true));
                        //        sc.Close();

                        //        //e2.Close();
                        //    });


                        //    //e2.Close();

                        //    //Console.WriteLine("");
                        //};
                        //ric.RequestAcceptanceStart();


                        //AutoResetEvent are = new AutoResetEvent(false);
                        //SocketChannel socketc = null;

                        //RealOutboundChannel roc = new RealOutboundChannel(IPAddress.Loopback, 7777, RsaKeySize.rsa2048, testPrivateRsaParameters);
                        //roc.Connected += (sender2, e2) =>
                        //{
                        //    socketc = e2;
                        //    socketc.Sessioned += (sender3, e3) =>
                        //    {
                        //        bool b3 = BitConverter.ToBoolean(e3.ReadBytes(), 0);

                        //        Console.WriteLine("");
                        //    };

                        //    are.Set();

                        //    //e2.Close();

                        //    //Console.WriteLine("connected");
                        //};
                        //roc.RequestConnection();

                        //are.WaitOne();

                        //bool b2 = BitConverter.ToBoolean(socketc.ReadBytes(), 0);

                        //socketc.WriteBytes(BitConverter.GetBytes(true));

                        //socketc.Close();


                        //CirculatedInteger ci = new CirculatedInteger(5);

                        //Console.WriteLine(ci.GetForward(0));
                        //Console.WriteLine(ci.GetForward(1));
                        //Console.WriteLine(ci.GetForward(2));
                        //Console.WriteLine(ci.GetForward(3));
                        //Console.WriteLine(ci.GetForward(4));
                        //Console.WriteLine(ci.GetForward(5));
                        //Console.WriteLine(ci.GetForward(6));

                        //Console.WriteLine(ci.GetBackward(0));
                        //Console.WriteLine(ci.GetBackward(1));
                        //Console.WriteLine(ci.GetBackward(2));
                        //Console.WriteLine(ci.GetBackward(3));
                        //Console.WriteLine(ci.GetBackward(4));
                        //Console.WriteLine(ci.GetBackward(5));
                        //Console.WriteLine(ci.GetBackward(6));

                        Secp256k1KeyPair<Sha256Hash> secp256k1KeyPair = new Secp256k1KeyPair<Sha256Hash>(true);

                        Sha256Ripemd160Hash address = new Sha256Ripemd160Hash(secp256k1KeyPair.pubKey.pubKey);

                        TransactionInput ti1 = new TransactionInput();
                        ti1.LoadVersion1(0, 0, 0);

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

                        //bool isFirst = true;
                        //int portNumber = 0;
                        //CreaNode cnlt = null;
                        //tb.KeyDown += (sender2, e2) =>
                        //{
                        //    if (e2.Key != Key.Enter)
                        //        return;

                        //    if (isFirst)
                        //    {
                        //        portNumber = int.Parse(tb.Text);

                        //        FirstNodeInfosDatabase fnidb = new FirstNodeInfosDatabase(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location));

                        //        cnlt = new CreaNode((ushort)portNumber, 0, "test", fnidb);
                        //        cnlt.Start();

                        //        cnlt.ReceivedNewChat += (sender3, e3) =>
                        //        {
                        //            this.ConsoleWriteLine(e3.Message);
                        //        };

                        //        isFirst = false;

                        //        return;
                        //    }

                        //    Chat chat = new Chat();
                        //    chat.LoadVersion0(portNumber.ToString(), tb.Text);
                        //    chat.Sign(secp256k1KeyPair.privKey);

                        //    cnlt.DiffuseNewChat(chat);
                        //};
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

            private void Test()
            {

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