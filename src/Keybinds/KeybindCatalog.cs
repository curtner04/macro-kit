using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using MacroKit.Core;

namespace MacroKit.Keybinds
{
    /// <summary>Where each app keeps its shortcuts. Defaults are the real locations; tests point them at fixtures.</summary>
    public sealed class KeybindLocations
    {
        public string Snipline;
        public string NoteKeeper;
        public string JancyDefault;
        /// <summary>PowerToys' settings folder, and Command Palette's settings file (it lives in its app package).</summary>
        public string PowerToys, CommandPalette;
        /// <summary>OBS Studio's settings folder (%APPDATA%\obs-studio).</summary>
        public string Obs;
        /// <summary>
        /// Whether a sister app (or Jancy) is on this PC, by its name. Each one is listed only when it is, so a fresh PC shows
        /// Macro Kit, the programs it finds and the apps added by hand. Null (tests) lists them all.
        /// </summary>
        public Func<string, bool> IsInstalled;

        public static KeybindLocations Default()
        {
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var locations = new KeybindLocations
            {
                Snipline = Path.Combine(local, "Snipline", "data", "settings.json"),
                NoteKeeper = Path.Combine(local, "Note Keeper", "data", "settings.json"),
                JancyDefault = Path.Combine(roaming, "Jancy", "keybinds.txt"),
                PowerToys = Path.Combine(local, "Microsoft", "PowerToys"),
                CommandPalette = Path.Combine(local, "Packages", "Microsoft.CommandPalette_8wekyb3d8bbwe", "LocalState", "settings.json"),
                Obs = Path.Combine(roaming, "obs-studio"),
            };
            // An app counts as installed when it keeps a data folder here or is running.
            locations.IsInstalled = name =>
            {
                try
                {
                    if (Directory.Exists(Path.Combine(local, name)) || Directory.Exists(Path.Combine(roaming, name))) return true;
                    return System.Diagnostics.Process.GetProcessesByName(name).Length > 0;
                }
                catch { return false; }
            };
            return locations;
        }
    }

    /// <summary>Two shortcuts that get in each other's way.</summary>
    public sealed class KeybindClash
    {
        public Keybind A, B;
        /// <summary>True when both work everywhere: whichever app started first gets the keys.</summary>
        public bool BothGlobal;
    }

    /// <summary>Every app's shortcuts, read in one go, with lookups by keys.</summary>
    public sealed class KeybindCatalog
    {
        public readonly List<AppKeybinds> Apps = new List<AppKeybinds>();
        public DateTime ReadAt = DateTime.Now;

        public IEnumerable<Keybind> All { get { return Apps.SelectMany(a => a.Keybinds); } }

        /// <summary>
        /// Reads every app: Macro Kit, its sister apps, other programs found on this PC (PowerToys, OBS Studio) and the apps the
        /// person added by hand. readText returns a file's text, or null when it does not exist (it may throw for a locked file).
        /// </summary>
        public static KeybindCatalog Read(KeybindLocations where, AppSettings settings, IEnumerable<MacroDef> macros, IEnumerable<KeybindAppDef> added, Func<string, string> readText)
        {
            var catalog = new KeybindCatalog();
            catalog.Apps.Add(KeybindReaders.MacroKit(settings, macros));

            string text, problem;
            Func<string, bool> present = name => where.IsInstalled == null || where.IsInstalled(name);
            if (present(KeybindReaders.SniplineName))
            {
                text = Load(readText, where.Snipline, out problem);
                catalog.Apps.Add(WithProblem(KeybindReaders.Snipline(where.Snipline, text), problem));
            }
            if (present(KeybindReaders.NoteKeeperName))
            {
                text = Load(readText, where.NoteKeeper, out problem);
                catalog.Apps.Add(WithProblem(KeybindReaders.NoteKeeper(where.NoteKeeper, text), problem));
            }
            if (present(KeybindReaders.JancyName))
            {
                text = Load(readText, where.JancyDefault, out problem);
                catalog.Apps.Add(WithProblem(KeybindReaders.Jancy(where.JancyDefault, text), problem));
            }
            if (present(KeybindReaders.RecordKeeperName)) catalog.Apps.Add(KeybindReaders.RecordKeeper());
            if (present(KeybindReaders.LittleButtonsName)) catalog.Apps.Add(KeybindReaders.LittleButtons());
            if (present(KeybindReaders.StorageLensName)) catalog.Apps.Add(KeybindReaders.StorageLens());
            if (present(KeybindReaders.TweakBenchName)) catalog.Apps.Add(KeybindReaders.TweakBench());

            // Programs that are not Macro Kit's sister apps only appear when they are installed.
            AppKeybinds other = OtherAppReaders.PowerToys(where.PowerToys, where.CommandPalette, readText);
            if (other != null) catalog.Apps.Add(other);
            other = OtherAppReaders.Obs(where.Obs, readText);
            if (other != null) catalog.Apps.Add(other);

            foreach (KeybindAppDef def in added ?? Enumerable.Empty<KeybindAppDef>())
                if (def != null && !string.IsNullOrWhiteSpace(def.Name)) catalog.Apps.Add(CustomKeybinds.Read(def));
            return catalog;
        }

        static string Load(Func<string, string> readText, string path, out string problem)
        {
            problem = null;
            if (string.IsNullOrEmpty(path)) return null;
            try { return readText(path); }
            catch (Exception ex) { problem = "Could not open " + Path.GetFileName(path) + " (" + ex.Message + ")."; return null; }
        }

