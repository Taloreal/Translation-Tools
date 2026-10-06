// File: CheckpointInspector.cs
// Namespace: TranslationTools
namespace TranslationTools {

	/// <summary>
	/// Works out a checkpoint's engine and form from names and shapes on disk only. No file
	/// is ever opened.
	///
	/// A checkpoint is a container folder with a fixed layout:
	///
	///   Scene.pck or nscript.dat     the master; it names the engine
	///   extract\                     the sources (Siglus: .ss files; NScripter: 0.txt)
	///   split\                       the split (Siglus: dialogues\; NScripter: all four parts)
	///   backups\                     never looked inside
	///   checkpoint.log               the checkpoint's own history of warnings and errors
	///   checkpoint.hashes            the sources as last compiled, for the integrity check
	///   checkpoint.info              the game it is, and VNDB's id for it
	///
	/// Nothing else may sit at the top level. The form follows what the folders hold:
	///
	///   master only, or extract\ empty            packed
	///   extract\ holds the sources                unsplit
	///   split\ holds a complete split too         split
	///   split\ partial, sources missing, the other engine's files anywhere, both masters,
	///   extra top-level entries, or a missing path                   invalid (with a reason)
	///   no master                                                    invalid - except NScripter:
	///       0.txt and nscript.dat are the same bytes either way, and a complete split rebuilds
	///       0.txt, so a folder holding either is still a checkpoint, with a warning that the
	///       missing piece is rebuildable. Only when all of them are gone is nothing left.
	///
	/// A path to the master file itself means its folder.
	///
	/// The one write: finding a checkpoint invalid deletes its checkpoint.info, since what it
	/// said is no longer known to be true of the folder.
	/// </summary>
	public static class CheckpointInspector {

		public const string ExtractFolder = "extract";
		public const string SplitFolder = "split";
		public const string BackupsFolder = "backups";

		private const string DialoguesFolder = "dialogues";
		private const string FunctionsFolder = "functions";
		private const string NscripterScript = "0.txt";
		private const string NscripterArchive = "nscript.dat";
		private const string SiglusArchive = "Scene.pck";
		private const string DialogueKeyFile = "DialogueKey.txt";
		private const string FunctionKeyFile = "FunctionKey.txt";


		/// <summary>
		/// Looks at what is at a path right now.
		/// </summary>
		/// <param name="path">The checkpoint's folder, or its master file.</param>
		/// <returns>The engine and form, or Invalid with the reason.</returns>
		public static CheckpointState Inspect(string path) {
			CheckpointState state = new();
			string folder = FolderOf(path);
			if (folder.Length == 0) {
				Invalidate(state, "not an archive (" + Path.GetFileName(path) + ")");
			}
			if (folder.Length > 0 && Directory.Exists(folder) == false) {
				string what = "the folder is missing";
				if (IsArchiveName(Path.GetFileName(path)) == true) {
					what = "the archive is missing";
				}
				Invalidate(state, what);
			}
			if (folder.Length > 0 && Directory.Exists(folder) == true) {
				InspectFolder(folder, state);
				if (state.Form == CheckpointForm.Invalid) {
					// An invalid checkpoint's info says nothing reliable about the folder.
					// It goes, so the next valid state starts by asking the game again.
					CheckpointInfo.Discard(folder);
				}
			}
			return state;
		}


		/// <summary>
		/// The container folder a checkpoint path means: the path itself for a folder, the
		/// parent for a master file. Empty for a file that is not a master.
		/// </summary>
		/// <param name="path">The checkpoint's path.</param>
		/// <returns>The folder, or empty.</returns>
		public static string FolderOf(string path) {
			string folder = path;
			bool isFile = File.Exists(path) == true || (Directory.Exists(path) == false && Path.HasExtension(path) == true);
			if (isFile == true) {
				folder = "";
				if (IsArchiveName(Path.GetFileName(path)) == true) {
					folder = Path.GetDirectoryName(Path.GetFullPath(path)) ?? "";
				}
			}
			return folder;
		}


		/// <summary>
		/// Reads the container's top level and the two working folders.
		/// </summary>
		private static void InspectFolder(string folder, CheckpointState state) {
			bool hasNscriptArchive = File.Exists(Path.Combine(folder, NscripterArchive));
			bool hasSiglusArchive = File.Exists(Path.Combine(folder, SiglusArchive));
			string extras = ExtraTopLevelEntries(folder);

			bool decided = false;
			if (hasNscriptArchive == true && hasSiglusArchive == true) {
				Invalidate(state, "holds both Scene.pck and nscript.dat");
				decided = true;
			}
			if (decided == false && hasNscriptArchive == false && hasSiglusArchive == false) {
				// NScripter's master is the script XORed, so 0.txt - or a complete split,
				// which rebuilds 0.txt - can rebuild nscript.dat. A folder holding either is a
				// checkpoint with a missing-but-rebuildable master; anything else is not one.
				bool rebuildable = NscripterMasterRebuildable(folder);
				if (rebuildable == true) {
					InspectNscripter(folder, state);
					if (state.Form != CheckpointForm.Invalid) {
						state.Warning = "nscript.dat missing, rebuildable";
						state.NeedsRecovery = true;
					}
				}
				if (rebuildable == false) {
					Invalidate(state, "no master archive (Scene.pck or nscript.dat)");
				}
				decided = true;
			}
			if (decided == false && extras.Length > 0) {
				Invalidate(state, "outside the layout:" + extras);
				decided = true;
			}
			if (decided == false && hasSiglusArchive == true) {
				InspectSiglus(folder, state);
			}
			if (decided == false && hasNscriptArchive == true) {
				InspectNscripter(folder, state);
			}
		}


