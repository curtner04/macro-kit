using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using MacroKit.Core;

namespace MacroKit.Keybinds
{
    /// <summary>
    /// Readers for programs that are not Macro Kit's sister apps but keep their shortcuts in files anyone can read
    /// (PowerToys, OBS Studio). Each returns null when the program is not installed, so it only shows up on PCs that have it.
    /// Like the other readers they only read: readText gets a file's text, null for a missing file, and may throw for a locked one.
    /// </summary>
    public static class OtherAppReaders
    {
        public const string PowerToysName = "PowerToys", ObsName = "OBS Studio";

        // ================================================================ PowerToys

        /// <summary>
        /// %LOCALAPPDATA%\Microsoft\PowerToys: settings.json says which tools are switched on ("enabled"), and each tool keeps
        /// its shortcuts in &lt;tool&gt;\settings.json as {"win","ctrl","alt","shift","code"} objects (code = virtual-key code).
        /// Command Palette keeps its own settings in its app package. Keyboard Manager remaps count as taken keys too.
        /// </summary>
        public static AppKeybinds PowerToys(string root, string commandPaletteSettings, Func<string, string> readText)
        {
            if (string.IsNullOrEmpty(root)) return null;
            var problems = new List<string>();
            string mainPath = Path.Combine(root, "settings.json");
            string mainText = Read(readText, mainPath, problems);
            if (mainText == null && problems.Count == 0) return null;   // not installed

            var app = new AppKeybinds(PowerToysName, root) { Processes = new[] { "PowerToys" } };
            IDictionary<string, object> enabled = null;
            if (mainText != null)
            {
                try { enabled = Get(AsObject(Json.ParseTree(mainText)), "enabled") as IDictionary<string, object>; }
                catch (Exception ex) { problems.Add("Could not read settings.json (" + ex.Message + ")."); }
            }
            if (enabled == null)
            {
                if (problems.Count == 0) problems.Add("settings.json does not say which tools are on.");
                app.Problem = string.Join(" ", problems);
                return app;
            }

            foreach (KeyValuePair<string, object> tool in enabled.OrderBy(t => ToolName(t.Key), StringComparer.OrdinalIgnoreCase))
            {
                if (!(tool.Value is bool) || !(bool)tool.Value) continue;   // switched off in PowerToys: its shortcuts do nothing
                if (tool.Key == "CmdPal") { CommandPalette(app, commandPaletteSettings, readText, problems); continue; }
                if (tool.Key.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) continue;
                string path = Path.Combine(root, tool.Key, "settings.json");
                string text = Read(readText, path, problems);
                if (text == null) continue;
                IDictionary<string, object> properties;
                try { properties = Get(AsObject(Json.ParseTree(text)), "properties") as IDictionary<string, object>; }
                catch (Exception ex) { problems.Add("Could not read " + tool.Key + "\\settings.json (" + ex.Message + ")."); continue; }
                if (properties == null) continue;

                if (tool.Key == "Keyboard Manager") { KeyboardManager(app, root, properties, readText, problems); }
                foreach (KeyValuePair<string, object> property in properties)
                {
                    if (property.Key.StartsWith("Default", StringComparison.OrdinalIgnoreCase)) continue;   // the factory value, not the one in use
                    if (tool.Key == "FindMyMouse" && property.Key == "activation_shortcut" && Int(Get(properties, "activation_method"), 0) != FindMyMouseShortcutMethod) continue;
                    foreach (KeyValuePair<string, IDictionary<string, object>> found in Shortcuts(property.Key, property.Value))
                    {
                        Hotkey combo = PowerToysCombo(found.Value);
                        if (combo.IsEmpty) continue;
                        Keybind added = app.Add(ToolName(tool.Key) + ": " + ShortcutName(tool.Key, found.Key), combo, KeybindScope.Global, true);
                        if (tool.Key == "Peek")
                        {
                            // Peek only acts on a file selected in File Explorer; in other windows the keys go through to that window.
                            added.Scope = KeybindScope.AppActive;
                            added.Note = "Only while File Explorer or the desktop is the active window";
                        }
                    }
                }
            }
            if (problems.Count > 0) app.Problem = string.Join(" ", problems);
            return app;
        }

        // Find My Mouse: 0 = press Left Ctrl twice, 1 = Right Ctrl twice, 2 = shake the mouse, 3 = its shortcut.
        const int FindMyMouseShortcutMethod = 3;

