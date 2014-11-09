//がをがを～！
//2014/11/03 分割

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography;
using System.Text;

namespace CREA2014
{
    #region UPnP

    //ネットワークインターフェイス情報
    public class NetworkInterfaceInformation
    {
        //名前
        public string Name { get; set; }
        //説明
        public string Description { get; set; }
        //種類
        public NetworkInterfaceType NetworkInterfaceType { get; set; }
        //ユニキャストアドレス
        public List<IPAddress> UnicastAddresses { get; set; }
        //ゲートウェイアドレス
        public List<IPAddress> GatewayAddresses { get; set; }

        //コンストラクタ
        public NetworkInterfaceInformation()
        {
            UnicastAddresses = new List<IPAddress>();
            GatewayAddresses = new List<IPAddress>();
        }

        //コンストラクタ（名前／説明／種類／ユニキャストアドレス／ゲートウェイアドレス）
        public NetworkInterfaceInformation(string _name, string _description, NetworkInterfaceType _networkInferfaceType, List<IPAddress> _unicastAddresses, List<IPAddress> _gatewayAddress)
        {
            Name = _name;
            Description = _description;
            NetworkInterfaceType = _networkInferfaceType;
            UnicastAddresses = _unicastAddresses;
            GatewayAddresses = _gatewayAddress;
        }

        //ネットワークインターフェイス情報を取得する
        public static List<NetworkInterfaceInformation> GetNetworkInterfacesInformation()
        {
            List<NetworkInterfaceInformation> networkInterfacesInformation = new List<NetworkInterfaceInformation>();

            //ネットワークインターフェイスを取得する
            NetworkInterface[] networkInterfaces = NetworkInterface.GetAllNetworkInterfaces();
            foreach (var networkInterface in networkInterfaces)
            {
                //情報クラスを作成
                NetworkInterfaceInformation networkInterfaceInformation = new NetworkInterfaceInformation();

                //名前・説明・種類
                networkInterfaceInformation.Name = networkInterface.Name;
                networkInterfaceInformation.Description = networkInterface.Description;
                networkInterfaceInformation.NetworkInterfaceType = networkInterface.NetworkInterfaceType;

                //ネットワークインターフェイスの構成情報を取得する
                IPInterfaceProperties ipInterfaceProperties = networkInterface.GetIPProperties();

                //ユニキャストアドレス
                foreach (var unicastAddress in ipInterfaceProperties.UnicastAddresses)
                    networkInterfaceInformation.UnicastAddresses.Add(unicastAddress.Address);

                //ゲートウェイアドレス
                foreach (var gatewayAddress in ipInterfaceProperties.GatewayAddresses)
                    networkInterfaceInformation.GatewayAddresses.Add(gatewayAddress.Address);

                //情報クラスを追加
                networkInterfacesInformation.Add(networkInterfaceInformation);
            }

            return networkInterfacesInformation;
        }

        public override string ToString()
        {
            return Name;
        }

        public string ToDetailString()
        {
            return "名前：" + Name + Environment.NewLine +
                   "説明：" + Description + Environment.NewLine +
                   "種類：" + NetworkInterfaceType + Environment.NewLine +
                   "マシンIP：" + (from u in UnicastAddresses select u.ToString()).Aggregate((w, n) => w + ", " + n) + Environment.NewLine +
                   "ゲートウェイIP：" + (from g in GatewayAddresses select g.ToString()).Aggregate((w, n) => w + ", " + n);
        }
    }

    //UPnP操作クラスVer.2
    public class UPnP2
    {
        //受信タイムアウト値
        public int receiveTimeout = 15000;
        //送信タイムアウト値
        public int sendTimeout = 15000;

        //マシンのローカルIPアドレス
        private string machineIpAddress = null;
        //ゲートウェイ（ルータなど）のローカルIPアドレス
        private string gatewayIpAddress = null;

        //サービス文字列
        string services = null;
        //アクセスするポート
        int gatewayPort = -1;

        //外部ポート番号
        private int externalPort = 0;
        //プロトコル
        private string protocol = "";
        //内部ポート番号
        private int internalPort = 0;
        //内部クライアント（ローカル／LAN側IPアドレス）
        private string internalClient = "";
        //ポートマッピングの説明文（適当で良い）
        private string portMappingDescription = "";
        //ポートマッピングのインデックス
        private int portMappingIndex = 0;

        //コンストラクタ（マシンIP／ゲートウェイIP）
        public UPnP2(string _machineIpAddress, string _gatewayIpAddress)
        {
            machineIpAddress = internalClient = _machineIpAddress;
            gatewayIpAddress = _gatewayIpAddress;
        }

        //グローバル／WAN側IPアドレス取得
        public string GetExternalIPAddress()
        {
            return Command("GetExternalIPAddress");
        }

        //ポート開放
        public string AddPortMapping(int _externalPort, int _internalPort, string _protocol, string _portMappingDescription)
        {
            //外部／内部ポート番号、プロトコル、説明文の設定が必要
            externalPort = _externalPort;
            internalPort = _internalPort;
            protocol = _protocol;
            portMappingDescription = _portMappingDescription;

            return Command("AddPortMapping");
        }

        //ポート閉鎖
        public string DeletePortMapping(int _externalPort, string _protocol)
        {
            //外部ポート番号、プロトコルの設定が必要
            externalPort = _externalPort;
            protocol = _protocol;

            return Command("DeletePortMapping");
        }

        //ポートマッピング一覧取得
        public string GetGenericPortMappingEntry(int _newPortMappingIndex)
        {
            //ポートマッピングのインデックスの設定が必要
            portMappingIndex = _newPortMappingIndex;

            return Command("GetGenericPortMappingEntry");
        }

        //ルータの情報を取得
        public string GetRouterInformation()
        {
            if (services == null)
                GetServicesFromDevice();

            return services;
        }

