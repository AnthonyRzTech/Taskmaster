using System;
using System.Runtime.InteropServices;
using System.Collections.Generic;

namespace Taskmaster.Utils
{
    public static class SignalHandler
    {
        // Define signal constants
        public const int SIGHUP = 1;
        public const int SIGINT = 2;
        public const int SIGQUIT = 3;
        public const int SIGTERM = 15;
        public const int SIGUSR1 = 10;
        public const int SIGUSR2 = 12;
        
        // P/Invoke declarations for Unix platforms
        [DllImport("libc", SetLastError = true, EntryPoint = "signal")]
        private static extern IntPtr Signal_Unix(int sig, SignalHandlerDelegate handler);
        
        [DllImport("libc", SetLastError = true)]
        private static extern int kill(int pid, int sig);
        
        // Delegate for signal handlers
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void SignalHandlerDelegate(int signal);
        
        // Store delegates to prevent garbage collection
        private static readonly Dictionary<int, SignalHandlerDelegate> RegisteredHandlers = 
            new Dictionary<int, SignalHandlerDelegate>();

        // Register handler for a specific signal
        public static bool RegisterSignalHandler(int signal, Action<int> handler)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                try
                {
                    // Create a delegate that will not be garbage collected
                    SignalHandlerDelegate signalDelegate = (int sig) => {
                        try
                        {
                            handler(sig);
                        }
                        catch (Exception ex)
                        {
                            Console.Error.WriteLine($"Error in signal handler: {ex.Message}");
                        }
                    };
                    
                    // Store the delegate to prevent garbage collection
                    RegisteredHandlers[signal] = signalDelegate;
                    
                    // Register with the OS
                    IntPtr result = Signal_Unix(signal, signalDelegate);
                    
                    return result != IntPtr.Zero;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Failed to register signal handler: {ex.Message}");
                    return false;
                }
            }
            
            return false;
        }
        
        // Helper method to send signals to child processes
        public static bool SendSignal(int pid, int signal)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                try
                {
                    int result = kill(pid, signal);
                    return result == 0;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Failed to send signal {signal} to process {pid}: {ex.Message}");
                    return false;
                }
            }
            
            return false;
        }
        
        // Helper method to send signals to child processes by name
        public static bool SendSignalByName(string signalName, int pid)
        {
            int signal = GetSignalByName(signalName);
            if (signal > 0)
            {
                return SendSignal(pid, signal);
            }
            return false;
        }
        
        // Convert signal name to number
        public static int GetSignalByName(string signalName)
        {
            switch (signalName.ToUpper())
            {
                case "HUP": return SIGHUP;
                case "INT": return SIGINT;
                case "QUIT": return SIGQUIT;
                case "TERM": return SIGTERM;
                case "USR1": return SIGUSR1;
                case "USR2": return SIGUSR2;
                default: return -1;
            }
        }
    }
}