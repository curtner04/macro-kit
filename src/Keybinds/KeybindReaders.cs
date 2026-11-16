using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using MacroKit.Core;

namespace MacroKit.Keybinds
{
    /// <summary>
    /// Reads the shortcuts of each of the person's apps from the files those apps save. Every reader is pure: it gets the file
    /// text (null when the file does not exist) and never changes anything. Built-in shortcuts are listed from what each app's
    /// source does; saved ones come from its settings file, so they follow the person's changes.
    /// </summary>
    public static class KeybindReaders
    {
        public const string MacroKitName = "Macro Kit", SniplineName = "Snipline", NoteKeeperName = "Note Keeper",
            StudioName = "Jancy Macro Studio", JancyName = "Jancy", RecordKeeperName = "Record Keeper";

        // ---------------------------------------------------------------- Macro Kit (its own live settings)

        public static AppKeybinds MacroKit(AppSettings settings, IEnumerable<MacroDef> macros)
        {
            var app = new AppKeybinds(MacroKitName);
            AddMacroKitHotkey(app, "Record / stop recording", settings.RecordHotkey);
            AddMacroKitHotkey(app, "Play / stop playback", settings.PlayHotkey);
            AddMacroKitHotkey(app, "Stop everything", settings.StopHotkey);
            AddMacroKitHotkey(app, "Show or hide the Deck", settings.DeckHotkey);
            foreach (MacroDef macro in macros ?? Enumerable.Empty<MacroDef>())
                if (!string.IsNullOrWhiteSpace(macro.Hotkey)) AddMacroKitHotkey(app, "Run macro “" + macro.Name + "”", macro.Hotkey);
            app.Add("Stop playback or a macro", "Esc", KeybindScope.Global, true, "Only while a playback or macro runs (Settings: Esc stops playback and macros)", settings.EscStops).Momentary = true;
            return app;
        }

        static void AddMacroKitHotkey(AppKeybinds app, string action, string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            app.Add(action, text, KeybindScope.Global, true);
        }

        // ---------------------------------------------------------------- Snipline

        /// <summary>%LOCALAPPDATA%\Snipline\data\settings.json: HotkeyKey (VK) + HotkeyModifiers (MOD_* bits), WindowShortcutModifier.</summary>
        public static AppKeybinds Snipline(string path, string text)
        {
            var app = new AppKeybinds(SniplineName, path);
            int key = 0x2C, modifiers = 0, windowModifier = 0;
            if (text == null) app.Problem = "Settings file not found, so Snipline's defaults are shown.";
            else
            {
                try
                {
                    var tree = AsObject(Json.ParseTree(text));
                    key = Int(tree, "HotkeyKey", key);
                    modifiers = Int(tree, "HotkeyModifiers", 0);
                    windowModifier = Int(tree, "WindowShortcutModifier", 0);
                }
                catch (Exception ex) { app.Problem = "Could not read the settings file (" + ex.Message + "), so Snipline's defaults are shown."; }
            }
            Hotkey capture = KeyText.FromNative(key, modifiers);
            app.Add("Take a screenshot (Capture shortcut)", capture, KeybindScope.Global, true);
            if (windowModifier == 1 || windowModifier == 2 || windowModifier == 4)
                app.Add("Copy the active window (Active window shortcut)", KeyText.FromNative(key, modifiers | windowModifier), KeybindScope.Global, true);

            const string overlay = "Only while a screenshot is open in Snipline";
            app.Add("Copy", "Ctrl+C", KeybindScope.AppActive, false, overlay);
            app.Add("Save PNG", "Ctrl+S", KeybindScope.AppActive, false, overlay);
            app.Add("Save and open in editor", "Ctrl+E", KeybindScope.AppActive, false, overlay);
            app.Add("Pin to screen", "Ctrl+P", KeybindScope.AppActive, false, overlay);
            app.Add("Undo", "Ctrl+Z", KeybindScope.AppActive, false, overlay);
            app.Add("Cancel", "Esc", KeybindScope.AppActive, false, overlay);
            app.Add("Select the full screen", "F", KeybindScope.AppActive, false, overlay + ", before you select an area");
            app.Add("Pick a window", "W", KeybindScope.AppActive, false, overlay + ", before you select an area");
            app.Add("Select / move tool", "V", KeybindScope.AppActive, false, overlay);
            app.Add("Highlighter", "H", KeybindScope.AppActive, false, overlay);
            app.Add("Blur / pixelate", "B", KeybindScope.AppActive, false, overlay);
            app.Add("Numbered step marker", "N", KeybindScope.AppActive, false, overlay);
            app.Add("Delete the selected annotation", "Delete", KeybindScope.AppActive, false, overlay);
            app.Add("Copy a pinned screenshot", "Ctrl+C", KeybindScope.AppActive, false, "Only while a pinned screenshot is the active window");
            return app;
        }

