using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.InteropServices;

namespace Taskmaster.Models
{
    // Possible states of a managed process
    public enum ProcessState
    {
        Starting,
        Running,
        Stopping,
        Stopped,
        Fatal,
        Backoff
    }

    // Class that tracks a single running process
    public class ProcessInfo : IDisposable
    {
        // Reference to the parent configuration and process
        public ProgramConfig Config { get; private set; }
        public Process? Process { get; private set; }
        
        // Process metadata
        public int ProcessId { get; private set; }
        public int ProcNumber { get; private set; }
        public ProcessState State { get; private set; } = ProcessState.Stopped;
        public DateTime StartTime { get; private set; }
        public int RestartCount { get; private set; } = 0;
        
        // Output handling
        private StreamWriter? StdoutWriter { get; set; }
        private StreamWriter? StderrWriter { get; set; }
        private readonly object _streamLock = new object();
        private bool _isDisposing = false;
        
        // Success tracking
        private bool IsStarting { get; set; } = false;
        private DateTime StartAttemptTime { get; set; }
        private CancellationTokenSource? StartCts { get; set; }
        
        public ProcessInfo(ProgramConfig config, int procNumber)
        {
            Config = config;
            ProcNumber = procNumber;
            
            // Set up output writers if configured
            if (!string.IsNullOrEmpty(config.StdoutLogfile))
            {
                string logPath = config.StdoutLogfile;
                if (config.NumProcs > 1)
                {
                    // Append process number to log file for multiple instances
                    string ext = Path.GetExtension(logPath);
                    logPath = Path.ChangeExtension(logPath, null) + $"-{procNumber}" + ext;
                }
                
                // Ensure directory exists
                logPath = EnsureDirectoryExists(logPath);
                
                StdoutWriter = new StreamWriter(new FileStream(logPath, FileMode.Append, FileAccess.Write, FileShare.Read));
                StdoutWriter.AutoFlush = true;
            }
            
            if (!string.IsNullOrEmpty(config.StderrLogfile))
            {
                string logPath = config.StderrLogfile;
                if (config.NumProcs > 1)
                {
                    // Append process number to log file for multiple instances
                    string ext = Path.GetExtension(logPath);
                    logPath = Path.ChangeExtension(logPath, null) + $"-{procNumber}" + ext;
                }
                
                // Ensure directory exists
                logPath = EnsureDirectoryExists(logPath);
                
                StderrWriter = new StreamWriter(new FileStream(logPath, FileMode.Append, FileAccess.Write, FileShare.Read));
                StderrWriter.AutoFlush = true;
            }
        }
        
        // Helper method to ensure log directory exists
        private string EnsureDirectoryExists(string filePath)
        {
            // Fix potential duplicate "logs/" prefix in path
            if (filePath.Contains("logs/logs/") || filePath.Contains("logs\\logs\\"))
            {
                filePath = filePath.Replace("logs/logs/", "logs/").Replace("logs\\logs\\", "logs\\");
                Logger.Log($"Fixed duplicate logs path: {filePath}");
            }
            
            string? directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                try
                {
                    Directory.CreateDirectory(directory);
                    Logger.Log($"Created directory: {directory}");
                }
                catch (Exception ex)
                {
                    Logger.Log($"Error creating directory {directory}: {ex.Message}");
                }
            }
            return filePath;
        }
        
        // Helper method to set umask (Unix only)
        private ProcessStartInfo SetUmask()
        {
            if (Environment.OSVersion.Platform == PlatformID.Unix)
            {
                try
                {
                    // On Unix systems, we'd use P/Invoke to call umask
                    // This is a simplified implementation
                    var processInfo = new ProcessStartInfo
                    {
                        FileName = "sh",
                        Arguments = $"-c \"umask {Config.Umask.ToString("000", System.Globalization.CultureInfo.InvariantCulture)} && exec {Config.Command}\"",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        WorkingDirectory = Config.WorkingDir,
                        CreateNoWindow = true
                    };
                    
                    // Add environment variables
                    foreach (var envVar in Config.Environment)
                    {
                        processInfo.EnvironmentVariables[envVar.Key] = envVar.Value;
                    }
                    
                    return processInfo;
                }
                catch (Exception ex)
                {
                    Logger.Log($"Error setting umask: {ex.Message}");
                }
            }
            
            // Fall back to normal process creation
            return Config.CreateProcessStartInfo();
        }
        
