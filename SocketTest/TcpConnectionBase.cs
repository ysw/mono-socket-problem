using System;
using System.Net;
using System.Net.Sockets;

namespace EventStore.Transport.Tcp
{
    public class TcpConnectionBase : IMonitoredTcpConnection
    {
        private Socket _socket;
        private IPEndPoint _endPoint;

        // this lock is per connection, so unlilkely to block on it
        // so locking any acces without any optimization
        private readonly object _lock = new object();

        private DateTime? _lastSendStarted;
        private DateTime? _lastReceiveStarted;
        private bool _isClosed;

        private uint _pendingSendBytes;
        private uint _inSendBytes;
        private uint _pendingReceivedBytes;
        private long _totaBytesSent;
        private long _totaBytesReceived;
        private bool _inStartSending;

        public TcpConnectionBase()
        {
            TcpConnectionMonitor.Default.Register(this);
        }

        public IPEndPoint EndPoint
        {
            get
            {
                lock (_lock)
                {
                    return _endPoint;
                }
            }
        }

        public bool IsReadyForSend
        {
            get
            {
                try
                {
                    return !_isClosed && _socket.Poll(0, SelectMode.SelectWrite);
                }
                catch (ObjectDisposedException ex)
                {
                    //TODO: why do we get this?
                    return false;
                }
            }
        }



        public bool IsReadyForReceive
        {
            get
            {
                try
                {
                    return !_isClosed && _socket.Poll(0, SelectMode.SelectRead);
                }
                catch (ObjectDisposedException ex)
                {
                    //TODO: why do we get this?
                    return false;
                }
            }
        }

        public bool IsInitialized
        {
            get
            {
                lock (_lock)
                {
                    return _socket != null;
                }
            }
        }

        public bool IsFaulted
        {
            get 
            {
                try
                {
                    return !_isClosed && _socket.Poll(0, SelectMode.SelectError);
                }
                catch (ObjectDisposedException ex)
                {
                    //TODO: why do we get this?
                    return false;
                }
            }
        }

        public bool IsClosed
        {
            get
            {
                lock (_lock)
                {
                    return _isClosed;
                }
            }
        }

        public bool InSend
        {
            get
            {
                lock (_lock)
                {
                    return _lastSendStarted != null;
                }
            }
        }

        public bool InStartSending
        {
            get
            {
                lock (_lock)
                {
                    return _inStartSending;
                }
            }
        }

        public bool InReceive
        {
            get
            {
                lock (_lock)
                {
                    return _lastReceiveStarted != null;
                }
            }
        }

        public DateTime? LastSendStarted
        {
            get 
            {
                lock (_lock)
                {
                    return _lastSendStarted;
                }
            }
        }

        public DateTime? LastReceiveStarted
        {
            get 
            {
                lock (_lock)
                {
                    return _lastReceiveStarted;
                }
            }
        }

        public uint PendingSendBytes
        {
            get
            {
                lock (_lock)
                {
                    return _pendingSendBytes;
                }
            }
        }

        public uint InSendBytes
        {
            get
            {
                lock (_lock)
                {
                    return _inSendBytes;
                }
            }
        }

        public uint PendingReceivedBytes
        {
            get
            {
                lock (_lock)
                {
                    return _pendingReceivedBytes;
                }
            }
        }

        public long TotalBytesSent
        {
            get
            {
                lock (_lock)
                {
                    return _totaBytesSent;
                }
            }
        }

        public long TotalBytesReceived
        {
            get
            {
                lock (_lock)
                {
                    return _totaBytesReceived;
                }
            }
        }

        protected void InitSocket(Socket socket, IPEndPoint endPoint)
        {

            _socket = socket;
            _endPoint = endPoint;
        }

        protected void NotifySendScheduled(uint bytes)
        {
            lock (_lock)
            {
                _pendingSendBytes += bytes;
            }
        }

        protected void NotifySendStarting(uint bytes)
        {
            lock (_lock)
            {
				if (_lastSendStarted != null)
					throw new Exception("Concurrent send deteced");
                _lastSendStarted = DateTime.Now;
                _pendingSendBytes -= bytes;
                _inSendBytes += bytes;
                _inStartSending = true;
            }
        }

        protected void NotifyStartSendingCompleted()
        {
            lock (_lock)
            {
                if (_lastSendStarted != null)
                    throw new Exception("Concurrent send deteced");
                _inStartSending = false;
            }
        }

        protected void NotifySendCompleted(uint bytes)
        {
            lock (_lock)
            {
                _lastSendStarted = null;
                _inSendBytes -= bytes;
                _totaBytesSent += bytes;
            }
        }

        protected void NotifyReceiveStarting()
        {
            lock (_lock)
            {
				if (_lastReceiveStarted != null)
					throw new Exception("Concurrent receive deteced");
                _lastReceiveStarted = DateTime.Now;
            }
        }

        protected void NotifyReceiveCompleted(uint bytes)
        {
            lock (_lock)
            {
                _lastReceiveStarted = null;
                _pendingReceivedBytes += bytes;
                _totaBytesReceived += bytes;
            }
        }

        protected void NotifyReceiveDispatched(uint bytes)
        {
            lock (_lock)
            {
                _pendingReceivedBytes -= bytes;
            }
        }

        protected void NotifyClosed()
        {
            lock (_lock)
            {
                _isClosed = true;
            }
            TcpConnectionMonitor.Default.Unregister(this);
        }
    }
}