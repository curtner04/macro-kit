# Macro Kit changelog

## 1.0.1 - 2026-11-12

- Macros: the Keys part of the command palette is grouped into labelled rows, each in its own colour: Clipboard (Copy, Cut, Paste, Select all), Switch (Alt+Tab, Ctrl+Tab), Edit (Enter, Tab, Esc, Backspace, Delete), Arrows, Jump (Home, End, PgUp, PgDn) and F keys. The Undo and Save buttons are gone; type {{CTRL}{Z}} or {{CTRL}{S}}, or use Your own shortcut.

## 1.0.0 - 2026-11-12

First release.

- Recorder: record mouse and keyboard, save recordings, rename, delete, export and import them, and play one back at 0.1x to 100x speed, once, a set number of times or continuously, with global hotkeys. An unsaved recording is kept when Macro Kit closes.
- Phrases: type a trigger such as /shrug in any program and it is replaced with the text you saved. Expand right away or after a space or punctuation, type or paste the text, match case, per-phrase on/off, programs where phrases never expand, and a warning when PhraseExpress is also running.
- Macros: write Super Macro scripts such as {{CTRL}{C}}{{PAUSE:50}}{{ALT}{TAB}} or click commands together with the command palette, see the steps and problems live with an estimated time, and run a script once, a set number of times or continuously.
- Macros run in the window you choose and never in Macro Kit itself: Macro Kit steps aside before a run, can pick the window by clicking it ("Pick"), and Alt+Tab can jump straight between two windows you name. A macro stops with a clear message if its window is missing or loses focus.
- Deck: a small always-on-top button panel for your macros and recording that never takes focus, plus a status bar with a Stop button while something runs.
- Hotkeys for record, play, stop, the Deck and each macro; Esc stops playback and macros.
- Picking a window (or choosing one from the list) remembers the program and shows the title as a hint, so a macro for Excel keeps working when you open a different workbook.
- After a run Macro Kit returns only if it was on screen before, without taking focus: minimized stays minimized, and it sits behind the window you are working in (or at the very back after a Deck, hotkey or tray run).
- A phrase that pastes keeps your whole clipboard (text, pictures, files) and stays out of the Windows clipboard history and cloud sync. Phrases pause while a macro or playback runs.
- Warns when the window you record or play in runs as administrator and Macro Kit does not. A macro, recording or playback started from a hotkey or the Deck that cannot start says why on the status bar.
- Safer leaving and restarting: Exit asks before discarding work that could not be saved, Restart keeps a custom data folder and starting in the tray, and a data file that is locked at startup is never overwritten.
- Export / import configuration, Restart, tray icon, Start with Windows, light and dark themes.