        //サービス文字列とポート番号を取得
        private void GetServicesFromDevice()
        {
            //応答文字列
            string responseString = "";
            try
            {
                //ソケット作成
                Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                //タイムアウト時間の設定
                socket.ReceiveTimeout = receiveTimeout;
                socket.SendTimeout = sendTimeout;

                //239.255.255.250:1900にM-SEARCHを送信する
                EndPoint endPoint = new IPEndPoint(IPAddress.Parse("239.255.255.250"), 1900);
                string requestString = "M-SEARCH * HTTP/1.1\r\n" +
                                       "HOST: 239.255.255.250:1900\r\n" +
                                       "ST: upnp:rootdevice\r\n" +
                                       "MAN: \"ssdp:discover\"\r\n" +
                                       "MX: 3\r\n" +
                                       "\r\n";
                byte[] requestByte = Encoding.ASCII.GetBytes(requestString);
                socket.SendTo(requestByte, endPoint);

                //while (true)
                //{
                //応答を受信
                EndPoint endPoint2 = new IPEndPoint(IPAddress.Any, 0);
                byte[] responseByte = new byte[1024];
                socket.ReceiveFrom(responseByte, ref endPoint2);
                responseString += Encoding.ASCII.GetString(responseByte);
                //}
            }
            catch (Exception ex)
            {
                throw new Exception("通信中にエラーが発生しました。", ex);
            }

            if (responseString.Length == 0)
                throw new Exception("サービスを取得できませんでした。");

            //応答のlocation部分を取得
            string location = "";
            string[] parts = responseString.Split(new string[] { System.Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
                if (part.ToLower().StartsWith("location"))
                {
                    location = part.Substring(part.IndexOf(':') + 1);
                    if (location.Length == 0)
                        throw new Exception("サービスを取得できませんでした。");

                    Uri uri = new Uri(location);
                    gatewayPort = uri.Port;
                    break;
                }

            try
            {
                using (WebClient webClient = new WebClient())
                {
                    //サービス文字列を取得
                    services = webClient.DownloadString(location);
                }
            }
            catch (Exception ex)
            {
                throw new Exception("通信中にエラーが発生しました。", ex);
            }

            if (services.Length == 0)
                throw new Exception("サービスを取得できませんでした。");
        }

        //コマンドを送信する
        private string Command(string _command)
        {
            //最初にWANPPPConnectionで試行
            string returnString = Soap("urn:schemas-upnp-org:service:WANPPPConnection:1", _command);
            //文字列が返ってきたなら成功
            if (returnString != null)
                return returnString;
            //WANPPPConnectionで失敗したのでWANIPConnection
            returnString = Soap("urn:schemas-upnp-org:service:WANIPConnection:1", _command);
            return returnString;
        }

        //SOAPを送信する
        private string Soap(string _serviceType, string _command)
        {
            //まだサービス文字列を取得していない場合は取得する
            if (services == null)
                GetServicesFromDevice();

            //サービスタイプを探す
            int serviceIndex = services.IndexOf(_serviceType);
            if (serviceIndex == -1)
                return null;

            //コントロールURLを取り出す
            string controlUrl = services.Substring(serviceIndex);
            string tag1 = "<controlURL>";
            string tag2 = "</controlURL>";
            controlUrl = controlUrl.Substring(controlUrl.IndexOf(tag1) + tag1.Length);
            controlUrl = controlUrl.Substring(0, controlUrl.IndexOf(tag2));

            //HTTPリクエストを作成
            string bodyString = CreateSoap(_serviceType, _command);
            byte[] bodyByte = UTF8Encoding.ASCII.GetBytes(bodyString);
            string headString = "POST " + controlUrl + " HTTP/1.1\r\n" +
                    "HOST: " + gatewayIpAddress + ":" + gatewayPort + "\r\n" +
                    "CONTENT-LENGTH: " + bodyByte.Length + "\r\n" +
                    "CONTENT-TYPE: text/xml; charset=\"utf-8\"" + "\r\n" +
                    "SOAPACTION: \"" + _serviceType + "#" + _command + "\"\r\n" +
                    "\r\n";
            byte[] headByte = Encoding.ASCII.GetBytes(headString);
            byte[] requestByte = new byte[headByte.Length + bodyByte.Length];
            Array.Copy(headByte, 0, requestByte, 0, headByte.Length);
            Array.Copy(bodyByte, 0, requestByte, headByte.Length, bodyByte.Length);

            //応答文字列
            string responseString = "";
            try
            {
                //ゲートウェイに接続
                Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                EndPoint endPoint = new IPEndPoint(IPAddress.Parse(gatewayIpAddress), gatewayPort);
                socket.Connect(endPoint);

                //タイムアウト時間の設定
                socket.ReceiveTimeout = receiveTimeout;
                socket.SendTimeout = sendTimeout;

                //HTTPリクエストを送信
                socket.Send(requestByte, requestByte.Length, SocketFlags.None);

                //HTTPレスポンスを受信
                byte[] responseByte = new byte[1024];
                using (MemoryStream memoryStream = new MemoryStream())
                {
                    while (true)
                    {
                        int responseSize = socket.Receive(responseByte, responseByte.Length, SocketFlags.None);
                        if (responseSize == 0)
                            break;
                        memoryStream.Write(responseByte, 0, responseSize);
                    }
                    responseString = Encoding.ASCII.GetString(memoryStream.ToArray(), 0, (int)memoryStream.Length);
                }

                //ソケットを閉じる
                socket.Shutdown(SocketShutdown.Both);
                socket.Close();
            }
            catch (Exception ex)
            {
                throw new Exception("通信中にエラーが発生しました。", ex);
            }

            return CreateReturnString(_command, responseString);
        }

        //SOAPメッセージを作成する
        private string CreateSoap(string _serviceType, string _command)
        {
            string bodyString = null;

            if (_command == "GetExternalIPAddress")
            {
                bodyString =
                    "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n" +
                    "<s:Envelope " + "xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\" " + "s:encodingStyle=\"http://schemas.xmlsoap.org/soap/encoding/\">" +
                    " <s:Body>" +
                    "  <u:GetExternalIPAddress xmlns:u=\"" + _serviceType + "\">" + "</u:GetExternalIPAddress>" +
                    " </s:Body>" +
                    "</s:Envelope>";
            }
            else if (_command == "AddPortMapping")
            {
                bodyString =
                    "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n" +
                    "<s:Envelope " + "xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\" " + "s:encodingStyle=\"http://schemas.xmlsoap.org/soap/encoding/\">" +
                    " <s:Body>" +
                    "  <u:AddPortMapping xmlns:u=\"" + _serviceType + "\">" +
                    "   <NewRemoteHost></NewRemoteHost>" +
                    "   <NewExternalPort>" + externalPort + "</NewExternalPort>" +
                    "   <NewProtocol>" + protocol + "</NewProtocol>" +
                    "   <NewInternalPort>" + internalPort + "</NewInternalPort>" +
                    "   <NewInternalClient>" + internalClient + "</NewInternalClient>" +
                    "   <NewEnabled>1</NewEnabled>" +
                    "   <NewPortMappingDescription>" + portMappingDescription + "</NewPortMappingDescription>" +
                    "   <NewLeaseDuration>0</NewLeaseDuration>" +
                    "  </u:AddPortMapping>" +
                    " </s:Body>" +
                    "</s:Envelope>";
            }
            else if (_command == "DeletePortMapping")
            {
                bodyString =
                    "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n" +
                    "<s:Envelope " + "xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\" " + "s:encodingStyle=\"http://schemas.xmlsoap.org/soap/encoding/\">" +
                    " <s:Body>" +
                    "  <u:DeletePortMapping xmlns:u=\"" + _serviceType + "\">" +
                    "   <NewRemoteHost></NewRemoteHost>" +
                    "   <NewExternalPort>" + externalPort + "</NewExternalPort>" +
                    "   <NewProtocol>" + protocol + "</NewProtocol>" +
                    "  </u:DeletePortMapping>" +
                    " </s:Body>" +
                    "</s:Envelope>";
            }
            else if (_command == "GetGenericPortMappingEntry")
            {
                bodyString =
                    "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n" +
                    "<s:Envelope " + "xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\" " + "s:encodingStyle=\"http://schemas.xmlsoap.org/soap/encoding/\">" +
                    " <s:Body>" +
                    "  <u:GetGenericPortMappingEntry xmlns:u=\"" + _serviceType + "\">" +
                    "   <NewPortMappingIndex>" + portMappingIndex + "</NewPortMappingIndex>" +
                    "  </u:GetGenericPortMappingEntry>" +
                    " </s:Body>" +
                    "</s:Envelope>";
            }
            else
                throw new Exception("コマンド名が不正です。");

            return bodyString;
        }

        //返却文字列を作成する
        private string CreateReturnString(string _command, string _response)
        {
            string response = _response;

            if (_command == "GetExternalIPAddress")
            {
                string tag3 = "<NewExternalIPAddress>";
                string tag4 = "</NewExternalIPAddress>";
                response = response.Substring(response.IndexOf(tag3) + tag3.Length);
                response = response.Substring(0, response.IndexOf(tag4));
            }
            else if (_command == "AddPortMapping")
                if (response.StartsWith("HTTP/1.1 200 OK"))
                    response = "成功しました。";
                else
                    response = "失敗しました。" + Environment.NewLine + _response;
            else if (_command == "DeletePortMapping")
            {
                if (response.StartsWith("HTTP/1.1 200 OK"))
                    response = "成功しました。";
                else
                    response = "失敗しました。" + Environment.NewLine + _response;
            }
            else if (_command == "GetGenericPortMappingEntry")
            {
                if (response.StartsWith("HTTP/1.1 200 OK"))
                {
                    string value = "";

                    string tag3 = "<NewRemoteHost>";
                    string tag4 = "</NewRemoteHost>";
                    value += response.Substring(response.IndexOf(tag3) + tag3.Length);
                    value = value.Substring(0, value.IndexOf(tag4));

                    value += " ";

                    tag3 = "<NewExternalPort>";
                    tag4 = "</NewExternalPort>";
                    value += response.Substring(response.IndexOf(tag3) + tag3.Length);
                    value = value.Substring(0, value.IndexOf(tag4));

                    value += " ";

                    tag3 = "<NewProtocol>";
                    tag4 = "</NewProtocol>";
                    value += response.Substring(response.IndexOf(tag3) + tag3.Length);
                    value = value.Substring(0, value.IndexOf(tag4));

                    value += " ";

                    tag3 = "<NewInternalPort>";
                    tag4 = "</NewInternalPort>";
                    value += response.Substring(response.IndexOf(tag3) + tag3.Length);
                    value = value.Substring(0, value.IndexOf(tag4));

                    value += " ";

                    tag3 = "<NewInternalClient>";
                    tag4 = "</NewInternalClient>";
                    value += response.Substring(response.IndexOf(tag3) + tag3.Length);
                    value = value.Substring(0, value.IndexOf(tag4));

                    value += " ";

                    tag3 = "<NewEnabled>";
                    tag4 = "</NewEnabled>";
                    value += response.Substring(response.IndexOf(tag3) + tag3.Length);
                    value = value.Substring(0, value.IndexOf(tag4));

                    value += " ";

                    tag3 = "<NewPortMappingDescription>";
                    tag4 = "</NewPortMappingDescription>";
                    value += response.Substring(response.IndexOf(tag3) + tag3.Length);
                    value = value.Substring(0, value.IndexOf(tag4));

                    value += " ";

                    tag3 = "<NewLeaseDuration>";
                    tag4 = "</NewLeaseDuration>";
                    value += response.Substring(response.IndexOf(tag3) + tag3.Length);
                    value = value.Substring(0, value.IndexOf(tag4));

                    response = value;
                }
                else
                    response = null;
            }

            return response;
        }
    }

    public class UPnPWanService
    {
        /// <summary>
        /// ネットワーク内の UPnP Wan サービスを探す。ちょっと時間がかかる。
        /// 見つからなかったときはnullが返るので気をつける。
        /// </summary>
        /// <returns></returns>
        public static UPnPWanService FindUPnPWanService()
        {
            Guid UPnPDeviceFinderClass = new Guid("E2085F28-FEB7-404A-B8E7-E659BDEAAA02");
            IUPnPDeviceFinder finder = Activator.CreateInstance(Type.GetTypeFromCLSID(UPnPDeviceFinderClass)) as IUPnPDeviceFinder;

            string[] deviceTypes = { "urn:schemas-upnp-org:service:WANPPPConnection:1", "urn:schemas-upnp-org:service:WANIPConnection:1", };
            string[] serviceNames = { "urn:upnp-org:serviceId:WANPPPConn1", "urn:upnp-org:serviceId:WANIPConn1", };
            foreach (string deviceType in deviceTypes)
                foreach (IUPnPDevice device in finder.FindByType(deviceType, 0))
                    foreach (string serviceName in serviceNames)
                        foreach (IUPnPService service in device.Services)
                            return new UPnPWanService() { service = service };
            return null;
        }

        IUPnPService service;
        private UPnPWanService() { }

        static void ThrowForHR(string action, HRESULT_UPnP hr, object result)
        {
            if (hr != HRESULT_UPnP.S_OK)
                throw new COMException("Action " + action + " returns " + hr + " " + result, (int)hr);
        }

        /// <summary>
        /// ポートマッピングを追加する。
        /// </summary>
        /// <param name="remoteHost">通信相手。通信先を限定する場合に指定。</param>
        /// <param name="externalPort">グローバルポート番号。</param>
        /// <param name="protocol">プロトコル名。"TCP" or "UDP"を指定。</param>
        /// <param name="internalPort">内部クライアントのポート番号。</param>
        /// <param name="internalClient">内部クライアントのIPアドレス。</param>
        /// <param name="description">説明。任意。</param>
        /// <param name="leaseDuration">リース期間(秒単位)。0を指定すると無期限</param>
        public void AddPortMapping(string remoteHost, ushort externalPort, string protocol, ushort internalPort, IPAddress internalClient, bool enabled, string description, uint leaseDuration)
        {
            if (string.IsNullOrEmpty(remoteHost)) remoteHost = "";
            if (string.IsNullOrEmpty(description)) description = "";
            if (protocol != "TCP" && protocol != "UDP") throw new ArgumentException("protocol must be \"TCP\" or \"UDP\"", protocol);

            object outArgs = null;
            object result = null;
            HRESULT_UPnP hresult = service.InvokeAction("AddPortMapping",
                new object[] { remoteHost, externalPort, protocol, internalPort, internalClient.ToString(), enabled, description, leaseDuration },
                ref outArgs, out result);
            ThrowForHR("AddPortMapping", hresult, result);
        }

        /// <summary>
        /// ポートマッピングを追加する。
        /// </summary>
        /// <param name="portMapping">追加するポートマッピング</param>
        public void AddPortMapping(UPnPPortMapping portMapping)
        {
            AddPortMapping(portMapping.RemoteHost, portMapping.ExternalPort, portMapping.Protocol, portMapping.InternalPort,
                portMapping.InternalClient, portMapping.Enabled, portMapping.PortMappingDescription, portMapping.LeaseDuration);
        }

        /// <summary>
        /// ポートマッピングを削除する。
        /// </summary>
        /// <param name="remoteHost">追加時に指定した通信相手。</param>
        /// <param name="externalPort">追加時に指定した外部ポート番号。</param>
        /// <param name="protocol">追加時に指定されたプロトコル。</param>
        public void DeletePortMapping(string remoteHost, ushort externalPort, string protocol)
        {
            if (string.IsNullOrEmpty(remoteHost)) remoteHost = "";
            if (protocol != "TCP" && protocol != "UDP") throw new ArgumentException("protocol must be \"TCP\" or \"UDP\"", protocol);

            object outArgs = null;
            object result = null;
            HRESULT_UPnP hresult = service.InvokeAction("DeletePortMapping", new object[] { remoteHost, externalPort, protocol }, ref outArgs, out result);
            ThrowForHR("DeletePortMapping", hresult, result);
        }

        /// <summary>
        /// ポートマッピングを削除する。
        /// </summary>
        /// <param name="portMapping">削除するポートマッピング。RemoteHostとExternalPortとProtocolだけが使われる。</param>
        public void DeletePortMapping(UPnPPortMapping portMapping)
        {
            DeletePortMapping(portMapping.RemoteHost, portMapping.ExternalPort, portMapping.Protocol);
        }

        /// <summary>
        /// 現在設定されているポートマッピング情報を得る。
        /// </summary>
        /// <returns>ポートマッピング情報</returns>
        public List<UPnPPortMapping> GetGenericPortMappingEntries()
        {
            object outArgs = null;
            object result = null;
            List<UPnPPortMapping> list = new List<UPnPPortMapping>(16);
            for (int i = 0; ; i++)
            {
                HRESULT_UPnP hresult = service.InvokeAction("GetGenericPortMappingEntry", new object[] { i }, ref outArgs, out result);
                if (hresult != HRESULT_UPnP.S_OK) break;

                object[] array = (object[])outArgs;
                list.Add(new UPnPPortMapping
                {
                    RemoteHost = (string)array[0],
                    ExternalPort = (ushort)array[1],
                    Protocol = (string)array[2],
                    InternalPort = (ushort)array[3],
                    InternalClient = IPAddress.Parse((string)array[4]),
                    Enabled = (bool)array[5],
                    PortMappingDescription = (string)array[6],
                    LeaseDuration = (uint)array[7],
                });
            }
            return list;
        }

        /// <summary>
        /// グローバルIPアドレスを得る。
        /// </summary>
        /// <returns>IPアドレス</returns>
        public System.Net.IPAddress GetExternalIPAddress()
        {
            object outArgs = null;
            object result = null;
            HRESULT_UPnP hresult = service.InvokeAction("GetExternalIPAddress", new object[] { }, ref outArgs, out result);
            ThrowForHR("GetExternalIPAddress", hresult, result);
            return IPAddress.Parse((string)((object[])outArgs)[0]);
        }

        /// <summary>
        /// 自分のIPアドレス(v4)を得る。
        /// </summary>
        /// <returns>IPアドレス</returns>
        public System.Net.IPAddress GetLocalIPAddress()
        {
            IPAddress[] address = Array.FindAll(Dns.GetHostAddresses(Dns.GetHostName()),
                a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
            if (address.Length > 0) return address[0];
            return null;
        }

        #region COM Interop

        [ComImport]
        [Guid("ADDA3D55-6F72-4319-BFF9-18600A539B10")]
        [InterfaceType(ComInterfaceType.InterfaceIsDual)]
        [SuppressUnmanagedCodeSecurity]
        interface IUPnPDeviceFinder
        {
            [DispId(1610744809)]
            IUPnPDevices FindByType(string bstrTypeURI, int dwFlags);
            [DispId(1610744812)]
            int CreateAsyncFind(string bstrTypeURI, int dwFlags, object punkDeviceFinderCallback);
            [DispId(1610744813)]
            void StartAsyncFind(int lFindData);
            [DispId(1610744814)]
            void CancelAsyncFind(int lFindData);
            [DispId(1610744811)]
            IUPnPDevice FindByUDN(string bstrUDN);
        }

        [ComImport]
        [Guid("3D44D0D1-98C9-4889-ACD1-F9D674BF2221")]
        [InterfaceType(ComInterfaceType.InterfaceIsDual)]
        [SuppressUnmanagedCodeSecurity]
        interface IUPnPDevice
        {
            [DispId(1610747809)]
            bool IsRootDevice { get; }
            [DispId(1610747810)]
            IUPnPDevice RootDevice { get; }
            [DispId(1610747811)]
            IUPnPDevice ParentDevice { get; }
            [DispId(1610747812)]
            bool HasChildren { get; }
            [DispId(1610747813)]
            IUPnPDevices Children { get; }
            [DispId(1610747814)]
            string UniqueDeviceName { get; }
            [DispId(1610747815)]
            string FriendlyName { get; }
            [DispId(1610747816)]
            string Type { get; }
            [DispId(1610747817)]
            string PresentationURL { get; }
            [DispId(1610747818)]
            string ManufacturerName { get; }
            [DispId(1610747819)]
            string ManufacturerURL { get; }
            [DispId(1610747820)]
            string ModelName { get; }
            [DispId(1610747821)]
            string ModelNumber { get; }
            [DispId(1610747822)]
            string Description { get; }
            [DispId(1610747823)]
            string ModelURL { get; }
            [DispId(1610747824)]
            string UPC { get; }
            [DispId(1610747825)]
            string SerialNumber { get; }
            [DispId(1610747827)]
            string IconURL(string bstrEncodingFormat, int lSizeX, int lSizeY, int lBitDepth);
            [DispId(1610747828)]
            IUPnPServices Services { get; }
        }

        [ComImport]
        [Guid("FDBC0C73-BDA3-4C66-AC4F-F2D96FDAD68C")]
        [InterfaceType(ComInterfaceType.InterfaceIsDual)]
        [SuppressUnmanagedCodeSecurity]
        interface IUPnPDevices : IEnumerable
        {
            [DispId(1610747309)]
            int Count { get; }

            [DispId(-4)]
            [TypeLibFunc(TypeLibFuncFlags.FHidden | TypeLibFuncFlags.FRestricted)]
            new IEnumerator GetEnumerator();

            [DispId(0)]
            IUPnPDevice this[string bstrUDN] { get; }
        }

        [ComImport]
        [Guid("3F8C8E9E-9A7A-4DC8-BC41-FF31FA374956")]
        [InterfaceType(ComInterfaceType.InterfaceIsDual)]
        [SuppressUnmanagedCodeSecurity]
        interface IUPnPServices : IEnumerable
        {
            [DispId(1610745809)]
            int Count { get; }

            [DispId(-4)]
            [TypeLibFunc(TypeLibFuncFlags.FHidden | TypeLibFuncFlags.FRestricted)]
            new IEnumerator GetEnumerator();

            [DispId(0)]
            IUPnPService this[string bstrServiceId] { get; }
        }

        [ComImport]
        [Guid("A295019C-DC65-47DD-90DC-7FE918A1AB44")]
        [InterfaceType(ComInterfaceType.InterfaceIsDual)]
        [SuppressUnmanagedCodeSecurity]
        interface IUPnPService
        {
            [PreserveSig]
            [DispId(1610746309)]
            HRESULT_UPnP QueryStateVariable(string bstrVariableName, out /*VARIANT*/ object pvarValue);
            [PreserveSig]
            [DispId(1610746310)]
            HRESULT_UPnP InvokeAction(string bstrActionName, /*VARIANT*/ object vInActionArgs, ref /*VARIANT*/ object pvOutActionArgs, out /*VARIANT*/ object pvarRetVal);

            [DispId(1610746311)]
            string ServiceTypeIdentifier { get; }
            [DispId(1610746312)]
            void AddCallback(/*IUnknown*/ object pUnkCallback);
            [DispId(1610746313)]
            string Id { get; }
            [DispId(1610746314)]
            int LastTransportStatus { get; }
        }

        enum HRESULT_UPnP : int
        {
            S_OK = 0,
            UPNP_FACILITY_ITF = -2147221504,
            UPNP_E_ROOT_ELEMENT_EXPECTED = UPNP_FACILITY_ITF + 0x0200,
            UPNP_E_DEVICE_ELEMENT_EXPECTED,
            UPNP_E_SERVICE_ELEMENT_EXPECTED,
            UPNP_E_SERVICE_NODE_INCOMPLETE,
            UPNP_E_DEVICE_NODE_INCOMPLETE,
            UPNP_E_ICON_ELEMENT_EXPECTED,
            UPNP_E_ICON_NODE_INCOMPLETE,
            UPNP_E_INVALID_ACTION,
            UPNP_E_INVALID_ARGUMENTS,
            UPNP_E_OUT_OF_SYNC,
            UPNP_E_ACTION_REQUEST_FAILED,
            UPNP_E_TRANSPORT_ERROR,
            UPNP_E_VARIABLE_VALUE_UNKNOWN,
            UPNP_E_INVALID_VARIABLE,
            UPNP_E_DEVICE_ERROR,
            UPNP_E_PROTOCOL_ERROR,
            UPNP_E_ERROR_PROCESSING_RESPONS,
            UPNP_E_DEVICE_TIMEOUT,
            UPNP_E_INVALID_DOCUMENT = UPNP_FACILITY_ITF + 0x0500,
            UPNP_E_EVENT_SUBSCRIPTION_FAILED,
            UPNP_E_ACTION_SPECIFIC_BASE = UPNP_FACILITY_ITF + 0x0300,
            UPNP_E_ACTION_SPECIFIC_MAX = UPNP_E_ACTION_SPECIFIC_BASE + 0x0258,
        }

        #endregion
    }

    public class UPnPPortMapping
    {
        public string RemoteHost { get; set; }
        public ushort ExternalPort { get; set; }
        public string Protocol { get; set; }
        public ushort InternalPort { get; set; }
        public IPAddress InternalClient { get; set; }
        public bool Enabled { get; set; }
        public string PortMappingDescription { get; set; }
        public uint LeaseDuration { get; set; }
    }

    #endregion

    #region base58

    public static class Base58Encoding
    {
        private static readonly char[] Alphabet = "123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz".ToCharArray();
        private static readonly int Base58Length = Alphabet.Length;
        private static readonly int[] INDEXES = new int[128];
        private const int Base256Length = 256;

        static Base58Encoding()
        {
            for (int i = 0; i < INDEXES.Length; i++)
                INDEXES[i] = -1;
            for (int i = 0; i < Alphabet.Length; i++)
                INDEXES[Alphabet[i]] = i;
        }

        public static string Encode(IReadOnlyList<byte> input)
        {
            var buffer = input.MutableCopy();

            char[] temp = new char[buffer.Length * 2];
            int j = temp.Length;

            int zeroCount = LeadingZerosCount(buffer);
            int startAt = zeroCount;
            while (startAt < buffer.Length)
            {
                byte mod = divmod58(buffer, startAt);
                if (buffer[startAt] == 0)
                    ++startAt;
                temp[--j] = Alphabet[mod];
            }

            while (j < temp.Length && temp[j] == Alphabet[0])
                ++j;

            while (--zeroCount >= 0)
                temp[--j] = Alphabet[0];

            return new string(temp, j, temp.Length - j);
        }

        public static byte[] Decode(string input)
        {
            if (input.Length == 0)
                return new byte[0];

            byte[] input58 = new byte[input.Length];

            for (int i = 0; i < input.Length; ++i)
            {
                char c = input[i];

                int digit58 = -1;
                if (c >= 0 && c < 128)
                    digit58 = INDEXES[c];

                if (digit58 < 0)
                    throw new AddressFormatException("Illegal character " + c + " at " + i);

                input58[i] = (byte)digit58;
            }

            int zeroCount = 0;
            while (zeroCount < input58.Length && input58[zeroCount] == 0)
                ++zeroCount;

            byte[] temp = new byte[input.Length];
            int j = temp.Length;

            int startAt = zeroCount;
            while (startAt < input58.Length)
            {
                byte mod = divmod256(input58, startAt);
                if (input58[startAt] == 0)
                    ++startAt;

                temp[--j] = mod;
            }

            while (j < temp.Length && temp[j] == 0)
                ++j;

            var result = new byte[temp.Length - (j - zeroCount)];
            Array.Copy(temp, j - zeroCount, result, 0, result.Length);
            return result;
        }

        private static int LeadingZerosCount(IReadOnlyList<byte> buffer)
        {
            int leadingZeros = 0;
            for (leadingZeros = 0; leadingZeros < buffer.Count && buffer[leadingZeros] == 0; leadingZeros++) ;
            return leadingZeros;
        }

        private static byte divmod58(byte[] number, int startAt)
        {
            int remainder = 0;
            for (int i = startAt; i < number.Length; i++)
            {
                int digit256 = (int)number[i] & 0xFF;
                int temp = remainder * Base256Length + digit256;

                number[i] = (byte)(temp / Base58Length);

                remainder = temp % Base58Length;
            }

            return (byte)remainder;
        }

        private static byte divmod256(byte[] number58, int startAt)
        {
            int remainder = 0;
            for (int i = startAt; i < number58.Length; i++)
            {
                int digit58 = (int)number58[i] & 0xFF;
                int temp = remainder * Base58Length + digit58;

                number58[i] = (byte)(temp / Base256Length);

                remainder = temp % Base256Length;
            }

            return (byte)remainder;
        }

        private static byte[] copyOfRange(byte[] buffer, int start, int end)
        {
            var result = new byte[end - start];
            Array.Copy(buffer, start, result, 0, end - start);
            return result;
        }
    }

    public static class Utils
    {
        internal static byte[] DoubleDigest(byte[] input)
        {
            return DoubleDigest(input, 0, input.Length);
        }

        internal static byte[] DoubleDigest(byte[] input, int offset, int count)
        {
            using (var hashAlgorithm = HashAlgorithm.Create("SHA-256"))
            {
                byte[] hash = hashAlgorithm.ComputeHash(input, offset, count);
                return hashAlgorithm.ComputeHash(hash);
            }
        }

        internal static bool ArraysEqual(IReadOnlyList<byte> array1, IReadOnlyList<byte> array2)
        {
            if (array1.Count != array2.Count)
                return false;

            for (int i = 0; i < array1.Count; i++)
                if (array1[i] != array2[i])
                    return false;

            return true;
        }

        internal static byte[] MutableCopy(this IReadOnlyList<byte> readOnlyBuffer)
        {
            var buffer = new byte[readOnlyBuffer.Count];
            for (int i = 0; i < readOnlyBuffer.Count; i++)
                buffer[i] = readOnlyBuffer[i];

            return buffer;
        }
    }

    public class AddressFormatException : Exception
    {
        public AddressFormatException(string message, Exception innerException = null) : base(message, innerException) { }
    }

    #endregion

    #region secp256k1

    public static class Secp256k1
    {
        public static readonly BigInteger P = "FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFEFFFFFC2F".HexToBigInteger();
        public static readonly ECPoint G = ECPoint.DecodePoint("0479BE667EF9DCBBAC55A06295CE870B07029BFCDB2DCE28D959F2815B16F81798483ADA7726A3C4655DA4FBFC0E1108A8FD17B448A68554199C47D08FFB10D4B8".HexToBytes());
        public static readonly BigInteger N = "FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFEBAAEDCE6AF48A03BBFD25E8CD0364141".HexToBigInteger();
    }

    public class ECDsaSigner
    {
        private RNGCryptoServiceProvider rngCsp = new RNGCryptoServiceProvider();

        public ECPoint RecoverFromSignature(byte[] hash, BigInteger r, BigInteger s, int recId)
        {
            var x = r;
            if (recId > 1 && recId < 4)
            {
                x += Secp256k1.N;
                x = x % Secp256k1.P;
            }

            if (x >= Secp256k1.P)
            {
                return null;
            }

            byte[] xBytes = x.ToByteArrayUnsigned(true);
            byte[] compressedPoint = new Byte[33];
            compressedPoint[0] = (byte)(0x02 + (recId % 2));
            Buffer.BlockCopy(xBytes, 0, compressedPoint, 33 - xBytes.Length, xBytes.Length);

            ECPoint publicKey = ECPoint.DecodePoint(compressedPoint);

            if (!publicKey.Multiply(Secp256k1.N).IsInfinity) return null;

            var z = -hash.ToBigIntegerUnsigned(true) % Secp256k1.N;
            if (z < 0)
            {
                z += Secp256k1.N;
            }

            var rr = r.ModInverse(Secp256k1.N);
            var u1 = (z * rr) % Secp256k1.N;
            var u2 = (s * rr) % Secp256k1.N;

            var Q = Secp256k1.G.Multiply(u1).Add(publicKey.Multiply(u2));

            return Q;
        }

        public BigInteger[] GenerateSignature(BigInteger privateKey, byte[] hash)
        {
            return GenerateSignature(privateKey, hash, null);
        }

        public BigInteger[] GenerateSignature(BigInteger privateKey, byte[] hash, BigInteger? k)
        {
            for (int i = 0; i < 100; i++)
            {
                if (k == null)
                {
                    byte[] kBytes = new byte[33];
                    rngCsp.GetBytes(kBytes);
                    kBytes[32] = 0;

                    k = new BigInteger(kBytes);
                }
                var z = hash.ToBigIntegerUnsigned(true);

                if (k.Value.IsZero || k >= Secp256k1.N) continue;

                var r = Secp256k1.G.Multiply(k.Value).X % Secp256k1.N;

                if (r.IsZero) continue;

                var ss = (z + r * privateKey);
                var s = (ss * (k.Value.ModInverse(Secp256k1.N))) % Secp256k1.N;

                if (s.IsZero) continue;

                return new BigInteger[] { r, s };
            }

            throw new Exception("Unable to generate signature");
        }

        public bool VerifySignature(ECPoint publicKey, byte[] hash, BigInteger r, BigInteger s)
        {
            if (r >= Secp256k1.N || r.IsZero || s >= Secp256k1.N || s.IsZero)
            {
                return false;
            }

            var z = hash.ToBigIntegerUnsigned(true);
            var w = s.ModInverse(Secp256k1.N);
            var u1 = (z * w) % Secp256k1.N;
            var u2 = (r * w) % Secp256k1.N;
            var pt = Secp256k1.G.Multiply(u1).Add(publicKey.Multiply(u2));
            var pmod = pt.X % Secp256k1.N;

            return pmod == r;
        }
    }

    public class ECPoint : ICloneable
    {
        private readonly bool _isInfinity;
        private readonly BigInteger _x;
        private BigInteger _y;

        public ECPoint(BigInteger x, BigInteger y)
            : this(x, y, false)
        {
        }

        public ECPoint(BigInteger x, BigInteger y, bool isInfinity)
        {
            _x = x;
            _y = y;
            _isInfinity = isInfinity;
        }

        private ECPoint()
        {
            _isInfinity = true;
        }

        public BigInteger X
        {
            get { return _x; }
        }

        public BigInteger Y
        {
            get { return _y; }
        }

        public static ECPoint Infinity
        {
            get { return new ECPoint(); }
        }

        public bool IsInfinity
        {
            get { return _isInfinity; }
        }

        public object Clone()
        {
            return new ECPoint(_x, _y, _isInfinity);
        }

        //TODO: Rename to Encode (point is implied)
        public byte[] EncodePoint(bool compressed)
        {
            if (IsInfinity)
                return new byte[1];

            byte[] x = X.ToByteArrayUnsigned(true);
            byte[] encoded;
            if (!compressed)
            {
                byte[] y = Y.ToByteArrayUnsigned(true);
                encoded = new byte[65];
                encoded[0] = 0x04;
                Buffer.BlockCopy(y, 0, encoded, 33 + (32 - y.Length), y.Length);
            }
            else
            {
                encoded = new byte[33];
                encoded[0] = (byte)(Y.TestBit(0) ? 0x03 : 0x02);
            }

            Buffer.BlockCopy(x, 0, encoded, 1 + (32 - x.Length), x.Length);
            return encoded;
        }

        //TODO: Rename to Decode (point is implied)
        public static ECPoint DecodePoint(byte[] encoded)
        {
            if (encoded == null || ((encoded.Length != 33 && encoded[0] != 0x02 && encoded[0] != 0x03) && (encoded.Length != 65 && encoded[0] != 0x04)))
                throw new FormatException("Invalid encoded point");

            var unsigned = new byte[32];
            Buffer.BlockCopy(encoded, 1, unsigned, 0, 32);
            BigInteger x = unsigned.ToBigIntegerUnsigned(true);
            BigInteger y;
            byte prefix = encoded[0];

            if (prefix == 0x04) //uncompressed PubKey
            {
                Buffer.BlockCopy(encoded, 33, unsigned, 0, 32);
                y = unsigned.ToBigIntegerUnsigned(true);
            }
            else // compressed PubKey
            {
                // solve y
                y = ((x * x * x + 7) % Secp256k1.P).ShanksSqrt(Secp256k1.P);

                if (y.IsEven ^ prefix == 0x02) // negate y for prefix (0x02 indicates y is even, 0x03 indicates y is odd)
                    y = -y + Secp256k1.P;      // TODO:  DRY replace this and body of Negate() with call to static method
            }
            return new ECPoint(x, y);
        }

        public ECPoint Negate()
        {
            var r = (ECPoint)Clone();
            r._y = -r._y + Secp256k1.P;
            return r;
        }

        public ECPoint Subtract(ECPoint b)
        {
            return Add(b.Negate());
        }

        public ECPoint Add(ECPoint b)
        {
            BigInteger m;
            //[Resharper unused local variable] BigInteger r = 0;

            if (IsInfinity)
                return b;
            if (b.IsInfinity)
                return this;

            if (X - b.X == 0)
            {
                if (Y - b.Y == 0)
                    m = 3 * X * X * (2 * Y).ModInverse(Secp256k1.P);
                else
                    return Infinity;
            }
            else
            {
                var mx = (X - b.X);
                if (mx < 0)
                    mx += Secp256k1.P;
                m = (Y - b.Y) * mx.ModInverse(Secp256k1.P);
            }

            m = m % Secp256k1.P;

            var v = Y - m * X;
            var x3 = (m * m - X - b.X);
            x3 = x3 % Secp256k1.P;
            if (x3 < 0)
                x3 += Secp256k1.P;
            var y3 = -(m * x3 + v);
            y3 = y3 % Secp256k1.P;
            if (y3 < 0)
                y3 += Secp256k1.P;

            return new ECPoint(x3, y3);
        }

        public ECPoint Twice()
        {
            return Add(this);
        }

        public ECPoint Multiply(BigInteger b)
        {
            if (b.Sign == -1)
                throw new FormatException("The multiplicator cannot be negative");

            b = b % Secp256k1.N;

            ECPoint result = Infinity;
            ECPoint temp = null;

            //[Resharper local variable only assigned not used] int bit = 0;
            do
            {
                temp = temp == null ? this : temp.Twice();

                if (!b.IsEven)
                    result = result.Add(temp);
                //bit++;
            }
            while ((b >>= 1) != 0);

            return result;
        }
    }

    public static class Hex
    {
        private static readonly string[] _byteToHex = new[]
        {
            "00", "01", "02", "03", "04", "05", "06", "07", 
            "08", "09", "0a", "0b", "0c", "0d", "0e", "0f",
            "10", "11", "12", "13", "14", "15", "16", "17", 
            "18", "19", "1a", "1b", "1c", "1d", "1e", "1f",
            "20", "21", "22", "23", "24", "25", "26", "27", 
            "28", "29", "2a", "2b", "2c", "2d", "2e", "2f",
            "30", "31", "32", "33", "34", "35", "36", "37", 
            "38", "39", "3a", "3b", "3c", "3d", "3e", "3f",
            "40", "41", "42", "43", "44", "45", "46", "47", 
            "48", "49", "4a", "4b", "4c", "4d", "4e", "4f",
            "50", "51", "52", "53", "54", "55", "56", "57", 
            "58", "59", "5a", "5b", "5c", "5d", "5e", "5f",
            "60", "61", "62", "63", "64", "65", "66", "67", 
            "68", "69", "6a", "6b", "6c", "6d", "6e", "6f",
            "70", "71", "72", "73", "74", "75", "76", "77", 
            "78", "79", "7a", "7b", "7c", "7d", "7e", "7f",
            "80", "81", "82", "83", "84", "85", "86", "87", 
            "88", "89", "8a", "8b", "8c", "8d", "8e", "8f",
            "90", "91", "92", "93", "94", "95", "96", "97", 
            "98", "99", "9a", "9b", "9c", "9d", "9e", "9f",
            "a0", "a1", "a2", "a3", "a4", "a5", "a6", "a7", 
            "a8", "a9", "aa", "ab", "ac", "ad", "ae", "af",
            "b0", "b1", "b2", "b3", "b4", "b5", "b6", "b7", 
            "b8", "b9", "ba", "bb", "bc", "bd", "be", "bf",
            "c0", "c1", "c2", "c3", "c4", "c5", "c6", "c7", 
            "c8", "c9", "ca", "cb", "cc", "cd", "ce", "cf",
            "d0", "d1", "d2", "d3", "d4", "d5", "d6", "d7", 
            "d8", "d9", "da", "db", "dc", "dd", "de", "df",
            "e0", "e1", "e2", "e3", "e4", "e5", "e6", "e7", 
            "e8", "e9", "ea", "eb", "ec", "ed", "ee", "ef",
            "f0", "f1", "f2", "f3", "f4", "f5", "f6", "f7", 
            "f8", "f9", "fa", "fb", "fc", "fd", "fe", "ff"
        };

        private static readonly Dictionary<string, byte> _hexToByte = new Dictionary<string, byte>();

        static Hex()
        {
            for (byte b = 0; b < 255; b++)
            {
                _hexToByte[_byteToHex[b]] = b;
            }

            _hexToByte["ff"] = 255;
        }

        public static string BigIntegerToHex(BigInteger value)
        {
            return BytesToHex(value.ToByteArrayUnsigned(true));
        }

        public static BigInteger HexToBigInteger(string hex)
        {
            byte[] bytes = HexToBytes(hex);
            Array.Reverse(bytes);
            Array.Resize(ref bytes, bytes.Length + 1);
            bytes[bytes.Length - 1] = 0x00;
            return new BigInteger(bytes);
        }

        public static string BytesToHex(byte[] bytes)
        {
            var hex = new StringBuilder(bytes.Length * 2);
            foreach (byte b in bytes)
            {
                hex.Append(_byteToHex[b]);
            }

            return hex.ToString();
        }

        public static byte[] HexToBytes(string hex)
        {
            if (hex.Length % 2 != 0)
            {
                hex = "0" + hex;
            }

            hex = hex.ToLower();

            var bytes = new byte[hex.Length / 2];
            for (int i = 0; i < hex.Length / 2; i++)
            {
                bytes[i] = _hexToByte[hex.Substring(i * 2, 2)];
            }

            return bytes;
        }

        public static string AsciiToHex(string ascii)
        {
            char[] chars = ascii.ToCharArray();
            var hex = new StringBuilder(ascii.Length);

            foreach (var currentChar in chars)
            {
                hex.Append(String.Format("{0:X}", Convert.ToInt32(currentChar)));
            }

            return hex.ToString();
        }
    }

    public static class BigIntExtensions
    {
        public static BigInteger ModInverse(this BigInteger n, BigInteger p)
        {
            BigInteger x = 1;
            BigInteger y = 0;
            BigInteger a = p;
            BigInteger b = n;

            while (b != 0)
            {
                BigInteger t = b;
                BigInteger q = BigInteger.Divide(a, t);
                b = a - q * t;
                a = t;
                t = x;
                x = y - q * t;
                y = t;
            }

            if (y < 0)
                return y + p;
            //else
            return y;
        }

        public static bool TestBit(this BigInteger i, int n)
        {
            //[resharper:unused local variable] int bitLength = i.BitLength();
            return !(i >> n).IsEven;
        }

        public static int BitLength(this BigInteger i)
        {
            int bitLength = 0;
            do
            {
                bitLength++;
            }
            while ((i >>= 1) != 0);
            return bitLength;
        }

        public static byte[] ToByteArrayUnsigned(this BigInteger i, bool bigEndian)
        {
            byte[] bytes = i.ToByteArray();
            if (bytes[bytes.Length - 1] == 0x00)
                Array.Resize(ref bytes, bytes.Length - 1);
            if (bigEndian)
                Array.Reverse(bytes, 0, bytes.Length);

            return bytes;
        }

        public static BigInteger Order(this BigInteger b, BigInteger p)
        {
            BigInteger m = 1;
            BigInteger e = 0;

            while (BigInteger.ModPow(b, m, p) != 1)
            {
                m *= 2;
                e++;
            }

            return e;
        }

        private static BigInteger FindS(BigInteger p)
        {
            BigInteger s = p - 1;
            BigInteger e = 0;

            while (s % 2 == 0)
            {
                s /= 2;
                e += 1;
            }

            return s;
        }

        private static BigInteger FindE(BigInteger p)
        {
            BigInteger s = p - 1;
            BigInteger e = 0;

            while (s % 2 == 0)
            {
                s /= 2;
                e += 1;
            }

            return e;
        }

        private static BigInteger TwoExp(BigInteger e)
        {
            BigInteger a = 1;

            while (e > 0)
            {
                a *= 2;
                e--;
            }

            return a;
        }

        public static string ToHex(this BigInteger b)
        {
            return Hex.BigIntegerToHex(b);
        }

        public static string ToHex(this byte[] bytes)
        {
            return Hex.BytesToHex(bytes);
        }

        public static BigInteger HexToBigInteger(this string s)
        {
            return Hex.HexToBigInteger(s);
        }

        public static byte[] HexToBytes(this string s)
        {
            return Hex.HexToBytes(s);
        }

        public static BigInteger ToBigInteger(this byte[] bytes, bool bigEndian)
        {
            byte[] clone = new byte[bytes.Length];
            Buffer.BlockCopy(bytes, 0, clone, 0, bytes.Length);
            Array.Reverse(clone);

            return new BigInteger(bytes);
        }

        public static BigInteger ToBigIntegerUnsigned(this byte[] bytes, bool bigEndian)
        {
            byte[] clone;
            if (bigEndian)
            {
                if (bytes[0] != 0x00)
                {
                    clone = new byte[bytes.Length + 1];
                    Buffer.BlockCopy(bytes, 0, clone, 1, bytes.Length);
                    Array.Reverse(clone);
                    return new BigInteger(clone);
                }
                clone = new byte[bytes.Length];
                Buffer.BlockCopy(bytes, 0, clone, 0, bytes.Length);
                Array.Reverse(clone);
                return new BigInteger(clone);
            }

            if (bytes[bytes.Length - 1] == 0x00)
                return new BigInteger(bytes);

            clone = new byte[bytes.Length + 1];
            Buffer.BlockCopy(bytes, 0, clone, 0, bytes.Length);
            return new BigInteger(clone);
        }

        public static BigInteger ShanksSqrt(this BigInteger a, BigInteger p)
        {
            if (BigInteger.ModPow(a, (p - 1) / 2, p) == (p - 1))
                return -1;

            if (p % 4 == 3)
                return BigInteger.ModPow(a, (p + 1) / 4, p);

            //Initialize 
            BigInteger s = FindS(p);
            BigInteger e = FindE(p);
            BigInteger n = 2;

            while (BigInteger.ModPow(n, (p - 1) / 2, p) == 1)
                n++;

            BigInteger x = BigInteger.ModPow(a, (s + 1) / 2, p);
            BigInteger b = BigInteger.ModPow(a, s, p);
            BigInteger g = BigInteger.ModPow(n, s, p);
            BigInteger r = e;
            BigInteger m = b.Order(p);

#if(DEBUG)
            Debug.WriteLine("{0}, {1}, {2}, {3}, {4}", m, x, b, g, r);
#endif
            while (m > 0)
            {
                x = (x * BigInteger.ModPow(g, TwoExp(r - m - 1), p)) % p;
                b = (b * BigInteger.ModPow(g, TwoExp(r - m), p)) % p;
                g = BigInteger.ModPow(g, TwoExp(r - m), p);
                r = m;
                m = b.Order(p);

#if(DEBUG)
                Debug.WriteLine("{0}, {1}, {2}, {3}, {4}", m, x, b, g, r);
#endif
            }

            return x;
        }
    }

    #endregion
}