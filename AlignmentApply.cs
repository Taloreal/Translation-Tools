// File: AlignmentApply.cs
// Namespace: TranslationTools
using System.Text;

using TALOREAL_NETCORE_API;

namespace TranslationTools {

	/// <summary>
	/// Apply: makes a completed alignment permanent in the files. The pairing's entries are
	/// ordered so the reference side ascends, every entry gets one new index, and both
	/// sides' dialogue files and the code that carries their tokens are renumbered through
	/// that map. Nothing moves: every line keeps its place in the game's reading order and
	/// only its number changes, so afterwards index n names the same line on both sides,
	/// and the editable side's numbers may cross where the walk found a swap. The code is
	/// rewritten per engine - NScripter's function file, Siglus's token lines inside every
	/// region whose pointer comment names the key, since ids are scoped to their label.
	/// Before anything is written, both sides' files are copied into the pair folder under
	/// "before"; afterwards each checkpoint gets a stamp. The reference is written too:
	/// alignment is a known change.
	/// </summary>
	public static class AlignmentApply {

		/// <summary>The pair folder's subfolder holding the pre-alignment copies, one folder per serial under it.</summary>
		public const string BeforeFolder = "before";


		/// <summary>
		/// Runs Apply on a pair from the menu: every line of the outcome on screen, then a pause.
		/// </summary>
		public static void Run(AlignmentPair pair, Checkpoint edit, Checkpoint reference) {
			string problem = Execute(pair, edit, reference, Console.WriteLine);
			if (problem.Length > 0) {
				Console.WriteLine(problem.Trim());
			}
			ConsoleExt.WaitForEnter("continue");
		}


		/// <summary>
		/// Apply itself, after every check. Nothing is written until both sides are planned
		/// whole; the before-copies are made first; the stamps come last.
		/// </summary>
		/// <param name="onLine">Receives each line of the outcome.</param>
		/// <returns>Empty on success, otherwise what stopped it, as plain sentences.</returns>
		public static string Execute(AlignmentPair pair, Checkpoint edit, Checkpoint reference, Action<string> onLine) {
			AlignmentPairing pairing = AlignmentPairing.Load(pair.Folder);
			string problem = "";
			if (pairing.Status == AlignmentPairing.Applied) {
				problem = "This alignment was already applied. Delete the pair and start a new one to realign.";
			}
			if (problem.Length == 0 && pairing.Status != AlignmentPairing.Complete) {
				problem = "The alignment of " + pair.Key + " is not complete (" + pairing.Status + "). Walk it to the end first.";
			}
			Side? editSide = null;
			Side? refSide = null;
			if (problem.Length == 0) {
				editSide = Side.Open(edit, pair.Key, out problem);
			}
			if (problem.Length == 0) {
				refSide = Side.Open(reference, pair.RefKey, out problem);
			}
			if (problem.Length == 0) {
				problem = Undecided(editSide!, pairing, true) + Undecided(refSide!, pairing, false);
			}
			Dictionary<int, int> editMap = new();
			Dictionary<int, int> refMap = new();
			if (problem.Length == 0) {
				Number(pairing, editMap, refMap);
				problem = editSide!.Plan(editMap) + refSide!.Plan(refMap);
			}
			if (problem.Length == 0) {
				onLine("Applying the alignment of " + pair.Key + ": " + pairing.Entries.Count + " new index(es) over " + pairing.PairedCount + " pair(s), "
					+ pairing.EditOnlyCount + " only on " + edit.Label + ", " + pairing.RefOnlyCount + " only on " + reference.Label + " ...");
				string beforeEdit = Path.Combine(pair.Folder, BeforeFolder, edit.Serial);
				string beforeRef = Path.Combine(pair.Folder, BeforeFolder, reference.Serial);
				problem = editSide!.CopyBefore(beforeEdit) + refSide!.CopyBefore(beforeRef);
				if (problem.Length == 0) {
					problem = editSide.Write() + refSide.Write();
				}
				if (problem.Length == 0) {
					editSide.RemapChoices(editMap);
					refSide.RemapChoices(refMap);
					pairing.Renumber(editMap, refMap);
					string when = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
					string stampProblem = editSide.Stamp(reference.Serial, pair.RefKey, when) + refSide.Stamp(edit.Serial, pair.Key, when);
					if (stampProblem.Length > 0) {
						onLine(stampProblem.Trim());
					}
					CheckpointLog.Warning(editSide.Folder, "Align", "alignment applied: " + pair.Key + " renumbered with \"" + reference.Label + "\" " + pair.RefKey + " (" + pairing.Entries.Count + " indexes); before-copies in " + pair.Folder);
					CheckpointLog.Warning(refSide.Folder, "Align", "alignment applied: " + pair.RefKey + " renumbered with \"" + edit.Label + "\" " + pair.Key + " (" + pairing.Entries.Count + " indexes); before-copies in " + pair.Folder);
					onLine("Applied. " + edit.Label + ": " + editSide.Written + " line(s) renumbered in " + editSide.CodePaths.Count + " code file(s); "
						+ reference.Label + ": " + refSide.Written + " line(s) in " + refSide.CodePaths.Count + " code file(s). Index n is now the same line on both sides.");
					onLine("Copies of both sides as they were: " + Path.Combine(pair.Folder, BeforeFolder));
				}
			}
			return problem;
		}


