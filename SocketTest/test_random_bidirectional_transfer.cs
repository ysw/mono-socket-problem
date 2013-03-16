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
            for (var i = 0; i < _data.Length/4; i++)
            {
                _data[i*4 + 3] = (byte) (i%256);
                _data[i * 4 + 2] = (byte)(i / 256 % 256);
                _data[i * 4 + 1] = (byte)(i / 256 / 256 % 256);
                _data[i * 4 + 0] = (byte)(i / 256 / 256 / 256);
            }
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
        }

        private void ServerReceiveCallback(ITcpConnection tcpConnection, IEnumerable<ArraySegment<byte>> arraySegments)
        {
            ReceiveAndSend(tcpConnection, arraySegments);
            tcpConnection.ReceiveAsync(ClientReceiveCallback);
        }

        private void ClientOnConnectionEstablished(TcpConnection tcpConnection)
        {
            _totalReceived[tcpConnection] = 0;
            _totalSent[tcpConnection] = 0;
            tcpConnection.ReceiveAsync(ClientReceiveCallback);
            SendRandomData(tcpConnection, small: false);
        }

        private void ClientOnConnectionFailed(TcpConnection tcpConnection, SocketError socketError)
        {
            Debug.Fail("Cannot establish connection: " + socketError);
        }

        private void ClientReceiveCallback(ITcpConnection tcpConnection, IEnumerable<ArraySegment<byte>> arraySegments)
        {
            ReceiveAndSend(tcpConnection, arraySegments);
            tcpConnection.ReceiveAsync(ClientReceiveCallback);
        }

        private int ReceiveData (ITcpConnection tcpConnection, IEnumerable<ArraySegment<byte>> data)
		{
            foreach (var segment in data)
            {
                if (segment.Count%4 != 0)
                    throw new Exception(4.ToString());
                var totalReceived = Interlocked.Read(ref _received);
                var received = _totalReceived[tcpConnection];
                for (var i = 0; i < segment.Count/4; i++)
                {
                    for (var j = 0; j < 4; j++)
                    {
                        var v = _data[(received + i*4 + j)%_data.Length];
                        var w = segment.Array[segment.Offset + i*4 + j];
                        if (v != w)
                            throw new Exception("v != w");
                    }
                }

                _totalReceived[tcpConnection] += (long)segment.Count;
                Interlocked.Add(ref _received, segment.Count);
            }
			int value = data.Sum (v => v.Count);
			return value;
        }

        private void ReceiveAndSend(ITcpConnection tcpConnection, IEnumerable<ArraySegment<byte>> arraySegments)
        {
            var received = ReceiveData(tcpConnection, arraySegments);
            SendRandomData(tcpConnection, small: _totalSent[tcpConnection] - _totalReceived[tcpConnection] > 32768 || received > 64);
        }

        private void SendRandomData (ITcpConnection tcpConnection, bool small)
		{
			var rnd = new Random ();
			WaitCallback callBack = state =>
			{
			    lock (tcpConnection) // to make sure ordered data generation -- even if we internally lock in enqueue
                    // the better test would be to generate consistent packages and verify them without this lock, but
                    // due to internal lock this lock just reduces probability of our problems not eliminating them
			    {
			        var outData = new List<ArraySegment<byte>>();
			        var sent = _totalSent[tcpConnection];
			        var index = (int) (sent%_data.Length);

			        int size = (4 + ((!small && rnd.Next(2) == 0) ? _data.Length/2 + rnd.Next(_data.Length/2) : rnd.Next(32)))/4
			                   *4;
			        if (index + size <= _data.Length)
			        {
			            outData.Add(new ArraySegment<byte>(_data, index, size));
			        }
			        else
			        {
			            outData.Add(new ArraySegment<byte>(_data, index, _data.Length - index));
			            outData.Add(new ArraySegment<byte>(_data, 0, size - _data.Length + index));
			        }
                    _totalSent[tcpConnection] += size;
                    Interlocked.Add(ref _sent, size);
			        tcpConnection.EnqueueSend(outData);
			    }
			};

			if (rnd.Next (3) != 0) {
				small = true;
				var loopCount = rnd.Next(1000); 		
				var nextRnd = 	 rnd.Next (4);
				for (var k = 0; k < 10; k++)
					ThreadPool.QueueUserWorkItem (v => 
					{
						long h = 1;
						// random cpu busy delay
						for (long i = 0; i < loopCount; i++)
							h = h + i;
					// randon cpu free delay
						callBack (null);
						if (!small && nextRnd == 0) {
							//Thread.Sleep (rnd.Next (5));
							ThreadPool.QueueUserWorkItem (callBack);
						}
					}
					);
			}
			else 
			{
				callBack (null);
//				callBack (null);
//				callBack (null);
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
