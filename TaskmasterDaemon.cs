using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.IO;
using System.Text.Json;
using System.Text;
using Taskmaster.Utils;

namespace Taskmaster
{
    public class TaskmasterDaemon
    {
        private string ConfigFilePath { get; set; }
        private ConfigurationManager? ConfigManager { get; set; }
        private ProcessManager? ProcessManager { get; set; }
        private CancellationTokenSource? CancellationTokenSource { get; set; }
        public bool IsRunning { get; private set; } = false;
        
        // For handling signals on Unix platforms
        private bool SignalHandlersRegistered { get; set; } = false;
        
        private TcpListener? controlListener;
        private int controlPort = 9090;
        private HttpListener? httpListener;
        private int httpPort = 8080;
        
        public TaskmasterDaemon(string configFilePath)
        {
            ConfigFilePath = configFilePath;
            ConfigManager = new ConfigurationManager(configFilePath);
            
            // Initialize the logger
            Logger.Initialize();
        }
        
        public bool Start()
        {
            if (IsRunning)
                return false;
            
            CancellationTokenSource = new CancellationTokenSource();
            
            // Load configuration
            if (!ConfigManager!.LoadConfiguration())
            {
                Logger.Log("Failed to load configuration, exiting");
                return false;
            }
            
            // Initialize process manager
            ProcessManager = new ProcessManager(ConfigManager);
            
            // Register signal handlers
            RegisterSignalHandlers();
            
            // Start monitoring and status update task
            Task.Run(() => MonitoringLoop(CancellationTokenSource!.Token));
            
            // Start all programs configured to autostart
            ProcessManager.StartAllAutoStart();
            
            // Start TCP control interface and HTTP API
            StartControlServer();
            StartHttpServer();
            
            IsRunning = true;
            Logger.Log("Taskmaster daemon started");
            
            return true;
        }
        
        public void Stop()
        {
            if (!IsRunning)
                return;
            
            Logger.Log("Stopping Taskmaster daemon");
            
            IsRunning = false;
            CancellationTokenSource?.Cancel();
            
            // Stop control and HTTP servers
            controlListener?.Stop();
            httpListener?.Close();
            Logger.Log("Control and HTTP servers stopped");

            // Stop all running programs
            ProcessManager?.StopAllPrograms();
            
            Logger.Log("Taskmaster daemon stopped");
            Logger.Close();
        }
        
        // Status and control methods
        public List<ProcessStatusInfo> GetAllProcessStatus()
        {
            return ProcessManager?.GetAllProcessStatus() ?? new List<ProcessStatusInfo>();
        }
        
        public bool StartProgram(string programName)
        {
            return ProcessManager?.StartProgram(programName) ?? false;
        }
        
        public bool StopProgram(string programName)
        {
            return ProcessManager?.StopProgram(programName) ?? false;
        }
        
        public bool RestartProgram(string programName)
        {
            return ProcessManager?.RestartProgram(programName) ?? false;
        }
        
        public void StartAllPrograms()
        {
            if (ConfigManager?.Programs == null || ProcessManager == null)
                return;
                
            foreach (var program in ConfigManager.Programs.Values)
            {
                ProcessManager.StartProgram(program.Name);
            }
        }
        
        public void StopAllPrograms()
        {
            if (ConfigManager?.Programs == null || ProcessManager == null)
                return;
                
            foreach (var program in ConfigManager.Programs.Keys.ToList())
            {
                ProcessManager.StopProgram(program);
            }
        }
        
        public void RestartAllPrograms()
        {
            if (ConfigManager?.Programs == null || ProcessManager == null)
                return;
                
            foreach (var program in ConfigManager.Programs.Keys.ToList())
            {
                ProcessManager.RestartProgram(program);
            }
        }
        
        // Reload configuration
        public bool ReloadConfiguration()
        {
            try
            {
                Logger.Log("Reloading configuration...");
                
                if (ConfigManager == null || ProcessManager == null)
                    return false;
                
                // Save old configuration for comparison
                var oldConfig = new Dictionary<string, Models.ProgramConfig>(ConfigManager.Programs);
                
                // Load new configuration
                if (!ConfigManager.LoadConfiguration())
                {
                    Logger.Log("Failed to reload configuration");
                    return false;
                }
                
                // Apply changes
                ProcessManager.ApplyConfigChanges(oldConfig, ConfigManager.Programs);
                
                Logger.Log("Configuration reloaded successfully");
                return true;
            }
            catch (Exception ex)
            {
                Logger.Log($"Error reloading configuration: {ex.Message}");
                return false;
            }
        }
        
