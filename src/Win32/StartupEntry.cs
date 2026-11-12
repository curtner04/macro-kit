using System;
using Microsoft.Win32;

namespace MacroKit.Win32
{
    /// <summary>
    /// "Start with Windows": the per-user Run value "Macro Kit" = "&lt;exe&gt;" --tray. No administrator rights needed.
    /// The registry value is the truth; the settings checkbox only mirrors it.
    /// </summary>
    public static class StartupEntry
    {
        public const string ValueName = "Macro Kit";
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        // Task Manager's Startup tab and Settings > Apps > Startup record the user's on/off choice here, next to the Run key.
        const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

        /// <summary>The command line stored in the Run value: the quoted exe path (it may contain spaces) and --tray.</summary>
        public static string BuildCommand(string exePath)
        {
            if (string.IsNullOrWhiteSpace(exePath)) throw new ArgumentException("The program path is empty.", "exePath");
            if (exePath.IndexOf('"') >= 0) throw new ArgumentException("The program path contains a quote mark.", "exePath");
            return "\"" + exePath + "\" --tray";
        }

        /// <summary>
        /// True for a StartupApproved value Windows treats as switched off. The first byte is 02 or 06 when enabled and an odd
        /// number (03, 07) when the user turned the entry off in Task Manager.
        /// </summary>
        public static bool IsDisabledByUser(byte[] approvedValue)
        {
            return approvedValue != null && approvedValue.Length > 0 && (approvedValue[0] & 1) == 1;
        }

        /// <summary>True when the Run value exists and the user has not switched it off in Windows' own startup list.</summary>
        public static bool IsEnabled
        {
            get
            {
                try
                {
                    using (RegistryKey run = Registry.CurrentUser.OpenSubKey(RunKey))
                    {
                        if (run == null || string.IsNullOrEmpty(run.GetValue(ValueName) as string)) return false;
                    }
                    using (RegistryKey approved = Registry.CurrentUser.OpenSubKey(ApprovedKey))
                    {
                        return approved == null || !IsDisabledByUser(approved.GetValue(ValueName) as byte[]);
                    }
                }
                catch (Exception ex) when (ex is System.Security.SecurityException || ex is UnauthorizedAccessException || ex is System.IO.IOException)
                {
                    return false;
                }
            }
        }

        /// <summary>Writes the Run value for this exe and clears an old "turned off in Task Manager" mark, which would otherwise keep it from starting.</summary>
        public static void Enable(string exePath)
        {
            string command = BuildCommand(exePath);
            using (RegistryKey run = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                run.SetValue(ValueName, command, RegistryValueKind.String);
            }
            using (RegistryKey approved = Registry.CurrentUser.OpenSubKey(ApprovedKey, true))
            {
                if (approved != null) approved.DeleteValue(ValueName, false);
            }
        }

        /// <summary>Removes the Run value (and the on/off mark). Nothing happens when it is not there.</summary>
        public static void Disable()
        {
            using (RegistryKey run = Registry.CurrentUser.OpenSubKey(RunKey, true))
            {
                if (run != null) run.DeleteValue(ValueName, false);
            }
            using (RegistryKey approved = Registry.CurrentUser.OpenSubKey(ApprovedKey, true))
            {
                if (approved != null) approved.DeleteValue(ValueName, false);
            }
        }
    }
}
