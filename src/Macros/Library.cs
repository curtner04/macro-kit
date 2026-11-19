using System;
using System.Collections.Generic;
using MacroKit.Core;

namespace MacroKit.Macros
{
    /// <summary>One ready-made macro or phrase in a library pack.</summary>
    public sealed class LibraryItem
    {
        public string Name = "";
        public string Description = "";
        // A macro has a Script; a phrase has a Trigger and a Replacement.
        public string Script;
        public RepeatMode Repeat = RepeatMode.Once;
        public int RepeatTimes = 5;
        public int RepeatDelayMs;
        public int KeyDelayMs = 10;
        public string Trigger;
        public string Replacement;

        public bool IsPhrase { get { return Trigger != null; } }

        public MacroDef ToMacro()
        {
            return new MacroDef
            {
                Name = Name,
                Script = Script ?? "",
                Repeat = Repeat,
                RepeatTimes = RepeatTimes,
                RepeatDelayMs = RepeatDelayMs,
                KeyDelayMs = KeyDelayMs,
                ShowOnDeck = true,
            };
        }

        public PhraseDef ToPhrase()
        {
            return new PhraseDef { Trigger = Trigger, Replacement = Replacement ?? "" };
        }
    }

    public sealed class LibraryPack
    {
        public string Id = "";
        public string Name = "";
        public string Description = "";
        public bool Pro;
        public List<LibraryItem> Items = new List<LibraryItem>();
    }

    /// <summary>
    /// The macro library: ready-made macros and phrases to add with one click. Every pack is free since 1.5.2 (Pro stays on
    /// LibraryPack for a future pack); the Conditions examples still need Pro to run. Adding copies an item into your own
    /// macros or phrases, where you can change it like any other.
    /// </summary>
    public static class MacroLibrary
    {
        public static readonly IList<LibraryPack> Packs = Build();

        static LibraryItem Macro(string name, string description, string script, RepeatMode repeat = RepeatMode.Once, int repeatDelayMs = 0)
        {
            return new LibraryItem { Name = name, Description = description, Script = script, Repeat = repeat, RepeatDelayMs = repeatDelayMs };
        }

        static LibraryItem Phrase(string trigger, string replacement, string description)
        {
            return new LibraryItem { Name = trigger + "  →  " + replacement, Description = description, Trigger = trigger, Replacement = replacement };
        }

