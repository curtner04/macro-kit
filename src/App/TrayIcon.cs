using System;
using System.Drawing;
using System.Windows.Forms;

namespace MacroKit.App
{
    /// <summary>The notification-area icon and its menu. No balloon notifications are ever shown.</summary>
    public sealed class TrayIcon : IDisposable
    {
        readonly NotifyIcon icon;
        readonly ContextMenuStrip menu = new ContextMenuStrip();
        readonly AppController c;
        readonly AppShell shell;

        public TrayIcon(AppController controller, AppShell shell)
        {
            c = controller;
            this.shell = shell;
            icon = new NotifyIcon { Text = "Macro Kit", Visible = false, ContextMenuStrip = menu, Icon = LoadIcon() };
            menu.Opening += (s, e) => Rebuild();
            // While recording, the tray is Macro Kit's own: the right-click that opened the menu is taken back out of the take,
            // and clicks on the menu are left out like clicks on the recorder bar, so a replay never opens this menu or presses Stop.
            menu.Opened += (s, e) => { c.ForgetTrayClicks(Core.MouseButton.Right, 1); shell.AddOwnWindow(menu.Handle); };
            menu.Closed += (s, e) => shell.RemoveOwnWindow(menu.Handle);
            icon.DoubleClick += (s, e) => { c.ForgetTrayClicks(Core.MouseButton.Left, 2); shell.OpenMain(); };
            icon.Visible = true;
        }

        static Icon LoadIcon()
        {
            try
            {
                using (var process = System.Diagnostics.Process.GetCurrentProcess())
                {
                    Icon own = Icon.ExtractAssociatedIcon(process.MainModule.FileName);
                    if (own != null) return own;
                }
            }
            catch (Exception) { }
            return SystemIcons.Application;
        }

        void Rebuild()
        {
            menu.Items.Clear();
            var open = new ToolStripMenuItem("Open Macro Kit", null, (s, e) => shell.OpenMain());
            open.Font = new Font(open.Font, FontStyle.Bold);
            menu.Items.Add(open);
            menu.Items.Add(new ToolStripMenuItem(c.Settings.DeckVisible ? "Hide Deck" : "Show Deck", null, (s, e) => c.ToggleDeck()));
            menu.Items.Add(new ToolStripMenuItem("Stop", null, (s, e) => c.Stop()) { Enabled = c.IsBusy });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("Expand phrases as I type", null, (s, e) => c.SetPhrasesEnabled(!c.Settings.ExpansionEnabled)) { Checked = c.Settings.ExpansionEnabled });
            menu.Items.Add(new ToolStripSeparator());
            // Restart waits for a quiet moment; greyed out, a stray click during a run can't open a message over the program it runs in.
            menu.Items.Add(new ToolStripMenuItem("Restart", null, (s, e) => shell.Restart()) { Enabled = !c.IsBusy });
            menu.Items.Add(new ToolStripMenuItem("Exit Macro Kit", null, (s, e) => shell.Exit()));
        }

        public void Dispose()
        {
            icon.Visible = false;
            icon.Dispose();
            menu.Dispose();
        }
    }
}
