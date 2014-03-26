using System;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows;
namespace CREA2014
{
    public class Test
    {
        private readonly Program.Logger logger;

        public Test(Program.Logger _logger)
        {
            logger = _logger;

            //ClientServerTest();

            CreaNodeTest();
        }

        private void CreaNodeTest()
        {
            CreaNodeLocalTest localTest = new CreaNodeLocalTest(7777, new FirstNodeInformation[] { }, 0);
            localTest.Start();

            Thread.Sleep(3000);

            string privateRsaParameters;
            string publicRsaParameters;
            using (RSACryptoServiceProvider rsacsp = new RSACryptoServiceProvider(2048))
            {
                privateRsaParameters = rsacsp.ToXmlString(true);
                publicRsaParameters = rsacsp.ToXmlString(false);
            }

            NodeInformation nodeinfo = new NodeInformation(IPAddress.Loopback, 7778, Network.localtest, DateTime.Now, publicRsaParameters);

            Client client = new Client(IPAddress.Loopback, 7777, RsaKeySize.rsa2048, privateRsaParameters, (ca, ip) =>
            {
                ca.WriteBytes(nodeinfo.ToBinary());
                ca.WriteBytes(BitConverter.GetBytes(0));
                ca.WriteBytes(BitConverter.GetBytes(0));
                bool isSameNetwork = BitConverter.ToBoolean(ca.ReadBytes(), 0);
                bool isOldCreaVersion = BitConverter.ToBoolean(ca.ReadBytes(), 0);
                int sessionProtocolVersion = BitConverter.ToInt32(ca.ReadBytes(), 0);

                if (sessionProtocolVersion == 0)
                {

                }
            });
            client.StartClient();

            Thread.Sleep(1000000);
        }

        private void ClientServerTest()
        {
            Listener listener = new Listener(7777, RsaKeySize.rsa2048, (ca, ip) =>
            {
                string message = Encoding.UTF8.GetString(ca.ReadCompressedBytes());

                MessageBox.Show(message);
            });
            listener.ReceiveTimeout = 1000;
            listener.SendTimeout = 1000;
            listener.ClientErrored += (sender, e) => MessageBox.Show("listener_client_error");
            listener.StartListener();

            Thread.Sleep(1000);

            string privateRSAParameters;
            using (RSACryptoServiceProvider rsacsp = new RSACryptoServiceProvider(2048))
                privateRSAParameters = rsacsp.ToXmlString(true);

            Client client = new Client(IPAddress.Loopback, 7777, RsaKeySize.rsa2048, privateRSAParameters, (ca, ip) =>
            {
                Thread.Sleep(3000);

                ca.WriteCompreddedBytes(Encoding.UTF8.GetBytes("テストだよ～"));
            });
            client.ReceiveTimeout = 1000;
            client.SendTimeout = 1000;
            client.Errored += (sender, e) => MessageBox.Show("client_error");
            client.StartClient();

            Thread.Sleep(1000000);
        }
    }
}