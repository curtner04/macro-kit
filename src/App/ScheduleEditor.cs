using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using MacroKit.Core;

namespace MacroKit.App
{
    /// <summary>The Macros tab's "Run on a schedule" card (Macro Kit Pro).</summary>
    public sealed class ScheduleEditor
    {
        public readonly Border Root;
        readonly AppController c;
        readonly ComboBox kind = new ComboBox { Width = 230 };
        readonly TextBox time = new TextBox { Width = 64 };
        readonly CheckBox[] days = new CheckBox[7];
        readonly NumberBox minutes = new NumberBox(30, MacroSchedule.MinMinutes, MacroSchedule.MaxMinutes, 60);
        readonly TextBox at = new TextBox { Width = 140 };
        readonly FrameworkElement dailyRow, everyRow, onceRow;
        readonly StackPanel editors;
        readonly StackPanel proNote;
        readonly TextBlock status = Ui.Text("", "Muted", false, 12, true);
        MacroDef macro;
        Action changed;
        bool loading;

        public ScheduleEditor(AppController controller, Func<Window> owner)
        {
            c = controller;
            foreach (string label in new[] { "Off: only when I start it", "Every day, or on chosen days", "Every few minutes", "Once, at a date and time" }) kind.Items.Add(label);
            kind.SelectionChanged += (s, e) => Change(x => x.Kind = (ScheduleKind)Math.Max(0, kind.SelectedIndex));

            time.ToolTip = "24-hour time, for example 09:00 or 17:30.";
            time.TextChanged += (s, e) => Change(x => x.Time = time.Text.Trim());
            var dayRow = new StackPanel { Orientation = Orientation.Horizontal };
            string[] names = { "Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat" };
            foreach (int day in new[] { 1, 2, 3, 4, 5, 6, 0 })
            {
                int bit = 1 << day;
                var box = new CheckBox { Content = names[day], Margin = new Thickness(0, 0, 10, 0) };
                box.Click += (s, e) => Change(x => x.Days = box.IsChecked == true ? x.Days | bit : x.Days & ~bit);
                days[day] = box;
                dayRow.Children.Add(box);
            }
            dailyRow = Ui.Stack(6, Ui.Row(8, Ui.Text("At", "Muted", false, 12), time), dayRow);

            minutes.Changed += () => Change(x => x.EveryMinutes = minutes.Value);
            everyRow = Ui.Row(8, Ui.Text("Every", "Muted", false, 12), minutes, Ui.Text("minutes", "Muted", false, 12));

            at.ToolTip = "Date and 24-hour time, for example 2026-12-24 08:00.";
            at.TextChanged += (s, e) => Change(x => x.At = at.Text.Trim());
            onceRow = Ui.Row(8, Ui.Text("On", "Muted", false, 12), at, Ui.Text("(year-month-day hour:minute)", "Muted", false, 12));

            editors = Ui.Stack(8, kind, dailyRow, everyRow, onceRow);

            proNote = Ui.Stack(6,
                Ui.Para(Edition.ProOnly("Scheduled macros"), "Muted", 12),
                Ui.Row(8,
                    Ui.Button("Enter license key", () => new LicenseWindow(owner(), c).ShowDialog(), "Small"),
                    ProPage.GetProButton()));

            var badge = Ui.Pill("PRO", "Good", "GoodBg", 10.5);
            badge.VerticalAlignment = VerticalAlignment.Center;
            Root = Ui.Card(Ui.Stack(8, Ui.Row(8, Ui.Heading("Run on a schedule"), badge), proNote, editors, status));
            Edition.Changed += Refresh;
            Refresh();
        }

        public void Bind(MacroDef m, Action onChanged)
        {
            macro = m;
            changed = onChanged;
            loading = true;
            MacroSchedule s = m == null ? new MacroSchedule() : m.Schedule ?? (m.Schedule = new MacroSchedule());
            kind.SelectedIndex = (int)s.Kind;
            time.Text = s.Time ?? "";
            for (int day = 0; day < 7; day++) days[day].IsChecked = (s.Days & (1 << day)) != 0;
            minutes.Value = Math.Max(MacroSchedule.MinMinutes, Math.Min(MacroSchedule.MaxMinutes, s.EveryMinutes));
            at.Text = string.IsNullOrEmpty(s.At) ? DateTime.Now.AddHours(1).ToString("yyyy-MM-dd HH:00", CultureInfo.InvariantCulture) : s.At;
            loading = false;
            Refresh();
        }

        void Change(Action<MacroSchedule> edit)
        {
            if (loading || macro == null) return;
            if (macro.Schedule == null) macro.Schedule = new MacroSchedule();
            edit(macro.Schedule);
            // A one-off picks up the suggested time shown in the box when it is switched on.
            if (macro.Schedule.Kind == ScheduleKind.Once && string.IsNullOrEmpty(macro.Schedule.At)) macro.Schedule.At = at.Text.Trim();
            if (changed != null) changed();
            Refresh();
        }

        public void Refresh()
        {
            bool pro = Edition.IsPro;
            proNote.Visibility = pro ? Visibility.Collapsed : Visibility.Visible;
            editors.IsEnabled = pro && macro != null;
            editors.Visibility = pro ? Visibility.Visible : Visibility.Collapsed;   // no dead controls under the Pro note
            ScheduleKind k = macro == null || macro.Schedule == null ? ScheduleKind.Off : macro.Schedule.Kind;
            dailyRow.Visibility = k == ScheduleKind.Daily ? Visibility.Visible : Visibility.Collapsed;
            everyRow.Visibility = k == ScheduleKind.Every ? Visibility.Visible : Visibility.Collapsed;
            onceRow.Visibility = k == ScheduleKind.Once ? Visibility.Visible : Visibility.Collapsed;
            status.Text = StatusText(pro, k);
            string last = pro && macro != null && macro.Schedule != null && k != ScheduleKind.Off ? macro.Schedule.DescribeLastRun() : "";
            if (last.Length > 0) status.Text += " " + last;
        }

        /// <summary>Self-test: the line under the schedule.</summary>
        public string StatusLine { get { return status.Text; } }

        string StatusText(bool pro, ScheduleKind k)
        {
            if (!pro || macro == null) return "";
            if (k == ScheduleKind.Off) return "This macro runs only when you start it.";
            if (!macro.Enabled) return macro.Schedule.Describe() + ". Paused: this macro is switched off, so it does not run by itself.";
            DateTime? next = c.NextScheduledRun(macro);
            if (next == null)
            {
                switch (k)
                {
                    case ScheduleKind.Daily: return "Type the time as hours:minutes (for example 09:00) and tick at least one day.";
                    case ScheduleKind.Once:
                        DateTime when;
                        return MacroSchedule.TryParseAt(macro.Schedule.At, out when) ? "That time has passed. Pick a time in the future." : "Type the date and time like 2026-12-24 08:00.";
                    default: return "";
                }
            }
            string where = macro.Target == TargetKind.SpecificWindow
                ? "It runs in the target window chosen in Options, Window."
                : "It types into whatever window is in front at that moment. To be safe, choose a target window in Options, Window.";
            return macro.Schedule.Describe() + ". Next run " + next.Value.ToString("ddd d MMM 'at' HH:mm", CultureInfo.InvariantCulture)
                + ". Scheduled runs happen only while Macro Kit is running (it can wait in the tray). " + where;
        }
    }
}
