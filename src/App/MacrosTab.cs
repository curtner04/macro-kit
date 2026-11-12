using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using MacroKit.Core;
using MacroKit.Macros;

namespace MacroKit.App
{
    /// <summary>The Macros tab: write or click together Super Macro scripts and run them in the window you choose.</summary>
    public sealed class MacrosTab
    {
        public readonly FrameworkElement Root;
        readonly AppController c;
        readonly Window owner;
        readonly ListBox list = new ListBox();
        readonly TextBlock listEmpty;
        readonly Button duplicateButton, deleteButton, runButton, stopButton;
        readonly FrameworkElement editorPanel;
        readonly TextBox name;
        readonly TextBox script;
        readonly ListBox steps = new ListBox();
        readonly TextBlock stepsHeader = Ui.Text("", "Muted", false, 12);
        readonly TextBlock stepsSummary = Ui.Text("", "Muted", false, 12);
        readonly TextBlock stepsEmpty;
        readonly RadioButton once, times, continuous, runPrevious, runSpecific, altWindows, altSpecific;
        readonly NumberBox repeatTimes, repeatDelay, keyDelay;
        readonly CheckBox newLines, stopIfFocusLeaves, showOnDeck;
        readonly HotkeyBox hotkey;
        readonly TextBlock hotkeyError = Ui.Text("", "Bad", false, 12);
        readonly WindowMatchEditor targetEditor, partnerEditor;
        readonly DispatcherTimer parseTimer;
        MacroDef current;
        ParseResult parsed;
        bool loading;
        bool stepsLoading;

        // palette inputs
        readonly NumberBox pauseMs = new NumberBox(100, 0, 3600000, 70);
        readonly TextBox typeText = new TextBox();
        readonly HotkeyBox customShortcut = new HotkeyBox(false, 170);
        readonly HotkeyBox repeatKey = new HotkeyBox(false, 140);
        readonly NumberBox repeatCount = new NumberBox(5, 1, 100000, 56);
        readonly NumberBox repeatEvery = new NumberBox(100, 0, 600000, 64);
        readonly NumberBox mouseX = new NumberBox(0, -100000, 100000, 66);
        readonly NumberBox mouseY = new NumberBox(0, -100000, 100000, 66);
        readonly NumberBox scrollNotches = new NumberBox(3, 1, 1000, 50);
        readonly ComboBox holdKey = new ComboBox { Width = 110 };
        readonly ComboBox functionKey = new ComboBox { Width = 76 };
        readonly TextBox commentText = new TextBox();
        Button pickPosition;
        DispatcherTimer countdown;

