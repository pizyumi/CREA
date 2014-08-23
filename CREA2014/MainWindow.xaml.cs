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
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Input;

namespace CREA2014
{
    public partial class MainWindow : Window
    {
        public class MainWindowSettings : SAVEABLESETTINGSDATA<MainWindowSettings.Setter>
        {
            public class Setter
            {
                public Setter(Action<int> _portWebSocketSetter, Action<int> _portWebServerSetter, Action<bool> _isWallpaperSetter, Action<string> _wallpaperSetter, Action<float> _wallpaperOpacitySetter, Action<bool> _isDefaultUiSetter, Action<string> _uiFilesDirectorySetter, Action<string> _miningAccountHolderSetter, Action<string> _miningAccountSetter, Action<bool> _isConfirmAtExitSetter)
                {
                    portWebSocketSetter = _portWebSocketSetter;
                    portWebServerSetter = _portWebServerSetter;
                    isWallpaperSetter = _isWallpaperSetter;
                    wallpaperSetter = _wallpaperSetter;
                    wallpaperOpacitySetter = _wallpaperOpacitySetter;
                    isDefaultUiSetter = _isDefaultUiSetter;
                    uiFilesDirectorySetter = _uiFilesDirectorySetter;
                    miningAccountHolderSetter = _miningAccountHolderSetter;
                    miningAccountSetter = _miningAccountSetter;
                    isConfirmAtExitSetter = _isConfirmAtExitSetter;
                }

                private readonly Action<int> portWebSocketSetter;
                public int PortWebSocket
                {
                    set { portWebSocketSetter(value); }
                }

                private readonly Action<int> portWebServerSetter;
                public int PortWebServer
                {
                    set { portWebServerSetter(value); }
                }

                private readonly Action<bool> isWallpaperSetter;
                public bool IsWallpaper
                {
                    set { isWallpaperSetter(value); }
                }

                private readonly Action<string> wallpaperSetter;
                public string Wallpaper
                {
                    set { wallpaperSetter(value); }
                }

                private readonly Action<float> wallpaperOpacitySetter;
                public float WallpaperOpacity
                {
                    set { wallpaperOpacitySetter(value); }
                }

                private readonly Action<bool> isDefaultUiSetter;
                public bool IsDefaultUi
                {
                    set { isDefaultUiSetter(value); }
                }

                private readonly Action<string> uiFilesDirectorySetter;
                public string UiFilesDirectory
                {
                    set { uiFilesDirectorySetter(value); }
                }

                private readonly Action<string> miningAccountHolderSetter;
                public string MiningAccountHolder
                {
                    set { miningAccountHolderSetter(value); }
                }

                private readonly Action<string> miningAccountSetter;
                public string MiningAccount
                {
                    set { miningAccountSetter(value); }
                }

                private readonly Action<bool> isConfirmAtExitSetter;
                public bool IsConfirmAtExit
                {
                    set { isConfirmAtExitSetter(value); }
                }
            }

            public class MainWindowSettingsChangedEventArgs : EventArgs
            {
                private bool isPortWebSocketAltered;
                public bool IsPortWebSocketAltered
                {
                    get { return isPortWebSocketAltered; }
                }

                private bool isPortWebServerAltered;
                public bool IsPortWebServerAltered
                {
                    get { return isPortWebServerAltered; }
                }

                private bool isIsWallpaperAltered;
                public bool IsIsWallpaperAltered
                {
                    get { return isIsWallpaperAltered; }
                }

                private bool isWallpaperAltered;
                public bool IsWallpaperAltered
                {
                    get { return isWallpaperAltered; }
                }

                private bool isWallpaperOpacityAltered;
                public bool IsWallpaperOpacityAltered
                {
                    get { return isWallpaperOpacityAltered; }
                }

                private bool isIsDefaultUiAltered;
                public bool IsIsDefaultUiAltered
                {
                    get { return isIsDefaultUiAltered; }
                }

                private bool isUiFilesDirectoryAltered;
                public bool IsUiFilesDirectoryAltered
                {
                    get { return isUiFilesDirectoryAltered; }
                }

                private bool isIsConfirmAtExitAltered;
                public bool IsIsConfirmAtExitAltered
                {
                    get { return isIsConfirmAtExitAltered; }
                }