		/// <summary>
		/// Assigns the new indexes. Entries are ordered so the reference ascends: every entry
		/// with a reference index by that index, and each editable-only entry right after
		/// the entry that preceded it in the walk.
		/// </summary>
		public static void Number(AlignmentPairing pairing, Dictionary<int, int> editMap, Dictionary<int, int> refMap) {
			List<KeyValuePair<long, PairingEntry>> ordered = new();
			int lastRef = -1;
			int seq = 0;
			foreach (PairingEntry entry in pairing.Entries) {
				seq++;
				long key = 0;
				if (entry.Ref >= 0) {
					lastRef = entry.Ref;
					key = ((long) entry.Ref << 24);
				}
				if (entry.Ref < 0) {
					key = ((long) lastRef << 24) + seq;
				}
				ordered.Add(new KeyValuePair<long, PairingEntry>(key, entry));
			}
			ordered.Sort((first, second) => first.Key.CompareTo(second.Key));
			int next = 0;
			foreach (KeyValuePair<long, PairingEntry> item in ordered) {
				if (item.Value.Edit >= 0) {
					editMap[item.Value.Edit] = next;
				}
				if (item.Value.Ref >= 0) {
					refMap[item.Value.Ref] = next;
				}
				next++;
			}
		}


		/// <summary>
		/// The sentence for lines the pairing never decided, or empty.
		/// </summary>
		private static string Undecided(Side side, AlignmentPairing pairing, bool editSide) {
			List<string> missing = new();
			foreach (int index in side.Indexes) {
				bool decided = pairing.EditDecided(index);
				if (editSide == false) {
					decided = pairing.RefDecided(index);
				}
				if (decided == false) {
					missing.Add(index.ToString());
				}
			}
			string problem = "";
			if (missing.Count > 0) {
				problem = side.Label + " " + side.Key + " holds " + missing.Count + " line(s) the pairing never decided (" + string.Join(", ", missing.GetRange(0, Math.Min(8, missing.Count))) + "...). The file changed since the walk; reset and walk again. ";
			}
			return problem;
		}


		/// <summary>
		/// One side of an Apply: its files read whole, its plan, and its writes.
		/// </summary>
		private class Side {

			public Checkpoint Checkpoint = new();
			public string Label = "";
			public string Key = "";
			public string Folder = "";
			public string SplitFolder = "";
			public CheckpointEngine Engine = CheckpointEngine.Unknown;
			public string DialoguePath = "";
			public List<string> CodePaths = new();
			public List<int> Indexes = new();
			public int Written = 0;

			private readonly Dictionary<string, string[]> texts = new(StringComparer.OrdinalIgnoreCase);
			private readonly Dictionary<string, string[]> planned = new(StringComparer.OrdinalIgnoreCase);


			/// <summary>
			/// Reads the dialogue file and finds the code that carries its tokens.
			/// </summary>
			public static Side? Open(Checkpoint checkpoint, string key, out string problem) {
				problem = "";
				Side side = new();
				side.Checkpoint = checkpoint;
				side.Label = checkpoint.Label;
				side.Key = key;
				side.Folder = CheckpointInspector.FolderOf(checkpoint.Path);
				side.SplitFolder = Path.Combine(side.Folder, CheckpointInspector.SplitFolder);
				side.Engine = CheckpointInspector.Inspect(checkpoint.Path).Engine;
				side.DialoguePath = AlignmentLines.DialoguePath(checkpoint, key);
				if (File.Exists(side.DialoguePath) == false) {
					problem = "Missing: " + side.DialoguePath + " ";
				}
				if (problem.Length == 0) {
					try {
						side.texts[side.DialoguePath] = Lines(side.DialoguePath);
						foreach (string line in side.texts[side.DialoguePath]) {
							if (NScripterSplit.TryReadPointer(Bare(line), out string pointer, out int index, out string rest) == true) {
								side.Indexes.Add(index);
							}
						}
						side.CodePaths = side.FindCode();
						foreach (string path in side.CodePaths) {
							side.texts[path] = Lines(path);
						}
					}
					catch (Exception exception) {
						problem = "Could not read " + checkpoint.Label + " " + key + ": " + exception.Message + " ";
					}
				}
				if (problem.Length == 0 && side.CodePaths.Count == 0) {
					problem = checkpoint.Label + ": no code file carries the tokens of " + key + " (looked for " + side.CodeDescription() + "). ";
				}
				Side? opened = side;
				if (problem.Length > 0) {
					opened = null;
				}
				return opened;
			}


