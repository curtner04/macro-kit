using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MacroKit.Core;
using MacroKit.Keybinds;

namespace MacroKit.App
{
    /// <summary>
    /// Adds or edits an app on the Global Keybinds tab by hand: its name, optionally its program (for the Running label), and
    /// its shortcuts with where each one works. Nothing here touches the app itself; the list is Macro Kit's own (keybinds.json).
    /// </summary>
    public sealed class KeybindAppEditor
    {
        public enum Outcome { Cancelled, Saved, Deleted }

        public readonly FrameworkElement Root;
        readonly KeybindAppDef existing;
        readonly List<string> takenNames;
        readonly TextBox name = new TextBox { MinWidth = 230 };
        readonly ComboBox program = new ComboBox { IsEditable = true, MinWidth = 230, ToolTip = "Optional. The program's file name, such as obs64 or Discord, so Macro Kit can say whether it is running. Pick one that is running now or type it." };
        readonly StackPanel rows = new StackPanel();
        readonly TextBlock problemsText = Ui.Text("", "Warn", false, 12, true);
        readonly Button save;
        readonly Button delete;
        readonly List<ShortcutRow> shortcutRows = new List<ShortcutRow>();
        Window window;
        Outcome outcome = Outcome.Cancelled;
        string initial = "";   // what the form held when it opened, to notice unsaved typing

        /// <summary>What was saved (valid after Save).</summary>
        public KeybindAppDef Result { get; private set; }

        public KeybindAppEditor(KeybindAppDef existing, List<string> takenNames)
        {
            this.existing = existing;
            this.takenNames = takenNames ?? new List<string>();

            var title = Ui.Heading(existing == null ? "Add an app" : "Edit " + existing.Name);
            var intro = Ui.Help("Type in the shortcuts of a program Macro Kit cannot read by itself.",
                "They count everywhere on the tab: in the list, in the overlap warnings and in Is it free?. This only changes Macro Kit's list, never the program.");
            intro.Margin = new Thickness(0, 4, 0, 0);

            name.Text = existing != null ? existing.Name : "";
            name.TextChanged += (s, e) => Validate();
            FillPrograms();
            program.Text = existing != null ? existing.Program ?? "" : "";
            program.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent, new TextChangedEventHandler((s, e) => Validate()));
            program.SelectionChanged += (s, e) => Validate();
            var fields = Ui.WrapRow(12, Ui.Field("App name", name), Ui.Field("Program (optional)", program));
            fields.Margin = new Thickness(0, 12, 0, 0);

            var head = ShortcutRow.Columns(Ui.Text("Keys", "Muted", true, 12), Ui.Text("What it does", "Muted", true, 12), Ui.Text("Works in", "Muted", true, 12), new Border());
            head.Margin = new Thickness(0, 14, 0, 4);
            var hint = Ui.Para("Click a Keys box and press the keys, or type them, such as ctrl+shift+m, F9 or prtsc.", "Muted", 12);
            var addRow = Ui.IconButton(Ui.GlyphAdd, "Add shortcut", () => { ShortcutRow row = AddRow("", "", false); row.Keys.Focus(); }, "Small");
            addRow.HorizontalAlignment = HorizontalAlignment.Left;
            addRow.Margin = new Thickness(0, 8, 0, 0);
            var scroll = new ScrollViewer { Content = new StackPanel { Children = { rows, addRow } }, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };

            save = Ui.Button(existing == null ? "Add app" : "Save", () => { if (Save()) Close(); }, "Primary");
            save.MinWidth = 90;
            var cancel = Ui.Button("Cancel", () => { outcome = Outcome.Cancelled; Close(); }, "Ghost");
            cancel.MinWidth = 84;
            cancel.IsCancel = true;
            cancel.Margin = new Thickness(8, 0, 0, 0);
            delete = Ui.IconButton(Ui.GlyphDelete, "Delete app", ConfirmDelete, "Ghost", "Remove this app and its shortcuts from Macro Kit's list.");
            delete.Visibility = existing == null ? Visibility.Collapsed : Visibility.Visible;
            delete.HorizontalAlignment = HorizontalAlignment.Left;
            var buttons = new Grid { Margin = new Thickness(0, 14, 0, 0) };
            buttons.ColumnDefinitions.Add(new ColumnDefinition());
            buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            buttons.Children.Add(delete);
            var right = Ui.Row(0, save, cancel);
            Grid.SetColumn(right, 1);
            buttons.Children.Add(right);
            problemsText.Margin = new Thickness(0, 10, 0, 0);

            var grid = new Grid { Margin = new Thickness(22, 20, 22, 18) };
            foreach (GridLength height in new[] { GridLength.Auto, GridLength.Auto, GridLength.Auto, GridLength.Auto, GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto, GridLength.Auto })
                grid.RowDefinitions.Add(new RowDefinition { Height = height });
            UIElement[] parts = { title, intro, fields, head, hint, scroll, problemsText, buttons };
            for (int i = 0; i < parts.Length; i++) { Grid.SetRow(parts[i], i); grid.Children.Add(parts[i]); }
            hint.Margin = new Thickness(0, 0, 0, 6);
            Root = grid;

            if (existing != null) foreach (KeybindDef shortcut in existing.Shortcuts) AddRow(shortcut.Keys, shortcut.Action, shortcut.OnlyWhenActive);
            if (shortcutRows.Count == 0) AddRow("", "", false);
            Validate();
            initial = Describe();
        }

        // ------------------------------------------------------------------ showing

        public Outcome ShowDialog(Window owner)
        {
            window = new Window
            {
                Title = existing == null ? "Add an app" : "Edit " + existing.Name, Width = 780, Height = 600, MinWidth = 640, MinHeight = 420,
                ShowInTaskbar = false, Content = Root,
                WindowStartupLocation = owner != null && owner.IsVisible ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
            };
            if (owner != null && owner.IsVisible) window.Owner = owner;
            Theme.Style(window);
            // Esc or the X would otherwise throw away everything typed.
            window.Closing += (s, e) =>
            {
                if (outcome == Outcome.Cancelled && Describe() != initial && !Ui.Confirm(window, "Discard your changes to this app?")) e.Cancel = true;
            };
            window.Loaded += (s, e) => { if (existing == null) name.Focus(); };
            window.ShowDialog();
            window.Content = null;
            return outcome;
        }

        /// <summary>The form's content as one string (without the id), to compare with how it started.</summary>
        string Describe()
        {
            KeybindAppDef def = Collect();
            return def.Name + "\n" + def.Program + "\n" + string.Join("\n", def.Shortcuts.Select(k => k.Keys + "|" + k.Action + "|" + k.OnlyWhenActive));
        }

        void Close() { if (window != null) window.Close(); }

        void ConfirmDelete()
        {
            if (existing == null) return;
            if (!Ui.Confirm(window, "Remove " + existing.Name + " and its " + existing.Shortcuts.Count + " shortcut" + (existing.Shortcuts.Count == 1 ? "" : "s") + " from Global Keybinds? This only changes Macro Kit's list.")) return;
            outcome = Outcome.Deleted;
            Close();
        }

        // ------------------------------------------------------------------ rows

        /// <summary>Adds a shortcut row (also used by the self test).</summary>
        public ShortcutRow AddRow(string keys, string action, bool onlyWhenActive)
        {
            var row = new ShortcutRow(keys, action, onlyWhenActive);
            row.Changed += Validate;
            row.Removed += () =>
            {
                rows.Children.Remove(row.Root);
                shortcutRows.Remove(row);
                if (shortcutRows.Count == 0) AddRow("", "", false);
                Validate();
            };
            shortcutRows.Add(row);
            rows.Children.Add(row.Root);
            Validate();
            return row;
        }

