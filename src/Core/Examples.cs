using System;
using System.Collections.Generic;
using System.Linq;

namespace MacroKit.Core
{
    /// <summary>
    /// Ten example phrases and ten example macros, so the Phrases and Macros tabs show what they can do on first look. A new install
    /// starts with them; an existing one gets the missing ones once (AppSettings.ExamplesAdded). An example still exactly as shipped
    /// can be removed in one go; one you changed is yours and stays.
    /// </summary>
    public static class Examples
    {
        public const string PhraseGroup = "Examples";

        public static List<PhraseDef> Phrases()
        {
            return new List<PhraseDef>
            {
                P(";sig", "Best regards,\nAlex Morgan\nalex@example.com"),
                P(";addr", "Alex Morgan\n123 Main Street\nSpringfield, IL 62701"),
                P(";em", "alex@example.com"),
                P(";date", "{date}"),
                P(";stamp", "{date:yyyy-MM-dd} {time:HH:mm}"),
                P(";ty", "Thanks so much for your help! I really appreciate it."),
                P(";meet", "Hi {fill:Name},\n\nAre you free for a quick call on {fill:Day}? {cursor}\n\nThanks,\nAlex"),
                P(";link", "<a href=\"{clipboard}\">{cursor}</a>"),
                P(";hours", "Our opening hours:\nMonday to Friday: 9:00 to 17:00\nSaturday: 10:00 to 14:00\nSunday: closed", InsertMethod.Paste),
                P("teh", "the", InsertMethod.Type, ExpandWhen.AfterDelimiter),
            };
        }

        public static List<MacroDef> Macros()
        {
            return new List<MacroDef>
            {
                M("Copy, switch, paste, next row",
                    "{{CTRL}{C}}{{PAUSE:50}}{{ALT}{TAB}}{{PAUSE:100}}{{CTRL}{V}}{{PAUSE:50}}{{ALT}{TAB}}{{PAUSE:100}}{{DOWN}}{{PAUSE:50}}",
                    RepeatMode.Times, 5, 0, true),
                M("Type my email signature", "Best regards,\nAlex Morgan\nalex@example.com", paste: true),
                M("Fill a contact form", "Alex Morgan{{TAB}}alex@example.com{{TAB}}555-0100{{TAB}}"),
                M("Auto-clicker (Esc stops)", "{{LBUTTON}}{{PAUSE:100}}", RepeatMode.Continuous),
                M("Refresh every 30 seconds", "{{F5}}", RepeatMode.Continuous, 5, 30000),
                M("Paste down 10 rows", "{{CTRL}{C}}{{PAUSE:30}}{{LOOP:10}}{{DOWN}}{{CTRL}{V}}{{PAUSE:30}}{{ENDLOOP}}"),
                M("Duplicate the line", "{{HOME}}{{SHIFT}{END}}{{CTRL}{C}}{{PAUSE:30}}{{END}}{{ENTER}}{{CTRL}{V}}"),
                M("Snip part of the screen", "{{WIN}{SHIFT}{S}}"),
                M("Start a note in Notepad", "{{RUN:notepad}}{{WAITWINDOW:Notepad}}{{PAUSE:300}}To do:\n- "),
                M("Reopen the closed browser tab", "{{CTRL}{SHIFT}{T}}"),
            };
        }

        /// <summary>
        /// Adds the examples that are not there yet: a phrase with the same trigger or a macro with the same name counts as there.
        /// Returns how many were added.
        /// </summary>
        public static int AddMissing(IList<PhraseDef> phrases, IList<MacroDef> macros)
        {
            int added = 0;
            var triggers = new HashSet<string>(phrases.Select(p => p.Trigger ?? ""), StringComparer.Ordinal);
            foreach (PhraseDef p in Phrases())
                if (triggers.Add(p.Trigger)) { phrases.Add(p); added++; }
            var names = new HashSet<string>(macros.Select(m => m.Name ?? ""), StringComparer.OrdinalIgnoreCase);
            foreach (MacroDef m in Macros())
                if (names.Add(m.Name))
                {
                    // Added to an existing setup, an example never lands on the person's Deck.
                    m.ShowOnDeck = false;
                    macros.Add(m);
                    added++;
                }
            return added;
        }

        /// <summary>True when the phrase is one of the examples, still exactly as shipped.</summary>
        public static bool IsUnchanged(PhraseDef phrase)
        {
            return phrase != null && Phrases().Any(e => e.Trigger == phrase.Trigger && e.Replacement == phrase.Replacement && e.Method == phrase.Method
                && e.When == phrase.When && e.MatchCase == phrase.MatchCase && (phrase.Group ?? "") == PhraseGroup);
        }

        /// <summary>True when the macro is one of the examples, still exactly as shipped (where it shows and its hotkey may differ).</summary>
        public static bool IsUnchanged(MacroDef macro)
        {
            return macro != null && string.IsNullOrEmpty(macro.Hotkey) && Macros().Any(e => e.Name == macro.Name && e.Script == macro.Script
                && e.Repeat == macro.Repeat && e.RepeatTimes == macro.RepeatTimes && e.RepeatDelayMs == macro.RepeatDelayMs && e.PasteText == macro.PasteText);
        }

        static PhraseDef P(string trigger, string replacement, InsertMethod method = InsertMethod.Type, ExpandWhen when = ExpandWhen.Immediately)
        {
            return new PhraseDef { Trigger = trigger, Replacement = replacement, Enabled = true, Method = method, When = when, Group = PhraseGroup };
        }

        static MacroDef M(string name, string script, RepeatMode repeat = RepeatMode.Once, int times = 5, int delayMs = 0, bool deck = false, bool paste = false)
        {
            return new MacroDef { Name = name, Script = script, Repeat = repeat, RepeatTimes = times, RepeatDelayMs = delayMs, ShowOnDeck = deck, PasteText = paste };
        }
    }
}
