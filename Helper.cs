using System;
using System.Collections.Generic;

namespace EventStore.Transport.Tcp
{
    internal static class Helper
    {
        public static void EatException(Action action)
        {
            try
            {
                action();
            }
            catch (Exception)
            {
            }
        }
    }
}
