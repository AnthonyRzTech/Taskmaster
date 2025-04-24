using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using Taskmaster.Utils;

namespace Taskmaster
{
    public class CommandShell
    {
        private TaskmasterDaemon Daemon { get; set; }
        private bool Running { get; set; } = true;
        private List<string> CommandHistory { get; set; } = new List<string>();
        private int HistoryIndex { get; set; } = -1;
        private bool IsInDashboardMode { get; set; } = false;
        private CancellationTokenSource? DashboardCts { get; set; }
        
        // Add a timer to check if daemon is still running
        private Timer? StatusCheckTimer { get; set; }

        // Define available commands
        private readonly Dictionary<string, (string Description, Action<string[]> Handler)> Commands = 
            new Dictionary<string, (string, Action<string[]>)>();
        
        public CommandShell(TaskmasterDaemon daemon)
        {
            Daemon = daemon;
            
            // Define commands
            Commands["help"] = ("Shows available commands", ShowHelp);
            Commands["status"] = ("Shows status of all programs", ShowStatus);
            Commands["start"] = ("Starts a program", StartProgram);
            Commands["stop"] = ("Stops a program", StopProgram);
            Commands["restart"] = ("Restarts a program", RestartProgram);
            Commands["reload"] = ("Reloads the configuration", ReloadConfig);
            Commands["config"] = ("Shows program configuration", ShowConfig);
            Commands["signal"] = ("Sends signal to a program", SendSignal);
            Commands["shutdown"] = ("Shuts down the taskmaster daemon", Shutdown);
            Commands["exit"] = ("Exits the shell", Exit);
            Commands["quit"] = ("Exits the shell", Exit);
            Commands["version"] = ("Shows version information", ShowVersion);
            Commands["dashboard"] = ("Shows interactive dashboard", ShowDashboard);
            Commands["logs"] = ("Shows recent logs", ShowLogs); // Add new command
            Commands["loglevel"] = ("Sets log level (error|warning|info|debug)", SetLogLevel); // Add a new command
        }
        
        public void Run()
        {
            Console.WriteLine("Taskmaster shell started. Type 'help' for available commands.");
            Console.WriteLine("Tip: Use 'dashboard' for a clean interactive view of your processes.");
            
            // Start a timer to periodically check if the daemon is still running
            // This allows us to detect if someone has triggered shutdown via Ctrl+C or other means
            StatusCheckTimer = new Timer(_ => CheckDaemonStatus(), null, 1000, 1000);
            
            while (Running)
            {
                Console.Write("taskmaster> ");
                string? input = ReadLineWithHistory();
                string commandInput = input?.Trim() ?? string.Empty;
                
                // Check if we should exit (could have been set by CheckDaemonStatus)
                if (!Running)
                {
                    break;
                }
                
                if (string.IsNullOrEmpty(commandInput))
                    continue;
                
                // Add to history
                CommandHistory.Add(commandInput);
                if (CommandHistory.Count > 100) // Limit history size
                    CommandHistory.RemoveAt(0);
                HistoryIndex = CommandHistory.Count;
                
                // Parse command
                string[] parts = commandInput.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                string command = parts[0].ToLower();
                string[] args = parts.Length > 1 ? parts.Skip(1).ToArray() : new string[0];
                
                if (Commands.TryGetValue(command, out var commandInfo))
                {
                    try
                    {
                        commandInfo.Handler(args);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error executing command: {ex.Message}");
                    }
                }
                else
                {
                    Console.WriteLine($"Unknown command: {command}. Type 'help' for available commands.");
                }
            }
            
            // Clean up timer
            StatusCheckTimer?.Dispose();
        }
        
        // Check if the daemon is still running, and exit the shell if it's not
        private void CheckDaemonStatus()
        {
            if (Daemon == null || !Daemon.IsRunning)
            {
                // The daemon has been stopped externally (e.g., by Ctrl+C)
                Running = false;
                
                // If we're in ReadLineWithHistory, we need to force input to be available
                // This is a bit of a hack, but it works to break out of the input loop
                try
                {
                    Console.CursorLeft = 0;
                    Console.WriteLine("\nTaskmaster daemon has been shut down. Press Enter to exit.");
                }
                catch
                {
                    // Ignore errors that might occur during shutdown
                }
            }
        }
        