        // Start the process and begin monitoring
        public bool Start()
        {
            if (State == ProcessState.Running || State == ProcessState.Starting)
                return false;
            
            try
            {
                var psi = SetUmask();
                Process = new Process
                {
                    StartInfo = psi,
                    EnableRaisingEvents = true
                };
                
                // Set up event handlers for output
                Process.OutputDataReceived += OnOutputDataReceived;
                Process.ErrorDataReceived += OnErrorDataReceived;
                
                // Start the process
                Process.Start();
                Process.BeginOutputReadLine();
                Process.BeginErrorReadLine();
                
                ProcessId = Process.Id;
                StartTime = DateTime.Now;
                State = ProcessState.Starting;
                
                // Set up "successful start" timer
                StartAttemptTime = DateTime.Now;
                StartCts = new CancellationTokenSource();
                IsStarting = true;
                
                // Wait for the process to be "successfully started"
                Task.Delay(TimeSpan.FromSeconds(Config.StartTime), StartCts.Token)
                    .ContinueWith(t => 
                    {
                        if (!t.IsCanceled && IsStarting && State == ProcessState.Starting)
                        {
                            IsStarting = false;
                            if (Process != null && !Process.HasExited)
                            {
                                State = ProcessState.Running;
                                RestartCount = 0; // Reset restart count on successful start
                            }
                        }
                    });
                
                // Set up exit handler
                Process.Exited += OnProcessExited;
                
                return true;
            }
            catch (Exception ex)
            {
                State = ProcessState.Fatal;
                Logger.Log($"Failed to start process {Config.Name}-{ProcNumber}: {ex.Message}");
                return false;
            }
        }
        
        // Stop the process
        public bool Stop(bool force = false)
        {
            if (Process == null || State == ProcessState.Stopped || State == ProcessState.Stopping)
                return false;
            
            State = ProcessState.Stopping;
            
            try
            {
                if (force)
                {
                    Process.Kill();
                    return true;
                }
                
                // Try graceful shutdown first
                if (!Process.HasExited)
                {
                    if (Utils.SignalHandler.SendSignalByName(Config.StopSignal, ProcessId))
                    {
                        // Signal sent successfully
                    }
                    else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    {
                        // On Windows, fall back to CloseMainWindow
                        Process.CloseMainWindow();
                    }
                    else
                    {
                        // If signal sending failed, try to kill directly
                        Process.Kill();
                    }
                    
                    // Wait for the configured stop time
                    bool exited = Process.WaitForExit((int)(Config.StopTime * 1000));
                    
                    if (!exited)
                    {
                        // If graceful shutdown didn't work, force kill
                        Process.Kill();
                    }
                }
                
                State = ProcessState.Stopped;
                return true;
            }
            catch (Exception ex)
            {
                Logger.Log($"Error stopping process {Config.Name}-{ProcNumber}: {ex.Message}");
                return false;
            }
        }
        
