using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using MacroKit.Core;

namespace MacroKit.App
{
    /// <summary>A small text box for whole numbers. Invalid text never reaches <see cref="Value"/>; it is tidied when focus leaves.</summary>
    public sealed class NumberBox : TextBox
    {
        int value;
        bool setting;

        public int Minimum { get; set; }
        public int Maximum { get; set; }
        public event Action Changed = () => { };

        public NumberBox(int value, int minimum, int maximum, double width = 64)
        {
            SetResourceReference(StyleProperty, typeof(TextBox));   // implicit styles do not apply to derived types
            Minimum = minimum;
            Maximum = maximum;
            Width = width;
            TextAlignment = TextAlignment.Right;
            InputMethod.SetIsInputMethodEnabled(this, false);
            this.value = Clamp(value);
            Text = this.value.ToString(CultureInfo.InvariantCulture);
            TextChanged += (s, e) =>
            {
                if (setting) return;
                int parsed;
                if (int.TryParse(Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
                {
                    int clamped = Clamp(parsed);
                    if (clamped != this.value) { this.value = clamped; Changed(); }
                }
            };
            LostKeyboardFocus += (s, e) => ShowValue();
            PreviewTextInput += (s, e) => { e.Handled = !e.Text.All(ch => char.IsDigit(ch) || (ch == '-' && Minimum < 0)); };
        }

        int Clamp(int v) { return Math.Max(Minimum, Math.Min(Maximum, v)); }

        public int Value
        {
            get { return value; }
            set { this.value = Clamp(value); ShowValue(); }
        }

        void ShowValue()
        {
            setting = true;
            string text = value.ToString(CultureInfo.InvariantCulture);
            if (Text != text) Text = text;
            setting = false;
        }
    }

    /// <summary>
    /// A box that records a key combination when you click it and press keys. With <see cref="RequireGlobal"/> it only accepts
    /// combinations that can be system-wide hotkeys (Backspace or Delete clears, Esc leaves it alone).
    /// </summary>
    public sealed class HotkeyBox : TextBox
    {
        Hotkey value;
        bool hasValue;

        /// <summary>Set by the app shell: called when a global-hotkey box starts or stops capturing keys, so the registered hotkeys can be released meanwhile.</summary>
        public static Action CaptureStarted = () => { };
        public static Action CaptureEnded = () => { };

        bool capturing;

        public bool RequireGlobal { get; set; }
        /// <summary>Releases Macro Kit's own hotkeys while the box has the keyboard, so pressing one shows it instead of running it. On for global-hotkey boxes.</summary>
        public bool ReleasesHotkeys { get; set; }
        /// <summary>Accepts one key on its own (no Ctrl, Alt, Shift or Win), for example the key a macro should press several times.</summary>
        public bool SingleKeyOnly { get; set; }
        public string Placeholder { get; set; }
        public event Action Changed = () => { };

        public HotkeyBox(bool requireGlobal, double width = 190)
        {
            SetResourceReference(StyleProperty, typeof(TextBox));
            RequireGlobal = requireGlobal;
            ReleasesHotkeys = requireGlobal;
            Placeholder = requireGlobal ? "Not set" : "Click, then press keys";
            Width = width;
            IsReadOnly = true;
            IsReadOnlyCaretVisible = false;
            Cursor = Cursors.Arrow;
            InputMethod.SetIsInputMethodEnabled(this, false);
            ContextMenu = null;
            ToolTip = requireGlobal ? "Click, then press the keys you want. Backspace clears it." : "Click, then press the keys you want to add.";
            PreviewKeyDown += OnKey;
            GotKeyboardFocus += (s, e) => { StartCapture(); Show(null); };
            LostKeyboardFocus += (s, e) => { EndCapture(); Show(null); };
            Unloaded += (s, e) => EndCapture();
            Show(null);
        }

        // A system-wide hotkey is swallowed by Windows before any window sees it, so while a global-hotkey box has the keyboard
        // every hotkey is released; otherwise pressing a combination that is already assigned would run it instead of showing it here.
        void StartCapture()
        {
            if (!ReleasesHotkeys || capturing) return;
            capturing = true;
            CaptureStarted();
        }

        void EndCapture()
        {
            if (!capturing) return;
            capturing = false;
            CaptureEnded();
        }

        public Hotkey Value { get { return value; } }

        /// <summary>The captured combination as Super Macro key values (modifiers first), or an empty list.</summary>
        public List<ushort> Keys()
        {
            var keys = new List<ushort>();
            if (!hasValue) return keys;
            Action<string> add = name => { ushort vk; if (KeyNames.TryGetVk(name, out vk)) keys.Add(vk); };
            if ((value.Modifiers & HotkeyModifiers.Ctrl) != 0) add("CTRL");
            if ((value.Modifiers & HotkeyModifiers.Shift) != 0) add("SHIFT");
            if ((value.Modifiers & HotkeyModifiers.Alt) != 0) add("ALT");
            if ((value.Modifiers & HotkeyModifiers.Win) != 0) add("WIN");
            keys.Add(value.Vk);
            return keys;
        }

        /// <summary>Shows a combination (text such as "Ctrl+Alt+1"); no event is raised.</summary>
        public void SetText(string text)
        {
            Hotkey parsed;
            hasValue = Hotkey.TryParse(text, out parsed);
            value = hasValue ? parsed : default(Hotkey);
            Show(null);
        }

        public void ClearHotkey()
        {
            bool had = hasValue;
            hasValue = false;
            value = default(Hotkey);
            Show(null);
            if (had) Changed();
        }

        void Show(string transient)
        {
            if (transient != null) { Text = transient; Ui.Ref(this, ForegroundProperty, "Warn"); return; }
            if (IsKeyboardFocused && !hasValue) { Text = "Press the keys…"; Ui.Ref(this, ForegroundProperty, "Muted"); return; }
            if (hasValue) { Text = value.ToString(); Ui.Ref(this, ForegroundProperty, "Text"); }
            else { Text = Placeholder; Ui.Ref(this, ForegroundProperty, "Muted"); }
        }

        void OnKey(object sender, KeyEventArgs e)
        {
            Key key = e.Key == Key.System ? e.SystemKey : e.Key == Key.ImeProcessed ? e.ImeProcessedKey : e.Key;
            if (key == Key.None || key == Key.DeadCharProcessed) return;
            ModifierKeys mods = Keyboard.Modifiers;
            bool plain = (mods & ~ModifierKeys.Shift) == 0;
            if (key == Key.Tab && plain) return;                       // Tab still moves between fields
            e.Handled = true;
            if (SingleKeyOnly && mods != ModifierKeys.None)
            {
                Show("Just one key, no " + Describe(mods).Replace("+", "/"));
                return;
            }
            if (key == Key.LeftCtrl || key == Key.RightCtrl || key == Key.LeftShift || key == Key.RightShift || key == Key.LeftAlt || key == Key.RightAlt || key == Key.LWin || key == Key.RWin)
            {
                Show(Describe(mods) + "…");
                return;
            }
            if (RequireGlobal && mods == ModifierKeys.None)
            {
                if (key == Key.Back || key == Key.Delete) { ClearHotkey(); return; }
                if (key == Key.Escape) { Show(null); return; }
            }
            int vk = KeyInterop.VirtualKeyFromKey(key);
            if (vk <= 0 || vk > 0xFF) return;
            HotkeyModifiers modifiers = HotkeyModifiers.None;
            if ((mods & ModifierKeys.Control) != 0) modifiers |= HotkeyModifiers.Ctrl;
            if ((mods & ModifierKeys.Shift) != 0) modifiers |= HotkeyModifiers.Shift;
            if ((mods & ModifierKeys.Alt) != 0) modifiers |= HotkeyModifiers.Alt;
            if ((mods & ModifierKeys.Windows) != 0) modifiers |= HotkeyModifiers.Win;
            var candidate = new Hotkey(modifiers, (ushort)vk);
            if (RequireGlobal && !candidate.IsValidGlobal) { Show("Add Ctrl, Alt or Win"); return; }
            value = candidate;
            hasValue = true;
            Show(null);
            Changed();
        }

        static string Describe(ModifierKeys mods)
        {
            var parts = new List<string>();
            if ((mods & ModifierKeys.Control) != 0) parts.Add("Ctrl");
            if ((mods & ModifierKeys.Shift) != 0) parts.Add("Shift");
            if ((mods & ModifierKeys.Alt) != 0) parts.Add("Alt");
            if ((mods & ModifierKeys.Windows) != 0) parts.Add("Win");
            return string.Join("+", parts);
        }

        /// <summary>The box plus a small clear button, ready to place.</summary>
        public FrameworkElement WithClear()
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(this);
            var clear = Ui.GlyphButton(Ui.GlyphClose, "Clear this hotkey", ClearHotkey);
            clear.Margin = new Thickness(6, 0, 0, 0);
            row.Children.Add(clear);
            return row;
        }
    }

