using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace EventStore.Transport.Tcp
{
    public class TcpServerListener
    {
        private readonly IPEndPoint _serverEndPoint;
        private readonly Socket _listeningSocket;
        private Action<TcpConnection> _onConnectionAccepted;

        public TcpServerListener(IPEndPoint serverEndPoint)
        {
            if (serverEndPoint == null)
                throw new ArgumentNullException("serverEndPoint");

            _serverEndPoint = serverEndPoint;

            _listeningSocket = new Socket(serverEndPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);

        }

        private SocketAsyncEventArgs CreateAcceptSocketArgs()
        {
            var socketArgs = new SocketAsyncEventArgs();
            socketArgs.Completed += AcceptCompleted;
            return socketArgs;
        }

        public void StartListening(Action<TcpConnection> callback)
        {
            if (callback == null)
                throw new ArgumentNullException("callback");

            _onConnectionAccepted = callback;

            try
            {
                _listeningSocket.Bind(_serverEndPoint);
                _listeningSocket.Listen(TcpConfiguration.AcceptBacklogCount);
            }
            catch (Exception)
            {
                Helper.EatException(() => _listeningSocket.Close(TcpConfiguration.SocketCloseTimeoutMs));
                throw;
            }

            for (int i = 0; i < TcpConfiguration.ConcurrentAccepts; ++i)
            {
                StartAccepting();
            }
        }

        private void StartAccepting()
        {
			var socketArgs = CreateAcceptSocketArgs();

            try
            {
                var firedAsync = _listeningSocket.AcceptAsync(socketArgs);
                if (!firedAsync)
                    ProcessAccept(socketArgs);
            }
            catch (ObjectDisposedException)
            {
                HandleBadAccept(socketArgs);
            }
        }

        private void AcceptCompleted(object sender, SocketAsyncEventArgs e)
        {
            ProcessAccept(e);
        }

        private void ProcessAccept(SocketAsyncEventArgs e)
        {
            if (e.SocketError != SocketError.Success)
            {
                HandleBadAccept(e);
            }
            else
            {
                var acceptSocket = e.AcceptSocket;
                e.AcceptSocket = null;

                OnSocketAccepted(acceptSocket);
            }

            StartAccepting();
        }

        private void HandleBadAccept(SocketAsyncEventArgs socketArgs)
        {
            Helper.EatException(() => socketArgs.AcceptSocket.Close(TcpConfiguration.SocketCloseTimeoutMs));
            socketArgs.AcceptSocket = null;
        }

        private void OnSocketAccepted(Socket socket)
        {
            IPEndPoint socketEndPoint;
            try
            {
                socketEndPoint = (IPEndPoint)socket.RemoteEndPoint;
            }
            catch (Exception)
            {
                return;
            }

            var tcpConnection = TcpConnection.CreateAcceptedTcpConnection(socketEndPoint, socket);
            _onConnectionAccepted(tcpConnection);
        }

        public void Stop()
        {
            Helper.EatException(() => _listeningSocket.Close(TcpConfiguration.SocketCloseTimeoutMs));
        }
    }
}