# TranslationTools

A console tool for carrying a visual novel translation between NScripter and SiglusEngine
builds of a game: extract, split, edit, join, build, run.

## What ships with it

- **ONScripter-EN** in `Tools\ONScripter\` - the NScripter engine the tool can run a game
  with when the game folder has no engine of its own. Build dated 2011-06-28, maintained by
  "Uncle" Mion Sonozaki (http://onscripter.unclemion.com). Licensed under the GNU General
  Public License; the license text is `Tools\ONScripter\GPL.txt`. Source for ONScripter-EN
  is available from the maintainer's site; a copy can also be requested from this project.
- **Update-SiglusSsu** in `Scripts\` - installs or updates `siglus-ssu`, the SiglusEngine
  compiler, together with a Python that satisfies it. The tool offers to run it when the
  compiler is missing. `siglus-ssu` itself is not shipped; it is fetched from PyPI.

Nothing else third-party is bundled. NScripter's `nscript.dat` is decoded and encoded by
the tool itself.

## Releases

A release is a zip on the repository's Releases page, built self-contained for 64-bit
Windows: unzip it anywhere and run `TranslationTools.exe`. Nothing has to be installed
first, not even .NET; the tool fetches the SiglusEngine compiler itself when a Siglus game
needs it. The manual, `MANUAL.md`, is in the zip beside the exe. The version is the
four-part number on the release tag, `v0.1.0.0` for the first alpha.

## Building from source

You need the .NET 9 SDK (https://dotnet.microsoft.com/download). Visual Studio is optional.

1. Clone with the submodule. The `TALOREAL_NETCORE_API` library lives in its own repository
   and is pulled in under `lib\`:

   ```
   git clone --recurse-submodules https://github.com/Taloreal/Translation-Tools.git
   ```

   If you cloned without the flag, `lib\TALOREAL_NETCORE_API` is empty; fill it with:

   ```
   git submodule update --init
   ```

2. Copy `Secrets.cs.sample` to `Secrets.cs` in the project folder. Leave its two values
   empty unless you run your own build-mode service: with them empty the tool builds and
   runs, the shared list of known games is simply unavailable and reporting stays off. The
   file is ignored by git, so it is never committed.

3. Build:

   ```
   dotnet build TranslationTools.csproj
   ```

   or open `TranslationTools.sln` in Visual Studio and build there. The exe, the manual, the
   compiler installer and the bundled engine land in `bin\Debug\net9.0\`.

The service the tool reports build modes to is not part of this repository.