			/// <summary>
			/// Builds every rewritten file in memory. Any token the map does not know stops
			/// Apply before a byte is written.
			/// </summary>
			public string Plan(Dictionary<int, int> map) {
				string problem = "";
				string[] dialogue = texts[DialoguePath];
				string[] newDialogue = new string[dialogue.Length];
				for (int at = 0; at < dialogue.Length; at++) {
					string line = Bare(dialogue[at]);
					string ending = Ending(dialogue[at]);
					newDialogue[at] = dialogue[at];
					if (NScripterSplit.TryReadPointer(line, out string pointer, out int index, out string rest) == true) {
						if (map.ContainsKey(index) == false) {
							problem += Label + " " + Key + " line " + index + " has no new index. ";
						}
						if (map.ContainsKey(index) == true) {
							newDialogue[at] = NScripterSplit.PointerFor(map[index]) + rest + ending;
							Written++;
						}
					}
				}
				planned[DialoguePath] = newDialogue;
				foreach (string path in CodePaths) {
					problem += PlanCode(path, map);
				}
				return problem;
			}


			/// <summary>
			/// Copies the dialogue and code files as they are now under the before folder,
			/// keeping their layout below the split folder.
			/// </summary>
			public string CopyBefore(string beforeFolder) {
				string problem = "";
				try {
					foreach (string path in texts.Keys) {
						string relative = Path.GetRelativePath(SplitFolder, path);
						string target = Path.Combine(beforeFolder, relative);
						Directory.CreateDirectory(Path.GetDirectoryName(target) ?? beforeFolder);
						File.Copy(path, target, true);
					}
				}
				catch (Exception exception) {
					problem = "Could not copy " + Label + "'s files before applying: " + exception.Message + " ";
				}
				return problem;
			}


			/// <summary>
			/// Writes every planned file.
			/// </summary>
			public string Write() {
				string problem = "";
				try {
					foreach (string path in planned.Keys) {
						File.WriteAllBytes(path, SiglusScript.ScriptEncoding.GetBytes(string.Join("", planned[path])));
					}
				}
				catch (Exception exception) {
					problem = "Could not write " + Label + "'s files: " + exception.Message + ". The before-copies in the pair folder are intact. ";
				}
				return problem;
			}


			/// <summary>
			/// Moves the choice blocks of this key to the new numbers.
			/// </summary>
			public void RemapChoices(Dictionary<int, int> map) {
				List<ChoiceLocation> blocks = ChoiceLocations.Read(Folder);
				bool changed = false;
				foreach (ChoiceLocation block in blocks) {
					if (string.Equals(block.FileKey, Key, StringComparison.OrdinalIgnoreCase)) {
						if (map.ContainsKey(block.FirstIndex) == true && map.ContainsKey(block.LastIndex) == true) {
							block.FirstIndex = map[block.FirstIndex];
							block.LastIndex = map[block.LastIndex];
							changed = true;
						}
					}
				}
				if (changed == true) {
					ChoiceLocations.Write(Folder, blocks);
				}
			}


			/// <summary>
			/// Records the stamp: partner, time, and the hashes of what was just written.
			/// </summary>
			public string Stamp(string partnerSerial, string partnerKey, string when) {
				Stamp stamp = new();
				stamp.File = Key;
				stamp.Partner = partnerSerial;
				stamp.PartnerFile = partnerKey;
				stamp.Applied = when;
				stamp.Dialogue = CheckpointStamps.HashOf(DialoguePath);
				List<string> sorted = new(CodePaths);
				sorted.Sort(StringComparer.OrdinalIgnoreCase);
				stamp.Code = CheckpointStamps.HashOfAll(sorted);
				List<string> relative = new();
				foreach (string path in sorted) {
					relative.Add(Path.GetRelativePath(SplitFolder, path));
				}
				stamp.CodeFiles = string.Join(";", relative);
				return CheckpointStamps.Set(Folder, stamp);
			}


