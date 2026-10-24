using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace AvasRoutingTool.Logging
{
    public enum LogLevel
    {
        Debug,
        Info,
        Warn,
        Error
    }

    public record LogEntry(DateTime Timestamp, LogLevel Level, string Category, string Message)
    {
        public override string ToString()
        {
            return $"[{Timestamp:yyyy-MM-dd HH:mm:ss.fff}] [{Level.ToString().ToUpperInvariant().PadRight(5)}] [{Category}] {Message}";
        }
    }

    public static class AppLogger
    {
        private static readonly object FileLock = new();
        private static string _logFilePath = string.Empty;
        private static bool _initialized;
        private static readonly ConcurrentQueue<LogEntry> MemoryBuffer = new();
        private const int MaxBufferEntries = 1000;

        public static event Action<LogEntry>? LogEntryAdded;

        public static string LogFilePath
        {
            get
            {
                if (!_initialized) Initialize();
                return _logFilePath;
            }
        }

        public static IReadOnlyList<LogEntry> RecentLogs => MemoryBuffer.ToArray();

        public static void Initialize(string? customPath = null)
        {
            lock (FileLock)
            {
                if (_initialized && string.IsNullOrEmpty(customPath)) return;

                try
                {
                    if (!string.IsNullOrWhiteSpace(customPath))
                    {
                        _logFilePath = customPath;
                    }
                    else
                    {
                        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                        _logFilePath = Path.Combine(baseDir, "app.log");

                        try
                        {
                            using var fs = File.Open(_logFilePath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.ReadWrite);
                        }
                        catch
                        {
                            string appData = Path.Combine(
                                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                "AvasRoutingTool",
                                "Logs");
                            Directory.CreateDirectory(appData);
                            _logFilePath = Path.Combine(appData, "app.log");
                        }
                    }

                    _initialized = true;
                    Info("LOGGER", $"Logger initialized. Output file: {_logFilePath}");
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"[AppLogger] Init failure: {ex.Message}");
                }
            }
        }

        public static void Debug(string category, string message) => Log(LogLevel.Debug, category, message);
        public static void Info(string category, string message) => Log(LogLevel.Info, category, message);
        public static void Warn(string category, string message, Exception? ex = null) => Log(LogLevel.Warn, category, FormatEx(message, ex));
        public static void Error(string category, string message, Exception? ex = null) => Log(LogLevel.Error, category, FormatEx(message, ex));

        public static void Log(LogLevel level, string category, string message)
        {
            if (!_initialized) Initialize();

            var entry = new LogEntry(DateTime.Now, level, category, message);

            MemoryBuffer.Enqueue(entry);
            while (MemoryBuffer.Count > MaxBufferEntries)
            {
                MemoryBuffer.TryDequeue(out _);
            }

            Trace.WriteLine(entry.ToString());

            if (!string.IsNullOrEmpty(_logFilePath))
            {
                lock (FileLock)
                {
                    try
                    {
                        File.AppendAllText(_logFilePath, entry.ToString() + Environment.NewLine);
                    }
                    catch { }
                }
            }

            LogEntryAdded?.Invoke(entry);
        }

        public static void ClearMemoryBuffer()
        {
            while (MemoryBuffer.TryDequeue(out _)) { }
        }

        private static string FormatEx(string message, Exception? ex)
        {
            if (ex == null) return message;
            return $"{message} | {ex.GetType().Name}: {ex.Message}";
        }
    }
}