        // Get information about a specific program
        public Models.ProgramConfig? GetProgramConfig(string programName)
        {
            if (ConfigManager?.Programs != null && 
                ConfigManager.Programs.TryGetValue(programName, out var config))
            {
                return config;
            }
            return null;
        }
        
        // Get all program configurations
        public Dictionary<string, Models.ProgramConfig> GetAllProgramConfigs()
        {
            if (ConfigManager?.Programs == null)
                return new Dictionary<string, Models.ProgramConfig>();
                
            return ConfigManager.Programs;
        }
        
        // Signal handlers
        private void RegisterSignalHandlers()
        {
            if (SignalHandlersRegistered)
                return;
            
            // Register handlers for process exit on all platforms
            AppDomain.CurrentDomain.ProcessExit += (sender, e) => Stop();
            
            // On Unix systems, register handlers for various signals
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // Handle SIGHUP (reload configuration)
                SignalHandler.RegisterSignalHandler(SignalHandler.SIGHUP, (signal) => {
                    Logger.Log("Received SIGHUP signal, reloading configuration");
                    ReloadConfiguration();
                });
                
                // Handle SIGTERM (graceful shutdown)
                SignalHandler.RegisterSignalHandler(SignalHandler.SIGTERM, (signal) => {
                    Logger.Log("Received SIGTERM signal, shutting down");
                    Stop();
                });
                
                // Handle SIGINT (Ctrl+C, graceful shutdown)
                SignalHandler.RegisterSignalHandler(SignalHandler.SIGINT, (signal) => {
                    Logger.Log("Received SIGINT signal, shutting down");
                    Stop();
                });
                
                // Handle SIGUSR1 (custom action - could be used for status dump)
                SignalHandler.RegisterSignalHandler(SignalHandler.SIGUSR1, (signal) => {
                    Logger.Log("Received SIGUSR1 signal");
                    DumpStatusToLog();
                });
            }
            
            // For Windows, we already handle Ctrl+C in Program.cs
            
            SignalHandlersRegistered = true;
            Logger.Log("Signal handlers registered");
        }
        
        // Dump status information to log (used for SIGUSR1)
        private void DumpStatusToLog()
        {
            Logger.Log("--- Taskmaster Status Dump ---");
            
            if (ConfigManager?.Programs == null || ProcessManager == null)
            {
                Logger.Log("No configuration or process manager available");
                return;
            }
            
            var statuses = ProcessManager.GetAllProcessStatus();
            
            Logger.Log($"Total programs: {ConfigManager.Programs.Count}");
            Logger.Log($"Total processes: {statuses.Count}");
            
            foreach (var group in statuses.GroupBy(s => s.ProgramName))
            {
                Logger.Log($"Program: {group.Key}");
                foreach (var process in group)
                {
                    Logger.Log($"  Process #{process.ProcessNumber} (PID: {process.ProcessId}): {process.State}");
                }
            }
            
            Logger.Log("--- End Status Dump ---");
        }
        
        // Periodic monitoring loop
        private async Task MonitoringLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    // This could be used to implement additional monitoring
                    // For example, checking for zombied processes, etc.
                    
