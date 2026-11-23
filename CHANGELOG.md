# Macro Kit changelog

## 1.13.1 - 2026-11-23

How-to guides, and a fix for recordings that switch windows.

- **How-to guides**: the commands that do more than one simple step now explain how they work. Each of these groups in the commands panel has a **How it works** link: holding a key down, repeating and taking turns, opening a program and waiting for it, switching windows and waiting for a colour, waiting for a picture or clicking it, variables and asking, conditions, choosing where a macro types, schedules and turning a recording into a macro. Every guide has examples to copy straight into a macro's steps. All of them are also under **⋯ › How-to guides**.
- **Convert to macro keeps Alt held over several Tabs**: a recording that held Alt and pressed Tab a few times to reach a window (or Shift+Tab to go back) became separate Alt+Tab steps, which only ever switch to the last window. It now holds Alt, presses Tab as often as you did and lets go, so it lands on the same window. The Windows key held over other keys is kept the same way.

## 1.13.0 - 2026-11-23

Macros that find a button or icon by how it looks.

- **Wait for a picture, or click it** (Macro Kit Pro): save a small picture of a button or icon, then a macro can wait until it shows anywhere in the window in front, or click its middle, wherever it is. Small differences in shade still count. It waits 10 seconds unless you say; if the picture never shows, the macro stops and says so. Plain lines: *wait for picture Save button 5s* and *click picture Save button*; code: `{{WAITIMAGE:Save button:5000}}` and `{{CLICKIMAGE:Save button}}`. Where it was found is kept in `$IMAGE_X` and `$IMAGE_Y`.
- **Pictures in the commands panel**: press **Capture…**, point at the button, and after 3 seconds Macro Kit saves the square around the pointer under the name you give (48 pixels unless you change the size). **Open folder** shows the saved pictures, which are PNG files in the data folder's `images` folder and move with Export / import.
- **Copy says what a Stream Deck can't run** now also covers the picture steps.

## 1.12.2 - 2026-11-21

- **Import takes new recordings again**: a recording made since 1.11 that clicks in a window could be exported, but importing it, or restoring it on another PC, failed with "made by a newer version of Macro Kit". Those recordings now import with their windows.

## 1.12.1 - 2026-11-21

Fixes from a review of 1.11 and 1.12.

- **Undo after a trim keeps a new name**: renaming a recording after trimming it, then pressing Undo, used to bring the old name back. Undo now puts back only what was cut.
- **Clicks follow the right window when several match**: with two Explorer or browser windows showing the same title, playback now picks the one nearest where the recorded window was, instead of whichever Windows listed first. When the title has changed (a browser tab) and the program has several windows, it uses the one still exactly where the recorded window was, instead of stopping.
- **Ask while running keeps what the macro was holding**: keys and mouse buttons the macro held are let go while you answer and pressed again afterwards, a button back where the pointer was, so a held Shift or a drag carries on.
- **Stop closes the question every time**: pressing Stop just as the question was opening could leave it open.
- **Pick measures from the window**: picking a spot in a menu or tooltip now measures from the window in front, as the macro does when it runs.
- Smaller fixes: a key whose release the recording never got no longer stops *Shorten pauses* from cutting the pauses after it; a second taskbar click after a click on Macro Kit's tray icon is no longer measured from the previous window; lengths just under a minute show as *1:00.0* instead of *60 s* or *1:60.0*; the colour in a wait for colour can be a variable (`{{WAITPIXEL:120,40:$colour}}`).

## 1.12.0 - 2026-11-20

Macros that wait for the screen and ask you for what they need.

- **Wait for a colour**: a macro can wait until a spot in a window turns a colour, such as a Save button lighting up or a progress bar turning green, then carry on. The spot is measured from the window in front, so it still works when the window moves. In the commands panel, Windows has a new row: press **Pick…**, point at the spot, and after 3 seconds Macro Kit fills in the spot, its colour and the window. It waits 10 seconds unless you say; if the colour never comes, the macro stops and says what colour the spot was instead. Plain line: *wait for colour #1E90FF at 120, 40 in the window 5s*; code: `{{WAITPIXEL:120,40:#1E90FF:5000}}`.
- **Ask while running**: `{{INPUT:row:Which row?}}` (plain line: *ask for $row "Which row?"*) stops to ask for a value in the same small window phrases use for fill-ins, then uses your answer as `$row`. **Continue** or Enter carries on; **Stop the macro** or Esc stops it. Any keys the macro was holding are let go first. It is under Variables in the commands panel as **Ask while running**.
- **Copy says what a Stream Deck can't run**: copying a macro's code now notes when it uses `{{WAITPIXEL}}` or `{{WINDOWXY}}`, which only Macro Kit runs.