        // Advanced ReadLine with arrow-key history and editing
        private string? ReadLineWithHistory()
        {
            var buffer = new StringBuilder();
            int pos = 0;
            // Add empty history marker
            if (HistoryIndex != CommandHistory.Count)
                HistoryIndex = CommandHistory.Count;
            int startLeft = Console.CursorLeft;
            int startTop = Console.CursorTop;
        
            while (true)
            {
                var keyInfo = Console.ReadKey(true);
                switch (keyInfo.Key)
                {
                    case ConsoleKey.Enter:
                        Console.SetCursorPosition(startLeft, startTop);
                        Console.WriteLine(buffer.ToString());
                        return buffer.ToString();
                    case ConsoleKey.LeftArrow:
                        if (pos > 0) pos--;
                        break;
                    case ConsoleKey.RightArrow:
                        if (pos < buffer.Length) pos++;
                        break;
                    case ConsoleKey.UpArrow:
                        if (CommandHistory.Count > 0 && HistoryIndex > 0)
                        {
                            HistoryIndex--;
                            buffer.Clear().Append(CommandHistory[HistoryIndex]);
                            pos = buffer.Length;
                        }
                        break;
                    case ConsoleKey.DownArrow:
                        if (HistoryIndex < CommandHistory.Count - 1)
                        {
                            HistoryIndex++;
                            buffer.Clear().Append(CommandHistory[HistoryIndex]);
                            pos = buffer.Length;
                        }
                        else if (HistoryIndex == CommandHistory.Count - 1)
                        {
                            HistoryIndex = CommandHistory.Count;
                            buffer.Clear();
                            pos = 0;
                        }
                        break;
                    case ConsoleKey.Backspace:
                        if (pos > 0)
                        {
                            buffer.Remove(pos - 1, 1);
                            pos--;
                        }
                        break;
                    case ConsoleKey.Delete:
                        if (pos < buffer.Length)
                        {
                            buffer.Remove(pos, 1);
                        }
                        break;
                    default:
                        if (!char.IsControl(keyInfo.KeyChar))
                        {
                            buffer.Insert(pos, keyInfo.KeyChar);
                            pos++;
                        }
                        break;
                }
                // Redraw line
                Console.SetCursorPosition(startLeft, startTop);
                Console.Write(buffer.ToString() + new string(' ', Console.WindowWidth - buffer.Length - startLeft));
                Console.SetCursorPosition(startLeft + pos, startTop);
            }
        }
        
        // Command handlers
        private void ShowHelp(string[] args)
        {
            Console.WriteLine("Available commands:");
            
            // Get the maximum command length for formatting
            int maxCommandLength = Commands.Keys.Max(k => k.Length);
            
            foreach (var command in Commands.OrderBy(c => c.Key))
            {
                Console.WriteLine($"  {command.Key.PadRight(maxCommandLength + 2)}{command.Value.Description}");
            }
            
            Console.WriteLine("\nUse 'help <command>' for more information on a specific command.");
            
            if (args.Length > 0 && Commands.TryGetValue(args[0].ToLower(), out var cmdInfo))
            {
                Console.WriteLine($"\n{args[0]} command:");
                
                switch (args[0].ToLower())
                {
                    case "help":
                        Console.WriteLine("  help [command]");
                        Console.WriteLine("    Shows help for all commands or a specific command.");
                        break;
                    case "status":
                        Console.WriteLine("  status [program]");
                        Console.WriteLine("    Shows status of all running programs or a specific program.");
                        Console.WriteLine("    If a program name is provided, shows detailed status for that program.");
                        break;
                    case "dashboard":
                        Console.WriteLine("  dashboard");
                        Console.WriteLine("    Shows an interactive, real-time dashboard of all processes.");
                        Console.WriteLine("    Press 'q' to exit the dashboard mode.");
                        break;
                    case "start":
                        Console.WriteLine("  start <program|all>");
                        Console.WriteLine("    Starts the specified program or all programs.");
                        Console.WriteLine("    If 'all' is specified, starts all programs configured with autostart=true.");
                        break;
                    case "stop":
                        Console.WriteLine("  stop <program|all>");
                        Console.WriteLine("    Stops the specified program or all programs.");
                        break;
                    case "restart":
                        Console.WriteLine("  restart <program|all>");
                        Console.WriteLine("    Restarts the specified program or all programs.");
                        Console.WriteLine("    This will gracefully stop and then start the program(s).");
                        break;
                    case "reload":
                        Console.WriteLine("  reload");
                        Console.WriteLine("    Reloads the configuration file and applies changes.");
                        Console.WriteLine("    Programs with changed configurations will be restarted if necessary.");
                        break;
                    case "config":
                        Console.WriteLine("  config <program>");
                        Console.WriteLine("    Shows the configuration for the specified program.");
                        break;
                    case "signal":
                        Console.WriteLine("  signal <program> <signal>");
                        Console.WriteLine("    Sends a signal to all processes of the specified program.");
                        Console.WriteLine("    Supported signals: TERM, HUP, INT, QUIT, USR1, USR2");
                        break;
                    case "shutdown":
                        Console.WriteLine("  shutdown");
                        Console.WriteLine("    Shuts down the taskmaster daemon and exits.");
                        break;
                    case "exit":
                    case "quit":
                        Console.WriteLine("  exit|quit");
                        Console.WriteLine("    Exits the shell but leaves the taskmaster daemon running.");
                        break;
                    case "version":
                        Console.WriteLine("  version");
                        Console.WriteLine("    Shows the taskmaster version information.");
                        break;
                }
            }
        }
        
