using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
                    var t = Ui.Para(line.Substring(2), "Text");
                    Grid.SetColumn(t, 1);
                    row.Children.Add(t);
                    stack.Children.Add(row);
                }
                else
                {
                    var p = Ui.Para(line, "Muted");
                    p.Margin = new Thickness(0, 2, 0, 2);
                    stack.Children.Add(p);
                }
            }
            Content = new ScrollViewer { Content = stack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            PreviewKeyDown += (s, e) => { if (e.Key == Key.Escape) { e.Handled = true; Close(); } };
        }
    }
}
