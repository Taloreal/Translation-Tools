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

## Building

Open `TranslationTools.sln` in Visual Studio. The project references the
`TALOREAL_NETCORE_API` library by relative path. Copy `Secrets.cs.sample` to `Secrets.cs`
and fill it in before building; it is ignored by git.

The service the tool reports build modes to is not part of this repository.
