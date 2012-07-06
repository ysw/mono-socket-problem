using System;
using System.Net;
using System.Net.Sockets;

namespace EventStore.Transport.Tcp
{
    public class TcpClientConnector
    {
        public TcpClientConnector()
        {
        }

        private SocketAsyncEventArgs CreateConnectSocketArgs()
        {
            var socketArgs = new SocketAsyncEventArgs();
            socketArgs.Completed += ConnectCompleted;
            socketArgs.UserToken = new CallbacksToken();
            return socketArgs;
        }

        public TcpConnection ConnectTo(IPEndPoint remoteEndPoint, 
                                       Action<TcpConnection> onConnectionEstablished = null,
                                       Action<TcpConnection, SocketError> onConnectionFailed = null)
        {
            if (remoteEndPoint == null) 
                throw new ArgumentNullException("remoteEndPoint");
            return TcpConnection.CreateConnectingTcpConnection(remoteEndPoint, this, onConnectionEstablished, onConnectionFailed);
        }

        internal void InitConnect (IPEndPoint serverEndPoint,
                                  Action<IPEndPoint, Socket> onConnectionEstablished,
                                  Action<IPEndPoint, SocketError> onConnectionFailed)
		{
			if (serverEndPoint == null)
				throw new ArgumentNullException ("serverEndPoint");
			if (onConnectionEstablished == null)
				throw new ArgumentNullException ("onConnectionEstablished");
			if (onConnectionFailed == null)
				throw new ArgumentNullException ("onConnectionFailed");

			var socketArgs = CreateConnectSocketArgs ();
            var connectingSocket = new Socket(serverEndPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            socketArgs.RemoteEndPoint = serverEndPoint;
            socketArgs.AcceptSocket = connectingSocket;
            var callbacks = (CallbacksToken)socketArgs.UserToken;
            callbacks.OnConnectionEstablished = onConnectionEstablished;
            callbacks.OnConnectionFailed = onConnectionFailed;

            try
            {
                var firedAsync = connectingSocket.ConnectAsync(socketArgs);
                if (!firedAsync)
                    ProcessConnect(socketArgs);
            }
            catch (ObjectDisposedException)
            {
                HandleBadConnect(socketArgs);
            }
        }

        private void ConnectCompleted(object sender, SocketAsyncEventArgs e)
        {
            ProcessConnect(e);
        }

        private void ProcessConnect(SocketAsyncEventArgs e)
        {
            if (e.SocketError != SocketError.Success)
                HandleBadConnect(e);
            else
                OnSocketConnected(e);
        }

        private void HandleBadConnect(SocketAsyncEventArgs socketArgs)
        {
            var serverEndPoint = socketArgs.RemoteEndPoint;
            var socketError = socketArgs.SocketError;
            var callbacks = (CallbacksToken)socketArgs.UserToken;
            var onConnectionFailed = callbacks.OnConnectionFailed;

            Helper.EatException(() => socketArgs.AcceptSocket.Close(TcpConfiguration.SocketCloseTimeoutMs));

            socketArgs.AcceptSocket = null;
            callbacks.Reset();

            onConnectionFailed((IPEndPoint)serverEndPoint, socketError);
        }

        private void OnSocketConnected(SocketAsyncEventArgs socketArgs)
        {
            var remoteEndPoint = (IPEndPoint) socketArgs.RemoteEndPoint;
            var socket = socketArgs.AcceptSocket;
            var callbacks = (CallbacksToken)socketArgs.UserToken;
            var onConnectionEstablished = callbacks.OnConnectionEstablished;
            socketArgs.AcceptSocket = null;

            callbacks.Reset();
            onConnectionEstablished(remoteEndPoint, socket);
        }

        private class CallbacksToken
        {
            public Action<IPEndPoint, Socket> OnConnectionEstablished;
            public Action<IPEndPoint, SocketError> OnConnectionFailed;

            public void Reset()
            {
                OnConnectionEstablished = null;
                OnConnectionFailed = null;
            }
        }
    }
}