## 1.11.0 - 2026-11-20

Recordings that keep working when windows move, and that you can tidy up after recording.

- **Clicks follow their window**: a new recording remembers which window each click was made in (its program, title and position). When it plays, Macro Kit finds that window, waiting up to 5 seconds for it, brings it to the front and clicks the same spots in it, even if it has moved. If the window is not open, playback stops and says so ("Couldn't find “Book1 - Excel” (EXCEL.EXE)") instead of clicking blind. It is a switch in each recording's Playback card, on for new recordings. Recordings made before 1.11 play exactly as before.
- **Convert to macro follows windows too**: the macro switches to each window first (`{{FOCUS:...}}`) and its clicks are measured from that window with the new `{{WINDOWXY:x,y}}` command (plain line: *move mouse to 50, 30 in the window*). `{{WINDOWXY}}` is Macro Kit only; Super Macro does not know it.
- **Trim a recording**: under Playback, a strip shows the selected recording with its clicks and keys marked. Drag the two handles, or Tab to one and use the arrow keys, to cut the start and end, then press **Trim**. Nothing is left held down: a key pressed before the cut loses its release, and one still down at the end is let go there.
- **Shorten pauses**: *Shorten pauses over 2 s to 0.5 s* (both can be changed) cuts the waiting out of a recording. A pause while a key or button is held, such as a long press or a slow drag, stays as it is.
- **Undo** puts a trimmed or shortened recording back as it was, up to ten steps.

## 1.10.0 - 2026-11-20

A calmer macro editor and easier reading, for everyone and for screen readers.

- **Options as four chips**: under a macro's steps, one strip shows **Repeat**, **Window**, **Start** and **Schedule**, each with its value (*Once*, *Previous window*, *Ctrl+Alt+1 · Deck*, *Off*). A click opens only that card, a second click closes it, and Macro Kit remembers which one you had open. At the usual window size the whole macro fits without scrolling.
- **Lines, Tiles or Code**: the Stream Deck text is now a third view beside Lines and Tiles, so the *Show code* switch is gone. Code shows the Super Macro text with **Copy**, and you can change it there.
- **Shorter help**: each card keeps one line of help; the rest sits behind an ⓘ button you can click, or reach with Tab and open with Enter or Space.
- **Easier to read**: small captions and notes are a little larger, and labels are no longer in capital letters.
- **Screen readers**: every button, box, list and switch you can reach with Tab now has a name, including icon-only buttons, the option chips and the Keybinds app cards.

## 1.9.0 - 2026-11-20

Quicker to set up and quicker to use from the keyboard.

- **Pick your hotkeys**: a new setup asks once, at the top of the Recorder, which keys to use for Record, Play, Stop and the Deck: the shorter Ctrl+Alt+R, P, S, D; F13 to F16 for a Stream Deck, keypad or programmable mouse; or the usual Ctrl+Shift+Alt+R, P, S, D. The shorter set is not offered when your keyboard types a character with AltGr and one of those keys, or another program already uses one. An existing setup keeps its keys and never sees the question; Settings can change them any time.
- **Keyboard shortcuts**: Ctrl+1 to Ctrl+5 and Ctrl+Tab switch tabs, Ctrl+F goes to the search box, Ctrl+N adds a phrase or macro, and F5 plays the selected recording or runs the selected macro. Tooltips name each shortcut.
- **Delete a recording and undo it**: Delete (the button or the Delete key) takes a recording away straight away, and **Undo delete** puts it back with its hotkey, Deck button and playback settings. The Macros list takes the Delete key too.
- **Tidier recordings list**: Duplicate, Export file and Import file moved into a ⋯ menu, so the list's buttons fit on one line even in the smallest window.
- **Where your data is**: Settings > About shows the whole data folder path, wrapped instead of cut off, with **Open folder** and **Copy path** beside it.

## 1.8.0 - 2026-11-19

A large clean-up release: many small fixes and clearer wording on every tab, and more care that nothing you saved is lost.

- **Switch a macro off**: every macro has an on/off switch (*Hotkey and schedule are on*). Off pauses its hotkey and its schedule without losing them. A switched-off macro shows a small *Off* label beside its name, its schedule card says it is paused, its hotkey is not shown on its Deck button, and Global Keybinds lists the hotkey as switched off and does not count it as taken. It still runs from its button, the Deck or another macro.
- **Check what a phrase will type**: phrases with placeholders such as `{date}` show a *Preview* line under the replacement, and the Phrases tab has a **Try it** button and a line saying phrases expand in other programs, not inside Macro Kit.
- **Recorder search and Duplicate**: the recordings list has a search box and a **Duplicate** button (the copy keeps the speed and repeat and has no hotkey). The Recorder also highlights the next thing to do: Save for a new take, Record when nothing is selected, Play once something is.
- **Safer data files**: a damaged settings, phrases, macros or keybinds file is now replaced by an earlier saved copy when that copy is good, so you keep your work instead of getting the examples (your most recent change to it may be missing). The status line names the file and what happened to it.
- **Dates and times in macros**: `$DATE` and `$TIME` type this PC's short date and time.
- **Status popup and tray notice**: when a macro, playback or recording ends, the small popup now says so for a few seconds (*Done*, *Recorded* or *Stopped*), even while Macro Kit's window is hidden. The first time you close the window and Macro Kit keeps running in the notification area, the popup tells you where it went and how to quit.

