using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using MacroKit.Core;

namespace MacroKit.Win32
{
    /// <summary>
    /// The real Stream Deck-style actions: Explorer-style open, closing a program's windows, sound through MCI, the speaker volume
    /// through Core Audio, and LockWorkStation. Every method returns null or the reason it didn't work, and never throws.
    /// </summary>
    public sealed class SystemActions : ISystemActions
    {
        const uint WM_CLOSE = 0x0010;

        public string Open(string target, string arguments)
        {
            target = (target ?? "").Trim();
            if (target.Length == 0) return "There is nothing to open.";
            try
            {
                var start = new ProcessStartInfo(target) { UseShellExecute = true };
                if (!string.IsNullOrWhiteSpace(arguments)) start.Arguments = arguments.Trim();
                string expanded = Environment.ExpandEnvironmentVariables(target);
                if (Path.IsPathRooted(expanded) && File.Exists(expanded))
                {
                    start.FileName = expanded;
                    start.WorkingDirectory = Path.GetDirectoryName(expanded);
                }
                using (Process.Start(start)) { }
                return null;
            }
            catch (Win32Exception e)
            {
                if (e.NativeErrorCode == 2 || e.NativeErrorCode == 3) return "Windows couldn't find “" + target + "”.";
                if (e.NativeErrorCode == 1223) return "Opening “" + target + "” was cancelled.";
                return "Couldn't open “" + target + "”: " + e.Message;
            }
            catch (Exception e)
            {
                return "Couldn't open “" + target + "”: " + e.Message;
            }
        }

        public string CloseProgram(string processName, out int windowsAsked)
        {
            windowsAsked = 0;
            string name = (processName ?? "").Trim();
            if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name.Substring(0, name.Length - 4);
            if (name.Length == 0) return "Say which program to close.";
            int own = Process.GetCurrentProcess().Id;
            var ids = new HashSet<uint>();
            try
            {
                foreach (Process p in Process.GetProcessesByName(name))
                {
                    using (p) { if (p.Id != own) ids.Add((uint)p.Id); }
                }
            }
            catch (Exception e)
            {
                return "Couldn't look for " + processName + ": " + e.Message;
            }
            if (ids.Count == 0) return null;

            var windows = new List<IntPtr>();
            Native.EnumWindows((hwnd, l) =>
            {
                uint pid;
                Native.GetWindowThreadProcessId(hwnd, out pid);
                if (ids.Contains(pid) && Native.IsWindowVisible(hwnd)) windows.Add(hwnd);
                return true;
            }, IntPtr.Zero);
            foreach (IntPtr hwnd in windows)
            {
                if (Native.PostMessageW(hwnd, WM_CLOSE, UIntPtr.Zero, IntPtr.Zero)) windowsAsked++;
            }
            if (windowsAsked == 0) return processName + " has no window Macro Kit can close.";
            return null;
        }

        // ---- Sound ----

        // MCI plays on one long-lived thread, so a sound keeps playing after the macro's worker thread has ended.
        readonly BlockingCollection<Action> soundQueue = new BlockingCollection<Action>();
        Thread soundThread;

        public string PlaySound(string path)
        {
            path = Environment.ExpandEnvironmentVariables((path ?? "").Trim());
            if (path.Length == 0) return "Say which sound file to play.";
            if (!File.Exists(path)) return "The sound file “" + path + "” doesn't exist.";
            string error = null;
            var done = new ManualResetEventSlim(false);
            EnsureSoundThread();
            soundQueue.Add(() =>
            {
                try
                {
                    Mci("close mksound");
                    string extension = Path.GetExtension(path).ToLowerInvariant();
                    string type = extension == ".wav" ? "waveaudio" : "mpegvideo";
                    error = Mci("open \"" + path + "\" type " + type + " alias mksound");
                    if (error == null) error = Mci("play mksound");
                }
                catch (Exception e) { error = e.Message; }
                finally { done.Set(); }
            });
            if (!done.Wait(5000)) return null;   // still opening: let it play when it is ready
            return error == null ? null : "Couldn't play “" + Path.GetFileName(path) + "”: " + error;
        }

        void EnsureSoundThread()
        {
            lock (soundQueue)
            {
                if (soundThread != null) return;
                soundThread = new Thread(() => { foreach (Action a in soundQueue.GetConsumingEnumerable()) a(); }) { IsBackground = true, Name = "Macro Kit sound" };
                soundThread.SetApartmentState(ApartmentState.STA);
                soundThread.Start();
            }
        }

        static string Mci(string command)
        {
            int code = mciSendStringW(command, null, 0, IntPtr.Zero);
            if (code == 0) return null;
            var text = new StringBuilder(256);
            return mciGetErrorStringW(code, text, text.Capacity) ? text.ToString() : "MCI error " + code;
        }

        // ---- Lock ----

        public string Lock()
        {
            return LockWorkStation() ? null : "Windows didn't lock the PC (error " + Marshal.GetLastWin32Error() + ").";
        }

        // ---- Volume ----

        public string SetVolume(int percent, bool relative)
        {
            IAudioEndpointVolume volume = null;
            IMMDevice device = null;
            IMMDeviceEnumerator enumerator = null;
            try
            {
                enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
                int hr = enumerator.GetDefaultAudioEndpoint(0 /* eRender */, 1 /* eMultimedia */, out device);
                if (hr != 0 || device == null) return "There is no speaker to set the volume of.";
                Guid iid = typeof(IAudioEndpointVolume).GUID;
                object activated;
                hr = device.Activate(ref iid, 1 /* CLSCTX_INPROC_SERVER */, IntPtr.Zero, out activated);
                if (hr != 0) return "Windows didn't let Macro Kit change the volume (0x" + hr.ToString("X8") + ").";
                volume = (IAudioEndpointVolume)activated;
                float level = percent / 100f;
                if (relative)
                {
                    float now;
                    Marshal.ThrowExceptionForHR(volume.GetMasterVolumeLevelScalar(out now));
                    level = now + percent / 100f;
                }
                level = Math.Max(0f, Math.Min(1f, level));
                Guid context = Guid.Empty;
                Marshal.ThrowExceptionForHR(volume.SetMasterVolumeLevelScalar(level, ref context));
                return null;
            }
            catch (Exception e)
            {
                return "Couldn't change the volume: " + e.Message;
            }
            finally
            {
                foreach (object com in new object[] { volume, device, enumerator })
                {
                    if (com != null) try { Marshal.ReleaseComObject(com); } catch { }
                }
            }
        }

        // ---- Interop ----

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool LockWorkStation();

        [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
        static extern int mciSendStringW(string command, StringBuilder returnString, int returnLength, IntPtr callback);

        [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
        static extern bool mciGetErrorStringW(int errorCode, StringBuilder text, int length);

        [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
        class MMDeviceEnumeratorComObject { }

        [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMMDeviceEnumerator
        {
            [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
            [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice endpoint);
        }

        [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMMDevice
        {
            [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object instance);
        }

        [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IAudioEndpointVolume
        {
            [PreserveSig] int RegisterControlChangeNotify(IntPtr notify);
            [PreserveSig] int UnregisterControlChangeNotify(IntPtr notify);
            [PreserveSig] int GetChannelCount(out uint count);
            [PreserveSig] int SetMasterVolumeLevel(float levelDb, ref Guid eventContext);
            [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid eventContext);
            [PreserveSig] int GetMasterVolumeLevel(out float levelDb);
            [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
        }
    }
}
