using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AppShared;
using MacroKit.Core;
using MacroKit.Keybinds;
using Rec = MacroKit.Core.Recording;

namespace MacroKit.App
{
    /// <summary>
    /// --self-test-ui &lt;dir&gt;: builds every window with synthetic data, renders each to a PNG and checks the UI logic that can run
    /// without a person (palette insertion, parse preview, settings round trip, no-activate styles). It installs no hooks or hotkeys,
    /// sends no input, touches no clipboard and never changes focus. Results go to self-test.txt; the exit code is the number of failures.
    /// </summary>
    public static class SelfTest
    {
        static readonly List<string> report = new List<string>();
        static int failures;

        public static int Run(string folder)
        {
            Directory.CreateDirectory(folder);
            foreach (string old in Directory.GetFiles(folder, "*.png")) File.Delete(old);
            // Synthetic data always goes into a fresh folder of its own: the --data folder when given, otherwise the temp folder.
            // Nothing is ever deleted, and the real data folder is never used.
            string parent = HasDataOverride() ? Paths.DataRoot : Path.Combine(Path.GetTempPath(), "MacroKit-selftest");
            string data = Path.Combine(parent, "run-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(data);
            Paths.Override(data);

            try { RunChecks(folder, data); }
            catch (Exception ex) { Fail("self test", ex.ToString()); }
            File.WriteAllLines(Path.Combine(folder, "self-test.txt"), report.Concat(new[] { failures == 0 ? "All checks passed." : failures + " check(s) failed." }), new UTF8Encoding(false));
            return failures;
        }

        static bool HasDataOverride()
        {
            string[] args = Environment.GetCommandLineArgs();
            return Array.IndexOf(args, "--data") >= 0;
        }

        static void Pass(string name) { report.Add("PASS " + name); }
        static void Fail(string name, string why) { failures++; report.Add("FAIL " + name + ": " + why); }

        static void Check(string name, Func<bool> test)
        {
            try { if (test()) Pass(name); else Fail(name, "the check returned false"); }
            catch (Exception ex) { Fail(name, ex.Message); }
        }

        static void Step(string name, Action action)
        {
            try { action(); Pass(name); }
            catch (Exception ex) { Fail(name, ex.ToString()); }
        }

        // ------------------------------------------------------------------ synthetic data

        static void SeedData(Storage storage)
        {
            storage.SavePhrases(new List<PhraseDef>
            {
                new PhraseDef { Trigger = "/shrug", Replacement = "¯\\_(ツ)_/¯" },
                new PhraseDef { Trigger = "/sig", Replacement = "Best regards,\nSam\nSent from my desk", When = ExpandWhen.AfterDelimiter },
                new PhraseDef { Trigger = ";addr", Replacement = "1 Example Street, Springfield", Method = InsertMethod.Paste },
                new PhraseDef { Trigger = "/ty", Replacement = "Thank you so much for your help!", Enabled = false },
                new PhraseDef { Trigger = "BTW", Replacement = "by the way", MatchCase = true, When = ExpandWhen.AfterDelimiter }
            });

            MacroDef copy = Storage.SeedMacros()[0];
            copy.Repeat = RepeatMode.Times;
            copy.RepeatTimes = 25;
            copy.Target = TargetKind.SpecificWindow;
            copy.TargetWindow = new WindowMatch { ProcessName = "EXCEL.EXE", TitleContains = "Book1" };
            copy.AltTabPartner = new WindowMatch { ProcessName = "Chrome.exe", TitleContains = "Orders" };
            copy.Hotkey = "Ctrl+Alt+1";
            var broken = new MacroDef { Name = "Broken example", Script = "{{CTRL}{C}}{{PAUSE:fifty}}{{ALT}{TAB}}{{FOO}}\n{{INPUT}}", ShowOnDeck = true };
            var paste = new MacroDef { Name = "Paste ten times", Script = "{{KEYPRESS:DOWN:10:100}}{{CTRL}{V}}", Repeat = RepeatMode.Continuous, Hotkey = "Ctrl+Alt+2" };
            var note = new MacroDef { Name = "Sign the form", Script = "{{//}} fills the last page\nSam Example{{TAB}}{{MLEFTDBLCLICK}}", ShowOnDeck = true };
            storage.SaveMacros(new List<MacroDef> { copy, broken, paste, note });

            var login = new Rec { Name = "Login steps", Created = "2026-11-11T16:20:00-05:00", DurationMs = 8400 };
            for (int i = 0; i < 40; i++) login.Events.Add(new RecEvent(i * 210, RecKind.Move, 100 + i * 8, 200 + i * 3));
            storage.SaveRecording(login);
            var rows = new Rec { Name = "Copy table rows", Created = "2026-11-12T08:05:00-05:00", DurationMs = 31200 };
            for (int i = 0; i < 90; i++) rows.Events.Add(new RecEvent(i * 340, i % 3 == 0 ? RecKind.KeyDown : RecKind.Move, i % 3 == 0 ? 67 : 300 + i, 400));
            storage.SaveRecording(rows);

            AppSettings settings = storage.LoadSettings();
            settings.SelectedRecordingId = login.Id;
            settings.DeckVisible = true;
            settings.ExcludedPrograms = new List<string> { "game.exe", "obs64.exe" };
            settings.Playback.Speed = 2;
            settings.Playback.Repeat = RepeatMode.Times;
            settings.Playback.RepeatTimes = 3;
            storage.SaveSettings(settings);
        }

        // ------------------------------------------------------------------ the run

        static void RunChecks(string folder, string data)
        {
            var storage = new Storage(data);
            SeedData(storage);
            storage = new Storage(data);

            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            var controller = new AppController(app.Dispatcher, storage, false);
            Theme.Initialize(app, "Light");
            var shell = new AppShell(controller, false);
            MainWindow main = shell.Main;
            UseKeybindFixtures(main.Keybinds, controller);
            controller.Pending = new Rec { Name = "Recording 2026-11-12 09:30", Created = "2026-11-12T09:30:00-05:00", DurationMs = 5600 };
            for (int i = 0; i < 120; i++) controller.Pending.Events.Add(new RecEvent(i * 40, RecKind.Move, 50 + i, 80));
            controller.Pending.DurationMs = 4760;
            controller.SelectRecording(controller.Pending.Id);
            controller.RefreshRecordings();

            Prepare(main, 1040, 720);
            main.Show();
            Settle(main);

            // ---- logic checks, headless
            RunLogicChecks(controller, shell, main, data);

            // ---- pictures
            foreach (string theme in new[] { "Light", "Dark" })
            {
                string suffix = theme.ToLowerInvariant();
                Theme.Apply(theme);
                Settle(main);
                PhrasesTab.PhraseExpressRunning = () => theme == "Light";
                main.Phrases.RefreshWarning();
                string[] tabs = { "recorder", "phrases", "macros", "settings", "keybinds" };
                for (int i = 0; i < tabs.Length; i++)
                {
                    int tab = i;
                    if (tab == 4) main.Keybinds.SetSearch("");
                    Step("render " + tabs[i] + " (" + suffix + ")", () =>
                    {
                        if (tab == 2) main.Macros.SelectMacro(0);
                        main.SelectTab(tab);
                        Settle(main);
                        Capture(main, Path.Combine(folder, (tab + 1) + "-" + tabs[tab] + "-" + suffix + ".png"));
                    });
                }
                PhrasesTab.PhraseExpressRunning = () => false;
                Step("render macros with errors (" + suffix + ")", () =>
                {
                    main.SelectTab(2);
                    main.Macros.SelectMacro(1);
                    Settle(main);
                    Capture(main, Path.Combine(folder, "3-macros-errors-" + suffix + ".png"));
                });
                Step("render macros, whole editor (" + suffix + ")", () =>
                {
                    main.Macros.SelectMacro(0);
                    main.Macros.ToggleStreamDeck(true);
                    main.Macros.ToggleOptions(true);
                    main.Height = 2100;
                    Settle(main);
                    Capture(main, Path.Combine(folder, "3-macros-full-" + suffix + ".png"));
                    main.Macros.ToggleStreamDeck(false);
                    main.Macros.ToggleOptions(false);
                    main.Height = 720;
                    Settle(main);
                });
                Step("render macros as key tiles (" + suffix + ")", () =>
                {
                    controller.Settings.MacroEditor = EditorKinds.Tiles;
                    controller.MarkDirty(SaveParts.Settings);
                    main.Macros.SelectMacro(0);
                    Settle(main);
                    Capture(main, Path.Combine(folder, "3-macros-tiles-" + suffix + ".png"));
                    main.Macros.TileEditor.SelectTile(0);
                    Settle(main);
                    Capture(main, Path.Combine(folder, "3-macros-tiles-edit-" + suffix + ".png"));
                    main.Macros.SelectMacro(1);
                    Settle(main);
                    Capture(main, Path.Combine(folder, "3-macros-tiles-errors-" + suffix + ".png"));
                    controller.Settings.MacroEditor = EditorKinds.Text;
                    controller.MarkDirty(SaveParts.Settings);
                    main.Macros.SelectMacro(0);
                    Settle(main);
                });
                Step("render the first-run editor question (" + suffix + ")", () =>
                {
                    var ask = new EditorChoiceWindow(null);
                    Prepare(ask, 760, 520);
                    ask.SizeToContent = SizeToContent.Height;
                    ask.Show();
                    Settle(ask);
                    Capture(ask, Path.Combine(folder, "7-editor-choice-" + suffix + ".png"));
                    ask.Close();
                });
                Step("render the key recorder (" + suffix + ")", () =>
                {
                    var rec = new KeyRecorderWindow(null);
                    Prepare(rec, 560, 360);
                    rec.SizeToContent = SizeToContent.Height;
                    rec.Show();
                    Settle(rec);
                    rec.Press(System.Windows.Input.Key.LeftCtrl, System.Windows.Input.Key.C);
                    rec.Press(System.Windows.Input.Key.LeftAlt, System.Windows.Input.Key.Tab);
                    rec.Press(System.Windows.Input.Key.LeftCtrl, System.Windows.Input.Key.V);
                    rec.Press(System.Windows.Input.Key.Enter);
                    Settle(rec);
                    Capture(rec, Path.Combine(folder, "7-key-recorder-" + suffix + ".png"));
                    rec.Close();
                });
                Step("render macros, every command group (" + suffix + ")", () =>
                {
                    main.Macros.SelectMacro(0);
                    string before = main.Macros.ScriptText;
                    main.Macros.SetScript("{{RUN:notepad}}{{WAITWINDOW:Notepad}}{{LOOP:3}}{{CTRL}{C}}{{SWITCH}}{{VOLUME:20}}{{OR}}{{VOLUME:80}}{{ENDSWITCH}}{{ENDLOOP}}{{MACRO:No such macro}}");
                    main.Macros.OpenAllCommandGroups();
                    main.Height = 2300;
                    Settle(main);
                    Capture(main, Path.Combine(folder, "3-macros-commands-" + suffix + ".png"));
                    main.Macros.SearchCommands("");
                    main.Macros.SetScript(before);
                    main.Height = 720;
                    Settle(main);
                });
                Step("render macros at the minimum size (" + suffix + ")", () =>
                {
                    main.Width = MainWindow.MinimumWidth;
                    main.Height = MainWindow.MinimumHeight;
                    main.Macros.SelectMacro(0);
                    Settle(main);
                    Capture(main, Path.Combine(folder, "3-macros-min-" + suffix + ".png"));
                    main.SelectTab(0);
                    Settle(main);
                    Capture(main, Path.Combine(folder, "1-recorder-min-" + suffix + ".png"));
                    main.Width = 1040;
                    main.Height = 720;
                    Settle(main);
                });

                Step("render keybinds checking keys (" + suffix + ")", () =>
                {
                    main.SelectTab(4);
                    main.Keybinds.SetSearch("ctrl+shift+c");
                    Settle(main);
                    Capture(main, Path.Combine(folder, "5-keybinds-check-" + suffix + ".png"));
                    main.Keybinds.SetSearch("");
                    main.Width = MainWindow.MinimumWidth;
                    main.Height = MainWindow.MinimumHeight;
                    Settle(main);
                    Capture(main, Path.Combine(folder, "5-keybinds-min-" + suffix + ".png"));
                    main.Width = 1040;
                    main.Height = 720;
                    Settle(main);
                });
                Step("render deck (" + suffix + ")", () => RenderFloating(shell.Deck, Path.Combine(folder, "5-deck-" + suffix + ".png")));
                RenderPills(shell, controller, folder, suffix);
                Step("render the add-an-app editor (" + suffix + ")", () =>
                {
                    KeybindAppDef chat = controller.KeybindApps.First(a => a.Name == "Chat App");
                    var editor = new KeybindAppEditor(chat, main.Keybinds.TakenNames(chat));
                    editor.AddRow("ctrl-banana", "Something", false);
                    var host = new Window { Content = editor.Root };
                    Theme.Style(host);
                    Prepare(host, 780, 600);
                    host.Show();
                    Settle(host);
                    Capture(host, Path.Combine(folder, "5-keybinds-editor-" + suffix + ".png"));
                    host.Content = null;
                    host.Close();
                });
                Step("render changelog (" + suffix + ")", () =>
                {
                    var log = new ChangelogWindow(null);
                    Prepare(log, 640, 520);
                    log.MinWidth = 0; log.MinHeight = 0;
                    log.Show();
                    Settle(log);
                    Capture(log, Path.Combine(folder, "6-changelog-" + suffix + ".png"));
                    log.Close();
                });
            }

            Check("floating windows are no-activate tool windows", () =>
            {
                long deck = NativeUi.ExStyle(shell.Deck), pill = NativeUi.ExStyle(shell.Pill);
                const long need = NativeUi.WS_EX_NOACTIVATE | NativeUi.WS_EX_TOOLWINDOW;
                return (deck & need) == need && (pill & need) == need && (deck & NativeUi.WS_EX_APPWINDOW) == 0 && (pill & NativeUi.WS_EX_APPWINDOW) == 0
                    && !shell.Deck.ShowActivated && !shell.Pill.ShowActivated && shell.Deck.Topmost && shell.Pill.Topmost;
            });

            Step("the status popup goes where it was dragged and back to the corner on reset", () =>
            {
                RunPill pill = shell.Pill;
                System.Drawing.Rectangle work = System.Windows.Forms.Screen.FromHandle(new System.Windows.Interop.WindowInteropHelper(main).EnsureHandle()).WorkingArea;
                pill.ShowForMoving(main);
                Settle(pill);
                Rect r;
                if (!pill.IsShownForMoving || !NativeUi.TryGetRect(pill, out r)) throw new Exception("the popup did not show for moving");
                if (Math.Abs(r.Right - (work.Right - 18)) > 1 || Math.Abs(r.Bottom - (work.Bottom - 18)) > 1) throw new Exception("default place " + r + " in " + work);
                double w = r.Width, h = r.Height;
                // Dropped near the top-left: the top-left corner stays put.
                controller.Settings.PillLeft = work.Left + 40; controller.Settings.PillTop = work.Top + 30; controller.Settings.PillRight = work.Left + 40 + w + 60; controller.Settings.PillBottom = work.Top + 30 + h;
                pill.ShowForMoving(main);
                Settle(pill);
                NativeUi.TryGetRect(pill, out r);
                if (Math.Abs(r.Left - (work.Left + 40)) > 1 || Math.Abs(r.Top - (work.Top + 30)) > 1) throw new Exception("top-left place gave " + r);
                // Dropped near the bottom-right and narrower than now: the right and bottom edges stay put, so it grows to the left.
                controller.Settings.PillLeft = work.Right - 300; controller.Settings.PillTop = work.Bottom - 120; controller.Settings.PillRight = work.Right - 100; controller.Settings.PillBottom = work.Bottom - 80;
                pill.ShowForMoving(main);
                Settle(pill);
                NativeUi.TryGetRect(pill, out r);
                if (Math.Abs(r.Right - (work.Right - 100)) > 1 || Math.Abs(r.Bottom - (work.Bottom - 80)) > 1) throw new Exception("bottom-right place gave " + r);
                // Saved hanging past the screen edge (its middle still on this screen): it is kept fully on this screen.
                controller.Settings.PillLeft = work.Right - 200; controller.Settings.PillTop = work.Top + 10; controller.Settings.PillRight = work.Right + 100; controller.Settings.PillBottom = work.Top + 10 + h;
                pill.ShowForMoving(main);
                Settle(pill);
                NativeUi.TryGetRect(pill, out r);
                if (r.Right > work.Right + 1 || r.Left < work.Left - 1) throw new Exception("off-screen place gave " + r);
                controller.MarkDirty(SaveParts.Settings);
                if (!controller.Flush()) throw new Exception("saving failed: " + controller.LastStatus);
                AppSettings saved = new Storage(data).LoadSettings();
                if (saved.PillLeft != controller.Settings.PillLeft || saved.PillBottom != controller.Settings.PillBottom) throw new Exception("the popup place did not round-trip");
                pill.ResetPlace();
                Settle(pill);
                NativeUi.TryGetRect(pill, out r);
                if (!double.IsNaN(controller.Settings.PillLeft) || Math.Abs(r.Right - (work.Right - 18)) > 1 || Math.Abs(r.Bottom - (work.Bottom - 18)) > 1) throw new Exception("reset gave " + r);
                controller.Flush();
                if (!double.IsNaN(new Storage(data).LoadSettings().PillLeft)) throw new Exception("the reset place did not round-trip");
                pill.EndMoving();
                if (pill.IsVisible) throw new Exception("Done did not hide the popup");
            });
            Step("the main window returns quietly after a run (minimized stays minimized)", () =>
            {
                main.Hide();
                main.RestoreQuietly(true, false);
                Settle(main);
                if (!main.IsVisible || main.WindowState != WindowState.Minimized) throw new Exception("minimized restore gave " + main.WindowState + ", visible " + main.IsVisible);
                main.WindowState = WindowState.Normal;
                main.Hide();
                main.RestoreQuietly(false, true);
                Settle(main);
                if (!main.IsVisible || main.WindowState != WindowState.Normal) throw new Exception("normal restore gave " + main.WindowState + ", visible " + main.IsVisible);
            });
            Step("closing the window with the close button leaves without a nested close", () =>
            {
                main.Close();      // OnClosing cancels and queues the exit; it must not close the window from inside Closing
                Pump();
                if (!shell.Exiting) throw new Exception("the exit did not happen");
            });

            controller.Dispose();
        }

        /// <summary>The Global Keybinds tab reads synthetic files, never the PC's real apps, and asks Windows nothing.</summary>
        static void UseKeybindFixtures(KeybindsTab tab, AppController c)
        {
            var files = new Dictionary<string, string>
            {
                { "snipline", "{\"Schema\":1,\"HotkeyKey\":44,\"HotkeyModifiers\":0,\"WindowShortcutModifier\":2}" },
                { "notekeeper", "{\"Schema\":1,\"RecordKey\":119,\"CaptureKey\":120,\"MarkKey\":118,\"Modifiers\":0}" },
                { "jancy", string.Join("\n",
                    "id:user-copy-url-list; keybind:ctrl-alt-shift-u; context:tab window",
                    "id:copy-title-to-the-clipboard; keybind:ctrl-shift-c; context:tab window",
                    "id:reload-all-tabs; keybind:ctrl-alt-shift-p; context:tab window",
                    "id:new-tab; keybind:ctrl-t; context:tab window",
                    "id:close-current-tab; keybind:ctrl-w; context:tab window") },
                { @"pt\settings.json", "{\"enabled\":{\"AlwaysOnTop\":true,\"ColorPicker\":true,\"Peek\":true,\"FancyZones\":false}}" },
                { @"pt\AlwaysOnTop\settings.json", "{\"properties\":{\"hotkey\":{\"value\":{\"win\":true,\"ctrl\":true,\"alt\":false,\"shift\":false,\"code\":84}}}}" },
                { @"pt\ColorPicker\settings.json", "{\"properties\":{\"DefaultActivationShortcut\":{\"win\":true,\"shift\":true,\"ctrl\":false,\"alt\":false,\"code\":67},\"ActivationShortcut\":{\"win\":true,\"shift\":true,\"ctrl\":false,\"alt\":false,\"code\":67}}}" },
                { @"pt\Peek\settings.json", "{\"properties\":{\"ActivationShortcut\":{\"win\":false,\"ctrl\":true,\"alt\":false,\"shift\":false,\"code\":32}}}" },
                { @"obs\user.ini", "[General]\nHotkeyFocusType=NeverDisableHotkeys\n[Basic]\nProfileDir=Main\nSceneCollectionFile=Main.json\n" },
                { @"obs\basic\profiles\Main\basic.ini", "[Hotkeys]\nOBSBasic.StartRecording={\"bindings\":[{\"key\":\"OBS_KEY_F9\",\"control\":true}]}\n" },
            };
            var where = new KeybindLocations { Snipline = "snipline", NoteKeeper = "notekeeper", JancyDefault = "jancy", PowerToys = "pt", Obs = "obs" };
            if (c.KeybindApps.Count == 0)
                c.KeybindApps.Add(new KeybindAppDef
                {
                    Name = "Chat App", Program = "chatapp",
                    Shortcuts = { new KeybindDef { Keys = "Ctrl+Shift+M", Action = "Mute or unmute" }, new KeybindDef { Keys = "Ctrl+K", Action = "Quick switcher", OnlyWhenActive = true } },
                });
            tab.Load = () => KeybindCatalog.Read(where, c.Settings, c.Macros, c.KeybindApps, path => { string text; return files.TryGetValue(path, out text) ? text : null; });
            tab.IsRunning = app => app.App == KeybindReaders.MacroKitName || app.App == KeybindReaders.SniplineName || app.App == KeybindReaders.JancyName || app.App == OtherAppReaders.PowerToysName
                ? true : app.Processes.Length > 0 ? (bool?)false : null;
            tab.Probe = null;
        }

        static void RunLogicChecks(AppController c, AppShell shell, MainWindow main, string data)
        {
            Check("keybinds tab lists every app and says where keys work", () =>
            {
                main.SelectTab(4);
                Settle(main);
                KeybindCatalog catalog = main.Keybinds.Catalog;
                main.Keybinds.SetSearch("prtsc");
                Settle(main);
                bool prtsc = main.Keybinds.VerdictText.Contains("taken everywhere by Snipline");
                main.Keybinds.SetSearch("ctrl+shift+c");
                Settle(main);
                bool jancy = main.Keybinds.VerdictText.Contains("only works inside Jancy");
                main.Keybinds.SetSearch("ctrl+alt+shift+k");
                Settle(main);
                bool free = main.Keybinds.VerdictText.Contains("is free");
                main.Keybinds.SetSearch("");
                return catalog != null && catalog.Apps.Count >= 5 && prtsc && jancy && free
                    && catalog.Clashes().Any(x => x.A.App == KeybindReaders.MacroKitName && x.B.App == KeybindReaders.JancyName)
                    && catalog.Apps.Any(a => a.App == OtherAppReaders.PowerToysName && a.Keybinds.Count == 3)
                    && catalog.Apps.Any(a => a.App == OtherAppReaders.ObsName && a.Keybinds.Any(k => k.Keys == "Ctrl+F9"))
                    && catalog.Apps.Any(a => a.App == "Chat App" && a.CustomId != null);
            });

            Check("an app added by hand is saved, counts on the tab and can be deleted", () =>
            {
                main.SelectTab(4);
                Settle(main);
                var editor = new KeybindAppEditor(null, main.Keybinds.TakenNames(null));
                editor.SetName("Jancy");
                bool refusesTakenName = editor.Problems.Any(p => p.Contains("already on the list"));
                editor.SetName("Stream Tool");
                KeybindAppEditor.ShortcutRow bad = editor.AddRow("ctrl-banana", "Nonsense", false);
                bool refusesBadKeys = editor.Problems.Any(p => p.Contains("does not recognise")) && !editor.Save();
                bad.Keys.Text = "ctrl+alt+shift+k";
                bad.Action.Text = "Start the stream";
                editor.SetProgram(@"C:\Tools\streamtool.exe");
                editor.AddRow("f8", "Mark a moment", true);
                bool saved = editor.Save();
                main.Keybinds.Apply(null, editor, KeybindAppEditor.Outcome.Saved);
                Settle(main);
                KeybindAppDef added = c.KeybindApps.FirstOrDefault(a => a.Name == "Stream Tool");
                main.Keybinds.SetSearch("ctrl+alt+shift+k");
                Settle(main);
                bool taken = main.Keybinds.VerdictText.Contains("taken everywhere by Stream Tool");
                main.Keybinds.SetSearch("");
                bool listed = main.Keybinds.Catalog.Apps.Any(a => a.App == "Stream Tool" && a.Keybinds.Count == 2);
                bool flushed = c.Flush() && File.Exists(Path.Combine(data, "keybinds.json"));
                bool reloaded = new Storage(data).LoadKeybindApps().Any(a => a.Name == "Stream Tool" && a.Program == "streamtool" && a.Shortcuts.Count == 2
                    && a.Shortcuts[0].Keys == "Ctrl+Shift+Alt+K" && a.Shortcuts[1].Keys == "F8" && a.Shortcuts[1].OnlyWhenActive);
                main.Keybinds.Apply(added, null, KeybindAppEditor.Outcome.Deleted);
                Settle(main);
                bool gone = !main.Keybinds.Catalog.Apps.Any(a => a.App == "Stream Tool") && !c.KeybindApps.Contains(added);
                c.Flush();
                return refusesTakenName && refusesBadKeys && saved && added != null && taken && listed && flushed && reloaded && gone;
            });

            Check("no hook, hotkeys or input in a self test", () => !c.InputEnabled && !c.HookRunning && c.HotkeyErrors.Count == 0);
            Check("starting a macro is refused without input", () =>
            {
                bool started = c.StartMacro(c.Macros[0], RunOrigin.MainWindow);
                return !started && !c.IsBusy && c.LastStatus.Contains("Input is turned off");
            });
            Check("recording and playback are refused without input", () => !c.StartRecording(RunOrigin.MainWindow) && !c.StartPlayback(RunOrigin.MainWindow) && !c.IsBusy);
            Check("picking a window ends cleanly when cancelled", () =>
            {
                bool began = c.BeginPick(info => { });
                c.Stop();
                return began && !c.IsBusy;
            });

            Step("the palette adds to the Stream Deck code when that box was used last", () =>
            {
                main.SelectTab(2);
                main.Macros.SelectMacro(2);
                main.Macros.InsertIntoCode = true;
                if (main.Macros.InsertIntoCode) throw new Exception("the hidden Stream Deck code was the target");
                main.Macros.ToggleStreamDeck(true);
                if (!main.Macros.StreamDeckCodeVisible || !main.Macros.InsertIntoCode) throw new Exception("the Stream Deck code did not show");
            });
            Step("palette inserts at the cursor", () =>
            {
                main.SelectTab(2);
                main.Macros.SelectMacro(2);
                main.Macros.InsertIntoCode = true;
                main.Macros.SetScript("ab");
                main.Macros.CaretIndex = 1;
                main.Macros.Insert("{{ENTER}}");
                if (main.Macros.ScriptText != "a{{ENTER}}b") throw new Exception("got " + main.Macros.ScriptText);
                if (main.Macros.CaretIndex != 1 + "{{ENTER}}".Length) throw new Exception("caret at " + main.Macros.CaretIndex);
                main.Macros.Insert("{{PAUSE:100}}");
                if (main.Macros.ScriptText != "a{{ENTER}}{{PAUSE:100}}b") throw new Exception("second insert gave " + main.Macros.ScriptText);
                if (c.Macros[2].Script != "a{{ENTER}}{{PAUSE:100}}b") throw new Exception("the macro was not updated");
            });
            Step("blocks go around the selected script text", () =>
            {
                main.Macros.SelectMacro(2);
                string gap = c.Macros[2].NewLineIsEnter ? "" : "\n";
                main.Macros.SetScript("ab");
                main.Macros.SelectScript(0, 1);
                main.Macros.WrapInRepeat(3);
                string want = "{{LOOP:3}}" + gap + "a" + gap + "{{ENDLOOP}}b";
                if (main.Macros.ScriptText != want) throw new Exception("repeat gave " + main.Macros.ScriptText);
                main.Macros.SetScript("x");
                main.Macros.SelectScript(1, 0);
                main.Macros.AddTakeTurns(3);
                want = "x{{SWITCH}}" + gap + gap + "{{OR}}" + gap + gap + "{{OR}}" + gap + gap + "{{ENDSWITCH}}";
                if (main.Macros.ScriptText != want) throw new Exception("take turns gave " + main.Macros.ScriptText);
                if (main.Macros.CaretIndex != ("x{{SWITCH}}" + gap).Length) throw new Exception("caret at " + main.Macros.CaretIndex);
                main.Macros.RefreshPreview();
                if (main.Macros.ErrorCount != 0 || main.Macros.StepCount != 5) throw new Exception(main.Macros.StepCount + " steps, " + main.Macros.ErrorCount + " problems");
            });
            Step("command search shows only matching groups and keeps the saved layout", () =>
            {
                var saved = new List<string>(c.Settings.OpenCommandGroups);
                main.Macros.SearchCommands("volume");
                if (main.Macros.VisibleCommandGroups != 1 || string.Join(",", main.Macros.OpenCommandGroups) != "media") throw new Exception("volume showed " + string.Join(",", main.Macros.OpenCommandGroups));
                main.Macros.SearchCommands("loop");
                if (string.Join(",", main.Macros.OpenCommandGroups) != "multi") throw new Exception("loop showed " + string.Join(",", main.Macros.OpenCommandGroups));
                main.Macros.SearchCommands("zzzz");
                if (main.Macros.VisibleCommandGroups != 0) throw new Exception("nonsense matched " + main.Macros.VisibleCommandGroups);
                main.Macros.SearchCommands("");
                if (main.Macros.VisibleCommandGroups != 10) throw new Exception(main.Macros.VisibleCommandGroups + " groups after clearing the search");
                if (string.Join(",", main.Macros.OpenCommandGroups) != string.Join(",", saved) || !c.Settings.OpenCommandGroups.SequenceEqual(saved)) throw new Exception("the saved open groups changed");
            });
            Step("parse preview counts steps and problems", () =>
            {
                main.Macros.SetScript("{{CTRL}{C}}{{PAUSE:50}}");
                if (main.Macros.StepCount != 2 || main.Macros.ErrorCount != 0) throw new Exception("good script: " + main.Macros.StepCount + " steps, " + main.Macros.ErrorCount + " problems");
                main.Macros.SetScript("{{FOO}}");
                if (main.Macros.ErrorCount != 1) throw new Exception("bad script: " + main.Macros.ErrorCount + " problems");
                main.Macros.SetScript("{{CTRL}{C}}{{PAUSE:50}}{{ALT}{TAB}}{{PAUSE:100}}{{CTRL}{V}}{{PAUSE:50}}{{ALT}{TAB}}{{PAUSE:100}}{{DOWN}}{{PAUSE:50}}");
                if (main.Macros.StepCount != 10) throw new Exception("The example script has " + main.Macros.StepCount + " steps");
                main.Macros.SetScript("{{KEYPRESS:DOWN:5:100}}");
            });
            Step("Stream Deck code edits change the steps", () =>
            {
                main.Macros.SetScript("{{CTRL}{V}}{{PAUSE:100}}");
                if (main.Macros.PlainText != "Ctrl+V\r\nwait 100ms") throw new Exception("the steps are " + main.Macros.PlainText);
                main.Macros.ToggleStreamDeck(false);
                if (main.Macros.StreamDeckCodeVisible || main.Macros.InsertIntoCode) throw new Exception("the Stream Deck code is still showing");
            });
            Step("typed steps become the macro's Super Macro text", () =>
            {
                main.Macros.SelectMacro(2);
                main.Macros.SetPlain("ctrl+c\r\nwait 50ms\r\nalt+tab\r\ntype \"hi\"");
                if (c.Macros[2].Script != "{{CTRL}{C}}{{PAUSE:50}}{{ALT}{TAB}}hi" || main.Macros.ScriptText != c.Macros[2].Script) throw new Exception("script " + c.Macros[2].Script);
                if (c.Macros[2].Plain != main.Macros.PlainText) throw new Exception("the typed text was not kept");
                if (!main.Macros.PlainSummaryText.StartsWith("4 steps", StringComparison.Ordinal)) throw new Exception("summary " + main.Macros.PlainSummaryText);
                main.Macros.SetPlain("ctrl+c\r\nfoo bar");
                if (c.Macros[2].Script.IndexOf("{{?:", StringComparison.Ordinal) < 0) throw new Exception("an unknown line did not block the run: " + c.Macros[2].Script);
                if (main.Macros.PlainSummaryText.IndexOf("Line 2", StringComparison.Ordinal) < 0) throw new Exception("summary " + main.Macros.PlainSummaryText);
                main.Macros.RefreshPreview();
                if (main.Macros.ErrorCount == 0) throw new Exception("the parser saw no problem");
                main.Macros.SelectMacro(1);
                main.Macros.SelectMacro(2);
                if (main.Macros.PlainText != "ctrl+c\r\nfoo bar") throw new Exception("reselecting showed " + main.Macros.PlainText);
            });
            Step("the palette adds plain steps", () =>
            {
                main.Macros.InsertIntoCode = false;
                main.Macros.SetPlain("type \"a\"");
                main.Macros.TextEditor.CaretIndex = main.Macros.PlainText.Length;
                main.Macros.Insert("{{ENTER}}");
                if (main.Macros.PlainText != "type \"a\"\r\nEnter" || c.Macros[2].Script != "a{{ENTER}}") throw new Exception("got " + main.Macros.PlainText + " / " + c.Macros[2].Script);
                main.Macros.SetPlain("ctrl+c\r\nctrl+v");
                main.Macros.TextEditor.Select(0, main.Macros.PlainText.Length);
                main.Macros.WrapInRepeat(2);
                if (main.Macros.PlainText != "repeat 2 times\r\nctrl+c\r\nctrl+v\r\nend repeat") throw new Exception("repeat gave " + main.Macros.PlainText);
                if (c.Macros[2].Script != "{{LOOP:2}}{{CTRL}{C}}{{CTRL}{V}}{{ENDLOOP}}") throw new Exception("repeat script " + c.Macros[2].Script);
                main.Macros.SetPlain("");
                main.Macros.AddTakeTurns(2);
                if (main.Macros.PlainText != "take turns\r\n\r\nor\r\n\r\nend turns") throw new Exception("take turns gave " + main.Macros.PlainText);
            });
            Step("key tiles show clean steps and edit the same macro", () =>
            {
                c.Settings.MacroEditor = EditorKinds.Tiles;
                c.MarkDirty(SaveParts.Settings);
                if (main.Macros.CurrentEditor != EditorKinds.Tiles) throw new Exception("the editor is " + main.Macros.CurrentEditor);
                main.Macros.SetPlain("ctrl+c\r\nwait 50ms\r\nalt+tab");
                string labels = string.Join("|", main.Macros.TileEditor.TileLabels);
                if (labels != "Ctrl + C|50 ms|Alt + Tab") throw new Exception("tiles " + labels);
                main.Macros.TileEditor.SelectTile(2);
                main.Macros.TileEditor.MoveSelected(-1);
                if (main.Macros.PlainText != "ctrl+c\r\nalt+tab\r\nwait 50ms" || c.Macros[2].Script != "{{CTRL}{C}}{{ALT}{TAB}}{{PAUSE:50}}") throw new Exception("move gave " + c.Macros[2].Script);
                main.Macros.Insert("{{ENTER}}");
                if (main.Macros.PlainText != "ctrl+c\r\nalt+tab\r\nEnter\r\nwait 50ms") throw new Exception("insert gave " + main.Macros.PlainText);
                c.Settings.MacroEditor = EditorKinds.Text;
                c.MarkDirty(SaveParts.Settings);
                if (main.Macros.CurrentEditor != EditorKinds.Text || main.Macros.PlainText != "ctrl+c\r\nalt+tab\r\nEnter\r\nwait 50ms") throw new Exception("switching back showed " + main.Macros.PlainText);
            });
            Step("recording keys makes one step per shortcut", () =>
            {
                var w = new KeyRecorderWindow(null);
                Prepare(w, 560, 360);
                w.Show();
                Settle(w);
                w.Press(System.Windows.Input.Key.LeftCtrl, System.Windows.Input.Key.C);
                for (int i = 0; i < 3; i++) w.Press(System.Windows.Input.Key.Down);
                w.Press(System.Windows.Input.Key.LeftShift);
                w.Press(System.Windows.Input.Key.LeftAlt, System.Windows.Input.Key.S);
                w.Finish();
                w.Close();
                if (w.Lines != "Ctrl+C\r\nDown 3 times\r\nShift\r\nAlt+S") throw new Exception("recorded " + w.Lines.Replace("\r\n", " | "));
                if (!MacroKit.Macros.PlainMacro.ToScript(w.Lines).Ok) throw new Exception("the recorded steps are not understood");
            });
            Check("the first-run question defaults to typing", () => new EditorChoiceWindow(null).Choice == EditorKinds.Text);
            Step("settings and macros are written and read back", () =>
            {
                c.Settings.Playback.Speed = 2.5;
                c.Settings.RecordHotkey = "Ctrl+Alt+9";
                c.MarkDirty(SaveParts.Settings | SaveParts.Macros);
                if (!c.Flush()) throw new Exception("saving failed: " + c.LastStatus);
                var again = new Storage(data);
                AppSettings s = again.LoadSettings();
                if (s.Playback.Speed != 2.5 || s.RecordHotkey != "Ctrl+Alt+9") throw new Exception("settings did not round-trip");
                MacroDef m = again.LoadMacros().FirstOrDefault(x => x.Id == c.Macros[2].Id);
                if (m == null || m.Script != c.Macros[2].Script || m.Script.Length == 0 || m.Plain != c.Macros[2].Plain || m.Plain.Length == 0) throw new Exception("macro did not round-trip");
                c.Settings.RecordHotkey = "Ctrl+Shift+Alt+R";
                c.MarkDirty(SaveParts.Settings);
                c.Flush();
            });
            Step("hotkey box shows and reports a combination", () =>
            {
                var box = new HotkeyBox(true);
                box.SetText("ctrl+alt+1");
                if (box.Value.ToString() != "Ctrl+Alt+1") throw new Exception("shows " + box.Value);
                var custom = new HotkeyBox(false);
                custom.SetText("Ctrl+Shift+Esc");
                if (custom.Keys().Count != 3) throw new Exception("keys: " + custom.Keys().Count);
            });
            Check("number boxes keep to their range", () =>
            {
                var box = new NumberBox(5, 1, 100);
                box.Text = "500";
                if (box.Value != 100) return false;
                box.Text = "abc";
                return box.Value == 100;
            });
            Check("switching tabs shows exactly one page", () =>
            {
                main.SelectTab(1);
                Settle(main);
                int visible = new FrameworkElement[] { main.Recorder.Root, main.Phrases.Root, main.Macros.Root, main.Settings.Root }.Count(p => p.Visibility == Visibility.Visible);
                return visible == 1 && main.Phrases.Root.Visibility == Visibility.Visible;
            });
            Check("theme switches between light and dark", () =>
            {
                Theme.Apply("Dark");
                bool dark = Theme.IsDark;
                Theme.Apply("Light");
                return dark && !Theme.IsDark;
            });
            Check("picking or choosing a window keeps the program only and shows the title as a hint", () =>
            {
                var editor = new WindowMatchEditor(c);
                var match = new WindowMatch { ProcessName = "old.exe", TitleContains = "old title" };
                editor.Bind(match, null);
                editor.ApplyWindow(new WindowInfo { ProcessName = "EXCEL.EXE", Title = "Budget 2026 - Excel", Handle = new IntPtr(5) });
                return match.ProcessName == "EXCEL.EXE" && match.TitleContains == "" && editor.HintText.Contains("Budget 2026 - Excel") && editor.HintText.Contains("title filter");
            });
            Check("the administrator warning appears only for an elevated window when Macro Kit is not elevated", () =>
            {
                var elevated = new WindowInfo { IsElevated = true };
                var normal = new WindowInfo { IsElevated = false };
                return AppController.ElevationWarning(elevated, false) == AppController.ElevationMessage
                    && AppController.ElevationWarning(elevated, true) == null
                    && AppController.ElevationWarning(normal, false) == null
                    && AppController.ElevationWarning(null, false) == null
                    && AppController.ElevationMessage.StartsWith("This window runs as administrator");
            });
            Check("global hotkeys are released while a hotkey box captures, and come back when the last one lets go", () =>
            {
                c.SuspendHotkeys();
                c.SuspendHotkeys();
                bool two = c.HotkeysSuspended;
                c.ResumeHotkeys();
                bool one = c.HotkeysSuspended;
                c.ResumeHotkeys();
                c.ResumeHotkeys();   // an extra resume is harmless
                return two && one && !c.HotkeysSuspended;
            });
            Check("the repeat-a-key box takes one plain key", () => main.Macros.RepeatKeyIsSingleKey);
            Check("focusing the repeat count does not switch a macro to N times, typing a count does", () =>
            {
                main.SelectTab(2);
                main.Macros.SelectMacro(3);
                if (c.Macros[3].Repeat != RepeatMode.Once) return false;
                main.Macros.FocusRepeatCount();
                bool unchanged = c.Macros[3].Repeat == RepeatMode.Once;
                main.Macros.TypeRepeatCount("7");   // actually editing the count is what selects "times"
                bool typed = c.Macros[3].Repeat == RepeatMode.Times && c.Macros[3].RepeatTimes == 7;
                c.Macros[3].Repeat = RepeatMode.Once;
                c.Macros[3].RepeatTimes = 5;
                main.Macros.ReloadList();
                main.Macros.SelectMacro(0);
                return unchanged && typed;
            });
            Check("a session without input never uses the real data folder", () =>
            {
                string fresh = Program.FreshTestFolder();
                string real = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Macro Kit");
                bool apart = !fresh.StartsWith(real, StringComparison.OrdinalIgnoreCase) && fresh != Program.FreshTestFolder();
                return apart && Paths.IsOverridden && !Paths.DataRoot.StartsWith(real, StringComparison.OrdinalIgnoreCase);
            });
            Check("restart options are read from the environment and the start-hidden flag is one-shot", () =>
            {
                string noInput = Environment.GetEnvironmentVariable("MK_NO_INPUT"), hidden = Environment.GetEnvironmentVariable("MK_START_HIDDEN");
                try
                {
                    Environment.SetEnvironmentVariable("MK_NO_INPUT", "1");
                    Environment.SetEnvironmentVariable("MK_START_HIDDEN", "1");
                    Options o = Options.Parse(new[] { "--restart-after", "1", "2" });
                    return o.NoInput && o.StartInTray && o.FromRestart;
                }
                finally
                {
                    Environment.SetEnvironmentVariable("MK_NO_INPUT", noInput);
                    Environment.SetEnvironmentVariable("MK_START_HIDDEN", hidden);
                }
            });
            Check("changelog is embedded and matches the version", () =>
            {
                string text = ReleaseNotes.Read();
                return text.Contains("## " + Release.Version + " - ") && AppIdentity.Version == Release.Version;
            });
        }

        static void RenderPills(AppShell shell, AppController c, string folder, string suffix)
        {
            var now = DateTime.Now;
            var states = new List<KeyValuePair<string, ActivityInfo>>
            {
                new KeyValuePair<string, ActivityInfo>("macro", new ActivityInfo { Kind = ActivityKind.Macro, Name = "Copy, switch, paste, next row", Pass = 3, TotalPasses = 25, StepIndex = 5, StepCount = 10, Message = "Switching to Chrome" }),
                new KeyValuePair<string, ActivityInfo>("macro-continuous", new ActivityInfo { Kind = ActivityKind.Macro, Name = "Paste ten times", Pass = 12, TotalPasses = 0, StepIndex = 2, StepCount = 2 }),
                new KeyValuePair<string, ActivityInfo>("playing", new ActivityInfo { Kind = ActivityKind.Playing, Name = "Login steps", Speed = 2, Pass = 1, TotalPasses = 3, ElapsedMs = 2100, DurationMs = 4200 }),
                new KeyValuePair<string, ActivityInfo>("recording", new ActivityInfo { Kind = ActivityKind.Recording, Name = "Recording", Started = now.AddSeconds(-12) }),
                new KeyValuePair<string, ActivityInfo>("recording-admin", new ActivityInfo { Kind = ActivityKind.Recording, Name = "Recording", Started = now.AddSeconds(-4), Warning = AppController.ElevationMessage }),
                new KeyValuePair<string, ActivityInfo>("picking", new ActivityInfo { Kind = ActivityKind.Picking, Name = "Pick a window", Candidate = new WindowInfo { Title = "Orders - Chrome", ProcessName = "Chrome.exe", Handle = new IntPtr(1) } })
            };
            foreach (var state in states)
            {
                var pair = state;
                Step("render pill " + pair.Key + " (" + suffix + ")", () =>
                {
                    shell.Pill.Render(pair.Value, null, false);
                    RenderFloating(shell.Pill, Path.Combine(folder, "7-pill-" + pair.Key + "-" + suffix + ".png"));
                });
            }
            Step("render pill notice (" + suffix + ")", () =>
            {
                shell.Pill.Flash("Can't start", "“Broken example” has an error. Line 1: Unknown command “FOO”.", "Bad");
                RenderFloating(shell.Pill, Path.Combine(folder, "7-pill-notice-" + suffix + ".png"));
            });
            Step("render pill error (" + suffix + ")", () =>
            {
                shell.Pill.Render(new ActivityInfo(), "Can't find the window to run in (Chrome.exe, title contains “Orders”). Open it and try again.", true);
                RenderFloating(shell.Pill, Path.Combine(folder, "7-pill-error-" + suffix + ".png"));
            });
            Step("render pill shown for moving (" + suffix + ")", () =>
            {
                shell.Pill.ShowForMoving(shell.Main);
                RenderFloating(shell.Pill, Path.Combine(folder, "7-pill-moving-" + suffix + ".png"));
                shell.Pill.EndMoving();
            });
        }

        // ------------------------------------------------------------------ capturing

        static void Prepare(Window w, int width, int height)
        {
            w.WindowStyle = WindowStyle.None;
            w.ResizeMode = ResizeMode.NoResize;
            w.ShowInTaskbar = false;
            w.ShowActivated = false;
            w.WindowStartupLocation = WindowStartupLocation.Manual;
            w.Left = 0; w.Top = 0;
            w.MinWidth = 0; w.MinHeight = 0;
            w.Width = width; w.Height = height;
        }

        static void RenderFloating(Window w, string file)
        {
            if (!w.IsVisible) w.Show();
            Settle(w);
            Capture(w, file);
        }

        static void Pump()
        {
            Dispatcher d = Dispatcher.CurrentDispatcher;
            for (int i = 0; i < 3; i++)
            {
                d.Invoke(DispatcherPriority.Loaded, new Action(() => { }));
                d.Invoke(DispatcherPriority.ApplicationIdle, new Action(() => { }));
            }
        }

        static void Settle(Window w)
        {
            Pump();
            w.UpdateLayout();
            Pump();
            w.UpdateLayout();
        }

        static void Capture(Window w, string file)
        {
            var content = (FrameworkElement)VisualTreeHelper.GetChild(w, 0);
            int width = (int)Math.Ceiling(content.ActualWidth), height = (int)Math.Ceiling(content.ActualHeight);
            if (width <= 0 || height <= 0) throw new InvalidOperationException("The window has no size yet.");
            var visual = new DrawingVisual();
            using (DrawingContext dc = visual.RenderOpen())
            {
                var rect = new Rect(0, 0, width, height);
                dc.DrawRectangle(w.Background, null, rect);
                dc.DrawRectangle(new VisualBrush(content)
                {
                    Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top,
                    ViewboxUnits = BrushMappingMode.Absolute, Viewbox = rect, ViewportUnits = BrushMappingMode.Absolute, Viewport = rect
                }, null, rect);
            }
            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var stream = File.Create(file)) encoder.Save(stream);
            report.Add("shot " + Path.GetFileName(file) + " " + width + "x" + height);
        }
    }
}