        // Handle process exit
        private void OnProcessExited(object? sender, EventArgs e)
        {
            if (Process == null) return;
            
            int exitCode = Process.ExitCode;
            bool expectedExit = Config.ExitCodes.Contains(exitCode);
            
            // Cancel the start timer if it's still running
            if (IsStarting)
            {
                IsStarting = false;
                StartCts?.Cancel();
            }
            
            // If the process was stopping, mark it as stopped
            if (State == ProcessState.Stopping)
            {
                State = ProcessState.Stopped;
                Logger.Log($"Process {Config.Name}-{ProcNumber} stopped with exit code {exitCode}");
                return;
            }
            
            // Otherwise, handle unexpected or expected exits based on policy
            if (Config.AutoRestart == RestartPolicy.Always || 
                (Config.AutoRestart == RestartPolicy.Unexpected && !expectedExit))
            {
                // Decide whether to restart based on retry count
                if (RestartCount < Config.StartRetries)
                {
                    RestartCount++;
                    State = ProcessState.Backoff;
                    Logger.Log($"Process {Config.Name}-{ProcNumber} exited with code {exitCode}, " + 
                              $"will restart (attempt {RestartCount} of {Config.StartRetries})");
                    
                    // Simple exponential backoff for restart attempts
                    int backoffSeconds = Math.Min(20, (int)Math.Pow(2, RestartCount - 1));
                    Task.Delay(TimeSpan.FromSeconds(backoffSeconds))
                        .ContinueWith(_ => Start());
                }
                else
                {
                    State = ProcessState.Fatal;
                    Logger.Log($"Process {Config.Name}-{ProcNumber} exited with code {exitCode}, " +
                              $"exceeded maximum restart attempts ({Config.StartRetries})");
                }
            }
            else
            {
                State = ProcessState.Stopped;
                Logger.Log($"Process {Config.Name}-{ProcNumber} exited with code {exitCode}, " +
                          $"not restarting (expected: {expectedExit})");
            }
        }
        
        // Handle stdout data
        private void OnOutputDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (e.Data == null || _isDisposing) return;
            
            lock (_streamLock)
            {
                try
                {
                    if (StdoutWriter != null && !Config.DiscardOutput)
                    {
                        StdoutWriter.WriteLine(e.Data);
                    }
                }
                catch (ObjectDisposedException)
                {
                    // Stream was already closed, ignore
                }
                catch (Exception ex)
                {
                    // Don't use Logger here to avoid potential deadlocks
                    Console.Error.WriteLine($"Error writing to stdout: {ex.Message}");
                }
            }
        }
        
        // Handle stderr data
        private void OnErrorDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (e.Data == null || _isDisposing) return;
            
            lock (_streamLock)
            {
                try
                {
                    if (StderrWriter != null && !Config.DiscardOutput)
                    {
                        StderrWriter.WriteLine(e.Data);
                    }
                }
                catch (ObjectDisposedException)
                {
                    // Stream was already closed, ignore
                }
                catch (Exception ex)
                {
                    // Don't use Logger here to avoid potential deadlocks
                    Console.Error.WriteLine($"Error writing to stderr: {ex.Message}");
                }
            }
        }
        
        // Clean up resources
        public void Dispose()
        {
            _isDisposing = true;
            
            if (Process != null)
            {
                try
                {
                    // Unregister event handlers first
                    try
                    {
                        Process.OutputDataReceived -= OnOutputDataReceived;
                        Process.ErrorDataReceived -= OnErrorDataReceived;
                        Process.Exited -= OnProcessExited;
                    }
                    catch { /* Ignore errors during event handler removal */ }
                    
                    if (!Process.HasExited)
                    {
                        Process.Kill();
                    }
                    Process.Dispose();
                }
                catch (Exception) { /* Ignore errors during disposal */ }
                Process = null;
            }
            
            lock (_streamLock)
            {
                try
                {
                    if (StdoutWriter != null)
                    {
                        StdoutWriter.Flush();
                        StdoutWriter.Close();
                        StdoutWriter.Dispose();
                        StdoutWriter = null;
                    }
                    
                    if (StderrWriter != null)
                    {
                        StderrWriter.Flush();
                        StderrWriter.Close();
                        StderrWriter.Dispose();
                        StderrWriter = null;
                    }
                }
                catch (Exception ex) 
                {
                    // Log the error but continue with disposal
                    Console.Error.WriteLine($"Error during stream disposal: {ex.Message}");
                }
            }
            
            StartCts?.Dispose();
            StartCts = null;
        }
    }
}