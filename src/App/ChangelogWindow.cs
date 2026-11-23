using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Text.RegularExpressions;
using AppShared;

namespace MacroKit.App
{
    /// <summary>Shows the embedded CHANGELOG.md (dated version history) as headings and bullet points.</summary>
    public sealed class ChangelogWindow : Window
    {
        public ChangelogWindow(Window owner)
        {
            Title = "Macro Kit " + Release.Version + " — changelog";
            Width = 640;
            Height = 560;
            MinWidth = 420;
            MinHeight = 320;
            ShowInTaskbar = false;
            if (owner != null && owner.IsVisible) { Owner = owner; WindowStartupLocation = WindowStartupLocation.CenterOwner; }
            else WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Theme.Style(this);

            string text;
            try { text = ReleaseNotes.Read(); }
            catch (Exception ex) { text = "The changelog could not be read: " + ex.Message; }

            var stack = new StackPanel { Margin = new Thickness(26, 20, 26, 24) };
            foreach (string raw in text.Replace("\r", "").Split('\n'))
            {
                string line = raw.TrimEnd();
                if (line.Length == 0) continue;
                if (line.StartsWith("# ", StringComparison.Ordinal)) stack.Children.Add(Ui.Heading(line.Substring(2), "H1"));
                else if (line.StartsWith("## ", StringComparison.Ordinal))
                {
                    var h = Ui.Heading(line.Substring(3));
                    h.Margin = new Thickness(0, 18, 0, 6);
                    stack.Children.Add(h);
                }
                else if (line.StartsWith("- ", StringComparison.Ordinal))
                {
                    var row = new Grid { Margin = new Thickness(4, 2, 0, 2) };
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
                    row.ColumnDefinitions.Add(new ColumnDefinition());
                    var bullet = Ui.Text("•", "Muted");
                    bullet.VerticalAlignment = VerticalAlignment.Top;
                    row.Children.Add(bullet);
                    var t = Ui.Para("", "Text");
                    AddInlines(t, line.Substring(2));
                    Grid.SetColumn(t, 1);
                    row.Children.Add(t);
                    stack.Children.Add(row);
                }
                else
                {
                    // A line that is only **bold** is a group title inside a version: normal text colour, with room above it.
                    bool group = line.Length > 4 && line.StartsWith("**", StringComparison.Ordinal)
                        && line.IndexOf("**", 2, StringComparison.Ordinal) == line.Length - 2;
                    var p = Ui.Para("", group ? "Text" : "Muted");
                    AddInlines(p, line);
                    p.Margin = group ? new Thickness(0, 12, 0, 3) : new Thickness(0, 2, 0, 2);
                    if (group) p.Tag = "Group";
                    stack.Children.Add(p);
                }
            }
            Content = new ScrollViewer { Content = stack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            PreviewKeyDown += (s, e) => { if (e.Key == Key.Escape) { e.Handled = true; Close(); } };
        }

        // **bold**, *italic* and `code` become real formatting; a marker with no closing partner stays literal text.
        static readonly Regex Marks = new Regex(@"(\*\*[^*]+\*\*|`[^`]+`|\*[^*\s][^*]*\*)");

        internal static void AddInlines(TextBlock t, string text)
        {
            t.Text = "";
            int at = 0;
            foreach (Match m in Marks.Matches(text))
            {
                if (m.Index > at) t.Inlines.Add(new Run(text.Substring(at, m.Index - at)));
                string v = m.Value;
                if (v.StartsWith("**", StringComparison.Ordinal)) t.Inlines.Add(new Run(v.Substring(2, v.Length - 4)) { FontWeight = FontWeights.SemiBold });
                else if (v[0] == '`')
                {
                    var code = new Run(v.Substring(1, v.Length - 2)) { FontFamily = Ui.MonoFont };
                    code.SetResourceReference(TextElement.ForegroundProperty, "AccentText");
                    t.Inlines.Add(code);
                }
                else t.Inlines.Add(new Run(v.Substring(1, v.Length - 2)) { FontStyle = FontStyles.Italic });
                at = m.Index + m.Length;
            }
            if (at < text.Length) t.Inlines.Add(new Run(text.Substring(at)));
        }
    }
}