        private void ShowStatus(string[] args)
        {
            if (args.Length == 0)
            {
                // Show status of all programs
                var statusList = Daemon.GetAllProcessStatus();
                
                if (statusList.Count == 0)
                {
                    Console.WriteLine("No programs running");
                    return;
                }
                
                Console.WriteLine("Program statuses:");
                
                // Group by program name for cleaner output
                foreach (var group in statusList.GroupBy(s => s.ProgramName))
                {
                    string programName = group.Key;
                    int totalProcesses = group.Count();
                    int runningProcesses = group.Count(s => s.State == Models.ProcessState.Running);
                    int stoppedProcesses = group.Count(s => s.State == Models.ProcessState.Stopped);
                    int startingProcesses = group.Count(s => s.State == Models.ProcessState.Starting);
                    int fatalProcesses = group.Count(s => s.State == Models.ProcessState.Fatal);
                    
                    Console.WriteLine($"{programName}: {runningProcesses} running, {startingProcesses} starting, " +
                                      $"{stoppedProcesses} stopped, {fatalProcesses} failed (total: {totalProcesses})");
                }
                
                Console.WriteLine("\nUse 'status <program>' for detailed information on a specific program.");
                Console.WriteLine("Or use 'dashboard' for an interactive process monitor.");
            }
            else
            {
                // Show status of a specific program
                string programName = args[0];
                var statusList = Daemon.GetAllProcessStatus().Where(s => s.ProgramName == programName).ToList();
                
                if (statusList.Count == 0)
                {
                    Console.WriteLine($"No processes found for program: {programName}");
                    return;
                }
                
                Console.WriteLine($"Status for program: {programName}");
                foreach (var status in statusList)
                {
                    Console.WriteLine($"  {status}");
                }
            }
        }
        
        private void ShowDashboard(string[] args)
        {
            if (IsInDashboardMode)
            {
                Console.WriteLine("Already in dashboard mode. Press 'q' to exit first.");
                return;
            }
            
            IsInDashboardMode = true;
            DashboardCts = new CancellationTokenSource();
            
            try
            {
                // Hide the cursor
                Console.CursorVisible = false;
                
                // Start dashboard loop
                RunDashboardLoop(DashboardCts.Token);
            }
            finally
            {
                // Restore cursor when exiting
                Console.CursorVisible = true;
                IsInDashboardMode = false;
                DashboardCts = null;
            }
        }
        