        static void CommandPalette(AppKeybinds app, string path, Func<string, string> readText, List<string> problems)
        {
            if (string.IsNullOrEmpty(path)) return;
            string text = Read(readText, path, problems);
            if (text == null) return;
            IDictionary<string, object> settings;
            try { settings = AsObject(Json.ParseTree(text)); }
            catch (Exception ex) { problems.Add("Could not read Command Palette's settings (" + ex.Message + ")."); return; }
            Hotkey open = PowerToysCombo(Get(settings, "Hotkey") as IDictionary<string, object>);
            if (!open.IsEmpty) app.Add("Command Palette: Open Command Palette", open, KeybindScope.Global, true);
            foreach (object item in Items(Get(settings, "CommandHotkeys")))
            {
                var entry = item as IDictionary<string, object>;
                Hotkey combo = PowerToysCombo(Get(entry, "Hotkey") as IDictionary<string, object>);
                if (combo.IsEmpty) continue;
                string command = Convert.ToString(Get(entry, "CommandId") ?? "", CultureInfo.InvariantCulture);
                app.Add("Command Palette: Run " + (command.Length > 0 ? "“" + command + "”" : "a command"), combo, KeybindScope.Global, true);
            }
        }

        /// <summary>
        /// Keyboard Manager's active profile (&lt;activeConfiguration&gt;.json): remapped shortcuts ("17;16;65" = Ctrl+Shift+A) never
        /// reach other apps; ones remapped for one program only count while that program is active.
        /// </summary>
        static void KeyboardManager(AppKeybinds app, string root, IDictionary<string, object> properties, Func<string, string> readText, List<string> problems)
        {
            string profile = Convert.ToString(Value(Get(properties, "activeConfiguration")) ?? "default", CultureInfo.InvariantCulture);
            if (profile.Length == 0 || profile.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) profile = "default";
            string text = Read(readText, Path.Combine(root, "Keyboard Manager", profile + ".json"), problems);
            if (text == null) return;
            IDictionary<string, object> remaps;
            try { remaps = Get(AsObject(Json.ParseTree(text)), "remapShortcuts") as IDictionary<string, object>; }
            catch (Exception ex) { problems.Add("Could not read Keyboard Manager's remaps (" + ex.Message + ")."); return; }
            foreach (object item in Items(Get(remaps, "global")))
            {
                Hotkey combo = KeyList(Convert.ToString(Get(item as IDictionary<string, object>, "originalKeys") ?? "", CultureInfo.InvariantCulture));
                if (!combo.IsEmpty) app.Add("Keyboard Manager: Remapped to something else", combo, KeybindScope.Global, true);
            }
            foreach (object item in Items(Get(remaps, "appSpecific")))
            {
                var entry = item as IDictionary<string, object>;
                Hotkey combo = KeyList(Convert.ToString(Get(entry, "originalKeys") ?? "", CultureInfo.InvariantCulture));
                string target = Convert.ToString(Get(entry, "targetApp") ?? "", CultureInfo.InvariantCulture);
                if (!combo.IsEmpty) app.Add("Keyboard Manager: Remapped to something else", combo, KeybindScope.AppActive, true, "Only while " + (target.Length > 0 ? target : "one program") + " is the active window");
            }
        }

        /// <summary>"17;16;65" (virtual-key codes, modifiers first) as a combination; empty when it holds no ordinary key.</summary>
        static Hotkey KeyList(string text)
        {
            HotkeyModifiers modifiers = HotkeyModifiers.None;
            ushort vk = 0;
            foreach (string part in text.Split(';'))
            {
                int code;
                if (!int.TryParse(part.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out code) || code <= 0 || code > 0xFE) return default(Hotkey);
                switch (code)
                {
                    case 0x10: case 0xA0: case 0xA1: modifiers |= HotkeyModifiers.Shift; break;
                    case 0x11: case 0xA2: case 0xA3: modifiers |= HotkeyModifiers.Ctrl; break;
                    case 0x12: case 0xA4: case 0xA5: modifiers |= HotkeyModifiers.Alt; break;
                    case 0x5B: case 0x5C: modifiers |= HotkeyModifiers.Win; break;
                    default: if (vk != 0) return default(Hotkey); vk = (ushort)code; break;
                }
            }
            return vk == 0 ? default(Hotkey) : new Hotkey(modifiers, vk);
        }

