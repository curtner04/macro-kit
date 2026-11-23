using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using MouseButton = MacroKit.Core.MouseButton;
using System.Windows.Threading;
using MacroKit.Core;
using MacroKit.Macros;


namespace MacroKit.App
{
    /// <summary>The Macros tab, part 2: the command palette (Show all commands) that adds steps.</summary>
    public sealed partial class MacrosTab
    {
        // ------------------------------------------------------------------ palette

        /// <summary>One collapsible group of commands. Which groups are open is saved in Settings.OpenCommandGroups.</summary>
        sealed class CommandGroup
        {
            public string Id, Words;
            public FrameworkElement Root, Body;
            public TextBlock Chevron;
            public bool Open;
        }

        const string ChevronOpen = "\uE70D", ChevronClosed = "\uE76C";

        FrameworkElement BuildPalette()
        {
            functionKey.ToolTip = "Function keys";
            for (int i = 1; i <= 24; i++) functionKey.Items.Add("F" + i);
            functionKey.SelectedIndex = 4;
            foreach (string key in new[] { "CTRL", "SHIFT", "ALT", "WIN", "SPACE", "ENTER", "TAB", "ESC", "UP", "DOWN", "LEFT", "RIGHT" }) holdKey.Items.Add(key);
            for (char ch = 'A'; ch <= 'Z'; ch++) holdKey.Items.Add(ch.ToString());
            holdKey.SelectedIndex = 0;
            // Show the selection while a palette button has the focus, so it is clear what a block will wrap.
            script.IsInactiveSelectionHighlightEnabled = true;

            var list = new StackPanel();
            list.Children.Add(Group("keys", "Keys", "Shortcuts, F keys, hold and release", "copy paste cut select all alt tab enter esc arrows home end page function hotkey combination modifier ctrl shift win", BuildKeysGroup()));
            list.Children.Add(Group("multi", "Multi action", "Repeat steps, run another macro, take turns, pick at random", "loop repeat times again switch toggle alternate random choice macro chain nested", BuildMultiGroup()));
            list.Children.Add(Group("wait", "Wait and repeat a key", "Pause, press one key several times", "pause delay sleep wait ms seconds keypress repeat", BuildWaitGroup()));
            list.Children.Add(Group("text", "Type text and notes", "Text to type, a note to yourself", "type text string phrase comment note", BuildTextGroup()));
            list.Children.Add(Group("mouse", "Mouse", "Click, move, scroll", "mouse click left right middle double move position scroll wheel pointer cursor", BuildMouseGroup()));
            list.Children.Add(Group("apps", "Apps and websites", "Open a program, file or web page, close a program, lock the PC", "open run launch start program app exe file folder website url browser link close quit exit kill lock", BuildAppsGroup()));
            list.Children.Add(Group("windows", "Windows", "Switch to a window, wait for one to open or for a colour", "window focus activate switch bring front wait title colour color pixel appear screen until", BuildWindowsGroup()));
            list.Children.Add(Group("pictures", "Pictures", "Wait for a button or icon to show, or click it", "picture image icon button find see screen appear show click wait capture screenshot", BuildPicturesGroup(), Ui.Pill("PRO", "Good", "GoodBg", 10)));
            list.Children.Add(Group("media", "Media and sound", "Play, pause, skip, volume, play a sound file", "media play pause next previous track stop mute volume louder quieter sound audio wav mp3 music", BuildMediaGroup()));
            list.Children.Add(Group("vars", "Variables and clipboard", "Remember a value, ask for one, use the clipboard", "variable var value set output clipboard copy remember input ask prompt question", BuildVariablesGroup()));
            list.Children.Add(Group("conditions", "Conditions", "Do steps only if a value or window matches", "if else condition compare window when check", BuildConditionsGroup(), Ui.Pill("PRO", "Good", "GoodBg", 10)));
            noMatches = Ui.Text("No command matches. Try another word.", "Muted", false, 12);
            noMatches.Margin = new Thickness(12, 4, 0, 6);
            noMatches.Visibility = Visibility.Collapsed;
            list.Children.Add(noMatches);

            // The search box, with its hint drawn behind the text.
            commandSearch.Width = 190;
            AutomationProperties.SetName(commandSearch, "Search commands");
            commandSearch.ToolTip = "Type a word such as copy, loop, volume or window to find the command.";
            var searchHint = Ui.Text("Find a command", "Muted", false, 12);
            searchHint.IsHitTestVisible = false;
            searchHint.Margin = new Thickness(8, 0, 0, 0);
            var search = new Grid { VerticalAlignment = VerticalAlignment.Center };
            search.Children.Add(commandSearch);
            search.Children.Add(searchHint);
            commandSearch.TextChanged += (s, e) => { searchHint.Visibility = commandSearch.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; ApplySearch(); };
            var closeAll = Ui.Button("Close all", () => { commandSearch.Text = ""; foreach (CommandGroup g in groups) SetOpen(g, false, false); SaveOpenGroups(); }, "Link", "Fold every group away.");
            closeAll.Margin = new Thickness(10, 0, 0, 0);

            var title = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            var tools = Ui.Row(0, search, closeAll);
            DockPanel.SetDock(tools, Dock.Right);
            title.Children.Add(tools);
            title.Children.Add(Ui.Text("Add commands at the cursor", "Muted", false, 12));

            var frame = new Border { Padding = new Thickness(8, 8, 8, 6), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1) };
            Ui.Ref(frame, Border.BackgroundProperty, "Card2");
            Ui.Ref(frame, Border.BorderBrushProperty, "Border");
            title.Margin = new Thickness(4, 0, 0, 6);
            frame.Child = Ui.Stack(0, title, list);