    /// <summary>
    /// Chooses a window for a macro: pick one of the open windows, use "Pick" (Macro Kit steps aside and takes the window you
    /// switch to), or type the program and part of the title. Edits go straight into the bound <see cref="WindowMatch"/>.
    /// </summary>
    public sealed class WindowMatchEditor
    {
        public readonly FrameworkElement Root;
        const string Placeholder = "Choose from the open windows…";
        readonly AppController controller;
        readonly ComboBox windows = new ComboBox();
        readonly TextBox program = new TextBox { Width = 150 };
        readonly TextBox title = new TextBox();
        readonly TextBlock summary;
        readonly TextBlock summaryDetail;
        readonly Border found;
        readonly TextBlock foundText;
        readonly TextBlock hint;
        readonly FrameworkElement detail;
        readonly Button detailToggle;
        WindowMatch match;
        Action changed = () => { };
        bool binding;

        public WindowMatchEditor(AppController controller)
        {
            this.controller = controller;
            windows.ToolTip = "The windows that are open now, like the Alt+Tab list.";
            AutomationProperties.SetName(windows, "Open windows");
            windows.DropDownOpened += (s, e) => FillWindows();
            windows.SelectionChanged += (s, e) =>
            {
                var chosen = windows.SelectedItem as WindowInfo;
                if (chosen == null || match == null || binding) return;
                UseWindow(chosen);
                // The card above now shows the choice, so the list goes back to its prompt.
                windows.Dispatcher.BeginInvoke(new Action(FillWindows), System.Windows.Threading.DispatcherPriority.Background);
            };
            var pick = Ui.IconButton(Ui.GlyphTarget, "Pick", Pick, "Ghost", "Macro Kit steps aside. Switch to the window you want and Macro Kit takes it.");
            program.ToolTip = "The program, for example Chrome.exe or EXCEL.EXE.";
            title.ToolTip = "Part of the window title. Leave empty to match any window of that program.";
            AutomationProperties.SetName(program, "Program");
            AutomationProperties.SetName(title, "Window title contains");
            program.TextChanged += (s, e) => Edited();
            title.TextChanged += (s, e) => Edited();
            hint = Ui.Text("", "Muted", false, 12, true);
            hint.Margin = new Thickness(0, 6, 0, 0);
            hint.Visibility = Visibility.Collapsed;

            // The chosen window, said plainly: which program, whether a title narrows it, and whether it is open now.
            summary = Ui.Text("", "Text", true, 13);
            summaryDetail = Ui.Text("", "Muted", false, 12);
            var names = Ui.Stack(1, summary, summaryDetail);
            names.VerticalAlignment = VerticalAlignment.Center;
            foundText = new TextBlock { FontSize = 12, FontWeight = FontWeights.SemiBold };
            found = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(8, 1, 8, 2), VerticalAlignment = VerticalAlignment.Center, Child = foundText, Margin = new Thickness(10, 0, 0, 0) };
            var icon = Ui.Glyph(Ui.GlyphTarget, 16, "Muted");
            icon.Margin = new Thickness(2, 0, 12, 0);
            var chosenRow = new DockPanel();
            DockPanel.SetDock(icon, Dock.Left);
            DockPanel.SetDock(found, Dock.Right);
            chosenRow.Children.Add(icon);
            chosenRow.Children.Add(found);
            chosenRow.Children.Add(names);
            var chosenCard = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 9, 12, 9), BorderThickness = new Thickness(1), Child = chosenRow };
            Ui.Ref(chosenCard, Border.BackgroundProperty, "Card2");
            Ui.Ref(chosenCard, Border.BorderBrushProperty, "Border");