        public MacrosTab(AppController controller, Window owner)
        {
            c = controller;
            this.owner = owner;

            // ---- list
            listEmpty = Ui.Para("No macros yet. Press New macro to make one.");
            listEmpty.Margin = new Thickness(10, 8, 10, 0);
            list.SelectionChanged += (s, e) => { if (!loading) Bind((list.SelectedItem as ListBoxItem) == null ? null : (MacroDef)((ListBoxItem)list.SelectedItem).Tag); };
            var newButton = Ui.Button("New macro", NewMacro, "Small", "Add an empty macro.");
            duplicateButton = Ui.Button("Duplicate", Duplicate, "Small", "Copy the selected macro.");
            deleteButton = Ui.Button("Delete", Delete, "Small", "Delete the selected macro.");
            var listButtons = Ui.Wrap(newButton, duplicateButton, deleteButton);
            foreach (UIElement b in listButtons.Children) ((FrameworkElement)b).Margin = new Thickness(0, 0, 6, 6);
            var listGrid = new Grid();
            listGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            listGrid.RowDefinitions.Add(new RowDefinition());
            listGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var heading = Ui.Heading("Macros");
            heading.Margin = new Thickness(0, 0, 0, 8);
            listGrid.Children.Add(heading);
            var host = new Grid();
            host.Children.Add(list);
            host.Children.Add(listEmpty);
            Grid.SetRow(host, 1);
            listGrid.Children.Add(host);
            listButtons.Margin = new Thickness(0, 8, 0, -6);
            Grid.SetRow(listButtons, 2);
            listGrid.Children.Add(listButtons);
            Border listCard = Ui.Card(listGrid);
            listCard.Margin = new Thickness(0, 0, 10, 0);

            // ---- header: name, run, stop
            name = new TextBox { FontWeight = FontWeights.SemiBold, FontSize = 14, MinHeight = 32 };
            name.TextChanged += (s, e) => Edit(m => m.Name = name.Text, true);
            runButton = Ui.Button(Ui.Row(7, Ui.Shape("play", 11, "AccentFg"), new TextBlock { Text = "Run" }), () => { if (current != null) c.StartMacro(current, RunOrigin.MainWindow); }, "Primary", "Run this macro. Macro Kit steps aside and the macro runs in the window you chose below.");
            stopButton = Ui.Button(Ui.Row(7, Ui.Shape("stop", 11, "Text"), new TextBlock { Text = "Stop" }), () => c.Stop(), null, "Stop the running macro.");
            var header = new Grid();
            header.ColumnDefinitions.Add(new ColumnDefinition());
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.Children.Add(name);
            Grid.SetColumn(runButton, 1);
            runButton.Margin = new Thickness(10, 0, 0, 0);
            runButton.MinWidth = 84;
            header.Children.Add(runButton);
            Grid.SetColumn(stopButton, 2);
            stopButton.Margin = new Thickness(6, 0, 0, 0);
            stopButton.MinWidth = 76;
            header.Children.Add(stopButton);

            // ---- script, palette and steps
            script = new TextBox
            {
                FontFamily = Ui.MonoFont, FontSize = 12.5, AcceptsReturn = true, AcceptsTab = false, TextWrapping = TextWrapping.Wrap, Height = 188,
                VerticalContentAlignment = VerticalAlignment.Top, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(8, 6, 8, 6),
                SpellCheck = { IsEnabled = false },
                ToolTip = "Super Macro text, for example {{CTRL}{C}}{{PAUSE:50}}{{ALT}{TAB}}. Anything outside {{ }} is typed as is."
            };
            script.TextChanged += (s, e) => { Edit(m => m.Script = script.Text, false); parseTimer.Stop(); parseTimer.Start(); };
            parseTimer = new DispatcherTimer(DispatcherPriority.Background, Application.Current.Dispatcher) { Interval = TimeSpan.FromMilliseconds(150) };
            parseTimer.Tick += (s, e) => { parseTimer.Stop(); ShowSteps(); };

            steps.SelectionChanged += (s, e) => SelectStepInScript();
            stepsEmpty = Ui.Para("Nothing yet. Add commands above or type a script.");
            stepsEmpty.FontSize = 12;
            stepsEmpty.Margin = new Thickness(8, 4, 0, 0);

            var scriptCard = Ui.Card(Ui.Stack(10,
                Ui.Heading("Script"),
                BuildScriptAndSteps(),
                BuildPalette()));

            // ---- repeat
            once = new RadioButton { Content = "Once", GroupName = "macroRepeat" };
            times = new RadioButton { Content = "", GroupName = "macroRepeat", VerticalAlignment = VerticalAlignment.Center };
            continuous = new RadioButton { Content = "Continuously", GroupName = "macroRepeat" };
            repeatTimes = new NumberBox(5, 1, 100000, 60);
            repeatDelay = new NumberBox(0, 0, 3600000, 70);
            keyDelay = new NumberBox(10, 0, 60000, 60);
            once.Checked += (s, e) => Edit(m => m.Repeat = RepeatMode.Once, true);
            times.Checked += (s, e) => Edit(m => m.Repeat = RepeatMode.Times, true);
            continuous.Checked += (s, e) => Edit(m => m.Repeat = RepeatMode.Continuous, true);
            // Only actually typing a count switches to "N times"; merely clicking or tabbing into the box changes nothing.
            repeatTimes.Changed += () => { if (!loading && times.IsChecked != true) times.IsChecked = true; Edit(m => m.RepeatTimes = repeatTimes.Value, true); };
            repeatDelay.Changed += () => Edit(m => m.RepeatDelayMs = repeatDelay.Value, false);
            keyDelay.Changed += () => { Edit(m => m.KeyDelayMs = keyDelay.Value, false); ShowSteps(); };
            newLines = new CheckBox { Content = "New lines in the script press Enter" };
            newLines.ToolTip = "On is how Super Macro behaves. Off ignores line breaks, so you can lay a long script out over several lines.";
            newLines.Click += (s, e) => { Edit(m => m.NewLineIsEnter = newLines.IsChecked == true, false); ShowSteps(); };
            repeatTimes.Margin = new Thickness(2, 0, 7, 0);
            var timesRow = Ui.Row(0, times, repeatTimes, Ui.Text("times", null));
            var repeatRow = Ui.Row(18, once, timesRow, continuous);
            var delays = Ui.Row(7, Ui.Text("Wait", "Muted", false, 12), repeatDelay, Ui.Text("ms between repeats", "Muted", false, 12),
                Ui.Text("·", "Muted"), keyDelay, Ui.Text("ms after each key or click", "Muted", false, 12));
            ((FrameworkElement)delays.Children[3]).Margin = new Thickness(12, 0, 0, 0);
            ((FrameworkElement)delays.Children[4]).Margin = new Thickness(12, 0, 0, 0);
            var repeatCard = Ui.Card(Ui.Stack(8, Ui.Heading("Repeat"), repeatRow, delays, newLines));

            // ---- where it runs
            targetEditor = new WindowMatchEditor(c);
            partnerEditor = new WindowMatchEditor(c);
            runPrevious = new RadioButton { Content = "The window I used before Macro Kit", GroupName = "macroTarget" };
            runSpecific = new RadioButton { Content = "A specific window", GroupName = "macroTarget" };
            runPrevious.Checked += (s, e) => TargetKindChanged(TargetKind.PreviousWindow);
            runSpecific.Checked += (s, e) => TargetKindChanged(TargetKind.SpecificWindow);
            stopIfFocusLeaves = new CheckBox { Content = "Stop if another window takes focus", ToolTip = "Protects you from typing into the wrong window." };
            stopIfFocusLeaves.Click += (s, e) => Edit(m => m.StopIfFocusLeaves = stopIfFocusLeaves.IsChecked == true, false);
            altWindows = new RadioButton { Content = "Whatever Windows picks (Macro Kit is never picked)", GroupName = "macroAlt" };
            altSpecific = new RadioButton { Content = "A specific window", GroupName = "macroAlt" };
            altWindows.Checked += (s, e) => AltKindChanged(false);
            altSpecific.Checked += (s, e) => AltKindChanged(true);
            var whereCard = Ui.Card(Ui.Stack(8,
                Ui.Heading("Where it runs"),
                Ui.Caption("RUN IN"), runPrevious, runSpecific, Indent(targetEditor.Root), Indent(stopIfFocusLeaves),
                Ui.Caption("ALT+TAB SWITCHES TO"), altWindows, altSpecific, Indent(partnerEditor.Root),
                Ui.Para("With a specific window, Alt+Tab in the script jumps straight between the two windows, so a copy and paste macro always goes from one to the other.", "Muted", 12)));
            ((FrameworkElement)((StackPanel)whereCard.Child).Children[6]).Margin = new Thickness(0, 14, 0, 0);

            // ---- hotkey and deck
            hotkey = new HotkeyBox(true, 190);
            hotkey.Changed += () => Edit(m => m.Hotkey = hotkey.Value.IsEmpty ? "" : hotkey.Value.ToString(), true);
            if (!c.InputEnabled) hotkey.ToolTip = "Hotkeys are off in this session (input is turned off).";
            showOnDeck = new CheckBox { Content = "Show this macro as a button on the Deck" };
            showOnDeck.Click += (s, e) => Edit(m => m.ShowOnDeck = showOnDeck.IsChecked == true, true);
            var launchCard = Ui.Card(Ui.Stack(8,
                Ui.Heading("Start it with"),
                Ui.Row(10, Ui.Text("Hotkey", "Muted", false, 12), hotkey.WithClear()),
                hotkeyError,
                showOnDeck));
            hotkeyError.TextWrapping = TextWrapping.Wrap;

            var editorStack = new StackPanel();
            var headerCard = Ui.Card(header);
            editorStack.Children.Add(headerCard);
            editorStack.Children.Add(scriptCard);
            editorStack.Children.Add(repeatCard);
            editorStack.Children.Add(whereCard);
            editorStack.Children.Add(launchCard);
            var scroll = new ScrollViewer { Content = editorStack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            editorPanel = scroll;

            var grid = new Grid { Margin = new Thickness(0, 14, 0, 0) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(250), MinWidth = 220 });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.Children.Add(listCard);
            Grid.SetColumn(scroll, 1);
            grid.Children.Add(scroll);
            Root = grid;

            c.ActivityChanged += UpdateRunButtons;
            c.HotkeysChanged += ShowHotkeyError;
            Reload(null);
        }