                public MainWindowSettingsChangedEventArgs(bool _isPortWebSocketAltered, bool _isPortWebServerAltered, bool _isIsWallpaperAltered, bool _isWallpaperAltered, bool _isWallpaperOpacityAltered, bool _isIsDefaultUiAltered, bool _isUiFilesDirectoryAltered, bool _isIsConfirmAtExitAltered)
                    : base()
                {
                    isPortWebSocketAltered = _isPortWebSocketAltered;
                    isPortWebServerAltered = _isPortWebServerAltered;
                    isIsWallpaperAltered = _isIsWallpaperAltered;
                    isWallpaperAltered = _isWallpaperAltered;
                    isWallpaperOpacityAltered = _isWallpaperOpacityAltered;
                    isIsDefaultUiAltered = _isIsDefaultUiAltered;
                    isUiFilesDirectoryAltered = _isUiFilesDirectoryAltered;
                    isIsConfirmAtExitAltered = _isIsConfirmAtExitAltered;
                }
            }

            private bool isPortWebSocketAltered;
            private int portWebSocket = 3333;
            public int PortWebSocket
            {
                get { return portWebSocket; }
            }
            public event EventHandler PortWebSocketChanged = delegate { };

            private bool isPortWebServerAltered;
            private int portWebServer = 3334;
            public int PortWebServer
            {
                get { return portWebServer; }
            }
            public event EventHandler PortWebServerChanged = delegate { };

            private bool isIsWallpaperAltered;
            private bool isWallpaper = true;
            public bool IsWallpaper
            {
                get { return isWallpaper; }
            }
            public event EventHandler IsWallpaperChanged = delegate { };

            private bool isWallpaperAltered;
            private string wallpaper = string.Empty;
            public string Wallpaper
            {
                get { return wallpaper; }
            }
            public event EventHandler WallpaperChanged = delegate { };

            private bool isWallpaperOpacityAltered;
            private float wallpaperOpacity = 0.5F;
            public float WallpaperOpacity
            {
                get { return wallpaperOpacity; }
            }
            public event EventHandler WallpaperOpacityChanged = delegate { };

            private bool isIsDefaultUiAltered;
            private bool isDefaultUi = true;
            public bool IsDefaultUi
            {
                get { return isDefaultUi; }
            }
            public event EventHandler IsDefaultUiChanged = delegate { };

            private bool isUiFilesDirectoryAltered;
            private string uiFilesDirectory = string.Empty;
            public string UiFilesDirectory
            {
                get { return uiFilesDirectory; }
            }
            public event EventHandler UiFilesDirectoryChanged = delegate { };

            private string miningAccountHolder = string.Empty;
            public string MiningAccountHolder
            {
                get { return miningAccountHolder; }
            }

            private string miningAccount = string.Empty;
            public string MiningAccount
            {
                get { return miningAccount; }
            }

            private bool isIsConfirmAtExitAltered;
            private bool isConfirmAtExit = true;
            public bool IsConfirmAtExit
            {
                get { return isConfirmAtExit; }
            }
            public event EventHandler IsConfirmAtExitChanged = delegate { };

            public event EventHandler WallpaperSettingsChanged = delegate { };
            public event EventHandler UiFilesSettingsChanged = delegate { };

            public event EventHandler<MainWindowSettingsChangedEventArgs> SettingsChanged = delegate { };

            public MainWindowSettings()
                : base("MainWindowSettings.xml")
            {
                Load();
            }

            protected override string XmlName
            {
                get { return "MainWindowSettings"; }
            }

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

