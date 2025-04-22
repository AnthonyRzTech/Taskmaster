using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using Taskmaster.Models;

namespace Taskmaster
{
    public class ConfigurationManager
    {
        private string ConfigFilePath { get; set; } = string.Empty;
        
        public Dictionary<string, ProgramConfig> Programs { get; private set; } = 
            new Dictionary<string, ProgramConfig>();
        
        // Global configuration settings
        public string LogFile { get; private set; } = "taskmaster.log";
        public int LogLevel { get; private set; } = 2; // 0=error, 1=warn, 2=info, 3=debug
        public string LogDirectory { get; private set; } = "logs";
        
        public ConfigurationManager(string configFilePath)
        {
            ConfigFilePath = configFilePath;
        }
        
        public bool LoadConfiguration()
        {
            try
            {
                Logger.Log($"Loading configuration from {ConfigFilePath}");
                
                string yamlContent = File.ReadAllText(ConfigFilePath);
                
                var deserializer = new DeserializerBuilder()
                    .WithNamingConvention(CamelCaseNamingConvention.Instance)
                    .Build();
                
                var rawConfig = deserializer.Deserialize<Dictionary<string, object>>(yamlContent);
                
                // Parse global settings first
                if (rawConfig.TryGetValue("global", out var globalObj) && 
                    globalObj is Dictionary<object, object> globalDict)
                {
                    // Global log file
                    if (globalDict.TryGetValue("logfile", out var logFileObj) && logFileObj != null)
                        LogFile = logFileObj.ToString() ?? "taskmaster.log";
                    
                    // Global log level
                    if (globalDict.TryGetValue("loglevel", out var logLevelObj))
                        LogLevel = Convert.ToInt32(logLevelObj);
                    
                    // Log directory
                    if (globalDict.TryGetValue("logdir", out var logDirObj) && logDirObj != null)
                        LogDirectory = logDirObj.ToString() ?? "logs";
                    
                    // Ensure log directory exists
                    if (!string.IsNullOrEmpty(LogDirectory) && !Directory.Exists(LogDirectory))
                    {
                        Directory.CreateDirectory(LogDirectory);
                        Logger.Log($"Created log directory: {LogDirectory}");
                    }
                }
                
                if (!rawConfig.TryGetValue("programs", out var programsObj) || 
                    !(programsObj is Dictionary<object, object> programsDict))
                {
                    Logger.Log("Error: 'programs' section not found in configuration");
                    return false;
                }
                
                // Parse each program configuration
                var newPrograms = new Dictionary<string, ProgramConfig>();
                var errors = new List<string>();
                
                foreach (var entry in programsDict)
                {
                    string programName = entry.Key.ToString() ?? string.Empty;
                    var programDict = entry.Value as Dictionary<object, object>;
                    
                    if (programDict == null)
                    {
                        errors.Add($"Invalid configuration for program '{programName}'");
                        continue;
                    }
                    
                    var config = new ProgramConfig { Name = programName };
                    
                    // Required: Command
                    if (!programDict.TryGetValue("cmd", out var cmdObj))
                    {
                        errors.Add($"Missing 'cmd' for program '{programName}'");
                        continue;
                    }
                    config.Command = cmdObj?.ToString() ?? string.Empty;
                    
                    // Optional settings with defaults
                    if (programDict.TryGetValue("numprocs", out var numProcsObj))
                        config.NumProcs = Convert.ToInt32(numProcsObj);
                    
                    if (programDict.TryGetValue("autostart", out var autoStartObj))
                        config.AutoStart = Convert.ToBoolean(autoStartObj);
                    
                    if (programDict.TryGetValue("autorestart", out var autoRestartObj) && autoRestartObj != null)
                    {
                        string autoRestartStr = autoRestartObj.ToString()?.ToLower() ?? "unexpected";
                        switch (autoRestartStr)
                        {
                            case "true":
                            case "always":
                                config.AutoRestart = RestartPolicy.Always;
                                break;
                            case "false":
                            case "never":
                                config.AutoRestart = RestartPolicy.Never;
                                break;
                            case "unexpected":
                                config.AutoRestart = RestartPolicy.Unexpected;
                                break;
                            default:
                                Logger.Log($"Warning: Invalid 'autorestart' value for program '{programName}', using default");
                                break;
                        }
                    }
                    
                    if (programDict.TryGetValue("exitcodes", out var exitCodesObj))
                    {
                        config.ExitCodes = new List<int>();
                        
                        if (exitCodesObj is List<object> exitCodesList)
                        {
                            foreach (var exitCodeObj in exitCodesList)
                            {
                                config.ExitCodes.Add(Convert.ToInt32(exitCodeObj));
                            }
                        }
                        else
                        {
                            // Single exit code
                            config.ExitCodes.Add(Convert.ToInt32(exitCodesObj));
                        }
                    }
                    
                    if (programDict.TryGetValue("startretries", out var startRetriesObj))
                        config.StartRetries = Convert.ToInt32(startRetriesObj);
                    
                    if (programDict.TryGetValue("starttime", out var startTimeObj))
                        config.StartTime = Convert.ToInt32(startTimeObj);
                    
                    if (programDict.TryGetValue("stopsignal", out var stopSignalObj) && stopSignalObj != null)
                        config.StopSignal = stopSignalObj.ToString() ?? "TERM";
                    
                    if (programDict.TryGetValue("stoptime", out var stopTimeObj))
                        config.StopTime = Convert.ToInt32(stopTimeObj);
                    
                    if (programDict.TryGetValue("startsecs", out var startSecsObj))
                        config.StartSeconds = Convert.ToInt32(startSecsObj);
                    
                    if (programDict.TryGetValue("stdout", out var stdoutObj) && stdoutObj != null)
                    {
                        string? stdoutPath = stdoutObj.ToString();
                        if (stdoutPath != null && !Path.IsPathRooted(stdoutPath) && !string.IsNullOrEmpty(LogDirectory))
                        {
                            stdoutPath = Path.Combine(LogDirectory, stdoutPath);
                        }
                        config.StdoutLogfile = stdoutPath;
                    }
                    
                    if (programDict.TryGetValue("stderr", out var stderrObj) && stderrObj != null)
                    {
                        string? stderrPath = stderrObj.ToString();
                        if (stderrPath != null && !Path.IsPathRooted(stderrPath) && !string.IsNullOrEmpty(LogDirectory))
                        {
                            stderrPath = Path.Combine(LogDirectory, stderrPath);
                        }
                        config.StderrLogfile = stderrPath;
                    }
                    
                    if (programDict.TryGetValue("workingdir", out var workingDirObj) && workingDirObj != null)
                        config.WorkingDir = workingDirObj.ToString() ?? Directory.GetCurrentDirectory();
                    
                    if (programDict.TryGetValue("umask", out var umaskObj) && umaskObj != null)
                    {
                        string? umaskStr = umaskObj.ToString();
                        if (umaskStr != null)
                        {
                            if (umaskStr.StartsWith("0")) // Octal
                                config.Umask = Convert.ToInt32(umaskStr, 8);
                            else
                                config.Umask = Convert.ToInt32(umaskStr);
                        }
                    }
                    
                    // Optional user/group settings (for Unix systems)
                    if (programDict.TryGetValue("user", out var userObj) && userObj != null)
                        config.User = userObj.ToString();
                    
                    if (programDict.TryGetValue("group", out var groupObj) && groupObj != null)
                        config.Group = groupObj.ToString();
                    
                    // Redirect stdin
                    if (programDict.TryGetValue("redirectstdin", out var redirectStdinObj))
                        config.RedirectStdin = Convert.ToBoolean(redirectStdinObj);
                    
                    if (programDict.TryGetValue("stdin", out var stdinObj) && stdinObj != null)
                    {
                        string? stdinPath = stdinObj.ToString();
                        if (stdinPath != null && !Path.IsPathRooted(stdinPath) && !string.IsNullOrEmpty(LogDirectory))
                        {
                            stdinPath = Path.Combine(LogDirectory, stdinPath);
                        }
                        config.StdinLogfile = stdinPath;
                        config.RedirectStdin = true;
                    }
                    
                    // Discard output
                    if (programDict.TryGetValue("discardoutput", out var discardOutputObj))
                        config.DiscardOutput = Convert.ToBoolean(discardOutputObj);
                    
                    // Parse environment variables
                    if (programDict.TryGetValue("env", out var envObj) && 
                        envObj is Dictionary<object, object> envDict)
                    {
                        foreach (var envVar in envDict)
                        {
                            string key = envVar.Key?.ToString() ?? string.Empty;
                            string value = envVar.Value?.ToString() ?? string.Empty;
                            config.Environment[key] = value;
                        }
                    }
                    
                    // Validate the configuration
                    if (config.Validate(out string? error))
                    {
                        newPrograms[programName] = config;
                    }
                    else
                    {
                        errors.Add(error ?? $"Unknown validation error in program '{programName}'");
                    }
                }
                
                // Report any errors
                if (errors.Count > 0)
                {
                    foreach (var error in errors)
                    {
                        Logger.Log($"Configuration error: {error}");
                    }
                    
                    if (newPrograms.Count == 0)
                    {
                        Logger.Log("No valid program configurations found");
                        return false;
                    }
                    
                    Logger.Log($"Warning: {errors.Count} configuration errors found, but continuing with {newPrograms.Count} valid programs");
                }
                
                Programs = newPrograms;
                Logger.Log($"Successfully loaded {Programs.Count} program configurations");
                return true;
            }
            catch (Exception ex)
            {
                Logger.Log($"Error loading configuration: {ex.Message}");
                return false;
            }
        }
    }
}