        static FrameworkElement Indent(FrameworkElement e) { e.Margin = new Thickness(26, e.Margin.Top, 0, e.Margin.Bottom); return e; }

        // ------------------------------------------------------------------ palette

        FrameworkElement BuildPalette()
        {
            functionKey.ToolTip = "Function keys";
            for (int i = 1; i <= 24; i++) functionKey.Items.Add("F" + i);
            functionKey.SelectedIndex = 4;
            foreach (string key in new[] { "CTRL", "SHIFT", "ALT", "WIN", "SPACE", "ENTER", "TAB", "ESC", "UP", "DOWN", "LEFT", "RIGHT" }) holdKey.Items.Add(key);
            for (char ch = 'A'; ch <= 'Z'; ch++) holdKey.Items.Add(ch.ToString());
            holdKey.SelectedIndex = 0;

            var shortcuts = Ui.Wrap(
                Chip("Copy", () => Keys("CTRL", "C")), Chip("Paste", () => Keys("CTRL", "V")), Chip("Cut", () => Keys("CTRL", "X")),
                Chip("Select all", () => Keys("CTRL", "A")), Chip("Undo", () => Keys("CTRL", "Z")), Chip("Save", () => Keys("CTRL", "S")),
                Chip("Alt+Tab", () => Keys("ALT", "TAB")), Chip("Ctrl+Tab", () => Keys("CTRL", "TAB")),
                Chip("Enter", () => Keys("ENTER")), Chip("Tab", () => Keys("TAB")), Chip("Esc", () => Keys("ESC")),
                Chip("Backspace", () => Keys("BACK")), Chip("Delete", () => Keys("DELETE")),
                Chip("↑", () => Keys("UP"), "Up arrow"), Chip("↓", () => Keys("DOWN"), "Down arrow"), Chip("←", () => Keys("LEFT"), "Left arrow"), Chip("→", () => Keys("RIGHT"), "Right arrow"),
                Chip("Home", () => Keys("HOME")), Chip("End", () => Keys("END")), Chip("PgUp", () => Keys("PGUP"), "Page Up"), Chip("PgDn", () => Keys("PGDN"), "Page Down"));
            var fRow = Ui.WrapRow(6, functionKey, AddButton(() => Keys((string)functionKey.SelectedItem), "Insert the function key."));

            var pauseRow = Ui.WrapRow(6, pauseMs, Ui.Text("ms", "Muted", false, 12), AddButton(() => MacroScript.Pause(pauseMs.Value), "Wait this long before the next command."));
            var pauseChips = Ui.Wrap(Chip("50", () => MacroScript.Pause(50), "Wait 50 ms"), Chip("100", () => MacroScript.Pause(100), "Wait 100 ms"),
                Chip("250", () => MacroScript.Pause(250), "Wait 250 ms"), Chip("500", () => MacroScript.Pause(500), "Wait 500 ms"), Chip("1000", () => MacroScript.Pause(1000), "Wait 1 second"));

            var typeRow = DockedAdd(typeText, AddButton(() => typeText.Text.Length == 0 ? null : MacroScript.Text(typeText.Text), "Type this text."));
            typeText.ToolTip = "Text to type, exactly as written.";

            var customRow = Ui.WrapRow(6, customShortcut, AddButton(() => { var k = customShortcut.Keys(); return k.Count == 0 ? null : MacroScript.Shortcut(k); }, "Press these keys together."));
            var repeatRow = Ui.WrapRow(6, repeatKey, Ui.Text("×", "Muted"), repeatCount, Ui.Text("every", "Muted", false, 12), repeatEvery, Ui.Text("ms", "Muted", false, 12),
                AddButton(() => repeatKey.Value.IsEmpty ? null : MacroScript.KeyPress(repeatKey.Value.Vk, repeatCount.Value, repeatEvery.Value), "Press this key several times."));
            repeatKey.SingleKeyOnly = true;
            repeatKey.ToolTip = "Click, then press the one key to repeat. Use \"Your own shortcut\" for a key with Ctrl, Alt, Shift or Win.";
            repeatKey.Placeholder = "Press a key";
            repeatKey.SetText("");

            var clicks = Ui.Wrap(Chip("Left click", () => MacroScript.Click(MouseButton.Left)), Chip("Right click", () => MacroScript.Click(MouseButton.Right)),
                Chip("Middle click", () => MacroScript.Click(MouseButton.Middle)), Chip("Double click", () => MacroScript.DoubleClick(MouseButton.Left)));
            pickPosition = Ui.Button("Pick…", PickPosition, "Small", "Move the pointer to the spot you want. After 3 seconds its position is filled in.");
            var moveRow = Ui.WrapRow(6, Ui.Text("Move to", "Muted", false, 12), mouseX, Ui.Text(",", "Muted"), mouseY, pickPosition,
                AddButton(() => MacroScript.MouseTo(mouseX.Value, mouseY.Value), "Move the pointer to this spot on the screen."));
            var scrollRow = Ui.WrapRow(6, Ui.Text("Scroll", "Muted", false, 12),
                Ui.Button("Up", null, "Chip"), Ui.Button("Down", null, "Chip"), scrollNotches, Ui.Text("notches", "Muted", false, 12));
            ((Button)scrollRow.Children[1]).Click += (s, e) => Insert(MacroScript.ScrollUp(scrollNotches.Value));
            ((Button)scrollRow.Children[2]).Click += (s, e) => Insert(MacroScript.ScrollDown(scrollNotches.Value));

            var holdRow = Ui.WrapRow(6, holdKey, Ui.Button("Hold down", null, "Chip"), Ui.Button("Release", null, "Chip"));
            ((Button)holdRow.Children[1]).Click += (s, e) => InsertKey(true);
            ((Button)holdRow.Children[2]).Click += (s, e) => InsertKey(false);

            var commentRow = DockedAdd(commentText, AddButton(() => commentText.Text.Trim().Length == 0 ? null : MacroScript.Comment(commentText.Text) + "\n", "A note to yourself; it is not run."));
            commentText.ToolTip = "A note to yourself on its own line.";

            var left = Ui.Stack(10,
                Ui.Stack(0, Ui.Caption("KEYS"), shortcuts, fRow),
                Ui.Stack(4, Ui.Caption("PAUSE"), pauseRow, pauseChips),
                Ui.Stack(4, Ui.Caption("TYPE TEXT"), typeRow));
            var right = Ui.Stack(10,
                Ui.Stack(4, Ui.Caption("YOUR OWN SHORTCUT"), customRow),
                Ui.Stack(4, Ui.Caption("REPEAT A KEY"), repeatRow),
                Ui.Stack(4, Ui.Caption("MOUSE"), clicks, moveRow, scrollRow),
                Ui.Stack(4, Ui.Caption("HOLD OR RELEASE A KEY"), holdRow),
                Ui.Stack(4, Ui.Caption("NOTE"), commentRow));
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.Children.Add(left);
            Grid.SetColumn(right, 2);
            grid.Children.Add(right);
            var frame = new Border { Padding = new Thickness(12, 8, 12, 8), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1) };
            Ui.Ref(frame, Border.BackgroundProperty, "Card2");
            Ui.Ref(frame, Border.BorderBrushProperty, "Border");
            var toggle = Ui.Button("Hide", null, "Link", "Hide or show the command buttons.");
            var title = new DockPanel { Margin = new Thickness(0, 0, 0, 2) };
            DockPanel.SetDock(toggle, Dock.Right);
            title.Children.Add(toggle);
            title.Children.Add(Ui.Text("Add commands at the cursor in the script", "Muted", false, 12));
            grid.Margin = new Thickness(0, 8, 0, 0);
            toggle.Click += (s, e) =>
            {
                bool show = grid.Visibility != Visibility.Visible;
                grid.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
                toggle.Content = show ? "Hide" : "Show";
            };
            frame.Child = Ui.Stack(0, title, grid);
            return frame;
        }