        private void RunDashboardLoop(CancellationToken token)
        {
            bool firstRun = true;
            
            while (!token.IsCancellationRequested)
            {
                // Get process status data
                var statusList = Daemon.GetAllProcessStatus();
                var configs = Daemon.GetAllProgramConfigs();
                
                // Render the dashboard, only doing full redraw on first run
                ConsoleVisualizer.RenderDashboard(statusList, configs, firstRun);
                
                // After first run, set flag to false
                if (firstRun)
                    firstRun = false;
                
                // Check for key press (non-blocking)
                if (Console.KeyAvailable)
                {
                    var key = Console.ReadKey(true);
                    HandleDashboardKeyPress(key);
                }
                
                // Refresh rate
                Thread.Sleep(1000);
            }
            
            // Clear the screen when exiting
            Console.Clear();
        }
        
        private void HandleDashboardKeyPress(ConsoleKeyInfo key)
        {
            switch (char.ToLower(key.KeyChar))
            {
                case 'q': // Exit dashboard
                    DashboardCts?.Cancel();
                    break;
                case 's': // Start a program
                    PromptAndExecute("Enter program name to start (or 'all'): ", StartProgram);
                    break;
                case 'p': // Stop a program
                    PromptAndExecute("Enter program name to stop (or 'all'): ", StopProgram);
                    break;
                case 'r': // Restart a program
                    PromptAndExecute("Enter program name to restart (or 'all'): ", RestartProgram);
                    break;
                case 'c': // Reload config
                    ReloadConfig(Array.Empty<string>());
                    break;
                case 'h': // Show help overlay
                    ShowDashboardHelp();
                    break;
            }
        }
        
        private void PromptAndExecute(string prompt, Action<string[]> action)
        {
            // Save cursor position
            int left = Console.CursorLeft;
            int top = Console.CursorTop;
            
            try
            {
                // Show prompt at the bottom of the screen
                Console.SetCursorPosition(0, Console.WindowHeight - 2);
                Console.Write(new string(' ', Console.WindowWidth));
                Console.SetCursorPosition(0, Console.WindowHeight - 2);
                Console.Write(prompt);
                
                // Make cursor visible for input
                Console.CursorVisible = true;
                
                // Get user input
                string? input = Console.ReadLine();
                
                if (!string.IsNullOrEmpty(input))
                {
                    // Execute the action with the input
                    action(new[] { input });
                }
            }
            finally
            {
                // Restore cursor
                Console.CursorVisible = false;
                Console.SetCursorPosition(left, top);
            }
        }
        
        private void ShowDashboardHelp()
        {
            // Save cursor position
            int left = Console.CursorLeft;
            int top = Console.CursorTop;
            
            try
            {
                // Calculate center position
                int helpWidth = 50;
                int helpHeight = 10;
                int startLeft = (Console.WindowWidth - helpWidth) / 2;
                int startTop = (Console.WindowHeight - helpHeight) / 2;
                
                // Draw help box
                for (int i = 0; i < helpHeight; i++)
                {
                    Console.SetCursorPosition(startLeft, startTop + i);
                    if (i == 0 || i == helpHeight - 1)
                        Console.Write(new string('═', helpWidth));
                    else
                    {
                        Console.Write("║");
                        Console.Write(new string(' ', helpWidth - 2));
                        Console.Write("║");
                    }
                }
                
                // Draw title
                Console.SetCursorPosition(startLeft + (helpWidth - 13) / 2, startTop);
                Console.Write("╡ DASHBOARD HELP ╞");
                
                // Draw content
                string[] helpLines = {
                    "q - Exit dashboard mode",
                    "s - Start a program",
                    "p - Stop a program",
                    "r - Restart a program",
                    "c - Reload configuration",
                    "h - Show this help"
                };
                
                for (int i = 0; i < helpLines.Length; i++)
                {
                    Console.SetCursorPosition(startLeft + 3, startTop + 2 + i);
                    Console.Write(helpLines[i]);
                }
                
                // Wait for key press
                Console.SetCursorPosition(startLeft + (helpWidth - 23) / 2, startTop + helpHeight - 2);
                Console.Write("Press any key to continue");
                Console.ReadKey(true);
            }
            finally
            {
                // Restore cursor
                Console.SetCursorPosition(left, top);
            }
        }
        