            // Only the first saved open group opens (earlier versions could keep several open at once).
            var open = (c.Settings.OpenCommandGroups ?? new List<string>()).Take(1).ToList();
            if (c.Settings.OpenCommandGroups != null && c.Settings.OpenCommandGroups.Count > 1) c.Settings.OpenCommandGroups = open;
            foreach (CommandGroup g in groups) SetOpen(g, open.Contains(g.Id), false);
            RefreshProCommands();
            return frame;
        }

        FrameworkElement BuildKeysGroup()
        {
            // One labelled row per kind of key, each in its own colour, so a key is quick to find.
            var keys = new Grid();
            keys.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            keys.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            KeyGroup(keys, "Clipboard", "KeyClip",
                Chip("Copy", () => Keys("CTRL", "C")), Chip("Cut", () => Keys("CTRL", "X")), Chip("Paste", () => Keys("CTRL", "V")), Chip("Select all", () => Keys("CTRL", "A")));
            KeyGroup(keys, "Switch", "KeySwitch",
                Chip("Alt+Tab", () => Keys("ALT", "TAB")), Chip("Ctrl+Tab", () => Keys("CTRL", "TAB")));
            KeyGroup(keys, "Edit", "KeyEdit",
                Chip("Enter", () => Keys("ENTER")), Chip("Tab", () => Keys("TAB")), Chip("Esc", () => Keys("ESC")),
                Chip("Backspace", () => Keys("BACK")), Chip("Delete", () => Keys("DELETE")));
            KeyGroup(keys, "Arrows", "KeyArrow",
                Chip("↑", () => Keys("UP"), "Up arrow"), Chip("↓", () => Keys("DOWN"), "Down arrow"), Chip("←", () => Keys("LEFT"), "Left arrow"), Chip("→", () => Keys("RIGHT"), "Right arrow"));
            KeyGroup(keys, "Jump", "KeyJump",
                Chip("Home", () => Keys("HOME")), Chip("End", () => Keys("END")), Chip("PgUp", () => Keys("PGUP"), "Page Up"), Chip("PgDn", () => Keys("PGDN"), "Page Down"));
            var fLabel = Ui.Text("F keys", "Muted", true, 12);
            AddKeyRow(keys, fLabel, Ui.WrapRow(6, functionKey, AddButton(() => Keys((string)functionKey.SelectedItem), "Insert the function key.")));
            fLabel.VerticalAlignment = VerticalAlignment.Center;
            fLabel.Margin = new Thickness(0, 0, 12, 5);

            var customRow = Ui.WrapRow(6, customShortcut, AddButton(() => { var k = customShortcut.Keys(); return k.Count == 0 ? null : MacroScript.Shortcut(k); }, "Press these keys together."));
            var holdRow = Ui.WrapRow(6, holdKey, Ui.Button("Hold down", null, "Chip"), Ui.Button("Release", null, "Chip"));
            ((Button)holdRow.Children[1]).Click += (s, e) => InsertKey(true);
            ((Button)holdRow.Children[2]).Click += (s, e) => InsertKey(false);
            return Ui.Stack(8, keys,
                Ui.Stack(4, Ui.Caption("Your own shortcut"), customRow),
                Ui.Stack(4, Ui.Caption("Hold or release a key"), holdRow));
        }

        FrameworkElement BuildMultiGroup()
        {
            var loopRow = Ui.WrapRow(6, Ui.Text("Repeat", "Muted", false, 12), loopTimes, Ui.Text("times", "Muted", false, 12),
                ActionButton("Add", () => { InsertBlock(MacroScript.Loop(loopTimes.Value), 0, MacroScript.EndLoop); return ""; },
                    "Repeat the steps you selected in the script. With nothing selected, an empty repeat block goes in at the cursor for you to fill."));
            otherMacro.ToolTip = "The macro to run. It runs once, in the same window, then this macro carries on.";
            otherMacro.DropDownOpened += (s, e) => FillMacroNames();
            var macroRow = Ui.WrapRow(6, Ui.Text("Run the macro", "Muted", false, 12), otherMacro,
                AddButton(() => otherMacro.Text.Trim().Length == 0 ? null : MacroScript.Macro(otherMacro.Text), "Run another of your macros here, then carry on."));
            var choiceRow = Ui.WrapRow(6, choiceCount, Ui.Text("choices:", "Muted", false, 12),
                ActionButton("Take turns", () => { InsertBlock(MacroScript.Switch, choiceCount.Value - 1, MacroScript.EndSwitch); return ""; },
                    "Each time the macro runs it does the next choice: mute, then unmute; or scene 1, 2, 3 and round again. Choices are split by {{OR}}."),
                ActionButton("Pick at random", () => { InsertBlock(MacroScript.Random, choiceCount.Value - 1, MacroScript.EndRandom); return ""; },
                    "Each time, one of the choices runs, picked at random. Choices are split by {{OR}}."),
                Chip("Next choice", () => MacroScript.Or, "Starts the next choice inside a take-turns or random block."));
            var hint = Ui.Para("Select steps in the script first and the block goes around them. Blocks can sit inside each other.", "Muted", 12);
            return Ui.Stack(6,
                Ui.Stack(4, Ui.Caption("Repeat steps"), loopRow),
                Ui.Stack(4, Ui.Caption("Another macro"), macroRow),
                Ui.Stack(4, Ui.Caption("Take turns or pick one"), choiceRow),
                hint);
        }