        /// <summary>Every shortcut object under a property: the property itself, its "value", or (Advanced Paste) nested actions.</summary>
        static IEnumerable<KeyValuePair<string, IDictionary<string, object>>> Shortcuts(string name, object node)
        {
            var tree = node as IDictionary<string, object>;
            if (tree == null) yield break;
            if (IsShortcut(tree)) { yield return new KeyValuePair<string, IDictionary<string, object>>(name, tree); yield break; }
            foreach (KeyValuePair<string, object> child in tree)
            {
                string childName = child.Key == "value" || child.Key == "shortcut" ? name : child.Key;
                foreach (var found in Shortcuts(childName, child.Value)) yield return found;
            }
        }

        static bool IsShortcut(IDictionary<string, object> tree) { return tree.ContainsKey("code") && (tree.ContainsKey("win") || tree.ContainsKey("ctrl")); }

        static Hotkey PowerToysCombo(IDictionary<string, object> tree)
        {
            if (tree == null || !IsShortcut(tree)) return default(Hotkey);
            int code = Int(Get(tree, "code"), 0);
            if (code <= 0 || code > 0xFE) return default(Hotkey);
            HotkeyModifiers modifiers = HotkeyModifiers.None;
            if (True(Get(tree, "win"))) modifiers |= HotkeyModifiers.Win;
            if (True(Get(tree, "ctrl"))) modifiers |= HotkeyModifiers.Ctrl;
            if (True(Get(tree, "alt"))) modifiers |= HotkeyModifiers.Alt;
            if (True(Get(tree, "shift"))) modifiers |= HotkeyModifiers.Shift;
            return new Hotkey(modifiers, (ushort)code);
        }

