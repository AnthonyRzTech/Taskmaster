using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Taskmaster.Models;

namespace Taskmaster
{
    public class ProcessManager
    {
        private Dictionary<string, List<ProcessInfo?>> RunningProcesses { get; set; } = 
            new Dictionary<string, List<ProcessInfo?>>();
        
        private ConfigurationManager ConfigManager { get; set; }
        
        public ProcessManager(ConfigurationManager configManager)
        {
            ConfigManager = configManager ?? throw new ArgumentNullException(nameof(configManager));
        }
        
        // Start all programs that are configured for autostart
        public void StartAllAutoStart()
        {
            foreach (var program in ConfigManager.Programs.Values)
            {
                if (program.AutoStart)
                {
                    StartProgram(program.Name);
                }
            }
        }
        
        // Start a specific program with all its processes
        public bool StartProgram(string programName)
        {
            if (!ConfigManager.Programs.TryGetValue(programName, out var config))
            {
                Logger.Log($"Unknown program: {programName}");
                return false;
            }
            
            Logger.Log($"Starting program: {programName}");
            
            if (!RunningProcesses.TryGetValue(programName, out var processes))
            {
                processes = new List<ProcessInfo?>();
                RunningProcesses[programName] = processes;
            }
            
            bool success = true;
            
            // Start the required number of processes
            for (int i = 0; i < config.NumProcs; i++)
            {
                // Check if we already have a process at this index
                ProcessInfo? existingProcess = null;
                if (i < processes.Count)
                {
                    existingProcess = processes[i];
                }
                
                // If process exists and is running, skip it
                if (existingProcess != null && 
                    (existingProcess.State == ProcessState.Running || 
                     existingProcess.State == ProcessState.Starting))
                {
                    continue;
                }
                
                // If we have an existing process but it's not running, dispose it
                if (existingProcess != null)
                {
                    existingProcess.Dispose();
                    processes[i] = null;
                }
                
                // Create and start a new process
                var procInfo = new ProcessInfo(config, i);
                
                if (i < processes.Count)
                {
                    processes[i] = procInfo;
                }
                else
                {
                    processes.Add(procInfo);
                }
                
                if (!procInfo.Start())
                {
                    success = false;
                    Logger.Log($"Failed to start {programName} process #{i}");
                }
                else
                {
                    Logger.Log($"Started {programName} process #{i} (PID: {procInfo.ProcessId})");
                }
            }
            
            return success;
        }
        
        // Stop a specific program and all its processes
        public bool StopProgram(string programName, bool force = false)
        {
            if (!RunningProcesses.TryGetValue(programName, out var processes))
            {
                Logger.Log($"No running processes found for program: {programName}");
                return false;
            }
            
            Logger.Log($"Stopping program: {programName}");
            
            bool success = true;
            foreach (var process in processes)
            {
                if (process != null && 
                    (process.State == ProcessState.Running || process.State == ProcessState.Starting))
                {
                    if (!process.Stop(force))
                    {
                        success = false;
                        Logger.Log($"Failed to stop {programName} process #{process.ProcNumber}");
                    }
                    else
                    {
                        Logger.Log($"Stopped {programName} process #{process.ProcNumber}");
                    }
                }
            }
            
            return success;
        }
        
        // Restart a specific program
        public bool RestartProgram(string programName)
        {
            Logger.Log($"Restarting program: {programName}");
            
            if (StopProgram(programName))
            {
                // Give processes a moment to stop
                Thread.Sleep(1000);
                return StartProgram(programName);
            }
            return false;
        }
        
        // Apply configuration changes when config is reloaded
        public void ApplyConfigChanges(Dictionary<string, ProgramConfig> oldConfig, 
                                       Dictionary<string, ProgramConfig> newConfig)
        {
            // Find programs that are deleted in the new configuration
            var deletedPrograms = oldConfig.Keys
                .Where(name => !newConfig.ContainsKey(name))
                .ToList();
            
            // Stop all deleted programs
            foreach (var programName in deletedPrograms)
            {
                Logger.Log($"Program removed from configuration: {programName}");
                StopProgram(programName, true);
                RunningProcesses.Remove(programName);
            }
            
            // Check for changed programs
            foreach (var newEntry in newConfig)
            {
                string programName = newEntry.Key;
                var newProgramConfig = newEntry.Value;
                
                if (oldConfig.TryGetValue(programName, out var oldProgramConfig))
                {
                    // Program exists in old config - check if it changed
                    bool significantChange = HasSignificantChanges(oldProgramConfig, newProgramConfig);
                    
                    if (significantChange)
                    {
                        Logger.Log($"Program configuration changed significantly: {programName}");
                        
                        // Stop and restart the program with new config
                        StopProgram(programName);
                        
                        // Wait for processes to stop
                        Thread.Sleep(1000);
                        
                        // Start with new config if autostart is true
                        if (newProgramConfig.AutoStart)
                        {
                            StartProgram(programName);
                        }
                    }
                    else if (newProgramConfig.AutoStart && !oldProgramConfig.AutoStart)
                    {
                        // AutoStart changed from false to true
                        Logger.Log($"Program set to autostart: {programName}");
                        StartProgram(programName);
                    }
                    else if (!newProgramConfig.AutoStart && oldProgramConfig.AutoStart)
                    {
                        // AutoStart changed from true to false
                        Logger.Log($"Program set to not autostart: {programName}");
                        // Note: We don't stop currently running processes
                    }
                }
                else
                {
                    // New program added
                    Logger.Log($"New program added to configuration: {programName}");
                    
                    if (newProgramConfig.AutoStart)
                    {
                        StartProgram(programName);
                    }
                }
            }
        }
        
