using System;
using System.Collections.Concurrent;
namespace CocurrentTest {
	class MainClass	{
		struct Data {
			public int A;
			public int B;
			public int C;
			public int D;
			public Data(int v) {
				A = v;
				B = -v;
				C = v;
				D = -v;
			}
		}

		public static void Main (string[] args)	{
			Console.WriteLine ("Hello World!");
			var data = new byte[1024 * 1024];
			var stack = new ConcurrentStack<Data> ();
			
			for (var i = 0; i < 50; i++) {
				
				var thread = new System.Threading.Thread (v => {
				
					var rnd = new Random ();
					while (true) {
						int pushCount = rnd.Next (50);
						int popCount = rnd.Next (50);
						
						for (var k = 0; k < pushCount; k++) {
						
						
							var sample = new Data (rnd.Next(Int32.MaxValue));
							CheckSample (sample);
						
							stack.Push (sample);
						}
						
						for (var k = 0; k < popCount; k++) {
							Data retrievedSample = new Data();
							if (stack.TryPop (out retrievedSample)) {
								CheckSample (retrievedSample);
							}
						}
					}
				}
				);
				
				thread.Start ();
			}
		}

		static void CheckSample (Data sample){
			if (sample.A != -sample.B || sample.A != sample.C || sample.B != sample.D)
				throw new Exception (string.Format ("Invalid sample detected"));
		}
	}
}
