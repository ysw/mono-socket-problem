using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace EventStore.Transport.Tcp.Tests
{
    public static class Program
    {
        public static void Main(string[] args)
        {
            if (args.Length != 1 || GetCommand(args[0]) == null)
            {
                Usage();
                return;
            }
            GetCommand(args[0]).Run();
        }

        private static Command GetCommand(string commandName)
        {
            switch (commandName)
            {
                case "server": return new ServerComand();
                case "client": return new ClientCommand();
                case "both": return new BothCommand();
                default: return null;
            }
        }

        private static void Usage()
        {
            Console.WriteLine("EventStore.Transport.Tcp.Tests.exe server|client");
        }
    }

    internal class ClientCommand : Command
    {
        public override void Run()
        {
            var test = new test_random_bidirectional_transfer();
            test.multiple_point_send();
        }
    }

    internal class ServerComand : Command
    {
        public override void Run()
        {
            var test = new test_random_bidirectional_transfer();
            test.multiple_point_receive();
        }
    }

    internal class BothCommand : Command
    {
        public override void Run()
        {
            var server = new ServerComand();
            var client = new ClientCommand();
            var serverTask = Task.Factory.StartNew(server.Run);
            var clientTask = Task.Factory.StartNew(client.Run);
            Task.WaitAll(serverTask, clientTask);
        }
    }

    public abstract class Command
    {
        public abstract void Run();
    }
}