            var top = new Grid { Margin = new Thickness(0, 8, 0, 0) };
            top.ColumnDefinitions.Add(new ColumnDefinition());
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            top.Children.Add(windows);
            Grid.SetColumn(pick, 1);
            pick.Margin = new Thickness(8, 0, 0, 0);
            top.Children.Add(pick);

            var detailGrid = new Grid { Margin = new Thickness(0, 6, 0, 0) };
            detail = detailGrid;
            detailGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            detailGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            detailGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            detailGrid.ColumnDefinitions.Add(new ColumnDefinition());
            var l1 = Ui.Text("Program", "Muted", false, 12); l1.Margin = new Thickness(0, 0, 8, 0);
            var l2 = Ui.Text("Title contains", "Muted", false, 12); l2.Margin = new Thickness(14, 0, 8, 0);
            Grid.SetColumn(program, 1); Grid.SetColumn(l2, 2); Grid.SetColumn(title, 3);
            detailGrid.Children.Add(l1); detailGrid.Children.Add(program); detailGrid.Children.Add(l2); detailGrid.Children.Add(title);
            detail.Visibility = Visibility.Collapsed;

            // Typing a program or title is for the few who need it, so it waits behind a link.
            detailToggle = Ui.Button("", () => ShowDetail(detail.Visibility != Visibility.Visible), "Link",
                "Type the program name or part of the window title yourself, for example to tell two windows of one program apart.");
            detailToggle.HorizontalAlignment = HorizontalAlignment.Left;
            detailToggle.Margin = new Thickness(0, 6, 0, 0);
            ShowDetail(false);

