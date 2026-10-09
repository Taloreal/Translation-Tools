// File: NScripterJoin.cs
// Namespace: TranslationTools
namespace TranslationTools {

	/// <summary>
	/// Join for NScripter: split\ -> extract\0.txt. Walks FunctionKey.txt in order and
	/// writes each function file with every ::index:: pointer replaced by the text that
	/// index holds in the paired dialogue file - the file at the same position in
	/// DialogueKey.txt first, then any dialogue file, so older splits with project-wide
	/// indexes and lines moved by hand between files still join. The preamble comes first,
	/// and one blank line follows each function.
	///
	/// A dialogue file that is listed but missing is damage: the join refuses, since its
	/// lines are gone. A missing index, or an index with no text after it, is commented
	/// out whole as an ;ERROR: line that names the file to open - the trailing backslash
	/// goes with it, so the line costs no click-wait in the game. The two are told apart
	/// but neither guesses why: a blank left on purpose and a blank left half-edited are
	/// the same bytes, and only the person who did it knows which.
	/// </summary>
	public static class NScripterJoin {

		/// <summary>Written before the first function when a split has no Preamble.txt: the old tool's fixed header.</summary>
		public const string FallbackPreamble = ";mode800,value1000";


		/// <summary>
		/// Joins a split into a script. The script is written whole to a new file and only
		/// then moved over the destination, so a join that fails leaves it untouched.
		/// </summary>
		/// <param name="splitFolder">The split\ folder.</param>
		/// <param name="scriptPath">The 0.txt to write.</param>
		/// <param name="stagingPath">Where the script is written first; a file in backups\.</param>
		/// <param name="warnings">What the join went ahead with but the user should know.</param>
		/// <param name="onLine">Receives progress.</param>
		/// <returns>Empty on success, otherwise a plain sentence.</returns>
		public static string Join(string splitFolder, string scriptPath, string stagingPath, List<string> warnings, Action<string> onLine) {
			string problem = "";
			string functionKeyPath = Path.Combine(splitFolder, NScripterSplit.FunctionKeyFile);
			string dialogueKeyPath = Path.Combine(splitFolder, NScripterSplit.DialogueKeyFile);
			if (File.Exists(functionKeyPath) == false) {
				problem = NScripterSplit.FunctionKeyFile + " is missing from the split.";
			}
			if (problem.Length == 0 && File.Exists(dialogueKeyPath) == false) {
				problem = NScripterSplit.DialogueKeyFile + " is missing from the split.";
			}

			string[] functionFiles = new string[0];
			string[] dialogueFiles = new string[0];
			if (problem.Length == 0) {
				functionFiles = File.ReadAllLines(functionKeyPath);
				dialogueFiles = File.ReadAllLines(dialogueKeyPath);
				problem = MissingFiles(splitFolder, functionFiles, dialogueFiles);
			}

			List<Dictionary<int, string>> perFile = new();
			if (problem.Length == 0) {
				foreach (string dialogueFile in dialogueFiles) {
					perFile.Add(ReadDialogues(Path.Combine(splitFolder, dialogueFile.Trim())));
				}
			}

			if (problem.Length == 0) {
				try {
					int blanks = 0;
					int missing = 0;
					Directory.CreateDirectory(Path.GetDirectoryName(stagingPath)!);
					using (StreamWriter script = new(stagingPath, false, NScripterSplit.ScriptEncoding)) {
						WritePreamble(splitFolder, script, warnings);
						for (int at = 0; at < functionFiles.Length; at++) {
							string[] lines = File.ReadAllLines(Path.Combine(splitFolder, functionFiles[at].Trim()), NScripterSplit.ScriptEncoding);
							Dictionary<int, string> own = new();
							if (at < perFile.Count) {
								own = perFile[at];
							}
							foreach (string line in lines) {
								script.WriteLine(Resolve(line, own, at, perFile, dialogueFiles, ref blanks, ref missing));
							}
							script.WriteLine();
						}
					}
					if (missing > 0) {
						warnings.Add(missing + " index(es) have no line in any dialogue file; commented out as ;ERROR: MISSING DIALOGUE");
					}
					if (blanks > 0) {
						warnings.Add(blanks + " index(es) have no text after them; commented out as ;ERROR: BLANK DIALOGUE");
					}
					File.Move(stagingPath, scriptPath, true);
					onLine("Joined " + functionFiles.Length + " functions into " + Path.GetFileName(scriptPath) + ".");
				}
				catch (Exception exception) {
					problem = "Could not join: " + exception.Message;
					try { File.Delete(stagingPath); } catch (Exception) { }
				}
			}
			return problem;
		}