        static List<LibraryPack> Build()
        {
            return new List<LibraryPack>
            {
                new LibraryPack
                {
                    Id = "starter", Name = "Starter", Pro = false,
                    Description = "Everyday shortcuts to start from.",
                    Items =
                    {
                        Macro("Copy, switch, paste", "Copies the selection, Alt+Tabs to the other window and pastes.",
                            "{{CTRL}{C}}{{PAUSE:50}}{{ALT}{TAB}}{{PAUSE:150}}{{CTRL}{V}}"),
                        Macro("Select the whole line", "Home, then Shift+End.", "{{HOME}}{{SHIFT}{END}}"),
                        Macro("Duplicate the line", "Copies the current line and pastes it on a new line below.",
                            "{{HOME}}{{SHIFT}{END}}{{CTRL}{C}}{{PAUSE:30}}{{END}}{{ENTER}}{{CTRL}{V}}"),
                        Phrase(";shrug", "¯\\_(ツ)_/¯", "The shrug."),
                        Phrase(";ty", "Thank you!", "A quick thanks."),
                    },
                },
                new LibraryPack
                {
                    Id = "spreadsheets", Name = "Spreadsheets and forms", Pro = false,
                    Description = "Repeat work down a column, move data between two windows, fill forms.",
                    Items =
                    {
                        Macro("Paste down 10 rows", "Copies the cell and pastes it into the 10 cells below.",
                            "{{CTRL}{C}}{{PAUSE:30}}{{LOOP:10}}{{DOWN}}{{CTRL}{V}}{{PAUSE:30}}{{ENDLOOP}}"),
                        Macro("Move 5 cells to the other window", "Copies a cell, pastes it in the other window, comes back and goes down a row. Change $rows to do more.",
                            "{{VARSET:rows:5}}{{LOOP:$rows}}{{CTRL}{C}}{{PAUSE:50}}{{ALT}{TAB}}{{PAUSE:150}}{{CTRL}{V}}{{ENTER}}{{PAUSE:50}}{{ALT}{TAB}}{{PAUSE:150}}{{DOWN}}{{ENDLOOP}}"),
                        Macro("Next row, first column", "Enter, then Home: ready for the next row of typing.", "{{ENTER}}{{HOME}}"),
                        Macro("Tab through 6 form fields", "Presses Tab 6 times, a little apart.", "{{KEYPRESS:TAB:6:80}}"),
                    },
                },
                new LibraryPack
                {
                    Id = "browser", Name = "Browser", Pro = false,
                    Description = "Tabs, addresses and pages that refresh themselves.",
                    Items =
                    {
                        Macro("Refresh every 30 seconds", "Presses F5, waits 30 seconds, again until you stop it.", "{{F5}}", RepeatMode.Continuous, 30000),
                        Macro("Reopen the closed tab", "Ctrl+Shift+T.", "{{CTRL}{SHIFT}{T}}"),
                        Macro("Copy the page address", "Selects the address bar, copies it and goes back to the page.", "{{CTRL}{L}}{{PAUSE:80}}{{CTRL}{C}}{{PAUSE:30}}{{ESC}}"),
                    },
                },
                new LibraryPack
                {
                    Id = "clicker", Name = "Clicker", Pro = false,
                    Description = "Clicking for you, at a pace you choose.",
                    Items =
                    {
                        Macro("Auto-clicker", "Clicks 10 times a second where the mouse is, until you stop it (Esc).", "{{LBUTTON}}{{PAUSE:100}}", RepeatMode.Continuous),
                        Macro("Click 50 times", "50 clicks, 50 ms apart, then stops.", "{{LOOP:50}}{{LBUTTON}}{{PAUSE:50}}{{ENDLOOP}}"),
                        Macro("Nudge the mouse every minute", "Moves the mouse one pixel and back, once a minute.", "{{MOUSEMOVE:1,0}}{{PAUSE:100}}{{MOUSEMOVE:-1,0}}", RepeatMode.Continuous, 60000),
                    },
                },
                new LibraryPack
                {
                    Id = "conditions", Name = "Conditions", Pro = false,
                    Description = "Examples of {{IF}} and {{IFWINDOW}} to copy from. Adding them is free; conditions run with Macro Kit Pro.",
                    Items =
                    {
                        Macro("Paste only in Notepad", "Pastes when Notepad is in front and does nothing anywhere else.", "{{IFWINDOW:notepad}}{{CTRL}{V}}{{ENDIF}}"),
                        Macro("Open the copied link", "If the clipboard holds a link, opens it in a new browser tab.",
                            "{{VARSETFROMCLIPBOARD:clip}}{{IF:$clip~http}}{{CTRL}{T}}{{PAUSE:150}}{{CTRL}{V}}{{ENTER}}{{ENDIF}}"),
                        Macro("Say what the clipboard holds", "Types whether the clipboard holds a link, nothing, or other text.",
                            "{{VARSETFROMCLIPBOARD:clip}}{{IF:$clip~http}}A link{{ELSE}}{{IF:$clip=}}Nothing{{ELSE}}Some text{{ENDIF}}{{ENDIF}}"),
                        Macro("Type a countdown", "Types 3, 2, 1 and Go! using a loop and a variable.",
                            "{{VARSET:n:3}}{{LOOP:3}}{{$n}} {{IF:$n=3}}{{VARSET:n:2}}{{ELSE}}{{IF:$n=2}}{{VARSET:n:1}}{{ENDIF}}{{ENDIF}}{{ENDLOOP}}Go!"),
                    },
                },
                new LibraryPack
                {
                    Id = "symbols", Name = "Symbols", Pro = false,
                    Description = "Phrases for symbols that have no key.",
                    Items =
                    {
                        Phrase(";tm", "™", "Trade mark."),
                        Phrase(";copy", "©", "Copyright."),
                        Phrase(";deg", "°", "Degrees."),
                        Phrase(";->", "→", "Arrow."),
                        Phrase(";check", "✓", "Check mark."),
                        Phrase(";eur", "€", "Euro."),
                        Phrase(";half", "½", "One half."),
                        Phrase(";flip", "(╯°□°)╯︵ ┻━┻", "The table flip."),
                    },
                },
            };
        }
    }
}
