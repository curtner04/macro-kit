using System;
using System.Runtime.InteropServices;
using System.Threading;
using MacroKit.Core;

namespace MacroKit.Win32
{
    /// <summary>
    /// Asks Windows for 1 ms timer ticks while a playback runs. By default waits end on a 15.6 ms tick, so a recorded mouse path
    /// replays in jerks and quick clicks drift apart. Windows 11 ignores the request of a process with no visible window (Macro Kit
    /// hides during a run) unless the process opts out of that, which <see cref="Raise"/> does too. Failures are ignored: the
    /// playback then simply keeps the normal timing.
    /// </summary>
    public static class TimerResolution
    {
        static int optedOut;

        /// <summary>Raises the resolution until the returned object is disposed. Dispose it in a finally block.</summary>
        public static IDisposable Raise()
        {
            KeepWhileHidden();
            bool raised = false;
            try { raised = Native.timeBeginPeriod(1) == 0; }
            catch (Exception ex) { Log.Warn("Could not ask Windows for finer timing: " + ex.Message); }
            return new Lease(raised);
        }

        // Once per process: the opt-out only says "honour my requests", so it costs nothing while none is active.
        static void KeepWhileHidden()
        {
            if (Interlocked.Exchange(ref optedOut, 1) != 0) return;
            try
            {
                var state = new Native.PROCESS_POWER_THROTTLING_STATE
                {
                    Version = Native.PROCESS_POWER_THROTTLING_CURRENT_VERSION,
                    ControlMask = Native.PROCESS_POWER_THROTTLING_IGNORE_TIMER_RESOLUTION,
                    StateMask = 0,
                };
                Native.SetProcessInformation(Native.GetCurrentProcess(), Native.ProcessPowerThrottling, ref state, Marshal.SizeOf(typeof(Native.PROCESS_POWER_THROTTLING_STATE)));
            }
            catch (Exception) { /* older Windows: there is nothing to opt out of */ }
        }

        sealed class Lease : IDisposable
        {
            int raised;

            public Lease(bool raised) { this.raised = raised ? 1 : 0; }

            public void Dispose()
            {
                if (Interlocked.Exchange(ref raised, 0) == 0) return;
                try { Native.timeEndPeriod(1); } catch (Exception) { }
            }
        }
    }
}