			/// <summary>
			/// The code file(s) carrying this key's tokens: the function file of the same
			/// name on NScripter; on Siglus every scene whose pointer comment names the key.
			/// </summary>
			private List<string> FindCode() {
				List<string> paths = new();
				if (Engine == CheckpointEngine.Siglus) {
					string wanted = SiglusScript.PointerFor(Key).Trim();
					foreach (string scene in Directory.GetFiles(SplitFolder, "*" + SiglusScript.ScriptExtension)) {
						bool points = false;
						foreach (string line in Lines(scene)) {
							if (string.Equals(Bare(line).Trim(), wanted, StringComparison.OrdinalIgnoreCase)) {
								points = true;
							}
						}
						if (points == true) {
							paths.Add(scene);
						}
					}
				}
				if (Engine != CheckpointEngine.Siglus) {
					string function = Path.Combine(SplitFolder, NScripterSplit.FunctionsFolder, Key + ".txt");
					if (File.Exists(function) == true) {
						paths.Add(function);
					}
				}
				return paths;
			}


			private string CodeDescription() {
				string text = Path.Combine(NScripterSplit.FunctionsFolder, Key + ".txt");
				if (Engine == CheckpointEngine.Siglus) {
					text = "a scene with the pointer comment " + SiglusScript.PointerFor(Key).Trim();
				}
				return text;
			}


			/// <summary>
			/// One code file renumbered in memory. NScripter: every pointer line. Siglus: only
			/// token lines inside the regions whose pointer comment names this key; a label
			/// line or another key's pointer ends a region.
			/// </summary>
			private string PlanCode(string path, Dictionary<int, int> map) {
				string problem = "";
				string[] lines = texts[path];
				string[] planned = new string[lines.Length];
				bool inRegion = Engine != CheckpointEngine.Siglus;
				for (int at = 0; at < lines.Length; at++) {
					string line = Bare(lines[at]);
					string ending = Ending(lines[at]);
					planned[at] = lines[at];
					if (Engine == CheckpointEngine.Siglus) {
						string pointed = SiglusScript.PointerTarget(line);
						if (pointed.Length > 0) {
							inRegion = string.Equals(pointed, Key, StringComparison.OrdinalIgnoreCase);
						}
						if (pointed.Length == 0 && SiglusScript.LabelOf(line).Length > 0) {
							inRegion = false;
						}
						if (inRegion == true && pointed.Length == 0 && SiglusScript.TryReadToken(line, out int id) == true) {
							if (map.ContainsKey(id) == false) {
								problem += Label + " " + Path.GetFileName(path) + " carries " + SiglusScript.TokenFor(id) + " for " + Key + ", which the dialogue file does not have. ";
							}
							if (map.ContainsKey(id) == true) {
								string indent = SiglusScript.LeadingWhitespace(line);
								string body = line.Substring(indent.Length);
								planned[at] = indent + SiglusScript.TokenFor(map[id]) + body.Substring(SiglusScript.TokenLength) + ending;
							}
						}
					}
					if (Engine != CheckpointEngine.Siglus) {
						if (NScripterSplit.TryReadPointer(line, out string pointer, out int index, out string tail) == true) {
							if (map.ContainsKey(index) == false) {
								problem += Label + " " + Path.GetFileName(path) + " carries " + pointer + ", which the dialogue file does not have. ";
							}
							if (map.ContainsKey(index) == true) {
								planned[at] = NScripterSplit.PointerFor(map[index]) + tail + ending;
							}
						}
					}
				}
				this.planned[path] = planned;
				return problem;
			}


			/// <summary>
			/// A file's lines with their endings kept on each, so a rewrite keeps them.
			/// </summary>
			private static string[] Lines(string path) {
				string text = SiglusScript.ScriptEncoding.GetString(File.ReadAllBytes(path));
				List<string> lines = new();
				int start = 0;
				for (int at = 0; at < text.Length; at++) {
					if (text[at] == '\n') {
						lines.Add(text.Substring(start, at - start + 1));
						start = at + 1;
					}
				}
				if (start < text.Length) {
					lines.Add(text.Substring(start));
				}
				return lines.ToArray();
			}


			private static string Bare(string line) {
				return line.TrimEnd('\n').TrimEnd('\r');
			}


			private static string Ending(string line) {
				return line.Substring(Bare(line).Length);
			}
		}
	}
}
