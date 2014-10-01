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
}

namespace New
{
    using CREA2014;

    public class TransactionInput : SHAREDDATA
    {
        public TransactionInput() : base(0) { }

        public void LoadVersion0(long _prevTxBlockIndex, int _prevTxIndex, int _prevTxOutputIndex, Ecdsa256PubKey _senderPubKey)
        {
            Version = 0;

            LoadCommon(_prevTxBlockIndex, _prevTxIndex, _prevTxOutputIndex);

            ecdsa256PubKey = _senderPubKey;
        }

        public void LoadVersion1(long _prevTxBlockIndex, int _prevTxIndex, int _prevTxOutputIndex)
        {
            Version = 1;

            LoadCommon(_prevTxBlockIndex, _prevTxIndex, _prevTxOutputIndex);
        }

        private void LoadCommon(long _prevTxBlockIndex, int _prevTxIndex, int _prevTxOutputIndex)
        {
            prevTxBlockIndex = _prevTxBlockIndex;
            prevTxIndex = _prevTxIndex;
            prevTxOutputIndex = _prevTxOutputIndex;
        }

        private long prevTxBlockIndex;
        private int prevTxIndex;
        private int prevTxOutputIndex;
        private Ecdsa256Signature ecdsa256Signature;
        private Secp256k1Signature secp256k1Signature;
        private Ecdsa256PubKey ecdsa256PubKey;

        public long PrevTxBlockIndex { get { return prevTxBlockIndex; } }
        public int PrevTxIndex { get { return prevTxIndex; } }
        public int PrevTxOutputIndex { get { return prevTxOutputIndex; } }
        public Ecdsa256Signature Ecdsa256Signature
        {
            get
            {
                if (Version != 0)
                    throw new NotSupportedException();
                return ecdsa256Signature;
            }
        }
        public Secp256k1Signature Secp256k1Signature
        {
            get
            {
                if (Version != 1)
                    throw new NotSupportedException();
                return secp256k1Signature;
            }
        }
        public Ecdsa256PubKey Ecdsa256PubKey
        {
            get
            {
                if (Version != 0)
                    throw new NotSupportedException();
                return ecdsa256PubKey;
            }
        }

        public DSASIGNATUREBASE SenderSignature
        {
            get
            {
                if (Version == 0)
                    return ecdsa256Signature;
                else if (Version == 1)
                    return secp256k1Signature;
                else
                    throw new NotSupportedException();
            }
        }
        public DSAPUBKEYBASE SenderPubKey
        {
            get
            {
                if (Version == 0)
                    return ecdsa256PubKey;
                //Secp256k1の場合、署名だけではなく署名対象のデータもなければ公開鍵を復元できない
                else if (Version == 1)
                    throw new InvalidOperationException();
                else
                    throw new NotSupportedException();
            }
        }

        protected override Func<ReaderWriter, IEnumerable<MainDataInfomation>> StreamInfo
        {
            get
            {
                if (Version == 0)
                    return (msrw) => new MainDataInfomation[]{
                        new MainDataInfomation(typeof(long), () => prevTxBlockIndex, (o) => prevTxBlockIndex = (long)o),
                        new MainDataInfomation(typeof(int), () => prevTxIndex, (o) => prevTxIndex = (int)o),
                        new MainDataInfomation(typeof(int), () => prevTxOutputIndex, (o) => prevTxOutputIndex = (int)o),
                        new MainDataInfomation(typeof(Ecdsa256Signature), null, () => ecdsa256Signature, (o) => ecdsa256Signature = (Ecdsa256Signature)o),
                        new MainDataInfomation(typeof(Ecdsa256PubKey), null, () => ecdsa256PubKey, (o) => ecdsa256PubKey = (Ecdsa256PubKey)o),
                    };
                else if (Version == 1)
                    return (msrw) => new MainDataInfomation[]{
                        new MainDataInfomation(typeof(long), () => prevTxBlockIndex, (o) => prevTxBlockIndex = (long)o),
                        new MainDataInfomation(typeof(int), () => prevTxIndex, (o) => prevTxIndex = (int)o),
                        new MainDataInfomation(typeof(int), () => prevTxOutputIndex, (o) => prevTxOutputIndex = (int)o),
                        new MainDataInfomation(typeof(Secp256k1Signature), null, () => secp256k1Signature, (o) => secp256k1Signature = (Secp256k1Signature)o),
                    };
                else
                    throw new NotSupportedException();
            }
        }
        public override bool IsVersioned { get { return true; } }
        public override bool IsVersionSaved { get { return false; } }

