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
using System.Linq;

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

        private AccountHoldersDatabase ahdb;
        private TransactionHistoriesDatabase thdb;
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

        public TransactionHistories transactionHistories { get; private set; }

        public BlockChain blockChain { get; private set; }

        private Dictionary<TransferTransaction, TransactionOutput[]> unconfirmedTtxs;
        private Sha256Ripemd160Hash miningAddress;
        private Mining mining;

        private bool isSystemStarted;

        private CachedData<CurrencyUnit> usableBalanceCache;
        public CurrencyUnit UsableBalance { get { return usableBalanceCache.Data; } }

        private CachedData<CurrencyUnit> unusableBalanceCache;
        public CurrencyUnit UnusableBalance { get { return unusableBalanceCache.Data; } }

        private CachedData<CurrencyUnit> unconfirmedBalanceCache;
        public CurrencyUnit UnconfirmedBalance { get { return unconfirmedBalanceCache.Data; } }

        private CachedData<CurrencyUnit> usableBalanceWithUnconfirmedCache;
        public CurrencyUnit UsableBalanceWithUnconfirmed { get { return usableBalanceWithUnconfirmedCache.Data; } }

        private CachedData<CurrencyUnit> unusableBalanceWithUnconformedCache;
        public CurrencyUnit UnusableBalanceWithUnconfirmed { get { return unusableBalanceWithUnconformedCache.Data; } }

        public CurrencyUnit Balance { get { return new CurrencyUnit(UsableBalance.rawAmount + UnusableBalance.rawAmount); } }

        public bool canMine { get { return blockChain.headBlockIndex >= 0; } }

        public event EventHandler BalanceUpdated = delegate { };

        public void StartSystem()
        {
            if (isSystemStarted)
                throw new InvalidOperationException("core_started");

            ahdb = new AccountHoldersDatabase(databaseBasepath);
            thdb = new TransactionHistoriesDatabase(databaseBasepath);
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

            byte[] ahDataBytes = ahdb.GetData();
            if (ahDataBytes.Length != 0)
                accountHolders.FromBinary(ahDataBytes);
            else
                accountHolders.LoadVersion0();

            transactionHistories = thdb.GetData().Pipe((data) => data.Length == 0 ? new TransactionHistories() : SHAREDDATA.FromBinary<TransactionHistories>(data));
            transactionHistories.UnconfirmedTransactionAdded += (sender, e) =>
            {
                foreach (var accountHolder in accountHolders.AllAccountHolders)
                    foreach (var account in accountHolder.Accounts)
                        foreach (var prevTxOut in e.senders)
                            if (account.Address.Equals(prevTxOut.Address))
                                account.accountStatus.unconfirmedAmount = new CurrencyUnit(account.accountStatus.unconfirmedAmount.rawAmount + prevTxOut.Amount.rawAmount);
            };
            transactionHistories.UnconfirmedTransactionRemoved += (sender, e) =>
            {
                foreach (var accountHolder in accountHolders.AllAccountHolders)
                    foreach (var account in accountHolder.Accounts)
                        foreach (var prevTxOut in e.receivers)
                            if (account.Address.Equals(prevTxOut.Address))
                                account.accountStatus.unconfirmedAmount = new CurrencyUnit(account.accountStatus.unconfirmedAmount.rawAmount - prevTxOut.Amount.rawAmount);
            };

            usableBalanceCache = new CachedData<CurrencyUnit>(() =>
            {
                long rawAmount = 0;
                foreach (var accountHolder in accountHolders.AllAccountHolders)
                    foreach (var account in accountHolder.Accounts)
                        rawAmount += account.accountStatus.usableAmount.rawAmount;
                return new CurrencyUnit(rawAmount);
            });
            unusableBalanceCache = new CachedData<CurrencyUnit>(() =>
            {
                long rawAmount = 0;
                foreach (var accountHolder in accountHolders.AllAccountHolders)
                    foreach (var account in accountHolder.Accounts)
                        rawAmount += account.accountStatus.unusableAmount.rawAmount;
                return new CurrencyUnit(rawAmount);
            });
            unconfirmedBalanceCache = new CachedData<CurrencyUnit>(() =>
            {
                long rawAmount = 0;
                foreach (var accountHolder in accountHolders.AllAccountHolders)
                    foreach (var account in accountHolder.Accounts)
                        rawAmount += account.accountStatus.unconfirmedAmount.rawAmount;
                return new CurrencyUnit(rawAmount);
            });
            usableBalanceWithUnconfirmedCache = new CachedData<CurrencyUnit>(() => new CurrencyUnit(usableBalanceCache.Data.rawAmount - unconfirmedBalanceCache.Data.rawAmount));
            unusableBalanceWithUnconformedCache = new CachedData<CurrencyUnit>(() => new CurrencyUnit(unusableBalanceCache.Data.rawAmount + unconfirmedBalanceCache.Data.rawAmount));

            blockChain = new BlockChain(bcadb, bmdb, bdb, bfpdb, ufadb, ufpdb, ufptempdb, utxodb);
            blockChain.LoadTransactionHistories(transactionHistories);

            //<未改良>暫定？
            if (blockChain.headBlockIndex == -1)
            {
                blockChain.UpdateChain(new GenesisBlock());

                this.RaiseNotification("genesis_block_generated", 5);
            }

            Dictionary<Account, EventHandler<Tuple<CurrencyUnit, CurrencyUnit>>> changeAmountDict = new Dictionary<Account, EventHandler<Tuple<CurrencyUnit, CurrencyUnit>>>();

            Action<bool> _UpdateBalance = (isOnlyUnconfirmed) =>
            {
                if (!isOnlyUnconfirmed)
                {
                    usableBalanceCache.IsModified = true;
                    unusableBalanceCache.IsModified = true;
                }
                unconfirmedBalanceCache.IsModified = true;
                usableBalanceWithUnconfirmedCache.IsModified = true;
                unusableBalanceWithUnconformedCache.IsModified = true;

                BalanceUpdated(this, EventArgs.Empty);
            };

            Action<Account, bool> _AddAddressEvent = (account, isUpdatebalance) =>
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

                long rawAmount = 0;
                foreach (var unconfirmedTh in transactionHistories.unconfirmedTransactionHistories.ToArray())
                    foreach (var prevTxOut in unconfirmedTh.senders)
                        if (prevTxOut.Address.Equals(account.Address))
                            rawAmount += prevTxOut.Amount.rawAmount;
                account.accountStatus.unconfirmedAmount = new CurrencyUnit(rawAmount);

                if (isUpdatebalance)
                    _UpdateBalance(false);
            };

            EventHandler<Account> _AccountAdded = (sender, e) =>
            {
                utxodb.Open();

                _AddAddressEvent(e, true);

                utxodb.Close();
            };
            EventHandler<Account> _AccountRemoved = (sender, e) =>
            {
                EventHandler<Tuple<CurrencyUnit, CurrencyUnit>> eh = changeAmountDict[e];

                changeAmountDict.Remove(e);

                AddressEvent addressEvent = blockChain.RemoveAddressEvent(e.Address.Hash);
                addressEvent.BalanceUpdated -= eh;

                _UpdateBalance(false);
            };

            utxodb.Open();

            foreach (var accountHolder in accountHolders.AllAccountHolders)
            {
                foreach (var account in accountHolder.Accounts)
                    _AddAddressEvent(account, false);

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

            blockChain.BalanceUpdated += (sender, e) => _UpdateBalance(false);

            _UpdateBalance(false);

            unconfirmedTtxs = new Dictionary<TransferTransaction, TransactionOutput[]>();
            mining = new Mining();
            mining.FoundNonce += (sender, e) => creaNodeTest.DiffuseNewBlock(e);

            blockChain.Updated += (sender, e) =>
            {
                foreach (var block in e)
                    foreach (var tx in block.Transactions)
                        foreach (var txIn in tx.TxInputs)
                        {
                            TransferTransaction contradiction = null;

                            foreach (var unconfirmedTx in unconfirmedTtxs)
                            {
                                foreach (var unconfirmedTxIn in unconfirmedTx.Key.TxInputs)
                                    if (txIn.PrevTxBlockIndex == unconfirmedTxIn.PrevTxBlockIndex && txIn.PrevTxIndex == unconfirmedTxIn.PrevTxIndex && txIn.PrevTxOutputIndex == unconfirmedTxIn.PrevTxOutputIndex)
                                    {
                                        contradiction = unconfirmedTx.Key;

                                        break;
                                    }

                                if (contradiction != null)
                                    break;
                            }

                            if (contradiction != null)
                                unconfirmedTtxs.Remove(contradiction);
                        }

                Mine();
            };

            Mine();

            //creaNodeTest = new CreaNode(ps.NodePort, creaVersion, appnameWithVersion, new FirstNodeInfosDatabase(p2pDirectory));
            creaNodeTest = new CreaNodeTest(blockChain, ps.NodePort, creaVersion, appnameWithVersion);
            //creaNodeTest.ConnectionKeeped += (sender, e) => creaNodeTest.SyncronizeBlockchain(blockChain);
            creaNodeTest.ReceivedNewTransaction += (sender, e) =>
            {
                TransferTransaction ttx = e as TransferTransaction;

                if (ttx == null)
                    return;

                TransactionOutput[] prevTxOuts = new TransactionOutput[ttx.TxInputs.Length];
                for (int i = 0; i < prevTxOuts.Length; i++)
                    prevTxOuts[i] = blockChain.GetMainBlock(ttx.TxInputs[i].PrevTxBlockIndex).Transactions[ttx.TxInputs[i].PrevTxIndex].TxOutputs[ttx.TxInputs[i].PrevTxOutputIndex];

                if (!ttx.Verify(prevTxOuts))
                    return;

                List<TransactionOutput> senders = new List<TransactionOutput>();
                List<TransactionOutput> receivers = new List<TransactionOutput>();

                long sentAmount = 0;
                long receivedAmount = 0;

                for (int i = 0; i < ttx.txInputs.Length; i++)
                    foreach (var accountHolder in accountHolders.AllAccountHolders)
                        foreach (var account in accountHolder.Accounts)
                            if (prevTxOuts[i].Address.Equals(account.Address.Hash))
                            {
                                sentAmount += prevTxOuts[i].Amount.rawAmount;

                                senders.Add(prevTxOuts[i]);
                            }

                for (int i = 0; i < ttx.TxOutputs.Length; i++)
                    foreach (var accountHolder in accountHolders.AllAccountHolders)
                        foreach (var account in accountHolder.Accounts)
                            if (ttx.TxOutputs[i].Address.Equals(account.Address.Hash))
                            {
                                receivedAmount += ttx.TxOutputs[i].Amount.rawAmount;

                                receivers.Add(ttx.TxOutputs[i]);
                            }

                if (senders.Count > 0 || receivers.Count > 0)
                {
                    TransactionHistoryType type = TransactionHistoryType.transfered;
                    if (receivers.Count < ttx.TxOutputs.Length)
                        type = TransactionHistoryType.sent;
                    else if (senders.Count < ttx.TxInputs.Length)
                        type = TransactionHistoryType.received;

                    transactionHistories.AddTransactionHistory(new TransactionHistory(true, false, type, DateTime.MinValue, 0, ttx.Id, senders.ToArray(), receivers.ToArray(), ttx, prevTxOuts, new CurrencyUnit(sentAmount), new CurrencyUnit(receivedAmount - sentAmount)));
                }

                utxodb.Open();

                for (int i = 0; i < ttx.TxInputs.Length; i++)
                    if (blockChain.FindUtxo(prevTxOuts[i].Address, ttx.TxInputs[i].PrevTxBlockIndex, ttx.TxInputs[i].PrevTxIndex, ttx.TxInputs[i].PrevTxOutputIndex) == null)
                        return;

                utxodb.Close();

                foreach (var txIn in ttx.TxInputs)
                    foreach (var unconfirmedTtx in unconfirmedTtxs)
                        foreach (var unconfirmedTxIn in unconfirmedTtx.Key.TxInputs)
                            if (txIn.PrevTxBlockIndex == unconfirmedTxIn.PrevTxBlockIndex && txIn.PrevTxIndex == unconfirmedTxIn.PrevTxIndex && txIn.PrevTxOutputIndex == unconfirmedTxIn.PrevTxOutputIndex)
                                return;

                unconfirmedTtxs.Add(ttx, prevTxOuts);

                Mine();
            };
            creaNodeTest.ReceivedNewBlock += (sender, e) => blockChain.UpdateChain(e).Pipe((ret) => this.RaiseResult("blockchain_update", 5, ret.ToString()));
            //creaNodeTest.Start();

            isSystemStarted = true;
        }

        public void EndSystem()
        {
            if (!isSystemStarted)
                throw new InvalidOperationException("core_not_started");

            blockChain.Exit();

            thdb.UpdateData(transactionHistories.ToBinary());
            ahdb.UpdateData(accountHolders.ToBinary());

            isSystemStarted = false;
        }

        public void NewTransaction(IAccount iAccount, Sha256Ripemd160Hash address, CurrencyUnit amount, CurrencyUnit fee)
        {
            if (!isSystemStarted)
                throw new InvalidOperationException("core_not_started");

            Account account = iAccount as Account;
            if (account == null)
                throw new ArgumentException("iaccount_type");

            utxodb.Open();

            List<Utxo> utxosList = blockChain.GetAllUtxos(account.Address.Hash);
            utxosList.Sort((a, b) =>
            {
                if (a.blockIndex < b.blockIndex)
                    return -1;
                else if (a.blockIndex > b.blockIndex)
                    return 1;

                if (a.txIndex < b.txIndex)
                    return -1;
                else if (a.txIndex > b.txIndex)
                    return 1;

                if (a.txOutIndex < b.txOutIndex)
                    return -1;
                else if (a.txOutIndex > b.txOutIndex)
                    return 1;

                return 0;
            });
            Utxo[] utxos = utxosList.ToArray();

            utxodb.Close();

            List<TransactionInput> usedTxInList = new List<TransactionInput>();
            foreach (var unconfirmedTh in transactionHistories.unconfirmedTransactionHistories.ToArray())
                for (int i = 0; i < unconfirmedTh.prevTxOuts.Length; i++)
                    if (unconfirmedTh.prevTxOuts[i].Address.Equals(account.Address.Hash))
                        usedTxInList.Add(unconfirmedTh.transaction.TxInputs[i]);
            usedTxInList.Sort((a, b) =>
            {
                if (a.PrevTxBlockIndex < b.PrevTxBlockIndex)
                    return -1;
                else if (a.PrevTxBlockIndex > b.PrevTxBlockIndex)
                    return 1;

                if (a.PrevTxIndex < b.PrevTxIndex)
                    return -1;
                else if (a.PrevTxIndex > b.PrevTxIndex)
                    return 1;

                if (a.PrevTxOutputIndex < b.PrevTxOutputIndex)
                    return -1;
                else if (a.PrevTxOutputIndex > b.PrevTxOutputIndex)
                    return 1;

                return 0;
            });
            TransactionInput[] usedTxIns = usedTxInList.ToArray();

            List<Utxo> unusedUtxosList = new List<Utxo>();

            int position = -1;
            for (int i = 0; i < usedTxIns.Length; i++)
            {
                bool flag = false;
                while (position < utxos.Length)
                {
                    position++;

                    if (usedTxIns[i].PrevTxBlockIndex == utxos[position].blockIndex && usedTxIns[i].PrevTxIndex == utxos[position].txIndex && usedTxIns[i].PrevTxOutputIndex == utxos[position].txOutIndex)
                    {
                        flag = true;

                        break;
                    }
                    else
                        unusedUtxosList.Add(utxos[position]);
                }

                if (!flag)
                    throw new InvalidOperationException();
            }

            long rawFeeAndAmount = amount.rawAmount + fee.rawAmount;

            List<Utxo> useUtxosList = new List<Utxo>();
            long rawFeeAndAmountAndChange = 0;

            bool flag2 = false;
            foreach (var utxo in unusedUtxosList)
            {
                useUtxosList.Add(utxo);

                if ((rawFeeAndAmountAndChange += utxo.amount.rawAmount) > rawFeeAndAmount)
                {
                    flag2 = true;

                    break;
                }
            }

            if (!flag2)
                for (int i = position + 1; i < utxos.Length; i++)
                {
                    useUtxosList.Add(utxos[i]);

                    if ((rawFeeAndAmountAndChange += utxos[i].amount.rawAmount) > rawFeeAndAmount)
                    {
                        flag2 = true;

                        break;
                    }
                }

            if (!flag2)
                throw new InvalidOperationException();

            Utxo[] useUtxos = useUtxosList.ToArray();

            TransactionInput[] txIns = new TransactionInput[useUtxos.Length];
            for (int i = 0; i < txIns.Length; i++)
            {
                txIns[i] = new TransactionInput();
                txIns[i].LoadVersion0(useUtxos[i].blockIndex, useUtxos[i].txIndex, useUtxos[i].txOutIndex, account.Ecdsa256KeyPair.pubKey);
            }

            long rawChange = rawFeeAndAmountAndChange - rawFeeAndAmount;

            TransactionOutput[] txOuts = new TransactionOutput[rawChange == 0 ? 1 : 2];
            txOuts[0] = new TransactionOutput();
            txOuts[0].LoadVersion0(address, amount);
            if (rawChange != 0)
            {
                txOuts[1] = new TransactionOutput();
                txOuts[1].LoadVersion0(account.Address.Hash, new CurrencyUnit(rawChange));
            }

            TransactionOutput[] prevTxOuts = new TransactionOutput[useUtxos.Length];
            for (int i = 0; i < prevTxOuts.Length; i++)
                prevTxOuts[i] = blockChain.GetMainBlock(txIns[i].PrevTxBlockIndex).Transactions[txIns[i].PrevTxIndex].TxOutputs[txIns[i].PrevTxOutputIndex];

            Ecdsa256PrivKey[] privKeys = new Ecdsa256PrivKey[useUtxos.Length];
            for (int i = 0; i < privKeys.Length; i++)
                privKeys[i] = account.Ecdsa256KeyPair.privKey;

            TransferTransaction ttx = new TransferTransaction();
            ttx.LoadVersion0(txIns, txOuts);
            ttx.Sign(prevTxOuts, privKeys);

            creaNodeTest.DiffuseNewTransaction(ttx);
        }

        public void StartMining(IAccount iAccount)
        {
            if (!isSystemStarted)
                throw new InvalidOperationException("core_not_started");
            if (mining.isStarted)
                throw new InvalidOperationException("already_mining");
            if (!canMine)
                throw new InvalidOperationException("cant_mine");

            Account account = iAccount as Account;
            if (account == null)
                throw new ArgumentException("iaccount_type");

            miningAddress = account.Address.Hash;

            mining.Start();

            Mine();
        }

        public void EndMining()
        {
            if (!isSystemStarted)
                throw new InvalidOperationException("core_not_started");
            if (!mining.isStarted)
                throw new InvalidOperationException("not_mining");

            mining.End();
        }

        private void Mine()
        {
            if (miningAddress != null)
            {
                TransferTransaction[] ttxs = unconfirmedTtxs.Keys.Take(TransactionalBlock.maxTxs - 2).ToArray();
                TransactionOutput[][] prevTxOutss = unconfirmedTtxs.Values.Take(TransactionalBlock.maxTxs - 2).ToArray();

                mining.NewMiningBlock(TransactionalBlock.GetBlockTemplate(blockChain.headBlockIndex + 1, miningAddress, ttxs, prevTxOutss, (index) => blockChain.GetMainBlock(index) as TransactionalBlock, 0));
            }
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
        public CurrencyUnit unconfirmedAmount { get; set; }
        public CurrencyUnit usableAmountWithUnconfirmed { get { return new CurrencyUnit(usableAmount.rawAmount - unconfirmedAmount.rawAmount); } }
        public CurrencyUnit unusableAmountWithUnconfirmed { get { return new CurrencyUnit(unusableAmount.rawAmount + unconfirmedAmount.rawAmount); } }
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