		/// <summary>
		/// Names the first listed file that is not there. Either kind missing is damage.
		/// </summary>
		private static string MissingFiles(string splitFolder, string[] functionFiles, string[] dialogueFiles) {
			string problem = "";
			foreach (string functionFile in functionFiles) {
				if (problem.Length == 0 && File.Exists(Path.Combine(splitFolder, functionFile.Trim())) == false) {
					problem = functionFile.Trim() + " is listed in " + NScripterSplit.FunctionKeyFile + " but missing. The split is damaged.";
				}
			}
			foreach (string dialogueFile in dialogueFiles) {
				if (problem.Length == 0 && File.Exists(Path.Combine(splitFolder, dialogueFile.Trim())) == false) {
					problem = dialogueFile.Trim() + " is listed in " + NScripterSplit.DialogueKeyFile + " but missing. The split is damaged.";
				}
			}
			return problem;
		}


		/// <summary>
		/// Reads a dialogue file's entries by index. The first line with an index wins if
		/// it appears twice.
		/// </summary>
		private static Dictionary<int, string> ReadDialogues(string path) {
			Dictionary<int, string> entries = new();
			foreach (string line in File.ReadAllLines(path, NScripterSplit.ScriptEncoding)) {
				if (NScripterSplit.TryReadPointer(line, out string pointer, out int index, out string text) == true) {
					if (entries.ContainsKey(index) == false) {
						// A standalone name line's marker is for the translator; it never reaches the script.
						entries.Add(index, NametagConvention.StripMarker(text));
					}
				}
			}
			return entries;
		}


		/// <summary>
		/// Writes Preamble.txt's lines, or the old fixed header with a warning when the
		/// split predates the file.
		/// </summary>
		private static void WritePreamble(string splitFolder, StreamWriter script, List<string> warnings) {
			string preamblePath = Path.Combine(splitFolder, NScripterSplit.PreambleFile);
			if (File.Exists(preamblePath) == true) {
				foreach (string line in File.ReadAllLines(preamblePath, NScripterSplit.ScriptEncoding)) {
					script.WriteLine(line);
				}
			}
			if (File.Exists(preamblePath) == false) {
				script.WriteLine(FallbackPreamble);
				warnings.Add("no " + NScripterSplit.PreambleFile + " in the split; wrote the old fixed header \"" + FallbackPreamble + "\" before the first function");
			}
			script.WriteLine();
		}


		/// <summary>
		/// One function-file line with its pointer, if it has one, replaced.
		/// </summary>
		private static string Resolve(string line, Dictionary<int, string> own, int ownAt, List<Dictionary<int, string>> perFile,
			string[] dialogueFiles, ref int blanks, ref int missing) {
			string resolved = line;
			if (NScripterSplit.TryReadPointer(line, out string pointer, out int index, out string tail) == true) {
				string? text = null;
				string foundIn = "";
				if (own.TryGetValue(index, out string? ownText) == true) {
					text = ownText;
					foundIn = FileKeyOf(dialogueFiles, ownAt);
				}
				int other = 0;
				while (text == null && other < perFile.Count) {
					if (perFile[other].TryGetValue(index, out string? otherText) == true) {
						text = otherText;
						foundIn = FileKeyOf(dialogueFiles, other);
					}
					other += 1;
				}
				if (text == null) {
					// The semicolon keeps NScripter from reading the rest as a command.
					resolved = line.Replace(pointer, ";ERROR: MISSING DIALOGUE - no line with this index in any dialogue file " + pointer);
					missing += 1;
				}
				if (text != null && text.Trim().Length == 0) {
					resolved = line.Replace(pointer, ";ERROR: BLANK DIALOGUE - " + foundIn + ".txt holds this index with no text after it,"
						+ " so nothing is shown and nothing is waited for. If that was not deliberate, the line to finish is "
						+ foundIn + ".txt " + pointer);
					blanks += 1;
				}
				if (text != null && text.Trim().Length > 0) {
					resolved = line.Replace(pointer, text);
				}
			}
			return resolved;
		}


		/// <summary>
		/// A dialogue file's bare name, for a message. Indexes restart in every file, so a
		/// message that names no file sends the reader scrolling.
		/// </summary>
		private static string FileKeyOf(string[] dialogueFiles, int at) {
			string named = "(unknown file)";
			if (at >= 0 && at < dialogueFiles.Length) {
				named = Path.GetFileNameWithoutExtension(dialogueFiles[at].Trim());
			}
			return named;
		}
	}
}