        public Func<ReaderWriter, IEnumerable<MainDataInfomation>> StreamInfoToSign
        {
            get
            {
                if (Version == 0 || Version == 1)
                    return (msrw) => new MainDataInfomation[]{
                        new MainDataInfomation(typeof(long), () => prevTxBlockIndex, (o) => { throw new NotSupportedException(); }),
                        new MainDataInfomation(typeof(int), () => prevTxIndex, (o) => { throw new NotSupportedException(); }),
                        new MainDataInfomation(typeof(int), () => prevTxOutputIndex, (o) => { throw new NotSupportedException(); }),
                    };
                else
                    throw new NotSupportedException();
            }
        }

        public void SetSenderSig(DSASIGNATUREBASE signature)
        {
            if (Version == 0)
            {
                if (!(signature is Ecdsa256Signature))
                    throw new ArgumentException();
                ecdsa256Signature = signature as Ecdsa256Signature;
            }
            else if (Version == 1)
            {
                if (!(signature is Secp256k1Signature))
                    throw new ArgumentException();
                secp256k1Signature = signature as Secp256k1Signature;
            }
            else
                throw new NotSupportedException();
        }
    }

    public class TransactionOutput : SHAREDDATA
    {
        public TransactionOutput() : base(0) { }

        public void LoadVersion0(Sha256Ripemd160Hash _receiverPubKeyHash, CurrencyUnit _amount)
        {
            Version = 0;

            receiverPubKeyHash = _receiverPubKeyHash;
            amount = _amount;
        }

        private Sha256Ripemd160Hash receiverPubKeyHash;
        private CurrencyUnit amount;

        public Sha256Ripemd160Hash Sha256Ripemd160Hash { get { return receiverPubKeyHash; } }
        public CurrencyUnit Amount { get { return amount; } }

        public HASHBASE ReceiverPubKeyHash { get { return receiverPubKeyHash; } }

        protected override Func<ReaderWriter, IEnumerable<MainDataInfomation>> StreamInfo
        {
            get
            {
                if (Version == 0)
                    return (msrw) => new MainDataInfomation[]{
                        new MainDataInfomation(typeof(Sha256Ripemd160Hash), null, () => receiverPubKeyHash, (o) => receiverPubKeyHash = (Sha256Ripemd160Hash)o),
                        new MainDataInfomation(typeof(long), () => amount.rawAmount, (o) => amount = new CurrencyUnit((long)o)),
                    };
                else
                    throw new NotSupportedException();
            }
        }
        public override bool IsVersioned { get { return true; } }
        public override bool IsVersionSaved { get { return false; } }

        public Func<ReaderWriter, IEnumerable<MainDataInfomation>> StreamInfoToSign
        {
            get
            {
                if (Version == 0)
                    return (msrw) => new MainDataInfomation[]{
                        new MainDataInfomation(typeof(Sha256Ripemd160Hash), null, () => receiverPubKeyHash, (o) => { throw new NotSupportedException(); }),
                        new MainDataInfomation(typeof(long), () => amount.rawAmount, (o) => { throw new NotSupportedException(); }),
                };
                else
                    throw new NotSupportedException();
            }
        }

        public Func<ReaderWriter, IEnumerable<MainDataInfomation>> StreamInfoToSignPrev
        {
            get
            {
                if (Version == 0)
                    return (msrw) => new MainDataInfomation[]{
                        new MainDataInfomation(typeof(Sha256Ripemd160Hash), null, () => receiverPubKeyHash, (o) => { throw new NotSupportedException(); }),
                };
                else
                    throw new NotSupportedException();
            }
        }
    }

    public abstract class Transaction : SHAREDDATA
    {
        public Transaction() : base(0) { idCache = new CachedData<Sha256Sha256Hash>(() => new Sha256Sha256Hash(ToBinary())); }

        public virtual void LoadVersion0(TransactionOutput[] _txOutputs)
        {
            Version = 0;

            LoadCommon(_txOutputs);
        }

        public virtual void LoadVersion1(TransactionOutput[] _txOutputs)
        {
            Version = 1;

            LoadCommon(_txOutputs);
        }

        private void LoadCommon(TransactionOutput[] _txOutputs)
        {
            if (_txOutputs.Length == 0)
                throw new ArgumentException();

            txOutputs = _txOutputs;
        }

        private static readonly CurrencyUnit dustTxoutput = new Yumina(0.1m);
        private static readonly int maxTxInputs = 100;
        private static readonly int maxTxOutputs = 10;