        FrameworkElement BuildWaitGroup()
        {
            var pauseRow = Ui.WrapRow(6, pauseMs, Ui.Text("ms", "Muted", false, 12), AddButton(() => MacroScript.Pause(pauseMs.Value), "Wait this long before the next command."),
                Chip("50", () => MacroScript.Pause(50), "Wait 50 ms"), Chip("100", () => MacroScript.Pause(100), "Wait 100 ms"),
                Chip("250", () => MacroScript.Pause(250), "Wait 250 ms"), Chip("500", () => MacroScript.Pause(500), "Wait 500 ms"), Chip("1000", () => MacroScript.Pause(1000), "Wait 1 second"));
            var repeatRow = Ui.WrapRow(6, repeatKey, Ui.Text("×", "Muted"), repeatCount, Ui.Text("every", "Muted", false, 12), repeatEvery, Ui.Text("ms", "Muted", false, 12),
                AddButton(() => repeatKey.Value.IsEmpty ? null : MacroScript.KeyPress(repeatKey.Value.Vk, repeatCount.Value, repeatEvery.Value), "Press this key several times."));
            repeatKey.SingleKeyOnly = true;
            repeatKey.ToolTip = "Click, then press the one key to repeat. Use \"Your own shortcut\" under Keys for a key with Ctrl, Alt, Shift or Win.";
            repeatKey.Placeholder = "Press a key";
            repeatKey.SetText("");
            return Ui.Stack(6,
                Ui.Stack(4, Ui.Caption("Pause"), pauseRow),
                Ui.Stack(4, Ui.Caption("Repeat a key"), repeatRow));
        }

        FrameworkElement BuildTextGroup()
        {
            var typeRow = DockedAdd(typeText, AddButton(() => typeText.Text.Length == 0 ? null : MacroScript.Text(typeText.Text), "Type this text."));
            typeText.ToolTip = "Text to type, exactly as written.";
            var commentRow = DockedAdd(commentText, AddButton(() => commentText.Text.Trim().Length == 0 ? null : MacroScript.Comment(commentText.Text) + "\n", "A note to yourself; it is not run."));
            commentText.ToolTip = "A note to yourself on its own line.";
            return Ui.Stack(6,
                Ui.Stack(4, Ui.Caption("Type text"), typeRow),
                Ui.Stack(4, Ui.Caption("Note"), commentRow));
        }

        FrameworkElement BuildMouseGroup()
        {
            var clicks = Ui.Wrap(Chip("Left click", () => MacroScript.Click(MouseButton.Left)), Chip("Right click", () => MacroScript.Click(MouseButton.Right)),
                Chip("Middle click", () => MacroScript.Click(MouseButton.Middle)), Chip("Double click", () => MacroScript.DoubleClick(MouseButton.Left)));
            pickPosition = Ui.Button("Pick…", PickPosition, "Small", "Move the pointer to the spot you want. After 3 seconds its position is filled in.");
            var moveRow = Ui.WrapRow(6, Ui.Text("Move to", "Muted", false, 12), mouseX, Ui.Text(",", "Muted"), mouseY, pickPosition,
                AddButton(() => MacroScript.MouseTo(mouseX.Value, mouseY.Value), "Move the pointer to this spot on the screen."));
            var scrollRow = Ui.WrapRow(6, Ui.Text("Scroll", "Muted", false, 12),
                Ui.Button("Up", null, "Chip"), Ui.Button("Down", null, "Chip"), scrollNotches, Ui.Text("notches", "Muted", false, 12));
            ((Button)scrollRow.Children[1]).Click += (s, e) => Insert(MacroScript.ScrollUp(scrollNotches.Value));
            ((Button)scrollRow.Children[2]).Click += (s, e) => Insert(MacroScript.ScrollDown(scrollNotches.Value));
            return Ui.Stack(4, clicks, moveRow, scrollRow);
        }

        FrameworkElement BuildAppsGroup()
        {
            openTarget.ToolTip = "A program, file or folder, such as notepad, C:\\Reports\\week.xlsx or a shortcut. Put a path in quotes to add arguments after it.";
            var openRow = DockedAdd(openTarget, Ui.Row(6,
                Ui.Button("Browse…", () => Browse(openTarget, "Choose what to open", "Programs and shortcuts (*.exe;*.lnk;*.bat;*.cmd)|*.exe;*.lnk;*.bat;*.cmd|All files (*.*)|*.*"), "Small", "Pick a program or file."),
                AddButton(() =>
                {
                    if (openTarget.Text.Trim().Length == 0) return null;
                    string target, arguments;
                    MacroParser.SplitRun(openTarget.Text, out target, out arguments);
                    return MacroScript.Run(target, arguments);
                }, "Open it, as if you double-clicked it.")));
            websiteText.ToolTip = "A web address, such as example.com. It opens in your default browser.";
            var websiteRow = DockedAdd(websiteText, AddButton(() => websiteText.Text.Trim().Length == 0 ? null : MacroScript.Website(websiteText.Text), "Open this web page."));
            closeProgram.ToolTip = "The program's name, such as notepad or chrome. Pick one that is open now or type it.";
            closeProgram.DropDownOpened += (s, e) => FillCombo(closeProgram, c.Windows.ListSwitchableWindows()
                .Select(w => ProgramName(w.ProcessName)).Where(n => n.Length > 0 && !n.Equals("MacroKit", StringComparison.OrdinalIgnoreCase)));
            var closeRow = Ui.WrapRow(6, closeProgram,
                AddButton(() => closeProgram.Text.Trim().Length == 0 ? null : MacroScript.Close(ProgramName(closeProgram.Text)), "Ask the program's windows to close, as if you pressed their close button. Unsaved work still asks first."),
                Chip("Lock the PC", () => MacroScript.Lock, "Lock Windows, like Win+L"));
            return Ui.Stack(6,
                Ui.Stack(4, Ui.Caption("Open a program, file or folder"), openRow),
                Ui.Stack(4, Ui.Caption("Open a website"), websiteRow),
                Ui.Stack(4, Ui.Caption("Close a program"), closeRow));
        }

