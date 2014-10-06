using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace CREA2014
{
    public class SimulationNetwork
    {
        public SimulationNetwork() : this(10000, 9000) { }

        public SimulationNetwork(ushort _maxPort, ushort _minListenerPortNumber)
        {
            if (_minListenerPortNumber > _maxPort)
                throw new ArgumentException("min_listener_port_number");

            maxPort = _maxPort;
            minListenerPortNumber = _minListenerPortNumber;

            snd = new SimulationSocket[maxPort];
        }

        private readonly SimulationSocket[] snd;

        private readonly object pointerLock = new object();
        private ushort pointer = 0;

        public ushort maxPort { get; private set; }
        public ushort minListenerPortNumber { get; private set; }

        public event EventHandler<Tuple<ushort, ushort>> Communicated = delegate { };

        public ConnectReturn Connect(ushort portNumber, SimulationSocket connectSS)
        {
            if (portNumber >= maxPort)
                throw new ArgumentException("too_large_port_number");
            if (portNumber < minListenerPortNumber)
                throw new ArgumentException("too_small_port_number");
            if (snd[portNumber] == null)
                throw new InvalidOperationException("not_yet_used");
            if (!snd[portNumber].isListened)
                throw new InvalidOperationException("not_listened");

            Func<ushort> _FindUsablePort = () =>
            {
                ushort? point = null;
                lock (pointerLock)
                {
                    for (ushort i = pointer; i < minListenerPortNumber; i++)
                        if (snd[i] == null)
                        {
                            point = i;
                            break;
                        }
                    if (point == null)
                        for (ushort i = 0; i < pointer; i++)
                            if (snd[i] == null)
                            {
                                point = i;
                                break;
                            }

                    if (point == null)
                        throw new InvalidOperationException("no_usable_port");

                    pointer = (ushort)(point.Value + 1);
                }

                return point.Value;
            };

            ushort connectPortNumber = _FindUsablePort();
            ushort connectedPortNumber = _FindUsablePort();

            SimulationSocket connectedSS = new SimulationSocket(this, connectedPortNumber, connectPortNumber);

            snd[connectPortNumber] = connectSS;
            snd[connectedPortNumber] = connectedSS;

            snd[portNumber].OnConnected(connectedSS);

            return new ConnectReturn(connectPortNumber, connectedPortNumber);
        }

        public void Listen(ushort portNumber, SimulationSocket listenSS)
        {
            if (portNumber >= maxPort)
                throw new ArgumentException("too_large_port_number");
            if (portNumber < minListenerPortNumber)
                throw new ArgumentException("too_small_port_number");
            if (snd[portNumber] != null)
                throw new InvalidOperationException("already_used");

            snd[portNumber] = listenSS;
        }

        public void Close(SimulationSocket closeSS)
        {
            if (closeSS.isConnected)
            {
                if (snd[closeSS.localPortNumber] == null)
                    throw new InvalidOperationException("not_used");
                if (snd[closeSS.localPortNumber] != closeSS)
                    throw new InvalidOperationException("not_equal");

                snd[closeSS.localPortNumber] = null;
            }
            else if (closeSS.isListened)
            {
                if (snd[closeSS.localPortNumber] == null)
                    throw new InvalidOperationException("not_used");
                if (snd[closeSS.localPortNumber] != closeSS)
                    throw new InvalidOperationException("not_equal");

                snd[closeSS.localPortNumber] = null;
            }
            else
                throw new InvalidOperationException("not_connected_and_listened");
        }

        public void Write(SimulationSocket writeSS, byte[] data)
        {
            if (writeSS.isListened)
                throw new InvalidOperationException("listened");
            if (!writeSS.isConnected)
                throw new InvalidOperationException("not_connected");
            if (snd[writeSS.localPortNumber] == null || snd[writeSS.remotePortNumber] == null)
                throw new InvalidOperationException("not_used");
            if (snd[writeSS.localPortNumber] != writeSS)
                throw new InvalidOperationException("not_equal");

            snd[writeSS.remotePortNumber].OnReceived(data);

            Communicated(this, new Tuple<ushort, ushort>(writeSS.localPortNumber, writeSS.remotePortNumber));
        }
    }

    public class ConnectReturn
    {
        public ConnectReturn(ushort _localPortNumber, ushort _remotePortNumber)
        {
            localPortNumber = _localPortNumber;
            remotePortNumber = _remotePortNumber;
        }

        public readonly ushort localPortNumber;
        public readonly ushort remotePortNumber;
    }

    public class SimulationSocket : ISocket
    {
        public SimulationSocket(SimulationNetwork _sn) : this(_sn, 0, 0) { }

        public SimulationSocket(SimulationNetwork _sn, ushort _localPortNumber, ushort _remotePortNumber)
        {
            sn = _sn;
            localPortNumber = _localPortNumber;
            remotePortNumber = _remotePortNumber;

            if (!(localPortNumber == 0 && remotePortNumber == 0))
                isConnected = true;

            ReceiveTimeout = 3000;
            SendTimeout = 3000;
        }

        private readonly SimulationNetwork sn;
        private readonly object acceptQueueLock = new object();
        private readonly Queue<SimulationSocket> acceptQueue = new Queue<SimulationSocket>();
        private readonly AutoResetEvent acceptAre = new AutoResetEvent(false);
        private readonly object receiveQueueLock = new object();
        private readonly Queue<byte[]> receiveQueue = new Queue<byte[]>();
        private readonly AutoResetEvent receiveAre = new AutoResetEvent(false);

        public ushort localPortNumber { get; private set; }
        public ushort remotePortNumber { get; private set; }

        public bool isConnected { get; private set; }
        public bool isListened { get; private set; }

        public AddressFamily AddressFamily
        {
            get { return AddressFamily.InterNetwork; }
        }

        public EndPoint LocalEndPoint
        {
            get
            {
                if (!isConnected && !isListened)
                    throw new InvalidOperationException("not_connected_or_listened");

                return new IPEndPoint(IPAddress.Loopback, localPortNumber);
            }
        }

        public EndPoint RemoteEndPoint
        {
            get
            {
                if (!isConnected)
                    throw new InvalidOperationException("not_connected");

                return new IPEndPoint(IPAddress.Loopback, remotePortNumber);
            }
        }

        public bool Connected
        {
            get { return isConnected; }
        }

        public int ReceiveTimeout { get; set; }
        public int SendTimeout { get; set; }

        public int ReceiveBufferSize
        {
            get { throw new NotImplementedException(); }
            set { throw new NotImplementedException(); }
        }

        public int SendBufferSize
        {
            get { throw new NotImplementedException(); }
            set { throw new NotImplementedException(); }
        }

        public void Connect(IPAddress ipAddress, ushort portNumber)
        {
            if (ipAddress != IPAddress.Loopback)
                throw new ArgumentException("not_loopback");
            if (isListened)
                throw new InvalidOperationException("listened");
            if (isConnected)
                throw new InvalidOperationException("already_connected");

            ConnectReturn cr = sn.Connect(portNumber, this);

            localPortNumber = cr.localPortNumber;
            remotePortNumber = cr.remotePortNumber;

            isConnected = true;
        }

        public void OnConnected(SimulationSocket ss)
        {
            if (!isListened)
                throw new InvalidOperationException("not_listened");

            lock (acceptQueueLock)
                acceptQueue.Enqueue(ss);

            acceptAre.Set();
        }

        public void Bind(IPEndPoint localEP)
        {
            localPortNumber = (ushort)localEP.Port;
        }

        public void Listen(int backlog)
        {
            if (isConnected)
                throw new InvalidOperationException("connected");
            if (isListened)
                throw new InvalidOperationException("already_listened");

            sn.Listen(localPortNumber, this);

            isListened = true;
        }

        public ISocket Accept()
        {
            if (!isListened)
                throw new InvalidOperationException("not_listened");

            lock (acceptQueueLock)
                if (acceptQueue.Count != 0)
                    return acceptQueue.Dequeue();

            acceptAre.Reset();
            acceptAre.WaitOne();

            lock (acceptQueueLock)
                if (acceptQueue.Count == 0)
                    throw new InvalidOperationException("accept_queue_empty");
                else
                    return acceptQueue.Dequeue();
        }

        public void Shutdown(SocketShutdown how) { }

        public void Close()
        {
            if (!isConnected && !isListened)
                throw new InvalidOperationException("cant_close");

            sn.Close(this);

            isConnected = isListened = false;
        }

        public void Dispose() { }

        public void OnReceived(byte[] data)
        {
            if (!isConnected)
                throw new InvalidOperationException("not_connected");

            lock (receiveQueueLock)
                receiveQueue.Enqueue(data);

            receiveAre.Set();
        }

        public void Write(byte[] data)
        {
            if (!isConnected)
                throw new InvalidOperationException("not_connected");

            sn.Write(this, data);
        }

        public byte[] Read()
        {
            if (!isConnected)
                throw new InvalidOperationException("not_connected");

            lock (receiveQueueLock)
                if (receiveQueue.Count != 0)
                    return receiveQueue.Dequeue();

            receiveAre.Reset();
            receiveAre.WaitOne(30000);

            lock (receiveQueueLock)
                if (receiveQueue.Count == 0)
                    throw new InvalidOperationException("receive_queue_empty");
                else
                    return receiveQueue.Dequeue();
        }
    }

    //試験用
    public class SimulationWindow : Window
    {
        public SimulationWindow()
        {
            Loaded += (sender, e) =>
            {
                bool isStarted = false;

                DiffuseSimulation ds = new DiffuseSimulation();
                Dictionary<ushort, int> nodes = new Dictionary<ushort, int>();
                for (int i = 0; i < ds.numberOfNodes; i++)
                    nodes.Add(ds.nodeInfos[i].portNumber, i);

                Background = Brushes.Black;

                DockPanel dp = new DockPanel();
                Content = dp;

                Canvas canvas = new Canvas();
                dp.Children.Add(canvas);

                TextBlock tbMessage = new TextBlock();
                tbMessage.Foreground = Brushes.White;
                canvas.Children.Add(tbMessage);

                Ellipse ellipse = new Ellipse();
                ellipse.Stroke = Brushes.White;
                canvas.Children.Add(ellipse);

                double nodeEllipseDiameter = 5.0;
                double nodeEllipseRadius = nodeEllipseDiameter / 2.0;

                Ellipse[] nodeEllipses = new Ellipse[ds.numberOfNodes];
                Border[] nodeBorders = new Border[ds.numberOfNodes];
                double[] nodeRadians = new double[ds.numberOfNodes];
                for (int i = 0; i < nodeEllipses.Length; i++)
                {
                    nodeEllipses[i] = new Ellipse();
                    nodeEllipses[i].Fill = Brushes.Red;
                    nodeEllipses[i].Width = nodeEllipseDiameter;
                    nodeEllipses[i].Height = nodeEllipseDiameter;
                    canvas.Children.Add(nodeEllipses[i]);

                    nodeBorders[i] = new Border();
                    nodeBorders[i].Visibility = Visibility.Collapsed;
                    nodeBorders[i].Background = Brushes.Black;
                    nodeBorders[i].BorderBrush = Brushes.White;
                    nodeBorders[i].BorderThickness = new Thickness(3.0);
                    canvas.Children.Add(nodeBorders[i]);

                    StackPanel nodeSp = new StackPanel();
                    nodeSp.Orientation = Orientation.Vertical;
                    nodeBorders[i].Child = nodeSp;

                    TextBlock tbPortNumber = new TextBlock();
                    tbPortNumber.Text = "ポート番号：" + ds.nodeInfos[i].portNumber.ToString();
                    tbPortNumber.Foreground = Brushes.White;
                    nodeSp.Children.Add(tbPortNumber);

                    TextBlock tbId = new TextBlock();
                    tbId.Text = "識別子：" + ds.nodeInfos[i].Id.ToString();
                    tbId.Foreground = Brushes.White;
                    nodeSp.Children.Add(tbId);

                    int index = i;

                    nodeEllipses[i].MouseEnter += (sender2, e2) => nodeBorders[index].Visibility = Visibility.Visible;
                    nodeEllipses[i].MouseLeave += (sender2, e2) => nodeBorders[index].Visibility = Visibility.Collapsed;

                    nodeRadians[i] = (double)BitConverter.ToUInt32(ds.nodeInfos[i].Id.hash.Decompose(0, 4).Reverse().ToArray(), 0) / (double)uint.MaxValue * 2.0 * Math.PI;
                }

                double[] nodeXs = new double[ds.numberOfNodes];
                double[] nodeYs = new double[ds.numberOfNodes];
                canvas.SizeChanged += (sender2, e2) =>
                {
                    double diameter = Math.Min(canvas.ActualWidth, canvas.ActualHeight) * 0.8;
                    double radius = diameter / 2.0;

                    ellipse.Width = diameter;
                    ellipse.Height = diameter;

                    double ellipseTop = (canvas.ActualHeight - diameter) / 2.0;
                    double ellipseLeft = (canvas.ActualWidth - diameter) / 2.0;

                    Canvas.SetTop(ellipse, ellipseTop);
                    Canvas.SetLeft(ellipse, ellipseLeft);

                    for (int i = 0; i < nodeEllipses.Length; i++)
                    {
                        nodeYs[i] = ellipseTop + radius - radius * Math.Sin(nodeRadians[i]);
                        nodeXs[i] = ellipseLeft + radius + radius * Math.Cos(nodeRadians[i]);

                        Canvas.SetTop(nodeEllipses[i], nodeYs[i] - nodeEllipseRadius);
                        Canvas.SetLeft(nodeEllipses[i], nodeXs[i] - nodeEllipseRadius);

                        Canvas.SetTop(nodeBorders[i], nodeYs[i] + nodeEllipseRadius);
                        Canvas.SetLeft(nodeBorders[i], nodeXs[i] + nodeEllipseRadius);
                    }

                    if (!isStarted)
                    {
                        isStarted = true;

                        this.StartTask(string.Empty, string.Empty, () => ds.Start());
                    }
                };

                ds.sn.Communicated += (sender2, e2) => this.Lambda(() =>
                {
                    Line line = new Line();
                    line.X1 = nodeXs[nodes[ds.connections[e2.Item1]]];
                    line.Y1 = nodeYs[nodes[ds.connections[e2.Item1]]];
                    line.X2 = nodeXs[nodes[ds.connections[e2.Item2]]];
                    line.Y2 = nodeYs[nodes[ds.connections[e2.Item2]]];
                    line.Stroke = Brushes.Blue;
                    line.Width = canvas.ActualWidth;
                    line.Height = canvas.ActualHeight;
                    canvas.Children.Add(line);
                }).BeginExecuteInUIThread();

                ds.Received += (sender2, e2) => this.Lambda(() => tbMessage.Text = e2.ToString()).BeginExecuteInUIThread();
            };
        }
    }

    public class DiffuseSimulation
    {
        public DiffuseSimulation()
        {
            nodeInfos = new NodeInformation[numberOfNodes];
            for (int i = 0; i < numberOfNodes; i++)
                nodeInfos[i] = new NodeInformation(IPAddress.Loopback, (ushort)(startPortNumber + i), Network.localtest, string.Empty);

            for (int i = 0; i < numberOfNodes; i++)
                portNumberToIndex.Add(nodeInfos[i].portNumber, i);

            sn = new SimulationNetwork();
            sss = new SimulationSocket[numberOfNodes];
            cremlias = new Cremlia[numberOfNodes];
            randomNumss = numberOfNodes.OperateWhileTrue((non) => non.RandomNums()).Where((ns) => ns.Select((n, i) => new { n, i }).All((ni) => ni.n != ni.i)).Take(numberOfDiffuseNodes).ToArray();
            int receiveCounter = 0;
            for (int i = 0; i < numberOfNodes; i++)
            {
                sss[i] = new SimulationSocket(sn);
                sss[i].Bind(new IPEndPoint(IPAddress.Any, nodeInfos[i].portNumber));
                sss[i].Listen(100);

                cremlias[i] = new Cremlia(new CremliaIdFactorySha256(), new CremliaDatabaseIo(), new CremliaNetworkIoSimulation(sss[i]), new CremliaNodeInfomationSha256(nodeInfos[i]));
                int[] randomNums = numberOfNodes.RandomNums();
                for (int j = 0; j < numberOfNodes; j++)
                    if (randomNums[j] != i)
                        cremlias[i].UpdateNodeStateWhenJoin(new CremliaNodeInfomationSha256(nodeInfos[randomNums[j]]));

                int index = i;

                this.StartTask(string.Empty, string.Empty, () =>
                {
                    object l = new object();
                    int counter = 0;

                    while (true)
                    {
                        SimulationSocket ss = sss[index].Accept() as SimulationSocket;

                        bool flag = false;
                        lock (l)
                            if (counter++ == 0)
                                flag = true;

                        this.StartTask(string.Empty, string.Empty, () =>
                        {
                            NodeInformation ni = SHAREDDATA.FromBinary<NodeInformation>(ss.Read());
                            byte[] data = ss.Read();
                            ss.Close();

                            Thread.Sleep(connectWait);

                            if (flag && index != 0)
                            {
                                Received(this, ++receiveCounter);

                                string.Join(",", receiveCounter.ToString(), stopwatch.ElapsedMilliseconds.ToString()).ConsoleWriteLine();

                                if (receiveCounter == numberOfNodes)
                                    stopwatch.Stop();

                                //int[] nodes = SelectNodes1(index);
                                int[] nodes = SelectNodes2(index, ni);
                                for (int j = 0; j < nodes.Length; j++)
                                {
                                    SimulationSocket ss2 = new SimulationSocket(sn);

                                    Thread.Sleep(verifyWait);

                                    ss2.Connect(IPAddress.Loopback, (ushort)(startPortNumber + nodes[j]));

                                    connections.Add(ss2.localPortNumber, (ushort)(startPortNumber + nodes[j]));
                                    connections.Add(ss2.remotePortNumber, nodeInfos[index].portNumber);

                                    ss2.Write(nodeInfos[index].ToBinary());
                                    ss2.Write(new byte[1024]);
                                    ss2.Close();
                                }
                            }
                        });
                    }
                });
            }
        }

        private int[] SelectNodes1(int myNodeIndex)
        {
            int[] nodes = new int[numberOfDiffuseNodes];
            for (int i = 0; i < numberOfDiffuseNodes; i++)
                nodes[i] = randomNumss[i][myNodeIndex];
            return nodes;
        }

        private int[] SelectNodes2(int myNodeIndex, NodeInformation prevNodeInfo)
        {
            if (prevNodeInfo == null)
            {
                List<int> nodes = new List<int>();
                for (int i = 255; i >= 0; i--)
                {
                    ICremliaNodeInfomation[] nodeInfos1 = cremlias[myNodeIndex].GetKbuckets(i);
                    if (nodeInfos1.Length != 0)
                        nodes.Add(portNumberToIndex[(nodeInfos1[nodeInfos1.Length.RandomNum()] as CremliaNodeInfomationSha256).nodeInfo.portNumber]);
                }
                return nodes.ToArray();
            }
            else
            {
                List<int> nodes = new List<int>();
                for (int i = cremlias[myNodeIndex].GetDistanceLevel(new CremliaId<Sha256Hash>(prevNodeInfo.Id)); i >= 0; i--)
                {
                    ICremliaNodeInfomation[] nodeInfos1 = cremlias[myNodeIndex].GetKbuckets(i);
                    if (nodeInfos1.Length != 0)
                        nodes.Add(portNumberToIndex[(nodeInfos1[nodeInfos1.Length.RandomNum()] as CremliaNodeInfomationSha256).nodeInfo.portNumber]);
                }
                return nodes.ToArray();
            }

            SortedList<ICremliaId, ICremliaNodeInfomation> findTable = new SortedList<ICremliaId, ICremliaNodeInfomation>();
            cremlias[myNodeIndex].GetNeighborNodesTable(new CremliaId<Sha256Hash>(nodeInfos[myNodeIndex].Id), findTable);
            int[] neighborNodes = new int[3];
            for (int i = 0; i < 3; i++)
                neighborNodes[i] = portNumberToIndex[(findTable[findTable.Keys[i]] as CremliaNodeInfomationSha256).nodeInfo.portNumber];
            return neighborNodes;
        }

        public void Start()
        {
            stopwatch.Start();

            //int[] nodes = SelectNodes1(0);
            int[] nodes = SelectNodes2(0, null);
            for (int j = 0; j < nodes.Length; j++)
            {
                SimulationSocket ss = new SimulationSocket(sn);

                Thread.Sleep(connectWait);

                ss.Connect(IPAddress.Loopback, (ushort)(startPortNumber + nodes[j]));

                connections.Add(ss.localPortNumber, (ushort)(startPortNumber + nodes[j]));
                connections.Add(ss.remotePortNumber, 9000);

                ss.Write(nodeInfos[0].ToBinary());
                ss.Write(new byte[1024]);
                ss.Close();
            }
        }

        private readonly ushort startPortNumber = 9000;
        private readonly Stopwatch stopwatch = new Stopwatch();

        public readonly int numberOfNodes = 8;
        public readonly int numberOfDiffuseNodes = 3;
        public readonly int connectWait = 50;
        public readonly int verifyWait = 70;

        public readonly NodeInformation[] nodeInfos;
        public readonly SimulationNetwork sn;
        public readonly SimulationSocket[] sss;
        public readonly Cremlia[] cremlias;
        public readonly int[][] randomNumss;

        public readonly Dictionary<ushort, ushort> connections = new Dictionary<ushort, ushort>();
        public readonly Dictionary<ushort, int> portNumberToIndex = new Dictionary<ushort, int>();

        public event EventHandler<int> Received = delegate { };
    }

    public class CremliaNetworkIoSimulation : ICremliaNetworkIo
    {
        public CremliaNetworkIoSimulation(SimulationSocket _ss)
        {
            ss = _ss;
        }

        private readonly SimulationSocket ss;

        public event EventHandler<ICremliaNetworkIoSession> SessionStarted = delegate { };

        public ICremliaNetworkIoSession StartSession(ICremliaNodeInfomation nodeInfo)
        {
            return null;
        }
    }

    public class CremliaNetworkIoSessionSimulation : ICremliaNetworkIoSession
    {
        public CremliaNetworkIoSessionSimulation(ICremliaNodeInfomation _nodeInfo, SimulationSocket _ss)
        {
            nodeInfo = _nodeInfo;
            ss = _ss;
        }

        private readonly ICremliaNodeInfomation nodeInfo;
        private readonly SimulationSocket ss;

        public ICremliaNodeInfomation NodeInfo
        {
            get { return nodeInfo; }
        }

        public void Write(CremliaMessageBase message)
        {
            throw new NotImplementedException();
        }

        public CremliaMessageBase Read()
        {
            throw new NotImplementedException();
        }

        public void Close()
        {
            ss.Close();
        }
    }

    #region BTS

    public interface IBTSUser
    {
        void ReportBug();
    }

    public interface IBTSTester : IBTSUser { }

    public interface IBTSDeveloper : IBTSTester
    {
        void ReportBugFix();
    }

    public interface IBTSAdministrator : IBTSDeveloper
    {
        void AssignBugToDeveloper();
        void CheckBugFix();
    }

    public class BTSUse
    {
        public void ReportBug() { Trace.WriteLine("report bug"); }
    }

    public class BTSTest
    {
        public void ReportBug() { Trace.WriteLine("report bug"); }
    }

    public class BTSDevelopment
    {
        public void ReportBugFix() { Trace.WriteLine("report bug fix"); }
    }

    public class BTSAdministration
    {
        public void AssignBugToDeveloper() { Trace.WriteLine("assign bug to developer"); }
        public void CheckBugFix() { Trace.WriteLine("assign bug fix"); }
    }

    public class BTSUser : IBTSUser
    {
        public BTSUser() { use = new BTSUse(); }

        private readonly BTSUse use;

        public void ReportBug() { use.ReportBug(); }
    }

    public class BTSTester : IBTSTester
    {
        public BTSTester()
        {
            use = new BTSUse();
            test = new BTSTest();
        }

        private readonly BTSUse use;
        private readonly BTSTest test;

        public void ReportBug() { test.ReportBug(); }
    }

    public class BTSDeveloper : IBTSDeveloper
    {
        public BTSDeveloper()
        {
            use = new BTSUse();
            test = new BTSTest();
            development = new BTSDevelopment();
        }

        private readonly BTSUse use;
        private readonly BTSTest test;
        private readonly BTSDevelopment development;

        public void ReportBug() { test.ReportBug(); }
        public void ReportBugFix() { development.ReportBugFix(); }
    }

    public class BTSAdministrator : IBTSAdministrator
    {
        private readonly BTSUse use;
        private readonly BTSTest test;
        private readonly BTSDevelopment development;
        private readonly BTSAdministration administration;

        public void ReportBug() { test.ReportBug(); }
        public void AssignBugToDeveloper() { administration.AssignBugToDeveloper(); }
        public void ReportBugFix() { development.ReportBugFix(); }
        public void CheckBugFix() { administration.CheckBugFix(); }
    }

    #endregion
}

namespace New
{
    using CREA2014;
    using System.IO;

}