		/// <summary>
		/// A Siglus container: .ss files in extract\ make it unsplit; a dialogues\ folder in
		/// split\ makes it split.
		/// </summary>
		private static void InspectSiglus(string folder, CheckpointState state) {
			string extract = Path.Combine(folder, ExtractFolder);
			string split = Path.Combine(folder, SplitFolder);
			bool hasExtract = Directory.Exists(extract);
			bool hasSplit = Directory.Exists(split);
			bool extractHasSources = hasExtract == true && FolderHoldsExtension(extract, ".ss") == true;
			bool extractHasNscripter = hasExtract == true && FolderHoldsNscripterFiles(extract) == true;
			bool splitHasSources = hasSplit == true && FolderHoldsExtension(split, ".ss") == true;
			bool splitHasDialogues = hasSplit == true && Directory.Exists(Path.Combine(split, DialoguesFolder)) == true;
			bool splitHasNscripter = hasSplit == true && FolderHoldsNscripterFiles(split) == true;

			string problem = "";
			if (extractHasNscripter == true || splitHasNscripter == true) {
				problem = "Scene.pck beside NScripter files";
			}
			if (problem.Length == 0 && hasExtract == true && extractHasSources == false && FolderIsEmpty(extract) == false) {
				problem = "extract\\ holds no .ss sources";
			}
			if (problem.Length == 0 && hasSplit == true && extractHasSources == false) {
				problem = "split\\ without sources in extract\\";
			}
			if (problem.Length == 0 && hasSplit == true && (splitHasSources == false || splitHasDialogues == false)) {
				problem = "split\\ is incomplete (needs .ss files and dialogues\\)";
			}

			if (problem.Length > 0) {
				Invalidate(state, problem);
			}
			if (problem.Length == 0) {
				state.Engine = CheckpointEngine.Siglus;
				state.Form = CheckpointForm.Packed;
				if (extractHasSources == true) {
					state.Form = CheckpointForm.Unsplit;
				}
				if (hasSplit == true) {
					state.Form = CheckpointForm.Split;
				}
			}
		}


		/// <summary>
		/// An NScripter container: 0.txt in extract\ makes it unsplit; all four parts in
		/// split\ make it split, one to three make it damaged.
		/// </summary>
		private static void InspectNscripter(string folder, CheckpointState state) {
			string extract = Path.Combine(folder, ExtractFolder);
			string split = Path.Combine(folder, SplitFolder);
			bool hasExtract = Directory.Exists(extract);
			bool hasSplit = Directory.Exists(split);
			bool extractHasScript = hasExtract == true && File.Exists(Path.Combine(extract, NscripterScript)) == true;
			bool siglusFiles = (hasExtract == true && FolderHoldsExtension(extract, ".ss") == true)
				|| (hasSplit == true && FolderHoldsExtension(split, ".ss") == true);

			int splitParts = 0;
			string missingParts = "";
			if (hasSplit == true) {
				CountSplitPart(Directory.Exists(Path.Combine(split, DialoguesFolder)), DialoguesFolder + "\\", ref splitParts, ref missingParts);
				CountSplitPart(Directory.Exists(Path.Combine(split, FunctionsFolder)), FunctionsFolder + "\\", ref splitParts, ref missingParts);
				CountSplitPart(File.Exists(Path.Combine(split, DialogueKeyFile)), DialogueKeyFile, ref splitParts, ref missingParts);
				CountSplitPart(File.Exists(Path.Combine(split, FunctionKeyFile)), FunctionKeyFile, ref splitParts, ref missingParts);
			}

			string problem = "";
			if (siglusFiles == true) {
				problem = "nscript.dat beside Siglus .ss files";
			}
			if (problem.Length == 0 && hasExtract == true && extractHasScript == false && FolderIsEmpty(extract) == false) {
				problem = "extract\\ holds no 0.txt";
			}
			if (problem.Length == 0 && hasSplit == true && splitParts < 4) {
				problem = "damaged split, missing" + missingParts;
			}
			// A complete split with no 0.txt beside it is whole: 0.txt is rebuilt by Join.
			if (problem.Length == 0 && hasSplit == true && extractHasScript == false) {
				state.Warning = "0.txt missing, rebuildable from the split";
				state.NeedsRecovery = true;
			}

			if (problem.Length > 0) {
				Invalidate(state, problem);
			}
			if (problem.Length == 0) {
				state.Engine = CheckpointEngine.NScripter;
				state.Form = CheckpointForm.Packed;
				if (extractHasScript == true) {
					state.Form = CheckpointForm.Unsplit;
				}
				if (hasSplit == true) {
					state.Form = CheckpointForm.Split;
				}
			}
		}


