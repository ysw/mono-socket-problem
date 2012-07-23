using System;
using System.Net;

namespace EventStore.Transport.Tcp
{
    public interface IMonitoredTcpConnection
    {
        IPEndPoint EndPoint { get; }

        bool IsReadyForSend { get; }
        bool IsReadyForReceive { get; }
        bool IsInitialized { get; } 
        bool IsFaulted { get; }
        bool IsClosed { get; }

        bool InSend { get; }
        bool InReceive { get; }

        DateTime? LastSendStarted { get; }
        DateTime? LastReceiveStarted { get; }

        uint PendingSendBytes { get; }
        uint InSendBytes { get; }
        uint PendingReceivedBytes { get; }

        long TotalBytesSent { get; }
        long TotalBytesReceived { get; }
    }
}