**Recorder**

- Stopping a recording with the recorder bar, the Deck, the popup or the tray keeps your last keys. Only the hotkey press that stopped it is removed, and an earlier Ctrl+S stays. Holding the Record hotkey a moment too long no longer types its last key at the start.
- Clicks on Macro Kit's tray icon and tray menu are no longer recorded, so a replay cannot open the menu or press Stop.
- Numpad Enter, and numpad keys with Num Lock off, replay as the keys you pressed. If Windows would not let go of a key or mouse button after playback, Macro Kit says which one to press and release.
- An empty recording explains that input from remote-control tools, the on-screen keyboard or key remappers is not recorded. If Macro Kit's keyboard and mouse watcher did not start, recording says so instead of ending with "Nothing was recorded."
- Playback timing is steadier (mouse paths and quick clicks keep their recorded timing), and playing the same long recording again starts faster.
- A new recording is labelled *New* (with a tooltip) instead of *Not saved*, and the message after recording explains Save. Play and Stop say why they are greyed out.
- On the recorder bar, Restart and Discard sit apart from Stop, and Discard asks *Sure?* before throwing away a take longer than 10 seconds.
- **Convert to macro** keeps Ctrl, Shift, Alt and Win held during clicks, drags and scrolls (Ctrl+click, Ctrl+wheel; a key let go before the mouse button stays held through the click), keeps the mouse Back and Forward buttons, and no longer makes a macro that fails to read when the recording has a gap longer than an hour.
- Recordings: double-click or F2 renames, and Delete asks before deleting.

**Phrases**

- Phrases no longer expand while you type in a standard Windows password box, so a saved phrase cannot change a password. (Password boxes in browsers and some other programs cannot be told apart.)
- A phrase with `{clipboard}` is no longer typed with an empty spot when another program is holding the clipboard: the trigger stays as you typed it and Macro Kit tells you why.
- If another window comes to the front just as a phrase starts, Macro Kit leaves both windows alone instead of erasing text in the wrong one.
- Phrases set to expand after a space or punctuation stay quiet inside joined words such as don't, e-mail, snake_case or name@site. An opening quote, a leading hyphen, an underscore or an @ that starts a word still lets a trigger expand (`'teh'`, `-teh`, `_teh_`).
- `{cursor}` always puts the caret in the right place, and `{time:H}`, `{time:h}`, `{date:K}` and `{date:z}` now give the hour and the time zone offset instead of being left as typed.
- A short trigger that expands right away and would stop a longer one from ever finishing now gets a warning.
- New phrase clears the search so the new row is always visible. Delete removes the selected phrase, and Undo delete steps back one phrase at a time (up to 10). A switched-off phrase shows a small *Off* label instead of fading.

**Macros**

- Run names the line that needs fixing (for example *Line 3*), and so do runs from the Deck, a hotkey or a schedule. An error inside a macro started with `{{MACRO}}` says which macro it is in.
- Example macros show a short sentence saying what they do and what to click first, until you change them.
- New macro and Duplicate clear the list search and scroll to the new macro. Duplicate gives the copy a free name (*copy*, *copy 2*) with no hotkey and no schedule, and says so.
- The *about X per pass, Y in all* line follows the repeat count, delays and key delay, and now counts the time spent typing text.
- **Wait for window** after **Open** waits for the window that program opens, not one that was already open (a matching window that was open all along is still used after a short wait, at most about 5 seconds).
- With *Paste text* on, a line break at the end of the text is pressed as Enter after the paste.
- Smaller fixes: a note written as `{{// note}}` no longer hides the lines below it; "hold down shift" works in Lines; a waited-for window whose title ends in a time, like "Meeting 10:30", keeps its title; *Forget all variables* also forgets your own variables whose names start with SM (only the built-in characters and the saved mouse position are kept); a continuous macro that is only waiting for its condition no longer uses a whole CPU core; *Stop if another window takes focus* also works while a long text is being typed.
- In Tiles, steps you add after an empty Repeat, Take turns or Pick at random block go inside it. Esc while changing a tile puts the step back as it was (Enter or Done keeps it), Alt+Left and Alt+Right move the selected tile, and the + menu (and the More… menu in Lines) works from the keyboard.
- Clearer hints and controls: a variable name with a space or dash explains the allowed characters, the summary line warns when a step runs a macro that does not exist, Run and Stop say why they are greyed out, and the **Open now** pill refreshes when you return to Macro Kit. Done is the main button in Tiles, edit boxes have a clearer border in both themes, and text boxes have names for screen readers.
- The schedule card shows when a scheduled macro last ran and how it went (for example *finished*, *stopped* or *skipped, it was more than 5 minutes late*). A scheduled run more than 5 minutes late (for example after the PC slept) is skipped instead of typing into whatever window is open.

