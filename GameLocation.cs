// File: GameLocation.cs
// Namespace: TranslationTools
namespace TranslationTools {

	/// <summary>
	/// What an installed game's folder looks like, by file names only: the engine's start-up
	/// files beside the live archive. This is what Run is gated behind, and what tells the
	/// checkpoint manager that a path the user gave is a game folder rather than a checkpoint.
	///
	///   Siglus      Scene.pck beside SiglusEngine.exe
	///   NScripter   nscript.dat (or 0.txt) beside at least one .exe
	///
	/// Every .exe in the folder is offered as a launcher; the tool ships none of its own and
	/// names no particular one, since a locale bypass or a wrapper is the user's to point at.
	/// </summary>
	public class GameLocation {

		public const string SiglusEngine = "SiglusEngine.exe";

		/// <summary>The engine the folder's files belong to.</summary>
		public CheckpointEngine Engine = CheckpointEngine.Unknown;

		/// <summary>The live archive's full path.</summary>
		public string Archive = "";

		/// <summary>The live 0.txt's full path, for NScripter; the engine prefers it over nscript.dat.</summary>
		public string Script = "";

		/// <summary>Every exe in the folder, as found.</summary>
		public List<string> Launchers = new();

		/// <summary>Why the folder is not a game folder, when it is not.</summary>
		public string Reason = "";

		/// <summary>True when the folder holds a game that can be started.</summary>
		public bool IsGame {
			get { return Engine != CheckpointEngine.Unknown && Launchers.Count > 0 && Archive.Length > 0; }
		}


		/// <summary>
		/// Looks at a folder, or the folder of a file, for a runnable game.
		/// </summary>
		/// <param name="path">A folder, or a file inside the game folder.</param>
		/// <returns>What was found; IsGame says whether it is enough to run.</returns>
		public static GameLocation Inspect(string path) {
			GameLocation location = new();
			string folder = path;
			if (File.Exists(path) == true) {
				folder = Path.GetDirectoryName(Path.GetFullPath(path)) ?? "";
			}
			if (Directory.Exists(folder) == false) {
				location.Reason = "the folder does not exist";
			}
			if (Directory.Exists(folder) == true) {
				location.Look(folder);
			}
			return location;
		}


		private void Look(string folder) {
			string siglusArchive = Path.Combine(folder, "Scene.pck");
			string siglusEngine = Path.Combine(folder, SiglusEngine);
			string nscripterArchive = Path.Combine(folder, NScriptArchive.ArchiveName);
			string nscripterScript = Path.Combine(folder, NScriptArchive.ScriptName);

			List<string> exes = new();
			foreach (string file in Directory.EnumerateFiles(folder)) {
				if (string.Equals(Path.GetExtension(file), ".exe", StringComparison.OrdinalIgnoreCase) == true) {
					exes.Add(file);
				}
			}

			if (File.Exists(siglusArchive) == true) {
				Engine = CheckpointEngine.Siglus;
				Archive = siglusArchive;
				if (File.Exists(siglusEngine) == true) {
					Launchers.AddRange(exes);
				}
				if (File.Exists(siglusEngine) == false) {
					Reason = "Scene.pck is here but " + SiglusEngine + " is not";
				}
			}
			if (Engine == CheckpointEngine.Unknown && (File.Exists(nscripterArchive) == true || File.Exists(nscripterScript) == true)) {
				Engine = CheckpointEngine.NScripter;
				Archive = nscripterArchive;
				if (File.Exists(nscripterScript) == true) {
					Script = nscripterScript;
				}
				Launchers.AddRange(exes);
				if (Launchers.Count == 0) {
					Reason = "the script is here but no .exe is";
				}
			}
			if (Engine == CheckpointEngine.Unknown) {
				Reason = "no Scene.pck or nscript.dat here";
			}
		}
	}
}
