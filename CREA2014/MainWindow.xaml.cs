using CREA2014.Windows;
using SuperWebSocket;
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows;

namespace CREA2014
{
    public partial class MainWindow : Window
    {
        public enum FileType { resource, file, data }

        public class MainformSettings
        {
            private int portWebSocket = 3333;
            public int PortWebSocket
            {
                get { return portWebSocket; }
                set { portWebSocket = value; }
            }

            private int portWebServer = 3334;
            public int PortWebServer
            {
                get { return portWebServer; }
                set { portWebServer = value; }
            }

            private string wallpaper = @"E:\#壁紙\good\16574.jpg";
            public string Wallpaper
            {
                get { return wallpaper; }
                set { wallpaper = value; }
            }
        }


        private HttpListener hl;
        private WebSocketServer wss;
        private MainformSettings ms;

        private CREACOINCore core;
        private Program.ProgramSettings psettings;
        private Program.ProgramStatus pstatus;
        private string appname;
        private string lisenceTextFilePath;
        private Assembly assembly;


        public MainWindow(CREACOINCore _core, Program.ProgramSettings _psettings, Program.ProgramStatus _pstatus, string _appname, string _lisenceTextFilename, Assembly _assembly, string _basepath)
        {
            core = _core;
            psettings = _psettings;
            pstatus = _pstatus;
            appname = _appname;
            lisenceTextFilePath = Path.Combine(_basepath, _lisenceTextFilename);
            assembly = _assembly;

            ms = new MainformSettings();

            InitializeComponent();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            Title = appname;

            if (pstatus.IsFirst)
            {
                if (!File.Exists(lisenceTextFilePath))
                    throw new FileNotFoundException("lisence_text_not_found");

                LisenceWindow lw = new LisenceWindow(File.ReadAllText(lisenceTextFilePath));
                if (lw.ShowDialog() == false)
                {
                    Close();
                    return;
                }
            }

            string addressWebSocket = "ws://localhost:" + ms.PortWebSocket.ToString() + "/";
            string prefix = "http://*:" + ms.PortWebServer.ToString() + "/";
            string url = "http://localhost:" + ms.PortWebServer.ToString() + "/";

            byte[] kabegamiData = null;
            if (File.Exists(ms.Wallpaper))
            {
                using (MemoryStream memoryStream = new MemoryStream())
                using (Bitmap bitmap = new Bitmap(ms.Wallpaper))
                using (Bitmap bitmap2 = new Bitmap(bitmap.Width, bitmap.Height))
                using (Graphics g = Graphics.FromImage(bitmap2))
                {
                    ColorMatrix cm = new ColorMatrix();
                    cm.Matrix00 = 1;
                    cm.Matrix11 = 1;
                    cm.Matrix22 = 1;
                    cm.Matrix33 = 0.7F;
                    cm.Matrix44 = 1;

                    ImageAttributes ia = new ImageAttributes();
                    ia.SetColorMatrix(cm);

                    g.DrawImage(bitmap, new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height), 0, 0, bitmap.Width, bitmap.Height, GraphicsUnit.Pixel, ia);

                    bitmap2.Save(memoryStream, ImageFormat.Png);

                    kabegamiData = memoryStream.ToArray();
                }
            }

            var iResource = new[] {
                    new {type = FileType.resource, path = "CREA2014.WebResources.home.htm", url = "/", replaces = new[]{Tuple.Create("<%title%>", appname)}}, 
                    new {type = FileType.resource, path = "CREA2014.WebResources.home.js", url = "/home.js", replaces = new[]{Tuple.Create("<%address%>", addressWebSocket)}}, 
                    new {type = FileType.resource, path = "CREA2014.WebResources.jquery-2.0.3.min.js", url = "/jquery-2.0.3.min.js", replaces = new Tuple<string,string>[]{}}, 
                };
            var iData = new[] {
                    new {url = "/back.png", data = kabegamiData}, 
                };

            Func<FileType, string, Tuple<string, string>[], byte[]> _GetData = (type, path, replaces) =>
            {
                byte[] data = null;

                if (type == FileType.resource)
                    using (Stream stream = assembly.GetManifestResourceStream(path))
                    {
                        data = new byte[stream.Length];
                        stream.Read(data, 0, data.Length);
                    }
                else if (File.Exists(path))
                    data = File.ReadAllBytes(path);
                else
                    data = new byte[] { };

                if (replaces == null || replaces.Length == 0)
                    return data;
                else
                {
                    string str = Encoding.UTF8.GetString(data);
                    foreach (var r in replaces)
                        str = str.Replace(r.Item1, r.Item2);
                    byte[] newdata = Encoding.UTF8.GetBytes(str);

                    return newdata;
                }
            };

            var i2 = (from ii in iResource
                      select
                          new { url = ii.url, data = _GetData(ii.type, ii.path, ii.replaces) }).ToArray().Concat(iData);


            hl = new HttpListener();
            hl.Prefixes.Add(prefix);
            try
            {
                hl.Start();
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
                        hlc = hl.GetContext();
                    }
                    catch (HttpListenerException)
                    {
                        hl.Close();
                        break;
                    }

                    HttpListenerRequest hlreq = hlc.Request;
                    HttpListenerResponse hlres = hlc.Response;

                    foreach (var ii2 in i2)
                        if (hlreq.RawUrl == ii2.url)
                        {
                            if (ii2.data != null)
                                hlres.OutputStream.Write(ii2.data, 0, ii2.data.Length);
                            break;
                        }

                    hlres.Close();
                }
            });
            thread.Start();


            //<未改良>.NET Framework 4.5 のWenSocketを使用する
            wss = new WebSocketServer();
            wss.Setup(ms.PortWebSocket);
            wss.NewSessionConnected += (session) =>
            {
                string script = "";

                session.Send("main <div id='main'><h1>テスト</h1></div>");
                //session.Send("script " + script);
            };
            wss.NewMessageReceived += (session, message) =>
            {
                //MessageBox.Show(message.ToString());
            };
            wss.SessionClosed += (session, e2) =>
            {
                //RemoveSession(session);
            };
            wss.Start();

            wb.Navigate(url);
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            if (hl != null)
                hl.Abort();
            if (wss != null)
                wss.Stop();
        }

        private void miClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}