            Root = Ui.Stack(0, chosenCard, top, hint, detailToggle, detail);
            FillWindows();
            // "Open now" is a snapshot: look again when the tab is shown and when you come back from starting the program.
            Root.IsVisibleChanged += (s, e) => { if (Root.IsVisible) UpdateSummary(); };
            Root.Loaded += (s, e) =>
            {
                Window host = Window.GetWindow(Root);
                if (host == null || activatedHooked) return;
                activatedHooked = true;
                host.Activated += (s2, e2) => { if (Root.IsVisible) UpdateSummary(); };
            };
        }

        bool activatedHooked;

        public void Bind(WindowMatch match, Action onChanged)
        {
            this.match = match;
            changed = onChanged ?? (() => { });
            ShowHint(null);
            ShowMatch();
            // Open the details when the saved choice uses them, so nothing set is hidden.
            ShowDetail(match != null && (!string.IsNullOrWhiteSpace(match.TitleContains) || (string.IsNullOrWhiteSpace(match.ProcessName) && !match.IsEmpty)));
        }

        void ShowDetail(bool show)
        {
            detail.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            detailToggle.Content = show ? "Hide match details" : "Match details (program and title)";
        }

        /// <summary>Whether the program and title boxes are showing; used by the self test.</summary>
        public bool DetailShown { get { return detail.Visibility == Visibility.Visible; } }

        // Picking or choosing a window keeps only the program. A window title usually holds the document name, which changes
        // from day to day, so it is shown as a hint and the person adds a title filter only when it is really needed.
        void UseWindow(WindowInfo window)
        {
            if (match == null || window == null) return;
            match.ProcessName = window.ProcessName ?? "";
            match.TitleContains = match.ProcessName.Length == 0 ? window.Title ?? "" : "";
            ShowMatch();
            ShowHint(window);
            if (match.TitleContains.Length == 0) ShowDetail(false);
            changed();
        }

        void ShowHint(WindowInfo window)
        {
            bool show = window != null && !string.IsNullOrEmpty(window.Title) && !string.IsNullOrEmpty(window.ProcessName);
            hint.Text = show ? "You picked “" + window.Title + "”. Any " + window.ProcessName + " window will do, so it still works when the title changes. Keep several open? Add a title filter under Match details." : "";
            hint.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>The hint under the fields (empty when none is shown).</summary>
        public string HintText { get { return hint.Visibility == Visibility.Visible ? hint.Text : ""; } }

        /// <summary>What picking or choosing the window does; also used by the self test.</summary>
        public void ApplyWindow(WindowInfo window) { UseWindow(window); }

        void ShowMatch()
        {
            binding = true;
            program.Text = match == null ? "" : match.ProcessName ?? "";
            title.Text = match == null ? "" : match.TitleContains ?? "";
            binding = false;
            UpdateSummary();
        }

        void Edited()
        {
            if (binding || match == null) return;
            match.ProcessName = program.Text.Trim();
            match.TitleContains = title.Text.Trim();
            ShowHint(null);
            UpdateSummary();
            changed();
        }

        void UpdateSummary()
        {
            if (match == null || match.IsEmpty)
            {
                summary.Text = "No window chosen yet";
                Ui.Ref(summary, TextBlock.ForegroundProperty, "Muted");
                summaryDetail.Text = "Click Pick and switch to it, or choose it from the list.";
                found.Visibility = Visibility.Collapsed;
                return;
            }
            Ui.Ref(summary, TextBlock.ForegroundProperty, "Text");
            bool hasProcess = !string.IsNullOrWhiteSpace(match.ProcessName);
            bool hasTitle = !string.IsNullOrWhiteSpace(match.TitleContains);
            summary.Text = hasProcess ? match.ProcessName.Trim() : "Any program";
            summaryDetail.Text = hasTitle ? "Title contains “" + match.TitleContains.Trim() + "”" : "Any window of this program";
            bool open = false;
            try { open = controller.Windows.Find(match) != null; } catch (Exception) { }
            foundText.Text = open ? "Open now" : "Not open right now";
            Ui.Ref(foundText, TextBlock.ForegroundProperty, open ? "Good" : "Muted");
            Ui.Ref(found, Border.BackgroundProperty, open ? "GoodBg" : "Card");
            found.Visibility = Visibility.Visible;
        }

        /// <summary>The chosen-window card's two lines; used by the self test.</summary>
        public string SummaryText { get { return summary.Text + " · " + summaryDetail.Text; } }

        void FillWindows()
        {
            binding = true;
            windows.Items.Clear();
            windows.Items.Add(Placeholder);
            try { foreach (WindowInfo w in controller.Windows.ListSwitchableWindows()) windows.Items.Add(w); }
            catch (Exception ex) { Log.Warn("Listing windows failed", ex); }
            windows.SelectedIndex = 0;
            binding = false;
        }

        void Pick()
        {
            controller.BeginPick(info =>
            {
                if (match == null) return;
                UseWindow(info);
                controller.SetStatus("Using " + info + ".", false);
            });
        }
    }

    /// <summary>A themed one-line prompt.</summary>
    public static class Dialogs
    {
        /// <summary>Asks for one line of text. Returns null when cancelled.</summary>
        public static string Prompt(Window owner, string title, string message, string initial, string okText)
        {
            var w = new Window
            {
                Title = title, Width = 460, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false,
                WindowStartupLocation = owner != null && owner.IsVisible ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen
            };
            if (owner != null && owner.IsVisible) w.Owner = owner;
            Theme.Style(w);
            var box = new TextBox { Text = initial ?? "", Margin = new Thickness(0, 12, 0, 0) };
            string result = null;
            Button ok = null;
            Action accept = () => { if (box.Text.Trim().Length > 0) { result = box.Text.Trim(); w.DialogResult = true; } };
            ok = Ui.Button(okText, accept, "Primary");
            ok.MinWidth = 84;
            ok.IsDefault = true;
            Button cancel = Ui.Button("Cancel", () => w.DialogResult = false, "Ghost");
            cancel.MinWidth = 84;
            cancel.IsCancel = true;
            cancel.Margin = new Thickness(8, 0, 0, 0);
            box.TextChanged += (s, e) => ok.IsEnabled = box.Text.Trim().Length > 0;
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            var panel = new StackPanel { Margin = new Thickness(22, 20, 22, 18) };
            panel.Children.Add(Ui.Heading(title));
            var text = Ui.Para(message);
            text.Margin = new Thickness(0, 5, 0, 0);
            panel.Children.Add(text);
            panel.Children.Add(box);
            panel.Children.Add(buttons);
            w.Content = panel;
            w.Loaded += (s, e) => { box.Focus(); box.SelectAll(); };
            ok.IsEnabled = box.Text.Trim().Length > 0;
            return w.ShowDialog() == true ? result : null;
        }

        /// <summary>
        /// The prompt a phrase with {fill:Name} shows as it expands: one box per name, on top of everything. Returns the answers,
        /// or null when cancelled. <paramref name="shown"/> gets the window once it is up (to bring it to the front).
        /// </summary>
        public static Dictionary<string, string> Fill(string trigger, IList<string> names, Action<Window> shown)
        {
            return Ask("Fill in the phrase", "Fill in " + trigger, null, names, "Insert", "Cancel", "Enter inserts the phrase. Esc leaves what you typed as it was.", shown);
        }

        /// <summary>
        /// 1.12.0: the question a macro's {{INPUT}} asks while it runs, in the same popup as a phrase's {fill:Name}. Returns the
        /// answer (it may be empty), or null when the person stops the macro.
        /// </summary>
        public static string AskForMacro(string macroName, string name, string question, Action<Window> shown)
        {
            string heading = string.IsNullOrWhiteSpace(question) ? "The macro needs a value" : question.Trim();
            string from = string.IsNullOrWhiteSpace(macroName) ? null : "From the macro \u201c" + macroName.Trim() + "\u201d.";
            Dictionary<string, string> answers = Ask("Macro Kit: " + (string.IsNullOrWhiteSpace(macroName) ? "a macro asks" : macroName.Trim()), heading, from,
                new[] { name }, "Continue", "Stop the macro", "Enter carries on with the macro. Esc stops it.", shown);
            string answer;
            return answers != null && answers.TryGetValue(name, out answer) ? answer ?? "" : null;
        }

        // One box per name, on top of everything, for a phrase's fill-ins and a macro's question.
        static Dictionary<string, string> Ask(string title, string heading, string note, IList<string> names, string okText, string cancelText, string hintText, Action<Window> shown)
        {
            var w = new Window
            {
                Title = title, Width = 420, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = true, Topmost = true, WindowStartupLocation = WindowStartupLocation.CenterScreen
            };
            Theme.Style(w);
            var panel = new StackPanel { Margin = new Thickness(22, 20, 22, 18) };
            var head = Ui.Heading(heading);
            head.TextWrapping = TextWrapping.Wrap;
            panel.Children.Add(head);
            if (note != null)
            {
                var sub = Ui.Para(note, "Muted", 12);
                sub.Margin = new Thickness(0, 4, 0, 0);
                panel.Children.Add(sub);
            }
            var boxes = new List<TextBox>();
            foreach (string name in names)
            {
                var label = Ui.Text(name, "Muted", false, 12);
                label.Margin = new Thickness(0, 10, 0, 3);
                panel.Children.Add(label);
                var box = new TextBox();
                AutomationProperties.SetName(box, name);
                boxes.Add(box);
                panel.Children.Add(box);
            }
            Dictionary<string, string> result = null;
            Button ok = Ui.Button(okText, () =>
            {
                result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < names.Count; i++) result[names[i]] = boxes[i].Text;
                w.DialogResult = true;
            }, "Primary");
            ok.MinWidth = 84;
            ok.IsDefault = true;
            Button cancel = Ui.Button(cancelText, () => w.DialogResult = false, "Ghost");
            cancel.MinWidth = 84;
            cancel.IsCancel = true;
            cancel.Margin = new Thickness(8, 0, 0, 0);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            panel.Children.Add(buttons);
            var hint = Ui.Para(hintText, "Muted", 12);
            hint.Margin = new Thickness(0, 10, 0, 0);
            panel.Children.Add(hint);
            w.Content = panel;
            w.Loaded += (s, e) =>
            {
                if (shown != null) shown(w);
                w.Activate();
                if (boxes.Count > 0) boxes[0].Focus();
            };
            return w.ShowDialog() == true ? result : null;
        }
    }
}
