using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;

namespace EventStore.Transport.Tcp
{
    public interface ITcpConnection
    {
        event Action<ITcpConnection, SocketError> ConnectionClosed;
        IPEndPoint EffectiveEndPoint { get; }
        int SendQueueSize { get; }
        void ReceiveAsync(Action<ITcpConnection, IEnumerable<ArraySegment<byte>>> callback);
        void EnqueueSend(IEnumerable<ArraySegment<byte>> data);
        void Close();
    }
}