            protected override Setter Setters
            {
                get
                {
                    return new Setter(
                        (_portWebSocket) =>
                        {
                            if (portWebSocket != _portWebSocket)
                            {
                                portWebSocket = _portWebSocket;
                                isPortWebSocketAltered = true;
                            }
                        },
                        (_portWebServer) =>
                        {
                            if (portWebServer != _portWebServer)
                            {
                                portWebServer = _portWebServer;
                                isPortWebServerAltered = true;
                            }
                        },
                        (_isWallpaper) =>
                        {
                            if (isWallpaper != _isWallpaper)
                            {
                                isWallpaper = _isWallpaper;
                                isIsWallpaperAltered = true;
                            }
                        },
                        (_wallpaper) =>
                        {
                            if (wallpaper != _wallpaper)
                            {
                                wallpaper = _wallpaper;
                                isWallpaperAltered = true;
                            }
                        },
                        (_wallpaperOpacity) =>
                        {
                            if (wallpaperOpacity != _wallpaperOpacity)
                            {
                                wallpaperOpacity = _wallpaperOpacity;
                                isWallpaperOpacityAltered = true;
                            }
                        },
                        (_isDefaultUi) =>
                        {
                            if (isDefaultUi != _isDefaultUi)
                            {
                                isDefaultUi = _isDefaultUi;
                                isIsDefaultUiAltered = true;
                            }
                        },
                        (_UiFilesDirectory) =>
                        {
                            if (uiFilesDirectory != _UiFilesDirectory)
                            {
                                uiFilesDirectory = _UiFilesDirectory;
                                isUiFilesDirectoryAltered = true;
                            }
                        },
                        (_miningAccountHolder) =>
                        {
                            if (miningAccountHolder != _miningAccountHolder)
                                miningAccountHolder = _miningAccountHolder;
                        },
                        (_miningAccount) =>
                        {
                            if (miningAccount != _miningAccount)
                                miningAccount = _miningAccount;
                        },
                        (_isConfirmAtExit) =>
                        {
                            if (isConfirmAtExit != _isConfirmAtExit)
                            {
                                isConfirmAtExit = _isConfirmAtExit;
                                isIsConfirmAtExitAltered = true;
                            }
                        });
                }
            }

            private readonly object setAndSaveLock = new object();
            public override void SetAndSave(Action<Setter> setAction)
            {
                lock (setAndSaveLock)
                {
                    setAction(Setters);
                    Save();

                    if (isPortWebSocketAltered)
                        PortWebSocketChanged(this, EventArgs.Empty);
                    if (isPortWebServerAltered)
                        PortWebServerChanged(this, EventArgs.Empty);
                    if (isIsWallpaperAltered)
                        IsWallpaperChanged(this, EventArgs.Empty);
                    if (isWallpaperAltered)
                        WallpaperChanged(this, EventArgs.Empty);
                    if (isWallpaperOpacityAltered)
                        WallpaperOpacityChanged(this, EventArgs.Empty);
                    if (isIsDefaultUiAltered)
                        IsDefaultUiChanged(this, EventArgs.Empty);
                    if (isUiFilesDirectoryAltered)
                        UiFilesDirectoryChanged(this, EventArgs.Empty);
                    if (isIsConfirmAtExitAltered)
                        IsConfirmAtExitChanged(this, EventArgs.Empty);

                    if (isIsWallpaperAltered || isWallpaperAltered || isWallpaperOpacityAltered)
                        WallpaperSettingsChanged(this, EventArgs.Empty);

                    if (isIsDefaultUiAltered || isUiFilesDirectoryAltered)
                        UiFilesSettingsChanged(this, EventArgs.Empty);

                    if (isPortWebSocketAltered || isPortWebServerAltered || isIsWallpaperAltered || isWallpaperAltered || isWallpaperOpacityAltered || isIsDefaultUiAltered || isUiFilesDirectoryAltered || isIsConfirmAtExitAltered)
                        SettingsChanged(this, new MainWindowSettingsChangedEventArgs(isPortWebSocketAltered, isPortWebServerAltered, isIsWallpaperAltered, isWallpaperAltered, isWallpaperOpacityAltered, isIsDefaultUiAltered, isUiFilesDirectoryAltered, isIsConfirmAtExitAltered));

                    isPortWebSocketAltered = false;
                    isPortWebServerAltered = false;
                    isIsWallpaperAltered = false;
                    isWallpaperAltered = false;
                    isWallpaperOpacityAltered = false;
                    isIsDefaultUiAltered = false;
                    isUiFilesDirectoryAltered = false;
                    isIsConfirmAtExitAltered = false;
                }
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
        private Func<byte[], bool> _UpVersion;

        private Action<string> _CreateUiFiles;

        public MainWindow(Core _core, Program.Logger _logger, Program.ProgramSettings _psettings, Program.ProgramStatus _pstatus, string _appname, string _version, string _appnameWithVersion, string _lisenceTextFilename, Assembly _assembly, string _basepath, Action<Exception, Program.ExceptionKind> __OnException, Func<byte[], bool> __UpVersion)
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
                Func<string, string> doNothing = (data) => data;

                foreach (var wsr in new[] {
                    new {path = pathHomeHtm, url = "/", processor = homeHtmProcessor}, 
                    new {path = "CREA2014.WebResources.jquery-2.0.3.min.js", url = "/jquery-2.0.3.min.js", processor = doNothing}, 
                    new {path = "CREA2014.WebResources.jquery-ui-1.10.4.custom.js", url = "/jquery-ui-1.10.4.custom.js", processor = doNothing}, 
                })
                    iWebServerData.Add(wsr.url, Encoding.UTF8.GetBytes(wsr.processor(_GetWebResource(wsr.path))));

                return iWebServerData;
            };
            Dictionary<string, byte[]> webServerData = _GetWebServerData();