        // ---------------------------------------------------------------- Note Keeper

        /// <summary>%LOCALAPPDATA%\Note Keeper\data\settings.json: RecordKey, CaptureKey, MarkKey (VKs, F1-F12) and one Modifiers mask.</summary>
        public static AppKeybinds NoteKeeper(string path, string text)
        {
            var app = new AppKeybinds(NoteKeeperName, path);
            int record = 119, capture = 120, mark = 118, modifiers = 0;
            if (text == null) app.Problem = "Settings file not found, so Note Keeper's defaults are shown.";
            else
            {
                try
                {
                    var tree = AsObject(Json.ParseTree(text));
                    record = Int(tree, "RecordKey", record);
                    capture = Int(tree, "CaptureKey", capture);
                    mark = Int(tree, "MarkKey", mark);
                    modifiers = Int(tree, "Modifiers", 0);
                }
                catch (Exception ex) { app.Problem = "Could not read the settings file (" + ex.Message + "), so Note Keeper's defaults are shown."; }
            }
            app.Add("Listen / stop", KeyText.FromNative(record, modifiers), KeybindScope.Global, true);
            app.Add("Attach monitor screenshot", KeyText.FromNative(capture, modifiers), KeybindScope.Global, true);
            if (mark != 0) app.Add("Mark moment (timestamp)", KeyText.FromNative(mark, modifiers), KeybindScope.Global, true);
            app.Add("Search notes", "Ctrl+F", KeybindScope.AppActive, false);
            return app;
        }

        // ---------------------------------------------------------------- Jancy Macro Studio

        /// <summary>data\studio.json beside the exe: Workflows[].Name and Workflows[].Hotkey ("Ctrl+Alt+1", null for none).</summary>
        public static AppKeybinds JancyMacroStudio(string path, string text, out string jancyKeybindPath)
        {
            jancyKeybindPath = null;
            var app = new AppKeybinds(StudioName, path);
            app.Add("Start / stop recording", "Ctrl+Alt+F11", KeybindScope.Global, false, "Built in; works while Jancy Macro Studio is open");
            app.Add("Emergency stop", "Ctrl+Alt+F12", KeybindScope.Global, false, "Built in; works while Jancy Macro Studio is open");
            if (text == null) app.Problem = "studio.json not found, so workflow hotkeys could not be read.";
            else
            {
                try
                {
                    var tree = AsObject(Json.ParseTree(text));
                    jancyKeybindPath = Str(tree, "KeybindPath");
                    object workflows;
                    if (tree != null && tree.TryGetValue("Workflows", out workflows) && workflows is IEnumerable)
                    {
                        foreach (object item in (IEnumerable)workflows)
                        {
                            var workflow = AsObject(item);
                            string hotkey = Str(workflow, "Hotkey");
                            if (string.IsNullOrWhiteSpace(hotkey)) continue;
                            string name = Str(workflow, "Name");
                            app.Add("Run workflow “" + (string.IsNullOrWhiteSpace(name) ? "(no name)" : name) + "”", hotkey, KeybindScope.Global, true, "Works while Jancy Macro Studio is open, even minimized");
                        }
                    }
                }
                catch (Exception ex) { app.Problem = "Could not read studio.json (" + ex.Message + ")."; }
            }
            app.Add("Save now", "Ctrl+S", KeybindScope.AppActive, false);
            app.Add("Delete the selected action", "Delete", KeybindScope.AppActive, false, "In the Actions table");
            app.Add("Edit the selected cell", "F2", KeybindScope.AppActive, false, "In the Actions table");
            return app;
        }

        // ---------------------------------------------------------------- Jancy (its own keybinds.txt)

        static readonly Regex fieldSplit = new Regex(@"(?<!\\);", RegexOptions.Compiled);

