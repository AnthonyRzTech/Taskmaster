using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace Taskmaster.Models
{
    // Enum to define restart policies
    public enum RestartPolicy
    {
        Always,
        Never,
        Unexpected
    }

    // Class that holds configuration for a single program
    public class ProgramConfig
    {
        // Required fields
        public string Name { get; set; } = string.Empty;
        public string Command { get; set; } = string.Empty;
        
        // Process control
        public int NumProcs { get; set; } = 1;
        public bool AutoStart { get; set; } = true;
        public RestartPolicy AutoRestart { get; set; } = RestartPolicy.Unexpected;
        public List<int> ExitCodes { get; set; } = new List<int> { 0 };
        public int StartRetries { get; set; } = 3;
        public int StartTime { get; set; } = 5; // seconds
        public string StopSignal { get; set; } = "TERM";
        public int StopTime { get; set; } = 10; // seconds
        
        // Environment settings
        public string WorkingDir { get; set; } = Directory.GetCurrentDirectory();
        public int Umask { get; set; } = 022;
        public Dictionary<string, string> Environment { get; set; } = new Dictionary<string, string>();
        
        // I/O settings
        public string? StdoutLogfile { get; set; } = null;
        public string? StderrLogfile { get; set; } = null;
        public bool DiscardOutput { get; set; } = false;
        
        // Additional settings
        public int StartSeconds { get; set; } = 1; // seconds between process starts
        public bool RedirectStdin { get; set; } = false;
        public string? StdinLogfile { get; set; } = null;
        public string? User { get; set; } = null;
        public string? Group { get; set; } = null;
        
        // Helper method to create process start info based on config
        public ProcessStartInfo CreateProcessStartInfo()
        {
            string[] cmdParts = Command.Split(new char[] { ' ' }, 2);
            string fileName = cmdParts[0];
            string arguments = cmdParts.Length > 1 ? cmdParts[1] : "";
            
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = RedirectStdin,
                WorkingDirectory = WorkingDir,
                CreateNoWindow = true
            };
            
            // Add environment variables
            foreach (var envVar in Environment)
            {
                psi.EnvironmentVariables[envVar.Key] = envVar.Value;
            }
            
            // On Unix, we could handle user/group if we have elevated privileges
            // This would require P/Invoke or a separate utility
            
            return psi;
        }
        
        // Validate configuration and provide helpful error messages
        public bool Validate(out string? error)
        {
            error = null;
            
            if (string.IsNullOrEmpty(Command))
            {
                error = $"Missing 'cmd' for program '{Name}'";
                return false;
            }
            
            if (NumProcs <= 0)
            {
                error = $"Invalid 'numprocs' value {NumProcs} for program '{Name}': must be greater than 0";
                return false;
            }
            
            if (StartRetries < 0)
            {
                error = $"Invalid 'startretries' value {StartRetries} for program '{Name}': must be non-negative";
                return false;
            }
            
            if (StartTime <= 0)
            {
                error = $"Invalid 'starttime' value {StartTime} for program '{Name}': must be greater than 0";
                return false;
            }
            
            if (StopTime <= 0)
            {
                error = $"Invalid 'stoptime' value {StopTime} for program '{Name}': must be greater than 0";
                return false;
            }
            
            // Validate umask (must be a valid octal number)
            if (Umask < 0 || Umask > 0777)
            {
                error = $"Invalid 'umask' value {Umask} for program '{Name}': must be between 0 and 0777 (octal)";
                return false;
            }
            
            return true;
        }
        
        // Create a deep copy of this configuration
        public ProgramConfig Clone()
        {
            var clone = new ProgramConfig
            {
                Name = Name,
                Command = Command,
                NumProcs = NumProcs,
                AutoStart = AutoStart,
                AutoRestart = AutoRestart,
                StartRetries = StartRetries,
                StartTime = StartTime,
                StopSignal = StopSignal,
                StopTime = StopTime,
                WorkingDir = WorkingDir,
                Umask = Umask,
                StdoutLogfile = StdoutLogfile,
                StderrLogfile = StderrLogfile,
                DiscardOutput = DiscardOutput,
                StartSeconds = StartSeconds,
                RedirectStdin = RedirectStdin,
                StdinLogfile = StdinLogfile,
                User = User,
                Group = Group
            };
            
            // Deep copy exit codes
            clone.ExitCodes = new List<int>(ExitCodes);
            
            // Deep copy environment variables
            clone.Environment = new Dictionary<string, string>(Environment);
            
            return clone;
        }
    }
}