            Action _StartWebServer = () =>
            {
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
                                hlres.OutputStream.Write(webServerData[hlc.Request.RawUrl], 0, webServerData[hlc.Request.RawUrl].Length);
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
                wssession.Send("acc_hols " + _GetAccountHolderHtml());

                foreach (var log in logger.Logs.Reverse())
                    wssession.Send("log " + _GetLogHtml(log));

                _SendBalance(wssession);
            };
            wss.NewMessageReceived += newMessageReceived;
            wss.Setup(mws.PortWebSocket);
            wss.Start();

            wb.Navigated += (sender2, e2) => ((mshtml.HTMLDocument)wb.Document).focus();
            wb.Navigate("http://localhost:" + mws.PortWebServer.ToString() + "/");

            mws.SettingsChanged += (sender2, e2) =>
            {
                if (e2.IsPortWebServerAltered)
                {
                    hl.Abort();

                    webServerData = _GetWebServerData();
                    _StartWebServer();

                    wb.Navigate("http://localhost:" + mws.PortWebServer.ToString() + "/");
                }
                else
                {
                    if (e2.IsIsWallpaperAltered || e2.IsWallpaperAltered || e2.IsWallpaperOpacityAltered)
                    {
                        webServerData.Remove(wallpaperFileName);
                        wallpaperFileName = _GetWallpaperFileName(DateTime.Now.Ticks.ToString());
                        webServerData.Add(wallpaperFileName, _GetWallpaperData());

                        foreach (var wssession in wss.GetAllSessions())
                            wssession.Send("wallpaper " + wallpaperFileName);
                    }
                    if (e2.IsPortWebSocketAltered)
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
                    if (e2.IsIsDefaultUiAltered || e2.IsUiFilesDirectoryAltered)
                    {
                        webResourceCache.Clear();

                        wb.Navigate("http://localhost:" + mws.PortWebServer.ToString() + "/");
                    }
                }
            };

            core.iAccountHolders.iAccountHoldersChanged += (sender2, e2) =>
            {
                foreach (var wssession in wss.GetAllSessions())
                    wssession.Send("acc_hols " + _GetAccountHolderHtml());
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
                mws.SetAndSave((setter) =>
                {
                    setter.PortWebSocket = int.Parse(sw.tbPortWebSocket.Text);
                    setter.PortWebServer = int.Parse(sw.tbPortWebServer.Text);
                    setter.IsWallpaper = (bool)sw.cbIsWallpaper.IsChecked;
                    setter.Wallpaper = sw.tbWallpaper.Text;
                    setter.WallpaperOpacity = float.Parse(sw.tbWallpaperOpacity.Text);
                    setter.IsDefaultUi = sw.rbDefault.IsChecked.Value;
                    setter.UiFilesDirectory = sw.tbUiFilesDirectory.Text;
                    setter.IsConfirmAtExit = (bool)sw.cbConfirmAtExit.IsChecked;
                });
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
                    mws.SetAndSave((setter) =>
                    {
                        setter.MiningAccountHolder = mw.rbAnonymous.IsChecked.Value ? string.Empty : (mw.cbAccountHolder.SelectedItem as IPseudonymousAccountHolder).iName;
                        setter.MiningAccount = (mw.cbAccount.SelectedItem as IAccount).iName;
                    });

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
            if (e.Key == Key.Up || e.Key == Key.Down || e.Key == Key.Left || e.Key == Key.Right)
                return;

            if (e.Key == Key.System)
                foreach (var wssession in wss.GetAllSessions())
                    wssession.Send("keydown " + ((int)e.SystemKey).ToString());

            e.Handled = true;
        }

        private void miTest_Click(object sender, RoutedEventArgs e)
        {
            if (_UpVersion(File.ReadAllBytes(assembly.Location)))
                Close();
            else
                MessageBox.Show("失敗");
        }
    }
}