        private TransactionOutput[] _txOutputs;
        private TransactionOutput[] txOutputs
        {
            get { return _txOutputs; }
            set
            {
                if (value != _txOutputs)
                {
                    _txOutputs = value;
                    idCache.IsModified = true;
                }
            }
        }

        public virtual TransactionInput[] TxInputs { get { return new TransactionInput[] { }; } }
        public virtual TransactionOutput[] TxOutputs { get { return txOutputs; } }

        protected CachedData<Sha256Sha256Hash> idCache;
        public virtual Sha256Sha256Hash Id { get { return idCache.Data; } }

        protected override Func<ReaderWriter, IEnumerable<MainDataInfomation>> StreamInfo
        {
            get
            {
                if (Version == 0 || Version == 1)
                    return (msrw) => new MainDataInfomation[]{
                        new MainDataInfomation(typeof(TransactionOutput[]), 0, null, () => txOutputs, (o) => txOutputs = (TransactionOutput[])o),
                    };
                else
                    throw new NotSupportedException();
            }
        }
        public override bool IsVersioned { get { return true; } }
        public override bool IsCorruptionChecked
        {
            get
            {
                if (Version == 0 || Version == 1)
                    return true;
                else
                    throw new NotSupportedException();
            }
        }

        public virtual bool Verify()
        {
            if (Version == 0 || Version == 1)
                return VerifyNotExistDustTxOutput() && VerifyNumberOfTxInputs() && VerifyNumberOfTxOutputs();
            else
                throw new NotSupportedException();
        }

        public bool VerifyNotExistDustTxOutput()
        {
            if (Version == 0 || Version == 1)
                return txOutputs.All((elem) => elem.Amount.rawAmount >= dustTxoutput.rawAmount);
            else
                throw new NotSupportedException();
        }

        public bool VerifyNumberOfTxInputs()
        {
            if (Version == 0 || Version == 1)
                return TxInputs.Length <= maxTxInputs;
            else
                throw new NotSupportedException();
        }

        public bool VerifyNumberOfTxOutputs()
        {
            if (Version == 0 || Version == 1)
                return TxOutputs.Length <= maxTxOutputs;
            else
                throw new NotSupportedException();
        }
    }

    public class CoinbaseTransaction : Transaction
    {
        public CoinbaseTransaction() : base() { }

        public override void LoadVersion0(TransactionOutput[] _txOutputs) { base.LoadVersion0(_txOutputs); }

        public override void LoadVersion1(TransactionOutput[] _txOutputs) { throw new NotSupportedException(); }

        protected override Func<ReaderWriter, IEnumerable<MainDataInfomation>> StreamInfo
        {
            get
            {
                if (Version == 0)
                    return base.StreamInfo;
                else
                    throw new NotSupportedException();
            }
        }
    }

    public class TransferTransaction : Transaction
    {
        public TransferTransaction() : base() { }

        public override void LoadVersion0(TransactionOutput[] _txOutputs) { throw new NotSupportedException(); }

        public virtual void LoadVersion0(TransactionInput[] _txInputs, TransactionOutput[] _txOutputs)
        {
            foreach (var txInput in _txInputs)
                if (txInput.Version != 0)
                    throw new ArgumentException();

            base.LoadVersion0(_txOutputs);

            LoadCommon(_txInputs);
        }

        public override void LoadVersion1(TransactionOutput[] _txOutputs) { throw new NotSupportedException(); }

        public virtual void LoadVersion1(TransactionInput[] _txInputs, TransactionOutput[] _txOutputs)
        {
            foreach (var txInput in _txInputs)
                if (txInput.Version != 1)
                    throw new ArgumentException();

            base.LoadVersion1(_txOutputs);

            LoadCommon(_txInputs);
        }

        private void LoadCommon(TransactionInput[] _txInputs)
        {
            if (_txInputs.Length == 0)
                throw new ArgumentException();

            txInputs = _txInputs;
        }

        private TransactionInput[] _txInputs;
        public TransactionInput[] txInputs
        {
            get { return _txInputs; }
            set
            {
                if (value != _txInputs)
                {
                    _txInputs = value;
                    idCache.IsModified = true;
                }
            }
        }