        Button Chip(string label, Func<string> token, string tooltip = null)
        {
            string text = token();
            var b = Ui.Button(label, () => Insert(token()), "Chip", (tooltip ?? label) + "  →  " + text.Replace("\n", ""));
            return b;
        }

        Button AddButton(Func<string> token, string tooltip)
        {
            var b = Ui.Button("Add", () => { string t = token(); if (t == null) c.SetStatus("Fill in the box first, then press Add.", true); else Insert(t); }, "Small", tooltip);
            return b;
        }

        static FrameworkElement DockedAdd(TextBox box, Button add)
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
            int at = Math.Min(script.SelectionStart, script.Text.Length);
            script.SelectedText = token;
            script.CaretIndex = at + token.Length;
            script.SelectionLength = 0;
            script.Focus();
            script.ScrollToLine(script.GetLineIndexFromCharacterIndex(script.CaretIndex));
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

        // ------------------------------------------------------------------ steps preview

        /// <summary>The script box and the live list of steps next to it (problems show in red).</summary>
        FrameworkElement BuildScriptAndSteps()
        {
            var host = new Grid();
            host.Children.Add(steps);
            host.Children.Add(stepsEmpty);
            var box = new Border { Padding = new Thickness(4), CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1), Child = host, Height = 188 };
            Ui.Ref(box, Border.BackgroundProperty, "Input");
            Ui.Ref(box, Border.BorderBrushProperty, "Border");
            stepsHeader.Margin = new Thickness(0, 0, 0, 3);
            stepsSummary.Margin = new Thickness(0, 5, 0, 0);
            var scriptLabel = Ui.Text("Script text", "Muted", false, 12);
            scriptLabel.Margin = new Thickness(0, 0, 0, 3);

