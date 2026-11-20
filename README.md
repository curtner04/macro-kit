# Macro Kit

Macro Kit is one small Windows app that replaces three tools without their extra features:

| It replaces | Tab | What it does |
| --- | --- | --- |
| TinyTask | **Recorder** | Record mouse and keyboard, save recordings, play one back at 0.1x to 100x speed, once, a set number of times or continuously. |
| PhraseExpress | **Phrases** | Type a trigger such as `/shrug` in any program and it turns into the text you saved (`¯\_(ツ)_/¯`). |
| Stream Deck + BarRaider Super Macro | **Macros** and the **Deck** | Write or click together a Super Macro script and run it once, a set number of times or continuously, in the window you choose. |

It is a normal Windows program (.NET Framework 4.8, nothing to install first). Run `Macro Kit.exe`, or use the Start menu shortcut once it is installed.

## Download

[**Download Macro Kit for Windows**](https://github.com/curtner04/macro-kit-downloads/releases/latest/download/MK-Setup.exe)
(64-bit Windows 10/11) · [release history](https://github.com/curtner04/macro-kit-downloads/releases)

The installer puts Macro Kit in your user profile without asking for administrator rights. If the link says "Not Found", the first installer has not been published yet.

## Getting started

1. Open **Macros**. Ten examples are already there, such as "Copy, switch, paste, next row" and an auto-clicker; **Phrases** has ten too. Pick one and click **Run**. **Remove examples** (above each list) takes out the ones you have not changed, and Undo puts them back.
2. Macro Kit steps aside, switches to the window you used before it, and runs the macro there. A small status bar at the bottom-right of the screen shows progress and has a **Stop** button. Drag it anywhere and it stays there; **Settings → Status popup** can show it while nothing runs so you can place it, and **Reset position** puts it back in the corner.
3. When the macro finishes, Macro Kit comes back without taking focus. The popup says *Done*, *Recorded* or *Stopped* for a few seconds, even while Macro Kit's window is hidden; a run that stopped because of a problem keeps its reason up for 30 seconds.

Closing the window keeps Macro Kit running in the notification area (tray), because phrases and hotkeys only work while it runs. The first time, the popup tells you where it went (it may sit under the ^ arrow by the clock). Use the tray menu (**Exit Macro Kit**) or **⋯ > Exit Macro Kit** to quit. This can be turned off in Settings.

## The tabs

### Recorder

- **● Record** starts recording the mouse and keyboard; **■ Stop** (or the Stop hotkey) ends it. While it records, Macro Kit shrinks to a small **recorder bar** at the top of where its window was: **Stop** keeps the take, **Restart** throws it away and starts again, **Discard** throws it away (it asks *Sure?* first for a take longer than 10 seconds). Clicks on the bar, on Macro Kit's tray icon and on the tray menu are never recorded, and dragging the bar moves it there for later recordings. Phrases pause while recording, so a recording replays exactly what you typed.
- **What Stop keeps.** Stopping with a button (the bar, the Deck, the popup or the tray) keeps everything you did. Stopping with a hotkey removes only that hotkey press from the end, so an earlier Ctrl+S stays. Keys already held when recording starts (usually the Record hotkey) are ignored until you let go. Only your own keyboard and mouse are recorded: input from remote-control tools, the on-screen keyboard or key remappers is left out, and a take made only of that is reported as empty with this reason.
- A new recording shows as **New** at the top of the list until you press **Save** (or turn on *Save new recordings automatically*). Exit, Restart and starting a recording by hotkey or from the Deck also save it; recording again from this window asks before replacing it. The button to press next is highlighted: Save for a new take, Record when nothing is selected, Play once a recording is.
- **Rename**, **Delete**, **Duplicate**, **Export file…** and **Import file…** manage recordings (double-click or F2 renames, and Delete asks first). **Duplicate** copies the selected recording with its speed and repeat but no hotkey. The search box above the list shows only the recordings whose name holds the words you type. Exported files can be shared with another Macro Kit.
- **▶ Play** plays the selected recording. **Speed** is a slider from 0.1x to 100x with quick buttons for ½x, 1x, 2x, 5x and 10x. **Repeat** is once, a number of times, or continuously, with an optional wait between repeats. Each recording keeps its own speed and repeat; a recording you have not set yet uses the last ones you chose.
- **This recording** gives the selected recording its own global **Hotkey** (press it again while it plays to stop it) and a button on the Deck (*Show a button for it on the Deck*).
- **Convert to macro** turns the selected recording into a macro on the Macros tab: its clicks (with where they happened), double clicks, drags, typed text, shortcuts, scrolls and the pauses between them, one step per line, so you can read and change it. Mouse movement between clicks is left out, and the pauses follow the recording's speed. Typed characters are read with a US keyboard layout. Ctrl, Shift, Alt and Win held during a click, drag or scroll are kept, as are the mouse Back and Forward buttons.
- **Clicks follow their window** (on for new recordings): a recording remembers which window each click was made in. When it plays, Macro Kit finds that window (waiting up to 5 seconds for it), brings it to the front and clicks the same spots in it, even if it has moved. If it is not open, playback stops with "Couldn't find “Book1 - Excel” (EXCEL.EXE)" instead of clicking where it used to be. Recordings made before 1.11 play exactly as before. Convert to macro then writes `{{FOCUS:...}}` before each window's steps and `{{WINDOWXY:x,y}}` (measured from that window) for its clicks.
- **Trim** shows the selected recording as a strip, with clicks and keys marked. Drag the two handles (or Tab to one and use the arrow keys, Shift for a second at a time) to cut the start and end, then press **Trim**. **Shorten pauses over [2 s] to [0.5 s]** cuts the waiting out; a pause while a key or button is held stays. **Undo** puts the recording back as it was, up to ten steps.
- *Record mouse movement* off records only clicks, wheel and keys (the pointer jumps straight to each click).
- Playback sends numpad Enter, and numpad keys with Num Lock off, as the keys you pressed. If Windows would not let go of a key or mouse button afterwards, the status line says which one to press and release.

### Phrases

- *Expand phrases as I type* switches the whole feature on or off (the tray menu has the same switch).
- Each phrase has a **trigger**, the **replacement** text (line breaks are typed as Enter), and options:
  - **Right away** (default): expands the moment the last character of the trigger is typed, like PhraseExpress "execute immediately". It works even inside a word, so choose triggers such as `/shrug` or `;addr`.
  - **After a space, Enter, Tab or punctuation**: the trigger only counts at the start of a word, and the key you pressed is typed after the text. It stays quiet inside joined words such as don't, e-mail, snake_case or name@site, but an opening quote, a leading hyphen, an underscore or an @ that starts a word still lets it expand (`'teh'`, `-teh`, `_teh_`).
  - **Type** or **Paste** the text. Paste is faster for long text. Pasting keeps whatever you had copied: Macro Kit remembers the whole clipboard (text, formatted text, pictures, files), pastes the phrase and puts everything back afterwards, unless you copied something else in the meantime. The pasted phrase is kept out of the Windows clipboard history (Win+V) and cloud clipboard sync, so addresses or other private text do not pile up there. Very large clipboard contents (over 64 MB) and a few special kinds are restored as text only. If the clipboard cannot be used at that moment, nothing is erased: the key you typed is typed normally and the status bar says the phrase was not inserted.
  - **Match case** and a per-phrase on/off switch.
- The replacement can hold **placeholders**, filled in when the phrase expands: `{date}` and `{time}` (in this PC's short format), a format of your own such as `{date:dd MMM yyyy}` or `{time:HH:mm}`, `{clipboard}` (the text you last copied; if another program is holding the clipboard the phrase is not typed and the trigger stays as you typed it), `{cursor}` (the caret ends up there instead of at the end) and `{fill:Name}`. Formats follow .NET, so `{time:H}` is the hour and `{date:z}` the time zone offset. A **Preview** line under the replacement shows what it will type. A phrase with `{fill:…}` opens a small **Fill in** window with one box per name before it expands, such as `Hi {fill:Name}, thanks for {fill:What}.`; the same name used twice is asked once. Enter inserts the phrase and Esc leaves what you typed as it was. Other text in braces is typed as it is.
- **Group** (optional) lists phrases together under a heading, such as *Work* or *Email*. Phrases without a group come first. **New phrase** puts the new one in the group of the phrase you had selected.
- **Search phrases** above the list shows only the phrases whose trigger, text or group hold the words you type.
- **Delete** (or the Delete key) removes the selected phrase at once; **Undo delete** beside **New phrase** puts it back, one phrase at a time (up to 10 steps). **New phrase** clears the search so the new row is visible. A switched-off phrase shows a small *Off* label.
- A note appears when a short trigger that expands right away would stop a longer one from ever finishing; set the short one to *After a space* to fix it.
- **Try it** (above the list) opens the same **Check input** window as Settings, to confirm that phrases expand on this PC.
- Phrases never expand inside Macro Kit itself, or in programs listed under *Programs where phrases never expand* in Settings. They do not expand in a standard Windows password box (password boxes in browsers and some other programs cannot be told apart, so exclude those programs if needed), and if another window comes to the front just as a phrase starts, nothing is erased or typed.
- If PhraseExpress is running, a warning appears: both programs could expand the same trigger.
- Ten example phrases come in an *Examples* group: a signature (`;sig`), an address, an email, today's date, a time stamp, a thank-you, a meeting request with fill-ins (`;meet`), a link around the copied address (`;link`), pasted opening hours and a typo fix (`teh` → `the`). **Remove examples** takes out the ones you have not changed; **Undo remove** puts them back. Earlier installs get them once, without duplicates.

### Macros

A macro is a list of steps plus where and how it runs. **Lines | Tiles** above the steps switches between two views of the same steps (Settings, *Macro editor*, has it too); macros start in Lines. Ten example macros come with Macro Kit, from copy-and-paste rows to an auto-clicker, a 30-second refresh and opening Notepad; **Remove examples** beside **Library** takes out the ones you have not changed.

- **Lines**: one step per line, written the way you'd say it: `ctrl+c`, `wait 50ms`, `alt+tab`, `type "hello"`, `click`, `open notepad`, `repeat 3 times` … `end repeat`, `take turns` … `or` … `end turns`. The column beside the text says what each line does and marks lines Macro Kit doesn't understand. Suggestions appear as you type (Tab or Ctrl+Space). The **Add** chips insert a step at the cursor: **Record keys** (press shortcuts; each becomes a line), **Type text**, **Wait**, **Click**, **Repeat** (around the selected lines), **Open app** and **More…**.
- **Record keys** (also **+**, *Record keystrokes…* on the tiles) turns each shortcut you press into one step. While its window is in front it takes the keys before Windows and other programs do, so Alt+Tab, the Windows key, PrtSc, Alt+F4 and other apps' hotkeys are recorded instead of acted on; only Ctrl+Alt+Delete and Win+L stay with Windows. **Delays**: *No delays*, *Standard delays (50 ms)* between steps, or *Real-time delays* (the pauses as you pressed them). Macro Kit remembers the choice.
- **Tiles** (key tiles): the same steps as tiles (**Ctrl + C**, **50 ms**, **“hello”**), without key down and key up markers. **+** adds Record keystrokes, Text, Action, Launch application, System (media keys, volume, show desktop, lock the PC) or Delay after the selected tile. Click a tile to retype it: **Done** or Enter keeps the change and Esc puts the step back as it was. Move a tile with ◀ ▶ or Alt+Left and Alt+Right, or delete it. The **+** and **More** menus work from the keyboard. Steps added after an empty Repeat, Take turns or Pick at random block go inside it.
- Both editors store the same Super Macro text (see the reference below), so a macro made in one opens in the other.
- **Show code** (the switch beside Run) shows the macro as Super Macro code to copy into BarRaider's Super Macro action, with the commands list and *New lines in the script press Enter*. Editing the code updates the steps. **Show all commands** opens the command list below the steps without turning the switch on.
- **Paste text instead of typing it** (under *Repeat and speed*) puts each `type` line's text in with one paste instead of key by key, so a paragraph appears at once. Your clipboard is put back afterwards, unless something else copied in the meantime, and the pasted text is kept out of Win+V history. Text under 4 characters is still typed, and so is text while the script holds a key down. A line break at the end of the text is pressed as Enter after the paste. Turn it off for programs that don't accept pasting, such as some games and remote desktops.
- **Options** sums up Repeat, Where it runs, Start it with and the schedule in one line, with the time a pass takes (typing counts); **Change options** (or a click on the line) opens them. The summary also warns about a step that runs a macro that does not exist.
- Example macros show a one-sentence description of what they do and what to click first, until you change them.

With **Show code** on:

- **Script text** is what runs. The steps it makes are the ones above, so under it is only a count and, when there are any, its problems in red with line and column (and *Run the macro* steps that name a macro you don't have); click one to select it in the script. A script with problems will not run.
- **Add commands** inserts at the cursor so you never have to remember the syntax. The commands sit in groups that fold open and closed, one at a time (Macro Kit remembers which is open), and **Find a command** shows only the groups that match a word such as *copy*, *loop* or *volume*:
  - **Keys**: keys grouped by kind, each in its own colour (Clipboard, Switch, Edit, Arrows, Jump, any F key), **Your own shortcut** and **Hold or release a key**.
  - **Multi action** (like Stream Deck's Multi Action): **Repeat** the steps you selected N times, **Run the macro** (another of your macros), and **Take turns** or **Pick at random** between 2 or more choices. Select steps in the script first and the block goes around them; with nothing selected an empty block goes in at the cursor.
  - **Wait and repeat a key**: **Pause** (box or 50/100/250/500/1000 ms) and **Repeat a key** (key × count, delay).
  - **Type text and notes**, **Mouse** (clicks, move to X,Y with a 3-second *Pick* countdown, scroll).
  - **Apps and websites**: open a program, file or folder (**Browse…**), open a website, close a program (pick one that is open), lock the PC.
  - **Windows**: switch to a window, or wait for one to open.
  - **Media and sound**: play/pause, next, previous, stop, mute and the volume keys, set the volume to a percentage or turn it up or down, play a sound file.
  - **Variables and clipboard**, and **Conditions** (Macro Kit Pro).
- **Repeat**: once, a number of times, or continuously, with a wait between repeats and a delay after every key or click. *New lines in the script press Enter* matches Super Macro; turn it off to lay a long script out over several lines.
- **Where it runs** (*Type into* and *When the script presses Alt+Tab, go to*): see the next section.
- **Start it with** has an on/off switch (*Hotkey and schedule are on*), the macro's own global hotkey (pressing it while that macro runs stops it) and whether it appears on the Deck, with an optional **Colour** stripe and **Icon** (an emoji or symbol; Win+. opens the emoji panel). Switched off, the hotkey and schedule are paused but kept: the macro shows a small *Off* label in the list, its Deck button does not show the hotkey, Global Keybinds lists the hotkey as switched off and does not count it as taken, and it still runs from **Run**, the Deck or another macro.
- **Search macros** above the list shows only the macros whose name, steps or hotkey hold the words you type. **New macro**, **Duplicate** and **Delete** manage the list; **New macro** and **Duplicate** clear the search and scroll to the new macro; **Duplicate** names the copy *… copy* (then *copy 2*) and gives it no hotkey and no schedule. **Delete** removes a macro at once and **Undo delete** puts it back. **Library** (beside the heading) adds ready-made macros and phrases.

### Global Keybinds

One list of the keyboard shortcuts in your apps, so you can see which keys are still free before you give something a new shortcut.

- **Works in** says whether a shortcut works in **Any window** (the app reacts whichever window is active, as long as it runs, like Snipline's PrtSc or Macro Kit's own hotkeys) or **Only in the app** (only while that app is the active window, like Jancy's keybinds). Keys used only inside an app are still free everywhere else.
- Saved shortcuts are read from each app's own files every time you open the tab (or press **Refresh**). Macro Kit only reads them; to change one, change it in that app.

  | App | Read from |
  | --- | --- |
  | Macro Kit | its own Settings and macro hotkeys |
  | Windows | built in: Windows' own shortcuts (Win+E, Win+V, Win+Shift+S, Alt+Tab, Win+1 to 9 and the rest; PrtSc is left out, since what it does is a Windows setting) |
  | Snipline | `%LOCALAPPDATA%\Snipline\data\settings.json` (Capture shortcut, Active window shortcut) |
  | Note Keeper | `%LOCALAPPDATA%\Note Keeper\data\settings.json` (Listen / stop, screenshot, mark moment) |
  | Jancy | `%APPDATA%\Jancy\keybinds.txt` |
  | Record Keeper, Little Buttons, Storage Lens, Tweak Bench | built-in shortcuts only (nothing to save) |
  | PowerToys | `%LOCALAPPDATA%\Microsoft\PowerToys` (the shortcuts of every switched-on tool, Keyboard Manager's shortcut remaps) and Command Palette's own `settings.json`. Listed only when PowerToys is installed |
  | OBS Studio | `%APPDATA%\obs-studio`: the current profile's `basic.ini` hotkeys and the current scene collection's scene and source hotkeys. Listed only when OBS is installed |

- **Add app** (on the Apps card) is for any other program: type its name, optionally the program it runs as (so the card can say whether it is running), and its shortcuts. For each one, click the Keys box and press the keys, or type them (`ctrl+shift+m`, `F9`, `prtsc`), say what it does, and choose **Any window** or **Only in the app** (a chord such as Ctrl+K, Ctrl+C is not supported; Esc, X or Cancel asks before throwing away what you typed). Apps you add are marked *Added by you* and count everywhere on the tab: in the list, the warnings and **Is it free?**. The pencil button on their card edits or deletes them. They are saved in `keybinds.json` in the data folder and move with Export / import. Macro Kit never changes the program itself.
- Built-in shortcuts (such as Ctrl+S in an app) are listed too; untick **Built-in shortcuts** to see only saved ones.
- A warning under a shortcut means it gets in another app's way: two apps with the same any-window shortcut, or an any-window shortcut that takes the keys before another app's in-app shortcut can see them.
- **Search** takes keys in any order, with spaces or plus signs: `ctrl alt s` lists every shortcut that uses Ctrl, Alt and S (Ctrl+Alt+S, Ctrl+Shift+Alt+S…), and `alt ctrl shift` lists everything with Ctrl, Shift and Alt. Words that are not keys search the names and descriptions.
- **Is it free?**: click the box and press keys, or type them in **Search** (for example `ctrl+shift+c`, `c shift ctrl` or a lone `F9`). It says which apps use them and where, and the list shows the checked keys until you press **Clear** (a search you typed stays). For keys no listed app uses, Macro Kit also asks Windows whether another running program has registered them. Programs that watch the keyboard another way (a keyboard hook, like Snipline's PrtSc) do not show up in that Windows check, and keys such a program grabs may run it instead of appearing in the box; type those keys instead.
- Each app card shows how many shortcuts it has, whether it is running (any-window shortcuts only work while it runs), the file it was read from and any problem reading it. Click a card to show only that app (it is highlighted; click it again to show all apps). A switched-off Macro Kit macro's hotkey is listed as switched off and does not count as taken.

### Settings

The four global hotkeys, whether Esc stops playback and macros, whether Macro Kit comes back after a run, whether closing the window keeps it in the tray, *Start Macro Kit when I sign in to Windows*, Deck columns, theme (follow Windows, light or dark), the macro editor (**Lines** or **Tiles**) and the programs where phrases never expand. If another program already uses a hotkey, the reason is shown under it. A hotkey on an everyday shortcut such as Ctrl+C or Ctrl+V, or on one Windows uses, gets a warning (that shortcut stops working in other programs while Macro Kit holds it) and a suggestion, but still works. **About** shows the version, Free or Pro, and **Check input**: Macro Kit types a short line into its own box and expands a test phrase, which confirms that Windows (and any security software) lets it send keys and see your typing on this PC.

## The Deck

The Deck is a small always-on-top panel of buttons, like a virtual Stream Deck: one button per macro that has *Show this macro as a button on the Deck* turned on, one per recording with *Show a button for it on the Deck*, a button that plays the selected recording, and a **Stop** button. Show or hide it with the **Deck** button in the header, the tray menu or the Deck hotkey. Drag it by its title strip; it remembers where you left it.

Each button shows the macro's icon and name, its hotkey underneath, and its colour stripe. A macro that can't run yet (a step needs fixing, or it has no steps) shows **⚠ needs fixing**, and its tooltip names the line as the Lines editor shows it. A switched-off macro keeps its button but not the hotkey underneath, and its tooltip says its hotkey and schedule are paused. A recording with the default name reads *Replay 11-12 09:30* rather than *Recording 2026-11-12 09:30*. **Compact** in the title strip shows smaller buttons in one row; **Bigger** goes back.

**Clicking a Deck button never takes focus**, exactly like the virtual Stream Deck. A macro started from the Deck runs in whichever window had focus a moment ago.

## Running in the right window (focus and Alt+Tab)

The classic problem with macros is that the macro runs in the wrong window: the one you just clicked, which is the macro app itself, or Alt+Tab lands on the macro app. Macro Kit is built so that cannot happen.

- **It steps aside.** When a macro, recording or playback starts from the main window, Macro Kit first brings the target window forward, then hides its own window. A macro does not send its first key until Macro Kit is hidden (it waits for that for at most a second), so even an Alt+Tab as the very first step cannot land on Macro Kit. A hidden window is not on the screen, not in the taskbar and not in Alt+Tab.
- **It waits for the window to catch up.** After every step and every window switch (Alt+Tab included), a macro waits until the window in front answers Windows and has somewhere for keys to go, for at most a second, before the next step. Copy and Cut wait (at most a second) until the clipboard really changes. Typed text goes one character at a time with the key delay and that check in between, as real key presses (Shift added where needed, Caps Lock allowed for) on the keyboard layout of the window in front. Only characters the layout doesn't have, and dead keys, go as Unicode packets, because some programs (the Windows 11 Notepad among them) repeat or drop those. Windows has no signal for "the program has finished with these keys", so a step that starts slow work in the program (loading a page, opening a file) can still need a `wait`.
- **Type into** has two choices. *Whichever window I was using before Macro Kit* is the one you were in before you opened Macro Kit (or, for the Deck and hotkeys, the window that has focus right now). *Always the same window* is found by program name and part of the title. The chooser shows only with this option on: a card names the chosen window and says whether it is open now. Choose it from the list of open windows, type it under **Match details**, or press **Pick** (Macro Kit hides, you switch to the window you want and Macro Kit takes it and returns; for the window that is already in front, press **Use this window** on the small status bar). Picking and the window list remember only the *program* (for example EXCEL.EXE) and show the window's title as a hint, because titles usually contain a document name that changes: add *Title contains* only when you have several windows of the same program open. The taskbar, desktop, Start menu and task switcher can never be picked. If the window is not open, the macro stops with a clear message instead of typing somewhere else. *Stop if another window takes focus* ends the run if something else pops up. With a specific window and the default Alt+Tab choice, a real Alt+Tab is fine: Macro Kit learns the window it lands on and lets the macro go back and forth between those two; a third window (or Macro Kit) stops the run.
- **When the script presses Alt+Tab, go to** *the window Windows would switch to* uses the real Alt+Tab, which can never land on Macro Kit because it is hidden. *Always the same window* is for copy-and-paste macros: Macro Kit makes the two windows (for example Excel and Chrome) the two most recent ones, so every `{{ALT}{TAB}}` in the script goes straight between them, however many other windows are open. If Windows refuses a switch, the real Alt+Tab keys are used as a fallback, and the macro stops if focus does not arrive where it should.
- The **Deck** and the **status bar** are no-activate tool windows: clicking their buttons never takes focus and they never appear in Alt+Tab.
- When the run ends, Macro Kit comes back only if it was on screen before, and never takes focus or covers the window you are working in. If it was minimized it returns minimized. If a run started from the main window, it comes back directly behind the window in front; after a Deck, hotkey or tray run it goes to the very back. Either way it is not first in Alt+Tab. (Turn this off with *Show Macro Kit again after a run*.)
- Hotkeys that use Ctrl, Alt, Shift or Win: playback started by a hotkey waits until you let those keys go before it sends anything.
- **Administrator windows.** If the window you record or play in runs as administrator and Macro Kit does not, the status line and the status bar say so: Windows blocks Macro Kit's input there. Run Macro Kit as administrator to use it with that window.
- **Phrases pause while a macro or playback runs** (and while recording), so a phrase can never paste over the macro's clipboard or mix with its keys.
- **Stop works right away.** Esc (or Stop, or the Stop hotkey) cancels a macro or playback even if you press it during the first moments of the start; the Stop hotkey does it before your held Ctrl, Shift or Alt can turn the next keys into shortcuts. Opening Macro Kit while a recording plays stops the playback first.
- A macro, recording or playback that cannot start (for example a script error) shows its message on the status bar for a few seconds when you started it from a hotkey or the Deck, so you are not left guessing while Macro Kit is in the tray. Starting a new recording while an earlier one could not be saved asks first (or is refused when it comes from a hotkey).

Windows only lets a program take focus under certain rules. If Windows ever refuses to switch to the target window, Macro Kit says so and stops, rather than typing into the wrong window.

## Super Macro reference

Macro Kit reads the same text as the Stream Deck Super Macro plugin by BarRaider, for example:

```
{{CTRL}{C}}{{PAUSE:50}}{{ALT}{TAB}}{{PAUSE:100}}{{CTRL}{V}}{{PAUSE:50}}{{ALT}{TAB}}{{PAUSE:100}}{{DOWN}}{{PAUSE:50}}
```

- `{{` starts a command and `}}` ends it. Names are not case-sensitive and spaces inside the braces are ignored.
- **Keys**: `{{ENTER}}` presses one key. `{{CTRL}{C}}`, `{{ALT}{TAB}}`, `{{CTRL}{SHIFT}{ESC}}` press every key in order and release them in reverse. `{{LBUTTON}}`, `{{RBUTTON}}`, `{{MBUTTON}}`, `{{XBUTTON1}}`, `{{XBUTTON2}}` click.
- **Text**: anything outside `{{ }}` is typed as is. Line breaks press Enter (unless turned off). A single `{` or `}` is literal. `{{//}}` hides the rest of its line.
- `{{PAUSE:ms}}` waits (0 to 3,600,000 ms).
- `{{KEYPRESS:KEY:count[:delayMs]}}` repeats a key. `{{KEYDOWN:KEY}}` and `{{KEYUP:KEY}}` hold and release.
- **Mouse**: `{{MOUSEXY:x,y}}` (screen pixels, 0,0 is the top-left of the main screen, negatives allowed), `{{WINDOWXY:x,y}}` (Macro Kit only: pixels from the top-left corner of the window in front, so it still hits the spot when that window moves; put `{{FOCUS:...}}` before it), `{{MOUSEMOVE:dx,dy}}`, `{{MOUSEPOS:x,y}}` (0 to 65535), `{{MLEFTDOWN}}` `{{MLEFTUP}}` `{{MLEFTDBLCLICK}}`, `{{MRIGHTDOWN}}` `{{MRIGHTUP}}` `{{MRIGHTDBLCLICK}}`, `{{MMIDDLEDOWN}}` `{{MMIDDLEUP}}`, `{{MSCROLLUP[:n]}}` `{{MSCROLLDOWN[:n]}}` `{{MSCROLLLEFT[:n]}}` `{{MSCROLLRIGHT[:n]}}`, `{{MSAVEPOS}}` and `{{MLOADPOS}}` (save and return to a pointer position, also available as `$MOUSE_X` and `$MOUSE_Y`).
- **Variables** (live for one run, across its repeats): `{{VARSET:name:value}}`, `{{OUTPUT:name}}` (types it; `{{$name}}` also works), `{{VARUNSET:name}}`, `{{VARUNSETALL}}`, `{{SETCLIPBOARD:value}}`, `{{VARSETFROMCLIPBOARD:name}}`. In command arguments `$Name` is replaced by the value. Built in: `$SMCOLON`, `$SMDOLLAR`, `$SMLCB` (`{`), `$SMRCB` (`}`), `$SMSPACE`, `$SMENTER`, and `$DATE` and `$TIME` (this PC's short date and time, read when used, unless the script sets a variable of that name). `{{VARUNSETALL}}` keeps the built-in characters.
- **Key names**: letters and digits; `ENTER`, `TAB`, `ESC`, `SPACE`, `BACK`, `DELETE`, `INSERT`, `HOME`, `END`, `PAGEUP`, `PAGEDOWN`, `UP`, `DOWN`, `LEFT`, `RIGHT`, `CAPITAL`, `NUMLOCK`, `SCROLL`, `SNAPSHOT`, `APPS`; modifiers `SHIFT`, `CTRL`, `ALT`, `WIN` (and `LSHIFT`, `RCTRL` and so on); `F1` to `F24`; `NUMPAD0` to `NUMPAD9`, `MULTIPLY`, `ADD`, `SUBTRACT`, `DECIMAL`, `DIVIDE`; `OEM_1` to `OEM_8`, `OEM_PLUS`, `OEM_MINUS`, `OEM_COMMA`, `OEM_PERIOD`; media, volume and browser keys such as `VOLUME_UP` and `BROWSER_BACK`. `BREAK` is the Pause/Break key (the word `PAUSE` is always the wait).
- **Repeat** (free): `{{LOOP:n}}…{{ENDLOOP}}` repeats the steps between (1 to 100,000 times, or `{{LOOP:$count}}`).
- **Take turns and random** (free, like Stream Deck's Multi Action Switch and Random Action): `{{SWITCH}}a{{OR}}b{{OR}}c{{ENDSWITCH}}` does the next choice each time the macro reaches it (a, then b, then c, then a again), so one hotkey can mute and then unmute. Turns are remembered until Macro Kit closes. `{{RANDOM}}a{{OR}}b{{ENDRANDOM}}` does one choice picked at random. Choices can hold any steps, including other blocks.
- **Actions** (free):
  - `{{RUN:target}}` opens a program, file, folder or web address as if you double-clicked it (`{{RUN:notepad}}`, `{{RUN:https://example.com}}`). Put a path in quotes to add arguments: `{{RUN:"C:\Tools\my app.exe" --quiet}}`.
  - `{{CLOSE:program}}` asks a program's windows to close, as their close button would (`{{CLOSE:notepad}}`); unsaved work still asks first. A program that isn't open is skipped.
  - `{{FOCUS:text}}` brings the window whose title or program name has that text to the front and carries on in it. `{{WAITWINDOW:text[:ms]}}` waits for such a window to open (10 seconds unless you say, up to an hour) and carries on in it; the macro stops if none opens in time. Straight after a `{{RUN}}` it waits for the window that run opens; a matching window that was open all along is used only after about 5 seconds with no new one.
  - `{{WAITPIXEL:x,y:#RRGGBB[:ms]}}` (Macro Kit only) waits until the spot x, y pixels from the top-left corner of the window in front turns that colour (each of red, green and blue within 10), 10 seconds unless you say, up to an hour. It is measured from the window each time it looks, so it follows a window that moves. If the colour never comes, the macro stops and says what colour the spot was. Plain line: *wait for colour #1E90FF at 120, 40 in the window 5s*. In the commands panel, Windows → **Pick…** fills the spot and colour from wherever the pointer is after 3 seconds.
  - `{{INPUT:name[:question]}}` asks for a value while the macro runs, in the same small window phrases use for fill-ins, and keeps the answer in `$name`. **Continue** (or Enter) carries on; **Stop the macro** (or Esc) stops it. Plain line: *ask for $row "Which row?"*.
  - `{{MACRO:name}}` runs another of your macros once, in the same window and with the same variables, then carries on. A macro can't start itself, and macros run inside each other at most 8 deep.
  - `{{VOLUME:50}}` sets the speaker volume to 50%; `{{VOLUME:+10}}` and `{{VOLUME:-10}}` turn it up or down. `{{PLAYSOUND:path}}` plays a .wav or .mp3 file and carries on while it plays. `{{LOCK}}` locks the PC.
  - Tests and runs with input turned off (`--no-input`) skip every action.
- **Conditions** (Macro Kit Pro): `{{IF:left op right}}…{{ELSE}}…{{ENDIF}}` compares after filling in `$names`: `=`, `!=`, `<`, `>`, `<=`, `>=` (numbers compare as numbers, text ignores case) and `~` (contains). `{{IFWINDOW:text}}` is true when the window in front has that text in its title or program name. Blocks nest; `{{ELSE}}` is optional.
- Not supported (Macro Kit says so instead of ignoring them): `OUTPUTTOFILE`, `APPENDTOFILE`, `VARSETFROMFILE`, `VARSETFROMENV`, `SETICONFROMFILE`, `SETKEYTITLE`, functions, `FOR` and `WHILE`.

## Macro Kit Pro

Macro Kit is free and stays free: everything above works without paying. **Macro Kit Pro** is a one-time purchase that supports the work and adds:

| Pro adds | Where |
| --- | --- |
| Conditions in macros (`{{IF}}`, `{{IFWINDOW}}`, `{{ELSE}}`). Repeat, take turns, random and every action are free | Macros, script |
| Scheduled macros: every day at a time (on chosen days), every few minutes, or once at a date and time | Macros, **Run on a schedule** card |

Every macro library pack is free (since 1.5.2); the Conditions pack's examples need Pro to run. Pro goes on sale soon on the [Macro Kit website](https://curtner04.github.io/macro-kit/#pro); until then Macro Kit shows *Pro goes on sale soon* where a Get Pro button would be. With a key, paste it in **Settings → About → Enter license key**.

- License keys start with `MK1-` and are checked on your PC with a public key built into Macro Kit (Ed25519 signatures). The key is never sent anywhere. Macro Kit's only network request is the launcher's update check against the public `macro-kit-downloads` releases on GitHub, which carries nothing about you or your key. The key is saved as `license.key` in the data folder and moves with Export / import.
- Scheduled macros run only while Macro Kit is running (in the tray is fine). A time missed while it was closed is not made up. A run that finds Macro Kit busy waits up to 5 minutes, then is skipped; so is a run that is more than 5 minutes late for any reason (the PC was asleep, say), so it never types into whatever window is open. The schedule card shows when it last ran and how it went (*finished*, *stopped*, *skipped*). A switched-off macro's schedule is paused. A scheduled macro types into the window in front at that moment unless **Where it runs** names a specific window, so pick one.
- Removing the key (**Settings → About → Remove license key**) turns the Pro features off again; macros that use conditions then show an error until a key is entered.
- The source is open. The key generator and the signing key are not in this repository.

## Hotkeys

Defaults (all changeable in Settings; each needs Ctrl, Alt or Win, or an F13 to F24 key):

| Hotkey | Does |
| --- | --- |
| Ctrl+Shift+Alt+R | Start or stop recording |
| Ctrl+Shift+Alt+P | Play or stop the selected recording |
| Ctrl+Shift+Alt+S | Stop whatever is running |
| Ctrl+Shift+Alt+D | Show or hide the Deck |
| Each macro's own hotkey | Run that macro (press again to stop it) |
| Each recording's own hotkey | Play that recording (press again to stop it) |
| Esc | Stops playback and macro runs (can be turned off; never stops a recording) |

Only one activity runs at a time. Starting another is refused with a message. A hotkey another program already uses is reported under it in Settings (Global Keybinds may show which program has it); while it is not set up, the popup and recorder bar do not tell you to press it.

## Your data

Everything is kept in `%LOCALAPPDATA%\Macro Kit\data` (**⋯ > Open data folder**): `settings.json`, `phrases.json`, `macros.json`, `recordings.json`, `keybinds.json` (apps added on the Global Keybinds tab, once you add one), `license.key` (Macro Kit Pro, once you enter a key) and a `recordings` folder. Edits save automatically about half a second after you make them; if a save fails, **Unsaved** appears in the header and Restart waits until it works. A file that is damaged is set aside (never overwritten) and replaced by its `.previous` copy (the version before the last save) when that is good, so your most recent change may be missing; otherwise Macro Kit starts with defaults. Settings, phrases, macros and keybinds also keep a `.session` copy of the file as it was before your first change in a session. A file that exists but cannot be read at the moment (another program has it open) is tried again for up to about a second and a half at startup; if it stays in use it is left alone: Macro Kit uses defaults in memory, never saves over it, and says so (**Data file in use** in the header and in Settings) until you restart it. A recording in use is reported by name and tried again the next time it is needed. Ordinary status messages clear after about 10 seconds; errors stay until something replaces them. The log, which never contains typed text or phrase contents, is `%LOCALAPPDATA%\Macro Kit\logs\macrokit.log`.

## Restart, export and import

- **Restart** (header and tray) saves, closes and reopens the same Macro Kit. It refuses while something is running ("Stop the macro first"); the tray's Restart is greyed out then. It never runs an installer.
- **⋯ > Export / import configuration…** opens the shared Configuration Transfer window to move all your settings, phrases, macros and recordings to another PC (or back them up). Macro Kit saves first and closes while the window is open; start it again afterwards.
- **Exit** (⋯ menu, tray, or the window's close button when closing does not keep it in the tray) stops what is running and saves. If your latest changes, or a recording you had not saved, could not be saved, Macro Kit asks *Your latest changes could not be saved. Exit anyway?* and stays open if you say No. If you changed a file that was in use at startup, Exit and Restart both ask first, because those changes cannot be saved. Signing out or shutting down Windows saves your latest edits and an unsaved recording first.
- Restart keeps how you started Macro Kit: a custom data folder, no-input mode and *hidden in the tray* carry over to the new window.

## Command line

`Macro Kit.exe --version`, `--changelog` (prints to the terminal), `--tray` (start hidden in the tray), `--data <folder>` (use another data folder), `--no-input` (no hooks, hotkeys or input at all: for testing; without `--data` it uses a fresh temporary data folder and never your real one), `--self-test-ui <folder>` (renders every window to PNG files with synthetic data and checks the UI logic; also sends no input).

## Limits

- **Administrator programs.** Windows does not let a normal program type into, or watch, a program that runs as administrator. Phrases, recording and macros then do nothing in that window. Run Macro Kit as administrator too to use it there. Macro Kit warns when the window it runs in is elevated.
- **Clipboard after a pasted phrase.** Most clipboard content comes back (see Phrases); a few kinds that only exist while the source program is open (some drag-and-drop or live-link data) cannot be copied and are not restored. Text copied by the *Set clipboard* macro command does appear in the clipboard history.
- **Hooks.** If Windows drops Macro Kit's keyboard or mouse hook after a stall, Macro Kit notices the late input and puts the hook back. Recording says so when the hook did not start.
- **Phrases are watched with a system-wide keyboard hook.** A trigger typed while another program holds the keyboard exclusively (some games and remote desktop windows) will not expand; add such programs to the exclusion list.
- Recordings store absolute screen positions, so they replay correctly only with the same screen layout and window positions.
- Keys typed during a phrase expansion are held back for a moment and then typed, so fast typing is not lost, but they cannot start a second expansion until the next key.
- Not included on purpose: TinyTask's compile-to-exe, PhraseExpress's clipboard manager, spelling, menus and sync, Super Macro's file, icon, key-title and input-prompt commands, Stream Deck profiles.

## Build

```
powershell -NoProfile -ExecutionPolicy Bypass -File Build.ps1                 builds Macro Kit.exe into this folder
powershell -NoProfile -ExecutionPolicy Bypass -File Build.ps1 -Test           also builds build\MacroKit.Tests.exe
powershell -NoProfile -ExecutionPolicy Bypass -File tests\Test.ps1            builds into a temp folder, runs the logic tests and the UI self test (always; it fails if the hidden-desktop runner is missing)
powershell -NoProfile -ExecutionPolicy Bypass -File tools\Test-Module.ps1 -Module Macros   one module's tests (Core, Keybinds, Macros, Phrases, Recording, Win32)
```

### Adding a keybinds reader

To list another program's shortcuts without anyone typing them in, add a reader in `src\Keybinds` (see `OtherAppReaders.cs` for PowerToys and OBS):

1. Write a static method that takes the program's settings path and a `Func<string, string> readText` (returns the file's text, or null when it does not exist) and returns an `AppKeybinds`. Return null when the program is not installed, so it is left off the list. Put a reading error in `Problem` instead of throwing, and keep what could be read.
2. Add one `Keybind` per shortcut: `Keys` as a `Hotkey` (use `KeyText.TryParse` for text), a plain-words `Action`, `Scope` (`Global` for any window, `AppActive` for only while the program is active), `Saved = true` for shortcuts the user set, and a `Note` when it helps. Set `Processes` to the program's process names (without `.exe`) so the card can say whether it runs.
3. Add the path to `KeybindLocations` (with its default under `%LOCALAPPDATA%` or `%APPDATA%`) and call the reader in `KeybindCatalog.Read`.
4. Add tests to `tests\KeybindsTests.cs` that feed the reader made-up files through `readText`: switched-off features, missing files, a damaged file.

Each module run compiles `src\Core` and `src\<Module>` with its own tests; `Macros` also runs `ActionsTests`, `PlainMacroTests`, `ProTests`, `RecordingToMacroTests` and `ExamplesTests` (and compiles `src\Keybinds` and `src\Recording` for the last), while the others run their own `<Module>Tests` file. `tests\Test.ps1` runs every test file.

The build also compiles a few shared sources (restart handoff, launcher handoff, single-instance guard, configuration-transfer launcher, changelog reader, version attributes) from a `_shared` folder next to `Build.ps1`, or from `C:\dev\App Maintenance\shared`; they live in a separate repository.

Tests never send real input, install hooks, register hotkeys, touch the clipboard or change focus. Anything that opens a window runs on a hidden desktop (`C:\dev\Toolchains\HiddenDesktop\Invoke-OnHiddenDesktop.ps1`). Installers are only built by the 4:00 AM Daily App Installer Queue. See `DESIGN.md` for the full specification.

### Launcher

Open the app from App Management or your usual shortcut to check for signed updates at startup. The build makes two programs: `Macro Kit.exe`, the app, and `Macro Kit Launcher.exe` beside it, which checks for updates and starts the app. The launcher stages new binaries separately and switches versions after the new app starts. Running sessions are left open. Restart inside the app relaunches the current build. The 4 AM installer queue publishes the signed update alongside the installer.