		/// <summary>
		/// Whether a folder without nscript.dat still holds what rebuilds it: extract\0.txt,
		/// or all four parts of a split in split\. No .ss files anywhere, or it is not NScripter.
		/// </summary>
		private static bool NscripterMasterRebuildable(string folder) {
			string extract = Path.Combine(folder, ExtractFolder);
			string split = Path.Combine(folder, SplitFolder);
			bool hasScript = File.Exists(Path.Combine(extract, NscripterScript));
			bool hasSplit = Directory.Exists(Path.Combine(split, DialoguesFolder)) == true
				&& Directory.Exists(Path.Combine(split, FunctionsFolder)) == true
				&& File.Exists(Path.Combine(split, DialogueKeyFile)) == true
				&& File.Exists(Path.Combine(split, FunctionKeyFile)) == true;
			bool siglusFiles = (Directory.Exists(extract) == true && FolderHoldsExtension(extract, ".ss") == true)
				|| (Directory.Exists(split) == true && FolderHoldsExtension(split, ".ss") == true);
			return siglusFiles == false && (hasScript == true || hasSplit == true);
		}


		/// <summary>
		/// Names of top-level entries that are not the master or one of the three folders,
		/// as " a, b, c" for a reason line. Empty when the layout is clean.
		/// </summary>
		private static string ExtraTopLevelEntries(string folder) {
			string extras = "";
			int shown = 0;
			foreach (string entry in Directory.EnumerateFileSystemEntries(folder)) {
				string name = Path.GetFileName(entry);
				bool allowed = string.Equals(name, NscripterArchive, StringComparison.OrdinalIgnoreCase)
					|| string.Equals(name, SiglusArchive, StringComparison.OrdinalIgnoreCase)
					|| string.Equals(name, ExtractFolder, StringComparison.OrdinalIgnoreCase)
					|| string.Equals(name, SplitFolder, StringComparison.OrdinalIgnoreCase)
					|| string.Equals(name, BackupsFolder, StringComparison.OrdinalIgnoreCase)
					|| string.Equals(name, CheckpointLog.FileName, StringComparison.OrdinalIgnoreCase)
					|| string.Equals(name, SourceHashes.FileName, StringComparison.OrdinalIgnoreCase)
					|| string.Equals(name, CheckpointInfo.FileName, StringComparison.OrdinalIgnoreCase);
				if (allowed == false) {
					if (shown < 3) {
						extras += " " + name;
					}
					if (shown == 3) {
						extras += " ...";
					}
					shown += 1;
				}
			}
			return extras;
		}


		/// <summary>
		/// Whether a folder has at least one file with an extension at its top level.
		/// Matched on the extension, never on a wildcard, so .ssx or a short name cannot count.
		/// </summary>
		private static bool FolderHoldsExtension(string folder, string extension) {
			bool found = false;
			foreach (string file in Directory.EnumerateFiles(folder)) {
				if (found == false && string.Equals(Path.GetExtension(file), extension, StringComparison.OrdinalIgnoreCase) == true) {
					found = true;
				}
			}
			return found;
		}


		/// <summary>
		/// Whether a working folder holds any of the NScripter shapes: 0.txt, a key file, or
		/// a functions\ folder.
		/// </summary>
		private static bool FolderHoldsNscripterFiles(string folder) {
			return File.Exists(Path.Combine(folder, NscripterScript)) == true
				|| File.Exists(Path.Combine(folder, DialogueKeyFile)) == true
				|| File.Exists(Path.Combine(folder, FunctionKeyFile)) == true
				|| Directory.Exists(Path.Combine(folder, FunctionsFolder)) == true;
		}


		private static bool FolderIsEmpty(string folder) {
			bool empty = true;
			foreach (string entry in Directory.EnumerateFileSystemEntries(folder)) {
				empty = false;
			}
			return empty;
		}


		/// <summary>
		/// Tallies one part of an NScripter split: counts it when present, names it when not.
		/// </summary>
		private static void CountSplitPart(bool present, string name, ref int count, ref string missing) {
			if (present == true) {
				count += 1;
			}
			if (present == false) {
				missing += " " + name;
			}
		}


		/// <summary>
		/// Whether a file name is one of the two archives a master can be.
		/// </summary>
		private static bool IsArchiveName(string name) {
			bool siglus = string.Equals(name, SiglusArchive, StringComparison.OrdinalIgnoreCase);
			bool nscripter = string.Equals(name, NscripterArchive, StringComparison.OrdinalIgnoreCase);
			bool anyPck = string.Equals(Path.GetExtension(name), ".pck", StringComparison.OrdinalIgnoreCase);
			return siglus == true || nscripter == true || anyPck == true;
		}


		private static void Invalidate(CheckpointState state, string reason) {
			state.Engine = CheckpointEngine.Unknown;
			state.Form = CheckpointForm.Invalid;
			state.Reason = reason;
		}
	}
}
