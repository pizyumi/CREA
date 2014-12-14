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
        public override Creahash PrevId { get { return null; } }
        public override Difficulty<Creahash> Difficulty { get { return null; } }
        public override Transaction[] Transactions { get { return new Transaction[] { }; } }

        public const string guidString = "4c5f058d31606b4d9830bc713f6162f0";
        public override Guid Guid { get { return new Guid(guidString); } }

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

    //2014/12/05 分岐が発生しない場合 試験済
    //2014/12/14 分岐が発生する場合 試験済
    public class BlockChain
    {
        public BlockChain(BlockchainAccessDB _bcadb, BlockManagerDB _bmdb, BlockDB _bdb, BlockFilePointersDB _bfpdb, UtxoFileAccessDB _ufadb, UtxoFilePointersDB _ufpdb, UtxoFilePointersTempDB _ufptempdb, UtxoDB _udb, long _maxBlockIndexMargin = 100, long _mainBlockFinalization = 300, int _mainBlocksRetain = 1000, int _oldBlocksRetain = 1000)
        {
            bcadb = _bcadb;
            bmdb = _bmdb;
            bdb = _bdb;
            bfpdb = _bfpdb;
            ufadb = _ufadb;
            ufpdb = _ufpdb;
            ufptempdb = _ufptempdb;
            udb = _udb;

            maxBlockIndexMargin = _maxBlockIndexMargin;
            mainBlockFinalization = _mainBlockFinalization;
            capacity = (maxBlockIndexMargin + mainBlockFinalization) * 2;

            mainBlocksRetain = _mainBlocksRetain;
            oldBlocksRetain = _oldBlocksRetain;

            blockManager = new BlockManager(bmdb, bdb, bfpdb, mainBlocksRetain, oldBlocksRetain, mainBlockFinalization);
            utxoManager = new UtxoManager(ufadb, ufpdb, ufptempdb, udb);

            pendingBlocks = new Dictionary<Creahash, Block>[capacity];
            rejectedBlocks = new Dictionary<Creahash, Block>[capacity];
            blocksCurrent = new CirculatedInteger((int)capacity);
        }

        private long maxBlockIndexMargin = 100;
        private long mainBlockFinalization = 300;
        private long capacity;

        private int mainBlocksRetain = 1000;
        private int oldBlocksRetain = 1000;

        private readonly BlockchainAccessDB bcadb;
        private readonly BlockManagerDB bmdb;
        private readonly BlockDB bdb;
        private readonly BlockFilePointersDB bfpdb;
        private readonly UtxoFileAccessDB ufadb;
        private readonly UtxoFilePointersDB ufpdb;
        private readonly UtxoFilePointersTempDB ufptempdb;
        private readonly UtxoDB udb;

        private readonly BlockManager blockManager;
        private readonly UtxoManager utxoManager;

        public readonly Dictionary<Creahash, Block>[] pendingBlocks;
        public readonly Dictionary<Creahash, Block>[] rejectedBlocks;
        public readonly CirculatedInteger blocksCurrent;

        public long headBlockIndex { get { return blockManager.headBlockIndex; } }
        public long finalizedBlockIndex { get { return blockManager.finalizedBlockIndex; } }

        public Utxo FindUtxo(Sha256Ripemd160Hash address, long blockIndex, int txIndex, int txOutIndex)
        {
            return utxoManager.FindUtxo(address, blockIndex, txIndex, txOutIndex);
        }

        public Block GetHeadBlock()
        {
            return blockManager.GetHeadBlock();
        }

        public Block GetMainBlock(long blockIndex)
        {
            return blockManager.GetMainBlock(blockIndex);
        }

        public enum UpdateChainReturnType { updated, invariable, pending, rejected, updatedAndRejected }

        public class UpdateChainInnerReturn
        {
            public UpdateChainInnerReturn(UpdateChainReturnType _type)
            {
                if (_type == UpdateChainReturnType.rejected || _type == UpdateChainReturnType.pending)
                    throw new ArgumentException();

                type = _type;
            }

            public UpdateChainInnerReturn(UpdateChainReturnType _type, int _position)
            {
                if (_type != UpdateChainReturnType.pending)
                    throw new ArgumentException();

                type = _type;
                position = _position;
            }

            public UpdateChainInnerReturn(UpdateChainReturnType _type, int _position, List<Block> _rejectedBlocks)
            {
                if (_type != UpdateChainReturnType.rejected && _type != UpdateChainReturnType.updatedAndRejected)
                    throw new ArgumentException();

                type = _type;
                position = _position;
                rejectedBlocks = _rejectedBlocks;
            }

            public UpdateChainReturnType type { get; set; }
            public int position { get; set; }
            public List<Block> rejectedBlocks { get; set; }
        }

        public class BlockNode
        {
            public BlockNode(Block _block, double _cumulativeDiff, CirculatedInteger _ci)
            {
                block = _block;
                cumulativeDiff = _cumulativeDiff;
                ci = _ci;
            }

            public Block block { get; set; }
            public double cumulativeDiff { get; set; }
            public CirculatedInteger ci { get; set; }
        }

        public UpdateChainReturnType UpdateChain(Block block)
        {
            //最後に確定されたブロックのブロック番号が最小ブロック番号である
            long minBlockIndex = blockManager.finalizedBlockIndex;
            //先頭ブロックのブロック番号に余裕を加えたものが最大ブロック番号である
            long maxBlockIndex = blockManager.headBlockIndex + maxBlockIndexMargin;

            //最小ブロック番号以下のブロック番号を有するブロックは認められない
            if (block.Index <= minBlockIndex)
                throw new InvalidOperationException();
            //現在のブロックのブロック番号より大き過ぎるブロック番号（最大ブロック番号を超えるブロック番号）を有するブロックは認められない
            if (block.Index > maxBlockIndex)
                throw new InvalidOperationException();

            int position = blocksCurrent.GetForward((int)(block.Index - blockManager.headBlockIndex));
            //既に阻却されているブロックの場合は何も変わらない
            if (rejectedBlocks[position] != null && rejectedBlocks[position].Keys.Contains(block.Id))
                return UpdateChainReturnType.invariable;

            //既にブロック鎖の一部である場合は何も変わらない
            Block mainBlock = block.Index > blockManager.headBlockIndex ? null : blockManager.GetMainBlock(block.Index);
            if (mainBlock != null && mainBlock.Id.Equals(block.Id))
                return UpdateChainReturnType.invariable;

            Node<BlockNode> root = new Node<BlockNode>(new BlockNode(block, block.Difficulty.Diff, new CirculatedInteger(position, (int)capacity)), null);

            Queue<Node<BlockNode>> queue = new Queue<Node<BlockNode>>();
            queue.Enqueue(root);

            while (queue.Count > 0)
            {
                Node<BlockNode> current = queue.Dequeue();

                int nextIndex = current.value.ci.GetForward(1);

                if (pendingBlocks[nextIndex] == null)
                    continue;

                foreach (var nextBlock in pendingBlocks[nextIndex].Values.Where((elem) => elem.PrevId.Equals(current.value.block.Id)))
                {
                    Node<BlockNode> child = new Node<BlockNode>(new BlockNode(nextBlock, current.value.cumulativeDiff + nextBlock.Difficulty.Diff, new CirculatedInteger(nextIndex, (int)capacity)), current);

                    current.children.Add(child);

                    queue.Enqueue(child);
                }
            }

            UpdateChainReturnType type = UpdateChainReturnType.rejected;
            double actualCumulativeDiff = 0.0;

            while (true)
            {
                queue.Enqueue(root);

                Node<BlockNode> maxCumulativeDiffNode = new Node<BlockNode>(new BlockNode(null, 0.0, null), null);
                while (queue.Count > 0)
                {
                    Node<BlockNode> current = queue.Dequeue();

                    if (current.children.Count == 0 && current.value.cumulativeDiff > maxCumulativeDiffNode.value.cumulativeDiff)
                        maxCumulativeDiffNode = current;
                    else
                        foreach (var child in current.children)
                            queue.Enqueue(child);
                }

                if (maxCumulativeDiffNode.value.cumulativeDiff <= actualCumulativeDiff)
                    return type;

                //ブロック以後のブロックを格納する
                //ブロックが分岐ブロック鎖のブロックの一部である場合は、ブロックの直前のブロックからブロック鎖から分岐するまでのブロックも格納する
                List<Block> blocksList = new List<Block>();
                //ブロック以後のブロックの被参照取引出力を格納する
                //ブロックが分岐ブロック鎖のブロックの一部である場合は、ブロックの直前のブロックからブロック鎖から分岐するまでのブロックの被参照取引出力も格納する
                List<TransactionOutput[][]> prevTxOutputssList = new List<TransactionOutput[][]>();

                //ブロックが分岐ブロック鎖のブロックの一部である場合は、先頭ブロックから分岐が始まっているブロックと同一のブロック番号のブロックまでを格納する
                List<Block> mainBlocksList = new List<Block>();
                //ブロックが分岐ブロック鎖のブロックの一部である場合は、先頭ブロックから分岐が始まっているブロックと同一のブロック番号のブロックまでの被参照取引出力を格納する
                List<TransactionOutput[][]> mainPrevTxOutputssList = new List<TransactionOutput[][]>();

                Node<BlockNode> temp = maxCumulativeDiffNode;
                while (temp != null)
                {
                    blocksList.Insert(0, temp.value.block);

                    temp = temp.parent;
                }

                UpdateChainInnerReturn ret = UpdateChainInner(block, blocksList, prevTxOutputssList, mainBlocksList, mainPrevTxOutputssList, minBlockIndex, position, maxCumulativeDiffNode.value.cumulativeDiff);

                if (ret.type == UpdateChainReturnType.updated)
                    return UpdateChainReturnType.updated;
                else if (ret.type == UpdateChainReturnType.pending)
                {
                    if (pendingBlocks[ret.position] == null)
                    {
                        pendingBlocks[ret.position] = new Dictionary<Creahash, Block>();
                        pendingBlocks[ret.position].Add(block.Id, block);
                    }
                    else if (!pendingBlocks[ret.position].Keys.Contains(block.Id))
                        pendingBlocks[ret.position].Add(block.Id, block);

                    return UpdateChainReturnType.pending;
                }
                else if (ret.type == UpdateChainReturnType.rejected || ret.type == UpdateChainReturnType.updatedAndRejected)
                {
                    Block rejectedRootBlock = ret.rejectedBlocks[0];

                    queue.Enqueue(root);

                    Node<BlockNode> rejectedRootNode = null;
                    while (queue.Count > 0)
                    {
                        Node<BlockNode> current = queue.Dequeue();

                        if (current.value.block == rejectedRootBlock)
                        {
                            rejectedRootNode = current;

                            break;
                        }

                        foreach (var child in current.children)
                            queue.Enqueue(child);
                    }

                    queue = new Queue<Node<BlockNode>>();
                    queue.Enqueue(rejectedRootNode);

                    CirculatedInteger ci = new CirculatedInteger(ret.position, (int)capacity);
                    while (queue.Count > 0)
                    {
                        Node<BlockNode> current = queue.Dequeue();

                        int rejectedPosition = ci.GetForward((int)(current.value.block.Index - rejectedRootBlock.Index));
                        if (pendingBlocks[rejectedPosition] != null && pendingBlocks[rejectedPosition].Keys.Contains(current.value.block.Id))
                            pendingBlocks[rejectedPosition].Remove(current.value.block.Id);
                        if (rejectedBlocks[rejectedPosition] == null)
                            rejectedBlocks[rejectedPosition] = new Dictionary<Creahash, Block>();
                        rejectedBlocks[rejectedPosition].Add(current.value.block.Id, current.value.block);

                        foreach (var child in current.children)
                            queue.Enqueue(child);
                    }

                    if (rejectedRootNode.parent == null)
                        return ret.type;

                    rejectedRootNode.parent.children.Remove(rejectedRootNode);

                    type = ret.type;
                    actualCumulativeDiff = rejectedRootNode.parent.value.cumulativeDiff;
                }
            }
        }

        private double VerifyBlockChain(List<Block> blocksList, Dictionary<Sha256Ripemd160Hash, List<Utxo>> addedUtxos, Dictionary<Sha256Ripemd160Hash, List<Utxo>> removedUtxos, List<TransactionOutput[][]> prevTxOutputssList)
        {
            Func<long, TransactionalBlock> _bindexToTxBlock = (bindex) =>
            {
                if (bindex < blocksList[0].Index)
                    return blockManager.GetMainBlock(bindex) as TransactionalBlock;
                else if (bindex < blocksList[0].Index + blocksList.Count)
                    return blocksList[(int)(bindex - blocksList[0].Index)] as TransactionalBlock;
                else
                    return null;
            };

            double cumulativeDiff = 0.0;

            udb.Open();

            for (int i = 0; i < blocksList.Count; i++)
            {
                //ブロックの被参照取引出力を取得する
                TransactionOutput[][] prevTxOutputss = GetPrevTxOutputss(blocksList[i], _bindexToTxBlock);

                if (prevTxOutputss == null)
                {
                    udb.Close();

                    return cumulativeDiff;
                }

                //ブロック自体の検証を実行する
                //無効ブロックである場合は阻却ブロックとする
                if (blocksList[i] is GenesisBlock)
                {
                    if (!(blocksList[i] as GenesisBlock).Verify())
                    {
                        udb.Close();

                        return cumulativeDiff;
                    }
                }
                else if (blocksList[i] is TransactionalBlock)
                {
                    if (!(blocksList[i] as TransactionalBlock).Verify(prevTxOutputss, _bindexToTxBlock))
                    {
                        udb.Close();

                        return cumulativeDiff;
                    }
                }
                else
                    throw new NotSupportedException();

                //ブロック鎖の状態（＝未使用の取引出力の存否）と矛盾がないか検証する
                //矛盾がある場合は無効ブロックとなり、阻却ブロックとする
                if (!VerifyUtxo(blocksList[i], prevTxOutputss, addedUtxos, removedUtxos))
                {
                    udb.Close();

                    return cumulativeDiff;
                }

                prevTxOutputssList.Add(prevTxOutputss);

                RetrieveTransactionTransitionForward(blocksList[i], prevTxOutputss, addedUtxos, removedUtxos);

                cumulativeDiff += blocksList[i].Difficulty.Diff;
            }

            udb.Close();

            return cumulativeDiff;
        }

        private UpdateChainInnerReturn UpdateChainInner(Block block, List<Block> blocksList, List<TransactionOutput[][]> prevTxOutputssList, List<Block> mainBlocksList, List<TransactionOutput[][]> mainPrevTxOutputssList, long minBlockIndex, int position, double cumulativeDiff)
        {
            //ブロックが起源ブロックである場合と先頭ブロックの直後のブロックである場合
            if (block.Index == blockManager.headBlockIndex + 1 && (blockManager.headBlockIndex == -1 || block.PrevId.Equals(blockManager.GetHeadBlock().Id)))
            {
                VerifyBlockChain(blocksList, new Dictionary<Sha256Ripemd160Hash, List<Utxo>>(), new Dictionary<Sha256Ripemd160Hash, List<Utxo>>(), prevTxOutputssList);

                UpdateBlockChainDB(blocksList, prevTxOutputssList, mainBlocksList, mainPrevTxOutputssList);
            }
            else
            {
                double mainCumulativeDiff = 0.0;

                for (long i = block.Index; i <= blockManager.headBlockIndex; i++)
                {
                    Block nextBlock = blockManager.GetMainBlock(i);

                    mainCumulativeDiff += nextBlock.Difficulty.Diff;

                    mainBlocksList.Add(nextBlock);
                }

                Block prevBlockBrunch = block;
                Block prevBlockMain = block.Index > blockManager.headBlockIndex ? null : blockManager.GetMainBlock(block.Index);

                CirculatedInteger ci = new CirculatedInteger(position, (int)capacity);

                while (true)
                {
                    if (prevBlockBrunch.Index == 0)
                        break;
                    if (prevBlockMain != null && prevBlockMain.PrevId.Equals(prevBlockBrunch.PrevId))
                        break;

                    long prevIndex = prevBlockBrunch.Index - 1;

                    if (prevIndex <= minBlockIndex)
                        return new UpdateChainInnerReturn(UpdateChainReturnType.rejected, ci.value, blocksList);

                    ci.Previous();

                    if (pendingBlocks[ci.value] == null || !pendingBlocks[ci.value].Keys.Contains(prevBlockBrunch.PrevId))
                        if (rejectedBlocks[ci.value] != null && rejectedBlocks[ci.value].Keys.Contains(prevBlockBrunch.PrevId))
                            return new UpdateChainInnerReturn(UpdateChainReturnType.rejected, ci.GetForward(1), blocksList);
                        else
                            return new UpdateChainInnerReturn(UpdateChainReturnType.pending, position);

                    prevBlockBrunch = pendingBlocks[ci.value][prevBlockBrunch.PrevId];
                    prevBlockMain = prevIndex > blockManager.headBlockIndex ? null : blockManager.GetMainBlock(prevIndex);

                    blocksList.Insert(0, prevBlockBrunch);
                    if (prevBlockMain != null)
                        mainBlocksList.Insert(0, prevBlockMain);

                    cumulativeDiff += prevBlockBrunch.Difficulty.Diff;
                    if (prevBlockMain != null)
                        mainCumulativeDiff += prevBlockMain.Difficulty.Diff;
                }

                if (cumulativeDiff <= mainCumulativeDiff)
                    return new UpdateChainInnerReturn(UpdateChainReturnType.pending, position);

                Func<long, TransactionalBlock> _bindexToTxBlock = (bindex) =>
                {
                    if (bindex <= blockManager.headBlockIndex)
                        return blockManager.GetMainBlock(bindex) as TransactionalBlock;
                    else
                        return null;
                };

                Dictionary<Sha256Ripemd160Hash, List<Utxo>> addedUtxos = new Dictionary<Sha256Ripemd160Hash, List<Utxo>>();
                Dictionary<Sha256Ripemd160Hash, List<Utxo>> removedUtxos = new Dictionary<Sha256Ripemd160Hash, List<Utxo>>();

                for (int i = mainBlocksList.Count - 1; i >= 0; i--)
                {
                    TransactionOutput[][] prevTxOutputss = GetPrevTxOutputss(mainBlocksList[i], _bindexToTxBlock);

                    if (prevTxOutputss == null)
                        throw new InvalidOperationException("blockchain_fork_main_backward");

                    mainPrevTxOutputssList.Insert(0, prevTxOutputss);

                    RetrieveTransactionTransitionBackward(mainBlocksList[i], prevTxOutputss, addedUtxos, removedUtxos);
                }

                cumulativeDiff = VerifyBlockChain(blocksList, addedUtxos, removedUtxos, prevTxOutputssList);

                if (cumulativeDiff > mainCumulativeDiff)
                    UpdateBlockChainDB(blocksList, prevTxOutputssList, mainBlocksList, mainPrevTxOutputssList);
            }

            if (blocksList.Count != prevTxOutputssList.Count)
            {
                List<Block> rejecteds = new List<Block>();
                for (int i = prevTxOutputssList.Count; i < blocksList.Count; i++)
                    rejecteds.Add(blocksList[i]);

                return new UpdateChainInnerReturn(UpdateChainReturnType.updatedAndRejected, blocksCurrent.GetForward((int)(blocksList[prevTxOutputssList.Count].Index - blockManager.headBlockIndex)), rejecteds);
            }

            return new UpdateChainInnerReturn(UpdateChainReturnType.updated);
        }

        private void UpdateBlockChainDB(List<Block> blocksList, List<TransactionOutput[][]> prevTxOutputssList, List<Block> mainBlocksList, List<TransactionOutput[][]> mainPrevTxOutputssList)
        {
            udb.Open();

            bcadb.Create();

            for (int i = mainBlocksList.Count - 1; i >= 0; i--)
            {
                blockManager.DeleteMainBlock(mainBlocksList[i].Index);
                utxoManager.RevertBlock(mainBlocksList[i], mainPrevTxOutputssList[i]);

                if (pendingBlocks[blocksCurrent.value] == null)
                    pendingBlocks[blocksCurrent.value] = new Dictionary<Creahash, Block>();
                pendingBlocks[blocksCurrent.value].Add(mainBlocksList[i].Id, mainBlocksList[i]);

                blocksCurrent.Previous();
            }

            for (int i = 0; i < prevTxOutputssList.Count; i++)
            {
                blockManager.AddMainBlock(blocksList[i]);
                utxoManager.ApplyBlock(blocksList[i], prevTxOutputssList[i]);

                blocksCurrent.Next();

                if (pendingBlocks[blocksCurrent.value] != null && pendingBlocks[blocksCurrent.value].Keys.Contains(blocksList[i].Id))
                    pendingBlocks[blocksCurrent.value].Remove(blocksList[i].Id);

                int nextMaxPosition = blocksCurrent.GetForward((int)maxBlockIndexMargin);

                rejectedBlocks[nextMaxPosition] = null;
                pendingBlocks[nextMaxPosition] = null;
            }

            utxoManager.SaveUFPTemp();

            bcadb.Delete();

            udb.Close();
        }

        private void RetrieveTransactionTransitionForward(Block block, TransactionOutput[][] prevTxOutss, Dictionary<Sha256Ripemd160Hash, List<Utxo>> addedUtxos, Dictionary<Sha256Ripemd160Hash, List<Utxo>> removedUtxos)
        {
            for (int i = 0; i < block.Transactions.Length; i++)
            {
                for (int j = 0; j < block.Transactions[i].TxInputs.Length; j++)
                    AddedRemoveAndRemovedAdd(prevTxOutss[i][j].Address, block.Transactions[i].TxInputs[j].PrevTxBlockIndex, block.Transactions[i].TxInputs[j].PrevTxIndex, block.Transactions[i].TxInputs[j].PrevTxOutputIndex, prevTxOutss[i][j].Amount, addedUtxos, removedUtxos);
                for (int j = 0; j < block.Transactions[i].TxOutputs.Length; j++)
                    RemovedRemoveAndAddedAdd(block.Transactions[i].TxOutputs[j].Address, block.Index, i, j, block.Transactions[i].TxOutputs[j].Amount, addedUtxos, removedUtxos);
            }
        }

        private void RetrieveTransactionTransitionBackward(Block block, TransactionOutput[][] prevTxOutss, Dictionary<Sha256Ripemd160Hash, List<Utxo>> addedUtxos, Dictionary<Sha256Ripemd160Hash, List<Utxo>> removedUtxos)
        {
            for (int i = 0; i < block.Transactions.Length; i++)
            {
                for (int j = 0; j < block.Transactions[i].TxInputs.Length; j++)
                    RemovedRemoveAndAddedAdd(prevTxOutss[i][j].Address, block.Transactions[i].TxInputs[j].PrevTxBlockIndex, block.Transactions[i].TxInputs[j].PrevTxIndex, block.Transactions[i].TxInputs[j].PrevTxOutputIndex, prevTxOutss[i][j].Amount, addedUtxos, removedUtxos);
                for (int j = 0; j < block.Transactions[i].TxOutputs.Length; j++)
                    AddedRemoveAndRemovedAdd(block.Transactions[i].TxOutputs[j].Address, block.Index, i, j, block.Transactions[i].TxOutputs[j].Amount, addedUtxos, removedUtxos);
            }
        }

        //後退する場合の取引入力（被参照取引出力）は削除集合から削除 or 追加集合に追加
        //前進する場合の取引出力は削除集合から削除 or 追加集合に追加
        private void RemovedRemoveAndAddedAdd(Sha256Ripemd160Hash address, long bindex, int txindex, int txoutindex, CurrencyUnit amount, Dictionary<Sha256Ripemd160Hash, List<Utxo>> addedUtxos, Dictionary<Sha256Ripemd160Hash, List<Utxo>> removedUtxos)
        {
            if (removedUtxos.Keys.Contains(address))
            {
                Utxo utxo = removedUtxos[address].Where((elem) => elem.IsMatch(bindex, txindex, txoutindex)).FirstOrDefault();
                if (utxo != null)
                {
                    removedUtxos[address].Remove(utxo);

                    return;
                }
            }

            List<Utxo> utxos = null;
            if (addedUtxos.Keys.Contains(address))
                utxos = addedUtxos[address];
            else
                addedUtxos.Add(address, utxos = new List<Utxo>());
            utxos.Add(new Utxo(bindex, txindex, txoutindex, amount));
        }

        //後退する場合の取引出力は追加集合から削除 or 削除集合に追加
        //前進する場合の取引入力（被参照取引出力）は追加集合から削除 or 削除集合に追加
        private void AddedRemoveAndRemovedAdd(Sha256Ripemd160Hash address, long bindex, int txindex, int txoutindex, CurrencyUnit amount, Dictionary<Sha256Ripemd160Hash, List<Utxo>> addedUtxos, Dictionary<Sha256Ripemd160Hash, List<Utxo>> removedUtxos)
        {
            if (addedUtxos.Keys.Contains(address))
            {
                Utxo utxo = addedUtxos[address].Where((elem) => elem.IsMatch(bindex, txindex, txoutindex)).FirstOrDefault();
                if (utxo != null)
                {
                    addedUtxos[address].Remove(utxo);

                    return;
                }
            }

            List<Utxo> utxos = null;
            if (removedUtxos.Keys.Contains(address))
                utxos = removedUtxos[address];
            else
                removedUtxos.Add(address, utxos = new List<Utxo>());
            utxos.Add(new Utxo(bindex, txindex, txoutindex, amount));
        }

        private TransactionOutput[][] GetPrevTxOutputss(Block block, Func<long, TransactionalBlock> _bindexToBlock)
        {
            TransactionOutput[][] prevTxOutputss = new TransactionOutput[block.Transactions.Length][];
            for (int i = 0; i < block.Transactions.Length; i++)
            {
                prevTxOutputss[i] = new TransactionOutput[block.Transactions[i].TxInputs.Length];
                for (int j = 0; j < block.Transactions[i].TxInputs.Length; j++)
                {
                    Block prevBlock = _bindexToBlock(block.Transactions[i].TxInputs[j].PrevTxBlockIndex);

                    if (prevBlock == null)
                        return null;
                    if (block.Transactions[i].TxInputs[j].PrevTxIndex >= prevBlock.Transactions.Length)
                        return null;

                    Transaction prevTx = prevBlock.Transactions[block.Transactions[i].TxInputs[j].PrevTxIndex];

                    if (block.Transactions[i].TxInputs[j].PrevTxOutputIndex >= prevTx.TxOutputs.Length)
                        return null;

                    prevTxOutputss[i][j] = prevTx.TxOutputs[block.Transactions[i].TxInputs[j].PrevTxOutputIndex];
                }
            }
            return prevTxOutputss;
        }

        private bool VerifyUtxo(Block block, TransactionOutput[][] prevTxOutss, Dictionary<Sha256Ripemd160Hash, List<Utxo>> addedUtxos, Dictionary<Sha256Ripemd160Hash, List<Utxo>> removedUtxos)
        {
            Dictionary<Sha256Ripemd160Hash, List<Utxo>> removedUtxos2 = new Dictionary<Sha256Ripemd160Hash, List<Utxo>>();

            for (int i = 0; i < block.Transactions.Length; i++)
                for (int j = 0; j < block.Transactions[i].TxInputs.Length; j++)
                {
                    if (removedUtxos2.Keys.Contains(prevTxOutss[i][j].Address) && removedUtxos2[prevTxOutss[i][j].Address].Where((elem) => elem.IsMatch(block.Transactions[i].TxInputs[j].PrevTxBlockIndex, block.Transactions[i].TxInputs[j].PrevTxIndex, block.Transactions[i].TxInputs[j].PrevTxOutputIndex)).FirstOrDefault() != null)
                        return false;
                    if (removedUtxos.Keys.Contains(prevTxOutss[i][j].Address) && removedUtxos[prevTxOutss[i][j].Address].Where((elem) => elem.IsMatch(block.Transactions[i].TxInputs[j].PrevTxBlockIndex, block.Transactions[i].TxInputs[j].PrevTxIndex, block.Transactions[i].TxInputs[j].PrevTxOutputIndex)).FirstOrDefault() != null)
                        return false;

                    List<Utxo> utxos;
                    if (!removedUtxos2.Keys.Contains(prevTxOutss[i][j].Address))
                        removedUtxos2.Add(prevTxOutss[i][j].Address, utxos = new List<Utxo>());
                    else
                        utxos = removedUtxos2[prevTxOutss[i][j].Address];
                    utxos.Add(new Utxo(block.Transactions[i].TxInputs[j].PrevTxBlockIndex, block.Transactions[i].TxInputs[j].PrevTxIndex, block.Transactions[i].TxInputs[j].PrevTxOutputIndex, prevTxOutss[i][j].Amount));

                    if (addedUtxos.Keys.Contains(prevTxOutss[i][j].Address) && addedUtxos[prevTxOutss[i][j].Address].Where((elem) => elem.IsMatch(block.Transactions[i].TxInputs[j].PrevTxBlockIndex, block.Transactions[i].TxInputs[j].PrevTxIndex, block.Transactions[i].TxInputs[j].PrevTxOutputIndex)).FirstOrDefault() != null)
                        continue;

                    if (utxoManager.FindUtxo(prevTxOutss[i][j].Address, block.Transactions[i].TxInputs[j].PrevTxBlockIndex, block.Transactions[i].TxInputs[j].PrevTxIndex, block.Transactions[i].TxInputs[j].PrevTxOutputIndex) == null)
                        return false;
                }

            return true;
        }
    }

    //2014/12/04 試験済
    public class BlockManager
    {
        public BlockManager(BlockManagerDB _bmdb, BlockDB _bdb, BlockFilePointersDB _bfpdb, int _mainBlocksRetain, int _oldBlocksRetain, long _mainBlockFinalization)
        {
            if (_mainBlocksRetain < _mainBlockFinalization)
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
        }

        private static readonly long blockFileCapacity = 100000;

        public readonly int mainBlocksRetain;
        public readonly int oldBlocksRetain;
        public readonly long mainBlockFinalization;

        private readonly BlockManagerDB bmdb;
        private readonly BlockDB bdb;
        private readonly BlockFilePointersDB bfpdb;

        private readonly BlockManagerData bmd;

        public readonly Block[] mainBlocks;
        //<未改良>どこに保存したかも保持しておかないと意味がない
        public readonly List<Block>[] sideBlocks;
        public readonly CirculatedInteger mainBlocksCurrent;

        public readonly Dictionary<long, Block> oldBlocks;

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

        //<未改良>sideBlocksを使うように
        public void AddMainBlock(Block block)
        {
            if (block.Index != bmd.headBlockIndex + 1)
                throw new InvalidOperationException();

            mainBlocksCurrent.Next();

            mainBlocks[mainBlocksCurrent.value] = block;
            sideBlocks[mainBlocksCurrent.value] = new List<Block>();

            bmd.headBlockIndex = block.Index;
            bmd.finalizedBlockIndex = bmd.headBlockIndex < mainBlockFinalization ? 0 : bmd.headBlockIndex - mainBlockFinalization;

            bfpdb.UpdateBlockFilePointerData(block.Index, bdb.AddBlockData(block.Index / blockFileCapacity, SHAREDDATA.ToBinary<Block>(block)));
            bmdb.UpdateData(bmd.ToBinary());
        }

        public void AddMainBlocks(Block[] blocks)
        {

        }

        public Block GetHeadBlock() { return GetMainBlock(bmd.headBlockIndex); }

        public Block GetMainBlock(long blockIndex)
        {
            if (blockIndex > bmd.headBlockIndex)
                throw new InvalidOperationException();
            if (blockIndex < 0)
                throw new InvalidOperationException();

            if (blockIndex > bmd.headBlockIndex - mainBlocksRetain)
            {
                int index = mainBlocksCurrent.GetBackward((int)(bmd.headBlockIndex - blockIndex));
                if (mainBlocks[index] == null)
                    mainBlocks[index] = SHAREDDATA.FromBinary<Block>(bdb.GetBlockData(blockIndex / blockFileCapacity, bfpdb.GetBlockFilePointerData(blockIndex)));

                if (mainBlocks[index].Index != blockIndex)
                    throw new InvalidOperationException();

                return mainBlocks[index];
            }

            if (oldBlocks.Keys.Contains(blockIndex))
                return oldBlocks[blockIndex];

            Block block = SHAREDDATA.FromBinary<Block>(bdb.GetBlockData(blockIndex / blockFileCapacity, bfpdb.GetBlockFilePointerData(blockIndex)));

            if (block.Index != blockIndex)
                throw new InvalidOperationException();

            oldBlocks.Add(blockIndex, block);

            while (oldBlocks.Count > oldBlocksRetain)
                oldBlocks.Remove(oldBlocks.First().Key);

            return block;
        }

        //<未改良>一括取得
        //2014/12/01 試験除外　一括取得を実装したら試験する
        public Block[] GetMainBlocks(long[] blockIndexes)
        {
            Block[] blocks = new Block[blockIndexes.Length];
            for (int i = 0; i < blocks.Length; i++)
                blocks[i] = GetMainBlock(blockIndexes[i]);
            return blocks;
        }

        //<未改良>一括取得
        //2014/12/01 試験除外　一括取得を実装したら試験する
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

    //2014/12/04 試験済
    public class UtxoManager
    {
        public UtxoManager(UtxoFileAccessDB _ufadb, UtxoFilePointersDB _ufpdb, UtxoFilePointersTempDB _ufptempdb, UtxoDB _udb)
        {
            ufadb = _ufadb;
            ufpdb = _ufpdb;
            ufptempdb = _ufptempdb;
            udb = _udb;

            ufp = ufpdb.GetData().Pipe((bytes) => bytes.Length == 0 ? new UtxoFilePointers() : SHAREDDATA.FromBinary<UtxoFilePointers>(bytes));
            ufptemp = ufptempdb.GetData().Pipe((bytes) => bytes.Length == 0 ? new UtxoFilePointers() : SHAREDDATA.FromBinary<UtxoFilePointers>(bytes));

            foreach (var ufpitem in ufptemp.GetAll())
                ufp.AddOrUpdate(ufpitem.Key, ufpitem.Value);

            ufadb.Create();
            ufpdb.UpdateData(ufp.ToBinary());
            ufptempdb.Delete();
            ufadb.Delete();

            ufptemp = new UtxoFilePointers();
        }

        private static readonly int FirstUtxoFileItemSize = 16;

        private readonly UtxoFileAccessDB ufadb;
        private readonly UtxoFilePointersDB ufpdb;
        private readonly UtxoFilePointersTempDB ufptempdb;
        private readonly UtxoDB udb;

        private readonly UtxoFilePointers ufp;
        private readonly UtxoFilePointers ufptemp;

        public void AddUtxo(Sha256Ripemd160Hash address, long blockIndex, int txIndex, int txOutIndex, CurrencyUnit amount)
        {
            long? position = ufptemp.Get(address);
            if (!position.HasValue)
                position = ufp.Get(address);

            bool isProcessed = false;

            bool isFirst = true;
            long? firstPosition = null;
            int? firstSize = null;

            UtxoFileItem ufi = null;
            while (true)
            {
                if (!position.HasValue)
                    ufi = new UtxoFileItem(FirstUtxoFileItemSize);
                else if (position == -1)
                    ufi = new UtxoFileItem(firstSize.Value * 2);
                else
                    ufi = SHAREDDATA.FromBinary<UtxoFileItem>(udb.GetUtxoData(position.Value));

                if (isFirst)
                {
                    firstPosition = position;
                    firstSize = ufi.Size;

                    isFirst = false;
                }

                for (int i = 0; i < ufi.Size; i++)
                    if (ufi.utxos[i].IsEmpty)
                    {
                        ufi.utxos[i].Reset(blockIndex, txIndex, txOutIndex, amount);

                        if (!position.HasValue)
                            ufptemp.Add(address, udb.AddUtxoData(ufi.ToBinary()));
                        else if (position == -1)
                        {
                            ufi.Update(firstPosition.Value);
                            ufptemp.Update(address, udb.AddUtxoData(ufi.ToBinary()));
                        }
                        else
                            udb.UpdateUtxoData(position.Value, ufi.ToBinary());

                        isProcessed = true;

                        break;
                    }

                if (isProcessed)
                    break;

                position = ufi.nextPosition;
            }
        }

        public void RemoveUtxo(Sha256Ripemd160Hash address, long blockIndex, int txIndex, int txOutIndex)
        {
            long? position = ufptemp.Get(address);
            if (!position.HasValue)
                position = ufp.Get(address);

            if (!position.HasValue)
                throw new InvalidOperationException("utxo_address_not_exist");

            bool isProcessed = false;

            while (position.Value != -1)
            {
                UtxoFileItem ufi = SHAREDDATA.FromBinary<UtxoFileItem>(udb.GetUtxoData(position.Value));

                for (int i = 0; i < ufi.Size; i++)
                    if (ufi.utxos[i].IsMatch(blockIndex, txIndex, txOutIndex))
                    {
                        ufi.utxos[i].Empty();

                        udb.UpdateUtxoData(position.Value, ufi.ToBinary());

                        isProcessed = true;

                        break;
                    }

                if (isProcessed)
                    break;

                position = ufi.nextPosition;
            }

            if (!isProcessed)
                throw new InvalidOperationException("utxo_not_exist");
        }

        public void SaveUFPTemp()
        {
            ufptempdb.UpdateData(ufptemp.ToBinary());
        }

        public Utxo FindUtxo(Sha256Ripemd160Hash address, long blockIndex, int txIndex, int txOutIndex)
        {
            long? position = ufptemp.Get(address);
            if (!position.HasValue)
                position = ufp.Get(address);

            if (!position.HasValue)
                return null;

            while (position.Value != -1)
            {
                UtxoFileItem ufi = SHAREDDATA.FromBinary<UtxoFileItem>(udb.GetUtxoData(position.Value));

                for (int i = 0; i < ufi.Size; i++)
                    if (ufi.utxos[i].IsMatch(blockIndex, txIndex, txOutIndex))
                        return ufi.utxos[i];

                position = ufi.nextPosition;
            }

            return null;
        }

        //prevTxOutssは全ての取引に対するものを含んでいなければならないことに注意
        //貨幣移動取引のみならず貨幣生成取引に対するもの（貨幣生成取引の場合取引入力は0個であるため空になる筈）も含んでいなければならない
        public void ApplyBlock(Block block, TransactionOutput[][] prevTxOutss)
        {
            if (block.Transactions.Length != prevTxOutss.Length)
                throw new InvalidOperationException("apply_blk_num_of_txs_mismatch");

            block.Transactions.ForEach((i, tx) =>
            {
                if (tx.TxInputs.Length != prevTxOutss[i].Length)
                    throw new InvalidOperationException("apply_blk_num_of_txin_and_txout_mismatch");

                tx.TxInputs.ForEach((j, txIn) => RemoveUtxo(prevTxOutss[i][j].Address, txIn.PrevTxBlockIndex, txIn.PrevTxIndex, txIn.PrevTxOutputIndex));
                tx.TxOutputs.ForEach((j, txOut) => AddUtxo(txOut.Address, block.Index, i, j, txOut.Amount));
            });
        }

        //prevTxOutssは全ての取引に対するものを含んでいなければならないことに注意
        //貨幣移動取引のみならず貨幣生成取引に対するもの（貨幣生成取引の場合取引入力は0個であるため空になる筈）も含んでいなければならない
        public void RevertBlock(Block block, TransactionOutput[][] prevTxOutss)
        {
            if (block.Transactions.Length != prevTxOutss.Length)
                throw new InvalidOperationException("revert_blk_num_of_txs_mismatch");

            block.Transactions.ForEach((i, tx) =>
            {
                if (tx.TxInputs.Length != prevTxOutss[i].Length)
                    throw new InvalidOperationException("revert_blk_num_of_txin_and_txout_mismatch");

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

                File.WriteAllText(path, string.Empty);
            }
            catch (Exception ex)
            {
                throw new ApplicationException("fatal:utxos_access", ex);
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
                throw new ApplicationException("fatal:utxos_access", ex);
            }
        }

        public string GetPath() { return System.IO.Path.Combine(pathBase, filenameBase); }
    }

    public class BlockchainAccessDB : DATABASEBASE
    {
        public BlockchainAccessDB(string _pathBase) : base(_pathBase) { }

        protected override int version { get { return 0; } }

#if TEST
        protected override string filenameBase { get { return "blkchain_access_test" + version.ToString(); } }
#else
        protected override string filenameBase { get { return "utxos" + version.ToString(); } }
#endif

        public void Create()
        {
            try
            {
                string path = GetPath();

                if (File.Exists(path))
                    throw new InvalidOperationException("blkchain_access_file_exist");

                File.WriteAllText(path, string.Empty);
            }
            catch (Exception ex)
            {
                throw new ApplicationException("fatal:blkchain_access", ex);
            }
        }

        public void Delete()
        {
            try
            {
                string path = GetPath();

                if (!File.Exists(path))
                    throw new InvalidOperationException("blkchain_access_file_not_exist");

                File.Delete(path);
            }
            catch (Exception ex)
            {
                throw new ApplicationException("fatal:blkchain_access", ex);
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

        public void Delete()
        {
            string path = GetPath();

            if (File.Exists(path))
                File.Delete(path);
        }
    }

    //2014/11/26 試験済
    public class UtxoDB : DATABASEBASE
    {
        public UtxoDB(string _pathBase) : base(_pathBase) { }

        private FileStream fs;

        protected override int version { get { return 0; } }

#if TEST
        protected override string filenameBase { get { return "utxos_test" + version.ToString(); } }
#else
        protected override string filenameBase { get { return "utxos" + version.ToString(); } }
#endif

        public void Open()
        {
            if (fs != null)
                throw new InvalidOperationException("utxodb_open_twice");

            fs = new FileStream(GetPath(), FileMode.OpenOrCreate, FileAccess.ReadWrite);
        }

        public void Close()
        {
            if (fs == null)
                throw new InvalidOperationException("utxodb_close_twice_or_first");

            fs.Close();
            fs = null;
        }

        public void Flush()
        {
            if (fs == null)
                throw new InvalidOperationException("utxodb_not_opened");

            fs.Flush();
        }

        public byte[] GetUtxoData(long position)
        {
            if (fs == null)
                throw new InvalidOperationException("utxodb_not_opened");

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

        public long AddUtxoData(byte[] utxoData)
        {
            if (fs == null)
                throw new InvalidOperationException("utxodb_not_opened");

            long position = fs.Length;

            fs.Seek(fs.Length, SeekOrigin.Begin);
            fs.Write(BitConverter.GetBytes(utxoData.Length), 0, 4);
            fs.Write(utxoData, 0, utxoData.Length);

            return position;
        }

        public void UpdateUtxoData(long position, byte[] utxoData)
        {
            if (fs == null)
                throw new InvalidOperationException("utxodb_not_opened");

            fs.Seek(position, SeekOrigin.Begin);

            byte[] lengthBytes = new byte[4];
            fs.Read(lengthBytes, 0, 4);
            int length = BitConverter.ToInt32(lengthBytes, 0);

            if (utxoData.Length != length)
                throw new InvalidOperationException();

            fs.Write(utxoData, 0, utxoData.Length);
        }

        public string GetPath() { return System.IO.Path.Combine(pathBase, filenameBase); }
    }

    //2014/12/01 試験済
    public class BlockFilePointersDB : DATABASEBASE
    {
        public BlockFilePointersDB(string _pathBase) : base(_pathBase) { }

        protected override int version { get { return 0; } }

#if TEST
        protected override string filenameBase { get { return "blks_index_test" + version.ToString(); } }
#else
        protected override string filenameBase { get { return "blks_index_test" + version.ToString(); } }
#endif

        public long GetBlockFilePointerData(long blockIndex)
        {
            using (FileStream fs = new FileStream(GetPath(), FileMode.OpenOrCreate, FileAccess.Read))
            {
                if (fs.Length % 8 != 0)
                    throw new InvalidOperationException("fatal:bfpdb");

                long position = blockIndex * 8;

                if (position >= fs.Length)
                    return -1;

                fs.Seek(position, SeekOrigin.Begin);

                byte[] blockPointerData = new byte[8];
                fs.Read(blockPointerData, 0, 8);
                return BitConverter.ToInt64(blockPointerData, 0);
            }
        }

        public void UpdateBlockFilePointerData(long blockIndex, long blockFilePointerData)
        {
            using (FileStream fs = new FileStream(GetPath(), FileMode.OpenOrCreate, FileAccess.Write))
            {
                if (fs.Length % 8 != 0)
                    throw new InvalidOperationException("fatal:bfpdb");

                long position = blockIndex * 8;

                if (position >= fs.Length)
                {
                    fs.Seek(fs.Length, SeekOrigin.Begin);
                    while (position > fs.Length)
                        fs.Write(BitConverter.GetBytes((long)-1), 0, 8);
                    fs.Write(BitConverter.GetBytes(blockFilePointerData), 0, 8);
                }
                else
                {
                    fs.Seek(position, SeekOrigin.Begin);
                    fs.Write(BitConverter.GetBytes(blockFilePointerData), 0, 8);
                }
            }
        }

        public string GetPath() { return System.IO.Path.Combine(pathBase, filenameBase); }
    }

    //2014/12/01 試験済
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
                    positions[i] = AddBlockData(fs, blockFileIndex, blockDatas[i]);

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

        public string GetPath(long blockFileIndex) { return System.IO.Path.Combine(pathBase, filenameBase + blockFileIndex.ToString()); }
    }

    #endregion



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