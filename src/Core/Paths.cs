using System;
using System.IO;

namespace MacroKit.Core
{
    /// <summary>
    /// Where Macro Kit keeps its files. The data folder (settings, phrases, macros, recordings) is what Export / import configuration
    /// transfers; the log folder is machine-local and never transferred.
    /// </summary>
    public static class Paths
    {
        static string overrideRoot;

        /// <summary>Points the data folder somewhere else (the --data command-line option, tests). Null or empty clears it.</summary>
        public static void Override(string dataFolder)
        {
            overrideRoot = string.IsNullOrWhiteSpace(dataFolder) ? null : Path.GetFullPath(dataFolder);
        }

        // The data folder chosen by --data (Override) or the MK_DATA_ROOT environment variable, else null.
        static string ExplicitRoot
        {
            get
            {
                if (overrideRoot != null) return overrideRoot;
                string env = Environment.GetEnvironmentVariable("MK_DATA_ROOT");
                return string.IsNullOrWhiteSpace(env) ? null : Path.GetFullPath(env);
            }
        }

        /// <summary>True when the data folder was chosen explicitly (--data or MK_DATA_ROOT) rather than the default one.</summary>
        public static bool IsOverridden
        {
            get { return ExplicitRoot != null; }
        }

        static string AppFolder
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Macro Kit"); }
        }

        /// <summary>--data folder, else MK_DATA_ROOT, else %LOCALAPPDATA%\Macro Kit\data.</summary>
        public static string DataRoot
        {
            get { return ExplicitRoot ?? Path.Combine(AppFolder, "data"); }
        }

        /// <summary>%LOCALAPPDATA%\Macro Kit\logs, or "&lt;data&gt;\..\logs" when the data folder is overridden (so tests never write to the real log).</summary>
        public static string Logs
        {
            get
            {
                string root = ExplicitRoot;
                return root != null ? Path.GetFullPath(Path.Combine(root, "..", "logs")) : Path.Combine(AppFolder, "logs");
            }
        }

        public static string RecordingsFolder
        {
            get { return Path.Combine(DataRoot, "recordings"); }
        }
    }
}
