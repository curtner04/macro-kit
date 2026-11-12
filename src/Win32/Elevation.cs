using System;
using System.Runtime.InteropServices;

namespace MacroKit.Win32
{
    /// <summary>Whether processes run as administrator. Windows blocks input from a normal process to an elevated window (UIPI).</summary>
    public static class Elevation
    {
        static bool? current;

        /// <summary>True when Macro Kit itself runs elevated. Cached: a process cannot change its elevation.</summary>
        public static bool IsCurrentProcessElevated
        {
            get
            {
                if (current == null) current = QueryToken(Native.GetCurrentProcess(), false);
                return current.Value;
            }
        }

        /// <summary>
        /// Best effort for another process: true when its token says elevated, and also true when Windows will not even let us
        /// look (access denied means it outranks us, and that is exactly the case the caller needs to warn about).
        /// Other failures (the process ended) read as not elevated.
        /// </summary>
        internal static bool IsProcessElevated(uint processId)
        {
            IntPtr process = Native.OpenProcess(Native.PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
            if (process == IntPtr.Zero) return Marshal.GetLastWin32Error() == Native.ERROR_ACCESS_DENIED;
            try { return QueryToken(process, true); }
            finally { Native.CloseHandle(process); }
        }

        static bool QueryToken(IntPtr process, bool deniedMeansElevated)
        {
            IntPtr token;
            if (!Native.OpenProcessToken(process, Native.TOKEN_QUERY, out token))
                return deniedMeansElevated && Marshal.GetLastWin32Error() == Native.ERROR_ACCESS_DENIED;
            try
            {
                uint elevated;
                int length;
                return Native.GetTokenInformation(token, Native.TokenElevation, out elevated, sizeof(uint), out length) && elevated != 0;
            }
            finally { Native.CloseHandle(token); }
        }
    }
}