        protected override Func<ReaderWriter, IEnumerable<MainDataInfomation>> StreamInfo
        {
            get
            {
                if (Version == 0)
                    return (msrw) => base.StreamInfo(msrw).Concat(new MainDataInfomation[]{
                        new MainDataInfomation(typeof(TransactionInput[]), 0, null, () => txInputs, (o) => txInputs = (TransactionInput[])o),
                    });
                else if (Version == 1)
                    return (msrw) => base.StreamInfo(msrw).Concat(new MainDataInfomation[]{
                        new MainDataInfomation(typeof(TransactionInput[]), 1, null, () => txInputs, (o) => txInputs = (TransactionInput[])o),
                    });
                else
                    throw new NotSupportedException();
            }
        }

        private Func<ReaderWriter, IEnumerable<MainDataInfomation>> StreamInfoToSign(TransactionOutput[] prevTxOutputs) { return (msrw) => StreamInfoToSignInner(prevTxOutputs); }
        private IEnumerable<MainDataInfomation> StreamInfoToSignInner(TransactionOutput[] prevTxOutputs)
        {
            if (Version == 0 || Version == 1)
            {
                for (int i = 0; i < txInputs.Length; i++)
                {
                    foreach (var mdi in txInputs[i].StreamInfoToSign(null))
                        yield return mdi;
                    foreach (var mdi in prevTxOutputs[i].StreamInfoToSignPrev(null))
                        yield return mdi;
                }
                for (int i = 0; i < TxOutputs.Length; i++)
                    foreach (var mdi in TxOutputs[i].StreamInfoToSign(null))
                        yield return mdi;
            }
            else
                throw new NotSupportedException();
        }

        public byte[] GetBytesToSign(TransactionOutput[] prevTxOutputs)
        {
            if (prevTxOutputs.Length != txInputs.Length)
                throw new ArgumentException();

            return ToBinaryMainData(StreamInfoToSign(prevTxOutputs));
        }

        public void Sign(TransactionOutput[] prevTxOutputs, DSAPRIVKEYBASE[] privKeys)
        {
            if (prevTxOutputs.Length != txInputs.Length)
                throw new ArgumentException();
            if (privKeys.Length != txInputs.Length)
                throw new ArgumentException();

            byte[] bytesToSign = GetBytesToSign(prevTxOutputs);

            for (int i = 0; i < txInputs.Length; i++)
                txInputs[i].SetSenderSig(privKeys[i].Sign(bytesToSign));

            //取引入力の内容が変更された
            idCache.IsModified = true;
        }

        public override bool Verify() { throw new NotSupportedException(); }

        public virtual bool Verify(TransactionOutput[] prevTxOutputs)
        {
            if (prevTxOutputs.Length != txInputs.Length)
                throw new ArgumentException();

            if (Version == 0 || Version == 1)
                return base.Verify() && VerifySignature(prevTxOutputs) && VerifyPubKey(prevTxOutputs) && VerifyAmount(prevTxOutputs);
            else
                throw new NotSupportedException();
        }

        public bool VerifySignature(TransactionOutput[] prevTxOutputs)
        {
            if (prevTxOutputs.Length != txInputs.Length)
                throw new ArgumentException();

            byte[] bytesToSign = GetBytesToSign(prevTxOutputs);

            if (Version == 0)
            {
                for (int i = 0; i < txInputs.Length; i++)
                    if (!txInputs[i].Ecdsa256PubKey.Verify(bytesToSign, txInputs[i].SenderSignature.signature))
                        return false;
            }
            else if (Version == 1)
            {
                for (int i = 0; i < txInputs.Length; i++)
                    if (!Secp256k1Utility.Recover<Sha256Hash>(bytesToSign, txInputs[i].SenderSignature.signature).Verify(bytesToSign, txInputs[i].SenderSignature.signature))
                        return false;
            }
            else
                throw new NotSupportedException();
            return true;
        }

        public bool VerifyPubKey(TransactionOutput[] prevTxOutputs)
        {
            if (prevTxOutputs.Length != txInputs.Length)
                throw new ArgumentException();

            if (Version == 0)
            {
                for (int i = 0; i < txInputs.Length; i++)
                    if (!(Activator.CreateInstance(typeof(Sha256Ripemd160Hash), txInputs[i].Ecdsa256PubKey.pubKey) as Sha256Ripemd160Hash).Equals(prevTxOutputs[i].ReceiverPubKeyHash))
                        return false;
            }
            else if (Version == 1)
            {
                byte[] bytesToSign = GetBytesToSign(prevTxOutputs);

                for (int i = 0; i < txInputs.Length; i++)
                    if (!(Activator.CreateInstance(typeof(Sha256Ripemd160Hash), Secp256k1Utility.Recover<Sha256Hash>(bytesToSign, txInputs[i].Secp256k1Signature.signature).pubKey) as Sha256Ripemd160Hash).Equals(prevTxOutputs[i].ReceiverPubKeyHash))
                        return false;
            }
            else
                throw new NotSupportedException();
            return true;
        }

