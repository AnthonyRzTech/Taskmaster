using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Taskmaster.Models;

namespace Taskmaster.Utils
{
    /// <summary>
    /// Provides enhanced console visualization capabilities similar to htop/btop
    /// </summary>
    public static class ConsoleVisualizer
    {
        // ANSI color codes
        private static class Colors
        {
            public const string Reset = "\u001b[0m";
            public const string Bold = "\u001b[1m";
            public const string Underline = "\u001b[4m";
            public const string Reverse = "\u001b[7m";
            
            public const string Black = "\u001b[30m";
            public const string Red = "\u001b[31m";
            public const string Green = "\u001b[32m";
            public const string Yellow = "\u001b[33m";
            public const string Blue = "\u001b[34m";
            public const string Magenta = "\u001b[35m";
            public const string Cyan = "\u001b[36m";
            public const string White = "\u001b[37m";
            
            public const string BgBlack = "\u001b[40m";
            public const string BgRed = "\u001b[41m";
            public const string BgGreen = "\u001b[42m";
            public const string BgYellow = "\u001b[43m";
            public const string BgBlue = "\u001b[44m";
            public const string BgMagenta = "\u001b[45m";
            public const string BgCyan = "\u001b[46m";
            public const string BgWhite = "\u001b[47m";
        }

        /// <summary>
        /// Renders a full dashboard showing all process information
        /// </summary>
        public static void RenderDashboard(List<ProcessStatusInfo> statuses, Dictionary<string, ProgramConfig> configs, bool fullRedraw = false)
        {
            if (statuses == null || configs == null)
                return;

            // Only clear the screen on first draw or when explicitly requested
            if (fullRedraw)
            {
                Console.Clear();
            }
            else
            {
                // Reset cursor to top-left corner and clear from cursor to end of screen
                Console.SetCursorPosition(0, 0);
                Console.Write("\u001b[J"); // ANSI escape code to clear from cursor to end of screen
            }
            
            // Draw header
            DrawHeader();
            
            // Draw summary statistics
            DrawSummaryStats(statuses);
            
            // Draw process table
            DrawProcessTable(statuses, configs);
            
            // Draw footer with help
            DrawFooter();
        }

        private static void DrawHeader()
        {
            int width = Console.WindowWidth;
            string title = "TASKMASTER PROCESS MONITOR";
            
            Console.WriteLine();
            Console.Write(Colors.BgBlue + Colors.White + Colors.Bold);
            Console.Write(new string(' ', width));
            Console.SetCursorPosition((width - title.Length) / 2, Console.CursorTop);
            Console.Write(title);
            Console.SetCursorPosition(width - 25, Console.CursorTop);
            Console.Write($"Press 'h' for help");
            Console.WriteLine(Colors.Reset);
            Console.WriteLine();
        }

        private static void DrawSummaryStats(List<ProcessStatusInfo> statuses)
        {
            int running = statuses.Count(s => s.State == ProcessState.Running);
            int starting = statuses.Count(s => s.State == ProcessState.Starting);
            int stopped = statuses.Count(s => s.State == ProcessState.Stopped);
            int fatal = statuses.Count(s => s.State == ProcessState.Fatal);
            int total = statuses.Count;
            
            int width = Console.WindowWidth;
            int statWidth = width / 4;
            
            Console.WriteLine($"{Colors.Bold}Summary:{Colors.Reset}  [Total: {total}]");
            Console.Write(Colors.Green + $"Running: {running}".PadRight(statWidth) + Colors.Reset);
            Console.Write(Colors.Yellow + $"Starting: {starting}".PadRight(statWidth) + Colors.Reset);
            Console.Write(Colors.Blue + $"Stopped: {stopped}".PadRight(statWidth) + Colors.Reset);
            Console.Write(Colors.Red + $"Failed: {fatal}".PadRight(statWidth) + Colors.Reset);
            Console.WriteLine("\n");
        }

