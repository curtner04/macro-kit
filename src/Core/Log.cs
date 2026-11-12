using System;
using System.IO;
using System.Text;

namespace MacroKit.Core
{
    /// <summary>
    /// Small machine-local log (Paths.Logs\macrokit.log, 512 KB then rotated to macrokit.log.old). Never throws.
    /// Privacy: never pass typed text or phrase contents here; refer to phrases by Id only.
    /// </summary>
    public static class Log
    {
        const long MaxBytes = 512 * 1024;
        static readonly object gate = new object();

        public static string FilePath { get { return Path.Combine(Paths.Logs, "macrokit.log"); } }

        public static void Info(string message, Exception ex = null) { Write("INFO", message, ex); }
        public static void Warn(string message, Exception ex = null) { Write("WARN", message, ex); }
        public static void Error(string message, Exception ex = null) { Write("ERROR", message, ex); }

        static void Write(string level, string message, Exception ex)
        {
            try
            {
                lock (gate)
                {
                    string file = FilePath;
                    Directory.CreateDirectory(Path.GetDirectoryName(file));
                    var info = new FileInfo(file);
                    if (info.Exists && info.Length > MaxBytes)
                    {
                        string old = file + ".old";
                        if (File.Exists(old)) File.Delete(old);
                        File.Move(file, old);
                    }
                    var line = new StringBuilder();
                    line.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")).Append(' ').Append(level).Append(' ').Append(message);
                    if (ex != null) line.Append(" | ").Append(ex);
                    line.AppendLine();
                    File.AppendAllText(file, line.ToString(), new UTF8Encoding(false));
                }
            }
            catch
            {
                // Logging must never break the app.
            }
        }
    }
}
