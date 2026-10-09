TranslationTools
================

A console tool for carrying a visual novel translation between NScripter and SiglusEngine
builds of a game: extract, split, edit, join, build, run.

What ships with it
------------------

- ONScripter-EN in Tools\ONScripter\ - the NScripter engine the tool can run a game
  with when the game folder has no engine of its own. Build dated 2011-06-28, maintained by
  "Uncle" Mion Sonozaki (http://onscripter.unclemion.com). Licensed under the GNU General
  Public License; the license text is Tools\ONScripter\GPL.txt. Source for ONScripter-EN
  is available from the maintainer's site; a copy can also be requested from this project.
- Update-SiglusSsu in Scripts\ - installs or updates siglus-ssu, the SiglusEngine
  compiler, together with a Python that satisfies it. The tool offers to run it when the
  compiler is missing. siglus-ssu itself is not shipped; it is fetched from PyPI.

Nothing else third-party is bundled. NScripter's nscript.dat is decoded and encoded by
the tool itself.

Releases
--------

A release is a zip on the repository's Releases page, built self-contained for 64-bit
Windows: unzip it anywhere and run TranslationTools.exe. Nothing has to be installed
first, not even .NET; the tool fetches the SiglusEngine compiler itself when a Siglus game
needs it. The manual, MANUAL.txt, is in the zip beside the exe. The version is the
four-part number on the release tag, v0.1.0.0 for the first alpha.

Roadmap
-------

This tool is a rewrite, feature by feature, of an earlier console tool that is not
published: the code exists and works, but it grew around one game's translation and is
tied to that project's folders and to a machine with Python and other helpers already in
place. The rewrite carries each feature over in a shape that works for any game on either
engine, for a user with nothing installed. Until the list below is done, the old tool is
still the one doing the day-to-day translation work.

The 0.1 line is the alpha: dialogue files are healthy, cross-comparable between
checkpoints, and build back into the game's source, so a translator gets an easier
pipeline than editing the script files directly.

Done so far:

- extract, split, join, build and run for both engines, with backups by choice
- split grain: one dialogue file per function, or one for the whole script
- word wrap for SiglusEngine, and the text repairs that keep a Siglus script compiling
- the glossary: characters with a list of names and a selected written name, speaker
  rules, copying and taking between checkpoints
- learning: the cast from VNDB, the speaker-tag shape from one file, the names pass over
  every tag in the script, and names that stand on their own line
- alignment between two versions of a game: pairing, the walk, apply, speaker repair,
  and the stamps that record what matched what
- choices stamped at split so their options are ordinary dialogue entries
- the language model connection: endpoint, model, key, presets, test, and the two
  languages a translation runs between

Still to carry over, in this order:

1. The dialogue editor core. Moving through files and lines, finding text, jumping to
   a file, filtering by scene or by speaker, bookmarks, copying lines or ranges to the
   clipboard and replacing them from it, and the writing path that puts translated text
   back into the engine's encoding.
2. Scratchpads. Per-line candidate translations from several sources, kept beside the
   working draft, with a review step before a candidate becomes the line.
3. The translation gate. A file is ready for a translation pass only when its stamps
   still match what is on disk and its partner checkpoint is locked.
4. Translation passes. Translating a range through the language model in windows with
   context, editable prompt sections, a file summary, and a progress tracker.
5. Retiring the old tool once everything above is proven in game.

Behind those: the dash substitutions when text is written into a CP932 script, and a
faster character list on very large splits.

Building from source
--------------------

You need the .NET 9 SDK (https://dotnet.microsoft.com/download). Visual Studio is optional.

1. Clone with the submodule. The TALOREAL_NETCORE_API library lives in its own repository
   and is pulled in under lib\:

   git clone --recurse-submodules https://github.com/Taloreal/Translation-Tools.git

   If you cloned without the flag, lib\TALOREAL_NETCORE_API is empty; fill it with:

   git submodule update --init

2. Copy Secrets.cs.sample to Secrets.cs in the project folder. Leave its two values
   empty unless you run your own build-mode service: with them empty the tool builds and
   runs, the shared list of known games is simply unavailable and reporting stays off. The
   file is ignored by git, so it is never committed.

3. Build:

   dotnet build TranslationTools.csproj

   or open TranslationTools.sln in Visual Studio and build there. The exe, the manual, the
   compiler installer and the bundled engine land in bin\Debug\net9.0\.

The service the tool reports build modes to is not part of this repository.