**Global Keybinds**

- Is it free? always shows the checked keys in the list, and **Clear** removes that narrowing (a search you typed stays). Typing a lone F key such as `F9` in Search says whether it is free.
- Add or edit app asks before discarding the shortcuts you typed when you press Esc, X or Cancel, and a chord such as Ctrl+K, Ctrl+C gets a clear message. The Add shortcut button follows the last shortcut.
- The app you filter by is highlighted, and clicking it again shows all apps. The tab opens and refreshes faster with many shortcuts. The scope filter, the editor and the list now say *Any window* and *Only in the app*, and a switched-off shortcut is shown in grey text instead of fading the whole row, so its warnings stay readable.

**Hotkeys and settings**

- Setting a hotkey to an everyday shortcut such as Ctrl+C or Ctrl+V shows a warning that it will stop working in other programs while Macro Kit holds it, and suggests another combination. The hotkey still works.
- Hotkey messages are plainer: they suggest a fix such as Ctrl+Alt+R or F13 on its own, and a clash with another program says Global Keybinds may show which one has it.
- If another program has your Stop hotkey, the popup and recorder bar no longer tell you to press it, and the status line says some hotkeys could not be set up.
- The Stop hotkey stops a playback or macro right away, before your held Ctrl, Shift or Alt can turn its keys into shortcuts.
- Settings: Deck columns and Theme line up, Deck columns explains the Deck, and the macro editor choice is named **Lines** (it was *Type it*).

**Deck and status popup**

- Deck buttons for recordings with the default name read *Replay 11-12 09:30* instead of *Recording 2026-11-12 09:30*, so they are not mistaken for a recording in progress.
- The Deck button in the header is marked quietly with an outline when the Deck is open. A problem that stops a run stays in the popup for 30 seconds instead of 8.
- Opening Macro Kit (tray, double-click or starting it again) while a recording plays stops the playback first, so the rest cannot go into Macro Kit's own window. The tray menu's Exit is now **Exit Macro Kit**, and its Restart is greyed out while something runs.
- The main window remembers being maximized. The status popup, recorder bar and Deck no longer open at a saved position that falls in the gap between two monitors, so they cannot open out of sight. Moving or resizing the window or the Deck is smoother. Ordinary status messages clear after about 10 seconds; errors stay until something replaces them.

**Reliability and data safety**

- If another program has your settings, phrases, macros or keybinds file open for a moment while Macro Kit starts, Macro Kit waits up to about a second and a half for it instead of falling back to defaults.
- A file that stays in use is never saved over. If you change it anyway, **Exit and Restart now ask first**, because those changes cannot be saved.
- A recording that another program has open no longer slows down the Recorder tab or drops out of the list. The warning names it, and renaming it is refused with a plain message until it can be read again.
- An empty entry in a data file (left by a hand edit or a sync tool) no longer stops Macro Kit from starting. Example phrases and macros you deleted do not come back if settings.json is lost or set aside.
- Settings, phrases, macros and keybinds also keep a `.session` copy of the file as it was before your first change in a session, so a bad edit can be undone from the data folder. Saving without changes no longer replaces the `.previous` copy, and exporting a recording or replacing a Pro key no longer leaves extra `.previous` files.
- Macro Kit notices when Windows may have dropped its keyboard and mouse watchers after a stall and puts them back, so phrases, recording and Esc-to-stop keep working.
- Signing out or shutting down Windows saves your latest edits and an unsaved recording. Exiting while a macro, playback or window pick is running no longer flashes windows or shows errors on the way out.
- **Check input** (Settings, and **Try it** on Phrases) only types when its own window is in front with the box focused, and clears the phrase box on each try so it cannot show a false pass.

## 1.7.1 - 2026-11-19

- Fixed: stopping a recording (or the end of a playback or macro) while the Macro Kit window was maximized showed "Cannot show Window when ShowActivated is false and WindowState is set to Maximized". The window now comes back maximized, behind the program you are working in, without taking the focus.

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
