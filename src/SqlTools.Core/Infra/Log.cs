using System;
using System.IO;
using System.Linq;
using System.Text;

namespace SqlTools.Infra
{
    /// <summary>Log em arquivo, um por dia. Nunca lança exceção.</summary>
    public static class Log
    {
        private static readonly object Sync = new object();
        private const int KeepDays = 14;

        public static string CurrentFile => Path.Combine(AppPaths.LogsDir, "sqltools-" + DateTime.Now.ToString("yyyyMMdd") + ".log");

        public static void Info(string message) => Write("INFO ", message, null);
        public static void Warn(string message, Exception ex = null) => Write("WARN ", message, ex);
        public static void Error(string message, Exception ex = null) => Write("ERROR", message, ex);

        public static void CleanupOldFiles()
        {
            try
            {
                var limit = DateTime.Now.AddDays(-KeepDays);
                foreach (var f in new DirectoryInfo(AppPaths.LogsDir).GetFiles("sqltools-*.log").Where(f => f.LastWriteTime < limit))
                    f.Delete();
            }
            catch
            {
                // log não pode quebrar nada
            }
        }

        private static void Write(string level, string message, Exception ex)
        {
            try
            {
                var sb = new StringBuilder();
                sb.Append(DateTime.Now.ToString("HH:mm:ss.fff")).Append(' ').Append(level).Append(' ').Append(message);
                if (ex != null) sb.AppendLine().Append(ex);
                sb.AppendLine();
                lock (Sync)
                {
                    File.AppendAllText(CurrentFile, sb.ToString(), Encoding.UTF8);
                }
            }
            catch
            {
                // log não pode quebrar nada
            }
        }
    }
}
