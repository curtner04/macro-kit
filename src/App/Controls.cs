using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
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
        /// <summary>Accepts one key on its own (no Ctrl, Alt, Shift or Win), for example the key a macro should press several times.</summary>
        public bool SingleKeyOnly { get; set; }
        public string Placeholder { get; set; }
        public event Action Changed = () => { };

        public HotkeyBox(bool requireGlobal, double width = 190)
        {
            SetResourceReference(StyleProperty, typeof(TextBox));
            RequireGlobal = requireGlobal;
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
            if (!RequireGlobal || capturing) return;
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
            if (RequireGlobal && !candidate.IsValidGlobal) { Show(candidate + " needs Ctrl, Alt or Win"); return; }
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
        const string Placeholder = "Choose an open window…";
        readonly AppController controller;
        readonly ComboBox windows = new ComboBox();
        readonly TextBox program = new TextBox { Width = 150 };
        readonly TextBox title = new TextBox();
        readonly TextBlock summary;
        readonly TextBlock found;
        readonly TextBlock hint;
        WindowMatch match;
        Action changed = () => { };
        bool binding;

        public WindowMatchEditor(AppController controller)
        {
            this.controller = controller;
            windows.ToolTip = "The windows that are open now, like the Alt+Tab list.";
            windows.DropDownOpened += (s, e) => FillWindows();
            windows.SelectionChanged += (s, e) =>
            {
                var chosen = windows.SelectedItem as WindowInfo;
                if (chosen == null || match == null || binding) return;
                UseWindow(chosen);
            };
            var pick = Ui.IconButton(Ui.GlyphTarget, "Pick", Pick, "Ghost", "Macro Kit steps aside. Switch to the window you want and Macro Kit takes it.");
            program.ToolTip = "The program, for example Chrome.exe or EXCEL.EXE.";
            title.ToolTip = "Part of the window title. Leave empty to match any window of that program.";
            program.TextChanged += (s, e) => Edited();
            title.TextChanged += (s, e) => Edited();
            summary = Ui.Text("", "Muted", false, 12);
            found = Ui.Text("", "Muted", false, 12);
            hint = Ui.Text("", "Muted", false, 12, true);
            hint.Margin = new Thickness(0, 4, 0, 0);
            hint.Visibility = Visibility.Collapsed;

            var top = new Grid();
            top.ColumnDefinitions.Add(new ColumnDefinition());
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            top.Children.Add(windows);
            Grid.SetColumn(pick, 1);
            pick.Margin = new Thickness(8, 0, 0, 0);
            top.Children.Add(pick);

            var detail = new Grid { Margin = new Thickness(0, 8, 0, 0) };
            detail.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            detail.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            detail.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            detail.ColumnDefinitions.Add(new ColumnDefinition());
            var l1 = Ui.Text("Program", "Muted", false, 12); l1.Margin = new Thickness(0, 0, 8, 0);
            var l2 = Ui.Text("Title contains", "Muted", false, 12); l2.Margin = new Thickness(14, 0, 8, 0);
            Grid.SetColumn(program, 1); Grid.SetColumn(l2, 2); Grid.SetColumn(title, 3);
            detail.Children.Add(l1); detail.Children.Add(program); detail.Children.Add(l2); detail.Children.Add(title);

            var footer = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
            DockPanel.SetDock(found, Dock.Right);
            footer.Children.Add(found);
            footer.Children.Add(summary);

            Root = Ui.Stack(0, top, detail, footer, hint);
            FillWindows();
        }

        public void Bind(WindowMatch match, Action onChanged)
        {
            this.match = match;
            changed = onChanged ?? (() => { });
            ShowHint(null);
            ShowMatch();
        }

        // Picking or choosing a window keeps only the program. A window title usually holds the document name, which changes
        // from day to day, so it is shown as a hint and the person adds a title filter only when it is really needed.
        void UseWindow(WindowInfo window)
        {
            if (match == null || window == null) return;
            match.ProcessName = window.ProcessName ?? "";
            match.TitleContains = match.ProcessName.Length == 0 ? window.Title ?? "" : "";
            ShowMatch();
            ShowHint(window);
            changed();
        }

        void ShowHint(WindowInfo window)
        {
            bool show = window != null && !string.IsNullOrEmpty(window.Title) && !string.IsNullOrEmpty(window.ProcessName);
            hint.Text = show ? "Picked: " + window.Title + ". Add a title filter only if you have several windows of this program open." : "";
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
            if (match == null || match.IsEmpty) { summary.Text = "No window chosen yet."; found.Text = ""; return; }
            summary.Text = match.Describe();
            bool open = false;
            try { open = controller.Windows.Find(match) != null; } catch (Exception) { }
            found.Text = open ? "Open now" : "Not open right now";
            Ui.Ref(found, TextBlock.ForegroundProperty, open ? "Good" : "Muted");
        }

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
    }
}