        static AppKeybinds WithProblem(AppKeybinds app, string problem)
        {
            if (problem != null) app.Problem = problem;
            return app;
        }

        /// <summary>Reads files from disk: null for a missing file. Shared read so an app that is writing its file is not disturbed.</summary>
        public static string ReadFile(string path)
        {
            if (!File.Exists(path)) return null;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var reader = new StreamReader(stream, Encoding.UTF8, true))
                return reader.ReadToEnd();
        }

        /// <summary>Every switched-on shortcut that uses these keys.</summary>
        public List<Keybind> Users(Hotkey combo)
        {
            return combo.IsEmpty ? new List<Keybind>() : All.Where(k => k.Matches(combo)).ToList();
        }

        /// <summary>
        /// Pairs that get in each other's way: two apps with the same global shortcut, or a global shortcut that takes the keys
        /// before another app's in-app shortcut sees them. Two in-app shortcuts never clash (only one app is active at a time),
        /// and neither do two shortcuts of the same app.
        /// </summary>
        public List<KeybindClash> Clashes()
        {
            var clashes = new List<KeybindClash>();
            var enabled = All.Where(k => k.Enabled && !k.Momentary && !k.Combo.IsEmpty).ToList();
            foreach (var group in enabled.GroupBy(k => k.Combo))
            {
                var items = group.ToList();
                for (int i = 0; i < items.Count; i++)
                    for (int j = i + 1; j < items.Count; j++)
                    {
                        Keybind a = items[i], b = items[j];
                        if (a.App == b.App) continue;
                        if (a.Scope == KeybindScope.AppActive && b.Scope == KeybindScope.AppActive) continue;
                        if (a.Scope == KeybindScope.AppActive) { var t = a; a = b; b = t; }   // the global one first
                        clashes.Add(new KeybindClash { A = a, B = b, BothGlobal = b.Scope == KeybindScope.Global });
                    }
            }
            return clashes;
        }

        /// <summary>A sentence about a shortcut's clash, or null when it has none.</summary>
        public string ClashNote(Keybind keybind)
        {
            foreach (KeybindClash clash in Clashes())
            {
                if (clash.A != keybind && clash.B != keybind) continue;
                Keybind other = clash.A == keybind ? clash.B : clash.A;
                if (clash.BothGlobal) return "Also a global shortcut in " + other.App + ": only one of them can have it.";
                return keybind.Scope == KeybindScope.Global
                    ? "Also used inside " + other.App + "; while " + keybind.App + " runs, " + other.App + " may not get it."
                    : other.App + " uses it everywhere, so it may never reach " + keybind.App + ".";
            }
            return null;
        }
    }

    /// <summary>The answer to "can I use these keys?"</summary>
    public enum ComboVerdict
    {
        /// <summary>Nothing listed uses it and Windows did not report it taken (or was not asked).</summary>
        Free,
        /// <summary>Only used inside apps while they are active: fine as a global shortcut for something else only if you accept the overlap; fine in any other app.</summary>
        InAppOnly,
        /// <summary>A listed app uses it in every window.</summary>
        TakenGlobally,
        /// <summary>Nothing listed uses it, but Windows says another running program has registered it.</summary>
        TakenByUnknown,
    }

    public static class ComboCheck
    {
        /// <summary>windowsFree: null when Windows was not asked, false when it refused to register the keys.</summary>
        public static ComboVerdict Judge(List<Keybind> users, bool? windowsFree)
        {
            if (users.Any(u => u.Scope == KeybindScope.Global)) return ComboVerdict.TakenGlobally;
            if (windowsFree == false) return ComboVerdict.TakenByUnknown;
            if (users.Count > 0) return ComboVerdict.InAppOnly;
            return ComboVerdict.Free;
        }

        public static string Explain(Hotkey combo, List<Keybind> users, bool? windowsFree)
        {
            string keys = combo.ToString();
            var globals = users.Where(u => u.Scope == KeybindScope.Global).ToList();
            var local = users.Where(u => u.Scope == KeybindScope.AppActive).Select(u => u.App).Distinct().ToList();
            switch (Judge(users, windowsFree))
            {
                case ComboVerdict.TakenGlobally:
                    return keys + " is taken everywhere by " + string.Join(" and ", globals.Select(g => g.App + " (" + g.Action + ")").Distinct()) + "."
                        + (local.Count > 0 ? " It is also used inside " + Join(local) + "." : "");
                case ComboVerdict.TakenByUnknown:
                    return keys + " is not used by any app on this list, but Windows says another running program has already claimed it."
                        + (local.Count > 0 ? " It is also used inside " + Join(local) + "." : "");
                case ComboVerdict.InAppOnly:
                    return keys + " only works inside " + Join(local) + " while " + (local.Count == 1 ? "it is" : "one of them is") + " the active window. It is free in every other app;"
                        + " as a global shortcut it would take the keys away from " + Join(local) + ".";
                default:
                    return keys + " is free: no app on this list uses it" + (windowsFree == true ? " and Windows let Macro Kit claim it for a moment, so no running program has it registered." : ".");
            }
        }

        static string Join(List<string> names)
        {
            if (names.Count <= 1) return names.FirstOrDefault() ?? "";
            return string.Join(", ", names.Take(names.Count - 1)) + " and " + names[names.Count - 1];
        }
    }
}