        // Check if configuration changes require process restart
        private bool HasSignificantChanges(ProgramConfig oldConfig, ProgramConfig newConfig)
        {
            // Changes that require restart
            if (oldConfig.Command != newConfig.Command ||
                oldConfig.NumProcs != newConfig.NumProcs ||
                oldConfig.StopSignal != newConfig.StopSignal ||
                oldConfig.StopTime != newConfig.StopTime ||
                oldConfig.WorkingDir != newConfig.WorkingDir ||
                oldConfig.Umask != newConfig.Umask)
            {
                return true;
            }
            
            // Check environment variables
            if (!AreEnvironmentsEqual(oldConfig.Environment, newConfig.Environment))
            {
                return true;
            }
            
            // Changes to stdout/stderr handling
            if (oldConfig.StdoutLogfile != newConfig.StdoutLogfile ||
                oldConfig.StderrLogfile != newConfig.StderrLogfile ||
                oldConfig.DiscardOutput != newConfig.DiscardOutput)
            {
                return true;
            }
            
            return false;
        }
        
        private bool AreEnvironmentsEqual(Dictionary<string, string> env1, Dictionary<string, string> env2)
        {
            if (env1.Count != env2.Count)
                return false;
                
            foreach (var pair in env1)
            {
                if (!env2.TryGetValue(pair.Key, out var value) || value != pair.Value)
                {
                    return false;
                }
            }
            
            return true;
        }
        
        // Get status of all processes
        public List<ProcessStatusInfo> GetAllProcessStatus()
        {
            var statusList = new List<ProcessStatusInfo>();
            
            foreach (var entry in RunningProcesses)
            {
                string programName = entry.Key;
                var processes = entry.Value;
                
                foreach (var process in processes)
                {
                    if (process != null)
                    {
                        statusList.Add(new ProcessStatusInfo
                        {
                            ProgramName = programName,
                            ProcessNumber = process.ProcNumber,
                            ProcessId = process.ProcessId,
                            State = process.State,
                            StartTime = process.StartTime,
                            RestartCount = process.RestartCount
                        });
                    }
                }
            }
            
            return statusList;
        }
        
        // Cleanup all processes during shutdown
        public void StopAllPrograms()
        {
            Logger.Log("Stopping all programs...");
            
            foreach (var entry in RunningProcesses)
            {
                try
                {
                    StopProgram(entry.Key);
                }
                catch (Exception ex)
                {
                    Logger.Log($"Error stopping program {entry.Key}: {ex.Message}");
                }
            }
            
            // Wait a bit for graceful shutdown
            Thread.Sleep(1000);
            
            // Final cleanup - force kill anything still running
            foreach (var entry in RunningProcesses)
            {
                foreach (var process in entry.Value)
                {
                    process?.Dispose();
                }
            }
            
            RunningProcesses.Clear();
        }
    }
    
    // Class for returning process status information
    public class ProcessStatusInfo
    {
        public string ProgramName { get; set; } = string.Empty;
        public int ProcessNumber { get; set; }
        public int ProcessId { get; set; }
        public ProcessState State { get; set; }
        public DateTime StartTime { get; set; }
        public int RestartCount { get; set; }
        
        public override string ToString()
        {
            string timeRunning = "";
            if (State == ProcessState.Running || State == ProcessState.Starting)
            {
                TimeSpan duration = DateTime.Now - StartTime;
                timeRunning = $", up for {FormatTimeSpan(duration)}";
            }
            
            return $"{ProgramName}-{ProcessNumber} (pid {ProcessId}): {State}{timeRunning}";
        }
        
        private string FormatTimeSpan(TimeSpan span)
        {
            if (span.TotalDays >= 1)
                return $"{(int)span.TotalDays}d {span.Hours}h {span.Minutes}m";
            else if (span.TotalHours >= 1)
                return $"{(int)span.TotalHours}h {span.Minutes}m {span.Seconds}s";
            else if (span.TotalMinutes >= 1)
                return $"{(int)span.TotalMinutes}m {span.Seconds}s";
            else
                return $"{span.Seconds}s";
        }
    }
}