# TranslationTools - User Manual

TranslationTools is a keyboard-driven program for carrying a visual novel translation
through a game's script: take the script out of the game, split it into editable text
files, edit them, put them back together, rebuild the game's script file, and run the game
to see the result. It works with two engines, NScripter and SiglusEngine, and it tells
them apart by looking at what is on disk.

You need nothing installed to use it. Anything else it needs, it finds, installs, or asks
you for.

This manual grows with the tool. Where something is not built yet, it says so.


## 1. The words the tool uses

**Checkpoint.** A folder the tool is keeping track of. It holds one game's script file and,
as you work, the pieces the tool makes from it. Everything you do, you do to a checkpoint.

**Master.** The game's script file at the top of the checkpoint: `Scene.pck` for a
SiglusEngine game, `nscript.dat` for an NScripter game. The master is what the game
actually loads.

**Label.** The name you give a checkpoint. It must be unique. The tool refers to the
checkpoint by it everywhere.

**Engine.** Which kind of game it is, NScripter or SiglusEngine. The tool never asks; it
reads it off the master's name each time it looks.

**Writable / locked.** A checkpoint is writable when the tool may change it, locked when it
may not. New checkpoints are writable. You can lock one to protect it. A checkpoint that
needs repair locks itself until Recover has fixed it.

**Game install.** An installed copy of a game on this computer: its folder and the program
that starts it. The tool remembers these so you only answer the questions once.


## 2. What a checkpoint looks like on disk

A checkpoint is a folder with a fixed shape. The tool makes the pieces; you never have to
arrange them.

```
MyCheckpoint\
    Scene.pck  or  nscript.dat     the master
    extract\                        the script as text, taken out of the master
    split\                          the editable files, cut out of extract\
    backups\                        every copy the tool took before changing something
    checkpoint.log                  what happened here, with dates
    checkpoint.info                 which game this is
    checkpoint.hashes               what the sources looked like at the last build
```

Nothing else may sit at the top of the folder. If something else is there, the tool calls
the checkpoint invalid and says what it found, and it will not work on it until the extra
thing is gone.

The pieces form a loop:

```
master  --Extract-->  extract\  --Split-->  split\  --(you edit)-->  --Join-->  extract\  --Build-->  master
```

