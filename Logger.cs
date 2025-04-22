using System;
using System.IO;

namespace Taskmaster
{
    // Log level enumeration
    public enum LogLevel
    {
        Error = 0,
        Warning = 1,
        Info = 2,
        Debug = 3
    }
    
    public static class Logger
    {
        private static readonly object LogLock = new object();
        private static StreamWriter? LogWriter;
        private static string LogFilePath = "taskmaster.log";
        private static LogLevel CurrentLogLevel = LogLevel.Info;
        
        public static void Initialize(string? logFile = null, LogLevel logLevel = LogLevel.Info)
        {
            if (!string.IsNullOrEmpty(logFile))
            {
                LogFilePath = logFile;
            }
            
            CurrentLogLevel = logLevel;
            
            try
            {
                // Ensure directory exists
                string? directory = Path.GetDirectoryName(LogFilePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                
                // Open log file with shared read access so it can be viewed while running
                LogWriter = new StreamWriter(new FileStream(LogFilePath, FileMode.Append, FileAccess.Write, FileShare.Read));
                LogWriter.AutoFlush = true;
                
                Log($"Taskmaster logger initialized with log level {CurrentLogLevel}", LogLevel.Info);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Failed to initialize logger: {ex.Message}");
                // Fall back to console-only logging
            }
        }
        
        public static void Log(string message, LogLevel level = LogLevel.Info)
        {
            // Only log if the message level is less than or equal to current level
            if (level > CurrentLogLevel)
                return;
                
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            string levelStr = level.ToString().ToUpper().PadRight(7);
            string formattedMessage = $"[{timestamp}] [{levelStr}] {message}";
            
            lock (LogLock)
            {
                try
                {
                    // Write to log file
                    LogWriter?.WriteLine(formattedMessage);
                    
                    // Also output to console with color based on level
                    ConsoleColor originalColor = Console.ForegroundColor;
                    
                    // Set color based on log level
                    switch (level)
                    {
                        case LogLevel.Error:
                            Console.ForegroundColor = ConsoleColor.Red;
                            break;
                        case LogLevel.Warning:
                            Console.ForegroundColor = ConsoleColor.Yellow;
                            break;
                        case LogLevel.Info:
                            Console.ForegroundColor = ConsoleColor.White;
                            break;
                        case LogLevel.Debug:
                            Console.ForegroundColor = ConsoleColor.Gray;
                            break;
                    }
                    
                    Console.WriteLine(formattedMessage);
                    Console.ForegroundColor = originalColor;
                }
                catch (Exception ex)
                {
                    // Last resort fallback if something goes wrong with logging itself
                    Console.Error.WriteLine($"Error in logger: {ex.Message}");
                }
            }
        }
        
        // Helper methods for different log levels
        public static void Error(string message) => Log(message, LogLevel.Error);
        public static void Warning(string message) => Log(message, LogLevel.Warning);
        public static void Info(string message) => Log(message, LogLevel.Info);
        public static void Debug(string message) => Log(message, LogLevel.Debug);
        
        // Set current log level
        public static void SetLogLevel(LogLevel level)
        {
            CurrentLogLevel = level;
            Log($"Log level changed to {level}", LogLevel.Info);
        }
        
        public static void Close()
        {
            lock (LogLock)
            {
                try
                {
                    Log("Closing logger", LogLevel.Info);
                    LogWriter?.Flush();
                    LogWriter?.Dispose();
                    LogWriter = null;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Error closing logger: {ex.Message}");
                }
            }
        }
    }
}