        private void StartProgram(string[] args)
        {
            if (args.Length == 0)
            {
                Console.WriteLine("Error: Missing program name. Usage: start <program|all>");
                return;
            }
            
            string programName = args[0];
            
            if (programName.ToLower() == "all")
            {
                Console.WriteLine("Starting all programs...");
                Daemon.StartAllPrograms();
            }
            else
            {
                Console.WriteLine($"Starting program: {programName}");
                if (Daemon.StartProgram(programName))
                {
                    Console.WriteLine($"Program {programName} started");
                }
                else
                {
                    Console.WriteLine($"Failed to start program: {programName}");
                }
            }
        }
        
        private void StopProgram(string[] args)
        {
            if (args.Length == 0)
            {
                Console.WriteLine("Error: Missing program name. Usage: stop <program|all>");
                return;
            }
            
            string programName = args[0];
            
            if (programName.ToLower() == "all")
            {
                Console.WriteLine("Stopping all programs...");
                Daemon.StopAllPrograms();
            }
            else
            {
                Console.WriteLine($"Stopping program: {programName}");
                if (Daemon.StopProgram(programName))
                {
                    Console.WriteLine($"Program {programName} stopped");
                }
                else
                {
                    Console.WriteLine($"Failed to stop program: {programName}");
                }
            }
        }
        
        private void RestartProgram(string[] args)
        {
            if (args.Length == 0)
            {
                Console.WriteLine("Error: Missing program name. Usage: restart <program|all>");
                return;
            }
            
            string programName = args[0];
            
            if (programName.ToLower() == "all")
            {
                Console.WriteLine("Restarting all programs...");
                Daemon.RestartAllPrograms();
            }
            else
            {
                Console.WriteLine($"Restarting program: {programName}");
                if (Daemon.RestartProgram(programName))
                {
                    Console.WriteLine($"Program {programName} restarted");
                }
                else
                {
                    Console.WriteLine($"Failed to restart program: {programName}");
                }
            }
        }
        
        private void ReloadConfig(string[] args)
        {
            Console.WriteLine("Reloading configuration...");
            if (Daemon.ReloadConfiguration())
            {
                Console.WriteLine("Configuration reloaded successfully");
            }
            else
            {
                Console.WriteLine("Failed to reload configuration");
            }
        }
        
        private void ShowConfig(string[] args)
        {
            if (args.Length == 0)
            {
                Console.WriteLine("Error: Missing program name. Usage: config <program>");
                return;
            }
            
            string programName = args[0];
            var config = Daemon.GetProgramConfig(programName);
            
            if (config == null)
            {
                Console.WriteLine($"Program not found: {programName}");
                return;
            }
            
            Console.WriteLine($"Configuration for program: {programName}");
            Console.WriteLine($"  Command: {config.Command}");
            Console.WriteLine($"  Number of processes: {config.NumProcs}");
            Console.WriteLine($"  Autostart: {config.AutoStart}");
            Console.WriteLine($"  Autorestart: {config.AutoRestart}");
            Console.WriteLine($"  Exit codes: {string.Join(", ", config.ExitCodes)}");
            Console.WriteLine($"  Start retries: {config.StartRetries}");
            Console.WriteLine($"  Start time: {config.StartTime} seconds");
            Console.WriteLine($"  Stop signal: {config.StopSignal}");
            Console.WriteLine($"  Stop time: {config.StopTime} seconds");
            Console.WriteLine($"  Working directory: {config.WorkingDir}");
            Console.WriteLine($"  Umask: {config.Umask.ToString("000")}");
            
            if (!string.IsNullOrEmpty(config.StdoutLogfile))
                Console.WriteLine($"  Stdout log: {config.StdoutLogfile}");
            
            if (!string.IsNullOrEmpty(config.StderrLogfile))
                Console.WriteLine($"  Stderr log: {config.StderrLogfile}");
            
            if (config.Environment.Count > 0)
            {
                Console.WriteLine("  Environment variables:");
                foreach (var env in config.Environment)
                {
                    Console.WriteLine($"    {env.Key}={env.Value}");
                }
            }
        }
        