        private static void DrawProcessTable(List<ProcessStatusInfo> statuses, Dictionary<string, ProgramConfig> configs)
        {
            // Table header
            Console.WriteLine($"{Colors.Bold}{Colors.Underline}{"PROGRAM".PadRight(20)} {"PID".PadRight(8)} {"STATE".PadRight(12)} {"UPTIME".PadRight(15)} {"RESTARTS".PadRight(10)} {"COMMAND".PadRight(30)}{Colors.Reset}");
            
            // Group by program name
            foreach (var group in statuses.GroupBy(s => s.ProgramName))
            {
                string programName = group.Key;
                bool hasConfig = configs.TryGetValue(programName, out var config);
                string command = hasConfig && config != null ? config.Command : "Unknown";
                
                foreach (var process in group)
                {
                    string stateColor = GetStateColor(process.State);
                    string uptime = GetFormattedUptime(process);
                    
                    Console.Write($"{Colors.Bold}{programName}-{process.ProcessNumber}{Colors.Reset}".PadRight(20));
                    Console.Write($"{process.ProcessId}".PadRight(8));
                    Console.Write($"{stateColor}{process.State}{Colors.Reset}".PadRight(12 + stateColor.Length + Colors.Reset.Length));
                    Console.Write($"{uptime}".PadRight(15));
                    Console.Write($"{process.RestartCount}".PadRight(10));
                    Console.WriteLine($"{TruncateString(command, 30)}");
                }
            }
        }

        private static string GetStateColor(ProcessState state)
        {
            return state switch
            {
                ProcessState.Running => Colors.Green,
                ProcessState.Starting => Colors.Yellow,
                ProcessState.Stopped => Colors.Blue,
                ProcessState.Fatal => Colors.Red,
                _ => ""
            };
        }

        private static string GetFormattedUptime(ProcessStatusInfo process)
        {
            if (process.State != ProcessState.Running && process.State != ProcessState.Starting)
                return "-";
                
            TimeSpan uptime = DateTime.Now - process.StartTime;
            
            if (uptime.TotalDays >= 1)
                return $"{(int)uptime.TotalDays}d {uptime.Hours}h {uptime.Minutes}m";
            else if (uptime.TotalHours >= 1)
                return $"{(int)uptime.TotalHours}h {uptime.Minutes}m {uptime.Seconds}s";
            else if (uptime.TotalMinutes >= 1)
                return $"{(int)uptime.TotalMinutes}m {uptime.Seconds}s";
            else
                return $"{uptime.Seconds}s";
        }

        private static string TruncateString(string input, int maxLength)
        {
            if (string.IsNullOrEmpty(input) || input.Length <= maxLength)
                return input;
                
            return input.Substring(0, maxLength - 3) + "...";
        }

        private static void DrawFooter()
        {
            int width = Console.WindowWidth;
            Console.WriteLine();
            Console.Write(Colors.BgBlue + Colors.White);
            Console.Write(new string(' ', width));
            Console.SetCursorPosition(2, Console.CursorTop);
            Console.Write("s:start  p:stop  r:restart  c:reload  q:quit");
            Console.WriteLine(Colors.Reset);
            Console.WriteLine();
        }

        /// <summary>
        /// Renders a horizontal progress bar
        /// </summary>
        public static void DrawProgressBar(int value, int max, int width, string prefix = "")
        {
            int filledWidth = max > 0 ? (int)Math.Round(width * ((double)value / max)) : 0;
            int emptyWidth = width - filledWidth;
            
            Console.Write(prefix);
            Console.Write("[");
            Console.Write(Colors.Green + new string('█', filledWidth) + Colors.Reset);
            Console.Write(new string('░', emptyWidth));
            Console.Write("] ");
            Console.Write($"{value}/{max} ({(max > 0 ? (int)(100.0 * value / max) : 0)}%)");
        }
    }
}