        public bool VerifyAmount(TransactionOutput[] prevTxOutputs)
        {
            if (prevTxOutputs.Length != txInputs.Length)
                throw new ArgumentException();

            return GetFee(prevTxOutputs).rawAmount >= 0;
        }

        public CurrencyUnit GetFee(TransactionOutput[] prevTxOutputs)
        {
            if (prevTxOutputs.Length != txInputs.Length)
                throw new ArgumentException();

            long totalPrevOutputs = 0;
            for (int i = 0; i < prevTxOutputs.Length; i++)
                totalPrevOutputs += prevTxOutputs[i].Amount.rawAmount;
            long totalOutpus = 0;
            for (int i = 0; i < TxOutputs.Length; i++)
                totalOutpus += TxOutputs[i].Amount.rawAmount;

            return new CurrencyUnit(totalPrevOutputs - totalOutpus);
        }
    }

    public abstract class Block : SHAREDDATA
    {
        public Block(int? _version) : base(_version) { idCache = new CachedData<X15Hash>(IdGenerator); }

        protected CachedData<X15Hash> idCache;
        protected virtual Func<X15Hash> IdGenerator { get { return () => new X15Hash(ToBinary()); } }
        public virtual X15Hash Id { get { return idCache.Data; } }

        public virtual bool Verify() { return true; }
    }

    public class GenesisBlock : Block
    {
        public GenesisBlock() : base(null) { }

        public readonly string genesisWord = "Bitstamp 2014/05/25 BTC/USD High 586.34 BTC to the moooooooon!!";

        protected override Func<ReaderWriter, IEnumerable<MainDataInfomation>> StreamInfo
        {
            get
            {
                return (msrw) => new MainDataInfomation[]{
                    new MainDataInfomation(typeof(string), () => genesisWord, (o) => { throw new NotSupportedException("genesis_block_cant_read"); }),
                };
            }
        }
    }

    public abstract class TransactionalBlock : Block
    {
        static TransactionalBlock()
        {
            rewards = new CurrencyUnit[numberOfCycles];
            rewards[0] = initialReward;
            for (int i = 1; i < numberOfCycles; i++)
                rewards[i] = new Creacoin(rewards[i - 1].Amount * rewardReductionRate);
        }

        public TransactionalBlock() : base(0) { }

        private static readonly long blockGenerationInterval = 60; //[sec]
        private static readonly long cycle = 60 * 60 * 24 * 365; //[sec]=1[year]
        private static readonly int numberOfCycles = 8;
        private static readonly long rewardlessStart = cycle * numberOfCycles; //[sec]=8[year]
        private static readonly decimal rewardReductionRate = 0.8m;
        private static readonly CurrencyUnit initialReward = new Creacoin(1.0m); //[CREA/sec]
        private static readonly CurrencyUnit[] rewards; //[CREA/sec]
        private static readonly decimal foundationShare = 0.1m;
        private static readonly long foundationInterval = 60 * 60 * 24; //[block]

        private static readonly long numberOfTimestamps = 11;
        private static readonly long targetTimespan = blockGenerationInterval * 1; //[sec]

        private static readonly Difficulty<X15Hash> minDifficulty = new Difficulty<X15Hash>(HASHBASE.FromHash<X15Hash>(new byte[] { 0, 127, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255 }));

#if TEST
        public static readonly Sha256Ripemd160Hash foundationPubKeyHash = new Sha256Ripemd160Hash(new byte[] { 69, 67, 83, 49, 32, 0, 0, 0, 16, 31, 116, 194, 127, 71, 154, 183, 50, 198, 23, 17, 129, 220, 25, 98, 4, 30, 93, 45, 53, 252, 176, 145, 108, 20, 226, 233, 36, 7, 35, 198, 98, 239, 109, 66, 206, 41, 162, 179, 255, 189, 126, 72, 97, 140, 165, 139, 118, 107, 137, 103, 76, 238, 125, 62, 163, 205, 108, 62, 189, 240, 124, 71 });
#else
        public static readonly Sha256Ripemd160Hash foundationPubKeyHash = null;
#endif

        public BlockHeader<BlockidHashType, TxidHashType> header { get; private set; }
        public CoinbaseTransaction coinbaseTxToMiner { get; private set; }
        public TransferTransaction[] transferTxs { get; private set; }
    }
}