`backups\` is yours. The tool only ever adds to it. If something goes wrong, the previous
version is in there with a date and time in its name.


## 3. Starting out

Run `TranslationTools.exe`. The first screen is the checkpoint selector, which is also the
main menu.

The top line shows the selected checkpoint: its label, what state it is in, which game it
is if known, and whether it is writable. Press **Left** or **Right** to move through your
checkpoints. Press **Enter** on that line to open the Checkpoints menu, where you add,
rename, fork and remove them.

Below it:

- **Extract / Split / Join / Build** - the operations, on the selected checkpoint.
- **Settings**.
- **Exit**.

Move with **Up** and **Down**, choose with **Enter**. Questions that need typed text are
answered with the keyboard and **Enter**; a blank answer always cancels.


## 4. Adding your first checkpoint

Checkpoints menu, **Add a checkpoint**.

1. Type a label.
2. Type the path of a folder or of a master file.

If the path you give is inside an installed game - the folder holding the game's own
`Scene.pck` or `nscript.dat` - the tool notices and offers to make a checkpoint for you:
a new folder named after the label, under your checkpoints folder, holding a **copy** of the
game's master. Say yes. The game's own files are never touched, and the tool remembers
where the game is for Run later.

The checkpoints folder is set the first time it is needed, and can be changed under
Settings.

You can also point a checkpoint at a folder you arranged yourself, as long as it has the
shape in section 2.


## 5. The operations

Open with **Extract / Split / Join / Build** on the main menu. The header shows the selected
checkpoint and its state.

### Extract - master to extract\

Takes the script out of the master as text files.

- NScripter: decodes `nscript.dat` into `extract\0.txt`.
- SiglusEngine: needs the SiglusEngine compiler. If it is not installed the tool offers to
  install it, which takes a few minutes the first time. Then it asks which game this is,
  because the compiler needs to know how that game was built. Pick from the list, or
  choose *Another name...* and type it; the tool looks the title up so the name is
  spelled the standard way. The tool then works out the build settings, which can take a
  few minutes, and extracts the scenes into `extract\`.

If `extract\` already has files in it, you are asked whether to back them up first,
overwrite them, or cancel.

### Split - extract\ to split\

Cuts the script into one file per section, with the dialogue separated from the code so
you can edit text without touching the script around it.

- NScripter: `split\dialogues\` holds the lines, `split\functions\` holds the code, and two
  key files list them in order.
- SiglusEngine: `split\` holds a working copy of every scene with each line of text
  replaced by a numbered marker, and `split\dialogues\` holds one file per section of the
  script with the lines themselves. A comment under each section in the working copy
  names its dialogue file. Two scenes that contain the same block of script share one
  dialogue file, so you translate it once. Two scenes that use the same section name for
  different blocks get separate files, the second named `SECTION_VER001.txt`; the tool
  tells you when it does this. A voice call that shares a line with its text in an
  untouched script is moved onto its own line, so the dialogue file holds only the text;
  the tool says how many it moved. Bare Japanese nametags, dialogue and narration are put
  in quotes, which the engine needs before it will show English in their place: a nametag
  becomes `【"name"】`, a spoken line becomes `"「...」"`, a narration line is quoted whole.
  An `nl` or `r` sitting between Japanese characters is the engine's line break or wait,
  and stays outside the quotes: `"left"nl"right"`. A character the original already had in
  quotes to keep the engine from obeying it, such as `"-"`, is folded into the quoted line
  as plain text.
  Lines already quoted are left as they are. An empty nametag or an unmatched bracket is
  reported and left alone. After the split it asks for a governing wrap width, see *Word
  wrap* below.

Split refuses to run when the checkpoint is already split, because splitting again would
throw away whatever you have edited. See *Start over* below.

### Join - split\ to extract\

Puts the edited files back together into the script in `extract\`. The split stays; you can
keep editing and join again.

A line you left empty, or a line the script asks for that is not in the dialogue file,
is not shown in the game. Instead the joined script carries a comment starting
`;ERROR:` (NScripter) or `//ERROR:` (SiglusEngine) that names the file to look in. Join
tells you how many there were. Search the joined scripts for `ERROR:` to find them all.

For SiglusEngine, Join also wraps English prose to fit the game's text window. See *Word
wrap* below. The support files in `split\` are copied over the ones in `extract\` every
time, so an edit to one of them, such as a new variable declared in an `.inc` file, reaches
the build. A dialogue file missing from the split stops the join before anything is
written; the split is damaged and nothing in `extract\` changes.

### Word wrap (SiglusEngine)

The game's text window holds a fixed number of characters per line. Join can break long
English lines at word boundaries so they fit.

- In a scene's working copy, a comment line `// wrap 60` wraps the lines after it at 60
  characters. The game ignores the comment; only the tool reads it.
- `// wrap off` goes back to the checkpoint's governing width.
- `// wrap 0` wraps nothing from there on.
- The setting resets at every section (`#label`) and at the end of the file, so a width
  never carries past the section it was set in.
- The **governing width** is the checkpoint's own, asked for after a Siglus split and
  changeable under the Checkpoints menu. It applies to any line with no `// wrap` comment
  in force above it. With no governing width, only scenes that say `// wrap` are wrapped.
- Your answer is remembered for the game. Another checkpoint whose master is the same game,
  patched or translated or not, gets the same width after its split without being asked;
  the tool says so, and the Checkpoints menu changes it.

A single word longer than the width cannot be broken; Join warns and names the line.
Japanese text has no spaces to break at and is never changed.

### Build - extract\ to master

