﻿using System;
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
        public override Difficulty<X15Hash> Diff { get { return null; } }
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

            blockManager = new BlockManager(bmdb, bdb, bfpdb);
            utxoManager = new UtxoManager(ufpdb, ufptempdb, udb);

            pendingBlocks = new Dictionary<long, Dictionary<X15Hash, Block>>();
            rejectedBlocks = new Dictionary<long, Dictionary<X15Hash, Block>>();
        }

        private static readonly long maxBlockIndexMargin = 100;
        private static readonly int pendingBlocksCapacity = 200;
        private static readonly int rejectedBlockscapacity = 1000;

        private readonly BlockManagerDB bmdb;
        private readonly BlockDB bdb;
        private readonly BlockFilePointersDB bfpdb;
        private readonly UtxoFilePointersDB ufpdb;
        private readonly UtxoFilePointersTempDB ufptempdb;
        private readonly UtxoDB udb;

        private readonly BlockManager blockManager;
        private readonly UtxoManager utxoManager;

        private readonly Dictionary<long, Dictionary<X15Hash, Block>> pendingBlocks;
        private readonly Dictionary<long, Dictionary<X15Hash, Block>> rejectedBlocks;

        public void UpdateChain(Block block)
        {
            long minBlockIndex = blockManager.headBlockIndex - BlockManager.mainBlockFinalization;
            long maxBlockIndex = blockManager.headBlockIndex + maxBlockIndexMargin;

            if (block.Index > maxBlockIndex)
                throw new InvalidOperationException();
            if (block.Index <= minBlockIndex)
                throw new InvalidOperationException();

            if (rejectedBlocks.Keys.Contains(block.Index) && rejectedBlocks[block.Index].Keys.Contains(block.Id))
                return;
            Dictionary<X15Hash, Block> pending = pendingBlocks.Keys.Contains(block.Index) ? pendingBlocks[block.Index] : null;
            if (pending != null && pending.Keys.Contains(block.Id))
                return;

            Block currentBlock = block;
            List<Block> candidates = new List<Block>() { block };
            double mainDiff = 0.0;
            double brunchDiff = 0.0;
            while (true)
            {
                long prevIndex = currentBlock.Index - 1;

                brunchDiff += currentBlock.Diff.Diff;

                Block prevMainBlock = blockManager.GetMainBlock(prevIndex);
                if (prevMainBlock.Id.Equals(currentBlock.PrevId))
                    break;

                mainDiff += prevMainBlock.Diff.Diff;

                if (prevIndex <= minBlockIndex)
                {
                    if (pending == null)
                        pendingBlocks.Add(block.Index, pending = new Dictionary<X15Hash, Block>());
                    pending.Add(block.Id, block);

                    return;
                }

                Block prevBlock = null;
                foreach (var kvp in pendingBlocks.Keys.Contains(prevIndex) ? pendingBlocks[prevIndex] : new Dictionary<X15Hash, Block>() { })
                    if (kvp.Value.Id.Equals(currentBlock.PrevId))
                        prevBlock = kvp.Value;

                if (prevBlock == null)
                {
                    if (pending == null)
                        pendingBlocks.Add(block.Index, pending = new Dictionary<X15Hash, Block>());
                    pending.Add(block.Id, block);

                    return;
                }

                candidates.Insert(0, prevBlock);

                currentBlock = prevBlock;
            }

            currentBlock = block;
            while (currentBlock.Index <= maxBlockIndex)
            {
                long nextIndex = currentBlock.Index + 1;

                Block nextBlock = null;
                foreach (var kvp in pendingBlocks.Keys.Contains(nextIndex) ? pendingBlocks[nextIndex] : new Dictionary<X15Hash, Block>() { })
                    if (kvp.Value.PrevId.Equals(currentBlock.Id) && (nextBlock == null || kvp.Value.Diff.Diff > nextBlock.Diff.Diff))
                        nextBlock = kvp.Value;

                if (nextBlock == null)
                    break;

                candidates.Add(nextBlock);

                currentBlock = nextBlock;

                brunchDiff += currentBlock.Diff.Diff;
            }

            if (mainDiff >= brunchDiff)
            {
                if (pending == null)
                    pendingBlocks.Add(block.Index, pending = new Dictionary<X15Hash, Block>());
                pending.Add(block.Id, block);

                return;
            }







            //if (block.Index > blockManager.headBlockIndex + 1)
            //{
            //    if (rejectedBlocks.Keys.Contains(block.Index) && rejectedBlocks[block.Index].Keys.Contains(block.Id))
            //        return;

            //    Dictionary<X15Hash, Block> pending = null;
            //    if (pendingBlocks.Keys.Contains(block.Index))
            //    {
            //        pending = pendingBlocks[block.Index];
            //        if (pending.Keys.Contains(block.Id))
            //            return;
            //    }
            //    else
            //    {
            //        pendingBlocks.Add(block.Index, pending = new Dictionary<X15Hash, Block>());

            //        while (pendingBlocks.Count > pendingBlocksCapacity)
            //            pendingBlocks.Remove(pendingBlocks.First().Key);
            //    }

            //    pending.Add(block.Id, block);

            //    return;
            //}

            //if (block.Index == blockManager.headBlockIndex + 1)
            //{
            //    Func<Block, Dictionary<X15Hash, Block>, bool> _VerifyBlock = (verifiedBlock, rejected2) =>
            //    {
            //        return VerifyBlock(verifiedBlock).Pipe((valid) =>
            //        {
            //            if (valid)
            //            {
            //                blockManager.AddMainBlock(verifiedBlock);
            //                utxoManager.ApplyBlock(verifiedBlock);
            //            }
            //            else
            //            {
            //                if (rejected2 == null)
            //                {
            //                    rejectedBlocks.Add(verifiedBlock.Index, rejected2 = new Dictionary<X15Hash, Block>());

            //                    while (rejectedBlocks.Count > rejectedBlockscapacity)
            //                        rejectedBlocks.Remove(rejectedBlocks.First().Key);
            //                }

            //                rejected2.Add(verifiedBlock.Id, verifiedBlock);
            //            }
            //        });
            //    };

            //    Dictionary<X15Hash, Block> rejected = null;
            //    if (rejectedBlocks.Keys.Contains(block.Index))
            //    {
            //        rejected = rejectedBlocks[block.Index];
            //        if (rejected.Keys.Contains(block.Id))
            //            return;
            //    }

            //    if (!_VerifyBlock(block, rejected))
            //        return;

            //    for (int i = 1; i <= maxBlockIndexMargin; i++)
            //    {
            //        long nextBlockIndex = block.Index + i;

            //        if (!pendingBlocks.Keys.Contains(nextBlockIndex))
            //            break;

            //        Dictionary<X15Hash, Block> pending = pendingBlocks[nextBlockIndex];
            //        Block nextBlock = null;
            //        foreach (var p in pending)
            //            if (p.Value.PrevId.Equals(block.Id))
            //                nextBlock = p.Value;

            //        if (nextBlock == null)
            //            break;

            //        if (pending.Count == 1)
            //            pendingBlocks.Remove(nextBlockIndex);
            //        else
            //            pending.Remove(nextBlock.Id);

            //        Dictionary<X15Hash, Block> rejected2 = null;
            //        if (rejectedBlocks.Keys.Contains(nextBlockIndex))
            //            rejected2 = rejectedBlocks[nextBlockIndex];

            //        if (!_VerifyBlock(nextBlock, rejected2))
            //            break;
            //    }

            //    return;
            //}
        }

        private bool VerifyBlock(Block block)
        {
            throw new NotImplementedException();
        }
    }

    public class BlockManager
    {
        public BlockManager(BlockManagerDB _bmdb, BlockDB _bdb, BlockFilePointersDB _bfpdb)
        {
            if (mainBlocksRetain < mainBlockFinalization)
                throw new InvalidOperationException();

            bmdb = _bmdb;
            bdb = _bdb;
            bfpdb = _bfpdb;

            bmd = bmdb.GetData().Pipe((bmdBytes) => bmdBytes.Length != 0 ? SHAREDDATA.FromBinary<BlockManagerData>(bmdBytes) : new BlockManagerData());

            mainBlocks = new Block[mainBlocksRetain];
            sideBlocks = new List<Block>[mainBlocksRetain];
            mainBlocksCurrent = new CirculatedInteger(mainBlocksRetain);

            oldBlocks = new Dictionary<long, Block>();

            if (bmd.headBlockIndex == -1)
                AddMainBlock(new GenesisBlock());
        }

        public static readonly int mainBlocksRetain = 1000;
        public static readonly int oldBlocksRetain = 1000;
        public static readonly int mainBlockFinalization = 300;

        public static readonly long blockFileCapacity = 100000;

        private readonly BlockManagerDB bmdb;
        private readonly BlockDB bdb;
        private readonly BlockFilePointersDB bfpdb;
        private readonly BlockManagerData bmd;

        private readonly Block[] mainBlocks;
        private readonly List<Block>[] sideBlocks;
        private readonly CirculatedInteger mainBlocksCurrent;

        private readonly Dictionary<long, Block> oldBlocks;

        public long headBlockIndex { get { return bmd.headBlockIndex; } }

        public void DeleteMainBlock(long blockIndex)
        {
            if (blockIndex != bmd.headBlockIndex)
                throw new InvalidOperationException();
            if (blockIndex <= bmd.finalizedBlockIndex)
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
            if (block.Index == bmd.headBlockIndex + 1)
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

            ufpdb.UpdateData(ufp.ToBinary());
        }

        private static readonly int FirstUtxoFileItemSize = 16;

        private readonly UtxoFilePointersDB ufpdb;
        private readonly UtxoFilePointersTempDB ufptempdb;
        private readonly UtxoFilePointers ufp;
        private readonly UtxoFilePointers ufptemp;
        private readonly UtxoDB udb;

        private void AddUtxo(Sha256Ripemd160Hash address, long blockIndex, int txIndex, int txOutIndex, CurrencyUnit amount)
        {
            long? prevPosition = null;
            long? position = ufptemp.Get(address);
            if (!position.HasValue)
                position = ufp.Get(address);

            bool isProcessed = false;

            UtxoFileItem ufi = null;
            while (!isProcessed)
            {
                if (!position.HasValue)
                    ufi = new UtxoFileItem(FirstUtxoFileItemSize);
                else if (position == -1)
                    ufi = new UtxoFileItem(ufi.Size * 2);
                else
                    ufi = SHAREDDATA.FromBinary<UtxoFileItem>(udb.GetUtxoData(position.Value));

                for (int k = 0; k < ufi.Size && !isProcessed; k++)
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
                    }

                prevPosition = position;
                position = ufi.nextPosition;
            }
        }

        private void RemoveUtxo(Sha256Ripemd160Hash address, long blockIndex, int txIndex, int txOutIndex)
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

        public void ApplyBlock(Block block)
        {
            block.Transactions.ForEach((i, tx) =>
            {
                tx.TxInputs.ForEach((j, txIn) => RemoveUtxo(txIn.PrevTxOutputAddress, txIn.PrevTxBlockIndex, txIn.PrevTxIndex, txIn.PrevTxOutputIndex));
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

                tx.TxInputs.ForEach((j, txIn) => AddUtxo(txIn.PrevTxOutputAddress, txIn.PrevTxBlockIndex, txIn.PrevTxIndex, txIn.PrevTxOutputIndex, prevTxOutss[i][j].Amount));
                tx.TxOutputs.ForEach((j, txOut) => RemoveUtxo(txOut.Address, block.Index, i, j));
            });
        }
    }

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
                    new MainDataInfomation(typeof(Utxo[]), () => utxos, (o) => utxos = (Utxo[])o),
                    new MainDataInfomation(typeof(long), () => nextPosition, (o) => nextPosition = (long)o),
                };
            }
        }

        public void Update(long nextPositionNew) { nextPosition = nextPositionNew; }
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

        private string GetPath() { return System.IO.Path.Combine(pathBase, filenameBase); }
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