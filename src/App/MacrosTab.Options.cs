using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using MacroKit.Core;

namespace MacroKit.App
{
    /// <summary>
    /// The Macros tab, part 4: the options strip. Four chips (Repeat, Window, Start, Schedule) say how the macro runs; a click opens
    /// only that chip's card under them, and a second click closes it. The open one is remembered in Settings.MacroOptionsSection.
    /// </summary>
    public sealed partial class MacrosTab
    {
        /// <summary>The sections, in chip order; the names are what Settings.MacroOptionsSection keeps.</summary>
        public static readonly string[] OptionSections = { "repeat", "window", "start", "schedule" };
        static readonly string[] OptionTitles = { "Repeat", "Window", "Start", "Schedule" };
        static readonly string[] OptionTips =
        {
            "How many times it runs, the pause between runs and after each key, and whether text is pasted.",
            "Which window it types into, and where Alt+Tab goes.",
            "Its hotkey and its Deck button, and a switch to pause both hotkey and schedule.",
            "Run it by itself at set times (Macro Kit Pro).",
        };

        FrameworkElement[] optionCards;
        readonly Button[] optionChips = new Button[4];
        readonly TextBlock[] optionValues = new TextBlock[4];

        /// <summary>The strip of chips; <paramref name="cards"/> are the four cards in chip order, shown one at a time under it.</summary>
        FrameworkElement BuildOptions(params FrameworkElement[] cards)
        {
            optionCards = cards;
            var label = Ui.Text("Options", "Text", true, 13);
            label.Margin = new Thickness(0, 0, 10, 5);
            var strip = Ui.Wrap(label);
            for (int i = 0; i < OptionSections.Length; i++)
            {
                int index = i;
                var title = Ui.Text(OptionTitles[i], "Muted", false, 12);
                var value = Ui.Text("", "Text", true, 12);
                value.MaxWidth = 190;
                optionValues[i] = value;
                var chip = Ui.Button(Ui.Row(6, title, value), () => ToggleOptionSection(OptionSections[index]), "Chip", OptionTips[i]);
                chip.Padding = new Thickness(10, 4, 10, 4);
                chip.Margin = new Thickness(0, 0, 6, 5);
                optionChips[i] = chip;
                strip.Children.Add(chip);
            }
            var card = Ui.Card(strip);
            card.Padding = new Thickness(14, 10, 14, 5);
            return card;
        }

        /// <summary>Opens a section's card, or closes it when it is the one already open.</summary>
        void ToggleOptionSection(string section)
        {
            c.Settings.MacroOptionsSection = OpenSection == section ? "" : section;
            c.MarkDirty(SaveParts.Settings);
            ShowOptionCard();
        }

        /// <summary>The section whose card is open, or "" (an unknown name from a newer version counts as none).</summary>
        string OpenSection { get { string s = c.Settings.MacroOptionsSection ?? ""; return OptionSections.Contains(s) ? s : ""; } }

        void ShowOptionCard()
        {
            if (optionCards == null) return;
            string open = OpenSection;
            for (int i = 0; i < optionCards.Length; i++)
            {
                bool shown = OptionSections[i] == open;
                optionCards[i].Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
                MarkChosen(optionChips[i], shown);
            }
        }

        /// <summary>Each chip's value: "Once", "Previous window", "Ctrl+Alt+1 · Deck", "Off".</summary>
        void UpdateOptionsSummary()
        {
            MacroDef m = current;
            string[] values = m == null ? new[] { "", "", "", "" } : OptionValues(m, Edition.IsPro);
            for (int i = 0; i < values.Length; i++)
            {
                optionValues[i].Text = values[i];
                AutomationProperties.SetName(optionChips[i], OptionTitles[i] + ": " + values[i]);
            }
        }

        /// <summary>The four chip values for a macro, in chip order. Pure, for the self test.</summary>
        public static string[] OptionValues(MacroDef m, bool pro)
        {
            string repeat = m.Repeat == RepeatMode.Once ? "Once" : m.Repeat == RepeatMode.Times ? m.RepeatTimes + " times" : "Continuously";
            if (m.PasteText) repeat += " · pastes";

            string window;
            if (m.Target == TargetKind.PreviousWindow) window = "Previous window";
            else
            {
                WindowMatch w = m.TargetWindow ?? new WindowMatch();
                window = !string.IsNullOrWhiteSpace(w.TitleContains) ? "“" + w.TitleContains.Trim() + "”"
                    : !string.IsNullOrWhiteSpace(w.ProcessName) ? w.ProcessName.Trim() : "Not chosen yet";
            }

            var start = new List<string>();
            if (!string.IsNullOrEmpty(m.Hotkey)) start.Add(m.Hotkey);
            if (m.ShowOnDeck) start.Add("Deck");
            string launch = start.Count == 0 ? "Run button" : string.Join(" · ", start);
            if (!m.Enabled && !string.IsNullOrEmpty(m.Hotkey)) launch = "Hotkey off";

            bool scheduled = m.Schedule != null && m.Schedule.Kind != ScheduleKind.Off;
            string when = !scheduled ? "Off" : !pro ? "Needs Pro" : !m.Enabled ? "Paused" : m.Schedule.Describe();
            return new[] { repeat, window, launch, when };
        }
    }
}
