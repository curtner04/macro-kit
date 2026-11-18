using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using MacroKit.Core;
using MacroKit.Macros;

namespace MacroKit.App
{
    /// <summary>
    /// A macro editor that works in the plain macro language (one step per line). The Macros tab turns its text into
    /// Super Macro text with <see cref="PlainMacro.ToScript"/> and hands the result back to <see cref="Show"/>.
    /// </summary>
    public interface IPlainEditor
    {
        FrameworkElement Root { get; }
        /// <summary>The plain text. Setting it does not raise <see cref="Changed"/>.</summary>
        string Text { get; set; }
        /// <summary>The user changed the text.</summary>
        event Action Changed;
        /// <summary>Shows what each line means (or what is wrong with it).</summary>
        void Show(PlainResult result);
        /// <summary>Adds lines at the cursor, or after the selected step.</summary>
        void InsertLines(string lines);
        /// <summary>Puts a block (repeat, take turns...) around the selected steps, or adds an empty one.</summary>
        void WrapBlock(string start, int separators, string end);
        void FocusEditor();
    }

    public static class EditorKinds
    {
        public const string Text = "Text", Tiles = "Tiles";
        public static string Normal(string kind) { return kind == Tiles ? Tiles : Text; }
    }

    // ====================================================================== typed editor

    /// <summary>Option A: type the macro, one step per line. Each line shows what Macro Kit understood, and suggestions pop up as you type.</summary>
    public sealed class PlainTextEditor : IPlainEditor
    {
        readonly Grid root = new Grid();
        readonly TextBox box;
        readonly Canvas marks = new Canvas { IsHitTestVisible = false, ClipToBounds = true };
        readonly Canvas notes = new Canvas { ClipToBounds = true };
        readonly TextBlock hint;
        readonly Popup suggest;
        readonly ListBox suggestList = new ListBox { MaxHeight = 260, MinWidth = 300 };
        readonly Func<Window> owner;
        PlainResult shown;
        bool setting, inserting, picked;

        public event Action Changed = () => { };
        public FrameworkElement Root { get { return root; } }

        public PlainTextEditor(Func<Window> owner)
        {
            this.owner = owner;
            box = new TextBox
            {
                FontFamily = Ui.MonoFont, FontSize = 13, AcceptsReturn = true, AcceptsTab = false, TextWrapping = TextWrapping.NoWrap,
                VerticalContentAlignment = VerticalAlignment.Top, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(10, 8, 10, 8), BorderThickness = new Thickness(0),
                Background = Brushes.Transparent, SpellCheck = { IsEnabled = false }
            };
            AutomationProperties.SetName(box, "Macro steps, one per line");
            box.TextChanged += (s, e) =>
            {
                UpdateHint();
                if (setting) return;
                Changed();
                if (!inserting) UpdateSuggestions(); else CloseSuggestions();
            };
            box.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler((s, e) => { UpdateNotes(); if (suggest != null && suggest.IsOpen) PlaceSuggestions(); }));
            box.SizeChanged += (s, e) => UpdateNotes();
            box.PreviewKeyDown += BoxKeyDown;
            box.LostKeyboardFocus += (s, e) => CloseSuggestions();
            box.SelectionChanged += (s, e) => { if (suggest != null && suggest.IsOpen && !CaretAtLineEnd()) CloseSuggestions(); };
            notes.SizeChanged += (s, e) => UpdateNotes();

            hint = Ui.Text("One step per line, like   ctrl+c   wait 50ms   type \"hello\"   repeat 3 times", "Muted", false, 12.5);
            hint.FontFamily = Ui.MonoFont;
            hint.IsHitTestVisible = false;
            hint.VerticalAlignment = VerticalAlignment.Top;
            hint.Margin = new Thickness(13, 9, 10, 0);