Rebuilds the master from `extract\`. The old master is copied into `backups\` first, every
time.

- NScripter: encodes `0.txt` into `nscript.dat`.
- SiglusEngine: compiles the scenes with the build settings recorded at Extract. If none
  were recorded, Build first asks whether you can point it at an untouched `Scene.pck` of
  the same game, which is the surest way to find them.

### Run - install the master into the game and start it

Copies the master over the installed game's own script file and starts the game. The
game's file is backed up into the checkpoint's `backups\` first, every time.

The first time, Run asks which installed game to use. If you have told it about one before
for this engine, it offers that; otherwise it asks for the game's folder, and which program
in it starts the game if there is more than one. These answers are remembered as a game
install, and every checkpoint of that game can use it.

### Recover - rebuild a missing piece

A checkpoint missing a piece that can be rebuilt locks itself as soon as the tool
notices, and nothing else runs on it until Recover has put the piece back.

- NScripter: a missing `nscript.dat` is rebuilt from `extract\0.txt`; a missing `0.txt` is
  rebuilt from a complete split.
- SiglusEngine: a missing `extract\` is rebuilt from a complete split, support files
  included, since Split copies them. A missing `Scene.pck` is compiled from `extract\`.
  That is a rebuild, not the original file; it needs the build settings, and Recover takes
  the checkpoint's, else the ones the last master was built with, else the standard ones,
  warning you each step down. If the game then will not run, Build can look again from an
  untouched `Scene.pck`.


## 6. Start over

Extract and Split each carry a **start over** switch, shown at the end of their menu line.
With the operation highlighted, press **Left** or **Right** to turn it on. It is off every
time the menu opens, and turns off again after it is used.

With it on, the operation no longer refuses a checkpoint that is already split. Instead it
asks what to do with the split: back it up into `backups\` first, discard it, or cancel.
Extract clears `extract\` as well; Split clears only `split\`. Then it runs as normal.

The switches are separate on purpose. Turning on start over for Extract does nothing to
Split, and the other way round.


## 7. The Checkpoints menu

Press **Enter** on the selector line.

- **Add a checkpoint** - section 4.
- **Rename this label** - a new name; blank keeps the old one.
- **Re-point this path** - the same checkpoint, a different folder.
- **Lock / Unlock this checkpoint** - a locked checkpoint is refused by every operation.
  A checkpoint that needs recovery cannot be unlocked by hand.
- **Remove this checkpoint** - takes it off the list. The folder stays on disk.
- **Wrap width for Join (Siglus)** - the governing wrap width; see *Word wrap* in
  section 5. Blank keeps it, 0 clears it.
- **Fork this checkpoint** - makes a copy under your checkpoints folder with a new label:
  the master, `extract\`, `split\` and the small files, but not `backups\`. The copy is
  selected and writable. Use it to try something without risking the original.
- **Open this checkpoint in Explorer** - opens its folder.


## 8. Settings

- **Checkpoints folder** - where new checkpoints made by Add and Fork go, one folder per
  label.
- **Send build-mode reports** - SiglusEngine only. When the tool has worked out a game's
  build settings the slow way, it can send them to a shared list so the next person with
  the same archive skips the wait. What it sends: a fingerprint of the archive, its size,
  the game's name, the settings, and the compiler's version. No script text, no personal
  details. Off until you say yes.
- **Build-mode service address** - where those reports go and come from. "Built-in"
  restores the default.
- **Game installs...** - every installed game the tool knows: its folder, what starts it,
  and which checkpoints use it. Change the starter program or its arguments, or remove an
  install; the checkpoints that used it will ask again.


## 9. When something is wrong

**The row says "invalid: ..."** The folder does not have the shape in section 2. The reason
says what is off: both masters present, no master, an extra file at the top level, a split
with a piece missing. Fix the folder and the row updates by itself.

**The row says "warning: ... rebuildable" and the checkpoint is locked.** Run Recover.

**The row shows a WARNING about the build mode.** SiglusEngine only. The tool could not
work out how this game was built and used the standard settings. The game may not accept
the result. Build will ask for an untouched `Scene.pck` to try again.

**Something was overwritten that you wanted.** Look in the checkpoint's `backups\`. Every
file the tool replaced is there, named with the date and time.

**You want to know what happened.** Open `checkpoint.log` in the checkpoint's folder. Every
warning and error the tool raised about that checkpoint is in it, newest at the bottom.


## 10. Not built yet

- The dialogue editor. Until it exists, edit the files under `split\dialogues\` with any
  text editor that saves Shift-JIS (code page 932) without a byte-order mark.
- The integrity check that gates Build on the sources matching what was last compiled.
