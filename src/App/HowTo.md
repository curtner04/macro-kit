# Macro Kit how-to guides

## Start here {#start}

Simple steps do what they say: a shortcut such as `ctrl+c`, a pause such as `wait 100ms`, text such as `type "hello"`, or a `click`. These guides cover the steps that do more than one thing at once: holding keys, repeating, waiting for windows, colours and pictures, variables and conditions.

**Where the commands are**

1. Click the **Macros** tab at the top of Macro Kit, then click a macro in the list on the left (or press **New macro** under the list).
2. Press **Show all commands**, at the bottom right of the box with the macro's steps.

![Show all commands is at the bottom right of the steps box.](commands.png)

3. The commands sit in groups under the steps, such as **Keys**, **Windows** and **Pictures**. Click a group's name to open it.

- Each group has a **How it works** link at the bottom that opens its guide here.
- Type a word in **Find a command** (for example *loop* or *picture*) to show only the groups that match.
- Most guides have a **Show me** button at the top. It switches Macro Kit to the place the guide is about and outlines it in red.

**Reading the examples**

- Each example is a macro written in **Lines**, one step per line. Type or paste it into a macro's steps to try it.
- The same steps in code (what **Show code** shows, and what a Stream Deck's Super Macro uses) are given under the example.
- Steps marked *Macro Kit only* run in Macro Kit but not on a Stream Deck. Copying a macro's code says when it has one.
- Steps marked *Pro* need Macro Kit Pro.

## Hold a key down {#keys}

Use this to keep a key pressed while other steps run: Alt held while Tab is pressed several times, Shift held while you click to select a range, or Ctrl held while you click several files.

**Where to find it**

1. Click the **Macros** tab at the top of Macro Kit, then click a macro in the list on the left (or press **New macro** under the list).
2. Press **Show all commands**, at the bottom right of the box with the macro's steps.

![Show all commands is at the bottom right of the steps box.](commands.png)

3. In the list of groups that opens under the steps, click **Keys**.

![The Keys group. Hold or release a key is near its bottom.](keys.png)

**Show me**, at the top of this guide, does all of that for you and outlines the place in red.

**How to add it**

1. In the **Keys** group, find **Hold or release a key**.
2. Choose the key, then press **Hold down**.
3. Add the steps that should happen while it is held.
4. Choose the same key again and press **Release**.

**How it works**

- `hold alt` presses Alt and keeps it down. Every step after it runs with Alt held, until `release alt` lets go.
- A shortcut step such as `alt+tab` presses its keys and lets go of them straight away. Two `alt+tab` steps in a row therefore switch to the previous window and back again. To reach the third window, Alt has to stay down while Tab is pressed twice.
- If a macro ends or is stopped while it still holds a key, Macro Kit lets go of the key for you.
- While a key is held, typed text is typed with that key down, so release it before a `type` step.

**Example: go to the third window in Alt+Tab**

```
hold alt
tab
tab
release alt
```

Code: `{{KEYDOWN:ALT}}{{TAB}}{{TAB}}{{KEYUP:ALT}}`

**Example: select from the first row to the fifth**

```
click
hold shift
down 4 times
release shift
```

**Tips**

- With **Window** (in the macro's **Options**) set to *Target window* and *Stop if a different window comes to the front* on, a macro can only go between two windows. Landing on a third one stops it. Use *Active window* for macros that move through several windows.
- `shift+tab` while Alt is held goes back one window, as it does when you press the keys yourself.

## Repeat, take turns and run other macros {#multi}

The **Multi action** group puts steps inside blocks. Select the lines first and the block goes around them. With nothing selected, an empty block goes in at the cursor.

**Where to find it**

1. Click the **Macros** tab at the top of Macro Kit, then click a macro in the list on the left (or press **New macro** under the list).
2. Press **Show all commands**, at the bottom right of the box with the macro's steps.

![Show all commands is at the bottom right of the steps box.](commands.png)

3. In the list of groups that opens under the steps, click **Multi action**.

![The Multi action group: Repeat, run another macro, take turns.](multi.png)

**Show me**, at the top of this guide, does all of that for you and outlines the place in red.

**Repeat some steps**

- `repeat 5 times` … `end repeat` runs the steps between them 5 times (1 to 100,000) before going on.
- The count can be a variable: `repeat $count times`.
- This repeats part of a macro. To repeat the whole macro, use **Repeat** in the macro's options instead.

```
repeat 3 times
ctrl+c
alt+tab
ctrl+v
alt+tab
down
end repeat
```

Code: `{{LOOP:3}}` … `{{ENDLOOP}}`

**Take turns**

- `take turns` … `or` … `end turns` does the next choice each time the macro reaches the block: the first choice the first time, the second choice the next time, then the first again.
- So one hotkey can do two things in turn, such as turning something on and then off.
- Macro Kit remembers whose turn it is until it closes.
- Add choices with **Take turns** and the number of choices, and **Next choice** for another `or`.

```
take turns
type "Good morning"
or
type "Good afternoon"
end turns
```

Code: `{{SWITCH}}` … `{{OR}}` … `{{ENDSWITCH}}`

**Pick at random**

- `pick at random` … `or` … `end random` does one of the choices, picked at random each time.

Code: `{{RANDOM}}` … `{{OR}}` … `{{ENDRANDOM}}`

**Run another macro**

- `run macro Copy row` runs your macro called *Copy row* once, then carries on with the next step.
- It runs in the same window and shares the same variables, so a macro can set `$row` and the one it runs can use it.
- Use it for steps that several macros share: change them in one place.
- A macro can't run itself, and macros run inside each other at most 8 deep.
- If no macro has that name, Macro Kit points it out under the steps and in the macro's options.

Code: `{{MACRO:Copy row}}`

## Open a program and wait for it {#apps}

**Where to find it**

1. Click the **Macros** tab at the top of Macro Kit, then click a macro in the list on the left (or press **New macro** under the list).
2. Press **Show all commands**, at the bottom right of the box with the macro's steps.

![Show all commands is at the bottom right of the steps box.](commands.png)

3. In the list of groups that opens under the steps, click **Apps and websites**.

![The Apps and websites group.](apps.png)

**Show me**, at the top of this guide, does all of that for you and outlines the place in red.

**Open something**

- `open notepad` opens a program, file, folder or web address, as if you had double-clicked it. Press **Browse…** in **Apps and websites** to pick a file.
- `open https://example.com` opens a web page in your browser.
- To give a program arguments, put its path in quotes in code: `{{RUN:"C:\Tools\my app.exe" --quiet}}`.
- `close notepad` asks the program's windows to close, as their close button would. Unsaved work still asks first, and a program that isn't open is skipped.
- `lock pc` locks Windows, like Win+L.

**Wait for its window**

- A program takes a moment to open, and keys sent before its window is ready go nowhere. Put `wait for` straight after `open`.
- `wait for Notepad` waits until a window whose title or program name contains *Notepad* is open, then carries on in it. It waits 10 seconds unless you say (`wait for Notepad 30s`), up to an hour.
- Straight after an `open`, it waits for the window that open makes. A matching window that was open all along is used only after about 5 seconds with no new one.
- If no window opens in time, the macro stops and says so.

```
open notepad
wait for Notepad
type "Hello from Macro Kit"
```

Code: `{{RUN:notepad}}{{WAITWINDOW:Notepad}}Hello from Macro Kit`

## Switch windows, click in a window, wait for a colour {#windows}

**Where to find it**

1. Click the **Macros** tab at the top of Macro Kit, then click a macro in the list on the left (or press **New macro** under the list).
2. Press **Show all commands**, at the bottom right of the box with the macro's steps.

![Show all commands is at the bottom right of the steps box.](commands.png)

3. In the list of groups that opens under the steps, click **Windows**.

![The Windows group. Wait until a spot turns a colour is at its bottom.](windows.png)

**Show me**, at the top of this guide, does all of that for you and outlines the place in red.

**Switch to a window**

- `switch to Excel` brings the window whose title or program name contains *Excel* to the front and carries on in it.
- Use it at the start of a macro, or before steps for another program, instead of counting Alt+Tabs.
- If no such window is open, the macro stops and says so.

Code: `{{FOCUS:Excel}}`

**Click a spot in a window** (Macro Kit only)

- `move mouse to 120, 40 in the window` puts the pointer 120 pixels from the left and 40 from the top of the window in front. A `click` after it clicks there.
- The spot is measured from the window, so it still hits the same place after the window has moved. Put `switch to` before it so the right window is in front.
- To find the numbers, use **Pick…** in the colour row below: it measures from the window in front.
- `move mouse to 120, 40` without *in the window* is measured from the top-left corner of the main screen instead.

Code: `{{WINDOWXY:120,40}}{{LBUTTON}}`

**Wait for a colour** (Macro Kit only)

Use this when something on screen changes colour once it is ready: a Save button that lights up, a progress bar that turns green, a status light.

1. In the **Windows** group, find **Wait until a spot turns a colour**.
2. Press **Pick…**, then switch to the other window and point at the spot within 3 seconds, while it shows the colour to wait for.
3. Macro Kit fills in the spot (measured from the window in front) and its colour, and puts the window's name in the box above, ready for **Switch to it**.
4. Change **up to** to how many seconds it may wait, then press **Add**.

How it works:

- Ten times a second, Macro Kit reads that one spot in the window in front.
- When red, green and blue are each within 10 of the colour, it carries on.
- The spot is measured from the window's top-left corner each time, so it follows a window that moves.
- It waits 10 seconds unless you say, up to an hour. If the colour never comes, the macro stops and says what colour the spot was instead, which helps you fix it.

```
switch to Excel
wait for colour #1E90FF at 120, 40 in the window 5s
move mouse to 120, 40 in the window
click
```

Code: `{{WAITPIXEL:120,40:#1E90FF:5000}}`

Tips:

- Pick the middle of a solid area. The edges of text and icons are blends of colours that change with the background.
- A dark or light theme, or a highlight under the pointer, changes colours. Pick the spot as it will look when the macro runs.

## Wait for a picture, or click it {#pictures}

*Pro, Macro Kit only.* Use this when a button or icon shows up in different places, or only once something is ready. Macro Kit looks for it by how it looks, not where it is.

**Where to find it**

1. Click the **Macros** tab at the top of Macro Kit, then click a macro in the list on the left (or press **New macro** under the list).
2. Press **Show all commands**, at the bottom right of the box with the macro's steps.

![Show all commands is at the bottom right of the steps box.](commands.png)

3. In the list of groups that opens under the steps, click **Pictures**.

![The Pictures group: the name and size of the picture, Capture, then Wait for it or Click it.](pictures.png)

**Show me**, at the top of this guide, does all of that for you and outlines the place in red.

**Save a picture**

1. Open the **Pictures** group, as above.
2. Type a name for the picture, such as *Save button*. Leave it empty and it is called *Picture 1*, *Picture 2* and so on.
3. The box beside the name is the picture's size: a square 48 pixels across unless you change it (8 to 256).
4. Press **Capture…**, switch to the other window, and point at the middle of the button within 3 seconds.
5. Macro Kit saves the square around the pointer and shows it beside **Wait for it**. Capturing again with the same name replaces it.

**Use it**

- **Wait for it** adds `wait for picture Save button`: the macro waits until the picture shows, then carries on.
- **Click it** adds `click picture Save button`: the macro waits for the picture, then moves the pointer to its middle and clicks it with the left button.
- Change **up to** first to say how many seconds it may wait. It is 10 unless you say, up to an hour.

**How it works**

- Four times a second, Macro Kit copies the window in front and looks for the picture anywhere in it.
- Small differences still count as found: each of red, green and blue may be off by 24, and 1 pixel in 50 may differ (a blinking cursor, a little glow).
- If the picture shows more than once, the top one is used (the left one when two are level).
- Where it was found is kept in `$IMAGE_X` and `$IMAGE_Y`: the middle of the picture, in screen pixels. Later steps can use them.
- If the picture doesn't show in time, the macro stops and says which picture it was waiting for and in which window.

```
switch to Excel
click picture Save button
wait 500ms
type "Saved"
```

Code: `{{FOCUS:Excel}}{{CLICKIMAGE:Save button}}{{PAUSE:500}}Saved`

**Tips**

- Capture something that looks the same every time: an icon or a button's border, not text that changes, such as a file name or a count.
- Many buttons light up while the pointer is on them, so the captured picture shows them lit, and the macro looks for that. If a button changes under the pointer, point just beside it and make the size bigger, so the square still covers the button.
- Smaller pictures are found faster, but they need enough detail not to match something else. 32 to 64 pixels is a good start.
- A picture is found only at the size it was captured. After changing Windows' display scaling, or zooming in the program, capture it again.
- A dark or light theme changes colours. Capture the button the way it will look when the macro runs.
- Pictures are saved as PNG files in the data folder's `images` folder. **Open folder** shows them, to rename or delete them. The file's name is the picture's name, so a macro that uses it needs the new name after a rename.
- Pictures move with **Export / import configuration**.

## Variables, the clipboard and asking {#vars}

A variable is a named value that a macro remembers while it runs. It lives for one run, through all of its repeats.

**Where to find it**

1. Click the **Macros** tab at the top of Macro Kit, then click a macro in the list on the left (or press **New macro** under the list).
2. Press **Show all commands**, at the bottom right of the box with the macro's steps.

![Show all commands is at the bottom right of the steps box.](commands.png)

3. In the list of groups that opens under the steps, click **Variables and clipboard**.

![The Variables and clipboard group.](vars.png)

**Show me**, at the top of this guide, does all of that for you and outlines the place in red.

**Set and use a value**

- `set $row = 5` remembers 5 as `$row`. The **Variables and clipboard** group has **Set**.
- `type $row` types its value (**Type it**).
- `$row` in another step uses its value there: `repeat $count times`, `switch to $app`, `if $row > 10`.
- `forget $row` forgets one value, and `forget all variables` forgets them all.
- Names use letters, numbers and `_`.

**The clipboard**

- `set $text = clipboard` keeps what is on the clipboard now (**From the clipboard**). Copy first with `ctrl+c`.
- `set clipboard to Hello` puts text on the clipboard (**Value to the clipboard**).

**Ask while the macro runs**

- `ask for $name "Who is this for?"` stops and asks in a small window, then keeps your answer as `$name`.
- **Continue** or Enter carries on. **Stop the macro** or Esc stops it.
- Keys the macro was holding are let go while you answer and pressed again afterwards.

```
ask for $name "Who is this for?"
type "Dear "
type $name
```

Code: `{{INPUT:name:Who is this for?}}Dear {{OUTPUT:name}}`

**Built in**

- `$DATE` and `$TIME` are this PC's short date and time when the step runs.
- `$MOUSE_X` and `$MOUSE_Y` are where `remember mouse position` saved the pointer.
- `$IMAGE_X` and `$IMAGE_Y` are where the last picture step found its picture.

## Do steps only if something matches {#conditions}

*Pro.* A condition runs some steps only when a value or the window in front matches.

**Where to find it**

1. Click the **Macros** tab at the top of Macro Kit, then click a macro in the list on the left (or press **New macro** under the list).
2. Press **Show all commands**, at the bottom right of the box with the macro's steps.

![Show all commands is at the bottom right of the steps box.](commands.png)

3. In the list of groups that opens under the steps, click **Conditions**.

![The Conditions group: If a value matches, If the window is, Otherwise.](conditions.png)

**Show me**, at the top of this guide, does all of that for you and outlines the place in red.

**How to add it**

1. Open the **Conditions** group, as above.
2. In **If a value matches**, fill in the two sides and how to compare them, then press **Add**. Or name a window in **If the window is** and press **Add**.
3. Add the steps for when it matches. Add **Otherwise** and the steps for when it doesn't, if you want them.

**How it works**

- `if $count > 3` compares after putting in the variables' values. The comparisons are `=`, `!=`, `<`, `>`, `<=`, `>=` and `~` (contains).
- Numbers compare as numbers. Text compares without minding capitals.
- `if window Excel` matches when the window in front has *Excel* in its title or program name.
- The steps up to `otherwise` (or `end if`) run when it matches. The steps from `otherwise` to `end if` run when it doesn't. `otherwise` is optional, and blocks can go inside each other.

```
if window Excel
ctrl+s
otherwise
type "This only saves in Excel"
end if
```

Code: `{{IFWINDOW:Excel}}{{CTRL}{S}}{{ELSE}}This only saves in Excel{{ENDIF}}`

## Choose where a macro types {#where}

The **Window** option decides which window gets the macro's keys and clicks.

**Where to find it**

1. Click the **Macros** tab at the top of Macro Kit, then click a macro in the list on the left.
2. Under the macro's steps, in the **Options** row, click **Window**. The card **Which window it types into** opens under it.

![Options, Window: which window the macro types into.](where.png)

**Show me**, at the top of this guide, opens it for you.

**Which window it types into**

- *Active window: the one I was using last*. The window you were in before you came to Macro Kit. From the Deck or a hotkey, the window in front right now. Most macros want this.
- *Target window: one window I pick*. Always one program's window, found by its program name (and part of its title, if you add **Title contains**). Choose it from the list, or press **Pick** and switch to it. If it isn't open, the macro stops instead of typing somewhere else.
- *Stop if a different window comes to the front* ends the run when something else pops up in front.

**When the macro presses Alt+Tab, go to**

- *The next window, as Alt+Tab normally goes* uses the real Alt+Tab.
- *A second target window I pick* is for copy-and-paste macros between two programs, such as Excel and a browser. Every `alt+tab` goes straight between those two, however many other windows are open.

## Run a macro on a schedule {#schedule}

*Pro.* A schedule runs a macro by itself: every day at a time (on the days you choose), every few minutes, or once at a date and time.

**How to set it up**

1. Click the **Macros** tab, then the macro in the list on the left.
2. Under the macro's steps, in the **Options** row, click **Schedule**.

![Options, Schedule: how often and when.](schedule.png)

3. Choose how often and when.
4. Click **Window** in the same row and choose *Target window*, so it types into the right window. Otherwise it types into whatever window is in front at that moment.

**How it works**

- Schedules run only while Macro Kit is running. In the tray is fine. A time missed while it was closed is not made up.
- If Macro Kit is busy, the run waits up to 5 minutes, then is skipped. So is a run more than 5 minutes late for any reason (the PC was asleep), so it never types into whatever happens to be open.
- The card shows when it last ran and how it went: *finished*, *stopped* or *skipped*.
- Switching a macro off pauses its schedule.

## Turn a recording into a macro {#recording}

**Convert to macro** on the **Recorder** tab turns the selected recording into a macro you can read and change. Click the recording in the list first; the button is with the recording's other buttons. **Show me** points at it.

**What it keeps**

- Clicks with where they happened, double clicks, drags, typed text, shortcuts, scrolls, and the pauses between them, one step per line.
- Mouse movement between clicks is left out: the pointer jumps to each click.
- With **Clicks follow their window** on, each window's steps start with `switch to` and its clicks are measured from that window, so they still land after the window moves.
- Alt or Win held over several keys stays held: holding Alt and pressing Tab three times becomes `hold alt`, three `tab` steps and `release alt`, so it reaches the same window.
- The pauses follow the recording's speed: at 2x they are half as long.
- Typed characters are read with a US keyboard layout.

**Tidying it up**

- Long pauses are often just you thinking. Shorten them, or trim the recording first (**Shorten pauses** under **Trim**).
- Replace a fixed pause before a window is ready with `wait for` the window, `wait for colour` or `wait for picture`, so the macro waits exactly as long as it needs.
