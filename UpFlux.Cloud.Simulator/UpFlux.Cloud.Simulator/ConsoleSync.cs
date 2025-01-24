using System;

namespace UpFlux.Cloud.Simulator
{
    /// <summary>
    /// Provides thread-safe console operations so that 
    /// both the console menu and the gRPC background threads 
    /// don't interfere with each other's input/output.
    /// </summary>
    public static class ConsoleSync
    {
        private static readonly object _consoleLock = new object();

        public static void WriteLine(string message)
        {
            lock (_consoleLock)
            {
                Console.WriteLine(message);
            }
        }

        public static void Write(string message)
        {
            lock (_consoleLock)
            {
                Console.Write(message);
            }
        }

        public static char ReadKey()
        {
            lock (_consoleLock)
            {
                // By default, we do intercept: true so user doesn't see the typed char. 
                // If you prefer user to see it, pass intercept: false.
                return Console.ReadKey(intercept: true).KeyChar;
            }
        }

        public static string ReadLine()
        {
            lock (_consoleLock)
            {
                return Console.ReadLine();
            }
        }
    }
}
