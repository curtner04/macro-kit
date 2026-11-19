# Macro Kit changelog

## 1.7.0 - 2026-11-19

- **Recorder bar**: while it records, Macro Kit no longer disappears completely. A small bar at the top of where its window was shows a blinking dot and the time, with **Stop** (keep the take), **Restart** (throw it away and start again) and **Discard**. Clicks on the bar are never recorded; drag it and it stays there.
- **Convert to macro** on the Recorder tab turns a recording into a macro you can read and edit: clicks with where they happened, double clicks, drags, typed text, shortcuts, scrolls and the pauses between them, one step per line.
- Each recording can have its own **hotkey** and **Deck button**, and keeps its own **speed and repeat** (*This recording* and *Playback* on the Recorder tab).
- **Ten example phrases and ten example macros** come with Macro Kit, so it is easier to see what they can do: a signature, an address, dates, fill-ins, a typo fix; copy-and-paste rows, an auto-clicker, a timed refresh, opening Notepad and more. Earlier installs get them once. **Remove examples** takes out the ones you have not changed, and Undo puts them all back.
- **Global Keybinds search takes keys in any order**: `ctrl alt s` shows every shortcut that uses Ctrl, Alt and S, and `alt ctrl shift` every one with all three modifiers.
- The macro editor has a **Lines | Tiles** switch above the steps instead of the *Use key tiles* link. With **Show code** on, the code no longer has a second steps list beside it: just the script, a count and any problems.

## 1.6.0 - 2026-11-19

- **Paste text instead of typing it**: a new option under *Repeat and speed* puts a macro's text in with one paste, so a whole paragraph appears at once instead of key by key. Your clipboard is put back afterwards and the pasted text stays out of Win+V history. Text under 4 characters is still typed, and if the clipboard is busy the text is typed instead.
- Phrases can say where the caret ends up with `{cursor}`, and ask for a fill-in as they expand with `{fill:Name}`: a small **Fill in** window opens with a box for each name, Enter inserts the phrase and Esc leaves what you typed as it was.
- Phrases can be put in a **Group**. The list shows each group under its own heading, and search also finds the group name.
- **Delete** no longer asks first: a deleted phrase or macro can be put back with **Undo delete**. The list buttons are normal size now, and Delete sits apart from New and Duplicate.
- Deck buttons can have a **Colour** stripe and an **Icon** (an emoji or symbol), and show their hotkey. A macro that can't run yet shows **⚠ needs fixing** with the reason in its tooltip. **Compact** in the Deck's title strip shows smaller buttons in one row.
- Command groups in *Show all commands* open one at a time, so the list no longer grows into a page of its own.
- The *Macro Kit understands* column wraps a long explanation into the empty lines below it instead of cutting it off.
- Settings → About shows the data folder as a short path (the full path is in its tooltip and behind **Open data folder**), and the Hotkeys card mentions that shorter combinations and lone F13 to F24 keys work too.

## 1.5.6 - 2026-11-19

- Macros type one character at a time, like a person, with the key delay (**ms after each key or click**, 10 ms unless you change it) and the ready check after each one. The Windows 11 Notepad lost whole stretches of text, and the shortcuts after it, when several keys arrived at once. A long text now takes a few seconds; set the key delay to 0 for programs that keep up.
- **Copy** and **Cut** (Ctrl+C, Ctrl+X, Ctrl+Insert, Shift+Delete) wait until the copy is really on the clipboard before the next step, for at most a second, so a paste right after them pastes the new text instead of what was there before.

## 1.5.5 - 2026-11-19

- Macros type text with real key presses, the way a keyboard does: each character on your keyboard layout is its own key, with Shift when needed ("!" is Shift+1), and Caps Lock is taken into account. Before, every character went as a special Unicode-only event, which the Windows 11 Notepad could repeat over and over ("tttt", "!!!!") or drop, and which left it so far behind that a later Ctrl+A arrived as a plain "a". Characters that aren't on your layout (emoji, é on a US keyboard) and keys that would combine with the next letter (dead keys) still go as Unicode.

## 1.5.4 - 2026-11-19

- Macros wait for the window to catch up instead of rushing on. After every step, and after switching windows, Macro Kit waits (up to a second) until the window's program is responding and ready for keys before sending the next step. Most `wait` lines between steps are no longer needed.
- Typed text goes out a few characters at a time with that check in between, so slow programs such as the Windows 11 Notepad no longer drop characters (a missing "!") or repeat one ("hhhh").
- A real Alt+Tab waits for the switch to finish and for the new window to be ready.
- In **Type it**, Enter takes the highlighted suggestion, so `typ` then Enter gives `type ""`. A line that is already a whole step, such as `click`, still starts a new line unless you moved to a suggestion with the arrow keys.
- `left click` is in the suggestions (it means the same as `click`).

## 1.5.3 - 2026-11-19

- **Where it runs** is simpler. The chosen window now appears as a card with the program name, whether a title narrows it, and **Open now** or **Not open right now**. Before, the list still said *Choose an open window* after you had chosen one.
- A window chooser appears only when you pick **Always the same window**, so options you aren't using no longer take up space.
- The **Program** and **Title contains** boxes are behind a **Match details** link. They open by themselves when a macro already uses a title.
- Clearer wording: **Type into** *Whichever window I was using before Macro Kit* or *Always the same window*, and **When the script presses Alt+Tab, go to** *The window Windows would switch to* or *Always the same window*, with the explanation above the choices.

## 1.5.2 - 2026-11-19

- Phrases can put in the date, the time or what you copied: `{date}`, `{time}` and `{clipboard}` in the replacement are filled in as the phrase expands. Give a format of your own with a colon, such as `{date:dd MMM yyyy}` or `{time:HH:mm}`. Other text in braces is typed as it is.
- **Search macros** and **Search phrases** boxes above the lists show only the macros or phrases that hold the words you type.
- Every macro library pack is now free: Spreadsheets and forms, Browser, Clicker, Conditions (formerly Loops and conditions) and Symbols join Starter. The Conditions examples still need Macro Kit Pro to run.
- Pro can't be bought yet, so the **Get Pro** buttons now say *Pro goes on sale soon* instead of opening a page with no checkout.
- The switch beside **Run** is now called **Show code** (it was **Stream Deck**). It still shows the Super Macro code to copy into a Stream Deck action.
- Macro Kit no longer asks which macro editor to use the first time it opens. Macros start in **Type it**; switch to key tiles in Settings or with the link above the steps.
- **Library** moved up beside the Macros heading, away from **Delete**.
- Global Keybinds lists Windows' own shortcuts (Win+E, Win+V, Win+Shift+S, Alt+Tab, Win+1 to 9 and more), so **Is it free?** and the clash warnings know about them.

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