        FrameworkElement BuildWindowsGroup()
        {
            windowName.ToolTip = "Part of the window's title or its program name. Pick an open window or type it.";
            windowName.DropDownOpened += (s, e) => FillCombo(windowName, c.Windows.ListSwitchableWindows()
                .Select(w => (w.Title ?? "").Trim()).Where(t => t.Length > 0));
            var focusRow = Ui.WrapRow(6, windowName,
                ActionButton("Switch to it", () => windowName.Text.Trim().Length == 0 ? null : MacroScript.Focus(windowName.Text),
                    "Bring this window to the front. The rest of the macro runs in it."));
            var waitRow = Ui.WrapRow(6, Ui.Text("Wait up to", "Muted", false, 12), waitWindowSeconds, Ui.Text("seconds for it to open", "Muted", false, 12),
                ActionButton("Add", () => windowName.Text.Trim().Length == 0 ? null : MacroScript.WaitWindow(windowName.Text, waitWindowSeconds.Value * 1000),
                    "Wait until a window like this opens (after Open, for example), then carry on in it. The macro stops if it doesn't open in time."));

            // 1.12.0: wait until a spot in the window turns a colour (a button lights up, a page finishes loading).
            AutomationProperties.SetName(pixelX, "Colour spot, across from the window's left edge");
            AutomationProperties.SetName(pixelY, "Colour spot, down from the window's top edge");
            AutomationProperties.SetName(pixelColor, "Colour to wait for");
            AutomationProperties.SetName(waitPixelSeconds, "Seconds to wait for the colour");
            pixelX.ToolTip = "Across from the window's left edge, in pixels.";
            pixelY.ToolTip = "Down from the window's top edge, in pixels.";
            pixelColor.ToolTip = "The colour as #RRGGBB, such as #1E90FF. Pick fills it in. A few shades either way still counts.";
            Ui.Ref(pixelSwatch, Border.BorderBrushProperty, "Border");
            pixelColor.TextChanged += (s, e) => ShowSwatch();
            ShowSwatch();
            pickPixel = Ui.Button("Pick\u2026", PickPixel, "Small", "Point at the spot in the other window. After 3 seconds its place and colour are filled in.");
            var pixelRow = Ui.WrapRow(6, Ui.Text("At", "Muted", false, 12), pixelX, Ui.Text(",", "Muted"), pixelY, pixelSwatch, pixelColor, pickPixel,
                Ui.Text("up to", "Muted", false, 12), waitPixelSeconds, Ui.Text("s", "Muted", false, 12),
                ActionButton("Add", PixelToken, "Wait until this spot in the window in front turns this colour, then carry on. The macro stops if it doesn't in time. Only Macro Kit runs this; a Stream Deck doesn't."));
            return Ui.Stack(4, focusRow, waitRow, Ui.Stack(4, Ui.Caption("Wait until a spot turns a colour"), pixelRow));
        }

        string PixelToken()
        {
            int rgb;
            if (!MacroParser.TryColor(pixelColor.Text, out rgb)) return null;
            return MacroScript.WaitPixel(pixelX.Value, pixelY.Value, rgb, waitPixelSeconds.Value * 1000);
        }