            var left = Ui.Stack(0, scriptLabel, script);
            var right = Ui.Stack(0, stepsHeader, box);
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.5, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 220 });
            grid.Children.Add(left);
            Grid.SetColumn(right, 2);
            grid.Children.Add(right);
            return Ui.Stack(0, grid, stepsSummary);
        }

        void ShowSteps()
        {
            stepsLoading = true;
            steps.Items.Clear();
            if (current == null) { stepsLoading = false; return; }
            parsed = MacroParser.Parse(current.Script, current.NewLineIsEnter);
            foreach (ParseError error in parsed.Errors)
            {
                var row = Ui.Text("Line " + error.Line + ", column " + error.Column + ":  " + error.Message, "Bad", false, 12.5, true);
                steps.Items.Add(new ListBoxItem { Content = row, Tag = error, Padding = new Thickness(8, 4, 8, 4) });
            }
            int number = 1;
            foreach (MacroStep step in parsed.Steps)
            {
                var row = new Grid();
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
                row.ColumnDefinitions.Add(new ColumnDefinition());
                row.Children.Add(Ui.Text(number.ToString(CultureInfo.InvariantCulture), "Muted", false, 12));
                var text = Ui.Text(step.Describe(), "Text", false, 12.5);
                Grid.SetColumn(text, 1);
                row.Children.Add(text);
                steps.Items.Add(new ListBoxItem { Content = row, Tag = step, Padding = new Thickness(8, 3, 8, 3) });
                number++;
            }
            stepsEmpty.Visibility = steps.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            stepsHeader.Text = "Steps: " + parsed.Steps.Count + (parsed.Errors.Count > 0 ? "   ·   " + parsed.Errors.Count + (parsed.Errors.Count == 1 ? " problem" : " problems") : "");
            Ui.Ref(stepsHeader, TextBlock.ForegroundProperty, parsed.Errors.Count > 0 ? "Bad" : "Muted");
            stepsSummary.Text = parsed.Steps.Count == 0 ? "" : "About " + AppController.FormatDuration(parsed.EstimatedMs(current)) + " per pass" + RepeatTotal();
            stepsLoading = false;
        }

        string RepeatTotal()
        {
            if (current.Repeat != RepeatMode.Times || current.RepeatTimes < 2) return "";
            long total = parsed.EstimatedMs(current) * current.RepeatTimes + (long)current.RepeatDelayMs * (current.RepeatTimes - 1);
            return ", about " + AppController.FormatDuration(total) + " in all";
        }

        void SelectStepInScript()
        {
            if (stepsLoading) return;
            var item = steps.SelectedItem as ListBoxItem;
            if (item == null) return;
            int index = -1, length = 0;
            var step = item.Tag as MacroStep;
            var error = item.Tag as ParseError;
            if (step != null) { index = step.Index; length = step.Length; }
            else if (error != null) { index = error.Index; length = error.Length; }
            if (index < 0 || index > script.Text.Length) return;
            script.Select(index, Math.Min(length, script.Text.Length - index));
            script.ScrollToLine(script.GetLineIndexFromCharacterIndex(index));
        }

        // ------------------------------------------------------------------ list and binding

        MacroDef Selected
        {
            get { var item = list.SelectedItem as ListBoxItem; return item == null ? null : item.Tag as MacroDef; }
        }

        void Reload(MacroDef select)
        {
            loading = true;
            list.Items.Clear();
            foreach (MacroDef m in c.Macros) list.Items.Add(MakeItem(m));
            ListBoxItem match = list.Items.Cast<ListBoxItem>().FirstOrDefault(i => ReferenceEquals(i.Tag, select)) ?? (list.Items.Count > 0 ? (ListBoxItem)list.Items[0] : null);
            list.SelectedItem = match;
            listEmpty.Visibility = list.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            loading = false;
            Bind(match == null ? null : (MacroDef)match.Tag);
        }

        ListBoxItem MakeItem(MacroDef m)
        {
            var panel = new StackPanel();
            panel.Children.Add(Ui.Text(m.Name.Length == 0 ? "(unnamed)" : m.Name, "Text", true));
            var sub = new List<string> { RepeatSummary(m) };
            if (!string.IsNullOrEmpty(m.Hotkey)) sub.Add(m.Hotkey);
            if (m.ShowOnDeck) sub.Add("Deck");
            var line = Ui.Text(string.Join("  ·  ", sub), "Muted", false, 11.5);
            line.Margin = new Thickness(0, 2, 0, 0);
            panel.Children.Add(line);
            return new ListBoxItem { Content = panel, Tag = m };
        }

        static string RepeatSummary(MacroDef m)
        {
            return m.Repeat == RepeatMode.Once ? "Once" : m.Repeat == RepeatMode.Times ? m.RepeatTimes + " times" : "Continuous";
        }

        void RefreshItem(MacroDef m)
        {
            var item = list.Items.Cast<ListBoxItem>().FirstOrDefault(i => ReferenceEquals(i.Tag, m));
            if (item == null) return;
            int index = list.Items.IndexOf(item);
            bool wasSelected = ReferenceEquals(list.SelectedItem, item);
            loading = true;
            list.Items[index] = MakeItem(m);
            if (wasSelected) list.SelectedIndex = index;
            loading = false;
        }

        void Bind(MacroDef m)
        {
            current = m;
            editorPanel.IsEnabled = m != null;
            duplicateButton.IsEnabled = m != null;
            deleteButton.IsEnabled = m != null;
            loading = true;
            if (m != null)
            {
                name.Text = m.Name;
                script.Text = m.Script;
                script.CaretIndex = script.Text.Length;
                once.IsChecked = m.Repeat == RepeatMode.Once;
                times.IsChecked = m.Repeat == RepeatMode.Times;
                continuous.IsChecked = m.Repeat == RepeatMode.Continuous;
                repeatTimes.Value = m.RepeatTimes;
                repeatDelay.Value = m.RepeatDelayMs;
                keyDelay.Value = m.KeyDelayMs;
                newLines.IsChecked = m.NewLineIsEnter;
                runPrevious.IsChecked = m.Target == TargetKind.PreviousWindow;
                runSpecific.IsChecked = m.Target == TargetKind.SpecificWindow;
                stopIfFocusLeaves.IsChecked = m.StopIfFocusLeaves;
                targetEditor.Bind(m.TargetWindow, () => Edit(x => { }, false));
                partnerEditor.Bind(m.AltTabPartner, () => Edit(x => { }, false));
                altSpecific.IsChecked = !m.AltTabPartner.IsEmpty;
                altWindows.IsChecked = m.AltTabPartner.IsEmpty;
                hotkey.SetText(m.Hotkey);
                showOnDeck.IsChecked = m.ShowOnDeck;
                UpdateEditorEnabled();
            }
            else
            {
                name.Text = "";
                script.Text = "";
            }
            loading = false;
            ShowSteps();
            ShowHotkeyError();
            UpdateRunButtons();
        }

        void UpdateEditorEnabled()
        {
            bool specific = current != null && current.Target == TargetKind.SpecificWindow;
            targetEditor.Root.IsEnabled = specific;
            stopIfFocusLeaves.IsEnabled = specific;
            partnerEditor.Root.IsEnabled = altSpecific.IsChecked == true;
        }

        void Edit(Action<MacroDef> change, bool refreshItem)
        {
            if (loading || current == null) return;
            change(current);
            c.MarkDirty(SaveParts.Macros);
            if (refreshItem) RefreshItem(current);
        }

        void TargetKindChanged(TargetKind kind)
        {
            Edit(m => m.Target = kind, false);
            UpdateEditorEnabled();
        }

        void AltKindChanged(bool specific)
        {
            if (loading || current == null) { UpdateEditorEnabled(); return; }
            if (!specific)
            {
                current.AltTabPartner.ProcessName = "";
                current.AltTabPartner.TitleContains = "";
                partnerEditor.Bind(current.AltTabPartner, () => Edit(x => { }, false));
                c.MarkDirty(SaveParts.Macros);
            }
            UpdateEditorEnabled();
        }

        void NewMacro()
        {
            var m = new MacroDef();
            c.Macros.Add(m);
            c.MarkDirty(SaveParts.Macros);
            Reload(m);
            name.Focus();
            name.SelectAll();
        }

        void Duplicate()
        {
            MacroDef m = Selected;
            if (m == null) return;
            MacroDef copy = Json.Deserialize<MacroDef>(Json.Serialize(m));
            copy.Id = Guid.NewGuid().ToString("N");
            copy.Name = m.Name + " copy";
            copy.Hotkey = "";
            c.Macros.Insert(c.Macros.IndexOf(m) + 1, copy);
            c.MarkDirty(SaveParts.Macros);
            Reload(copy);
        }

        void Delete()
        {
            MacroDef m = Selected;
            if (m == null) return;
            if (!Ui.Confirm(owner, "Delete “" + m.Name + "”? This can't be undone.")) return;
            int index = c.Macros.IndexOf(m);
            c.Macros.Remove(m);
            c.MarkDirty(SaveParts.Macros);
            Reload(c.Macros.Count == 0 ? null : c.Macros[Math.Min(index, c.Macros.Count - 1)]);
        }

        void UpdateRunButtons()
        {
            bool idle = !c.IsBusy;
            runButton.IsEnabled = idle && current != null;
            bool running = c.Activity.Kind == ActivityKind.Macro;
            stopButton.IsEnabled = running;
        }

        void ShowHotkeyError()
        {
            string error;
            if (current != null && c.HotkeyErrors.TryGetValue(current.Id, out error)) hotkeyError.Text = error;
            else hotkeyError.Text = "";
            hotkeyError.Visibility = hotkeyError.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        }

        // ------------------------------------------------------------------ self test hooks

        public string ScriptText { get { return script.Text; } }
        public int CaretIndex { get { return script.CaretIndex; } set { script.CaretIndex = value; } }
        public int StepCount { get { return parsed == null ? 0 : parsed.Steps.Count; } }
        public int ErrorCount { get { return parsed == null ? 0 : parsed.Errors.Count; } }
        public bool RepeatKeyIsSingleKey { get { return repeatKey.SingleKeyOnly; } }
        /// <summary>Self test: gives the repeat count box the keyboard focus (which must not change the repeat mode).</summary>
        /// <summary>Self test: rebuilds the list from the current macros.</summary>
        public void ReloadList() { Reload(null); }
        public void FocusRepeatCount() { repeatTimes.Focus(); }
        /// <summary>Self test: types a repeat count.</summary>
        public void TypeRepeatCount(string text) { repeatTimes.Text = text; }
        public void SelectMacro(int index) { if (index >= 0 && index < list.Items.Count) list.SelectedIndex = index; }
        public void SetScript(string text) { script.Text = text; ShowSteps(); }
        /// <summary>Applies pending parse work immediately (the preview normally follows typing after 150 ms).</summary>
        public void RefreshPreview() { parseTimer.Stop(); ShowSteps(); }
    }
}