        void FillPrograms()
        {
            var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (Process process in Process.GetProcesses())
                {
                    try { if (process.MainWindowHandle != IntPtr.Zero && process.ProcessName.Length > 0) names.Add(process.ProcessName); }
                    catch (Exception) { }
                    finally { process.Dispose(); }
                }
            }
            catch (Exception ex) { Log.Warn("Listing programs failed", ex); }
            foreach (string n in names) program.Items.Add(n);
        }

        // ------------------------------------------------------------------ result

        public void SetName(string text) { name.Text = text; }
        public void SetProgram(string text) { program.Text = text; }

        KeybindAppDef Collect()
        {
            var def = new KeybindAppDef { Id = existing != null ? existing.Id : Guid.NewGuid().ToString("N"), Name = name.Text.Trim(), Program = CustomKeybinds.ProgramName(program.Text) };
            foreach (ShortcutRow row in shortcutRows)
            {
                string keys = row.Keys.Text.Trim();
                string action = row.Action.Text.Trim();
                if (keys.Length == 0 && action.Length == 0) continue;
                Hotkey combo;
                if (KeyText.TryParse(keys, out combo)) keys = combo.ToString();
                def.Shortcuts.Add(new KeybindDef { Keys = keys, Action = action, OnlyWhenActive = row.OnlyWhenActive });
            }
            return def;
        }

        /// <summary>Why it cannot be saved yet; empty when it can.</summary>
        public List<string> Problems { get { return CustomKeybinds.Problems(Collect(), takenNames); } }

        void Validate()
        {
            if (save == null) return;   // still building
            List<string> problems = Problems;
            bool untouched = name.Text.Trim().Length == 0 && Collect().Shortcuts.Count == 0;   // a fresh form: nothing to complain about yet
            problemsText.Text = string.Join("\n", problems.Take(3)) + (problems.Count > 3 ? "\n…and " + (problems.Count - 3) + " more." : "");
            problemsText.Visibility = problems.Count > 0 && !untouched ? Visibility.Visible : Visibility.Collapsed;
            save.IsEnabled = problems.Count == 0;
        }

        /// <summary>Keeps what was entered when it is valid. Returns false (and shows why) otherwise.</summary>
        public bool Save()
        {
            List<string> problems = Problems;
            if (problems.Count > 0)
            {
                problemsText.Text = string.Join("\n", problems);
                problemsText.Visibility = Visibility.Visible;
                return false;
            }
            Result = Collect();
            outcome = Outcome.Saved;
            return true;
        }

        // ------------------------------------------------------------------ one shortcut

        public sealed class ShortcutRow
        {
            public readonly Grid Root;
            public readonly KeysBox Keys;
            public readonly TextBox Action = new TextBox { ToolTip = "What the shortcut does, for example Toggle mute." };
            readonly ComboBox scope = new ComboBox();
            public event Action Changed = () => { };
            public event Action Removed = () => { };

            public ShortcutRow(string keys, string action, bool onlyWhenActive)
            {
                Keys = new KeysBox { Text = keys ?? "" };
                Action.Text = action ?? "";
                scope.Items.Add("Any window");
                scope.Items.Add("Only in the app");
                scope.SelectedIndex = onlyWhenActive ? 1 : 0;
                scope.ToolTip = "Any window: the program reacts whichever window is active. Only in the app: only while that program is the active window.";
                Keys.TextChanged += (s, e) => Changed();
                Action.TextChanged += (s, e) => Changed();
                scope.SelectionChanged += (s, e) => Changed();
                var remove = Ui.GlyphButton(Ui.GlyphClose, "Remove this shortcut", () => Removed());
                System.Windows.Automation.AutomationProperties.SetName(Keys, "Keys");
                System.Windows.Automation.AutomationProperties.SetName(Action, "What it does");
                System.Windows.Automation.AutomationProperties.SetName(scope, "Works in");
                Root = Columns(Keys, Action, scope, remove);
                Root.Margin = new Thickness(0, 0, 0, 6);
            }

            public bool OnlyWhenActive { get { return scope.SelectedIndex == 1; } }

            public static Grid Columns(FrameworkElement keys, FrameworkElement action, FrameworkElement scope, FrameworkElement remove)
            {
                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 150 });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
                FrameworkElement[] cells = { keys, action, scope, remove };
                for (int i = 0; i < cells.Length; i++)
                {
                    cells[i].Margin = new Thickness(0, 0, i < 3 ? 8 : 0, 0);
                    cells[i].VerticalAlignment = VerticalAlignment.Center;
                    Grid.SetColumn(cells[i], i);
                    grid.Children.Add(cells[i]);
                }
                return grid;
            }
        }

        /// <summary>
        /// A text box for keys: type them (ctrl+shift+m) or press them. Pressing a key with Ctrl, Alt or Win held, or an F key,
        /// Print Screen, Pause or Scroll Lock, puts that combination in the box; other keys type and edit as usual. While the box
        /// has the keyboard Macro Kit's own hotkeys are released, so pressing one shows it here instead of running it.
        /// </summary>
        public sealed class KeysBox : TextBox
        {
            bool capturing;

            public KeysBox()
            {
                SetResourceReference(StyleProperty, typeof(TextBox));
                FontFamily = Ui.MonoFont;
                InputMethod.SetIsInputMethodEnabled(this, false);
                ToolTip = "Press the keys, or type them such as ctrl+shift+m, F9 or prtsc.";
                PreviewKeyDown += (s, e) => { if (Capture(e.Key == Key.System ? e.SystemKey : e.Key)) e.Handled = true; };
                // Print Screen only arrives as a key-up.
                PreviewKeyUp += (s, e) => { Key key = e.Key == Key.System ? e.SystemKey : e.Key; if (key == Key.Snapshot && Capture(key)) e.Handled = true; };
                GotKeyboardFocus += (s, e) => { if (!capturing) { capturing = true; HotkeyBox.CaptureStarted(); } };
                LostKeyboardFocus += (s, e) => { EndCapture(); Normalize(); };
                Unloaded += (s, e) => EndCapture();
            }

            void EndCapture() { if (capturing) { capturing = false; HotkeyBox.CaptureEnded(); } }

            void Normalize()
            {
                Hotkey combo;
                if (KeyText.TryParse(Text, out combo) && combo.ToString() != Text) Text = combo.ToString();
            }

            bool Capture(Key key)
            {
                if (key == Key.LeftCtrl || key == Key.RightCtrl || key == Key.LeftAlt || key == Key.RightAlt || key == Key.LeftShift || key == Key.RightShift
                    || key == Key.LWin || key == Key.RWin || key == Key.None || key == Key.ImeProcessed || key == Key.DeadCharProcessed) return false;
                ModifierKeys held = Keyboard.Modifiers;
                bool win = Keyboard.IsKeyDown(Key.LWin) || Keyboard.IsKeyDown(Key.RWin);
                bool chord = (held & (ModifierKeys.Control | ModifierKeys.Alt)) != 0 || win;
                bool special = (key >= Key.F1 && key <= Key.F24) || key == Key.Snapshot || key == Key.Pause || key == Key.Scroll;
                if (!chord && !special) return false;
                int vk = KeyInterop.VirtualKeyFromKey(key);
                if (vk <= 0 || vk > 0xFE) return false;
                HotkeyModifiers modifiers = HotkeyModifiers.None;
                if ((held & ModifierKeys.Control) != 0) modifiers |= HotkeyModifiers.Ctrl;
                if ((held & ModifierKeys.Shift) != 0) modifiers |= HotkeyModifiers.Shift;
                if ((held & ModifierKeys.Alt) != 0) modifiers |= HotkeyModifiers.Alt;
                if (win) modifiers |= HotkeyModifiers.Win;
                Text = new Hotkey(modifiers, (ushort)vk).ToString();
                CaretIndex = Text.Length;
                return true;
            }
        }
    }
}
