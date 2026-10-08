using System;
using System.IO;

namespace NetworkSpeedChecker.Helpers
{
    /// <summary>Simple file logger used for diagnostics and startup-crash investigation.</summary>
    public static class AppLogger
    {
        private static readonly object Gate = new object();
        private static string? _logDir;

        private static string LogDir
        {
            get
            {
                if (_logDir != null) return _logDir;
                try
                {
                    _logDir = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "NetworkSpeedChecker");
                }
                catch
                {
                    _logDir = AppContext.BaseDirectory;
                }
                Directory.CreateDirectory(_logDir!);
                return _logDir;
            }
        }

        public static string LogPath => Path.Combine(LogDir, "NetworkSpeedChecker.log");

        public static void Log(string message)
        {
            try
            {
                lock (Gate)
                {
                    File.AppendAllText(LogPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}  {message}{Environment.NewLine}");
                }
            }
            catch
            {
                // Logging must never take the app down.
            }
        }

        public static void LogException(Exception ex, string context) =>
            Log($"[ERROR] {context}: {ex.GetType().Name}: {ex.Message}{Environment.NewLine}{ex.StackTrace}");
    }
}
