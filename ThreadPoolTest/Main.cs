using System;
using System.Collections.Generic;
using System.Threading;
using System.Linq;

namespace ThreadPoolTest
{
	class MainClass
	{

		public const int MaxPendingTaskCount = 500;
		public const int PollPeriod = 1000;
		
		public static uint Counter = 1;
		private static readonly Dictionary<uint, DateTime> _taskTimestamps = new Dictionary<uint, DateTime>();
		private static readonly object _lock = new object ();
		private static readonly Random _rnd = new Random();
		
		public static void Main (string[] args)
		{
			Console.WriteLine ("Hello World!");
			for (var i = 0; i < 10; i++) 
				StartOne ();
			KeyValuePair<uint, DateTime>[] dead;
			var now = DateTime.Now;
			do {
				int count;
				int threadCount;
				lock (_lock) {
					Monitor.Wait (_lock, PollPeriod);
					count = _taskTimestamps.Count;
					threadCount = _rnd.Next (20);
					dead = 
						(from task in _taskTimestamps
						where (now - task.Value).TotalMilliseconds > PollPeriod * 2
							select task).ToArray ();
				}
				Console.WriteLine ("Pending: {0}", count);
				foreach (var missedCallback in dead)
					Console.WriteLine (
					"Callback {0} has not been received for {1}s", missedCallback.Key, (now - missedCallback.Value).TotalSeconds);
				
		
				ThreadPool.SetMinThreads (threadCount, threadCount);
				ThreadPool.SetMaxThreads (threadCount, threadCount);
			} while (true);
			
		}
				
		private static void StartOne ()
		{
			uint id;
			lock (_lock) {
				 id = Counter++;
				_taskTimestamps [id] = DateTime.Now;
			}
			ThreadPool.QueueUserWorkItem (Proc, id);
		}
		
		public static void Proc (object v)
		{
			bool removed;
			var id = (uint)v;
			int count;
			lock (_lock) {
				removed = _taskTimestamps.Remove (id);
				count = _taskTimestamps.Count;
			}
			if (!removed) 
				Console.WriteLine ("Task {0} was not found in task registry", id);
			int newCount;
			if (count > MaxPendingTaskCount)
				return;
			lock (_lock) 
				newCount = _rnd.Next (100);
			for (var i = 0; i < newCount; i++) 
				StartOne ();
			Thread.Sleep (newCount * 50);
		}
	}
}