        /// <summary>%APPDATA%\Jancy\keybinds.txt: one "id:...; keybind:ctrl-shift-c; context:tab window" per line.</summary>
        public static AppKeybinds Jancy(string path, string text)
        {
            var app = new AppKeybinds(JancyName, path);
            if (text == null) { app.Problem = "keybinds.txt not found. Jancy writes it once you change a keybind in Jancy."; return app; }
            int skipped = 0;
            foreach (string raw in text.Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#") || line.StartsWith("//")) continue;
                var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (string field in fieldSplit.Split(line))
                {
                    int colon = field.IndexOf(':');
                    if (colon < 1) continue;
                    fields[field.Substring(0, colon).Trim()] = field.Substring(colon + 1).Trim().Replace(@"\;", ";");
                }
                string id, keys, context;
                if (!fields.TryGetValue("id", out id) || !fields.TryGetValue("keybind", out keys) || id.Length == 0 || keys.Length == 0) { skipped++; continue; }
                fields.TryGetValue("context", out context);
                string note = string.IsNullOrWhiteSpace(context) || context.Equals("tab window", StringComparison.OrdinalIgnoreCase) ? "" : "Jancy context: " + context;
                app.Add(JancyAction(id), keys, KeybindScope.AppActive, true, note);
            }
            if (skipped > 0) app.Problem = skipped + " line" + (skipped == 1 ? "" : "s") + " of keybinds.txt could not be read.";
            return app;
        }

        static string JancyAction(string id)
        {
            string text = KeyText.Title(id.StartsWith("user-", StringComparison.OrdinalIgnoreCase) ? id.Substring(5) : id);
            foreach (string acronym in new[] { "Tm", "Sms", "Cvv", "Url", "Id", "Ui", "Ip" })
                text = Regex.Replace(text, @"\b" + acronym + @"\b", acronym.ToUpperInvariant());
            return text;
        }

        // ---------------------------------------------------------------- Record Keeper (nothing saved; one key around the OBS preview)

        public static AppKeybinds RecordKeeper()
        {
            var app = new AppKeybinds(RecordKeeperName);
            app.Add("Close the OBS preview (or cancel a screenshot)", "Esc", KeybindScope.AppActive, false, "Only while Record Keeper's OBS preview is the active window");
            app.Add("Submit added rooms", "Ctrl+Enter", KeybindScope.AppActive, false, "In the Add rooms panel");
            return app;
        }

        // ---------------------------------------------------------------- apps with only built-in, in-window shortcuts

        public const string LittleButtonsName = "Little Buttons", StorageLensName = "Storage Lens", TweakBenchName = "Tweak Bench";

        public static AppKeybinds LittleButtons()
        {
            var app = new AppKeybinds(LittleButtonsName);
            app.Add("Full screen on / off", "F11", KeybindScope.AppActive, false);
            app.Add("Parent controls", "Ctrl+Shift+P", KeybindScope.AppActive, false, "Also stops a song while the song player is open");
            return app;
        }

        public static AppKeybinds StorageLens()
        {
            var app = new AppKeybinds(StorageLensName);
            app.Add("Scan again", "F5", KeybindScope.AppActive, false);
            app.Add("Back", "Alt+Left", KeybindScope.AppActive, false);
            app.Add("Parent folder", "Alt+Up", KeybindScope.AppActive, false);
            app.Add("Move to the Recycle Bin", "Delete", KeybindScope.AppActive, false, "On a selected row");
            return app;
        }

        public static AppKeybinds TweakBench()
        {
            var app = new AppKeybinds(TweakBenchName);
            app.Add("Search", "Ctrl+F", KeybindScope.AppActive, false);
            return app;
        }

        // ---------------------------------------------------------------- helpers

        static IDictionary<string, object> AsObject(object tree) { return tree as IDictionary<string, object>; }

        static int Int(IDictionary<string, object> tree, string name, int fallback)
        {
            object value;
            if (tree == null || !tree.TryGetValue(name, out value) || value == null) return fallback;
            try { return Convert.ToInt32(value, CultureInfo.InvariantCulture); }
            catch (Exception) { return fallback; }
        }

        static string Str(IDictionary<string, object> tree, string name)
        {
            object value;
            if (tree == null || !tree.TryGetValue(name, out value) || value == null) return null;
            return Convert.ToString(value, CultureInfo.InvariantCulture);
        }
    }
}
