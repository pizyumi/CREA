using CREA2014.Windows;
using SuperSocket.SocketBase;
using SuperWebSocket;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Mime;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace CREA2014
{
    public partial class MainWindow : Window
    {
        public class MainWindowSettings : SAVEABLESETTINGSDATA
        {
            public MainWindowSettings() : base("MainWindowSettings.xml") { }

            public bool isPortWebSocketAltered { get; private set; }
            private int portWebSocket = 3333;
            public int PortWebSocket
            {
                get { return portWebSocket; }
                set
                {
                    if (!canSet)
                        throw new InvalidOperationException("cant_set");

                    if (value != portWebSocket)
                    {
                        portWebSocket = value;
                        isPortWebSocketAltered = true;
                    }
                }
            }

            public bool isPortWebServerAltered { get; private set; }
            private int portWebServer = 3334;
            public int PortWebServer
            {
                get { return portWebServer; }
                set
                {
                    if (!canSet)
                        throw new InvalidOperationException("cant_set");

                    if (value != portWebServer)
                    {
                        portWebServer = value;
                        isPortWebServerAltered = true;
                    }
                }
            }

            public bool isIsWallpaperAltered { get; private set; }
            private bool isWallpaper = true;
            public bool IsWallpaper
            {
                get { return isWallpaper; }
                set
                {
                    if (!canSet)
                        throw new InvalidOperationException("cant_set");

                    if (value != isWallpaper)
                    {
                        isWallpaper = value;
                        isIsWallpaperAltered = true;
                    }
                }
            }

            public bool isWallpaperAltered { get; private set; }
            private string wallpaper = string.Empty;
            public string Wallpaper
            {
                get { return wallpaper; }
                set
                {
                    if (!canSet)
                        throw new InvalidOperationException("cant_set");

                    if (value != wallpaper)
                    {
                        wallpaper = value;
                        isWallpaperAltered = true;
                    }
                }
            }

            public bool isWallpaperOpacityAltered { get; private set; }
            private float wallpaperOpacity = 0.5F;
            public float WallpaperOpacity
            {
                get { return wallpaperOpacity; }
                set
                {
                    if (!canSet)
                        throw new InvalidOperationException("cant_set");

                    if (value != wallpaperOpacity)
                    {
                        wallpaperOpacity = value;
                        isWallpaperOpacityAltered = true;
                    }
                }
            }

            public bool isIsDefaultUiAltered { get; private set; }
            private bool isDefaultUi = true;
            public bool IsDefaultUi
            {
                get { return isDefaultUi; }
                set
                {
                    if (!canSet)
                        throw new InvalidOperationException("cant_set");

                    if (value != isDefaultUi)
                    {
                        isDefaultUi = value;
                        isIsDefaultUiAltered = true;
                    }
                }
            }

            public bool isUiFilesDirectoryAltered { get; private set; }
            private string uiFilesDirectory = string.Empty;
            public string UiFilesDirectory
            {
                get { return uiFilesDirectory; }
                set
                {
                    if (!canSet)
                        throw new InvalidOperationException("cant_set");

                    if (value != uiFilesDirectory)
                    {
                        uiFilesDirectory = value;
                        isUiFilesDirectoryAltered = true;
                    }
                }
            }

            public bool isMiningAccountHolderAltered { get; private set; }
            private string miningAccountHolder = string.Empty;
            public string MiningAccountHolder
            {
                get { return miningAccountHolder; }
                set
                {
                    if (!canSet)
                        throw new InvalidOperationException("cant_set");

                    if (value != miningAccountHolder)
                    {
                        miningAccountHolder = value;
                        isMiningAccountHolderAltered = true;
                    }
                }
            }

            public bool isMiningAccountAltered { get; private set; }
            private string miningAccount = string.Empty;
            public string MiningAccount
            {
                get { return miningAccount; }
                set
                {
                    if (!canSet)
                        throw new InvalidOperationException("cant_set");

                    if (value != miningAccount)
                    {
                        miningAccount = value;
                        isMiningAccountAltered = true;
                    }
                }
            }

            public bool isIsConfirmAtExitAltered { get; private set; }
            private bool isConfirmAtExit = true;
            public bool IsConfirmAtExit
            {
                get { return isConfirmAtExit; }
                set
                {
                    if (!canSet)
                        throw new InvalidOperationException("cant_set");

                    if (value != isConfirmAtExit)
                    {
                        isConfirmAtExit = value;
                        isIsConfirmAtExitAltered = true;
                    }
                }
            }

            protected override string XmlName { get { return "MainWindowSettings"; } }
            protected override MainDataInfomation[] MainDataInfo
            {
                get
                {
                    return new MainDataInfomation[]{
                        new MainDataInfomation(typeof(int), "PortWebSocket", () => portWebSocket, (o) => portWebSocket = (int)o), 
                        new MainDataInfomation(typeof(int), "PortWebServer", () => portWebServer, (o) => portWebServer = (int)o), 
                        new MainDataInfomation(typeof(bool), "IsWallpaper", () => isWallpaper, (o) => isWallpaper = (bool)o), 
                        new MainDataInfomation(typeof(string), "Wallpaper", () => wallpaper, (o) => wallpaper = (string)o), 
                        new MainDataInfomation(typeof(float), "WallpaperOpecity", () => wallpaperOpacity, (o) => wallpaperOpacity = (float)o), 
                        new MainDataInfomation(typeof(bool), "IsDefaultUi", () => isDefaultUi, (o) => isDefaultUi = (bool)o), 
                        new MainDataInfomation(typeof(string), "UiFilesDirectory", () => uiFilesDirectory, (o) => uiFilesDirectory = (string)o), 
                        new MainDataInfomation(typeof(string), "MiningAccountHolder", () => miningAccountHolder, (o) => miningAccountHolder = (string)o), 
                        new MainDataInfomation(typeof(string), "MiningAccount", () => miningAccount, (o) => miningAccount = (string)o), 
                        new MainDataInfomation(typeof(bool), "IsConfirmAtExit", () => isConfirmAtExit, (o) => isConfirmAtExit = (bool)o), 
                    };
                }
            }

            public override void StartSetting()
            {
                base.StartSetting();

                isPortWebSocketAltered = false;
                isPortWebServerAltered = false;
                isIsWallpaperAltered = false;
                isWallpaperAltered = false;
                isWallpaperOpacityAltered = false;
                isIsDefaultUiAltered = false;
                isUiFilesDirectoryAltered = false;
                isMiningAccountHolderAltered = false;
                isMiningAccountAltered = false;
                isIsConfirmAtExitAltered = false;
            }
        }

        private HttpListener hl;
        private WebSocketServer wss;
        private MainWindowSettings mws;

        private bool isMining;

        private Core core;
        private Program.Logger logger;
        private Program.ProgramSettings psettings;
        private Program.ProgramStatus pstatus;
        private string appname;
        private string version;
        private string appnameWithVersion;
        private string lisenceTextFilePath;
        private Assembly assembly;

        private Action<Exception, Program.ExceptionKind> _OnException;
        private Func<byte[], Version, bool> _UpVersion;

        private List<UnhandledExceptionEventHandler> unhandledExceptionEventHandlers;
        private List<DispatcherUnhandledExceptionEventHandler> dispatcherUnhandledExceptionEventHandlers;

        private Action<string> _CreateUiFiles;

        public abstract class WebResourceBase
        {
            public abstract byte[] GetData();
        }

        public class WebResourceWallpaper : WebResourceBase
        {
            public WebResourceWallpaper(string _path, float _opacity)
            {
                path = _path;
                opacity = _opacity;
            }

            private readonly string path;
            private readonly float opacity;

            private byte[] cache;

            //<未実装>ぼかし効果に対応
            //<未実装>埋め込まれたリソースを選択できるようにする
            public override byte[] GetData()
            {
                if (cache == null)
                    if (path == null || !File.Exists(path))
                        cache = new byte[] { };
                    else
                        using (MemoryStream ms = new MemoryStream())
                        using (Bitmap bitmap = new Bitmap(path))
                        using (Bitmap bitmap2 = new Bitmap(bitmap.Width, bitmap.Height))
                        using (Graphics g = Graphics.FromImage(bitmap2))
                        {
                            ColorMatrix cm = new ColorMatrix();
                            cm.Matrix00 = 1;
                            cm.Matrix11 = 1;
                            cm.Matrix22 = 1;
                            cm.Matrix33 = opacity;
                            cm.Matrix44 = 1;

                            ImageAttributes ia = new ImageAttributes();
                            ia.SetColorMatrix(cm);

                            g.DrawImage(bitmap, new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height), 0, 0, bitmap.Width, bitmap.Height, GraphicsUnit.Pixel, ia);

                            bitmap2.Save(ms, ImageFormat.Png);

                            return ms.ToArray();
                        }

                return cache;
            }
        }

        public class WebResourceEmbedded : WebResourceBase
        {
            public WebResourceEmbedded(string _path, Assembly _assembly)
            {
                path = _path;
                assembly = _assembly;
            }

            private readonly string path;
            private readonly Assembly assembly;

            private byte[] cache;

            public override byte[] GetData()
            {
                if (cache == null)
                    using (Stream stream = assembly.GetManifestResourceStream(path))
                    {
                        cache = new byte[stream.Length];
                        stream.Read(cache, 0, cache.Length);
                    }

                return cache;
            }
        }

        public class WebResourceHome : WebResourceBase
        {
            public WebResourceHome(bool _isDefault, string _embeddedPath, string _customPath, Assembly _assembly)
            {
                isDefault = _isDefault;
                embeddedPath = _embeddedPath;
                customPath = _customPath;
                assembly = _assembly;
            }

            private readonly bool isDefault;
            private readonly string embeddedPath;
            private readonly string customPath;
            private readonly Assembly assembly;

            public string host { get; set; }
            public ushort port { get; set; }

            private string cache;

            public override byte[] GetData()
            {
                if (cache == null)
                    if (!isDefault && customPath != null && File.Exists(customPath))
                        cache = File.ReadAllText(customPath);
                    else
                        using (Stream stream = assembly.GetManifestResourceStream(embeddedPath))
                        {
                            byte[] cacheBytes = new byte[stream.Length];
                            stream.Read(cacheBytes, 0, cacheBytes.Length);
                            cache = Encoding.UTF8.GetString(cacheBytes);
                        }

                return Encoding.UTF8.GetBytes(cache.Replace("%%host%%", host).Replace("%%port%%", port.ToString()));
            }
        }

        public MainWindow(Core _core, Program.Logger _logger, Program.ProgramSettings _psettings, Program.ProgramStatus _pstatus, string _appname, string _version, string _appnameWithVersion, string _lisenceTextFilename, Assembly _assembly, string _basepath, Action<Exception, Program.ExceptionKind> __OnException, Func<byte[], Version, bool> __UpVersion, List<UnhandledExceptionEventHandler> _unhandledExceptionEventHandlers, List<DispatcherUnhandledExceptionEventHandler> _dispatcherUnhandledExceptionEventHandlers)
        {
            core = _core;
            logger = _logger;
            psettings = _psettings;
            pstatus = _pstatus;
            appname = _appname;
            version = _version;
            appnameWithVersion = _appnameWithVersion;
            lisenceTextFilePath = Path.Combine(_basepath, _lisenceTextFilename);
            assembly = _assembly;

            _OnException = __OnException;
            _UpVersion = __UpVersion;

            unhandledExceptionEventHandlers = _unhandledExceptionEventHandlers;
            dispatcherUnhandledExceptionEventHandlers = _dispatcherUnhandledExceptionEventHandlers;

            InitializeComponent();

            Title = _appnameWithVersion;
            miFile.Header = "ファイル".Multilanguage(19) + "(_F)";
            miClose.Header = "終了".Multilanguage(20) + "(_X)";
            miTool.Header = "ツール".Multilanguage(48) + "(_T)";
            miSettings.Header = "設定".Multilanguage(49) + "(_S)...";
            miMining.Header = "採掘開始".Multilanguage(135) + "(_M)";
            miHelp.Header = "ヘルプ".Multilanguage(21) + "(_H)";
            miAbout.Header = "CREAについて".Multilanguage(22) + "(_A)...";
        }

        private void NewAccountHolder(Window window)
        {
            NewAccountHolderWindow nahw = new NewAccountHolderWindow();
            nahw.Owner = window;
            if (nahw.ShowDialog() == true)
                core.iAccountHolders.iAddAccountHolder(core.iAccountHoldersFactory.CreatePseudonymousAccountHolder(nahw.tbAccountHolder.Text));
        }

        private void NewAccount(Window window, bool? isAnonymous, IAccountHolder iAccountHolder)
        {
            NewAccountWindow naw = new NewAccountWindow((window2) => NewAccountHolder(window2));
            naw.Owner = window;

            Action _Clear = () => naw.cbAccountHolder.Items.Clear();
            Action _Add = () =>
            {
                foreach (var ah in core.iAccountHolders.iPseudonymousAccountHolders)
                    naw.cbAccountHolder.Items.Add(ah);
            };

            EventHandler<IAccountHolder> accountHolderAdded = (sender2, e2) => _Clear.AndThen(_Add).ExecuteInUIThread();

            core.iAccountHolders.iAccountHolderAdded += accountHolderAdded;

            _Add();

            if (!isAnonymous.HasValue || isAnonymous.Value)
                naw.rbAnonymous.IsChecked = true;
            else
            {
                naw.rbPseudonymous.IsChecked = true;
                if (iAccountHolder != null)
                    naw.cbAccountHolder.SelectedItem = iAccountHolder;
            }

            if (naw.ShowDialog() == true)
            {
                IAccountHolder ahTarget = null;
                if (naw.rbAnonymous.IsChecked == true)
                    ahTarget = core.iAccountHolders.iAnonymousAccountHolder;
                else
                    foreach (var ah in core.iAccountHolders.iPseudonymousAccountHolders)
                        if (ah == naw.cbAccountHolder.SelectedItem)
                            ahTarget = ah;

                if (ahTarget != null)
                    ahTarget.iAddAccount(core.iAccountHoldersFactory.CreateAccount(naw.tbName.Text, naw.tbDescription.Text));
            }

            core.iAccountHolders.iAccountHolderAdded -= accountHolderAdded;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            mws = new MainWindowSettings();

            if (pstatus.IsFirst)
            {
                if (!File.Exists(lisenceTextFilePath))
                    throw new FileNotFoundException("lisence_text_not_found");

                LisenceWindow lw = new LisenceWindow(File.ReadAllText(lisenceTextFilePath));
                lw.Owner = this;
                if (lw.ShowDialog() == false)
                {
                    Close();
                    return;
                }

                pstatus.IsFirst = false;
            }

            string wallpaperFileName = "/back.png";

            WebResourceHome webResourceHome = new WebResourceHome(mws.IsDefaultUi, "CREA2014.WebResources.home2.htm", Path.Combine(mws.UiFilesDirectory, "home2.htm"), assembly);
            webResourceHome.port = (ushort)mws.PortWebSocket;

            Dictionary<string, WebResourceBase> resources = new Dictionary<string, WebResourceBase>();
            resources.Add(wallpaperFileName, new WebResourceWallpaper(mws.IsWallpaper ? mws.Wallpaper : null, mws.WallpaperOpacity));
            resources.Add("/favicon.ico", new WebResourceEmbedded("CREA2014.up0669_2.ico", assembly));
            resources.Add("/knockout-3.2.0.js", new WebResourceEmbedded("CREA2014.WebResources.knockout-3.2.0.js", assembly));
            resources.Add("/jquery-2.1.1.js", new WebResourceEmbedded("CREA2014.WebResources.jquery-2.1.1.js", assembly));
            resources.Add("/jquery-ui-1.10.4.custom.js", new WebResourceEmbedded("CREA2014.WebResources.jquery-ui-1.10.4.custom.js", assembly));
            resources.Add("/", webResourceHome);

            _CreateUiFiles = (basePath) =>
            {
                foreach (var path in new string[] { "CREA2014.WebResources.home2.htm" })
                {
                    string fullPath = Path.Combine(basePath, path);
                    if (File.Exists(fullPath))
                        File.Move(fullPath, fullPath + DateTime.Now.Ticks.ToString());
                    using (Stream stream = assembly.GetManifestResourceStream(path))
                    {
                        byte[] data = new byte[stream.Length];
                        stream.Read(data, 0, data.Length);
                        File.WriteAllText(fullPath, Encoding.UTF8.GetString(data));
                    }
                }
            };

            Action _StartWebServer = () =>
            {
                if (!HttpListener.IsSupported)
                    throw new Exception("http_listener_not_supported");

                DefaltNetworkInterface defaultNetworkInterface = new DefaltNetworkInterface();
                defaultNetworkInterface.Get();

                HttpListener innerHl = hl = new HttpListener();
                innerHl.Prefixes.Add("http://*:" + mws.PortWebServer.ToString() + "/");
                try
                {
                    innerHl.Start();
                }
                catch (HttpListenerException ex)
                {
                    throw new HttpListenerException(ex.ErrorCode, "require_administrator");
                }

                Thread thread = new Thread(() =>
                {
                    while (true)
                    {
                        HttpListenerContext hlc = null;

                        try
                        {
                            hlc = innerHl.GetContext();
                        }
                        catch (HttpListenerException)
                        {
                            innerHl.Close();
                            break;
                        }

                        using (HttpListenerResponse hlres = hlc.Response)
                            if (resources.Keys.Contains(hlc.Request.RawUrl))
                            {
                                byte[] data = null;
                                if (hlc.Request.RawUrl == "/")
                                {
                                    hlres.StatusCode = (int)HttpStatusCode.OK;
                                    hlres.ContentType = MediaTypeNames.Text.Html;
                                    hlres.ContentEncoding = Encoding.UTF8;

                                    WebResourceHome wrh = resources[hlc.Request.RawUrl] as WebResourceHome;

                                    if (!hlc.Request.RemoteEndPoint.Address.Equals(IPAddress.Loopback) && !hlc.Request.RemoteEndPoint.Address.Equals(IPAddress.IPv6Loopback))
                                        wrh.host = defaultNetworkInterface.MachineIpAddress.ToString();
                                    else
                                        wrh.host = "localhost";

                                    data = wrh.GetData();
                                }
                                else
                                    data = resources[hlc.Request.RawUrl].GetData();

                                hlres.OutputStream.Write(data, 0, data.Length);
                            }
                            else
                                throw new KeyNotFoundException("web_server_data");
                    }
                });
                thread.Start();
            };
            _StartWebServer();

            //<未改良>.NET Framework 4.5 のWenSocketを使用する
            SessionHandler<WebSocketSession, string> newMessageReceived = (session, message) =>
            {
                //2014/08/26
                //このイベントハンドラの中で例外が発生しても、例外を捕捉していないにも拘らず、
                //捕捉されなかった例外とならない
                //内部で例外が握り潰されているのではないかと思うが・・・
                //仕方がないので、全ての例外を捕捉し、本来例外が捕捉されなかった場合に実行する処理を特別に実行することにした

                try
                {
                    this.ExecuteInUIThread(() =>
                    {
                        if (message == "new_account_holder")
                            NewAccountHolder(this);
                        else if (message == "new_account")
                            NewAccount(this, null, null);
                        else
                            throw new NotSupportedException("wss_command");
                    });
                }
                catch (Exception ex)
                {
                    _OnException(ex, Program.ExceptionKind.unhandled);
                }
            };

            JSON json = new JSON();

            Func<string[]> _CreateBalanceJSON = () =>
            {
                string[] usableName = json.CreateJSONPair("name", "使用可能".Multilanguage(198));
                string[] usableValue = json.CreateJSONPair("value", core.UsableBalance.AmountInCreacoin.Amount);
                string[] usableUnit = json.CreateJSONPair("unit", Creacoin.Name);
                string[] usable = json.CreateJSONObject(usableName, usableValue, usableUnit);

                string[] unusableName = json.CreateJSONPair("name", "使用不能".Multilanguage(199));
                string[] unusableValue = json.CreateJSONPair("value", core.UnusableBalance.AmountInCreacoin.Amount);
                string[] unusableUnit = json.CreateJSONPair("unit", Creacoin.Name);
                string[] unusable = json.CreateJSONObject(unusableName, unusableValue, unusableUnit);

                string[] balanceName = json.CreateJSONPair("name", "残高".Multilanguage(200));
                string[] balanceValue = json.CreateJSONPair("value", core.Balance.AmountInCreacoin.Amount);
                string[] balanceUnit = json.CreateJSONPair("unit", Creacoin.Name);
                string[] balanceUsable = json.CreateJSONPair("usable", usable);
                string[] balanceUnusable = json.CreateJSONPair("unusable", unusable);
                return json.CreateJSONObject(balanceName, balanceValue, balanceUnit, balanceUsable, balanceUnusable);
            };

            Func<IAccount[], string[]> _CreateAccountsJSON = (iaccounts) =>
            {
                List<string[]> anonymousAccountsList = new List<string[]>();
                foreach (var iaccount in iaccounts)
                {
                    string[] accountName = json.CreateJSONPair("name", iaccount.iName);
                    string[] accountDescription = json.CreateJSONPair("description", iaccount.iDescription);
                    string[] accountAddress = json.CreateJSONPair("address", iaccount.iAddress);
                    anonymousAccountsList.Add(json.CreateJSONObject(accountName, accountDescription, accountAddress));
                }
                return json.CreateJSONArray(anonymousAccountsList.ToArray());
            };

            Func<string[]> _CreateAahJSON = () =>
            {
                string[] anonymousAccountHolderName = json.CreateJSONPair("name", "匿名".Multilanguage(207));
                string[] anonymousAccounts = json.CreateJSONPair("accounts", _CreateAccountsJSON(core.iAccountHolders.iAnonymousAccountHolder.iAccounts));
                return json.CreateJSONObject(anonymousAccountHolderName, anonymousAccounts);
            };

            Func<string[]> _CreatePahsJSON = () =>
            {
                List<string[]> pseudonymousAccountHoldersList = new List<string[]>();
                foreach (var pah in core.iAccountHolders.iPseudonymousAccountHolders)
                {
                    string[] pseudonymousAccountHolderName = json.CreateJSONPair("name", pah.iSign);
                    string[] pseudonymousAccounts = json.CreateJSONPair("accounts", _CreateAccountsJSON(pah.iAccounts));
                    pseudonymousAccountHoldersList.Add(json.CreateJSONObject(pseudonymousAccountHolderName, pseudonymousAccounts));
                }
                return json.CreateJSONArray(pseudonymousAccountHoldersList.ToArray());
            };

            Func<Program.LogData, string[]> _CreateLogJSON = (log) =>
            {
                string[] logType = json.CreateJSONPair("type", log.Kind.ToString());
                string[] logMessage = json.CreateJSONPair("message", log.ToString());
                return json.CreateJSONObject(logType, logMessage);
            };

            core.BalanceUpdated += (sender2, e2) =>
            {
                string[] balance = _CreateBalanceJSON();

                foreach (var wssession in wss.GetAllSessions())
                    wssession.Send("balanceUpdated " + string.Join(Environment.NewLine, balance));
            };

            Action _SendAccountHolders = () =>
            {
                string[] aah = _CreateAahJSON();
                string[] pahs = _CreatePahsJSON();

                foreach (var wssession in wss.GetAllSessions())
                {
                    wssession.Send("aahUpdated " + string.Join(Environment.NewLine, aah));
                    wssession.Send("pahsUpdated " + string.Join(Environment.NewLine, pahs));
                }
            };

            EventHandler _AccountChanged = (sender2, e2) => _SendAccountHolders();
            EventHandler<IAccount> _AccountAdded = (sender2, e2) =>
            {
                _SendAccountHolders();

                e2.iAccountChanged += _AccountChanged;
            };
            EventHandler<IAccount> _AccountRemoved = (sender2, e2) =>
            {
                _SendAccountHolders();

                e2.iAccountChanged -= _AccountChanged;
            };

            Action<IAccountHolder> _SubscribeEvents = (accountHolder) =>
            {
                accountHolder.iAccountAdded += _AccountAdded;
                accountHolder.iAccountRemoved += _AccountRemoved;
                foreach (var account in accountHolder.iAccounts)
                    account.iAccountChanged += _AccountChanged;
            };

            _SubscribeEvents(core.iAccountHolders.iAnonymousAccountHolder);
            foreach (var pseudonymousAccountHolder in core.iAccountHolders.iPseudonymousAccountHolders)
                _SubscribeEvents(pseudonymousAccountHolder);

            core.iAccountHolders.iAccountHolderAdded += (sender2, e2) =>
            {
                _SendAccountHolders();

                _SubscribeEvents(e2);
            };
            core.iAccountHolders.iAccountHolderRemoved += (sender2, e2) =>
            {
                _SendAccountHolders();

                e2.iAccountAdded -= _AccountAdded;
                e2.iAccountRemoved -= _AccountRemoved;
                foreach (var account in e2.iAccounts)
                    account.iAccountChanged -= _AccountChanged;
            };

            logger.LogAdded += (sender2, e2) =>
            {
                string[] log = _CreateLogJSON(e2);

                foreach (var wssession in wss.GetAllSessions())
                    wssession.Send("logAdded " + string.Join(Environment.NewLine, log));
            };

            WebSocketServer oldWss;
            wss = new WebSocketServer();
            wss.NewSessionConnected += (wssession) =>
            {
                string[] partBalanceName = json.CreateJSONPair("name", "残高".Multilanguage(201));
                string[] partBalanceDetail = json.CreateJSONPair("detail", _CreateBalanceJSON());
                string[] partBalance = json.CreateJSONObject(partBalanceName, partBalanceDetail);

                string[] accountHolderColumnName = json.CreateJSONPair("name", "口座名".Multilanguage(202));
                string[] accountHolderColumnDescription = json.CreateJSONPair("description", "説明".Multilanguage(203));
                string[] accountHolderColumnAddress = json.CreateJSONPair("address", "口座番号".Multilanguage(204));
                string[] accountHolderColumns = json.CreateJSONObject(accountHolderColumnName, accountHolderColumnDescription, accountHolderColumnAddress);

                string[] buttonNewAccountHolderName = json.CreateJSONPair("name", "新しい口座名義".Multilanguage(205));
                string[] buttonNewAccountHolderKeyName = json.CreateJSONPair("keyName", "A");
                string[] buttonNewAccountHolderKey = json.CreateJSONPair("key", ((int)Key.A).ToString());
                string[] buttonNewAccountHolder = json.CreateJSONPair("buttonNewAccountHolder", json.CreateJSONObject(buttonNewAccountHolderName, buttonNewAccountHolderKeyName, buttonNewAccountHolderKey));

                string[] buttonNewAccountName = json.CreateJSONPair("name", "新しい口座".Multilanguage(206));
                string[] buttonNewAccountKeyName = json.CreateJSONPair("keyName", "B");
                string[] buttonNewAccountKey = json.CreateJSONPair("key", ((int)Key.B).ToString());
                string[] buttonNewAccount = json.CreateJSONPair("buttonNewAccount", json.CreateJSONObject(buttonNewAccountName, buttonNewAccountKeyName, buttonNewAccountKey));

                List<string[]> logsList = new List<string[]>();
                foreach (var log in logger.Logs.Reverse())
                    logsList.Add(_CreateLogJSON(log));

                List<string[]> chatsList = new List<string[]>();

                Chat chat1 = new Chat();
                chat1.LoadVersion0("ゆみにゃん", "ぬるぽ");
                Chat chat2 = new Chat();
                chat2.LoadVersion0("ゆいにゃん", "ガッ");

                foreach (var chat in new Chat[] { chat1, chat2 })
                {
                    string[] chatName = json.CreateJSONPair("name", chat.Name);
                    string[] chatMessage = json.CreateJSONPair("message", chat.Message);
                    chatsList.Add(json.CreateJSONObject(chatName, chatMessage));
                }

                string[] partAccountName = json.CreateJSONPair("name", "受け取り口座".Multilanguage(208));
                string[] partAccountButtons = json.CreateJSONPair("accountButtons", json.CreateJSONObject(buttonNewAccountHolder, buttonNewAccount));
                string[] partAccountColumns = json.CreateJSONPair("accountHolderColumns", accountHolderColumns);
                string[] partAccountAah = json.CreateJSONPair("anonymousAccountHolder", _CreateAahJSON());
                string[] partAccountPahs = json.CreateJSONPair("pseudonymousAccountHolders", _CreatePahsJSON());
                string[] partAccount = json.CreateJSONObject(partAccountName, partAccountButtons, partAccountColumns, partAccountAah, partAccountPahs);

                string[] partLogName = json.CreateJSONPair("name", "運用記録".Multilanguage(209));
                string[] partLogItems = json.CreateJSONPair("logs", json.CreateJSONArray(logsList.ToArray()));
                string[] partLog = json.CreateJSONObject(partLogName, partLogItems);

                string[] partChatName = json.CreateJSONPair("name", "チャット");
                string[] partChatItems = json.CreateJSONPair("chats", json.CreateJSONArray(chatsList.ToArray()));
                string[] partChat = json.CreateJSONObject(partChatName, partChatItems);

                string[] universeTitle = json.CreateJSONPair("title", appnameWithVersion);
                string[] universePartBalance = json.CreateJSONPair("partBalance", partBalance);
                string[] universePartAccount = json.CreateJSONPair("partAccount", partAccount);
                string[] universePartLog = json.CreateJSONPair("partLog", partLog);
                string[] universePartChat = json.CreateJSONPair("partChat", partChat);
                string[] universe = json.CreateJSONObject(universeTitle, universePartBalance, universePartAccount, universePartLog, universePartChat);

                string jsonString = string.Join(Environment.NewLine, universe);

                wssession.Send("initial_data " + jsonString);
            };
            wss.NewMessageReceived += newMessageReceived;
            wss.Setup(mws.PortWebSocket);
            wss.Start();

            //wb.Navigated += (sender2, e2) => ((mshtml.HTMLDocument)wb.Document).focus();
            wb.Navigate("http://localhost:" + mws.PortWebServer.ToString() + "/");

            mws.SettingsChanged += (sender2, e2) =>
            {
                if (mws.isPortWebServerAltered)
                {
                    hl.Abort();

                    _StartWebServer();

                    wb.Navigate("http://localhost:" + mws.PortWebServer.ToString() + "/");
                }
                else
                {
                    if (mws.isIsWallpaperAltered || mws.isWallpaperAltered || mws.isWallpaperOpacityAltered)
                    {
                        resources.Remove(wallpaperFileName);
                        wallpaperFileName = "/back" + DateTime.Now.Ticks.ToString() + ".png";
                        resources.Add(wallpaperFileName, new WebResourceWallpaper(mws.IsWallpaper ? mws.Wallpaper : null, mws.WallpaperOpacity));

                        foreach (var wssession in wss.GetAllSessions())
                            wssession.Send("wallpaper " + wallpaperFileName);
                    }
                    if (mws.isPortWebSocketAltered)
                    {
                        (resources["/"] as WebResourceHome).port = (ushort)mws.PortWebSocket;

                        //2014/07/02
                        //<未実装>古いイベントの登録を解除していない
                        oldWss = wss;
                        wss = new WebSocketServer();
                        wss.NewSessionConnected += (session) =>
                        {
                            if (oldWss != null)
                            {
                                oldWss.Stop();
                                oldWss = null;
                            }
                        };
                        wss.NewMessageReceived += newMessageReceived;
                        wss.Setup(mws.PortWebSocket);
                        wss.Start();

                        foreach (var wssession in oldWss.GetAllSessions())
                            wssession.Send("wss " + mws.PortWebSocket.ToString());
                    }
                    if (mws.isIsDefaultUiAltered || mws.isUiFilesDirectoryAltered)
                    {
                        resources.Remove("/");
                        resources.Add("/", new WebResourceHome(mws.IsDefaultUi, "CREA2014.WebResources.home2.htm", Path.Combine(mws.UiFilesDirectory, "home2.htm"), assembly));

                        wb.Navigate("http://localhost:" + mws.PortWebServer.ToString() + "/");
                    }
                }
            };
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (mws.IsConfirmAtExit && MessageBox.Show(string.Format("{0}を終了しますか？".Multilanguage(50), appname), appname, MessageBoxButton.YesNo) == MessageBoxResult.No)
                e.Cancel = true;
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            if (hl != null)
                hl.Abort();
            if (wss != null)
                wss.Stop();

            mws.Save();
        }

        private void miClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void miSettings_Click(object sender, RoutedEventArgs e)
        {
            SettingsWindow sw = new SettingsWindow(appname, (sw2) =>
            {
                sw2.tbPortWebSocket.Text = mws.PortWebSocket.ToString();
                sw2.tbPortWebServer.Text = mws.PortWebServer.ToString();
                sw2.cbIsWallpaper.IsChecked = mws.IsWallpaper;
                sw2.tbWallpaper.Text = mws.Wallpaper;
                sw2.tbWallpaperOpacity.Text = mws.WallpaperOpacity.ToString();
                sw2.rbDefault.IsChecked = mws.IsDefaultUi;
                sw2.rbNotDefault.IsChecked = !mws.IsDefaultUi;
                sw2.tbUiFilesDirectory.Text = mws.UiFilesDirectory;
                sw2.cbConfirmAtExit.IsChecked = mws.IsConfirmAtExit;
            }, _CreateUiFiles);
            sw.Owner = this;

            if (sw.ShowDialog() == true)
            {
                mws.StartSetting();

                mws.PortWebSocket = int.Parse(sw.tbPortWebSocket.Text);
                mws.PortWebServer = int.Parse(sw.tbPortWebServer.Text);
                mws.IsWallpaper = (bool)sw.cbIsWallpaper.IsChecked;
                mws.Wallpaper = sw.tbWallpaper.Text;
                mws.WallpaperOpacity = float.Parse(sw.tbWallpaperOpacity.Text);
                mws.IsDefaultUi = sw.rbDefault.IsChecked.Value;
                mws.UiFilesDirectory = sw.tbUiFilesDirectory.Text;
                mws.IsConfirmAtExit = (bool)sw.cbConfirmAtExit.IsChecked;

                mws.EndSetting();
            }
        }

        private void miMining_Click(object sender, RoutedEventArgs e)
        {
            if (isMining)
            {
                core.EndMining();

                miMining.Header = "採掘開始".Multilanguage(135) + "(_M)";
            }
            else
            {
                MiningWindow mw = null;

                IAccountHolder iAccountHolder = null;

                Action _ClearAccount = () => mw.cbAccount.Items.Clear();
                Action _AddAccount = () =>
                {
                    foreach (var account in iAccountHolder.iAccounts)
                        mw.cbAccount.Items.Add(account);
                };

                EventHandler<IAccount> accountAdded = (sender2, e2) => _ClearAccount.AndThen(_AddAccount).ExecuteInUIThread();

                mw = new MiningWindow((window2) => NewAccountHolder(window2), (window2) => NewAccount(window2, mw.rbAnonymous.IsChecked, iAccountHolder), () =>
                {
                    if (iAccountHolder != null)
                    {
                        iAccountHolder.iAccountAdded -= accountAdded;

                        _ClearAccount();
                    }

                    if (mw.rbAnonymous.IsChecked.Value)
                        iAccountHolder = core.iAccountHolders.iAnonymousAccountHolder;
                    else
                        iAccountHolder = mw.cbAccountHolder.SelectedItem as IAccountHolder;

                    if (iAccountHolder != null)
                    {
                        iAccountHolder.iAccountAdded += accountAdded;

                        _AddAccount();
                    }
                });
                mw.Owner = this;

                Action _ClearAccountHolder = () => mw.cbAccountHolder.Items.Clear();
                Action _AddAccountHolder = () =>
                {
                    foreach (var ah in core.iAccountHolders.iPseudonymousAccountHolders)
                        mw.cbAccountHolder.Items.Add(ah);
                };

                EventHandler<IAccountHolder> accountHolderAdded = (sender2, e2) => _ClearAccountHolder.AndThen(_AddAccountHolder).ExecuteInUIThread();

                core.iAccountHolders.iAccountHolderAdded += accountHolderAdded;

                _AddAccountHolder();

                if (mws.MiningAccountHolder == string.Empty)
                    mw.rbAnonymous.IsChecked = true;
                else
                {
                    mw.rbPseudonymous.IsChecked = true;
                    mw.cbAccountHolder.SelectedItem = core.iAccountHolders.iPseudonymousAccountHolders.FirstOrDefault((elem) => elem.iName == mws.MiningAccountHolder);
                }

                mw.cbAccount.SelectedItem = iAccountHolder.iAccounts.FirstOrDefault((elem) => elem.iName == mws.MiningAccount);

                if (mw.ShowDialog() == true)
                {
                    mws.StartSetting();

                    mws.MiningAccountHolder = mw.rbAnonymous.IsChecked.Value ? string.Empty : (mw.cbAccountHolder.SelectedItem as IPseudonymousAccountHolder).iName;
                    mws.MiningAccount = (mw.cbAccount.SelectedItem as IAccount).iName;

                    mws.EndSetting();

                    core.StartMining(mw.cbAccount.SelectedItem as IAccount);

                    miMining.Header = "採掘停止".Multilanguage(136) + "(_M)";
                }

                core.iAccountHolders.iAccountHolderAdded -= accountHolderAdded;

                iAccountHolder.iAccountAdded -= accountAdded;

                if (!mw.DialogResult.Value)
                    return;
            }

            isMining = !isMining;
        }

        private void miAbout_Click(object sender, RoutedEventArgs e)
        {
            AboutWindow aw = new AboutWindow(version);
            aw.Owner = this;
            aw.ShowDialog();
        }

        private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            //if (e.Key == Key.Up || e.Key == Key.Down || e.Key == Key.Left || e.Key == Key.Right)
            //    return;

            if (e.Key == Key.System)
            {
                foreach (var wssession in wss.GetAllSessions())
                    wssession.Send("keydown " + ((int)e.SystemKey).ToString());

                e.Handled = true;
            }
        }

        private void miTest_Click(object sender, RoutedEventArgs e)
        {
            if (_UpVersion(File.ReadAllBytes(assembly.Location), assembly.GetName().Version))
                Close();
            else
                MessageBox.Show("失敗");
        }
    }
}