            // The text on the left; what each line does on the right, level with it.
            var frame = new Border { CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Height = 236 };
            Ui.Ref(frame, Border.BackgroundProperty, "Input");
            Ui.Ref(frame, Border.BorderBrushProperty, "Border");
            var inner = new Grid();
            inner.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.25, GridUnitType.Star) });
            inner.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 180 });
            inner.Children.Add(marks);
            inner.Children.Add(box);
            inner.Children.Add(hint);
            var notesFrame = new Border { BorderThickness = new Thickness(1, 0, 0, 0), Child = notes, CornerRadius = new CornerRadius(0, 8, 8, 0) };
            Ui.Ref(notesFrame, Border.BorderBrushProperty, "Border");
            Ui.Ref(notesFrame, Border.BackgroundProperty, "Card2");
            Grid.SetColumn(notesFrame, 1);
            inner.Children.Add(notesFrame);
            frame.Child = inner;

            var head = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.25, GridUnitType.Star) });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 180 });
            head.Children.Add(Ui.Text("Type each step on its own line", "Muted", false, 12));
            var understood = Ui.Text("Macro Kit understands", "Muted", false, 12);
            understood.Margin = new Thickness(10, 0, 0, 0);
            Grid.SetColumn(understood, 1);
            head.Children.Add(understood);

            // Quick ways to add a step, for when you don't know what to type.
            Button record = Chip("Record keys", () => StepActions.Record(owner(), this), "Press keys to add them as steps.");
            Button text = Chip("Type text", () => InsertLines("type \"\""), "Type some text. Put it between the quotes.");
            Button wait = Chip("Wait", () => InsertLines("wait 100ms"), "Pause before the next step.");
            Button click = Chip("Click", () => InsertLines("click"), "A left click. Also: right click, double click.");
            Button repeat = Chip("Repeat", () => WrapBlock("repeat 3 times", 0, "end repeat"), "Repeat the selected lines (or add an empty repeat).");
            Button open = Chip("Open app", () => StepActions.Launch(owner(), this), "Open a program or file.");
            Button more = null;
            more = Chip("More…", () => StepActions.ShowAddMenu(more, owner, this), "Every kind of step.");
            var chips = Ui.Wrap(Ui.Text("Add", "Muted", false, 12), record, text, wait, click, repeat, open, more);
            ((FrameworkElement)chips.Children[0]).Margin = new Thickness(0, 0, 8, 5);

            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.Children.Add(head);
            Grid.SetRow(frame, 1);
            root.Children.Add(frame);
            chips.Margin = new Thickness(0, 8, 0, -5);
            Grid.SetRow(chips, 2);
            root.Children.Add(chips);

            // Suggestions: a list under the line being typed. Tab (or Enter once you've picked with the arrows) takes one.
            suggestList.Focusable = false;
            suggestList.BorderThickness = new Thickness(0);
            suggestList.Background = Brushes.Transparent;
            var suggestFrame = new Border { Child = suggestList, CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Padding = new Thickness(3),
                Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 12, ShadowDepth = 2, Opacity = 0.3 }, Margin = new Thickness(0, 0, 8, 10) };
            Ui.Ref(suggestFrame, Border.BackgroundProperty, "Card");
            Ui.Ref(suggestFrame, Border.BorderBrushProperty, "Border");
            suggest = new Popup { Child = suggestFrame, PlacementTarget = box, Placement = PlacementMode.Relative, StaysOpen = true, AllowsTransparency = true, Focusable = false };
            UpdateHint();
        }

        static Button Chip(string label, Action click, string tooltip)
        {
            return Ui.Button(label, click, "Chip", tooltip);
        }

        public string Text
        {
            get { return box.Text; }
            set
            {
                setting = true;
                box.Text = value ?? "";
                box.CaretIndex = box.Text.Length;
                setting = false;
                CloseSuggestions();
                UpdateHint();
            }
        }

        public void FocusEditor() { box.Focus(); }

        public void Show(PlainResult result)
        {
            shown = result;
            UpdateNotes();
            // Right after the text is set the lines have no layout yet; try again once they do.
            root.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(UpdateNotes));
        }

        void UpdateHint() { hint.Visibility = box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; }

        void UpdateNotes()
        {
            notes.Children.Clear();
            marks.Children.Clear();
            if (shown == null || box.Text.Length == 0) return;
            int first = box.GetFirstVisibleLineIndex(), last = box.GetLastVisibleLineIndex();
            if (first < 0 || last < 0) return;
            double width = Math.Max(0, notes.ActualWidth - 18);
            foreach (PlainLine line in shown.Lines)
            {
                if (line.Number < first || line.Number > last || line.Kind == PlainKind.Empty || line.Number >= box.LineCount) continue;
                int at = box.GetCharacterIndexFromLineIndex(line.Number);
                if (at < 0) continue;
                Rect r = box.GetRectFromCharacterIndex(at);
                if (r.IsEmpty) continue;
                Point p = box.TranslatePoint(new Point(0, r.Top), notes);
                string text;
                string colour;
                if (line.Error != null) { text = line.Error; colour = "Bad"; }
                else if (line.Kind == PlainKind.Note) { text = "A note (not run)"; colour = "Muted"; }
                else { text = line.Meaning; colour = line.Kind == PlainKind.Block ? "AccentText" : "Muted"; }
                if (line.Kind == PlainKind.Code && line.Error == null) text = "Stream Deck code: " + text;
                var t = Ui.Text(text, colour, false, 12);
                t.Width = width;
                t.ToolTip = text;
                Canvas.SetLeft(t, 10);
                Canvas.SetTop(t, p.Y + Math.Max(0, (r.Height - 16) / 2));
                notes.Children.Add(t);
                if (line.Error != null)
                {
                    Point q = box.TranslatePoint(new Point(0, r.Top), marks);
                    var band = new Border { Width = Math.Max(0, marks.ActualWidth), Height = r.Height + 2, IsHitTestVisible = false };
                    Ui.Ref(band, Border.BackgroundProperty, "BadBg");
                    Canvas.SetLeft(band, 0);
                    Canvas.SetTop(band, q.Y - 1);
                    marks.Children.Add(band);
                }
            }
        }

        // ---------------------------------------------------------------- inserting

        static string Normal(string text) { return (text ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Trim('\n').Replace("\n", "\r\n"); }

        void LineAround(int index, out int start, out int end)
        {
            string text = box.Text;
            index = Math.Max(0, Math.Min(index, text.Length));
            start = index == 0 ? 0 : text.LastIndexOf('\n', index - 1) + 1;
            end = text.IndexOf('\n', index);
            if (end < 0) end = text.Length;
            if (end > start && text[end - 1] == '\r') end--;
        }

        /// <summary>Replaces [start, end) with text and puts the cursor at caret (an offset into text).</summary>
        void Put(int start, int end, string text, int caret)
        {
            inserting = true;
            box.Select(start, end - start);
            box.SelectedText = text;
            inserting = false;
            box.Select(start + caret, 0);
            box.Focus();
            box.ScrollToLine(box.GetLineIndexFromCharacterIndex(box.CaretIndex));
        }

        public void InsertLines(string lines)
        {
            string text = Normal(lines);
            if (text.Length == 0) return;
            int start, end;
            LineAround(box.SelectionStart + box.SelectionLength, out start, out end);
            bool blank = box.Text.Substring(start, end - start).Trim().Length == 0;
            string put = blank ? text : "\r\n" + text;
            int caret = put.EndsWith("\"\"", StringComparison.Ordinal) ? put.Length - 1 : put.Length;
            if (blank) Put(start, end, put, caret);
            else Put(end, end, put, caret);
        }

        public void WrapBlock(string start, int separators, string end)
        {
            var lines = new List<string> { start };
            int caretLine;
            int from, to;
            if (box.SelectionLength > 0)
            {
                int s0, e0, s1, e1;
                LineAround(box.SelectionStart, out s0, out e0);
                LineAround(box.SelectionStart + box.SelectionLength - 1, out s1, out e1);
                from = s0; to = e1;
                lines.AddRange(Normal(box.Text.Substring(from, to - from)).Split(new[] { "\r\n" }, StringSplitOptions.None));
                for (int i = 0; i < separators; i++) { lines.Add("or"); lines.Add(""); }
                caretLine = -1;
            }
            else
            {
                LineAround(box.CaretIndex, out from, out to);
                if (box.Text.Substring(from, to - from).Trim().Length > 0) { lines.Insert(0, box.Text.Substring(from, to - from)); }
                caretLine = lines.Count;
                lines.Add("");
                for (int i = 0; i < separators; i++) { lines.Add("or"); lines.Add(""); }
            }
            lines.Add(end);
            string text = string.Join("\r\n", lines);
            int caret = caretLine < 0 ? text.Length : string.Join("\r\n", lines.Take(caretLine)).Length + 2;
            Put(from, to, text, caret);
        }

        // ---------------------------------------------------------------- suggestions

        bool CaretAtLineEnd()
        {
            int start, end;
            LineAround(box.CaretIndex, out start, out end);
            return box.SelectionLength == 0 && box.CaretIndex == end;
        }

        void UpdateSuggestions()
        {
            if (!box.IsKeyboardFocusWithin || !CaretAtLineEnd()) { CloseSuggestions(); return; }
            int start, end;
            LineAround(box.CaretIndex, out start, out end);
            string typed = box.Text.Substring(start, end - start).Trim();
            if (typed.Length == 0) { CloseSuggestions(); return; }
            var matches = PlainMacro.Suggestions.Where(s => s[0].StartsWith(typed, StringComparison.OrdinalIgnoreCase) && !s[0].Equals(typed, StringComparison.OrdinalIgnoreCase))
                .Concat(PlainMacro.Suggestions.Where(s => typed.Length >= 3 && !s[0].StartsWith(typed, StringComparison.OrdinalIgnoreCase)
                    && (s[0].IndexOf(typed, StringComparison.OrdinalIgnoreCase) >= 0 || s[1].IndexOf(typed, StringComparison.OrdinalIgnoreCase) >= 0)))
                .Take(8).ToList();
            if (matches.Count == 0) { CloseSuggestions(); return; }
            suggestList.Items.Clear();
            foreach (string[] m in matches)
            {
                var row = new Grid();
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var code = Ui.Text(m[0], "Text", false, 12.5);
                code.FontFamily = Ui.MonoFont;
                row.Children.Add(code);
                var what = Ui.Text(m[1], "Muted", false, 12);
                what.Margin = new Thickness(18, 0, 0, 0);
                Grid.SetColumn(what, 1);
                row.Children.Add(what);
                var item = new ListBoxItem { Content = row, Tag = m[0], Padding = new Thickness(8, 3, 8, 3), Focusable = false };
                item.PreviewMouseLeftButtonDown += (s, e) => { e.Handled = true; suggestList.SelectedItem = item; Accept(); };
                suggestList.Items.Add(item);
            }
            suggestList.SelectedIndex = 0;
            picked = false;
            PlaceSuggestions();
            suggest.IsOpen = true;
        }

        void PlaceSuggestions()
        {
            int start, end;
            LineAround(box.CaretIndex, out start, out end);
            Rect line = box.GetRectFromCharacterIndex(start);
            Rect caret = box.GetRectFromCharacterIndex(box.CaretIndex);
            if (line.IsEmpty || caret.IsEmpty) return;
            suggest.HorizontalOffset = Math.Max(0, line.Left - 6);
            suggest.VerticalOffset = caret.Bottom + 3;
        }

        void CloseSuggestions() { if (suggest != null) suggest.IsOpen = false; }

        void Accept()
        {
            var item = suggestList.SelectedItem as ListBoxItem;
            CloseSuggestions();
            if (item == null) return;
            string text = (string)item.Tag;
            int start, end;
            LineAround(box.CaretIndex, out start, out end);
            int caret = text.EndsWith("\"\"", StringComparison.Ordinal) ? text.Length - 1 : text.Length;
            Put(start, end, text, caret);
        }

        void BoxKeyDown(object sender, KeyEventArgs e)
        {
            if (!suggest.IsOpen)
            {
                if (e.Key == Key.Space && Keyboard.Modifiers == ModifierKeys.Control) { e.Handled = true; UpdateSuggestions(); }
                return;
            }
            switch (e.Key)
            {
                case Key.Down:
                    suggestList.SelectedIndex = Math.Min(suggestList.Items.Count - 1, suggestList.SelectedIndex + 1);
                    suggestList.ScrollIntoView(suggestList.SelectedItem);
                    picked = true; e.Handled = true; break;
                case Key.Up:
                    suggestList.SelectedIndex = Math.Max(0, suggestList.SelectedIndex - 1);
                    suggestList.ScrollIntoView(suggestList.SelectedItem);
                    picked = true; e.Handled = true; break;
                case Key.Tab:
                    e.Handled = true; Accept(); break;
                case Key.Enter:
                    if (picked) { e.Handled = true; Accept(); }
                    else CloseSuggestions();
                    break;
                case Key.Escape:
                    e.Handled = true; CloseSuggestions(); break;
            }
        }

        // self test
        public int CaretIndex { get { return box.CaretIndex; } set { box.CaretIndex = value; } }
        public void Select(int start, int length) { box.Select(start, length); }
        public int NoteCount { get { return notes.Children.Count; } }
    }

    // ====================================================================== key tiles editor

    /// <summary>Option B: the macro as a row of tiles ("Ctrl + C", "Alt + Tab", "50 ms"), built with a + button. No key down and up clutter.</summary>
    public sealed class KeyTileEditor : IPlainEditor
    {
        readonly StackPanel root = new StackPanel();
        readonly WrapPanel tiles = new WrapPanel();
        readonly Border editBar;
        readonly TextBox editBox;
        readonly TextBlock editInfo;
        readonly Button moveLeft, moveRight;
        readonly Func<Window> owner;
        List<string> lines = new List<string>();
        int selected = -1;
        bool editing;
        PlainResult shown;
        Button addTile;

        public event Action Changed = () => { };
        public FrameworkElement Root { get { return root; } }

        public KeyTileEditor(Func<Window> owner)
        {
            this.owner = owner;
            var frame = new Border { CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Padding = new Thickness(10, 10, 4, 4), MinHeight = 120 };
            Ui.Ref(frame, Border.BackgroundProperty, "Input");
            Ui.Ref(frame, Border.BorderBrushProperty, "Border");
            frame.Child = new ScrollViewer { Content = tiles, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 300 };

            editBox = new TextBox { FontFamily = Ui.MonoFont, FontSize = 13, MinWidth = 220, SpellCheck = { IsEnabled = false } };
            AutomationProperties.SetName(editBox, "The selected step");
            editBox.TextChanged += (s, e) =>
            {
                if (editing || selected < 0 || selected >= lines.Count) return;
                lines[selected] = editBox.Text;
                Changed();
            };
            editBox.PreviewKeyDown += (s, e) => { if (e.Key == Key.Enter || e.Key == Key.Escape) { e.Handled = true; Select(-1); } };
            editInfo = Ui.Text("", "Muted", false, 12);
            moveLeft = Ui.Button("◀", () => Move(-1), "Small", "Move this step earlier.");
            moveRight = Ui.Button("▶", () => Move(1), "Small", "Move this step later.");
            var delete = Ui.Button("Delete", () => Delete(selected), "Small", "Remove this step.");
            var done = Ui.Button("Done", () => Select(-1), "Small", "Finish editing this step.");
            var editDock = new DockPanel();
            var editButtons = Ui.Row(6, moveLeft, moveRight, delete, done);
            editButtons.Margin = new Thickness(8, 0, 0, 0);
            DockPanel.SetDock(editButtons, Dock.Right);
            editDock.Children.Add(editButtons);
            editDock.Children.Add(editBox);
            editInfo.Margin = new Thickness(2, 5, 0, 0);
            editInfo.TextWrapping = TextWrapping.Wrap;
            editBar = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 8, 10, 8), Margin = new Thickness(0, 8, 0, 0), Visibility = Visibility.Collapsed,
                Child = Ui.Stack(0, Ui.Text("Change this step by typing, like ctrl+shift+s or wait 2s", "Muted", false, 12), editDock, editInfo) };
            ((FrameworkElement)((StackPanel)editBar.Child).Children[1]).Margin = new Thickness(0, 4, 0, 0);
            Ui.Ref(editBar, Border.BackgroundProperty, "Card2");

            root.Children.Add(Ui.Text("Your macro, step by step. Press + to add keys, text, actions and delays. Click a step to change it.", "Muted", false, 12));
            ((FrameworkElement)root.Children[0]).Margin = new Thickness(0, 0, 0, 4);
            root.Children.Add(frame);
            root.Children.Add(editBar);
            Render();
        }

        public string Text
        {
            get { return string.Join("\r\n", lines); }
            set
            {
                lines = (value ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').ToList();
                if (lines.Count == 1 && lines[0].Length == 0) lines.Clear();
                selected = -1;
                editBar.Visibility = Visibility.Collapsed;
            }
        }

        public void FocusEditor()
        {
            if (editBar.Visibility == Visibility.Visible) editBox.Focus();
            else if (addTile != null) addTile.Focus();
        }

        public void Show(PlainResult result)
        {
            shown = result;
            Render();
            ShowEditInfo();
        }

        PlainLine LineFor(int number)
        {
            return shown == null ? null : shown.Lines.FirstOrDefault(l => l.Number == number);
        }

        void Render()
        {
            tiles.Children.Clear();
            for (int i = 0; i < lines.Count; i++)
            {
                if (lines[i].Trim().Length == 0) continue;
                PlainLine line = LineFor(i);
                tiles.Children.Add(Tile(i, line));
            }
            addTile = Ui.Button(Ui.Glyph(Ui.GlyphAdd, 14, "AccentText"), null, "Chip", "Add a step: record keys, text, an action, an app, a system key or a delay.");
            addTile.Padding = new Thickness(14, 7, 14, 7);
            addTile.Margin = new Thickness(0, 0, 8, 8);
            AutomationProperties.SetName(addTile, "Add a step");
            Button anchor = addTile;
            addTile.Click += (s, e) => StepActions.ShowAddMenu(anchor, owner, this);
            tiles.Children.Add(addTile);
            if (tiles.Children.Count == 1)
            {
                var empty = Ui.Text("No steps yet. Press + to add the first one.", "Muted", false, 12.5);
                empty.Margin = new Thickness(4, 0, 0, 8);
                tiles.Children.Add(empty);
            }
        }

        FrameworkElement Tile(int number, PlainLine line)
        {
            string label = line == null ? lines[number].Trim() : PlainMacro.TileLabel(line);
            PlainKind kind = line == null ? PlainKind.Code : line.Error != null ? PlainKind.Problem : line.Kind;
            string fill, ink;
            switch (kind)
            {
                case PlainKind.Keys: fill = "KeySwitchBg"; ink = "Text"; break;
                case PlainKind.Text: fill = "KeyClipBg"; ink = "Text"; label = "“" + label + "”"; break;
                case PlainKind.Variable: fill = "KeyClipBg"; ink = "Text"; break;
                case PlainKind.Wait: fill = "Card2"; ink = "Muted"; break;
                case PlainKind.Mouse: fill = "KeyArrowBg"; ink = "Text"; break;
                case PlainKind.App: fill = "KeyEditBg"; ink = "Text"; break;
                case PlainKind.Block: fill = "InfoBg"; ink = "AccentText"; break;
                case PlainKind.Note: fill = "Card2"; ink = "Muted"; break;
                case PlainKind.Problem: fill = "BadBg"; ink = "Bad"; break;
                default: fill = "Card2"; ink = "Text"; break;
            }
            if (kind == PlainKind.Text && line != null && line.Steps.Count == 1 && line.Steps[0].Text == "\n") { label = "Enter"; fill = "KeySwitchBg"; }
            if (label.Length > 40) label = label.Substring(0, 40) + "…";
            var text = Ui.Text(label, ink, kind == PlainKind.Keys || kind == PlainKind.Block, kind == PlainKind.Wait ? 12 : 13);
            if (kind == PlainKind.Note) text.FontStyle = FontStyles.Italic;
            if (kind == PlainKind.Code) text.FontFamily = Ui.MonoFont;
            FrameworkElement content = text;
            if (kind == PlainKind.Wait)
            {
                var clock = Ui.Glyph("", 11, "Muted");
                clock.Margin = new Thickness(0, 0, 5, 0);
                content = Ui.Row(0, clock, text);
            }
            else if (kind == PlainKind.Problem)
            {
                var warn = Ui.Glyph(Ui.GlyphWarn, 11, "Bad");
                warn.Margin = new Thickness(0, 0, 6, 0);
                content = Ui.Row(0, warn, text);
            }
            var face = new Border { CornerRadius = new CornerRadius(7), Padding = kind == PlainKind.Wait ? new Thickness(9, 4, 10, 4) : new Thickness(13, 6, 13, 6),
                BorderThickness = new Thickness(2), Child = content, VerticalAlignment = VerticalAlignment.Center };
            Ui.Ref(face, Border.BackgroundProperty, fill);
            if (number == selected) Ui.Ref(face, Border.BorderBrushProperty, "AccentText");
            else Ui.Ref(face, Border.BorderBrushProperty, fill);
            var b = Ui.Button(face, () => Select(number == selected ? -1 : number), "Ghost",
                line == null ? lines[number] : line.Error ?? (line.Meaning.Length > 0 ? line.Meaning : line.Source));
            b.Padding = new Thickness(0);
            b.Margin = new Thickness(0, 0, 6, 8);
            b.VerticalAlignment = VerticalAlignment.Center;
            AutomationProperties.SetName(b, label);
            b.KeyDown += (s, e) => { if (e.Key == Key.Delete) { e.Handled = true; Delete(number); } };
            return b;
        }

        void Select(int number)
        {
            selected = number;
            editing = true;
            editBox.Text = number >= 0 && number < lines.Count ? lines[number].Trim() : "";
            editing = false;
            editBar.Visibility = number >= 0 ? Visibility.Visible : Visibility.Collapsed;
            Render();
            ShowEditInfo();
            if (number >= 0) { editBox.Focus(); editBox.CaretIndex = editBox.Text.Length; }
        }

        void ShowEditInfo()
        {
            if (selected < 0) return;
            PlainLine line = LineFor(selected);
            if (line == null) { editInfo.Text = ""; return; }
            editInfo.Text = line.Error ?? (line.Kind == PlainKind.Note ? "A note to yourself; it is not run." : line.Meaning);
            Ui.Ref(editInfo, TextBlock.ForegroundProperty, line.Error != null ? "Bad" : "Muted");
            int before = Neighbour(selected, -1), after = Neighbour(selected, 1);
            moveLeft.IsEnabled = before >= 0;
            moveRight.IsEnabled = after >= 0;
        }

        int Neighbour(int number, int direction)
        {
            for (int i = number + direction; i >= 0 && i < lines.Count; i += direction)
                if (lines[i].Trim().Length > 0) return i;
            return -1;
        }

        void Move(int direction)
        {
            if (selected < 0) return;
            int other = Neighbour(selected, direction);
            if (other < 0) return;
            string t = lines[other];
            lines[other] = lines[selected];
            lines[selected] = t;
            selected = other;
            Changed();
            Select(selected);
        }

        void Delete(int number)
        {
            if (number < 0 || number >= lines.Count) return;
            lines.RemoveAt(number);
            selected = -1;
            editBar.Visibility = Visibility.Collapsed;
            Changed();
        }

        int InsertAt { get { return selected >= 0 ? selected + 1 : TrimmedCount(); } }

        /// <summary>The line count without blank lines at the end, so new steps go after the last real one.</summary>
        int TrimmedCount()
        {
            int n = lines.Count;
            while (n > 0 && lines[n - 1].Trim().Length == 0) n--;
            return n;
        }

        public void InsertLines(string text)
        {
            var add = (text ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').Where(l => l.Trim().Length > 0).ToList();
            if (add.Count == 0) return;
            int at = InsertAt;
            lines.InsertRange(at, add);
            selected = -1;
            editBar.Visibility = Visibility.Collapsed;
            Changed();
        }

        public void WrapBlock(string start, int separators, string end)
        {
            var block = new List<string> { start };
            int at;
            if (selected >= 0)
            {
                at = selected;
                block.Add(lines[selected]);
                lines.RemoveAt(selected);
            }
            else at = TrimmedCount();
            for (int i = 0; i < separators; i++) block.Add("or");
            block.Add(end);
            lines.InsertRange(at, block);
            selected = -1;
            editBar.Visibility = Visibility.Collapsed;
            Changed();
        }

        // self test
        public int TileCount { get { return tiles.Children.OfType<Button>().Count() - 1; } }
        public string[] TileLabels { get { return tiles.Children.OfType<Button>().Where(b => b != addTile).Select(b => AutomationProperties.GetName(b)).ToArray(); } }
        public void SelectTile(int lineNumber) { Select(lineNumber); }
        public void MoveSelected(int direction) { Move(direction); }
    }

    // ====================================================================== adding steps

    /// <summary>The ways to add a step that both editors share: the + menu, recording keys, typing text, launching an app, delays.</summary>
    public static class StepActions
    {
        static KeyValuePair<string, Action> Item(string label, Action action) { return new KeyValuePair<string, Action>(label, action); }

        static void Open(Popup menu) { menu.IsOpen = true; }

        public static void ShowAddMenu(UIElement anchor, Func<Window> owner, IPlainEditor editor)
        {
            Open(Ui.Menu(anchor,
                Item("Record keystrokes…", () => Record(owner(), editor)),
                Item("Text…", () => AddText(owner(), editor)),
                Item("Action…", () => ShowActionMenu(anchor, owner, editor)),
                Item("Launch application…", () => Launch(owner(), editor)),
                Item("System…", () => ShowSystemMenu(anchor, owner, editor)),
                Item("Delay…", () => AddDelay(owner(), editor)),
                Item("Type a step yourself…", () => AddTyped(owner(), editor))));
        }

        static void ShowActionMenu(UIElement anchor, Func<Window> owner, IPlainEditor editor)
        {
            Open(Ui.Menu(anchor,
                Item("Left click", () => editor.InsertLines("click")),
                Item("Right click", () => editor.InsertLines("right click")),
                Item("Double click", () => editor.InsertLines("double click")),
                Item("Middle click", () => editor.InsertLines("middle click")),
                Item("Scroll down", () => editor.InsertLines("scroll down 3")),
                Item("Scroll up", () => editor.InsertLines("scroll up 3")),
                Item("Move the mouse to…", () => Ask(owner(), "Move the mouse", "Where on the screen? Two numbers in pixels, like 640, 400:", "640, 400", v => "move mouse to " + v, editor)),
                Item("Repeat steps", () => editor.WrapBlock("repeat 3 times", 0, "end repeat")),
                Item("Take turns", () => editor.WrapBlock("take turns", 1, "end turns")),
                Item("Pick at random", () => editor.WrapBlock("pick at random", 1, "end random")),
                Item("Run another macro…", () => Ask(owner(), "Run another macro", "The name of the macro to run:", "", v => PlainMacro.ToPlain(MacroScript.Macro(v), true), editor)),
                Item("Switch to a window…", () => Ask(owner(), "Switch to a window", "Part of the window's title or its program name:", "", v => PlainMacro.ToPlain(MacroScript.Focus(v), true), editor)),
                Item("Wait for a window…", () => Ask(owner(), "Wait for a window", "Part of the window's title or its program name:", "", v => PlainMacro.ToPlain(MacroScript.WaitWindow(v, MacroParser.DefaultWaitWindowMs), true), editor)),
                Item("Open a website…", () => Ask(owner(), "Open a website", "The web address:", "https://", v => PlainMacro.ToPlain(MacroScript.Website(v), true), editor)),
                Item("Note…", () => Ask(owner(), "Note", "A note to yourself. It is not run:", "", v => "# " + v, editor))));
        }

        static void ShowSystemMenu(UIElement anchor, Func<Window> owner, IPlainEditor editor)
        {
            Open(Ui.Menu(anchor,
                Item("Play / pause", () => editor.InsertLines("play/pause")),
                Item("Next track", () => editor.InsertLines("next track")),
                Item("Previous track", () => editor.InsertLines("previous track")),
                Item("Mute", () => editor.InsertLines("mute")),
                Item("Volume up", () => editor.InsertLines("volume up")),
                Item("Volume down", () => editor.InsertLines("volume down")),
                Item("Set the volume…", () => Ask(owner(), "Set the volume", "The volume, from 0 to 100:", "50", v => "volume " + v.TrimEnd('%') + "%", editor)),
                Item("Show the desktop", () => editor.InsertLines("win+d")),
                Item("Task Manager", () => editor.InsertLines("ctrl+shift+esc")),
                Item("Close a program…", () => Ask(owner(), "Close a program", "The program's name, like notepad:", "", v => PlainMacro.ToPlain(MacroScript.Close(v), true), editor)),
                Item("Lock the PC", () => editor.InsertLines("lock pc"))));
        }

        /// <summary>Asks for a value, checks that the line it makes is understood, then adds it.</summary>
        static void Ask(Window owner, string title, string label, string initial, Func<string, string> line, IPlainEditor editor)
        {
            string value = PlainPrompt.Ask(owner, title, label, initial, v =>
            {
                if (v.Trim().Length == 0) return "Fill this in first.";
                PlainResult r = PlainMacro.ToScript(line(v.Trim()));
                PlainLine bad = r.Lines.FirstOrDefault(l => l.Error != null);
                return bad == null ? null : bad.Error;
            });
            if (value != null) editor.InsertLines(line(value.Trim()));
        }

        public static void Record(Window owner, IPlainEditor editor)
        {
            var w = new KeyRecorderWindow(owner);
            if (w.ShowDialog() == true && w.Lines.Length > 0) editor.InsertLines(w.Lines);
        }

        public static void AddText(Window owner, IPlainEditor editor)
        {
            string text = PlainPrompt.Ask(owner, "Text", "The text to type, exactly as written:", "", v => v.Length == 0 ? "Type the text first." : null);
            if (!string.IsNullOrEmpty(text)) editor.InsertLines(PlainMacro.ToPlain(MacroScript.Text(text), true));
        }

        public static void AddDelay(Window owner, IPlainEditor editor)
        {
            int ms = 0;
            string text = PlainPrompt.Ask(owner, "Delay", "How long to wait, like 100ms, 2s or 1 min:", "100ms",
                v => PlainMacro.TryDuration(v.Trim(), out ms) ? null : "Write a time, like 100ms or 2s.");
            if (text != null && PlainMacro.TryDuration(text.Trim(), out ms)) editor.InsertLines("wait " + PlainMacro.Ms(ms));
        }

        static void AddTyped(Window owner, IPlainEditor editor)
        {
            string text = PlainPrompt.Ask(owner, "Add a step", "Type the step the way you'd say it, like ctrl+shift+s, wait 2s or type \"hello\":", "", v =>
            {
                if (v.Trim().Length == 0) return "Type a step first.";
                PlainLine bad = PlainMacro.ToScript(v).Lines.FirstOrDefault(l => l.Error != null);
                return bad == null ? null : bad.Error;
            });
            if (text != null) editor.InsertLines(text.Trim());
        }

        public static void Launch(Window owner, IPlainEditor editor)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Choose a program or file to open",
                Filter = "Programs and shortcuts (*.exe;*.lnk;*.bat;*.cmd)|*.exe;*.lnk;*.bat;*.cmd|All files (*.*)|*.*",
                DereferenceLinks = false
            };
            if (dialog.ShowDialog(owner) == true) editor.InsertLines(PlainMacro.ToPlain(MacroScript.Run(dialog.FileName), true));
        }
    }

    // ====================================================================== small windows

    /// <summary>A one-line question with OK and Cancel. Returns null when cancelled.</summary>
    public sealed class PlainPrompt : Window
    {
        readonly TextBox input = new TextBox { MinWidth = 380, FontSize = 13 };
        readonly TextBlock error = Ui.Text("", "Bad", false, 12, true);
        readonly Func<string, string> check;
        string answer;

        PlainPrompt(Window owner, string title, string label, string initial, Func<string, string> check)
        {
            this.check = check;
            Title = title;
            SizeToContent = SizeToContent.WidthAndHeight;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            if (owner != null && owner.IsVisible) { Owner = owner; WindowStartupLocation = WindowStartupLocation.CenterOwner; }
            else WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Theme.Style(this);
            input.Text = initial ?? "";
            input.TextChanged += (s, e) => error.Visibility = Visibility.Collapsed;
            error.Visibility = Visibility.Collapsed;
            error.MaxWidth = 420;
            var ok = Ui.Button("OK", Accept, "Primary");
            ok.IsDefault = true;
            ok.MinWidth = 80;
            var cancel = Ui.Button("Cancel", () => { DialogResult = false; }, null);
            cancel.IsCancel = true;
            cancel.MinWidth = 80;
            var buttons = Ui.Row(8, ok, cancel);
            buttons.HorizontalAlignment = HorizontalAlignment.Right;
            var text = Ui.Para(label, "Text");
            text.MaxWidth = 420;
            Content = Ui.Stack(10, text, input, error, buttons);
            ((FrameworkElement)Content).Margin = new Thickness(20, 18, 20, 18);
            Loaded += (s, e) => { input.Focus(); input.SelectAll(); };
        }

        void Accept()
        {
            string problem = check == null ? null : check(input.Text);
            if (problem != null) { error.Text = problem; error.Visibility = Visibility.Visible; return; }
            answer = input.Text;
            DialogResult = true;
        }

        public static string Ask(Window owner, string title, string label, string initial, Func<string, string> check = null)
        {
            var w = new PlainPrompt(owner, title, label, initial, check);
            return w.ShowDialog() == true ? w.answer : null;
        }
    }

    /// <summary>
    /// Records keys pressed in this window as steps: Ctrl+C becomes one "Ctrl+C" step, never a key down and a key up.
    /// Keys Windows keeps for itself (Alt+Tab, Win+L, Ctrl+Alt+Del) never reach it; type those steps instead.
    /// </summary>
    public sealed class KeyRecorderWindow : Window
    {
        readonly List<string> steps = new List<string>();
        readonly List<ushort> held = new List<ushort>();
        readonly WrapPanel preview = new WrapPanel();
        readonly CheckBox withPauses = new CheckBox { Content = "Also record the pauses between keys", Focusable = false };
        readonly TextBlock empty = Ui.Text("Press the keys now…", "Muted", false, 13);
        ushort lone;              // a modifier pressed with no other key yet
        DateTime last = DateTime.MinValue;
        bool closing;

        /// <summary>The recorded steps, one per line.</summary>
        public string Lines { get; private set; }

        public KeyRecorderWindow(Window owner)
        {
            Lines = "";
            Title = "Record keystrokes";
            Width = 560;
            SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            if (owner != null && owner.IsVisible) { Owner = owner; WindowStartupLocation = WindowStartupLocation.CenterOwner; }
            else WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Theme.Style(this);

            var dot = Ui.Shape("record", 12, "Rec");
            var title = Ui.Row(9, dot, Ui.Text("Recording keys", "Text", true, 15));
            var help = Ui.Para("Press keys and shortcuts the way you want the macro to. Each shortcut becomes one step, such as Ctrl + C. " +
                "Click Done when you have finished. Shortcuts Windows keeps for itself, like Alt+Tab or Win+L, can't be recorded here; add those by typing them.", "Muted", 12);
            var frame = new Border { CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Padding = new Thickness(10, 10, 4, 4), MinHeight = 86 };
            Ui.Ref(frame, Border.BackgroundProperty, "Input");
            Ui.Ref(frame, Border.BorderBrushProperty, "Border");
            frame.Child = new ScrollViewer { Content = preview, MaxHeight = 220, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            preview.Children.Add(empty);
            var clear = Button("Clear", () => { steps.Clear(); held.Clear(); lone = 0; ShowSteps(); }, null);
            var cancel = Button("Cancel", () => { closing = true; DialogResult = false; }, null);
            var done = Button("Done", Done, "Primary");
            done.MinWidth = 84;
            var buttons = new DockPanel { LastChildFill = false };
            buttons.Children.Add(withPauses);
            withPauses.VerticalAlignment = VerticalAlignment.Center;
            var right = Ui.Row(8, clear, cancel, done);
            DockPanel.SetDock(right, Dock.Right);
            buttons.Children.Add(right);
            var body = Ui.Stack(12, title, help, frame, buttons);
            body.Margin = new Thickness(22, 18, 22, 18);
            body.Focusable = true;
            Content = body;
            Loaded += (s, e) => body.Focus();
            PreviewKeyDown += RecordDown;
            PreviewKeyUp += RecordUp;
            // Alt+F4 is a key to record here, not a way to close the window.
            Closing += (s, e) => { if (!closing && (Keyboard.Modifiers & ModifierKeys.Alt) != 0 && Keyboard.IsKeyDown(Key.F4)) e.Cancel = true; };
        }

        static Button Button(string label, Action click, string style)
        {
            var b = Ui.Button(label, click, style);
            b.Focusable = false;
            b.MinWidth = 72;
            return b;
        }

        static ushort Vk(KeyEventArgs e)
        {
            Key k = e.Key == Key.System ? e.SystemKey : e.Key == Key.ImeProcessed ? e.ImeProcessedKey : e.Key == Key.DeadCharProcessed ? e.DeadCharProcessedKey : e.Key;
            int vk = KeyInterop.VirtualKeyFromKey(k);
            switch (vk)
            {
                // Left and right modifiers both record as the plain Shift, Ctrl, Alt and Win (the left keys, as Super Macro's CTRL is).
                case 0x10: case 0xA0: case 0xA1: return 0xA0;   // Shift
                case 0x11: case 0xA2: case 0xA3: return 0xA2;   // Ctrl
                case 0x12: case 0xA4: case 0xA5: return 0xA4;   // Alt
                case 0x5C: return 0x5B;                         // Win
            }
            return (ushort)vk;
        }

        static readonly ushort[] ModifierOrder = { 0xA2, 0xA0, 0xA4, 0x5B };

        void RecordDown(object sender, KeyEventArgs e)
        {
            e.Handled = true;
            if (e.IsRepeat) return;
            ushort vk = Vk(e);
            if (vk == 0) return;
            if (KeyNames.IsModifier(vk))
            {
                if (!held.Contains(vk)) held.Add(vk);
                lone = vk;
                return;
            }
            lone = 0;
            var keys = ModifierOrder.Where(held.Contains).ToList();
            keys.Add(vk);
            Add(keys);
        }

        void RecordUp(object sender, KeyEventArgs e)
        {
            e.Handled = true;
            ushort vk = Vk(e);
            if (!KeyNames.IsModifier(vk)) return;
            if (lone == vk) Add(new List<ushort> { vk });
            lone = 0;
            held.Remove(vk);
        }

        void Add(List<ushort> keys)
        {
            DateTime now = DateTime.UtcNow;
            if (withPauses.IsChecked == true && steps.Count > 0)
            {
                int ms = (int)Math.Round((now - last).TotalMilliseconds / 10.0) * 10;
                if (ms >= 10) steps.Add("wait " + PlainMacro.Ms(Math.Min(ms, 3600000)));
            }
            last = now;
            steps.Add(PlainMacro.KeysLine(keys));
            ShowSteps();
        }

        void ShowSteps()
        {
            preview.Children.Clear();
            if (steps.Count == 0) { preview.Children.Add(empty); return; }
            PlainResult r = PlainMacro.ToScript(string.Join("\n", steps));
            foreach (PlainLine line in r.Lines)
            {
                bool wait = line.Kind == PlainKind.Wait;
                var face = new Border { CornerRadius = new CornerRadius(7), Padding = wait ? new Thickness(8, 3, 8, 3) : new Thickness(12, 5, 12, 5), Margin = new Thickness(0, 0, 6, 6),
                    Child = Ui.Text(PlainMacro.TileLabel(line), wait ? "Muted" : "Text", !wait, wait ? 11.5 : 13), VerticalAlignment = VerticalAlignment.Center };
                Ui.Ref(face, Border.BackgroundProperty, wait ? "Card2" : "KeySwitchBg");
                preview.Children.Add(face);
            }
        }

        void Done()
        {
            Lines = Collect();
            closing = true;
            DialogResult = true;
        }

        /// <summary>The same key pressed several times in a row becomes one "Down 5 times" step.</summary>
        static List<string> Squash(List<string> steps)
        {
            var result = new List<string>();
            for (int i = 0; i < steps.Count; )
            {
                int j = i + 1;
                while (j < steps.Count && steps[j] == steps[i]) j++;
                int count = j - i;
                result.Add(count > 1 && !steps[i].StartsWith("{{", StringComparison.Ordinal) ? steps[i] + " " + count + " times" : steps[i]);
                if (count > 1 && steps[i].StartsWith("{{", StringComparison.Ordinal)) for (int k = 1; k < count; k++) result.Add(steps[i]);
                i = j;
            }
            return result;
        }

        // self test
        public void Press(params Key[] keys)
        {
            // Modifiers go down first and come up last, as a person presses a shortcut.
            foreach (Key k in keys) PressKey(k, true);
            foreach (Key k in keys.Reverse()) PressKey(k, false);
        }

        void PressKey(Key k, bool down)
        {
            var source = PresentationSource.FromVisual(this);
            if (source == null) return;
            var args = new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, k) { RoutedEvent = down ? Keyboard.PreviewKeyDownEvent : Keyboard.PreviewKeyUpEvent };
            if (down) RecordDown(this, args); else RecordUp(this, args);
        }

        string Collect() { return string.Join("\r\n", withPauses.IsChecked == true ? steps : Squash(steps)); }

        /// <summary>Self test: what Done gives, without closing (the window is not a dialog in the test).</summary>
        public void Finish() { Lines = Collect(); }
    }

    /// <summary>Asked once, on the first launch: which macro editor do you prefer? Settings can change it later.</summary>
    public sealed class EditorChoiceWindow : Window
    {
        /// <summary>"Text" or "Tiles"; "Text" when the window is closed without a choice.</summary>
        public string Choice { get; private set; }

        public EditorChoiceWindow(Window owner)
        {
            Choice = EditorKinds.Text;
            Title = "Welcome to Macro Kit";
            Width = 760;
            SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            if (owner != null && owner.IsVisible) { Owner = owner; WindowStartupLocation = WindowStartupLocation.CenterOwner; }
            else WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Theme.Style(this);

            var heading = Ui.Heading("How would you like to make macros?", "H1");
            var intro = Ui.Para("Pick the editor that suits you. Both make the same macros, and you can switch whenever you like.", "Muted");

            var typed = Ui.Stack(4,
                Mono("ctrl+c", "Press Ctrl+C"), Mono("wait 50ms", "Wait 50 ms"), Mono("alt+tab", "Press Alt+Tab"), Mono("type \"hello\"", "Type “hello”"));
            var tileRow = new WrapPanel();
            foreach (string t in new[] { "Ctrl + C", "50 ms", "Alt + Tab", "“hello”", "+" })
            {
                bool wait = t == "50 ms", plus = t == "+";
                var face = new Border { CornerRadius = new CornerRadius(7), Padding = wait ? new Thickness(8, 3, 8, 3) : new Thickness(12, 5, 12, 5), Margin = new Thickness(0, 0, 6, 6),
                    Child = Ui.Text(t, plus ? "AccentText" : wait ? "Muted" : "Text", !wait, wait ? 11.5 : 13), VerticalAlignment = VerticalAlignment.Center };
                Ui.Ref(face, Border.BackgroundProperty, plus ? "Card2" : wait ? "Card2" : t.StartsWith("“", StringComparison.Ordinal) ? "KeyClipBg" : "KeySwitchBg");
                tileRow.Children.Add(face);
            }

            var cards = new Grid();
            cards.ColumnDefinitions.Add(new ColumnDefinition());
            cards.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
            cards.ColumnDefinitions.Add(new ColumnDefinition());
            var a = Option("Type it", "Write each step on its own line, the way you'd say it. Macro Kit shows what each line does as you type, and suggests the rest.", typed, EditorKinds.Text);
            var b = Option("Key tiles", "Build the macro from tiles like Ctrl + C and Alt + Tab. Press + to record keys, or add text, actions, apps and delays.", tileRow, EditorKinds.Tiles);
            cards.Children.Add(a);
            Grid.SetColumn(b, 2);
            cards.Children.Add(b);

            var later = Ui.Row(7, Ui.Glyph(Ui.GlyphInfo, 13, "Info"), Ui.Text("You can change this any time in Settings.", "Muted", false, 12.5));
            var body = Ui.Stack(12, heading, intro, cards, later);
            ((FrameworkElement)body.Children[2]).Margin = new Thickness(0, 18, 0, 0);
            body.Margin = new Thickness(28, 22, 28, 24);
            Content = body;
        }

        static FrameworkElement Mono(string code, string meaning)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            var c = Ui.Text(code, "Text", false, 12.5);
            c.FontFamily = Ui.MonoFont;
            g.Children.Add(c);
            var m = Ui.Text(meaning, "Muted", false, 12);
            Grid.SetColumn(m, 1);
            g.Children.Add(m);
            return g;
        }

        FrameworkElement Option(string title, string text, FrameworkElement sample, string kind)
        {
            var sampleFrame = new Border { CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Padding = new Thickness(12, 10, 12, 6), Child = sample, Height = 108 };
            Ui.Ref(sampleFrame, Border.BackgroundProperty, "Input");
            Ui.Ref(sampleFrame, Border.BorderBrushProperty, "Border");
            var choose = Ui.Button("Use " + title.ToLowerInvariant(), () => { Choice = kind; DialogResult = true; }, "Primary");
            choose.HorizontalAlignment = HorizontalAlignment.Left;
            AutomationProperties.SetName(choose, "Use " + title);
            var stack = Ui.Stack(10, Ui.Heading(title), sampleFrame, Ui.Para(text, "Text", 12.5), choose);
            return Ui.Card(stack);
        }
    }
}