                    // Wait for a bit before next check
                    await Task.Delay(5000, token);
                }
                catch (TaskCanceledException)
                {
                    // Token cancelled, exit loop
                    break;
                }
                catch (Exception ex)
                {
                    Logger.Log($"Error in monitoring loop: {ex.Message}");
                }
            }
        }
        
        private void StartControlServer()
        {
            controlListener = new TcpListener(IPAddress.Loopback, controlPort);
            controlListener.Start();
            Logger.Log($"Control server listening on 127.0.0.1:{controlPort}");
            Task.Run(async () =>
            {
                while (CancellationTokenSource?.IsCancellationRequested == false)
                {
                    var client = await controlListener.AcceptTcpClientAsync();
                    HandleControlClient(client);
                }
            });
        }
        
        private void HandleControlClient(TcpClient client)
        {
            Task.Run(async () =>
            {
                using var stream = client.GetStream();
                using var reader = new StreamReader(stream);
                using var writer = new StreamWriter(stream) { AutoFlush = true };
                writer.WriteLine("Welcome to Taskmaster control interface");
                writer.WriteLine("Type 'help' for commands");
                writer.Write("> ");
                string? line;
                while ((line = await reader.ReadLineAsync()) != null)
                {
                    var parts = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length == 0)
                    {
                        writer.Write("> ");
                        continue;
                    }
                    var cmd = parts[0].ToLower();
                    var args = parts.Skip(1).ToArray();
                    switch (cmd)
                    {
                        case "status":
                            foreach (var s in GetAllProcessStatus()) writer.WriteLine(s.ToString());
                            break;
                        case "start":
                            if (args.Length > 0 && StartProgram(args[0])) writer.WriteLine($"Started {args[0]}"); else writer.WriteLine("Start failed");
                            break;
                        case "stop":
                            if (args.Length > 0 && StopProgram(args[0])) writer.WriteLine($"Stopped {args[0]}"); else writer.WriteLine("Stop failed");
                            break;
                        case "restart":
                            if (args.Length > 0 && RestartProgram(args[0])) writer.WriteLine($"Restarted {args[0]}"); else writer.WriteLine("Restart failed");
                            break;
                        case "reload":
                            if (ReloadConfiguration()) writer.WriteLine("Configuration reloaded"); else writer.WriteLine("Reload failed");
                            break;
                        case "shutdown":
                            writer.WriteLine("Shutting down daemon");
                            Stop();
                            break;
                        case "exit":
                        case "quit":
                            writer.WriteLine("Goodbye");
                            return;
                        case "help":
                            writer.WriteLine("Commands: status, start <prog>, stop <prog>, restart <prog>, reload, shutdown, exit");
                            break;
                        default:
                            writer.WriteLine("Unknown command");
                            break;
                    }
                    writer.Write("> ");
                }
            });
        }

        private void StartHttpServer()
        {
            httpListener = new HttpListener();
            httpListener.Prefixes.Add($"http://localhost:{httpPort}/");
            httpListener.Start();
            Logger.Log($"HTTP API listening on http://localhost:{httpPort}/api/");
            Task.Run(async () =>
            {
                while (CancellationTokenSource?.IsCancellationRequested == false)
                {
                    try
                    {
                        var ctx = await httpListener.GetContextAsync();
                        _ = Task.Run(() => HandleHttpContext(ctx));
                    }
                    catch (HttpListenerException) { break; }
                }
            });
        }

        private async Task HandleHttpContext(HttpListenerContext ctx)
        {
            var req = ctx.Request;
            var resp = ctx.Response;

            // Guard against null URL
            Uri? url = req.Url;
            if (url == null)
            {
                resp.StatusCode = 404;
                resp.Close();
                return;
            }
            string path = url.AbsolutePath.ToLower();
            if (path.StartsWith("/api/"))
            {
                if (req.HttpMethod == "GET" && path == "/api/status")
                {
                    var statuses = GetAllProcessStatus();
                    var json = JsonSerializer.Serialize(statuses);
                    var buffer = Encoding.UTF8.GetBytes(json);
                    resp.ContentType = "application/json";
                    await resp.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                }
                else if (req.HttpMethod == "POST")
                {
                    // Set ContentLength64 to 0 for empty responses to avoid HTTP 411 errors
                    resp.ContentLength64 = 0;
                    string result = "";
                    if (path.StartsWith("/api/programs/"))
                    {
                        var parts = path.Split('/');
                        if (parts.Length >= 4)
                        {
                            string program = parts[3];
                            switch (parts.Length > 4 ? parts[4] : "")
                            {
                                case "start": result = StartProgram(program) ? "ok" : "error"; break;
                                case "stop": result = StopProgram(program) ? "ok" : "error"; break;
                                case "restart": result = RestartProgram(program) ? "ok" : "error"; break;
                            }
                        }
                    }
                    else if (path == "/api/reload")
                        result = ReloadConfiguration() ? "ok" : "error";
                    else if (path == "/api/shutdown")
                    {
                        result = "shutting down";
                        Stop();
                    }
                    
                    if (!string.IsNullOrEmpty(result))
                    {
                        var buf = Encoding.UTF8.GetBytes(result);
                        resp.ContentLength64 = buf.Length;
                        await resp.OutputStream.WriteAsync(buf, 0, buf.Length);
                    }
                }
                else
                {
                    resp.StatusCode = 404;
                }
            }
            else
            {
                resp.StatusCode = 404;
            }
            resp.Close();
        }

        // Keep the daemon running in daemon mode
        public void RunDaemonLoop()
        {
            while (IsRunning)
            {
                Thread.Sleep(1000);
            }
        }

        public string GetLogFilePath()
        {
            return Logger.GetLogFilePath();
        }
    }
}