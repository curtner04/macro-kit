# Macro Kit changelog

## 1.5.1 - 2026-11-18

- **Record keystrokes** now records Alt+Tab, the Windows key, PrtSc, Alt+F4 and keys other programs use as hotkeys. While the recorder is in front it takes those keys before Windows does, so they become steps instead of switching windows or taking a screenshot. Only Ctrl+Alt+Delete and Win+L stay with Windows.
- The recorder's pauses checkbox is now a choice of three: **No delays**, **Standard delays (50 ms)** between steps, or **Real-time delays**, the pauses as you pressed them. Macro Kit remembers your choice.

## 1.5.0 - 2026-11-18

- A simpler Macros tab. **Type it**: write each step on its own line the way you'd say it (`ctrl+c`, `wait 50ms`, `alt+tab`, `type "hello"`, `repeat 3 times` … `end repeat`). Macro Kit shows what each line does beside it, marks lines it doesn't understand, and suggests the rest as you type (Tab accepts).
- **Key tiles**: build a macro from clean tiles such as **Ctrl + C**, **50 ms** and **“hello”**, with no key down and key up clutter. Press **+** to record keystrokes, add text, an action, a program to launch, a system action (media keys, volume, lock the PC) or a delay. Click a tile to change, move or delete it.
- **Record keys** turns the shortcuts you press into steps, one per shortcut (Ctrl+C, Alt+S), and can also record the pauses between them.
- The first time Macro Kit opens it asks which editor you'd like. Switch any time in Settings, or with the link at the top of the steps.
- The Super Macro code now hides behind the **Stream Deck** switch at the top of a macro. Turn it on to copy the code into a Super Macro action, or to edit it there; the steps follow your changes.
- The full list of commands is one click away (**Show all commands**), and Repeat, Where it runs, Start it with and Schedule fold into one **Options** line that sums them up.

## 1.4.0 - 2026-11-17

- Multi action, like Stream Deck's: repeat blocks are now free. Select steps in the script and press **Repeat** to run them a number of times, then repeat other steps another number of times (`{{LOOP:3}}…{{ENDLOOP}}`).
- **Take turns**: each time a macro reaches a `{{SWITCH}}…{{OR}}…{{ENDSWITCH}}` block it does the next choice, so one hotkey can mute and then unmute, or step through scenes. **Pick at random** (`{{RANDOM}}…{{ENDRANDOM}}`) does one choice at random. Free.
- **Run the macro** (`{{MACRO:name}}`) runs another of your macros in place, in the same window and with the same variables. Free.
- New free actions: open a program, file, folder or website (`{{RUN}}`), close a program (`{{CLOSE}}`), switch to a window (`{{FOCUS}}`) or wait for one to open (`{{WAITWINDOW}}`), set or change the volume (`{{VOLUME:50}}`, `{{VOLUME:+10}}`), play a sound file (`{{PLAYSOUND}}`) and lock the PC (`{{LOCK}}`). The media keys (play/pause, next, previous, mute, volume) have their own buttons.
- The commands under the script now sit in groups that fold open and closed: Keys, Multi action, Wait, Text, Mouse, Apps and websites, Windows, Media and sound, Variables, and Conditions. Macro Kit remembers which groups are open, and **Find a command** shows only the groups that match a word.
- The steps list indents the steps inside a block and points out a `{{MACRO}}` whose name doesn't match any of your macros.
- Conditions (`{{IF}}`, `{{IFWINDOW}}`) still come with Macro Kit Pro.

## 1.3.0 - 2026-11-17

- Macro Kit stays free, and everything it did before still works without paying. The new **Macro Kit Pro** adds four things, unlocked with a license key in Settings, About. Keys are checked on your PC and never sent anywhere.
- Pro: loops and conditions in macros. `{{LOOP:5}}…{{ENDLOOP}}` repeats steps (or `{{LOOP:$count}}`), `{{IF:$name=Sam}}…{{ELSE}}…{{ENDIF}}` compares text or numbers (=, !=, <, >, <=, >=, ~ for contains), and `{{IFWINDOW:Notepad}}` checks the window in front. Blocks can nest, and the steps list explains any mistake.
- Pro: scheduled macros. A macro can run every day at a time (on the days you tick), every few minutes, or once at a date and time, while Macro Kit is running. A run that finds Macro Kit busy waits up to 5 minutes, then skips.
- Macro library: the **Library** button on the Macros tab adds ready-made macros and phrases with one click. The Starter pack is free; the Spreadsheets and forms, Browser, Clicker, Loops and conditions, and Symbols packs come with Pro.
- Settings, About shows Free or Pro, and has **Check input**: Macro Kit types a short line into its own box and expands a test phrase, to confirm Windows lets it send keys and see typing on this PC.
- Global Keybinds lists a sister app only when it is installed or running on this PC.

## 1.2.0 - 2026-11-16

- Global Keybinds lists PowerToys (every switched-on tool's shortcuts, Command Palette and Keyboard Manager's shortcut remaps) and OBS Studio (the current profile's and scene collection's hotkeys) when they are installed.
- **Add app** on the Apps card: type in the shortcuts of any other program. Press or type the keys, say what each one does and whether it works in any window or only in that app. Added apps count in the list, the warnings and Is it free?, can be edited or deleted from their card, and move with Export / import.

## 1.1.2 - 2026-11-16

- The launcher checks for signed updates on a fresh start and activates a build after it starts successfully. Restart still relaunches the current build. Sign-in startup still opens in the tray.

## 1.1.1 - 2026-11-16

- Removed the retired macro editor from Global Keybinds. Jancy shortcuts are read from Jancy’s own keybinds file.

## 1.1.0 - 2026-11-16

- New Global Keybinds tab: every keyboard shortcut saved in your apps in one list (Macro Kit, Snipline, Note Keeper, Jancy, Record Keeper, Little Buttons, Storage Lens and Tweak Bench), read straight from each app's own settings each time you open the tab.
- Each shortcut says where it works: *Any window* (the app reacts whichever window is active, like Snipline's PrtSc) or *Only when active* (only while that app is the active window, like Jancy's keybinds).
- Shortcuts that get in each other's way are marked, for example a global shortcut that stops another app's in-app shortcut from ever reaching it.
- **Is it free?**: press keys (or type them in the search box, such as ctrl+shift+c) to see which apps use them. Macro Kit also asks Windows whether some other running program has already claimed them.
- Search, filter by app or by where shortcuts work, sort by keys to line up identical shortcuts, hide built-in shortcuts, and see which apps are running.

## 1.0.2 - 2026-11-12

- Status popup: drag it anywhere and it stays there for every recording, playback and macro run. Dropped on the right or bottom half of a screen it keeps its right or bottom edge, so it grows away from the screen edge, and it never ends up off screen.
- Settings has a Status popup card: Show popup puts it on screen while nothing runs so you can drag it into place (press Done when it is there), and Reset position puts it back in the bottom-right corner of the screen you are working on.

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