        private void SendSignal(string[] args)
        {
            if (args.Length < 2)
            {
                Console.WriteLine("Error: Missing arguments. Usage: signal <program> <signal>");
                return;
            }
            
            string programName = args[0];
            string signalName = args[1].ToUpper();
            
            // Check if this is a valid signal
            if (Utils.SignalHandler.GetSignalByName(signalName) == -1)
            {
                Console.WriteLine($"Error: Unknown signal: {signalName}");
                Console.WriteLine("Supported signals: TERM, HUP, INT, QUIT, USR1, USR2");
                return;
            }
            
            var statuses = Daemon.GetAllProcessStatus()
                .Where(s => s.ProgramName == programName)
                .ToList();
            
            if (statuses.Count == 0)
            {
                Console.WriteLine($"No running processes found for program: {programName}");
                return;
            }
            
            int successCount = 0;
            foreach (var status in statuses)
            {
                if (status.State == Models.ProcessState.Running || status.State == Models.ProcessState.Starting)
                {
                    if (Utils.SignalHandler.SendSignalByName(signalName, status.ProcessId))
                    {
                        successCount++;
                    }
                }
            }
            
            Console.WriteLine($"Signal {signalName} sent to {successCount} of {statuses.Count} processes for program {programName}");
        }
        
        private void Shutdown(string[] args)
        {
            Console.WriteLine("Shutting down Taskmaster daemon...");
            Running = false;
            Daemon.Stop();
        }
        
        private void Exit(string[] args)
        {
            Console.WriteLine("Exiting Taskmaster shell. Daemon continues to run in the background.");
            Running = false;
        }
        
        private void ShowVersion(string[] args)
        {
            Console.WriteLine("Taskmaster v1.0.0");
            Console.WriteLine("Copyright (c) 2025 Taskmaster Team");
        }

        private void ShowLogs(string[] args)
        {
            int lines = 20; // Default number of lines
            string filter = "";
            
            if (args.Length > 0 && int.TryParse(args[0], out int requestedLines))
            {
                lines = requestedLines;
                if (args.Length > 1)
                    filter = args[1].ToLower();
            }
            else if (args.Length > 0)
            {
                filter = args[0].ToLower();
            }
            
            try
            {
                string logPath = Daemon.GetLogFilePath();
                if (!File.Exists(logPath))
                {
                    Console.WriteLine($"Log file not found: {logPath}");
                    return;
                }
                
                // Use FileStream with FileShare.ReadWrite to allow reading the file while it's being written to
                using (var fileStream = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var reader = new StreamReader(fileStream))
                {
                    // Read all lines into a list
                    var allLines = new List<string>();
                    string? line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        allLines.Add(line);
                    }
                    
                    // Process the lines (most recent first, with filtering)
                    var filteredLines = allLines
                        .AsEnumerable()
                        .Reverse()
                        .Where(l => string.IsNullOrEmpty(filter) || l.ToLower().Contains(filter))
                        .Take(lines);
                    
                    // Display the lines in chronological order
                    foreach (var logLine in filteredLines.Reverse())
                    {
                        Console.WriteLine(logLine);
                    }
                    
                    // Count the total matching lines for the summary message
                    int matchingCount = allLines.Count(l => string.IsNullOrEmpty(filter) || l.ToLower().Contains(filter));
                    int shown = Math.Min(matchingCount, lines);
                    
                    Console.WriteLine($"\nShowing {shown} of {matchingCount} log entries" + 
                                    (string.IsNullOrEmpty(filter) ? "" : $" matching '{filter}'"));
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error reading logs: {ex.Message}");
            }
        }

        private void SetLogLevel(string[] args)
        {
            if (args.Length == 0)
            {
                Console.WriteLine("Current log level: " + Logger.GetCurrentLogLevel());
                Console.WriteLine("Usage: loglevel [error|warning|info|debug]");
                return;
            }
            
            string level = args[0].ToLower();
            switch (level)
            {
                case "error":
                    Logger.SetLogLevel(LogLevel.Error);
                    break;
                case "warning":
                    Logger.SetLogLevel(LogLevel.Warning);
                    break;
                case "info":
                    Logger.SetLogLevel(LogLevel.Info);
                    break;
                case "debug":
                    Logger.SetLogLevel(LogLevel.Debug);
                    break;
                default:
                    Console.WriteLine($"Unknown log level: {level}");
                    Console.WriteLine("Valid levels: error, warning, info, debug");
                    return;
            }
            
            Console.WriteLine($"Log level set to {level}");
        }
    }
}