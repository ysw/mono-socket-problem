using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace EventStore.Transport.Tcp.Tests
{
    public class test_random_bidirectional_transfer
    {
    	
		
        private byte[] _data;
        private long _sent;
        private long _received;
        private readonly object _lock = new object();

        private ConcurrentDictionary<ITcpConnection, long> _totalReceived = new ConcurrentDictionary<ITcpConnection, long>();
        private ConcurrentDictionary<ITcpConnection, long> _totalSent = new ConcurrentDictionary<ITcpConnection, long>();

        public void Setup()
        {
            _data = new byte[4*1024];
            var rnd = new Random();
            rnd.NextBytes(_data);
        }

		int[] _ports = Enumerable.Range(2001, 100).ToArray();

        public void multiple_point_send ()
		{
			Setup ();
			_sent = 0;
			_received = 0;

			foreach (var p in _ports) {
				Thread.Sleep (1000);
                StartSend(p);
			}

            MonitorAndWaitForDoneSignal();
            Debug.WriteLine("Done");
        }

        public void multiple_point_receive()
        {
            Setup();
            _sent = 0;
            _received = 0;

            foreach (var p in _ports)
                StartReceive(p);

            MonitorAndWaitForDoneSignal();
            Debug.WriteLine("Done");
        }

        private void StartSend(int port)
        {
            var client = new TcpClientConnector();
            client.ConnectTo(new IPEndPoint(IPAddress.Loopback, port), ClientOnConnectionEstablished,
                             ClientOnConnectionFailed);
        }

        private void StartReceive(int port)
        {
            var server = new TcpServerListener(new IPEndPoint(IPAddress.Any, port));
            server.StartListening(ServerListenCallback);
        }

        private void ServerListenCallback(TcpConnection tcpConnection)
        {
            _totalReceived[tcpConnection] = 0;
            _totalSent[tcpConnection] = 0;
            tcpConnection.ReceiveAsync(ServerReceiveCallback);
            SendRandomData(tcpConnection, small: false);
            //StartPing(tcpConnection);
        }

        private void ServerReceiveCallback(ITcpConnection tcpConnection, IEnumerable<ArraySegment<byte>> arraySegments)
        {
            tcpConnection.ReceiveAsync(ClientReceiveCallback);
            ReceiveAndSend(tcpConnection, arraySegments);
        }

        private void ClientOnConnectionEstablished(TcpConnection tcpConnection)
        {
            _totalReceived[tcpConnection] = 0;
            _totalSent[tcpConnection] = 0;
            tcpConnection.ReceiveAsync(ClientReceiveCallback);
            SendRandomData(tcpConnection, small: false);
            StartPing(tcpConnection);
        }

        private void ClientOnConnectionFailed(TcpConnection tcpConnection, SocketError socketError)
        {
            Debug.Fail("Cannot establish connection: " + socketError);
        }

        private void ClientReceiveCallback(ITcpConnection tcpConnection, IEnumerable<ArraySegment<byte>> arraySegments)
        {
            tcpConnection.ReceiveAsync(ClientReceiveCallback);
            ReceiveAndSend(tcpConnection, arraySegments);
        }

        private int ReceiveData (ITcpConnection tcpConnection, IEnumerable<ArraySegment<byte>> data)
		{
			int value = data.Sum (v => v.Count);
			_totalReceived [tcpConnection] += (long)value;
			Interlocked.Add (ref _received, value);
			return value;
        }

        private void ReceiveAndSend(ITcpConnection tcpConnection, IEnumerable<ArraySegment<byte>> arraySegments)
        {
            var received = ReceiveData(tcpConnection, arraySegments);
            SendRandomData(tcpConnection, small: _totalSent[tcpConnection] - _totalReceived[tcpConnection] > 32768 || received > 64);
        }

        private void StartPing(TcpConnection tcpConnection)
        {
            WaitCallback callBack = null;
            callBack = state =>
                           {
                              //var outData = new List<ArraySegment<byte>>();
                               //outData.Add(new ArraySegment<byte>(_data, 0, 4));
                               //Interlocked.Add(ref _sent, 4);
                               //tcpConnection.TotalSent += 4;
                               //tcpConnection.EnqueueSend(outData);
                               Thread.Sleep(2000);
                               ThreadPool.QueueUserWorkItem(callBack);
                           };
            ThreadPool.QueueUserWorkItem(callBack);
        }

        private void SendRandomData (ITcpConnection tcpConnection, bool small)
		{
			var rnd = new Random ();
			WaitCallback callBack = state =>
			{ 
				
				var outData = new List<ArraySegment<byte>> ();
				int index = 0;
										        
				int size = 1 + ((!small && rnd.Next (2) == 0) ? _data.Length/2 + rnd.Next (_data.Length/2) : rnd.Next (32));
				int totalSize = 0;
				outData.Add (new ArraySegment<byte> (_data, index, size));
				totalSize += size;
				Interlocked.Add (ref _sent, size);
				_totalSent [tcpConnection] += totalSize;
				tcpConnection.EnqueueSend (outData);
			};

			if (rnd.Next (5) != 0) {
				ThreadPool.QueueUserWorkItem (v => 
				{
					long h = 1;
					// random cpu busy delay
					for (long i = 0; i < rnd.Next(1000000000); i++)
						h = h + i;
					for (long i = 0; i < rnd.Next(1000000000); i++)
						h = h + i;
					for (long i = 0; i < rnd.Next(1000000000); i++)
						h = h + i;
					for (long i = 0; i < rnd.Next(1000000000); i++)
						h = h + i;
					// randon cpu free delay
					Thread.Sleep (50 + rnd.Next (5));
					callBack (null);
					if (!small && rnd.Next (4) == 0) {
						Thread.Sleep (rnd.Next (5));
						ThreadPool.QueueUserWorkItem (callBack);
					}
				}
				);
			}
			else 
			{
				callBack (null);
				callBack (null);
				callBack (null);
			}
        }

        private void MonitorAndWaitForDoneSignal ()
		{
			var counter = 3000000000; // just infinity
			lock (_lock) {
				while (--counter > 0) {
					Monitor.Wait (_lock, 1000);
					//NOTE: this significantly increases probability of missing callback
					ThreadPool.SetMinThreads ((int)(5 + counter % 5), (int)(1 + counter % 5));
					ThreadPool.SetMaxThreads ((int)(5 + counter % 5), (int)(1 + counter % 5));
                }
            }
        }

    }
}
