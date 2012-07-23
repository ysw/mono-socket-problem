using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

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

    public abstract class Command
    {
        public abstract void Run();
    }
}
