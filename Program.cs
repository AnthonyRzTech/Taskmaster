using System;
using System.IO;
using System.Threading;

namespace Taskmaster
{
    class Program
    {
        static void Main(string[] args)
        {
            string configFile = "taskmaster.yaml";
            bool daemonMode = false;
            bool showHelp = false;
            
            // Parse command line arguments
            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                
                if (arg == "-c" || arg == "--config")
                {
                    if (i + 1 < args.Length)
                    {
                        configFile = args[++i];
                    }
                    else
                    {
                        Console.WriteLine("Error: Missing configuration file path");
                        return;
                    }
                }
                else if (arg == "-d" || arg == "--daemon")
                {
                    daemonMode = true;
                }
                else if (arg == "-h" || arg == "--help")
                {
                    showHelp = true;
                }
                else if (File.Exists(arg))
                {
                    configFile = arg;
                }
                else
                {
                    Console.WriteLine($"Unknown argument: {arg}");
                    showHelp = true;
                }
            }
            
            // Show help and exit if requested
            if (showHelp)
            {
                ShowHelp();
                return;
            }

            // Check if configuration file exists
            if (!File.Exists(configFile))
            {
                Console.WriteLine($"Configuration file not found: {configFile}");
                Console.WriteLine("Use -c or --config to specify a configuration file path");
                return;
            }

            Console.WriteLine($"Taskmaster starting with configuration: {configFile}");
            
            // Create and start the daemon
            var daemon = new TaskmasterDaemon(configFile);
            
            // Set up signal handling for SIGHUP (for non-Windows platforms)
            // Windows handling is done in the TaskmasterDaemon class
            Console.CancelKeyPress += (sender, e) => {
                if (e.SpecialKey == ConsoleSpecialKey.ControlC)
                {
                    Console.WriteLine("Shutting down Taskmaster...");
                    daemon.Stop();
                    e.Cancel = true;
                }
            };
            
            // Initialize logger
            Logger.Initialize();
            
            // Start the daemon
            if (!daemon.Start())
            {
                Console.WriteLine("Failed to start Taskmaster daemon");
                return;
            }
            
            if (daemonMode)
            {
                // In daemon mode, we just need to wait without running the command shell
                Console.WriteLine("Taskmaster running in daemon mode");
                
                // Keep the process alive
                daemon.RunDaemonLoop();
            }
            else
            {
                // Run the command shell
                var shell = new CommandShell(daemon);
                shell.Run();
            }
        }
        
        static void ShowHelp()
        {
            Console.WriteLine("Taskmaster - Process Manager");
            Console.WriteLine("Usage: taskmaster [options] [config-file]");
            Console.WriteLine();
            Console.WriteLine("Options:");
            Console.WriteLine("  -c, --config <file>    Specify configuration file (default: taskmaster.yaml)");
            Console.WriteLine("  -d, --daemon           Run in daemon mode without interactive shell");
            Console.WriteLine("  -h, --help             Show this help message");
            Console.WriteLine();
            Console.WriteLine("Example:");
            Console.WriteLine("  taskmaster -c /etc/taskmaster.yaml  # Run with specific config file");
            Console.WriteLine("  taskmaster -d                       # Run in daemon mode");
        }
    }
}