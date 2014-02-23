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
        public class MainWindowSettings : CREACOINSETTINGSDATA
        {
            public class Setter
            {
                public Setter(Action<int> _portWebSocketSetter, Action<int> _portWebServerSetter, Action<bool> _isWallpaperSetter, Action<string> _wallpaperSetter, Action<float> _wallpaperOpacitySetter, Action<bool> _isConfirmAtExitSetter)
                {
                    portWebSocketSetter = _portWebSocketSetter;
                    portWebServerSetter = _portWebServerSetter;
                    isWallpaperSetter = _isWallpaperSetter;
                    wallpaperSetter = _wallpaperSetter;
                    wallpaperOpacitySetter = _wallpaperOpacitySetter;
                    isConfirmAtExitSetter = _isConfirmAtExitSetter;
                }

                private Action<int> portWebSocketSetter;
                public int PortWebSocket
                {
                    set { portWebSocketSetter(value); }
                }

                private Action<int> portWebServerSetter;
                public int PortWebServer
                {
                    set { portWebServerSetter(value); }
                }

                private Action<bool> isWallpaperSetter;
                public bool IsWallpaper
                {
                    set { isWallpaperSetter(value); }
                }

                private Action<string> wallpaperSetter;
                public string Wallpaper
                {
                    set { wallpaperSetter(value); }
                }

                private Action<float> wallpaperOpacitySetter;
                public float WallpaperOpacity
                {
                    set { wallpaperOpacitySetter(value); }
                }

                private Action<bool> isConfirmAtExitSetter;
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

                private bool isIsConfirmAtExitAltered;
                public bool IsIsConfirmAtExitAltered
                {
                    get { return isIsConfirmAtExitAltered; }
                }

                public MainWindowSettingsChangedEventArgs(bool _isPortWebSocketAltered, bool _isPortWebServerAltered, bool _isIsWallpaperAltered, bool _isWallpaperAltered, bool _isWallpaperOpacityAltered, bool _isIsConfirmAtExitAltered)
                    : base()
                {
                    isPortWebSocketAltered = _isPortWebSocketAltered;
                    isPortWebServerAltered = _isPortWebServerAltered;
                    isIsWallpaperAltered = _isIsWallpaperAltered;
                    isWallpaperAltered = _isWallpaperAltered;
                    isWallpaperOpacityAltered = _isWallpaperOpacityAltered;
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
            private string wallpaper = @"E:\#壁紙\good\16574.jpg";
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

            private bool isIsConfirmAtExitAltered;
            private bool isConfirmAtExit = true;
            public bool IsConfirmAtExit
            {
                get { return isConfirmAtExit; }
            }
            public event EventHandler IsConfirmAtExitChanged = delegate { };

            public event EventHandler WallpaperSettingsChanged = delegate { };

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

            protected override CREACOINSETTINGSDATA.MainDataInfomation[] MainDataInfo
            {
                get
                {
                    return new MainDataInfomation[]{
                        new MainDataInfomation(typeof(int), "PortWebSocket", () => portWebSocket, (o) => portWebSocket = (int)o), 
                        new MainDataInfomation(typeof(int), "PortWebServer", () => portWebServer, (o) => portWebServer = (int)o), 
                        new MainDataInfomation(typeof(bool), "IsWallpaper", () => isWallpaper, (o) => isWallpaper = (bool)o), 
                        new MainDataInfomation(typeof(string), "Wallpaper", () => wallpaper, (o) => wallpaper = (string)o), 
                        new MainDataInfomation(typeof(float), "WallpaperOpecity", () => wallpaperOpacity, (o) => wallpaperOpacity = (float)o), 
                        new MainDataInfomation(typeof(bool), "IsConfirmAtExit", () => isConfirmAtExit, (o) => isConfirmAtExit = (bool)o), 
                    };
                }
            }

            private readonly object setAndSaveLock = new object();
            public void SetAndSave(Action<Setter> setAction)
            {
                lock (setAndSaveLock)
                {
                    setAction(new Setter(
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
                        (_isConfirmAtExit) =>
                        {
                            if (isConfirmAtExit != _isConfirmAtExit)
                            {
                                isConfirmAtExit = _isConfirmAtExit;
                                isIsConfirmAtExitAltered = true;
                            }
                        }));
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
                    if (isIsConfirmAtExitAltered)
                        IsConfirmAtExitChanged(this, EventArgs.Empty);

                    if (isIsWallpaperAltered || isWallpaperAltered || isWallpaperOpacityAltered)
                        WallpaperSettingsChanged(this, EventArgs.Empty);

                    if (isPortWebSocketAltered || isPortWebServerAltered || isIsWallpaperAltered || isWallpaperAltered || isWallpaperOpacityAltered || isIsConfirmAtExitAltered)
                        SettingsChanged(this, new MainWindowSettingsChangedEventArgs(isPortWebSocketAltered, isPortWebServerAltered, isIsWallpaperAltered, isWallpaperAltered, isWallpaperOpacityAltered, isIsConfirmAtExitAltered));

                    isPortWebSocketAltered = false;
                    isPortWebServerAltered = false;
                    isIsWallpaperAltered = false;
                    isWallpaperAltered = false;
                    isWallpaperOpacityAltered = false;
                    isIsConfirmAtExitAltered = false;
                }
            }
        }

        private HttpListener hl;
        private WebSocketServer wss;
        private MainWindowSettings mws;

        private CREACOINCore core;
        private Program.ProgramSettings psettings;
        private Program.ProgramStatus pstatus;
        private string appname;
        private string version;
        private string appnameWithVersion;
        private string lisenceTextFilePath;
        private Assembly assembly;

        private Action<Exception, Program.ExceptionKind> OnException;

        public MainWindow(CREACOINCore _core, Program.ProgramSettings _psettings, Program.ProgramStatus _pstatus, string _appname, string _version, string _appnameWithVersion, string _lisenceTextFilename, Assembly _assembly, string _basepath, Action<Exception, Program.ExceptionKind> _OnException)
        {
            core = _core;
            psettings = _psettings;
            pstatus = _pstatus;
            appname = _appname;
            version = _version;
            appnameWithVersion = _appnameWithVersion;
            lisenceTextFilePath = Path.Combine(_basepath, _lisenceTextFilename);
            assembly = _assembly;
            OnException = _OnException;

            InitializeComponent();

            Title = _appnameWithVersion;
            miFile.Header = "ファイル".Multilanguage(19) + "(_F)";
            miClose.Header = "終了".Multilanguage(20) + "(_X)";
            miTool.Header = "ツール".Multilanguage(48) + "(_T)";
            miSettings.Header = "設定".Multilanguage(49) + "(_S)...";
            miHelp.Header = "ヘルプ".Multilanguage(21) + "(_H)";
            miAbout.Header = "CREAについて".Multilanguage(22) + "(_A)...";
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

            Func<string, string> _GetWebResource = (filename) =>
            {
                using (Stream stream = assembly.GetManifestResourceStream(filename))
                {
                    byte[] data = new byte[stream.Length];
                    stream.Read(data, 0, data.Length);
                    return Encoding.UTF8.GetString(data);
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

                    string buttonBaseHtml = _GetWebResource("CREA2014.WebResources.button.htm");

                    foreach (var button in new[] { 
                        new { identifier = "new_account_holder", name = "button1", text = "新しい口座名義".Multilanguage(60) + "(<u>A</u>)...", command = "new_account_holder", key = Key.A }, 
                        new { identifier = "new_account", name = "button2", text = "新しい口座".Multilanguage(61) + "(<u>B</u>)...", command = "new_account", key = Key.B }, 
                    })
                        data = data.Replace("%%" + button.identifier + "%%", buttonBaseHtml.Replace("button1", button.name).Replace("%%text%%", button.text).Replace("%%command%%", button.command).Replace("%%key%%", ((int)button.key).ToString()));

                    return data;
                };
                Func<string, string> doNothing = (data) => data;

                foreach (var wsr in new[] {
                    new {path = "CREA2014.WebResources.home.htm", url = "/", processor = homeHtmProcessor}, 
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
                    ((Action)(() =>
                    {
                        Action<Window> _NewAccountHolder = (window) =>
                        {
                            NewAccountHolderWindow nahw = new NewAccountHolderWindow();
                            nahw.Owner = window;
                            if (nahw.ShowDialog() == true)
                                core.AccountHolderDatabase.AddAccountHolder(new PseudonymousAccountHolder(nahw.tbAccountHolder.Text, nahw.rb256bit.IsChecked == true ? EcdsaKey.EcdsaKeyLength.Ecdsa256 : nahw.rb384bit.IsChecked == true ? EcdsaKey.EcdsaKeyLength.Ecdsa384 : EcdsaKey.EcdsaKeyLength.Ecdsa521));
                        };

                        if (message == "new_account_holder")
                            _NewAccountHolder(this);
                        else if (message == "new_account")
                        {
                            NewAccountWindow naw = new NewAccountWindow(_NewAccountHolder);
                            naw.Owner = this;

                            Action _Clear = () => naw.cbAccountHolder.Items.Clear();
                            Action _Add = () =>
                            {
                                foreach (var ah in core.AccountHolderDatabase.PseudonymousAccountHolders)
                                    naw.cbAccountHolder.Items.Add(ah);
                            };

                            EventHandler accountHolderAdded = (sender2, e2) => _Clear.AndThen(_Add).ExecuteInUIThread();

                            core.AccountHolderDatabase.AccountHolderAdded += accountHolderAdded;

                            _Add();

                            if (naw.ShowDialog() == true)
                            {
                                if (naw.rbAnonymous.IsChecked == true)
                                    core.AccountHolderDatabase.AnonymousAccountHolder.AddAccount(new Account(naw.tbName.Text, naw.tbDescription.Text, naw.rb256bit.IsChecked == true ? EcdsaKey.EcdsaKeyLength.Ecdsa256 : naw.rb384bit.IsChecked == true ? EcdsaKey.EcdsaKeyLength.Ecdsa384 : EcdsaKey.EcdsaKeyLength.Ecdsa521));
                                else
                                    foreach (var ah in core.AccountHolderDatabase.PseudonymousAccountHolders)
                                        if (ah == naw.cbAccountHolder.SelectedItem)
                                            ah.AddAccount(new Account(naw.tbName.Text, naw.tbDescription.Text, naw.rb256bit.IsChecked == true ? EcdsaKey.EcdsaKeyLength.Ecdsa256 : naw.rb384bit.IsChecked == true ? EcdsaKey.EcdsaKeyLength.Ecdsa384 : EcdsaKey.EcdsaKeyLength.Ecdsa521));
                            }

                            core.AccountHolderDatabase.AccountHolderAdded -= accountHolderAdded;
                        }
                        else
                            throw new NotSupportedException("wss_command");
                    })).ExecuteInUIThread();
                }
                catch (Exception ex)
                {
                    OnException(ex, Program.ExceptionKind.unhandled);
                }
            };

            Func<string> _GetAccountHolderHtml = () =>
            {
                string accHolsHtml = _GetWebResource("CREA2014.WebResources.acc_hols.htm");
                string accHolHtml = _GetWebResource("CREA2014.WebResources.acc_hol.htm");
                string accHtml = _GetWebResource("CREA2014.WebResources.acc.htm");

                string accs = string.Concat(from i in core.AccountHolderDatabase.AnonymousAccountHolder.Accounts
                                            select accHtml.Replace("%%name%%", i.Name).Replace("%%description%%", i.Description).Replace("%%address%%", i.Address.Base58));

                string psu_acc_hols = string.Concat(from i in core.AccountHolderDatabase.PseudonymousAccountHolders
                                                    select accHolHtml.Replace("%%title%%", i.Sign).Replace("%%accs%%",
                                      string.Concat(from j in i.Accounts
                                                    select accHtml.Replace("%%name%%", j.Name).Replace("%%description%%", j.Description).Replace("%%address%%", j.Address.Base58))));

                return accHolsHtml.Replace("%%accs%%", accs).Replace("%%psu_acc_hols%%", psu_acc_hols);
            };

            WebSocketServer oldWss;
            wss = new WebSocketServer();
            wss.NewSessionConnected += (session) => session.Send("acc_hols " + _GetAccountHolderHtml());
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
                }
            };

            Action _SendAccountHolderHtmlToAllSessions = () =>
            {
                foreach (var wssession in wss.GetAllSessions())
                    wssession.Send("acc_hols " + _GetAccountHolderHtml());
            };

            core.AccountHolderDatabase.AccountHoldersChanged += (sender2, e2) => _SendAccountHolderHtmlToAllSessions();
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
            SettingsWindow sw = new SettingsWindow(mws);
            sw.Owner = this;

            sw.tbPortWebSocket.Text = mws.PortWebSocket.ToString();
            sw.tbPortWebServer.Text = mws.PortWebServer.ToString();
            sw.cbIsWallpaper.IsChecked = mws.IsWallpaper;
            sw.tbWallpaper.Text = mws.Wallpaper;
            sw.tbWallpaperOpacity.Text = mws.WallpaperOpacity.ToString();
            sw.cbConfirmAtExit.IsChecked = mws.IsConfirmAtExit;

            if (sw.ShowDialog() == true)
                mws.SetAndSave((setter) =>
                {
                    setter.PortWebSocket = int.Parse(sw.tbPortWebSocket.Text);
                    setter.PortWebServer = int.Parse(sw.tbPortWebServer.Text);
                    setter.IsWallpaper = (bool)sw.cbIsWallpaper.IsChecked;
                    setter.Wallpaper = sw.tbWallpaper.Text;
                    setter.WallpaperOpacity = float.Parse(sw.tbWallpaperOpacity.Text);
                    setter.IsConfirmAtExit = (bool)sw.cbConfirmAtExit.IsChecked;
                });
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
    }
}