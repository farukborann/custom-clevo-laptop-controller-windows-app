using System;
using System.IO;

namespace BSCustomClevoController.Utility
{
    public static class AppLogger
    {
        private const long MaxLogBytes = 2 * 1024 * 1024;
        private static readonly object LogLock = new object();

        private static string GetSafeLogDirectory()
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(localAppData))
            {
                return AppDomain.CurrentDomain.BaseDirectory;
            }
            return Path.Combine(localAppData, "CustomClevoController", "logs");
        }

        private static bool IsReparsePoint(string path)
        {
            return (File.Exists(path) || Directory.Exists(path)) &&
                   (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }

        public static string Log(string context, Exception ex)
        {
            string logDir = GetSafeLogDirectory();
            string logPath = Path.Combine(logDir, "crash.txt");
            try
            {
                string entry = string.Format(
                    "[{1:yyyy-MM-dd HH:mm:ss}] {2}{0}{3}{0}{0}",
                    Environment.NewLine,
                    DateTime.Now,
                    context,
                    ex);
                lock (LogLock)
                {
                    if (IsReparsePoint(logDir))
                    {
                        return logPath;
                    }
                    Directory.CreateDirectory(logDir);
                    if (IsReparsePoint(logDir) || IsReparsePoint(logPath))
                    {
                        return logPath;
                    }

                    var info = new FileInfo(logPath);
                    if (info.Exists && info.Length > MaxLogBytes)
                    {
                        string backupPath = Path.Combine(logDir, "crash.old.txt");
                        if (!IsReparsePoint(backupPath))
                        {
                            if (File.Exists(backupPath))
                            {
                                File.Delete(backupPath);
                            }
                            File.Move(logPath, backupPath);
                        }
                    }
                    File.AppendAllText(logPath, entry);
                }
            }
            catch
            {
                // Cannot recover if log file itself is unwritable
            }
            return logPath;
        }
    }
}

