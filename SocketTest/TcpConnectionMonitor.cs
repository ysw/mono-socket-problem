using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace EventStore.Transport.Tcp
{
    internal class TcpConnectionMonitor
    {
        private readonly object _lock = new object();

        private class ConnectionData
        {
            private readonly IMonitoredTcpConnection _connection;

            public ConnectionData(IMonitoredTcpConnection connection)
            {
                _connection = connection;
            }

            public IMonitoredTcpConnection Connection
            {
                get { return _connection; }
            }

            public bool LastMissingSendCallBack { get; set; }

            public bool LastMissingReceiveCallBack { get; set; }

            public long LastTotalBytesSent { get; set; }
            public long LastTotalBytesReceived { get; set; }
        }

        private readonly Dictionary<IMonitoredTcpConnection, ConnectionData> _connections = new Dictionary<IMonitoredTcpConnection, ConnectionData>();

        private long _sentSinceLastRun;
        private long _receivedSinceLastRun;
		private long _pendingSendOnLastRun;
		private long _inSendOnLastRun;
		private long _pendingReceivedOnLastRun;
        
		bool _anySendBlockedOnLastRun;


        private TcpConnectionMonitor()
        {
            new Thread(Run).Start();
        }

        public void Register(IMonitoredTcpConnection connection)
        {
            lock (_lock)
            {
                DoRegisterConnection(connection);
            }
        }

        public void Unregister(IMonitoredTcpConnection connection)
        {
            lock (_lock)
            {
                DoUnregisterConnection(connection);
            }
        }

        private void Run()
        {
            while (true)
            {
                ConnectionData[] connections;
                lock (_lock)
                {
                    // allow breaking wait by Pulsing monitor
                    Monitor.Wait(_lock, TimeSpan.FromSeconds(_runPeriod));
                    connections = _connections.Values.ToArray();
                }
                AnalyzeConnections(connections);
            }
        }

        private void AnalyzeConnections(ConnectionData[] connections)
        {

            _receivedSinceLastRun = 0;
            _sentSinceLastRun = 0;
			_pendingSendOnLastRun = 0;
			_inSendOnLastRun = 0;
			_pendingReceivedOnLastRun = 0;
			_anySendBlockedOnLastRun = false;
			
            foreach (var connection in connections)
            {
                AnalyzeConnection(connection);
            }

			Console.WriteLine("# Total connections: {0,3}. Out: {1,8:f1}kb/s  In: {2,8:f1}kb.s  Pending Send: {3}  In Send: {4}  Pending Received: {5}", 
                connections.Length, 
                _sentSinceLastRun / 1024.0 / _runPeriod,
                _receivedSinceLastRun / 1024.0 / _runPeriod,
			    _pendingSendOnLastRun,
			    _inSendOnLastRun,
			    _pendingReceivedOnLastRun);
        }

        private void AnalyzeConnection(ConnectionData connectionData)
        {
            var connection = connectionData.Connection;
            if (!connection.IsInitialized)
                return;

            if (connection.IsFaulted)
            {
                Console.Error.WriteLine("# {0} is faulted", connection);
                return;
            }

            UpdateStatistics(connectionData);

            CheckPendingReceived(connection);
            CheckPendingSend(connection);
            CheckMissingSendCallback(connectionData, connection);
            CheckMissingReceiveCallback(connectionData, connection);
            CheckReceiveTimeout(connectionData, connection);
        }

        private void UpdateStatistics(ConnectionData connectionData)
        {
			var connection = connectionData.Connection;
            long totalBytesSent = connection.TotalBytesSent;
            long totalBytesReceived = connection.TotalBytesReceived;
			long pendingSend = connection.PendingSendBytes;
			long inSend = connection.InSendBytes;
			long pendingReceived = connection.PendingReceivedBytes;

            _sentSinceLastRun += totalBytesSent - connectionData.LastTotalBytesSent;
            _receivedSinceLastRun += totalBytesReceived - connectionData.LastTotalBytesReceived;
			_pendingSendOnLastRun += pendingSend;
			_inSendOnLastRun += inSend;
			_pendingReceivedOnLastRun = pendingReceived;

            connectionData.LastTotalBytesSent = totalBytesSent;
            connectionData.LastTotalBytesReceived = totalBytesReceived;
        }

        private static void CheckMissingReceiveCallback(ConnectionData connectionData, IMonitoredTcpConnection connection)
        {
            bool inReceive = connection.InReceive;
            bool isReadyForReceive = connection.IsReadyForReceive;
            DateTime? lastReceiveStarted = connection.LastReceiveStarted;

            int sinceLastReceive = (int) (DateTime.Now - lastReceiveStarted.GetValueOrDefault()).TotalMilliseconds;
            bool missingReceiveCallback = inReceive && isReadyForReceive && sinceLastReceive > 500;

            if (missingReceiveCallback && connectionData.LastMissingReceiveCallBack)
            {
                Console.Error.WriteLine(
                    "# {0} {1}ms since last Receive started. No completion callback received, but socket status is READY_FOR_RECEIVE",
                    connection, sinceLastReceive);
            }
            connectionData.LastMissingReceiveCallBack = missingReceiveCallback;
        }

        private static void CheckReceiveTimeout(ConnectionData connectionData, IMonitoredTcpConnection connection)
        {
            DateTime? lastReceiveStarted = connection.LastReceiveStarted;
            if (lastReceiveStarted == null)
                return;
            int sinceLastReceive = (int)(DateTime.Now - lastReceiveStarted.GetValueOrDefault()).TotalMilliseconds;
            if (sinceLastReceive > 10000)
            {
                Console.Error.WriteLine(
                    "# {0} {1}ms since last Receive started. No data receive din 10000ms. TIMEOUT DETECTED",
                    connection, sinceLastReceive);
            }
        }

        private void CheckMissingSendCallback(ConnectionData connectionData, IMonitoredTcpConnection connection)
        {
// snapshot all data?
            bool inSend = connection.InSend;
            bool isReadyForSend = connection.IsReadyForSend;
            DateTime? lastSendStarted = connection.LastSendStarted;
			uint inSendBytes = connection.InSendBytes;
            bool inStartSending = connection.InStartSending;


            int sinceLastSend = (int) (DateTime.Now - lastSendStarted.GetValueOrDefault()).TotalMilliseconds;
            bool missingSendCallback = inSend && isReadyForSend && sinceLastSend > 500;

            if (missingSendCallback && connectionData.LastMissingSendCallBack)
            {
				// _anySendBlockedOnLastRun = true;
                Console.Error.WriteLine(
					"# {0} {1}ms since last send started. No completion callback received, but socket status is READY_FOR_SEND. In send: {2}. In start_sending: {3}",
                    connection, sinceLastSend, inSendBytes, inStartSending);
            }
            connectionData.LastMissingSendCallBack = missingSendCallback;
        }

        private static void CheckPendingSend(IMonitoredTcpConnection connection)
        {
            uint pendingSendBytes = connection.PendingSendBytes;

            if (pendingSendBytes > 128*1024)
            {
                Console.WriteLine("# {0} {1}kb pending send", connection, pendingSendBytes/1024);
            }
        }

        private static void CheckPendingReceived(IMonitoredTcpConnection connection)
        {
            uint pendingReceivedBytes = connection.PendingReceivedBytes;

            if (pendingReceivedBytes > 128*1024)
            {
                Console.WriteLine("# {0} {1}kb are not dispatched", connection, pendingReceivedBytes/1024);
            }
        }

		public bool IsSendBlocked ()
		{
			return _anySendBlockedOnLastRun;
		}
		
        private void DoRegisterConnection(IMonitoredTcpConnection connection)
        {
            _connections.Add(connection, new ConnectionData(connection));
        }

        private void DoUnregisterConnection(IMonitoredTcpConnection connection)
        {
            _connections.Remove(connection);
        }

        public static readonly TcpConnectionMonitor Default = new TcpConnectionMonitor();
        private int _runPeriod = 5;
    }
}