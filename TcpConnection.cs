using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace EventStore.Transport.Tcp
{
    //public static class Async {

    public class TcpConnection : TcpConnectionBase, ITcpConnection
    {
        internal static TcpConnection CreateConnectingTcpConnection(IPEndPoint remoteEndPoint, TcpClientConnector connector, Action<TcpConnection> onConnectionEstablished, Action<TcpConnection, SocketError> onConnectionFailed)
        {
            var connection = new TcpConnection(remoteEndPoint);
            connector.InitConnect(remoteEndPoint,
                                  (_, socket) =>
                                  {
                                      connection.InitSocket(socket);
                                      if (onConnectionEstablished != null)
                                          onConnectionEstablished(connection);
                                      connection.TrySend();
                                  },
                                  (_, socketError) =>
                                  {
                                      if (onConnectionFailed != null)
                                          onConnectionFailed(connection, socketError);
                                  });
            return connection;
        }

        internal static TcpConnection CreateAcceptedTcpConnection(IPEndPoint effectiveEndPoint, Socket socket)
        {
            var connection = new TcpConnection(effectiveEndPoint);
            connection.InitSocket(socket);
            return connection;
        }

        private const int MaxSendPacketSize = 64*1024;


        private int _packagesSent;
        private long _bytesSent;
        private int _packagesReceived;
        private long _bytesReceived;

        public event Action<ITcpConnection, SocketError> ConnectionClosed;

        public IPEndPoint EffectiveEndPoint { get; private set; }

        public int SendQueueSize
        {
            get { return _sendQueue.Count; }
        }

        private Socket _socket;
        private SocketAsyncEventArgs _receiveSocketArgs;
        private SocketAsyncEventArgs _sendSocketArgs;

        private readonly ConcurrentQueue<ArraySegment<byte>> _sendQueue = new ConcurrentQueue<ArraySegment<byte>>();

        private readonly Queue<ArraySegment<byte>> _receiveQueue =
            new Queue<ArraySegment<byte>>();

        private readonly MemoryStream _memoryStream = new MemoryStream();

        private readonly object _receivingLock = new object();
        private readonly object _sendingLock = new object();
        private bool _isSending;
        private int _closed;

        private Action<ITcpConnection, IEnumerable<ArraySegment<byte>>> _receiveCallback;

        private int _sentAsyncs;
        private int _sentAsyncCallbacks;
        private int _recvAsyncs;
        private int _recvAsyncCallbacks;

        private TcpConnection(IPEndPoint effectiveEndPoint)
        {
            if (effectiveEndPoint == null)
                throw new ArgumentNullException("effectiveEndPoint");

            EffectiveEndPoint = effectiveEndPoint;
        }

        private new void InitSocket (Socket socket)
		{
			base.InitSocket (socket, EffectiveEndPoint);
			lock (_sendingLock) {
				_socket = socket;
				try {
					socket.NoDelay = true;
					//socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReceiveBuffer, TcpConfiguration.SocketBufferSize);
					//socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.SendBuffer, TcpConfiguration.SocketBufferSize);
				} catch (ObjectDisposedException) {
					CloseInternal (SocketError.Shutdown);
					_socket = null;
					return;
				}

				_receiveSocketArgs = new SocketAsyncEventArgs ();
				_receiveSocketArgs.AcceptSocket = socket;
				_receiveSocketArgs.Completed += OnReceiveAsyncCompleted;

				_sendSocketArgs = new SocketAsyncEventArgs ();
                _sendSocketArgs.AcceptSocket = socket;
                _sendSocketArgs.Completed += OnSendAsyncCompleted;
            }
            StartReceive();
        }

        public void EnqueueSend(IEnumerable<ArraySegment<byte>> data)
        {
            lock (_sendingLock)
            {
                uint bytes = 0;
                foreach (var segment in data)
                {
                    _sendQueue.Enqueue(segment);
                    bytes += (uint)segment.Count;
                }
                NotifySendScheduled(bytes);
            }

            TrySend();
        }

        private void TrySend()
        {
            lock (_sendingLock)
            {
                if (_isSending || _sendQueue.Count == 0 || _socket == null)
                    return;

				if (TcpConnectionMonitor.Default.IsSendBlocked())
				{
					return;
				}
                _isSending = true;
            }

            _memoryStream.SetLength(0);

            ArraySegment<byte> sendPiece;
            while (_sendQueue.TryDequeue(out sendPiece))
            {
                _memoryStream.Write(sendPiece.Array, sendPiece.Offset, sendPiece.Count);

                if (_memoryStream.Length >= MaxSendPacketSize)
                    break;
            }
            Interlocked.Add(ref _bytesSent, _memoryStream.Length);
            _sendSocketArgs.SetBuffer(_memoryStream.GetBuffer(), 0, (int) _memoryStream.Length);

			if (_sendSocketArgs.Count == 0)
			{
				lock (_sendingLock) {
					_isSending = false;
					return;
				}
			}
			
            try
            {
                Interlocked.Increment(ref _sentAsyncs);

                //Console.WriteLine(String.Format("{0:mmss.fff}", DateTime.Now) + " sending " + _memoryStream.Length + " bytes.");
                NotifySendStarting((uint) _sendSocketArgs.Count);
                var firedAsync = _sendSocketArgs.AcceptSocket.SendAsync(_sendSocketArgs);
                if (!firedAsync)
                    ProcessSend(_sendSocketArgs);
            }
            catch (ObjectDisposedException)
            {
                ReturnSendingSocketArgs();
            }
        }

        private void OnSendAsyncCompleted(object sender, SocketAsyncEventArgs e)
        {
            // no other code should go here. All handling is the same for sync/async completion
            ProcessSend(e);
        }

        private void ProcessSend(SocketAsyncEventArgs socketArgs)
        {
            Interlocked.Increment(ref _sentAsyncCallbacks);
            if (socketArgs.SocketError != SocketError.Success)
            {
                NotifySendCompleted(0);
                ReturnSendingSocketArgs();
                CloseInternal(socketArgs.SocketError);
                return;
            }
            NotifySendCompleted((uint) socketArgs.Count);
            //Console.WriteLine(String.Format("{0:mmss.fff}", DateTime.Now) + " done sending " + _memoryStream.Length + " bytes.");
            var sentPackages = Interlocked.Increment(ref _packagesSent);

            lock (_sendingLock)
            {
                _isSending = false;
            }
            TrySend();
        }

        public void ReceiveAsync(Action<ITcpConnection, IEnumerable<ArraySegment<byte>>> callback)
        {
            if (callback == null)
                throw new ArgumentNullException("callback");

            lock (_receivingLock)
            {
                if (_receiveCallback != null)
                    throw new InvalidOperationException("ReceiveAsync called again while previous call wasn't fulfilled");
                _receiveCallback = callback;
            }
            TryDequeueReceivedData();
        }

        private void StartReceive()
        {
			var buffer = new byte[4096];
			lock (_receiveSocketArgs) 
			{
                _receiveSocketArgs.SetBuffer(buffer, 0, buffer.Length);
         		if (_receiveSocketArgs.Buffer == null)
	        		throw new Exception("Buffer was not set");
			}
            try
            {
                Interlocked.Increment(ref _recvAsyncs);
                NotifyReceiveStarting();
				bool firedAsync;
				lock (_receiveSocketArgs) {
    				if (_receiveSocketArgs.Buffer == null)
	    				throw new Exception("Buffer was lost");
                 	 firedAsync = _receiveSocketArgs.AcceptSocket.ReceiveAsync(_receiveSocketArgs);
				}
                if (!firedAsync) {
					Console.WriteLine("SYNC receive");					                  
                    ProcessReceive(_receiveSocketArgs);
				}
            }
            catch (ObjectDisposedException)
            {
                ReturnReceivingSocketArgs();
            }
        }

        private void OnReceiveAsyncCompleted(object sender, SocketAsyncEventArgs e)
        {
            // no other code should go here.  All handling is the same or async and sync completion
            ProcessReceive(e);
        }

        private void ProcessReceive(SocketAsyncEventArgs socketArgs)
        {
			if (socketArgs != _receiveSocketArgs)
				throw new Exception("Invalid socket args received");
            Interlocked.Increment(ref _recvAsyncCallbacks);

            // socket closed normally or some error occured
            if (socketArgs.BytesTransferred == 0 || socketArgs.SocketError != SocketError.Success)
            {
                NotifyReceiveCompleted(0);
                ReturnReceivingSocketArgs();
                CloseInternal(socketArgs.SocketError);
                return;
            }
            NotifyReceiveCompleted((uint) socketArgs.BytesTransferred);
            var receivedPackages = Interlocked.Increment(ref _packagesReceived);
            var receivedBytes = Interlocked.Add(ref _bytesReceived, socketArgs.BytesTransferred);
            //Console.WriteLine(String.Format("{0:mmss.fff}", DateTime.Now) + " received " + socketArgs.BytesTransferred + " bytes.");
            // OK, so what does this line of code do? It makes an ArraySegment<byte> representing the data 
            // that we actually read.
            // Then it constructs a little array to meet the IEnumerable interface.
            // Then it makes original buffer (ArraySegment<byte>) we used for receive operation.
            // Then it builds an IEnumerable that will dispose of our buffer (returning it to the buffer pool) 
            // later (as in later when some other thread (but it may be on this thread, we aren't sure) processes 
            // this buffer).
            // This should be benchmarked vs copying the byte array every time into a new byte array
            var receiveBufferSegment =
                new ArraySegment<byte>(socketArgs.Buffer, socketArgs.Offset, socketArgs.BytesTransferred);

            lock (_receivingLock)
            {
                var fullBuffer = new ArraySegment<byte>(socketArgs.Buffer, socketArgs.Offset, socketArgs.Count);
                _receiveQueue.Enqueue(receiveBufferSegment);
            }

			lock (_receiveSocketArgs) {
				if (socketArgs.Buffer == null)
					throw new Exception("Cleaning already null buffer");
            	socketArgs.SetBuffer(null, 0, 0);
			}

            StartReceive();
            TryDequeueReceivedData();
        }

        private void TryDequeueReceivedData()
        {
            Action<ITcpConnection, IEnumerable<ArraySegment<byte>>> callback;
            List<ArraySegment<byte>> res = null;

            lock (_receivingLock)
            {
                // no awaiting callback or no data to dequeue
                if (_receiveCallback == null || _receiveQueue.Count == 0)
                    return;

                res = new List<ArraySegment<byte>>(_receiveQueue.Count);
                while (_receiveQueue.Count > 0)
                {
                    var arraySegments = _receiveQueue.Dequeue();
                    res.Add(arraySegments);
                }

                callback = _receiveCallback;
                _receiveCallback = null;
            }
            callback(this, res.ToArray());
            uint bytes = 0;
            foreach (var tuple in res)
            {
                bytes += (uint)tuple.Count;
            }
            NotifyReceiveDispatched(bytes);
        }

        public void Close()
        {
            CloseInternal(SocketError.Success); // normal socket closing
        }

        private void CloseInternal(SocketError socketError)
        {
            var isClosed = Interlocked.CompareExchange(ref _closed, 1, 0) != 0;
            if (isClosed)
                return;

            Console.WriteLine(
                "[{0}]:\nReceived packages: {1}, bytes: {2}\nSend packages: {3}, bytes: {4}\nSendAsync calls: {5}, callbacks: {6}\nReceiveAsync calls: {7}, callbacks: {8}\n",
                EffectiveEndPoint,
                _packagesReceived,
                _bytesReceived,
                _packagesSent,
                _bytesSent,
                _sentAsyncs,
                _sentAsyncCallbacks,
                _recvAsyncs,
                _recvAsyncCallbacks);

            Helper.EatException(() => _socket.Shutdown(SocketShutdown.Both));
            Helper.EatException(() => _socket.Close(TcpConfiguration.SocketCloseTimeoutMs));
            _socket = null;

            lock (_sendingLock)
            {
                if (!_isSending)
                    ReturnSendingSocketArgs();
            }

            var handler = ConnectionClosed;
            if (handler != null)
                handler(this, socketError);
        }

        private void ReturnSendingSocketArgs()
        {
            var socketArgs = Interlocked.Exchange(ref _sendSocketArgs, null);
            if (socketArgs != null)
            {
                socketArgs.Completed -= OnSendAsyncCompleted;
                socketArgs.AcceptSocket = null;
                if (socketArgs.Buffer != null)
                    socketArgs.SetBuffer(null, 0, 0);
            }
        }

        private void ReturnReceivingSocketArgs()
        {
            var socketArgs = Interlocked.Exchange(ref _receiveSocketArgs, null);
            if (socketArgs != null)
            {
                socketArgs.Completed -= OnReceiveAsyncCompleted;
                socketArgs.AcceptSocket = null;
                if (socketArgs.Buffer != null)
                {
                    socketArgs.SetBuffer(null, 0, 0);
                }
            }
        }

		
		public override string ToString ()
    	{
    		return EffectiveEndPoint.ToString();
    	}
 		
    }

    // }
	
	

}