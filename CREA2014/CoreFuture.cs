using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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
            randomNumss = numberOfNodes.PipeForever((non) => non.RandomNums()).Where((ns) => ns.Select((n, i) => new { n, i }).All((ni) => ni.n != ni.i)).Take(numberOfDiffuseNodes).ToArray();
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

                                this.ConsoleWriteLine(string.Join(",", receiveCounter.ToString(), stopwatch.ElapsedMilliseconds.ToString()));

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
    using System.Reflection;

    //using BMI = BlockManagementInformation;
    //using BMIBlocks = BlockManagementInformationsPerBlockIndex;
    //using BMIFile = BlockManagementInformationsPerFile;
    //using BMIManager = BlockManagementInformationManager;
    //using BMIDB = BlockManagementInfomationDB;

    public class TestBlock : Block
    {
        public TestBlock() : base(null) { }

        public TestBlock(long _index) : base(null) { index = _index; }

        private long index;

        public override long Index { get { return index; } }
        public override X15Hash PrevId { get { return null; } }
        public override Difficulty<X15Hash> Difficulty { get { return null; } }
        public override Transaction[] Transactions { get { return new Transaction[] { }; } }

        protected override Func<ReaderWriter, IEnumerable<MainDataInfomation>> StreamInfo
        {
            get
            {
                return (msrw) => new MainDataInfomation[]{
                    new MainDataInfomation(typeof(long), () => index, (o) => index = (long)o),
                };
            }
        }
    }



    public class BlockManagerData : SHAREDDATA
    {
        public BlockManagerData()
            : base(0)
        {
            headBlockIndex = -1;
            finalizedBlockIndex = -1;
        }

        public long headBlockIndex { get; set; }
        public long finalizedBlockIndex { get; set; }

        protected override Func<ReaderWriter, IEnumerable<MainDataInfomation>> StreamInfo
        {
            get
            {
                if (Version == 0)
                    return (msrw) => new MainDataInfomation[]{
                        new MainDataInfomation(typeof(long), () => headBlockIndex, (o) => headBlockIndex = (long)o),
                        new MainDataInfomation(typeof(long), () => finalizedBlockIndex, (o) => finalizedBlockIndex = (long)o),
                };
                else
                    throw new NotSupportedException();
            }
        }
        public override bool IsVersioned { get { return true; } }
    }

    public class BlockManagerDB : SimpleDatabase
    {
        public BlockManagerDB(string _pathBase) : base(_pathBase) { }

        protected override int version { get { return 0; } }

#if TEST
        protected override string filenameBase { get { return "blk_mng_test_" + version.ToString(); } }
#else
        protected override string filenameBase { get { return "blk_mng_" + version.ToString(); } }
#endif
    }

    public class BlockChain
    {
        public BlockChain(BlockManagerDB _bmdb, BlockDB _bdb, BlockFilePointersDB _bfpdb, UtxoFilePointersDB _ufpdb, UtxoFilePointersTempDB _ufptempdb, UtxoDB _udb)
        {
            bmdb = _bmdb;
            bdb = _bdb;
            bfpdb = _bfpdb;
            ufpdb = _ufpdb;
            ufptempdb = _ufptempdb;
            udb = _udb;

            blockManager = new BlockManager(bmdb, bdb, bfpdb, mainBlocksRetain, oldBlocksRetain, mainBlockFinalization);
            utxoManager = new UtxoManager(ufpdb, ufptempdb, udb);

            pendingBlocks = new Dictionary<X15Hash, Block>[capacity];
            rejectedBlocks = new Dictionary<X15Hash, Block>[capacity];
            blocksCurrent = new CirculatedInteger((int)capacity);
        }

        private static readonly long maxBlockIndexMargin = 100;
        private static readonly long mainBlockFinalization = 300;
        private static readonly long capacity = (maxBlockIndexMargin + mainBlockFinalization) * 2;

        private static readonly int mainBlocksRetain = 1000;
        private static readonly int oldBlocksRetain = 1000;

        private readonly BlockManagerDB bmdb;
        private readonly BlockDB bdb;
        private readonly BlockFilePointersDB bfpdb;
        private readonly UtxoFilePointersDB ufpdb;
        private readonly UtxoFilePointersTempDB ufptempdb;
        private readonly UtxoDB udb;

        private readonly BlockManager blockManager;
        private readonly UtxoManager utxoManager;

        private readonly Dictionary<X15Hash, Block>[] pendingBlocks;
        private readonly Dictionary<X15Hash, Block>[] rejectedBlocks;
        private readonly CirculatedInteger blocksCurrent;

        public enum UpdateChainInnerReturnType { updated, invariable, pending, rejected }

        public class UpdateChainInnerReturn
        {
            public UpdateChainInnerReturn(UpdateChainInnerReturnType _type)
            {
                if (type == UpdateChainInnerReturnType.rejected || type == UpdateChainInnerReturnType.pending)
                    throw new ArgumentException();

                type = _type;
            }

            public UpdateChainInnerReturn(UpdateChainInnerReturnType _type, int _position)
            {
                if (type != UpdateChainInnerReturnType.pending)
                    throw new ArgumentException();

                type = _type;
                position = _position;
            }

            public UpdateChainInnerReturn(UpdateChainInnerReturnType _type, int _position, List<Block> _rejectedBlocks)
            {
                if (type != UpdateChainInnerReturnType.rejected)
                    throw new ArgumentException();

                type = _type;
                position = _position;
                rejectedBlocks = _rejectedBlocks;
            }

            public UpdateChainInnerReturnType type { get; set; }
            public int position { get; set; }
            public List<Block> rejectedBlocks { get; set; }
        }

        public void UpdateChain(Block block)
        {
            UpdateChainInnerReturn ret = UpdateChainInner(block);

            if (ret.type == UpdateChainInnerReturnType.pending)
            {
                if (pendingBlocks[ret.position] == null)
                {
                    pendingBlocks[ret.position] = new Dictionary<X15Hash, Block>();
                    pendingBlocks[ret.position].Add(block.Id, block);
                }
                else if (!pendingBlocks[ret.position].Keys.Contains(block.Id))
                    pendingBlocks[ret.position].Add(block.Id, block);
            }
            else if (ret.type == UpdateChainInnerReturnType.rejected)
            {
                CirculatedInteger ci = new CirculatedInteger(ret.position, (int)capacity);
                for (int i = 0; i < ret.rejectedBlocks.Count; i++, ci.Previous())
                {
                    if (pendingBlocks[ci.value] != null && pendingBlocks[ci.value].Keys.Contains(ret.rejectedBlocks[i].Id))
                        pendingBlocks[ci.value].Remove(ret.rejectedBlocks[i].Id);
                    if (rejectedBlocks[ci.value] == null)
                    {
                        rejectedBlocks[ci.value] = new Dictionary<X15Hash, Block>();
                        rejectedBlocks[ci.value].Add(ret.rejectedBlocks[i].Id, ret.rejectedBlocks[i]);
                    }
                    else
                        rejectedBlocks[ci.value].Add(ret.rejectedBlocks[i].Id, ret.rejectedBlocks[i]);
                }
            }
        }

        public UpdateChainInnerReturn UpdateChainInner(Block block)
        {
            long minBlockIndex = blockManager.mainBlockFinalization;
            long maxBlockIndex = blockManager.headBlockIndex + maxBlockIndexMargin;

            if (block.Index > maxBlockIndex)
                throw new InvalidOperationException();
            if (block.Index <= minBlockIndex)
                throw new InvalidOperationException();

            int position = blocksCurrent.GetForward((int)(block.Index - blockManager.headBlockIndex));
            if (rejectedBlocks[position] != null && rejectedBlocks[position].Keys.Contains(block.Id))
                return new UpdateChainInnerReturn(UpdateChainInnerReturnType.invariable);

            if (block.Index == blockManager.headBlockIndex + 1)
            {
                //if (block.PrevId.Equals(blockManager.GetHeadBlock().Id) && VerifyBlock(block))
                //{


                //    blockManager.AddMainBlock(block);
                //    //utxoManager.ApplyBlock(block);

                //    return new UpdateChainInnerReturn(UpdateChainInnerReturnType.updated);
                //}

                return new UpdateChainInnerReturn(UpdateChainInnerReturnType.pending, position);
            }

            Block currentBrunchBlock = block;
            Block currentMainBlock = blockManager.GetMainBlock(currentBrunchBlock.Index);

            if (currentBrunchBlock.Id.Equals(currentMainBlock.Id))
                return new UpdateChainInnerReturn(UpdateChainInnerReturnType.invariable);

            double brunchDiff = 0.0;
            double mainDiff = 0.0;

            List<Block> brunches = new List<Block>();
            CirculatedInteger ci = new CirculatedInteger(position, (int)capacity);
            while (true)
            {
                brunches.Add(currentBrunchBlock);

                brunchDiff += currentBrunchBlock.Difficulty.Diff;
                mainDiff += currentMainBlock.Difficulty.Diff;

                if (currentBrunchBlock.PrevId.Equals(currentMainBlock.PrevId))
                    break;

                long prevIndex = currentBrunchBlock.Index - 1;

                if (prevIndex <= minBlockIndex)
                    return new UpdateChainInnerReturn(UpdateChainInnerReturnType.rejected, position, brunches);

                ci.Previous();

                if (pendingBlocks[ci.value] == null || !pendingBlocks[ci.value].Keys.Contains(currentBrunchBlock.PrevId))
                    if (rejectedBlocks[ci.value] != null && rejectedBlocks[ci.value].Keys.Contains(currentBrunchBlock.PrevId))
                        return new UpdateChainInnerReturn(UpdateChainInnerReturnType.rejected, position, brunches);
                    else
                        return new UpdateChainInnerReturn(UpdateChainInnerReturnType.pending, position);

                currentBrunchBlock = pendingBlocks[ci.value][currentBrunchBlock.PrevId];
                currentMainBlock = blockManager.GetMainBlock(prevIndex);
            }

            if (brunchDiff <= mainDiff)
                return new UpdateChainInnerReturn(UpdateChainInnerReturnType.pending, position);

            Block currentBrunchBlock2 = block;
            ci = new CirculatedInteger(position, (int)capacity);
            while (true)
            {
                ci.Next();

                if (pendingBlocks[ci.value] == null)
                    break;

                currentBrunchBlock2 = pendingBlocks[ci.value].Values.Where((elem) => elem.PrevId.Equals(currentBrunchBlock2.Id)).FirstOrDefault();
                if (currentBrunchBlock2 == null)
                    break;

                brunches.Insert(0, currentBrunchBlock2);

                brunchDiff += currentBrunchBlock2.Difficulty.Diff;
            }

            Block currentMainBlock2 = blockManager.GetMainBlock(currentBrunchBlock.Index);
            while (currentMainBlock2.Index != blockManager.headBlockIndex)
            {
                currentMainBlock2 = blockManager.GetMainBlock(currentMainBlock2.Index + 1);

                mainDiff += currentMainBlock2.Difficulty.Diff;
            }

            if (brunchDiff <= mainDiff)
                return new UpdateChainInnerReturn(UpdateChainInnerReturnType.pending, position);

            //検証


            for (long i = blockManager.headBlockIndex; i >= currentMainBlock.Index; i--)
            {
                blockManager.DeleteMainBlock(i);
                //utxoManager.RevertBlock()
            }
            for (long i = blockManager.headBlockIndex + 1; i <= brunches[0].Index; i++)
            {
                blockManager.AddMainBlock(brunches[(int)(brunches[0].Index - i)]);
                //utxoManager.ApplyBlock(brunches[(int)(brunches[0].Index - i)]);
            }





            throw new NotImplementedException();

        }

        private void AddUtxo(Dictionary<Sha256Ripemd160Hash, List<Utxo>> utxoDict, Sha256Ripemd160Hash address, Utxo utxo)
        {
            List<Utxo> utxos = null;
            if (utxoDict.Keys.Contains(address))
                utxos = utxoDict[address];
            else
                utxoDict.Add(address, utxos = new List<Utxo>());
            utxos.Add(utxo);
        }

        private void RetrieveTransactionTransitionForward(Block block, TransactionOutput[][] prevTxOutss, Dictionary<Sha256Ripemd160Hash, List<Utxo>> addedUtxos, Dictionary<Sha256Ripemd160Hash, List<Utxo>> removedUtxos)
        {
            block.Transactions.ForEach((i, tx) =>
            {
                tx.TxInputs.ForEach((j, txIn) => AddUtxo(removedUtxos, prevTxOutss[i][j].Address, new Utxo(txIn.PrevTxBlockIndex, txIn.PrevTxIndex, txIn.PrevTxOutputIndex, prevTxOutss[i][j].Amount)));
                tx.TxOutputs.ForEach((j, txOut) => AddUtxo(addedUtxos, txOut.Address, new Utxo(block.Index, i, j, txOut.Amount)));
            });
        }

        private void RetrieveTransactionTransitionBackward(Block block, TransactionOutput[][] prevTxOutss, Dictionary<Sha256Ripemd160Hash, List<Utxo>> addedUtxos, Dictionary<Sha256Ripemd160Hash, List<Utxo>> removedUtxos)
        {
            block.Transactions.ForEach((i, tx) =>
            {
                tx.TxInputs.ForEach((j, txIn) => AddUtxo(addedUtxos, prevTxOutss[i][j].Address, new Utxo(txIn.PrevTxBlockIndex, txIn.PrevTxIndex, txIn.PrevTxOutputIndex, prevTxOutss[i][j].Amount)));
                tx.TxOutputs.ForEach((j, txOut) => AddUtxo(removedUtxos, txOut.Address, new Utxo(block.Index, i, j, txOut.Amount)));
            });
        }

        private TransactionOutput[][] GetPrevTxOutputss(Block block)
        {
            throw new NotImplementedException();
            //block.Transactions
        }




        private bool VerifyBlock(Block block, TransactionOutput[][] prevTxOutss, Dictionary<Sha256Ripemd160Hash, List<Utxo>> addedUtxos, Dictionary<Sha256Ripemd160Hash, List<Utxo>> removedUtxos)
        {
            throw new NotImplementedException();
        }
    }

    public class BlockManager
    {
        public BlockManager(BlockManagerDB _bmdb, BlockDB _bdb, BlockFilePointersDB _bfpdb, int _mainBlocksRetain, int _oldBlocksRetain, long _mainBlockFinalization)
        {
            if (mainBlocksRetain < mainBlockFinalization)
                throw new InvalidOperationException();

            bmdb = _bmdb;
            bdb = _bdb;
            bfpdb = _bfpdb;

            mainBlocksRetain = _mainBlocksRetain;
            oldBlocksRetain = _oldBlocksRetain;
            mainBlockFinalization = _mainBlockFinalization;

            bmd = bmdb.GetData().Pipe((bmdBytes) => bmdBytes.Length != 0 ? SHAREDDATA.FromBinary<BlockManagerData>(bmdBytes) : new BlockManagerData());

            mainBlocks = new Block[mainBlocksRetain];
            sideBlocks = new List<Block>[mainBlocksRetain];
            mainBlocksCurrent = new CirculatedInteger(mainBlocksRetain);

            oldBlocks = new Dictionary<long, Block>();

            if (bmd.headBlockIndex == -1)
                AddMainBlock(new GenesisBlock());
        }

        private static readonly long blockFileCapacity = 100000;

        public readonly int mainBlocksRetain;
        public readonly int oldBlocksRetain;
        public readonly long mainBlockFinalization;

        private readonly BlockManagerDB bmdb;
        private readonly BlockDB bdb;
        private readonly BlockFilePointersDB bfpdb;
        private readonly BlockManagerData bmd;

        private readonly Block[] mainBlocks;
        private readonly List<Block>[] sideBlocks;
        private readonly CirculatedInteger mainBlocksCurrent;

        private readonly Dictionary<long, Block> oldBlocks;

        public long headBlockIndex { get { return bmd.headBlockIndex; } }
        public long finalizedBlockIndex { get { return bmd.finalizedBlockIndex; } }

        public void DeleteMainBlock(long blockIndex)
        {
            if (blockIndex != bmd.headBlockIndex)
                throw new InvalidOperationException();
            if (blockIndex <= bmd.finalizedBlockIndex)
                throw new InvalidOperationException();
            if (blockIndex < 0)
                throw new InvalidOperationException();

            if (sideBlocks[mainBlocksCurrent.value] == null)
                sideBlocks[mainBlocksCurrent.value] = new List<Block>();
            if (sideBlocks[mainBlocksCurrent.value].Where((elem) => elem.Id.Equals(mainBlocks[mainBlocksCurrent.value].Id)).FirstOrDefault() == null)
                sideBlocks[mainBlocksCurrent.value].Add(mainBlocks[mainBlocksCurrent.value]);

            mainBlocks[mainBlocksCurrent.value] = null;

            mainBlocksCurrent.Previous();

            bmd.headBlockIndex = blockIndex - 1;

            bmdb.UpdateData(bmd.ToBinary());
        }

        public void DeleteMainBlocks(long blockIndex)
        {

        }

        public void AddMainBlock(Block block)
        {
            if (block.Index != bmd.headBlockIndex + 1)
                throw new InvalidOperationException();

            mainBlocksCurrent.Next();

            mainBlocks[mainBlocksCurrent.value] = block;
            sideBlocks[mainBlocksCurrent.value] = new List<Block>();

            bmd.headBlockIndex = block.Index;
            bmd.finalizedBlockIndex = bmd.headBlockIndex < 300 ? 0 : bmd.headBlockIndex - mainBlockFinalization;

            bfpdb.UpdateBlockFilePointerData(block.Index, BitConverter.GetBytes(bdb.AddBlockData(block.Index / blockFileCapacity, SHAREDDATA.ToBinary<Block>(block))));
            bmdb.UpdateData(bmd.ToBinary());
        }

        public void AddMainBlocks(Block[] blocks)
        {

        }

        public Block GetHeadBlock() { return GetMainBlock(headBlockIndex); }

        public Block GetMainBlock(long blockIndex)
        {
            if (blockIndex > bmd.headBlockIndex)
                throw new InvalidOperationException();

            if (blockIndex > bmd.headBlockIndex - mainBlocksRetain)
            {
                int index = mainBlocksCurrent.GetBackward((int)(bmd.headBlockIndex - blockIndex));
                if (mainBlocks[index] == null)
                    mainBlocks[index] = SHAREDDATA.FromBinary<Block>(bdb.GetBlockData(blockIndex / blockFileCapacity, BitConverter.ToInt64(bfpdb.GetBlockFilePointerData(blockIndex), 0)));

                if (mainBlocks[index].Index != blockIndex)
                    throw new InvalidOperationException();

                return mainBlocks[index];
            }

            if (oldBlocks.Keys.Contains(blockIndex))
                return oldBlocks[blockIndex];

            Block block = SHAREDDATA.FromBinary<Block>(bdb.GetBlockData(blockIndex / blockFileCapacity, BitConverter.ToInt64(bfpdb.GetBlockFilePointerData(blockIndex), 0)));

            if (block.Index != blockIndex)
                throw new InvalidOperationException();

            oldBlocks.Add(blockIndex, block);

            while (oldBlocks.Count > oldBlocksRetain)
                oldBlocks.Remove(oldBlocks.First().Key);

            return block;
        }

        //<未改良>一括取得
        public Block[] GetMainBlocks(long[] blockIndexes)
        {
            Block[] blocks = new Block[blockIndexes.Length];
            for (int i = 0; i < blocks.Length; i++)
                blocks[i] = GetMainBlock(blockIndexes[i]);
            return blocks;
        }

        //<未改良>一括取得
        public Block[] GetMainBlocks(long blockIndexFrom, long blockIndexThrough)
        {
            if (blockIndexFrom > blockIndexThrough)
                throw new InvalidOperationException();

            Block[] blocks = new Block[blockIndexThrough - blockIndexFrom + 1];
            for (int i = 0; i < blocks.Length; i++)
                blocks[i] = GetMainBlock(blockIndexFrom + i);
            return blocks;
        }
    }

    public class UtxoManager
    {
        public UtxoManager(UtxoFilePointersDB _ufpdb, UtxoFilePointersTempDB _ufptempdb, UtxoDB _udb)
        {
            ufpdb = _ufpdb;
            ufptempdb = _ufptempdb;
            udb = _udb;

            ufp = SHAREDDATA.FromBinary<UtxoFilePointers>(ufpdb.GetData());
            ufptemp = SHAREDDATA.FromBinary<UtxoFilePointers>(ufptempdb.GetData());

            foreach (var ufpitem in ufptemp.GetAll())
                ufp.AddOrUpdate(ufpitem.Key, ufpitem.Value);

            //<未実装>ファイルが壊れていないか確認するための
            ufpdb.UpdateData(ufp.ToBinary());
        }

        private static readonly int FirstUtxoFileItemSize = 16;

        private readonly UtxoFilePointersDB ufpdb;
        private readonly UtxoFilePointersTempDB ufptempdb;
        private readonly UtxoFilePointers ufp;
        private readonly UtxoFilePointers ufptemp;
        private readonly UtxoDB udb;

        public void AddUtxo(Sha256Ripemd160Hash address, long blockIndex, int txIndex, int txOutIndex, CurrencyUnit amount)
        {
            long? prevPosition = null;
            long? position = ufptemp.Get(address);
            if (!position.HasValue)
                position = ufp.Get(address);

            bool isProcessed = false;

            UtxoFileItem ufi = null;
            while (true)
            {
                if (!position.HasValue)
                    ufi = new UtxoFileItem(FirstUtxoFileItemSize);
                else if (position == -1)
                    ufi = new UtxoFileItem(ufi.Size * 2);
                else
                    ufi = SHAREDDATA.FromBinary<UtxoFileItem>(udb.GetUtxoData(position.Value));

                for (int k = 0; k < ufi.Size; k++)
                    if (ufi.utxos[k].IsEmpty)
                    {
                        ufi.utxos[k].Reset(blockIndex, txIndex, txOutIndex, amount);

                        if (!position.HasValue)
                            ufptemp.Add(address, udb.AddUtxoData(ufi.ToBinary()));
                        else if (position == -1)
                        {
                            ufi.Update(prevPosition.Value);
                            ufptemp.Update(address, udb.AddUtxoData(ufi.ToBinary()));
                        }
                        else
                            udb.UpdateUtxoData(position.Value, ufi.ToBinary());

                        isProcessed = true;

                        break;
                    }

                if (isProcessed)
                    break;

                prevPosition = position;
                position = ufi.nextPosition;
            }
        }

        public void RemoveUtxo(Sha256Ripemd160Hash address, long blockIndex, int txIndex, int txOutIndex)
        {
            long? position = ufptemp.Get(address);
            if (!position.HasValue)
                position = ufp.Get(address);

            if (!position.HasValue)
                throw new InvalidOperationException();

            bool isProcessed = false;

            while (!isProcessed && position.Value != -1)
            {
                UtxoFileItem ufi = SHAREDDATA.FromBinary<UtxoFileItem>(udb.GetUtxoData(position.Value));

                for (int k = 0; k < ufi.Size && !isProcessed; k++)
                    if (ufi.utxos[k].IsMatch(blockIndex, txIndex, txOutIndex))
                    {
                        ufi.utxos[k].Empty();

                        udb.UpdateUtxoData(position.Value, ufi.ToBinary());

                        isProcessed = true;
                    }

                position = ufi.nextPosition;
            }

            if (!isProcessed)
                throw new InvalidOperationException();
        }

        public void ApplyBlock(Block block, TransactionOutput[][] prevTxOutss)
        {
            block.Transactions.ForEach((i, tx) =>
            {
                tx.TxInputs.ForEach((j, txIn) => RemoveUtxo(prevTxOutss[i][j].Address, txIn.PrevTxBlockIndex, txIn.PrevTxIndex, txIn.PrevTxOutputIndex));
                tx.TxOutputs.ForEach((j, txOut) => AddUtxo(txOut.Address, block.Index, i, j, txOut.Amount));
            });
        }

        public void RevertBlock(Block block, TransactionOutput[][] prevTxOutss)
        {
            if (block.Transactions.Length != prevTxOutss.Length)
                throw new ArgumentException();

            block.Transactions.ForEach((i, tx) =>
            {
                if (tx.TxInputs.Length != prevTxOutss[i].Length)
                    throw new ArgumentException();

                tx.TxInputs.ForEach((j, txIn) => AddUtxo(prevTxOutss[i][j].Address, txIn.PrevTxBlockIndex, txIn.PrevTxIndex, txIn.PrevTxOutputIndex, prevTxOutss[i][j].Amount));
                tx.TxOutputs.ForEach((j, txOut) => RemoveUtxo(txOut.Address, block.Index, i, j));
            });
        }
    }

    //2014/11/27 試験済
    public class UtxoFilePointers : SHAREDDATA
    {
        public UtxoFilePointers() : base(null) { addressFilePointers = new Dictionary<Sha256Ripemd160Hash, long>(); }

        private Dictionary<Sha256Ripemd160Hash, long> addressFilePointers;

        protected override Func<ReaderWriter, IEnumerable<MainDataInfomation>> StreamInfo { get { return (msrw) => StreamInfoInner(msrw); } }
        private IEnumerable<MainDataInfomation> StreamInfoInner(ReaderWriter msrw)
        {
            Sha256Ripemd160Hash[] addresses = addressFilePointers.Keys.ToArray();
            long[] positions = addressFilePointers.Values.ToArray();

            yield return new MainDataInfomation(typeof(Sha256Ripemd160Hash[]), null, null, () => addresses, (o) => addresses = (Sha256Ripemd160Hash[])o);
            yield return new MainDataInfomation(typeof(long[]), null, () => positions, (o) =>
            {
                positions = (long[])o;

                if (addresses.Length != positions.Length)
                    throw new InvalidOperationException();

                addressFilePointers = new Dictionary<Sha256Ripemd160Hash, long>();
                for (int i = 0; i < addresses.Length; i++)
                    addressFilePointers.Add(addresses[i], positions[i]);
            });
        }

        public void Add(Sha256Ripemd160Hash address, long position)
        {
            if (addressFilePointers.Keys.Contains(address))
                throw new InvalidOperationException();

            addressFilePointers.Add(address, position);
        }

        public void Remove(Sha256Ripemd160Hash address)
        {
            if (!addressFilePointers.Keys.Contains(address))
                throw new InvalidOperationException();

            addressFilePointers.Remove(address);
        }

        public void Update(Sha256Ripemd160Hash address, long positionNew)
        {
            if (!addressFilePointers.Keys.Contains(address))
                throw new InvalidOperationException();

            addressFilePointers[address] = positionNew;
        }

        public void AddOrUpdate(Sha256Ripemd160Hash address, long position)
        {
            if (addressFilePointers.Keys.Contains(address))
                addressFilePointers[address] = position;
            else
                addressFilePointers.Add(address, position);
        }

        public long? Get(Sha256Ripemd160Hash address)
        {
            return addressFilePointers.Keys.Contains(address) ? (long?)addressFilePointers[address] : null;
        }

        public Dictionary<Sha256Ripemd160Hash, long> GetAll() { return addressFilePointers; }
    }

    public class Utxos
    {
        public Utxos() { }

        public Utxos(Dictionary<Sha256Ripemd160Hash, List<Utxo>> _utxos) { utxos = _utxos; }

        public Dictionary<Sha256Ripemd160Hash, List<Utxo>> utxos { get; private set; }

        public void Add(Sha256Ripemd160Hash address, Utxo utxo)
        {
            List<Utxo> list = null;
            if (utxos.Keys.Contains(address))
                list = utxos[address];
            else
                utxos.Add(address, list = new List<Utxo>());

            if (list.FirstOrDefault((elem) => elem.blockIndex == utxo.blockIndex && elem.txIndex == utxo.txIndex && elem.txOutIndex == utxo.txOutIndex) != null)
                throw new InvalidOperationException("already_existed");

            list.Add(utxo);
        }

        public void Remove(Sha256Ripemd160Hash address, long blockIndex, int txIndex, int txOutIndex)
        {
            if (!utxos.Keys.Contains(address))
                throw new InvalidOperationException("not_existed");

            List<Utxo> list = utxos[address];

            Utxo utxo = null;
            if ((utxo = list.FirstOrDefault((elem) => elem.blockIndex == blockIndex && elem.txIndex == txIndex && elem.txOutIndex == txOutIndex)) == null)
                throw new InvalidOperationException("not_existed");

            list.Remove(utxo);

            if (list.Count == 0)
                utxos.Remove(address);
        }

        public void Update(Dictionary<Sha256Ripemd160Hash, List<Utxo>> addedUtxos, Dictionary<Sha256Ripemd160Hash, List<Utxo>> removedUtxos)
        {
            foreach (var addedUtxos2 in addedUtxos)
                foreach (var addedUtxo in addedUtxos2.Value)
                    Add(addedUtxos2.Key, addedUtxo);
            foreach (var removedUtxos2 in removedUtxos)
                foreach (var removedUtxo in removedUtxos2.Value)
                    Remove(removedUtxos2.Key, removedUtxo.blockIndex, removedUtxo.txIndex, removedUtxo.txOutIndex);
        }

        public bool Contains(Sha256Ripemd160Hash address, long blockIndex, int txIndex, int txOutIndex)
        {
            if (!utxos.Keys.Contains(address))
                return false;

            List<Utxo> list = utxos[address];

            return list.FirstOrDefault((elem) => elem.blockIndex == blockIndex && elem.txIndex == txIndex && elem.txOutIndex == txOutIndex) != null;
        }

        public bool ContainsAddress(Sha256Ripemd160Hash address) { return utxos.Keys.Contains(address); }

        public List<Utxo> GetAddressUtxos(Sha256Ripemd160Hash address)
        {
            if (!utxos.Keys.Contains(address))
                throw new InvalidOperationException("not_existed");

            return utxos[address];
        }
    }

    #region kakutei

    public class Utxo : SHAREDDATA
    {
        public Utxo() : base(null) { amount = new CurrencyUnit(0); }

        public Utxo(long _blockIndex, int _txIndex, int _txOutIndex, CurrencyUnit _amount)
            : base(null)
        {
            blockIndex = _blockIndex;
            txIndex = _txIndex;
            txOutIndex = _txOutIndex;
            amount = _amount;
        }

        public long blockIndex { get; private set; }
        public int txIndex { get; private set; }
        public int txOutIndex { get; private set; }
        public CurrencyUnit amount { get; private set; }

        protected override Func<ReaderWriter, IEnumerable<MainDataInfomation>> StreamInfo
        {
            get
            {
                return (msrw) => new MainDataInfomation[]{
                    new MainDataInfomation(typeof(long), () => blockIndex, (o) => blockIndex = (long)o),
                    new MainDataInfomation(typeof(int), () => txIndex, (o) => txIndex = (int)o),
                    new MainDataInfomation(typeof(int), () => txOutIndex, (o) => txOutIndex = (int)o),
                    new MainDataInfomation(typeof(long), () => amount.rawAmount, (o) => amount = new CurrencyUnit((long)o)),
                };
            }
        }

        public bool IsEmpty { get { return blockIndex == 0; } }

        public void Empty()
        {
            blockIndex = 0;
            txIndex = 0;
            txOutIndex = 0;
            amount = CurrencyUnit.Zero;
        }

        public void Reset(long _blockIndex, int _txIndex, int _txOutIndex, CurrencyUnit _amount)
        {
            blockIndex = _blockIndex;
            txIndex = _txIndex;
            txOutIndex = _txOutIndex;
            amount = _amount;
        }

        public bool IsMatch(long _blockIndex, int _txIndex, int _txOutIndex)
        {
            return blockIndex == _blockIndex && txIndex == _txIndex && txOutIndex == _txOutIndex;
        }

        public bool IsMatch(long _blockIndex, int _txIndex, int _txOutIndex, CurrencyUnit _amount)
        {
            return blockIndex == _blockIndex && txIndex == _txIndex && txOutIndex == _txOutIndex && amount.Amount == _amount.Amount;
        }
    }

    //2014/11/26 試験済
    public class UtxoFileItem : SHAREDDATA
    {
        public UtxoFileItem() : base(null) { }

        public UtxoFileItem(int _size)
            : base(null)
        {
            utxos = new Utxo[_size];
            for (int i = 0; i < utxos.Length; i++)
                utxos[i] = new Utxo();
            nextPosition = -1;
        }

        public Utxo[] utxos { get; private set; }
        public long nextPosition { get; private set; }

        public int Size { get { return utxos.Length; } }

        protected override Func<ReaderWriter, IEnumerable<MainDataInfomation>> StreamInfo
        {
            get
            {
                return (msrw) => new MainDataInfomation[]{
                    new MainDataInfomation(typeof(Utxo[]), null, null, () => utxos, (o) => utxos = (Utxo[])o),
                    new MainDataInfomation(typeof(long), () => nextPosition, (o) => nextPosition = (long)o),
                };
            }
        }

        public void Update(long nextPositionNew) { nextPosition = nextPositionNew; }
    }

    public class UtxoFileAccessDB : DATABASEBASE
    {
        public UtxoFileAccessDB(string _pathBase) : base(_pathBase) { }

        protected override int version { get { return 0; } }

#if TEST
        protected override string filenameBase { get { return "utxos_access_test" + version.ToString(); } }
#else
        protected override string filenameBase { get { return "utxos" + version.ToString(); } }
#endif

        public void Create()
        {
            try
            {
                string path = GetPath();

                if (File.Exists(path))
                    throw new InvalidOperationException("utxos_access_file_exist");

                File.Create(path);
            }
            catch (Exception ex)
            {

            }
        }

        public void Delete()
        {
            try
            {
                string path = GetPath();

                if (!File.Exists(path))
                    throw new InvalidOperationException("utxo_access_file_not_exist");

                File.Delete(path);
            }
            catch (Exception ex)
            {

            }
        }

        public string GetPath() { return System.IO.Path.Combine(pathBase, filenameBase); }
    }

    public class UtxoFilePointersDB : SimpleDatabase
    {
        public UtxoFilePointersDB(string _pathBase) : base(_pathBase) { }

        protected override int version { get { return 0; } }

#if TEST
        protected override string filenameBase { get { return "utxos_index_test" + version.ToString(); } }
#else
        protected override string filenameBase { get { return "utxos_index" + version.ToString(); } }
#endif
    }

    public class UtxoFilePointersTempDB : SimpleDatabase
    {
        public UtxoFilePointersTempDB(string _pathBase) : base(_pathBase) { }

        protected override int version { get { return 0; } }

#if TEST
        protected override string filenameBase { get { return "utxos_index_temp_test" + version.ToString(); } }
#else
        protected override string filenameBase { get { return "utxos_index_tamp" + version.ToString(); } }
#endif
    }

    //2014/11/26 試験済
    public class UtxoDB : DATABASEBASE
    {
        public UtxoDB(string _pathBase) : base(_pathBase) { }

        protected override int version { get { return 0; } }

#if TEST
        protected override string filenameBase { get { return "utxos_test" + version.ToString(); } }
#else
        protected override string filenameBase { get { return "utxos" + version.ToString(); } }
#endif

        public byte[] GetUtxoData(long position)
        {
            using (FileStream fs = new FileStream(GetPath(), FileMode.OpenOrCreate, FileAccess.Read))
            {
                if (position >= fs.Length)
                    return new byte[] { };

                fs.Seek(position, SeekOrigin.Begin);

                byte[] lengthBytes = new byte[4];
                fs.Read(lengthBytes, 0, 4);
                int length = BitConverter.ToInt32(lengthBytes, 0);

                byte[] data = new byte[length];
                fs.Read(data, 0, length);
                return data;
            }
        }

        public long AddUtxoData(byte[] utxoData)
        {
            using (FileStream fs = new FileStream(GetPath(), FileMode.Append, FileAccess.Write))
            {
                long position = fs.Position;

                fs.Write(BitConverter.GetBytes(utxoData.Length), 0, 4);
                fs.Write(utxoData, 0, utxoData.Length);

                return position;
            }
        }

        public void UpdateUtxoData(long position, byte[] utxoData)
        {
            using (FileStream fs = new FileStream(GetPath(), FileMode.OpenOrCreate, FileAccess.ReadWrite))
            {
                fs.Seek(position, SeekOrigin.Begin);

                byte[] lengthBytes = new byte[4];
                fs.Read(lengthBytes, 0, 4);
                int length = BitConverter.ToInt32(lengthBytes, 0);

                if (utxoData.Length != length)
                    throw new InvalidOperationException();

                fs.Write(utxoData, 0, utxoData.Length);
            }
        }

        public string GetPath() { return System.IO.Path.Combine(pathBase, filenameBase); }
    }

    public class BlockFilePointersDB : DATABASEBASE
    {
        public BlockFilePointersDB(string _pathBase) : base(_pathBase) { }

        protected override int version { get { return 0; } }

#if TEST
        protected override string filenameBase { get { return "blks_index_test" + version.ToString(); } }
#else
        protected override string filenameBase { get { return "blks_index_test" + version.ToString(); } }
#endif

        public byte[] GetBlockFilePointerData(long blockIndex)
        {
            using (FileStream fs = new FileStream(GetPath(), FileMode.OpenOrCreate, FileAccess.Read))
            {
                fs.Seek(blockIndex * 8, SeekOrigin.Begin);

                byte[] blockPointerData = new byte[8];
                fs.Read(blockPointerData, 0, 8);
                return blockPointerData;
            }
        }

        public void UpdateBlockFilePointerData(long blockIndex, byte[] blockFilePointerData)
        {
            using (FileStream fs = new FileStream(GetPath(), FileMode.OpenOrCreate, FileAccess.Write))
            {
                fs.Seek(blockIndex * 8, SeekOrigin.Begin);
                fs.Write(blockFilePointerData, 0, 8);
            }
        }

        private string GetPath() { return System.IO.Path.Combine(pathBase, filenameBase); }
    }

    public class BlockDB : DATABASEBASE
    {
        public BlockDB(string _pathBase) : base(_pathBase) { }

        protected override int version { get { return 0; } }

#if TEST
        protected override string filenameBase { get { return "blks_test" + version.ToString() + "_"; } }
#else
        protected override string filenameBase { get { return "blks" + version.ToString() + "_"; } }
#endif

        public byte[] GetBlockData(long blockFileIndex, long position)
        {
            using (FileStream fs = new FileStream(GetPath(blockFileIndex), FileMode.OpenOrCreate, FileAccess.Read))
                return GetBlockData(fs, position);
        }

        public byte[][] GetBlockDatas(long blockFileIndex, long[] positions)
        {
            byte[][] blockDatas = new byte[positions.Length][];

            using (FileStream fs = new FileStream(GetPath(blockFileIndex), FileMode.OpenOrCreate, FileAccess.Read))
                for (int i = 0; i < positions.Length; i++)
                    blockDatas[i] = GetBlockData(fs, positions[i]);

            return blockDatas;
        }

        public long AddBlockData(long blockFileIndex, byte[] blockData)
        {
            using (FileStream fs = new FileStream(GetPath(blockFileIndex), FileMode.Append, FileAccess.Write))
                return AddBlockData(fs, blockFileIndex, blockData);
        }

        public long[] AddBlockDatas(long blockFileIndex, byte[][] blockDatas)
        {
            long[] positions = new long[blockDatas.Length];

            using (FileStream fs = new FileStream(GetPath(blockFileIndex), FileMode.Append, FileAccess.Write))
                for (int i = 0; i < blockDatas.Length; i++)
                    positions[i] = AddBlockData(blockFileIndex, blockDatas[i]);

            return positions;
        }

        private byte[] GetBlockData(FileStream fs, long position)
        {
            if (position >= fs.Length)
                return new byte[] { };

            fs.Seek(position, SeekOrigin.Begin);

            byte[] lengthBytes = new byte[4];
            fs.Read(lengthBytes, 0, 4);
            int length = BitConverter.ToInt32(lengthBytes, 0);

            byte[] data = new byte[length];
            fs.Read(data, 0, length);
            return data;
        }

        private long AddBlockData(FileStream fs, long blockFileIndex, byte[] blockData)
        {
            long position = fs.Position;

            fs.Write(BitConverter.GetBytes(blockData.Length), 0, 4);
            fs.Write(blockData, 0, blockData.Length);

            return position;
        }

        private string GetPath(long blockFileIndex) { return System.IO.Path.Combine(pathBase, filenameBase + blockFileIndex.ToString()); }
    }

    #endregion

    public static class BlockChainTest
    {
        //UtxoDBのテスト
        public static void Test1()
        {
            string basepath = System.IO.Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);
            UtxoDB utxodb = new UtxoDB(basepath);
            string path = utxodb.GetPath();

            if (File.Exists(path))
                File.Delete(path);

            byte[] emptyBytes = utxodb.GetUtxoData(0);

            if (emptyBytes.Length != 0)
                throw new Exception("test1_1");

            byte[] utxoBytesIn1 = new byte[1024];
            for (int i = 0; i < utxoBytesIn1.Length; i++)
                utxoBytesIn1[i] = (byte)256.RandomNum();

            int overallLangth = utxoBytesIn1.Length + 4;

            long position1 = utxodb.AddUtxoData(utxoBytesIn1);

            if (position1 != 0)
                throw new Exception("test1_2");

            long position2 = utxodb.AddUtxoData(utxoBytesIn1);

            if (position2 != overallLangth)
                throw new Exception("test1_3");

            byte[] utxoBytesOut1 = utxodb.GetUtxoData(position1);

            if (!utxoBytesIn1.BytesEquals(utxoBytesOut1))
                throw new Exception("test1_4");

            byte[] utxoBytesOut2 = utxodb.GetUtxoData(position2);

            if (!utxoBytesIn1.BytesEquals(utxoBytesOut2))
                throw new Exception("test1_5");

            byte[] utxoBytesIn2 = new byte[utxoBytesIn1.Length];
            for (int i = 0; i < utxoBytesIn2.Length; i++)
                utxoBytesIn2[i] = (byte)256.RandomNum();

            utxodb.UpdateUtxoData(position1, utxoBytesIn2);

            byte[] utxoBytesOut3 = utxodb.GetUtxoData(position1);

            if (!utxoBytesIn2.BytesEquals(utxoBytesOut3))
                throw new Exception("test1_6");

            byte[] utxoBytesOut4 = utxodb.GetUtxoData(position2);

            if (!utxoBytesIn1.BytesEquals(utxoBytesOut4))
                throw new Exception("test1_7");

            utxodb.UpdateUtxoData(position2, utxoBytesIn2);

            byte[] utxoBytesOut5 = utxodb.GetUtxoData(position1);

            if (!utxoBytesIn2.BytesEquals(utxoBytesOut5))
                throw new Exception("test1_8");

            byte[] utxoBytesOut6 = utxodb.GetUtxoData(position2);

            if (!utxoBytesIn2.BytesEquals(utxoBytesOut6))
                throw new Exception("test1_9");

            byte[] emptyBytes2 = utxodb.GetUtxoData(overallLangth * 2);
        }

        //UtxoFileItemのテスト
        public static void Test2()
        {
            int size1 = 16;

            UtxoFileItem ufiIn = new UtxoFileItem(size1);

            for (int i = 0; i < ufiIn.utxos.Length; i++)
            {
                if (ufiIn.utxos[i].blockIndex != 0)
                    throw new Exception("test2_11");
                if (ufiIn.utxos[i].txIndex != 0)
                    throw new Exception("test2_12");
                if (ufiIn.utxos[i].txOutIndex != 0)
                    throw new Exception("test2_13");
                if (ufiIn.utxos[i].amount.rawAmount != 0)
                    throw new Exception("test2_14");
            }

            if (ufiIn.utxos.Length != size1)
                throw new Exception("test2_1");

            if (ufiIn.Size != size1)
                throw new Exception("test2_2");

            if (ufiIn.nextPosition != -1)
                throw new Exception("test2_3");

            for (int i = 0; i < ufiIn.utxos.Length; i++)
                ufiIn.utxos[i] = new Utxo(65536.RandomNum(), 65536.RandomNum(), 65536.RandomNum(), new Creacoin(65536.RandomNum()));

            byte[] ufiBytes = ufiIn.ToBinary();

            if (ufiBytes.Length != 397)
                throw new Exception("test2_16");

            UtxoFileItem ufiOut = SHAREDDATA.FromBinary<UtxoFileItem>(ufiBytes);

            if (ufiIn.utxos.Length != ufiOut.utxos.Length)
                throw new Exception("test2_4");

            if (ufiIn.Size != ufiOut.Size)
                throw new Exception("test2_5");

            if (ufiIn.nextPosition != ufiOut.nextPosition)
                throw new Exception("test2_6");

            for (int i = 0; i < ufiIn.utxos.Length; i++)
            {
                if (ufiIn.utxos[i].blockIndex != ufiOut.utxos[i].blockIndex)
                    throw new Exception("test2_7");
                if (ufiIn.utxos[i].txIndex != ufiOut.utxos[i].txIndex)
                    throw new Exception("test2_8");
                if (ufiIn.utxos[i].txOutIndex != ufiOut.utxos[i].txOutIndex)
                    throw new Exception("test2_9");
                if (ufiIn.utxos[i].amount.rawAmount != ufiOut.utxos[i].amount.rawAmount)
                    throw new Exception("test2_10");
            }

            for (int i = 0; i < ufiIn.utxos.Length; i++)
                ufiOut.utxos[i] = new Utxo(65536.RandomNum(), 65536.RandomNum(), 65536.RandomNum(), new Creacoin(65536.RandomNum()));

            byte[] ufiBytes2 = ufiOut.ToBinary();

            if (ufiBytes.Length != ufiBytes2.Length)
                throw new Exception("test2_15");
        }

        //UtxoFilePointersのテスト
        public static void Test3()
        {
            UtxoFilePointers ufp = new UtxoFilePointers();

            Dictionary<Sha256Ripemd160Hash, long> afps = ufp.GetAll();

            if (afps.Count != 0)
                throw new Exception("test3_1");

            Sha256Ripemd160Hash hash1 = new Sha256Ripemd160Hash(new byte[] { (byte)256.RandomNum() });
            Sha256Ripemd160Hash hash2 = new Sha256Ripemd160Hash(new byte[] { (byte)256.RandomNum() });
            Sha256Ripemd160Hash hash3 = new Sha256Ripemd160Hash(new byte[] { (byte)256.RandomNum() });

            long position1 = 56636.RandomNum();
            long position2 = 56636.RandomNum();
            long position3 = 56636.RandomNum();

            long? positionNull = ufp.Get(hash1);

            if (positionNull.HasValue)
                throw new Exception("test3_2");

            ufp.Add(hash1, position1);
            ufp.Add(hash2, position2);
            ufp.Add(hash3, position3);

            bool flag = false;
            try
            {
                ufp.Add(hash1, position1);
            }
            catch (InvalidOperationException)
            {
                flag = true;
            }
            if (!flag)
                throw new Exception("test3_3");

            Dictionary<Sha256Ripemd160Hash, long> afps2 = ufp.GetAll();

            if (afps2.Count != 3)
                throw new Exception("test3_4");
            if (!afps2.Keys.Contains(hash1))
                throw new Exception("test3_5");
            if (!afps2.Keys.Contains(hash2))
                throw new Exception("test3_6");
            if (!afps2.Keys.Contains(hash3))
                throw new Exception("test3_7");
            if (afps2[hash1] != position1)
                throw new Exception("test3_8");
            if (afps2[hash2] != position2)
                throw new Exception("test3_9");
            if (afps2[hash3] != position3)
                throw new Exception("test3_10");

            long? position1Out = ufp.Get(hash1);
            long? position2Out = ufp.Get(hash2);
            long? position3Out = ufp.Get(hash3);

            if (!position1Out.HasValue || position1Out.Value != position1)
                throw new Exception("test3_11");
            if (!position2Out.HasValue || position2Out.Value != position2)
                throw new Exception("test3_12");
            if (!position3Out.HasValue || position3Out.Value != position3)
                throw new Exception("test3_13");

            ufp.Remove(hash1);

            bool flag2 = false;
            try
            {
                ufp.Remove(hash1);
            }
            catch (InvalidOperationException)
            {
                flag2 = true;
            }
            if (!flag2)
                throw new Exception("test3_14");

            Dictionary<Sha256Ripemd160Hash, long> afps3 = ufp.GetAll();

            if (afps3.Count != 2)
                throw new Exception("test3_15");

            ufp.Update(hash2, position1);

            long? position1Out2 = ufp.Get(hash2);

            if (!position1Out2.HasValue || position1Out2.Value != position1)
                throw new Exception("test3_16");

            bool flag3 = false;
            try
            {
                ufp.Update(hash1, position2);
            }
            catch (InvalidOperationException)
            {
                flag3 = true;
            }
            if (!flag3)
                throw new Exception("test3_17");

            Dictionary<Sha256Ripemd160Hash, long> afps4 = ufp.GetAll();

            if (afps4.Count != 2)
                throw new Exception("test3_18");

            ufp.AddOrUpdate(hash2, position3);

            long? position1Out3 = ufp.Get(hash2);

            if (!position1Out3.HasValue || position1Out3.Value != position3)
                throw new Exception("test3_19");

            Dictionary<Sha256Ripemd160Hash, long> afps5 = ufp.GetAll();

            if (afps5.Count != 2)
                throw new Exception("test3_20");

            ufp.AddOrUpdate(hash1, position3);

            long? position1Out4 = ufp.Get(hash1);

            if (!position1Out4.HasValue || position1Out4.Value != position3)
                throw new Exception("test3_21");

            Dictionary<Sha256Ripemd160Hash, long> afps6 = ufp.GetAll();

            if (afps5.Count != 3)
                throw new Exception("test3_22");

            byte[] ufpBytes = ufp.ToBinary();

            UtxoFilePointers ufp2 = SHAREDDATA.FromBinary<UtxoFilePointers>(ufpBytes);

            Dictionary<Sha256Ripemd160Hash, long> afps7 = ufp2.GetAll();

            if (afps6.Count != afps7.Count)
                throw new Exception("test3_23");

            foreach (var key in afps6.Keys)
            {
                if (!afps7.Keys.Contains(key))
                    throw new Exception("test3_24");
                if (afps6[key] != afps7[key])
                    throw new Exception("test3_25");
            }
        }

        //
        public static void Test4()
        {
            string basepath = System.IO.Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);

            UtxoFilePointersDB ufpdb = new UtxoFilePointersDB(basepath);
            string ufpdbPath = ufpdb.GetPath();

            if (File.Exists(ufpdbPath))
                File.Delete(ufpdbPath);

            UtxoFilePointersTempDB ufptempdb = new UtxoFilePointersTempDB(basepath);
            string ufptempdbPath = ufptempdb.GetPath();

            if (File.Exists(ufptempdbPath))
                File.Delete(ufptempdbPath);

            UtxoDB utxodb = new UtxoDB(basepath);
            string utxodbPath = utxodb.GetPath();

            if (File.Exists(utxodbPath))
                File.Delete(utxodbPath);

            UtxoManager utxom = new UtxoManager(ufpdb, ufptempdb, utxodb);


        }
    }

    #region temp

    //    public class BlockManagementInformation : SHAREDDATA
    //    {
    //        public BlockManagementInformation() : base(null) { }

    //        public BlockManagementInformation(long _position, bool _isMain)
    //            : base(null)
    //        {
    //            position = _position;
    //            isMain = _isMain;
    //        }

    //        public long position { get; private set; }
    //        public bool isMain { get; set; }

    //        protected override Func<ReaderWriter, IEnumerable<MainDataInfomation>> StreamInfo
    //        {
    //            get
    //            {
    //                return (msrw) => new MainDataInfomation[]{
    //                    new MainDataInfomation(typeof(long), () => position, (o) => position = (long)o),
    //                    new MainDataInfomation(typeof(bool), () => isMain, (o) => isMain = (bool)o),
    //                };
    //            }
    //        }
    //    }

    //    public class BlockManagementInformationsPerBlockIndex : SHAREDDATA
    //    {
    //        public BlockManagementInformationsPerBlockIndex() : base(null) { bmis = new BMI[] { }; }

    //        public BlockManagementInformationsPerBlockIndex(BMI[] _bmis) : base(null) { bmis = _bmis; }

    //        private readonly object bmisLock = new object();
    //        public BMI[] bmis { get; private set; }

    //        protected override Func<ReaderWriter, IEnumerable<MainDataInfomation>> StreamInfo
    //        {
    //            get
    //            {
    //                return (msrw) => new MainDataInfomation[]{
    //                    new MainDataInfomation(typeof(BMI[]), null, null, () => bmis, (o) => bmis = (BMI[])o),
    //                };
    //            }
    //        }

    //        public void AddBMI(BMI bmiAdded)
    //        {
    //            lock (bmisLock)
    //            {
    //                BMI[] bmisNew = new BMI[bmis.Length + 1];
    //                for (int i = 0; i < bmis.Length; i++)
    //                {
    //                    bmisNew[i] = bmis[i];
    //                    bmisNew[i].isMain = bmisNew[i].isMain.Nonimp(bmiAdded.isMain);
    //                }
    //                bmisNew[bmis.Length] = bmiAdded;
    //                bmis = bmisNew;
    //            }
    //        }

    //        public void AddBMIs(BMI[] bmisAdded)
    //        {
    //            lock (bmisLock)
    //            {
    //                bool isMain = false;
    //                for (int i = 0; i < bmisAdded.Length; i++)
    //                {
    //                    if (isMain && bmisAdded[i].isMain)
    //                        throw new InvalidOperationException();

    //                    isMain = isMain || bmisAdded[i].isMain;
    //                }

    //                BMI[] bmisNew = new BMI[bmis.Length + bmisAdded.Length];
    //                for (int i = 0; i < bmis.Length; i++)
    //                {
    //                    bmisNew[i] = bmis[i];
    //                    bmisNew[i].isMain = bmisNew[i].isMain.Nonimp(isMain);
    //                }
    //                for (int i = 0; i < bmisAdded.Length; i++)
    //                    bmisNew[bmis.Length + i] = bmisAdded[i];
    //                bmis = bmisNew;
    //            }
    //        }
    //    }

    //    public class BlockManagementInformationsPerFile : SHAREDDATA
    //    {
    //        public BlockManagementInformationsPerFile() : base(null) { bmiBlockss = new BMIBlocks[] { }; }

    //        public BlockManagementInformationsPerFile(BMIBlocks[] _bmiBlockss) : base(null) { bmiBlockss = _bmiBlockss; }

    //        public BMIBlocks[] bmiBlockss { get; private set; }

    //        protected override Func<ReaderWriter, IEnumerable<MainDataInfomation>> StreamInfo
    //        {
    //            get
    //            {
    //                return (msrw) => new MainDataInfomation[]{
    //                    new MainDataInfomation(typeof(BMIBlocks[]), null, null, () => bmiBlockss, (o) => bmiBlockss = (BMIBlocks[])o),
    //                };
    //            }
    //        }

    //        //ここでのindexはbmiBlockssのindex＝ファイル内でのindex＝block index % capacity
    //        public void AddBMI(long index, BMI bmiAdded) { bmiBlockss[index].AddBMI(bmiAdded); }
    //        public void AddBMIs(long index, BMI[] bmisAdded) { bmiBlockss[index].AddBMIs(bmisAdded); }
    //    }

    //    public class BlockManagementInformationManager
    //    {
    //        public BlockManagementInformationManager(BMIDB _bmidb)
    //        {
    //            bmidb = _bmidb;
    //            bmiFileCache = new CirculatedReadCache<long, BMIFile>(bmiFileCachesNum, (bmiFileIndex) =>
    //            {
    //                byte[] bmiFileData = bmidb.GetBMIFileData(bmiFileIndex);
    //                if (bmiFileData.Length != 0)
    //                    return SHAREDDATA.FromBinary<BMIFile>(bmidb.GetBMIFileData(bmiFileIndex));

    //                BMIBlocks[] bmiBlockss = new BMIBlocks[bmiFileCapacity];
    //                for (int i = 0; i < bmiBlockss.Length; i++)
    //                    bmiBlockss[i] = new BMIBlocks();
    //                return new BMIFile(bmiBlockss);
    //            }, (bmiFileIndex, bmiFile) => bmidb.UpdateBMIFileData(bmiFileIndex, bmiFile.ToBinary()));
    //        }

    //        private static readonly long bmiFileCapacity = 10000;
    //        private static readonly int bmiFileCachesNum = 10;

    //        private readonly BMIDB bmidb;
    //        private readonly CirculatedReadCache<long, BMIFile> bmiFileCache;

    //        public void AddBMI(long blockIndex, BMI bmiAdded)
    //        {
    //            bmiFileCache.Get(blockIndex / bmiFileCapacity).AddBMI(blockIndex % bmiFileCapacity, bmiAdded);
    //        }

    //        public void AddBMIs(long blockIndex, BMI[] bmisAdded)
    //        {
    //            bmiFileCache.Get(blockIndex / bmiFileCapacity).AddBMIs(blockIndex % bmiFileCapacity, bmisAdded);
    //        }

    //        public BMI GetMainBMI(long blockIndex)
    //        {
    //            foreach (BMI bmi in bmiFileCache.Get(blockIndex / bmiFileCapacity).bmiBlockss[blockIndex % bmiFileCapacity].bmis)
    //                if (bmi.isMain)
    //                    return bmi;

    //            throw new InvalidOperationException();
    //        }

    //        public BMI[] GetBMIs(long blockIndex)
    //        {
    //            return bmiFileCache.Get(blockIndex / bmiFileCapacity).bmiBlockss[blockIndex % bmiFileCapacity].bmis;
    //        }

    //        public void Save()
    //        {
    //            bmiFileCache.SaveAll();
    //        }
    //    }

    //    public class BlockManager
    //    {
    //        public BlockManager(BlockDB _blockdb, BMIDB _bmidb)
    //        {
    //            blockdb = _blockdb;
    //            bmidb = _bmidb;
    //            bmiManager = new BMIManager(bmidb);
    //        }

    //        private static readonly long blockFileDiv = 100000;
    //        private static readonly int blockCacheNum = 300;

    //        private readonly BlockDB blockdb;
    //        private readonly BMIDB bmidb;
    //        private readonly BMIManager bmiManager;

    //        public void AddBlock(Block block)
    //        {

    //        }

    //        public void AddBlocks(Block[] blocks)
    //        {

    //        }

    //        public Block GetMainBlock(long blockIndex)
    //        {
    //            throw new NotImplementedException();
    //        }

    //        public Block GetBlocks(long blockIndex)
    //        {
    //            throw new NotImplementedException();
    //        }

    //        public void Save()
    //        {

    //        }
    //    }

    //    public class BlockManagementInfomationDB : DATABASEBASE
    //    {
    //        public BlockManagementInfomationDB(string _pathBase) : base(_pathBase) { }

    //        protected override int version { get { return 0; } }

    //#if TEST
    //        protected override string filenameBase { get { return "blg_mng_infos_test" + version.ToString() + "_"; } }
    //#else
    //        protected override string filenameBase { get { return "blg_mng_infos" + version.ToString() + "_"; } }
    //#endif

    //        public byte[] GetBMIFileData(long bmiFileIndex)
    //        {
    //            using (FileStream fs = new FileStream(GetPath(bmiFileIndex), FileMode.OpenOrCreate, FileAccess.Read))
    //            {
    //                byte[] bmiFileData = new byte[fs.Length];
    //                fs.Read(bmiFileData, 0, bmiFileData.Length);
    //                return bmiFileData;
    //            }
    //        }

    //        public void UpdateBMIFileData(long bmiFileIndex, byte[] bmiFileData)
    //        {
    //            using (FileStream fs = new FileStream(GetPath(bmiFileIndex), FileMode.Create, FileAccess.Write))
    //                fs.Write(bmiFileData, 0, bmiFileData.Length);
    //        }

    //        private string GetPath(long bngIndex) { return System.IO.Path.Combine(pathBase, filenameBase + bngIndex.ToString()); }
    //    }

    #endregion
}