        static readonly Dictionary<string, string> toolNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "AdvancedPaste", "Advanced Paste" }, { "AlwaysOnTop", "Always On Top" }, { "ColorPicker", "Color Picker" },
            { "CropAndLock", "Crop And Lock" }, { "FancyZones", "FancyZones" }, { "FindMyMouse", "Find My Mouse" },
            { "MouseHighlighter", "Mouse Highlighter" }, { "MouseJump", "Mouse Jump" }, { "MousePointerCrosshairs", "Mouse Pointer Crosshairs" },
            { "MouseWithoutBorders", "Mouse Without Borders" }, { "PowerDisplay", "Power Display" }, { "TextExtractor", "Text Extractor" },
            { "LightSwitch", "Light Switch" }, { "CmdPal", "Command Palette" }, { "ZoomIt", "ZoomIt" },
        };

        static string ToolName(string key) { string name; return toolNames.TryGetValue(key, out name) ? name : key; }

        static readonly Dictionary<string, string> shortcutNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "AlwaysOnTop/hotkey", "Pin or unpin the active window" },
            { "AlwaysOnTop/increase-opacity-hotkey", "Make the pinned window more opaque" },
            { "AlwaysOnTop/decrease-opacity-hotkey", "Make the pinned window more see-through" },
            { "AdvancedPaste/advanced-paste-ui-hotkey", "Open Advanced Paste" },
            { "AdvancedPaste/paste-as-plain-hotkey", "Paste as plain text" },
            { "AdvancedPaste/paste-as-markdown-hotkey", "Paste as Markdown" },
            { "AdvancedPaste/paste-as-json-hotkey", "Paste as JSON" },
            { "CropAndLock/reparent-hotkey", "Crop the active window (reparent)" },
            { "CropAndLock/thumbnail-hotkey", "Crop the active window (thumbnail)" },
            { "CropAndLock/screenshot-hotkey", "Crop the active window (screenshot)" },
            { "FancyZones/fancyzones_editor_hotkey", "Open the layout editor" },
            { "FancyZones/fancyzones_nextTab_hotkey", "Next window in the zone" },
            { "FancyZones/fancyzones_prevTab_hotkey", "Previous window in the zone" },
            { "MousePointerCrosshairs/gliding_cursor_activation_shortcut", "Gliding cursor" },
            { "MouseWithoutBorders/ToggleEasyMouseShortcut", "Easy Mouse on / off" },
            { "MouseWithoutBorders/LockMachineShortcut", "Lock every PC" },
            { "MouseWithoutBorders/ReconnectShortcut", "Reconnect" },
            { "MouseWithoutBorders/Switch2AllPCShortcut", "Switch to all PCs mode" },
            { "LightSwitch/toggle-theme-hotkey", "Switch light / dark theme" },
            { "PowerToys Run/open_powerlauncher", "Open PowerToys Run" },
            { "PowerToys Run/open_file_location", "Open the file location" },
            { "PowerToys Run/copy_path_location", "Copy the path" },
            { "PowerToys Run/open_console", "Open a console here" },
            { "Shortcut Guide/open_shortcutguide", "Show the Windows shortcut guide" },
            { "Workspaces/hotkey", "Open the Workspaces editor" },
            { "Keyboard Manager/EditorShortcut", "Open the Keyboard Manager editor" },
        };

        static string ShortcutName(string tool, string property)
        {
            string name;
            if (shortcutNames.TryGetValue(tool + "/" + property, out name)) return name;
            string lower = property.ToLowerInvariant();
            if (lower == "activationshortcut" || lower == "activation_shortcut" || lower == "hotkey") return "Open " + ToolName(tool);
            return Sentence(property);
        }

        /// <summary>"paste-as-txt-file" / "image-to-text" / "OpenThing" as plain words: "Paste as txt file".</summary>
        static string Sentence(string id)
        {
            var words = new StringBuilder();
            for (int i = 0; i < id.Length; i++)
            {
                char ch = id[i];
                if (ch == '-' || ch == '_') { words.Append(' '); continue; }
                if (i > 0 && char.IsUpper(ch) && char.IsLower(id[i - 1])) words.Append(' ');
                words.Append(char.ToLowerInvariant(ch));
            }
            string text = string.Join(" ", words.ToString().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Where(w => w != "hotkey" && w != "shortcut"));
            return text.Length == 0 ? id : char.ToUpperInvariant(text[0]) + text.Substring(1);
        }

        // ================================================================ OBS Studio

        /// <summary>
        /// %APPDATA%\obs-studio: user.ini (older OBS: global.ini) names the profile and scene collection in use and when hotkeys
        /// work ([General] HotkeyFocusType). The profile's basic.ini [Hotkeys] holds stream/record/studio-mode hotkeys and the
        /// scene collection JSON holds per-scene and per-source ones, each as {"bindings":[{"key":"OBS_KEY_F9","control":true}]}.
        /// </summary>
        public static AppKeybinds Obs(string root, Func<string, string> readText)
        {
            if (string.IsNullOrEmpty(root)) return null;
            var problems = new List<string>();
            string userText = Read(readText, Path.Combine(root, "user.ini"), problems);
            string globalText = Read(readText, Path.Combine(root, "global.ini"), problems);
            if (userText == null && globalText == null && problems.Count == 0) return null;   // not installed
            var app = new AppKeybinds(ObsName, root) { Processes = new[] { "obs64", "obs32", "obs" } };

            Dictionary<string, Dictionary<string, string>> user = Ini(userText), global = Ini(globalText);
            Func<string, string, string> setting = (section, key) => IniValue(user, section, key) ?? IniValue(global, section, key);
            string focus = setting("General", "HotkeyFocusType") ?? "NeverDisableHotkeys";
            KeybindScope scope = focus == "DisableHotkeysOutOfFocus" ? KeybindScope.AppActive : KeybindScope.Global;
            string note = focus == "DisableHotkeysOutOfFocus" ? "OBS is set to ignore hotkeys while it is not the active window"
                : focus == "DisableHotkeysInFocus" ? "Works in other windows, not while OBS is the active window (an OBS setting)"
                : "OBS also lets the keys through to the active window";

            string profileDir = setting("Basic", "ProfileDir") ?? setting("Basic", "Profile") ?? "Untitled";
            string collection = setting("Basic", "SceneCollectionFile") ?? setting("Basic", "SceneCollection") ?? "Untitled";
            if (!collection.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) collection += ".json";
            string profiles = Location(IniValue(global, "Locations", "Profiles"), root, "profiles");
            string scenes = Location(IniValue(global, "Locations", "SceneCollections"), root, "scenes");

            if (Safe(profileDir))
            {
                string text = Read(readText, Path.Combine(profiles, profileDir, "basic.ini"), problems);
                Dictionary<string, string> hotkeys;
                if (text != null && Ini(text).TryGetValue("Hotkeys", out hotkeys))
                    foreach (KeyValuePair<string, string> hotkey in hotkeys)
                        AddObsBindings(app, ObsAction(hotkey.Key, null), ObsBindings(hotkey.Value, problems), scope, note);
            }
            if (Safe(collection))
            {
                string text = Read(readText, Path.Combine(scenes, collection), problems);
                if (text != null)
                {
                    try { WalkObsSources(app, Json.ParseTree(text), null, scope, note); }
                    catch (Exception ex) { problems.Add("Could not read the scene collection (" + ex.Message + ")."); }
                }
            }
            if (problems.Count > 0) app.Problem = string.Join(" ", problems);
            return app;
        }

        static bool Safe(string name) { return !string.IsNullOrEmpty(name) && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0; }

        /// <summary>[Locations] Profiles / SceneCollections name the folder that holds obs-studio\basic\...; default is the OBS root.</summary>
        static string Location(string configured, string root, string kind)
        {
            if (!string.IsNullOrEmpty(configured))
            {
                string folder = configured.Replace("\\\\", "\\").Replace('/', '\\');
                try { if (Path.IsPathRooted(folder)) return Path.Combine(folder, "obs-studio", "basic", kind); }
                catch (ArgumentException) { }
            }
            return Path.Combine(root, "basic", kind);
        }

        static void WalkObsSources(AppKeybinds app, object node, string owner, KeybindScope scope, string note)
        {
            var tree = node as IDictionary<string, object>;
            if (tree != null)
            {
                string name = Get(tree, "name") as string ?? owner;
                foreach (KeyValuePair<string, object> child in tree)
                {
                    if (child.Key == "hotkeys")
                    {
                        var hotkeys = child.Value as IDictionary<string, object>;
                        if (hotkeys == null) continue;
                        foreach (KeyValuePair<string, object> hotkey in hotkeys)
                            AddObsBindings(app, ObsAction(hotkey.Key, name), Items(hotkey.Value).OfType<IDictionary<string, object>>(), scope, note);
                    }
                    else if (child.Key != "settings") WalkObsSources(app, child.Value, name, scope, note);
                }
                return;
            }
            if (node is string) return;
            foreach (object item in Items(node)) WalkObsSources(app, item, owner, scope, note);
        }

        static IEnumerable<IDictionary<string, object>> ObsBindings(string json, List<string> problems)
        {
            try { return Items(Get(AsObject(Json.ParseTree(json)), "bindings")).OfType<IDictionary<string, object>>().ToList(); }
            catch (Exception) { return Enumerable.Empty<IDictionary<string, object>>(); }
        }

        static void AddObsBindings(AppKeybinds app, string action, IEnumerable<IDictionary<string, object>> bindings, KeybindScope scope, string note)
        {
            foreach (IDictionary<string, object> binding in bindings)
            {
                Hotkey combo = ObsCombo(binding);
                if (!combo.IsEmpty) app.Add(action, combo, scope, true, note);
            }
        }

        static Hotkey ObsCombo(IDictionary<string, object> binding)
        {
            string key = Get(binding, "key") as string;
            if (string.IsNullOrEmpty(key) || !key.StartsWith("OBS_KEY_", StringComparison.Ordinal)) return default(Hotkey);   // mouse buttons and unset keys
            string name = key.Substring("OBS_KEY_".Length);
            string mapped;
            if (obsKeys.TryGetValue(name, out mapped)) name = mapped;
            else if (name.StartsWith("NUM", StringComparison.Ordinal) && name.Length > 3 && char.IsDigit(name[3])) name = "num" + name.Substring(3);
            Hotkey bare;
            if (!KeyText.TryParse(name, out bare) || bare.Modifiers != HotkeyModifiers.None) return default(Hotkey);
            HotkeyModifiers modifiers = HotkeyModifiers.None;
            if (True(Get(binding, "control"))) modifiers |= HotkeyModifiers.Ctrl;
            if (True(Get(binding, "shift"))) modifiers |= HotkeyModifiers.Shift;
            if (True(Get(binding, "alt"))) modifiers |= HotkeyModifiers.Alt;
            if (True(Get(binding, "command"))) modifiers |= HotkeyModifiers.Win;
            return new Hotkey(modifiers, bare.Vk);
        }

        static readonly Dictionary<string, string> obsKeys = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "PRINT", "prtsc" }, { "RETURN", "enter" }, { "ESCAPE", "esc" }, { "BACKSPACE", "backspace" }, { "PAGEUP", "pageup" },
            { "PAGEDOWN", "pagedown" }, { "NUMASTERISK", "numpadmultiply" }, { "NUMPLUS", "numpadadd" }, { "NUMMINUS", "numpadsubtract" },
            { "NUMPERIOD", "numpaddecimal" }, { "NUMSLASH", "numpaddivide" }, { "MINUS", "minus" }, { "EQUAL", "equal" },
            { "BRACKETLEFT", "bracketleft" }, { "BRACKETRIGHT", "bracketright" }, { "BACKSLASH", "backslash" }, { "SEMICOLON", "semicolon" },
            { "APOSTROPHE", "quote" }, { "QUOTE", "quote" }, { "GRAVE", "backquote" }, { "COMMA", "comma" }, { "PERIOD", "period" }, { "SLASH", "slash" },
            { "MENU", "apps" }, { "SCROLLLOCK", "scrolllock" }, { "NUMLOCK", "numlock" }, { "CAPSLOCK", "capslock" },
        };

        static readonly Dictionary<string, string> obsActions = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "OBSBasic.StartStreaming", "Start streaming" }, { "OBSBasic.StopStreaming", "Stop streaming" },
            { "OBSBasic.ForceStopStreaming", "Stop streaming without the delay" }, { "OBSBasic.StartRecording", "Start recording" },
            { "OBSBasic.StopRecording", "Stop recording" }, { "OBSBasic.PauseRecording", "Pause recording" },
            { "OBSBasic.UnpauseRecording", "Resume recording" }, { "OBSBasic.SplitFile", "Split the recording file" },
            { "OBSBasic.StartReplayBuffer", "Start the replay buffer" }, { "OBSBasic.StopReplayBuffer", "Stop the replay buffer" },
            { "OBSBasic.StartVirtualCam", "Start the virtual camera" }, { "OBSBasic.StopVirtualCam", "Stop the virtual camera" },
            { "OBSBasic.EnablePreview", "Turn the preview on" }, { "OBSBasic.DisablePreview", "Turn the preview off" },
            { "OBSBasic.EnablePreviewProgram", "Studio mode on" }, { "OBSBasic.DisablePreviewProgram", "Studio mode off" },
            { "OBSBasic.Transition", "Transition (studio mode)" }, { "OBSBasic.ResetStats", "Reset the stats" },
            { "OBSBasic.Screenshot", "Take a screenshot of the output" }, { "OBSBasic.SelectedSourceScreenshot", "Take a screenshot of the selected source" },
            { "ReplayBuffer.Save", "Save the replay" },
        };

        static string ObsAction(string id, string owner)
        {
            string quoted = string.IsNullOrEmpty(owner) ? "" : " “" + owner + "”";
            string name;
            if (obsActions.TryGetValue(id, out name)) return name;
            switch (id)
            {
                case "OBSBasic.SelectScene": return "Switch to scene" + quoted;
                case "libobs.mute": return "Mute" + quoted;
                case "libobs.unmute": return "Unmute" + quoted;
                case "libobs.push-to-mute": return "Push to mute" + quoted;
                case "libobs.push-to-talk": return "Push to talk" + quoted;
            }
            if (id.StartsWith("libobs.show_scene_item.", StringComparison.Ordinal)) return "Show a source in" + quoted;
            if (id.StartsWith("libobs.hide_scene_item.", StringComparison.Ordinal)) return "Hide a source in" + quoted;
            int dot = id.LastIndexOf('.');
            return Sentence(dot >= 0 ? id.Substring(dot + 1) : id) + quoted;
        }

        /// <summary>A minimal INI reader: [Section] then key=value lines; later duplicates win; ; and # start comments.</summary>
        internal static Dictionary<string, Dictionary<string, string>> Ini(string text)
        {
            var sections = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            if (text == null) return sections;
            Dictionary<string, string> current = null;
            foreach (string raw in text.Split('\n'))
            {
                string line = raw.Trim().TrimStart('\uFEFF');
                if (line.Length == 0 || line[0] == ';' || line[0] == '#') continue;
                if (line[0] == '[' && line.EndsWith("]"))
                {
                    string name = line.Substring(1, line.Length - 2).Trim();
                    if (!sections.TryGetValue(name, out current)) { current = new Dictionary<string, string>(StringComparer.Ordinal); sections[name] = current; }
                    continue;
                }
                int equals = line.IndexOf('=');
                if (current == null || equals <= 0) continue;
                current[line.Substring(0, equals).Trim()] = line.Substring(equals + 1).Trim();
            }
            return sections;
        }

        static string IniValue(Dictionary<string, Dictionary<string, string>> ini, string section, string key)
        {
            Dictionary<string, string> values;
            string value;
            return ini.TryGetValue(section, out values) && values.TryGetValue(key, out value) && value.Length > 0 ? value : null;
        }

        // ================================================================ helpers

        static string Read(Func<string, string> readText, string path, List<string> problems)
        {
            try { return readText(path); }
            catch (Exception ex) { problems.Add("Could not open " + Path.GetFileName(path) + " (" + ex.Message + ")."); return null; }
        }

        static IDictionary<string, object> AsObject(object tree) { return tree as IDictionary<string, object>; }

        static object Get(IDictionary<string, object> tree, string name)
        {
            object value;
            return tree != null && tree.TryGetValue(name, out value) ? value : null;
        }

        /// <summary>PowerToys wraps many settings as {"value": x}.</summary>
        static object Value(object node)
        {
            var tree = node as IDictionary<string, object>;
            return tree != null && tree.ContainsKey("value") ? tree["value"] : node;
        }

        static IEnumerable<object> Items(object node)
        {
            if (node == null || node is string || node is IDictionary<string, object>) return Enumerable.Empty<object>();
            var list = node as IEnumerable;
            return list == null ? Enumerable.Empty<object>() : list.Cast<object>();
        }

        static bool True(object node) { return node is bool && (bool)node; }

        static int Int(object node, int fallback)
        {
            node = Value(node);
            if (node == null || node is bool || node is IEnumerable) return fallback;
            try { return Convert.ToInt32(node, CultureInfo.InvariantCulture); }
            catch (Exception) { return fallback; }
        }
    }

    /// <summary>Apps the person added by hand: their shortcuts as typed in, with problems to show before saving.</summary>
    public static class CustomKeybinds
    {
        public static AppKeybinds Read(KeybindAppDef def)
        {
            var app = new AppKeybinds(def.Name ?? "") { CustomId = def.Id };
            string program = ProgramName(def.Program);
            if (program.Length > 0) app.Processes = new[] { program };
            foreach (KeybindDef shortcut in def.Shortcuts ?? new List<KeybindDef>())
            {
                if (string.IsNullOrWhiteSpace(shortcut.Keys)) continue;
                Keybind added = app.Add(string.IsNullOrWhiteSpace(shortcut.Action) ? "(no description)" : shortcut.Action.Trim(), shortcut.Keys,
                    shortcut.OnlyWhenActive ? KeybindScope.AppActive : KeybindScope.Global, true);
                if (added.Combo.IsEmpty) added.Note = "Macro Kit does not recognise these keys";
            }
            return app;
        }

        /// <summary>"C:\...\obs64.exe" / "obs64.exe" / " obs64 " as the bare process name "obs64".</summary>
        public static string ProgramName(string text)
        {
            string name = (text ?? "").Trim().Trim('"');
            if (name.Length == 0) return "";
            int slash = name.LastIndexOfAny(new[] { '\\', '/' });
            if (slash >= 0) name = name.Substring(slash + 1);
            if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name.Substring(0, name.Length - 4);
            return name.Trim();
        }

        /// <summary>Why an app cannot be saved yet, or an empty list. takenNames are the other apps on the tab.</summary>
        public static List<string> Problems(KeybindAppDef def, IEnumerable<string> takenNames)
        {
            var problems = new List<string>();
            string name = (def.Name ?? "").Trim();
            if (name.Length == 0) problems.Add("Give the app a name.");
            else if (takenNames.Any(n => string.Equals((n ?? "").Trim(), name, StringComparison.OrdinalIgnoreCase)))
                problems.Add("“" + name + "” is already on the list. Pick another name.");
            if (ProgramName(def.Program).IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) problems.Add("The program name has characters a file name cannot have.");
            int row = 0;
            foreach (KeybindDef shortcut in def.Shortcuts ?? new List<KeybindDef>())
            {
                row++;
                if (string.IsNullOrWhiteSpace(shortcut.Keys)) { if (!string.IsNullOrWhiteSpace(shortcut.Action)) problems.Add("Shortcut " + row + " has no keys."); continue; }
                Hotkey combo;
                if (!KeyText.TryParse(shortcut.Keys, out combo)) problems.Add("Shortcut " + row + ": Macro Kit does not recognise “" + shortcut.Keys.Trim() + "”. Try a form such as Ctrl+Shift+M or F9.");
            }
            return problems;
        }
    }
}
