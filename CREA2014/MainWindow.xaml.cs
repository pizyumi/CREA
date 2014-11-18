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
            public WebResourceBase(string _url) { url = _url; }

            public string url { get; private set; }
            public abstract byte[] GetData();
        }

        public class RebResourceWallpaper : WebResourceBase
        {
            public RebResourceWallpaper(string _url) : base(_url) { }

            private byte[] cache;

            public override byte[] GetData()
            {
                if (cache == null)
                {

                }

                return cache;
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

            //<未実装>ぼかし効果に対応
            //<未実装>埋め込まれたリソースを選択できるようにする
            Func<byte[]> _GetWallpaperData = () =>
            {
                if (mws.IsWallpaper && File.Exists(mws.Wallpaper))
                    using (MemoryStream memoryStream = new MemoryStream())
                    using (Bitmap bitmap = new Bitmap(mws.Wallpaper))
                    using (Bitmap bitmap2 = new Bitmap(bitmap.Width, bitmap.Height))
                    using (Graphics g = Graphics.FromImage(bitmap2))
                    {
                        ColorMatrix cm = new ColorMatrix();
                        cm.Matrix00 = 1;
                        cm.Matrix11 = 1;
                        cm.Matrix22 = 1;
                        cm.Matrix33 = mws.WallpaperOpacity;
                        cm.Matrix44 = 1;

                        ImageAttributes ia = new ImageAttributes();
                        ia.SetColorMatrix(cm);

                        g.DrawImage(bitmap, new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height), 0, 0, bitmap.Width, bitmap.Height, GraphicsUnit.Pixel, ia);

                        bitmap2.Save(memoryStream, ImageFormat.Png);

                        return memoryStream.ToArray();
                    }
                else
                    return new byte[] { };
            };
            Func<string, string> _GetWallpaperFileName = (id) => id == null ? "/back.png" : "/back" + id + ".png";

            Func<int, string> _GetWssAddress = (wssPort) => "ws://localhost:" + wssPort.ToString() + "/";

            //2014/07/02
            //UIファイルが変更された場合には、一旦空にする
            //<未改良>Streamをif文の中に、処理を纏める
            Dictionary<string, string> webResourceCache = new Dictionary<string, string>();
            Func<string, string> _GetWebResource = (filename) =>
            {
                if (mws.IsDefaultUi)
                    using (Stream stream = assembly.GetManifestResourceStream(filename))
                    {
                        string webResource = null;

                        if (webResourceCache.Keys.Contains(filename))
                            webResource = webResourceCache[filename];
                        else
                        {
                            byte[] data = new byte[stream.Length];
                            stream.Read(data, 0, data.Length);
                            webResourceCache.Add(filename, webResource = Encoding.UTF8.GetString(data));
                        }

                        return webResource;
                    }
                else
                {
                    string webResource = null;

                    if (webResourceCache.Keys.Contains(filename))
                        webResource = webResourceCache[filename];
                    else
                    {
                        string path = Path.Combine(mws.UiFilesDirectory, filename);
                        if (File.Exists(path))
                            webResourceCache.Add(filename, webResource = File.ReadAllText(path));
                        else
                            using (Stream stream = assembly.GetManifestResourceStream(filename))
                            {
                                byte[] data = new byte[stream.Length];
                                stream.Read(data, 0, data.Length);
                                webResourceCache.Add(filename, webResource = Encoding.UTF8.GetString(data));

                                File.WriteAllText(path, webResource);
                            }
                    }

                    return webResource;
                }
            };

            string pathHomeHtm = "CREA2014.WebResources.home.htm";
            string pathButtonHtm = "CREA2014.WebResources.button.htm";
            string pathAccHolsHtm = "CREA2014.WebResources.acc_hols.htm";
            string pathAccHolHtm = "CREA2014.WebResources.acc_hol.htm";
            string pathAccHtm = "CREA2014.WebResources.acc.htm";
            string pathErrorLogHtm = "CREA2014.WebResources.error_log.htm";
            string pathLogHtm = "CREA2014.WebResources.log.htm";

            string[] paths = new string[] { pathHomeHtm, pathButtonHtm, pathAccHolsHtm, pathAccHolHtm, pathAccHtm, pathErrorLogHtm, pathLogHtm };

            _CreateUiFiles = (basePath) =>
            {
                foreach (var path in paths)
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

            string wallpaperFileName = null;
            Func<Dictionary<string, byte[]>> _GetWebServerData = () =>
            {
                Dictionary<string, byte[]> iWebServerData = new Dictionary<string, byte[]>();

                iWebServerData.Add(wallpaperFileName = _GetWallpaperFileName(null), _GetWallpaperData());

                byte[] iconData = null;
                using (Stream stream = assembly.GetManifestResourceStream("CREA2014.up0669_2.ico"))
                {
                    iconData = new byte[stream.Length];
                    stream.Read(iconData, 0, iconData.Length);
                }

                iWebServerData.Add("/favicon.ico", iconData);

                Func<string, string> homeHtmProcessor = (data) =>
                {
                    data = data.Replace("%%title%%", appnameWithVersion).Replace("%%address%%", _GetWssAddress(mws.PortWebSocket));

                    string buttonBaseHtml = _GetWebResource(pathButtonHtm);

                    foreach (var button in new[] { 
                        new { identifier = "new_account_holder", name = "button1", text = "新しい口座名義".Multilanguage(60) + "(<u>A</u>)...", command = "new_account_holder", key = Key.A }, 
                        new { identifier = "new_account", name = "button2", text = "新しい口座".Multilanguage(61) + "(<u>B</u>)...", command = "new_account", key = Key.B }, 
                    })
                        data = data.Replace("%%" + button.identifier + "%%", buttonBaseHtml.Replace("button1", button.name).Replace("%%text%%", button.text).Replace("%%command%%", button.command).Replace("%%key%%", ((int)button.key).ToString()));

                    return data;
                };
                Func<string, string> homeHtmProcessor2 = (data) =>
                {
                    data = data.Replace("%%address%%", _GetWssAddress(mws.PortWebSocket));

                    return data;
                };
                Func<string, string> doNothing = (data) => data;

                foreach (var wsr in new[] {
                    //new {path = pathHomeHtm, url = "/", processor = homeHtmProcessor}, 
                    new {path = "CREA2014.WebResources.home2.htm", url = "/", processor = homeHtmProcessor2}, 
                    new {path = "CREA2014.WebResources.knockout-3.2.0.js", url = "/knockout-3.2.0.js", processor = doNothing}, 
                    new {path = "CREA2014.WebResources.jquery-2.1.1.js", url = "/jquery-2.1.1.js", processor = doNothing}, 
                    new {path = "CREA2014.WebResources.jquery-ui-1.10.4.custom.js", url = "/jquery-ui-1.10.4.custom.js", processor = doNothing}, 
                })
                    iWebServerData.Add(wsr.url, Encoding.UTF8.GetBytes(wsr.processor(_GetWebResource(wsr.path))));

                return iWebServerData;
            };
            Dictionary<string, byte[]> webServerData = _GetWebServerData();

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
                            if (webServerData.Keys.Contains(hlc.Request.RawUrl) && webServerData[hlc.Request.RawUrl] != null)
                            {
                                if (hlc.Request.RawUrl == "/")
                                {
                                    hlres.StatusCode = (int)HttpStatusCode.OK;
                                    hlres.ContentType = MediaTypeNames.Text.Html;
                                    hlres.ContentEncoding = Encoding.UTF8;
                                }

                                if (hlc.Request.RawUrl == "/" && (hlc.Request.RemoteEndPoint.Address != IPAddress.Loopback || hlc.Request.RemoteEndPoint.Address != IPAddress.IPv6Loopback))
                                {
                                    byte[] bytes = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(webServerData[hlc.Request.RawUrl]).Replace("localhost", defaultNetworkInterface.MachineIpAddress.ToString()));

                                    hlres.OutputStream.Write(bytes, 0, bytes.Length);
                                }
                                else
                                    hlres.OutputStream.Write(webServerData[hlc.Request.RawUrl], 0, webServerData[hlc.Request.RawUrl].Length);
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

            Func<string> _GetAccountHolderHtml = () =>
            {
                string accHolsHtml = _GetWebResource(pathAccHolsHtm);
                string accHolHtml = _GetWebResource(pathAccHolHtm);
                string accHtml = _GetWebResource(pathAccHtm);

                string accs = string.Concat(from i in core.iAccountHolders.iAnonymousAccountHolder.iAccounts
                                            select accHtml.Replace("%%name%%", i.iName).Replace("%%description%%", i.iDescription).Replace("%%address%%", i.iAddress));

                string psu_acc_hols = string.Concat(from i in core.iAccountHolders.iPseudonymousAccountHolders
                                                    select accHolHtml.Replace("%%title%%", i.iSign).Replace("%%accs%%",
                                      string.Concat(from j in i.iAccounts
                                                    select accHtml.Replace("%%name%%", j.iName).Replace("%%description%%", j.iDescription).Replace("%%address%%", j.iAddress))));

                return accHolsHtml.Replace("%%accs%%", accs).Replace("%%psu_acc_hols%%", psu_acc_hols);
            };

            Func<Program.LogData, string> _GetLogHtml = (logData) =>
            {
                if (logData.Kind == Program.LogData.LogKind.error)
                    return _GetWebResource(pathErrorLogHtm).Replace("%%log%%", logData.ToString().Replace(Environment.NewLine, "<br/>"));
                else
                    return _GetWebResource(pathLogHtm).Replace("%%log%%", logData.ToString().Replace(Environment.NewLine, "<br/>"));
            };

            Action<WebSocketSession> _SendBalance = (wssession) =>
            {
                wssession.Send("balance " + core.Balance.AmountInCreacoin.Amount.ToString() + "CREA");
                wssession.Send("usable_balance " + core.UsableBalance.AmountInCreacoin.Amount.ToString() + "CREA");
                wssession.Send("unusable_balance " + core.UnusableBalance.AmountInCreacoin.Amount.ToString() + "CREA");
            };

            WebSocketServer oldWss;
            wss = new WebSocketServer();
            wss.NewSessionConnected += (wssession) =>
            {
                JSON json = new JSON();

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

                string[] usableName = json.CreateJSONPair("name", "使用可能");
                string[] usableValue = json.CreateJSONPair("value", 0);
                string[] usableUnit = json.CreateJSONPair("unit", Creacoin.Name);
                string[] usable = json.CreateJSONObject(usableName, usableValue, usableUnit);

                string[] unusableName = json.CreateJSONPair("name", "使用不能");
                string[] unusableValue = json.CreateJSONPair("value", 0);
                string[] unusableUnit = json.CreateJSONPair("unit", Creacoin.Name);
                string[] unusable = json.CreateJSONObject(unusableName, unusableValue, unusableUnit);

                string[] balanceName = json.CreateJSONPair("name", "残高");
                string[] balanceValue = json.CreateJSONPair("value", 0);
                string[] balanceUnit = json.CreateJSONPair("unit", Creacoin.Name);
                string[] balanceUsable = json.CreateJSONPair("usable", usable);
                string[] balanceUnusable = json.CreateJSONPair("unusable", unusable);
                string[] balance = json.CreateJSONObject(balanceName, balanceValue, balanceUnit, balanceUsable, balanceUnusable);

                string[] partBalanceName = json.CreateJSONPair("name", "残高");
                string[] partBalanceDetail = json.CreateJSONPair("detail", balance);
                string[] partBalance = json.CreateJSONObject(partBalanceName, partBalanceDetail);

                string[] accountHolderColumnName = json.CreateJSONPair("name", "口座名");
                string[] accountHolderColumnDescription = json.CreateJSONPair("description", "説明");
                string[] accountHolderColumnAddress = json.CreateJSONPair("address", "口座番号");
                string[] accountHolderColumns = json.CreateJSONObject(accountHolderColumnName, accountHolderColumnDescription, accountHolderColumnAddress);

                string[] buttonNewAccountHolderName = json.CreateJSONPair("name", "新しい口座名義");
                string[] buttonNewAccountHolderKeyName = json.CreateJSONPair("keyName", "A");
                string[] buttonNewAccountHolderKey = json.CreateJSONPair("key", ((int)Key.A).ToString());
                string[] buttonNewAccountHolder = json.CreateJSONPair("buttonNewAccountHolder", json.CreateJSONObject(buttonNewAccountHolderName, buttonNewAccountHolderKeyName, buttonNewAccountHolderKey));

                string[] buttonNewAccountName = json.CreateJSONPair("name", "新しい口座");
                string[] buttonNewAccountKeyName = json.CreateJSONPair("keyName", "B");
                string[] buttonNewAccountKey = json.CreateJSONPair("key", ((int)Key.B).ToString());
                string[] buttonNewAccount = json.CreateJSONPair("buttonNewAccount", json.CreateJSONObject(buttonNewAccountName, buttonNewAccountKeyName, buttonNewAccountKey));

                string[] accountButtons = json.CreateJSONPair("accountButtons", json.CreateJSONObject(buttonNewAccountHolder, buttonNewAccount));

                string[] anonymousAccountHolderName = json.CreateJSONPair("name", "匿名");
                string[] anonymousAccounts = json.CreateJSONPair("accounts", _CreateAccountsJSON(core.iAccountHolders.iAnonymousAccountHolder.iAccounts));

                List<string[]> pseudonymousAccountHoldersList = new List<string[]>();
                foreach (var pah in core.iAccountHolders.iPseudonymousAccountHolders)
                {
                    string[] pseudonymousAccountHolderName = json.CreateJSONPair("name", pah.iSign);
                    string[] pseudonymousAccounts = json.CreateJSONPair("accounts", _CreateAccountsJSON(pah.iAccounts));
                    pseudonymousAccountHoldersList.Add(json.CreateJSONObject(pseudonymousAccountHolderName, pseudonymousAccounts));
                }

                List<string[]> logsList = new List<string[]>();
                foreach (var log in logger.Logs.Reverse())
                {
                    string[] logType = json.CreateJSONPair("type", log.Kind.ToString());
                    string[] logMessage = json.CreateJSONPair("message", log.ToString());
                    logsList.Add(json.CreateJSONObject(logType, logMessage));
                }

                string[] partAccountName = json.CreateJSONPair("name", "受け取り口座");
                string[] partAccountColumns = json.CreateJSONPair("accountHolderColumns", accountHolderColumns);
                string[] anonymousAccountHolder = json.CreateJSONPair("anonymousAccountHolder", json.CreateJSONObject(anonymousAccountHolderName, anonymousAccounts));
                string[] pseudonymousAccountHolders = json.CreateJSONPair("pseudonymousAccountHolders", json.CreateJSONArray(pseudonymousAccountHoldersList.ToArray()));
                string[] partAccount = json.CreateJSONObject(partAccountName, accountButtons, partAccountColumns, anonymousAccountHolder, pseudonymousAccountHolders);

                string[] partLogName = json.CreateJSONPair("name", "運用記録");
                string[] partLogItems = json.CreateJSONPair("logs", json.CreateJSONArray(logsList.ToArray()));
                string[] partLog = json.CreateJSONObject(partLogName, partLogItems);

                string[] universeTitle = json.CreateJSONPair("title", appnameWithVersion);
                string[] universePartBalance = json.CreateJSONPair("partBalance", partBalance);
                string[] universePartAccount = json.CreateJSONPair("partAccount", partAccount);
                string[] universePartLog = json.CreateJSONPair("partLog", partLog);
                string[] universe = json.CreateJSONObject(universeTitle, universePartBalance, universePartAccount, universePartLog);

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

                    webServerData = _GetWebServerData();
                    _StartWebServer();

                    wb.Navigate("http://localhost:" + mws.PortWebServer.ToString() + "/");
                }
                else
                {
                    if (mws.isIsWallpaperAltered || mws.isWallpaperAltered || mws.isWallpaperOpacityAltered)
                    {
                        webServerData.Remove(wallpaperFileName);
                        wallpaperFileName = _GetWallpaperFileName(DateTime.Now.Ticks.ToString());
                        webServerData.Add(wallpaperFileName, _GetWallpaperData());

                        foreach (var wssession in wss.GetAllSessions())
                            wssession.Send("wallpaper " + wallpaperFileName);
                    }
                    if (mws.isPortWebSocketAltered)
                    {
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
                            wssession.Send("wss " + _GetWssAddress(mws.PortWebSocket));
                    }
                    if (mws.isIsDefaultUiAltered || mws.isUiFilesDirectoryAltered)
                    {
                        webResourceCache.Clear();

                        wb.Navigate("http://localhost:" + mws.PortWebServer.ToString() + "/");
                    }
                }
            };

            Action _SendAccountHolders = () =>
            {
                foreach (var wssession in wss.GetAllSessions())
                    wssession.Send("acc_hols " + _GetAccountHolderHtml());
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
                foreach (var wssession in wss.GetAllSessions())
                    wssession.Send("log " + _GetLogHtml(e2));
            };

            core.BalanceUpdated += (sender2, e2) =>
            {
                foreach (var wssession in wss.GetAllSessions())
                    _SendBalance(wssession);
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