        void ShowSwatch()
        {
            int rgb;
            if (MacroParser.TryColor(pixelColor.Text, out rgb))
                pixelSwatch.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));
            else Ui.Ref(pixelSwatch, Border.BackgroundProperty, "Card2");
        }

        /// <summary>Fills the colour row from a point on the screen (the Pick button, after its countdown; the self test calls it directly).</summary>
        internal bool FillPixelFrom(ScreenPoint p)
        {
            bool switchable;
            WindowInfo window = c.WindowProbe.WindowAt(p.X, p.Y, out switchable);
            // A menu or tooltip under the pointer: measure from the window in front instead, as the macro will when it runs.
            if (window != null && !switchable)
            {
                WindowInfo front = c.Windows.Describe(c.Windows.Foreground);
                if (front != null) { window = front; switchable = true; }
            }
            if (window == null || c.Windows.IsOwnWindow(window.Handle))
            {
                c.SetStatus("Point at the other program's window, not at Macro Kit, then press Pick again.", true);
                return false;
            }
            int rgb;
            if (!c.Screen.TryGetPixel(p.X, p.Y, out rgb))
            {
                c.SetStatus("Macro Kit couldn't read the colour there. Try another spot.", true);
                return false;
            }
            pixelX.Value = p.X - window.Left;
            pixelY.Value = p.Y - window.Top;
            pixelColor.Text = MacroParser.FormatColor(rgb);
            string title = (window.Title ?? "").Trim();
            if (switchable && title.Length > 0) windowName.Text = title;
            c.SetStatus(pixelX.Value + ", " + pixelY.Value + " in " + (title.Length > 0 ? "\u201c" + title + "\u201d" : "that window") + " is " + pixelColor.Text + ". Press Add to wait for it.", false);
            return true;
        }

        FrameworkElement BuildMediaGroup()
        {
            var mediaChips = Ui.Wrap(Chip("Play/Pause", () => Keys("MEDIA_PLAY_PAUSE")), Chip("Next", () => Keys("MEDIA_NEXT_TRACK"), "Next track"),
                Chip("Previous", () => Keys("MEDIA_PREV_TRACK"), "Previous track"), Chip("Stop", () => Keys("MEDIA_STOP"), "Stop playing"),
                Chip("Mute", () => Keys("VOLUME_MUTE"), "Mute or unmute"), Chip("Volume up", () => Keys("VOLUME_UP"), "The volume key: one step up"),
                Chip("Volume down", () => Keys("VOLUME_DOWN"), "The volume key: one step down"));
            var volumeRow = Ui.WrapRow(6, Ui.Text("Set the volume to", "Muted", false, 12), volumePercent, Ui.Text("%", "Muted", false, 12),
                AddButton(() => MacroScript.Volume(volumePercent.Value), "Set the speaker volume to exactly this."),
                Chip("+10%", () => MacroScript.VolumeBy(10), "Turn the volume up by 10%"), Chip("−10%", () => MacroScript.VolumeBy(-10), "Turn the volume down by 10%"));
            soundFile.ToolTip = "A .wav or .mp3 file to play. The macro carries on while it plays.";
            var soundRow = DockedAdd(soundFile, Ui.Row(6,
                Ui.Button("Browse…", () => Browse(soundFile, "Choose a sound", "Sounds (*.wav;*.mp3;*.wma)|*.wav;*.mp3;*.wma|All files (*.*)|*.*"), "Small", "Pick a sound file."),
                AddButton(() => soundFile.Text.Trim().Length == 0 ? null : MacroScript.PlaySound(soundFile.Text), "Play this sound.")));
            return Ui.Stack(6,
                Ui.Stack(4, Ui.Caption("Media keys"), mediaChips),
                Ui.Stack(4, Ui.Caption("Volume"), volumeRow),
                Ui.Stack(4, Ui.Caption("Play a sound"), soundRow));
        }

        FrameworkElement BuildVariablesGroup()
        {
            varName.Width = 90;
            varName.ToolTip = "The variable's name, such as row. Use it in the script as $row.";
            varValue.Width = 150;
            varValue.ToolTip = "The value. It can use other variables, such as $row.";
            var setRow = Ui.WrapRow(6, Ui.Text("Name", "Muted", false, 12), varName, Ui.Text("value", "Muted", false, 12), varValue,
                ActionButton("Set", () => VarToken(n => MacroScript.SetVar(n, varValue.Text)), "Remember this value under this name."),
                ActionButton("Type it", () => VarToken(MacroScript.OutputVar), "Type the variable's value."),
                ActionButton("From the clipboard", () => VarToken(MacroScript.VarFromClipboard), "Remember what is on the clipboard under this name."),
                ActionButton("Ask while running", () => VarToken(n => MacroScript.Input(n)), "While the macro runs, ask for a value in a small window and remember it under this name. To word the question, add it after a colon: {{INPUT:row:Which row?}}"),
                ActionButton("Value to the clipboard", () => MacroScript.SetClipboard(varValue.Text), "Put the value on the clipboard."));
            return setRow;
        }

        FrameworkElement BuildConditionsGroup()
        {
            ifLeft.Width = 90;
            ifLeft.ToolTip = "Usually a variable, such as $count.";
            ifRight.Width = 90;
            foreach (string op in new[] { "=", "!=", "<", ">", "<=", ">=", "~" }) ifOp.Items.Add(op);
            ifOp.SelectedItem = "=";
            ifOp.ToolTip = "~ means \"contains\".";
            var ifRow = Ui.WrapRow(6, Ui.Text("If", "Muted", false, 12), ifLeft, ifOp, ifRight,
                ActionButton("Add", () => { if (ifLeft.Text.Trim().Length == 0) return null; InsertBlock(MacroScript.If(ifLeft.Text.Trim(), (string)ifOp.SelectedItem, ifRight.Text.Trim()), 0, MacroScript.EndIf); return ""; },
                    "Do the selected steps only when this is true."));
            ifWindow.ToolTip = "Part of the title or program name of the window the macro is typing into.";
            var windowRow = DockedAdd(ifWindow, Ui.Row(6,
                ActionButton("Add", () => { if (ifWindow.Text.Trim().Length == 0) return null; InsertBlock(MacroScript.IfWindow(ifWindow.Text.Trim()), 0, MacroScript.EndIf); return ""; },
                    "Do the selected steps only when the macro's window matches."),
                Chip("Otherwise", () => MacroScript.Else, "{{ELSE}}: the steps after it run when the condition is false.")));
            proCommands = Ui.Stack(6,
                Ui.Stack(4, Ui.Caption("If a value matches"), ifRow),
                Ui.Stack(4, Ui.Caption("If the window is"), windowRow));
            proNote = Ui.Note("Conditions come with Macro Kit Pro; enter your license key in Settings. Multi action's blocks are free.", "Info");
            return Ui.Stack(0, proNote, proCommands);
        }

        void RefreshProCommands()
        {
            bool pro = Edition.IsPro;
            if (proCommands != null)
            {
                proCommands.IsEnabled = pro;
                proNote.Visibility = pro ? Visibility.Collapsed : Visibility.Visible;
            }
            if (pictureCommands != null)
            {
                pictureCommands.IsEnabled = pro;
                pictureNote.Visibility = pro ? Visibility.Collapsed : Visibility.Visible;
            }
        }

        // ---- 1.13.0, Pro: pictures ----------------------------------------------------------------------------------------

        FrameworkElement BuildPicturesGroup()
        {
            AutomationProperties.SetName(pictureName, "Picture name");
            AutomationProperties.SetName(pictureSize, "Picture size in pixels");
            AutomationProperties.SetName(waitPictureSeconds, "Seconds to wait for the picture");
            pictureName.ToolTip = "A name for the picture, such as Save button. Pick a saved one or type a new name before Capture.";
            pictureName.DropDownOpened += (s, e) => FillCombo(pictureName, c.Storage.ListImages());
            pictureName.LostKeyboardFocus += (s, e) => ShowPicturePreview();
            pictureName.SelectionChanged += (s, e) => pictureName.Dispatcher.BeginInvoke(new Action(ShowPicturePreview));
            pictureSize.ToolTip = "How big a square to capture around the pointer, in pixels. Keep it small: just the button or icon, not the space around it.";
            AutomationProperties.SetName(picturePreview, "The saved picture");
            capturePicture = Ui.Button("Capture…", CapturePicture, "Small", "Point at the button or icon in the other window. After 3 seconds a square around the pointer is saved under this name.");
            var captureRow = Ui.WrapRow(6, Ui.Text("Name", "Muted", false, 12), pictureName, pictureSize, Ui.Text("px", "Muted", false, 12), capturePicture,
                Ui.Button("Open folder", OpenPicturesFolder, "Small", "Show the saved pictures in File Explorer, to rename or delete them."));
            var useRow = Ui.WrapRow(6, picturePreview, Ui.Text("up to", "Muted", false, 12), waitPictureSeconds, Ui.Text("s", "Muted", false, 12),
                ActionButton("Wait for it", () => PictureToken(false), "Wait until this picture shows in the window in front, then carry on. Its middle is kept in $IMAGE_X and $IMAGE_Y. The macro stops if it doesn't show in time. Only Macro Kit runs this; a Stream Deck doesn't."),
                ActionButton("Click it", () => PictureToken(true), "Wait until this picture shows in the window in front, then click its middle. The macro stops if it doesn't show in time. Only Macro Kit runs this; a Stream Deck doesn't."));
            var hint = Ui.Para("Capture works best on something that looks the same every time, such as an icon or a button's label. Keep the window's size and zoom the same as when you captured it.", "Muted", 12);
            pictureCommands = Ui.Stack(6,
                Ui.Stack(4, Ui.Caption("Save a picture"), captureRow),
                Ui.Stack(4, Ui.Caption("Use it"), useRow),
                hint);
            pictureNote = Ui.Note("Picture steps come with Macro Kit Pro; enter your license key in Settings.", "Info");
            return Ui.Stack(0, pictureNote, pictureCommands);
        }

        string PictureToken(bool click)
        {
            string name = pictureName.Text.Trim();
            if (name.Length == 0) return null;
            if (!PixelImage.IsValidName(name)) { c.SetStatus(PixelImage.NameRule, true); return ""; }
            if (c.Storage.LoadImage(name) == null) { c.SetStatus("There is no saved picture called “" + name + "” yet. Press Capture first.", true); return ""; }
            int ms = waitPictureSeconds.Value * 1000;
            return click ? MacroScript.ClickImage(name, ms) : MacroScript.WaitImage(name, ms);
        }

        void ShowPicturePreview()
        {
            string name = pictureName.Text.Trim();
            PixelImage image = PixelImage.IsValidName(name) ? c.Storage.LoadImage(name) : null;
            picturePreview.Source = image == null ? null : image.ToBitmap();
            picturePreview.ToolTip = image == null ? null : name + " (" + image.Width + " × " + image.Height + " px)";
        }

        void CapturePicture()
        {
            if (countdown != null) return;
            string name = pictureName.Text.Trim();
            if (name.Length > 0 && !PixelImage.IsValidName(name)) { c.SetStatus(PixelImage.NameRule, true); return; }
            int left = 3;
            capturePicture.Content = left + "…";
            countdown = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            countdown.Tick += (s, e) =>
            {
                left--;
                if (left > 0) { capturePicture.Content = left + "…"; return; }
                countdown.Stop();
                countdown = null;
                capturePicture.Content = "Capture…";
                FillPictureFrom(c.Input.CursorPosition);
            };
            countdown.Start();
        }

        /// <summary>Saves a square around a screen point as the named picture (Capture, after its countdown; the self test calls it directly).</summary>
        internal bool FillPictureFrom(ScreenPoint p)
        {
            bool switchable;
            WindowInfo window = c.WindowProbe.WindowAt(p.X, p.Y, out switchable);
            if (window == null || c.Windows.IsOwnWindow(window.Handle))
            {
                c.SetStatus("Point at the other program's window, not at Macro Kit, then press Capture again.", true);
                return false;
            }
            int side = pictureSize.Value;
            PixelImage shot = c.Screen.Capture(p.X - side / 2, p.Y - side / 2, side, side);
            if (shot == null)
            {
                c.SetStatus("Macro Kit couldn't read the screen there. Try again.", true);
                return false;
            }
            string name = pictureName.Text.Trim();
            if (name.Length == 0) name = NextPictureName();
            bool replaced = c.Storage.LoadImage(name) != null;
            try { c.Storage.SaveImage(name, shot); }
            catch (Exception e) when (e is System.IO.IOException || e is UnauthorizedAccessException)
            {
                c.SetStatus("Couldn't save the picture: " + e.Message, true);
                return false;
            }
            pictureName.Text = name;
            ShowPicturePreview();
            string title = (window.Title ?? "").Trim();
            c.SetStatus((replaced ? "Replaced" : "Saved") + " the picture “" + name + "” from " + (title.Length > 0 ? "“" + title + "”" : "that window")
                + ". Press Wait for it or Click it to use it.", false);
            return true;
        }

        string NextPictureName()
        {
            var taken = new HashSet<string>(c.Storage.ListImages(), StringComparer.OrdinalIgnoreCase);
            for (int n = 1; ; n++) if (!taken.Contains("Picture " + n)) return "Picture " + n;
        }

        void OpenPicturesFolder()
        {
            string folder = System.IO.Path.Combine(c.Storage.DataRoot, "images");
            try
            {
                System.IO.Directory.CreateDirectory(folder);
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", "\"" + folder + "\"") { UseShellExecute = true });
            }
            catch (Exception e) { c.SetStatus("Couldn't open the pictures folder: " + e.Message, true); }
        }

        FrameworkElement Group(string id, string title, string hint, string words, FrameworkElement body, FrameworkElement badge = null)
        {
            var g = new CommandGroup { Id = id, Words = (title + " " + hint + " " + words).ToLowerInvariant(), Body = body };
            g.Chevron = Ui.Glyph(ChevronClosed, 10, "Muted");
            g.Chevron.Width = 16;
            var head = new Grid();
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            head.Children.Add(g.Chevron);
            var titleText = Ui.Text(title, "Text", true, 12.5);
            Grid.SetColumn(titleText, 1);
            head.Children.Add(titleText);
            if (badge != null)
            {
                badge.Margin = new Thickness(8, 0, 0, 0);
                Grid.SetColumn(badge, 2);
                head.Children.Add(badge);
            }
            var hintText = Ui.Text(hint, "Muted", false, 12);
            hintText.Margin = new Thickness(12, 0, 0, 0);
            Grid.SetColumn(hintText, 3);
            head.Children.Add(hintText);
            var header = Ui.Button(head, null, "MenuBtn", "Show or hide these commands.");
            header.Padding = new Thickness(6, 5, 8, 5);
            header.HorizontalAlignment = HorizontalAlignment.Stretch;
            System.Windows.Automation.AutomationProperties.SetName(header, title);
            header.Click += (s, e) =>
            {
                bool opening = !g.Open;
                // One group at a time, so the commands never grow into a page of their own. A search can still show several.
                if (opening && commandSearch.Text.Trim().Length == 0)
                    foreach (CommandGroup other in groups) if (!ReferenceEquals(other, g) && other.Open) SetOpen(other, false, false);
                SetOpen(g, opening, true);
                if (g.Open && (id == "conditions" || id == "pictures")) RefreshProCommands();
                if (g.Open && id == "pictures") ShowPicturePreview();
                if (g.Open) header.BringIntoView();
            };
            body.Margin = new Thickness(28, 4, 6, 10);
            g.Root = Ui.Stack(0, header, body);
            groups.Add(g);
            return g.Root;
        }

        void SetOpen(CommandGroup g, bool open, bool save)
        {
            g.Open = open;
            g.Body.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            g.Chevron.Text = open ? ChevronOpen : ChevronClosed;
            if (save) SaveOpenGroups();
        }

        void SaveOpenGroups()
        {
            // While a search is showing its matches, opening and closing is only for the moment.
            if (commandSearch.Text.Trim().Length > 0) return;
            var open = groups.Where(g => g.Open).Select(g => g.Id).ToList();
            var saved = c.Settings.OpenCommandGroups ?? new List<string>();
            if (open.SequenceEqual(saved)) return;
            c.Settings.OpenCommandGroups = open;
            c.MarkDirty(SaveParts.Settings);
        }

        /// <summary>Shows only the groups that match every word typed in the search box, opened; an empty box brings back the saved layout.</summary>
        void ApplySearch()
        {
            string[] words = commandSearch.Text.ToLowerInvariant().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            int shown = 0;
            var saved = c.Settings.OpenCommandGroups ?? new List<string>();
            foreach (CommandGroup g in groups)
            {
                bool match = words.All(w => g.Words.Contains(w));
                g.Root.Visibility = match ? Visibility.Visible : Visibility.Collapsed;
                if (match) shown++;
                SetOpen(g, words.Length > 0 ? match : saved.Contains(g.Id), false);
            }
            noMatches.Visibility = shown == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (words.Length > 0) RefreshProCommands();
        }

        /// <summary>
        /// Puts a block into the script around the selected text: start, the selection, an {{OR}} for each further choice, end.
        /// With nothing selected the cursor lands inside the empty block. Line breaks only go in when they don't press Enter.
        /// </summary>
        void InsertBlock(string start, int separators, string end)
        {
            if (current == null) return;
            if (!CodeIsTarget)
            {
                // The block in plain words: "repeat 3 times" ... "end repeat".
                string[] words = PlainMacro.ToPlain(start + string.Concat(Enumerable.Repeat(MacroScript.Or, separators)) + end, true)
                    .Split(new[] { "\r\n" }, StringSplitOptions.None);
                editor.WrapBlock(words[0], separators, words[words.Length - 1]);
                return;
            }
            string gap = current.NewLineIsEnter ? "" : "\n";
            string inside = script.SelectedText;
            string text = start + gap + inside;
            int caret = inside.Length == 0 ? text.Length : -1;
            for (int i = 0; i < separators; i++) text += gap + MacroScript.Or + gap;
            text += gap + end;
            if (caret < 0) caret = text.Length;
            int at = Math.Min(script.SelectionStart, script.Text.Length);
            script.SelectedText = text;
            script.CaretIndex = at + caret;
            script.SelectionLength = 0;
            script.Focus();
            script.ScrollToLine(script.GetLineIndexFromCharacterIndex(script.CaretIndex));
        }

        void FillMacroNames()
        {
            FillCombo(otherMacro, c.Macros.Where(m => m != current).Select(m => (m.Name ?? "").Trim()).Where(n => n.Length > 0));
        }

        /// <summary>Refills an editable combo box's list and keeps what is typed in it.</summary>
        static void FillCombo(ComboBox box, IEnumerable<string> items)
        {
            string typed = box.Text;
            box.Items.Clear();
            foreach (string item in items.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase)) box.Items.Add(item);
            box.Text = typed;
        }

        static string ProgramName(string processName)
        {
            string name = (processName ?? "").Trim();
            return name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name.Substring(0, name.Length - 4) : name;
        }

        string VarName()
        {
            string n = varName.Text.Trim().TrimStart('$');
            return n.Length == 0 || n.Any(ch => !char.IsLetterOrDigit(ch) && ch != '_') ? null : n;
        }

        /// <summary>The variable button's text, or null when the name box is empty; a name that cannot be used says why.</summary>
        string VarToken(Func<string, string> make)
        {
            string n = VarName();
            if (n != null) return make(n);
            if (varName.Text.Trim().Length == 0) return null;
            c.SetStatus("Variable names can use letters, digits and _ (for example row_1).", true);
            return "";   // nothing to insert
        }

        void Browse(TextBox box, string title, string filter)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Title = title, Filter = filter, DereferenceLinks = false };
            if (dialog.ShowDialog(owner) == true) box.Text = dialog.FileName;
        }

        /// <summary>Adds a row of key chips tinted with a key-group colour (tint = theme key; tint + "Bg" is the chip fill).</summary>
        static void KeyGroup(Grid grid, string label, string tint, params Button[] chips)
        {
            foreach (Button chip in chips)
            {
                Ui.Ref(chip, Control.BackgroundProperty, tint + "Bg");
                Ui.Ref(chip, Control.BorderBrushProperty, tint + "Bg");
            }
            var wrap = Ui.Wrap(chips);
            wrap.Margin = new Thickness(0, 0, 0, 2);
            AddKeyRow(grid, Ui.Text(label, tint, true, 12), wrap);
        }

        static void AddKeyRow(Grid grid, TextBlock label, FrameworkElement content)
        {
            int row = grid.RowDefinitions.Count;
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            label.VerticalAlignment = VerticalAlignment.Top;
            label.Margin = new Thickness(0, 4, 12, 0);
            Grid.SetRow(label, row);
            grid.Children.Add(label);
            Grid.SetRow(content, row);
            Grid.SetColumn(content, 1);
            grid.Children.Add(content);
        }

        Button Chip(string label, Func<string> token, string tooltip = null)
        {
            string text = token();
            var b = Ui.Button(label, () => Insert(token()), "Chip", (tooltip ?? label) + "  →  " + text.Replace("\n", ""));
            return b;
        }

        Button AddButton(Func<string> token, string tooltip) { return ActionButton("Add", token, tooltip); }

        /// <summary>A small button that inserts what token gives. Null means a box still needs filling in; "" means token inserted it itself.</summary>
        Button ActionButton(string label, Func<string> token, string tooltip)
        {
            var b = Ui.Button(label, () => { string t = token(); if (t == null) c.SetStatus("Fill in the box first, then press " + label + ".", true); else Insert(t); }, "Small", tooltip);
            return b;
        }

        static FrameworkElement DockedAdd(TextBox box, FrameworkElement add)
        {
            var dock = new DockPanel();
            DockPanel.SetDock(add, Dock.Right);
            add.Margin = new Thickness(6, 0, 0, 0);
            dock.Children.Add(add);
            dock.Children.Add(box);
            return dock;
        }

        static string Keys(params string[] names)
        {
            var vks = new List<ushort>();
            foreach (string n in names) { ushort vk; if (KeyNames.TryGetVk(n, out vk)) vks.Add(vk); }
            return MacroScript.Shortcut(vks);
        }

        void InsertKey(bool down)
        {
            ushort vk;
            if (KeyNames.TryGetVk((string)holdKey.SelectedItem, out vk)) Insert(down ? MacroScript.KeyDown(vk) : MacroScript.KeyUp(vk));
        }

        /// <summary>Puts text into the script at the cursor (replacing the selection) and keeps the cursor after it.</summary>
        public void Insert(string token)
        {
            if (current == null || string.IsNullOrEmpty(token)) return;
            if (!CodeIsTarget) { editor.InsertLines(PlainMacro.ToPlain(token, true)); return; }
            int at = Math.Min(script.SelectionStart, script.Text.Length);
            script.SelectedText = token;
            script.CaretIndex = at + token.Length;
            script.SelectionLength = 0;
            script.Focus();
            script.ScrollToLine(script.GetLineIndexFromCharacterIndex(script.CaretIndex));
        }

        void PickPixel()
        {
            if (countdown != null) return;
            int left = 3;
            pickPixel.Content = left + "\u2026";
            countdown = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            countdown.Tick += (s, e) =>
            {
                left--;
                if (left > 0) { pickPixel.Content = left + "\u2026"; return; }
                countdown.Stop();
                countdown = null;
                pickPixel.Content = "Pick\u2026";
                FillPixelFrom(c.Input.CursorPosition);
            };
            countdown.Start();
        }

        void PickPosition()
        {
            if (countdown != null) return;
            int left = 3;
            pickPosition.Content = left + "…";
            countdown = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            countdown.Tick += (s, e) =>
            {
                left--;
                if (left > 0) { pickPosition.Content = left + "…"; return; }
                countdown.Stop();
                countdown = null;
                ScreenPoint p = c.Input.CursorPosition;
                mouseX.Value = p.X;
                mouseY.Value = p.Y;
                pickPosition.Content = "Pick…";
                c.SetStatus("Pointer position " + p + " filled in.", false);
            };
            countdown.Start();
        }
    }
}
