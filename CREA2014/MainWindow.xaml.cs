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

        public MainWindow(CREACOINCore _core, Program.ProgramSettings _psettings, Program.ProgramStatus _pstatus, string _appname, string _version, string _appnameWithVersion, string _lisenceTextFilename, Assembly _assembly, string _basepath)
        {
            core = _core;
            psettings = _psettings;
            pstatus = _pstatus;
            appname = _appname;
            version = _version;
            appnameWithVersion = _appnameWithVersion;
            lisenceTextFilePath = Path.Combine(_basepath, _lisenceTextFilename);
            assembly = _assembly;

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

            string wallpaperFileName = null;
            Func<Dictionary<string, byte[]>> _GetWebServerData = () =>
            {
                Dictionary<string, byte[]> iWebServerData = new Dictionary<string, byte[]>();

                iWebServerData.Add(wallpaperFileName = _GetWallpaperFileName(null), _GetWallpaperData());

                Func<string, string> homeHtmProcessor = (data) =>
                {
                    data = data.Replace("%%title%%", appnameWithVersion).Replace("%%address%%", _GetWssAddress(mws.PortWebSocket));

                    string buttonBaseHtml;
                    using (Stream stream = assembly.GetManifestResourceStream("CREA2014.WebResources.button.htm"))
                    {
                        byte[] buttonData = new byte[stream.Length];
                        stream.Read(buttonData, 0, buttonData.Length);
                        buttonBaseHtml = Encoding.UTF8.GetString(buttonData);
                    }

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
                    using (Stream stream = assembly.GetManifestResourceStream(wsr.path))
                    {
                        byte[] data = new byte[stream.Length];
                        stream.Read(data, 0, data.Length);

                        iWebServerData.Add(wsr.url, Encoding.UTF8.GetBytes(wsr.processor(Encoding.UTF8.GetString(data))));
                    }

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
                ((Action)(() =>
                {
                    if (message == "new_account_holder")
                    {
                        NewAccountHolderWindow nahw = new NewAccountHolderWindow();
                        nahw.Owner = this;
                        if (nahw.ShowDialog() == true)
                        {
                        }
                    }
                    else
                        throw new NotSupportedException("wss_command");
                })).ExecuteInUIThread();
            };

            WebSocketServer oldWss;
            wss = new WebSocketServer();
            wss.NewMessageReceived += newMessageReceived;
            wss.Setup(mws.PortWebSocket);
            wss.Start();

            wb.Navigate("http://localhost:" + mws.PortWebServer.ToString() + "/");

            mws.PortWebSocketChanged += (sender2, e2) =>
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
            };
            mws.PortWebServerChanged += (sender2, e2) =>
            {
                hl.Abort();

                webServerData = _GetWebServerData();
                _StartWebServer();

                wb.Navigate("http://localhost:" + mws.PortWebServer.ToString() + "/");
            };
            mws.WallpaperSettingsChanged += (sender2, e2) =>
            {
                webServerData.Remove(wallpaperFileName);
                wallpaperFileName = _GetWallpaperFileName(DateTime.Now.Ticks.ToString());
                webServerData.Add(wallpaperFileName, _GetWallpaperData());

                foreach (var wssession in wss.GetAllSessions())
                    wssession.Send("wallpaper " + wallpaperFileName);
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
            if (e.Key == Key.System)
                foreach (var wssession in wss.GetAllSessions())
                    wssession.Send("keydown " + ((int)e.SystemKey).ToString());

            e.Handled = true;
        }
    }
}