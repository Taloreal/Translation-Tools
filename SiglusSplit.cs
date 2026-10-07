// File: SiglusSplit.cs
// Namespace: TranslationTools
using System.Text;

namespace TranslationTools {

	/// <summary>
	/// What splitting one scene produced: the working copy, its dialogue files keyed by
	/// file key, and the counts.
	/// </summary>
	public class SceneSplit {

		/// <summary>The rewritten .ss text, every text run replaced by its id token.</summary>
		public string Script = "";

		/// <summary>Dialogue file text keyed by file key (the file name without .txt).</summary>
		public Dictionary<string, string> Dialogues = new(StringComparer.Ordinal);

		/// <summary>The file keys this scene is the first to write, in script order.</summary>
		public List<string> OwnedKeys = new();

		/// <summary>The file keys this scene shares with an earlier scene, in script order.</summary>
		public List<string> SharedKeys = new();

		/// <summary>Problems worth reporting that did not stop the split.</summary>
		public List<string> Warnings = new();

		/// <summary>Things worth saying that are neither problems nor surprises, e.g. a label given its own versioned file.</summary>
		public List<string> Messages = new();

		/// <summary>How many lines were classified as text and moved out.</summary>
		public int TextLines = 0;

		/// <summary>How many of those were selbtn / select argument lists.</summary>
		public int ArgumentLists = 0;
	}


	/// <summary>
	/// One version of a label's region seen so far in a split run: the dialogue file key
	/// it owns, the scene that wrote it, and the region byte for byte.
	/// </summary>
	public class LabelVersion {

		/// <summary>The dialogue file's name without .txt: the label, or label_VER###.</summary>
		public string FileKey = "";

		/// <summary>The scene file that wrote it.</summary>
		public string OwnerScene = "";

		/// <summary>The whole labelled region, code and text together, as it reads in the source.</summary>
		public string Source = "";
	}


	/// <summary>
	/// Split for Siglus: every .ss in extract\ becomes a working copy in split\ with its
	/// text runs replaced by ::id:: tokens, and one dialogue file per text-bearing #label
	/// under split\dialogues\. A pointer comment under every text-bearing label names the
	/// dialogue file, so Join follows the pointer, not the label.
	///
	/// Label names are only unique per scene. Two scenes reaching the same label with a
	/// byte-identical REGION - code and text together - share one dialogue file: translate
	/// it once and every copy gets it. Two scenes reaching the same label with different
	/// regions are different blocks, and the later one gets its own file, label_VER001,
	/// label_VER002 and so on, in scene order so a re-split lands the same names. Nothing
	/// is refused over a name.
	/// </summary>
	public static class SiglusSplit {

		/// <summary>
		/// Splits every .ss in a folder into a split folder, which must be absent or empty.
		/// Scenes are taken in name order so version numbers are stable.
		/// </summary>
		/// <param name="sourceFolder">extract\: the .ss files, read only.</param>
		/// <param name="splitFolder">split\: the working copies and dialogues\ go here.</param>
		/// <param name="warnings">Everything worth telling the user that did not stop the split.</param>
		/// <param name="onLine">Receives progress.</param>
		/// <returns>Empty on success, otherwise a plain sentence.</returns>
		public static string SplitFolder(string sourceFolder, string splitFolder, List<CharacterEntry> characters, List<string> warnings, Action<string> onLine) {
			string problem = "";
			if (Directory.Exists(sourceFolder) == false) {
				problem = "Nothing to split: " + sourceFolder + " is missing.";
			}
			if (problem.Length == 0 && Directory.Exists(splitFolder) == true && Directory.EnumerateFileSystemEntries(splitFolder).GetEnumerator().MoveNext() == true) {
				problem = "The split folder is not empty: " + splitFolder;
			}
			string[] scenes = new string[0];
			if (problem.Length == 0) {
				scenes = Directory.GetFiles(sourceFolder, "*" + SiglusScript.ScriptExtension);
				Array.Sort(scenes, StringComparer.OrdinalIgnoreCase);
				if (scenes.Length == 0) {
					problem = "Nothing to split: no " + SiglusScript.ScriptExtension + " files in " + sourceFolder;
				}
			}

			// Every scene is split in memory first, so a scene that cannot be split
			// refuses the whole run before anything is written.
			Dictionary<string, List<LabelVersion>> versions = new(StringComparer.Ordinal);
			List<SceneSplit> splits = new();
			List<string> names = new();
			int at = 0;
			while (problem.Length == 0 && at < scenes.Length) {
				string name = Path.GetFileName(scenes[at]);
				try {
					SceneSplit split = SplitScene(SiglusScript.ReadScript(scenes[at]), name, versions, characters);
					splits.Add(split);
					names.Add(name);
					foreach (string warning in split.Warnings) {
						warnings.Add(name + ": " + warning);
					}
					foreach (string message in split.Messages) {
						onLine(name + ": " + message);
					}
				}
				catch (Exception exception) {
					problem = name + ": " + exception.Message;
				}
				at += 1;
			}

			if (problem.Length == 0) {
				try {
					int textLines = 0;
					int files = 0;
					for (int index = 0; index < splits.Count; index++) {
						SceneSplit split = splits[index];
						foreach (string key in split.OwnedKeys) {
							SiglusScript.WriteScript(Path.Combine(splitFolder, SiglusScript.DialoguesFolder, key + SiglusScript.DialogueExtension), split.Dialogues[key]);
							files += 1;
						}
						SiglusScript.WriteScript(Path.Combine(splitFolder, names[index]), split.Script);
						textLines += split.TextLines;
					}
					// The support files - *.inc, Gameexe.ini, the angou file - travel with the
					// split, so a split alone can rebuild extract\ and then the master. Root
					// only: nothing under extract\ is a support file.
					int supportFiles = 0;
					foreach (string file in Directory.GetFiles(sourceFolder)) {
						bool isScene = file.EndsWith(SiglusScript.ScriptExtension, StringComparison.OrdinalIgnoreCase);
						if (isScene == false) {
							File.Copy(file, Path.Combine(splitFolder, Path.GetFileName(file)), true);
							supportFiles += 1;
						}
					}
					onLine("Split " + splits.Count + " scenes: " + textLines + " text lines into " + files + " dialogue files; " + supportFiles + " support files copied.");
				}
				catch (Exception exception) {
					problem = "Could not write the split: " + exception.Message;
				}
			}
			return problem;
		}


		/// <summary>
		/// Splits one scene's text. Versions carries what earlier scenes claimed, and is
		/// updated with what this one claims.
		/// </summary>
		/// <param name="scriptText">The whole .ss file's text.</param>
		/// <param name="sceneName">The scene's file name, for versions and messages.</param>
		/// <param name="versions">Label name to the versions seen so far, across the run.</param>
		/// <param name="characters">Glossary characters whose quoted Japanese name is replaced by the English one.</param>
		/// <returns>The working copy and the dialogue files it points at.</returns>
		public static SceneSplit SplitScene(string scriptText, string sceneName, Dictionary<string, List<LabelVersion>> versions, List<CharacterEntry> characters) {
			SceneSplit output = new();
			List<ScriptLine> lines = SeparateVoiceCalls(SiglusScript.ReadLines(scriptText), output.Warnings, out int separated);
			if (separated > 0) {
				output.Messages.Add(separated + " voice call(s) moved onto their own line ahead of the text they shared it with.");
			}
			int quoted = QuoteBareText(lines, output.Warnings);
			if (quoted > 0) {
				output.Messages.Add(quoted + " line(s) had their nametag, dialogue or narration put in quotes so the engine can show them.");
			}
			int renamed = RenameSpeakers(lines, characters);
			if (renamed > 0) {
				output.Messages.Add(renamed + " speaker name(s) replaced by their English name from the glossary.");
			}

			// Pass 1 decides which labels own text, and collects each text-bearing region
			// whole. The pointer is written right under the label, before its text is
			// reached, so the file key has to be settled before the rewrite.
			List<bool> textFlags = SiglusScript.ClassifyLines(lines);
			HashSet<string> textBearing = FindTextBearingLabels(lines, textFlags);
			Dictionary<string, string> regions = CollectRegions(lines, textBearing, out List<string> labelOrder);
			Dictionary<string, string> keys = ResolveFileKeys(regions, labelOrder, sceneName, versions, output);

			Dictionary<string, StringBuilder> bodies = new(StringComparer.Ordinal);
			Dictionary<string, int> nextId = new(StringComparer.Ordinal);
			HashSet<string> seenLabels = new(StringComparer.Ordinal);
			List<ScriptLine> rewritten = new();
			string current = "";
			int at = -1;
			foreach (ScriptLine line in lines) {
				at += 1;
				// A later split starts a new working generation: an old pointer comment
				// is replaced by the fresh one written under the label, not kept beside it.
				bool oldPointer = SiglusScript.PointerTarget(line.Content).Length > 0;
				string label = "";
				if (oldPointer == false) {
					label = SiglusScript.LabelOf(line.Content);
				}
				if (oldPointer == false && label.Length > 0) {
					current = label;
					rewritten.Add(line);
					if (seenLabels.Add(label) == false) {
						output.Warnings.Add("label #" + label + " appears more than once; its entries share one dialogue file");
					}
					// The pointer goes under EVERY occurrence of the label, so a repeated
					// label switches the join back to the right dialogue file.
					if (textBearing.Contains(label) == true) {
						StartLabelBody(label, bodies, nextId);
						ScriptLine pointer = new();
						pointer.Content = SiglusScript.PointerFor(keys[label]);
						pointer.Ending = line.Ending;
						if (pointer.Ending.Length == 0) {
							pointer.Ending = SiglusScript.FallbackBreak;
						}
						rewritten.Add(pointer);
					}
				}
				if (oldPointer == false && label.Length == 0) {
					bool isText = textFlags[at];
					if (isText == false) {
						rewritten.Add(line);
					}
					if (isText == true) {
						string indent = SiglusScript.LeadingWhitespace(line.Content);
						string body = line.Content.Substring(indent.Length);
						string run = SiglusScript.PeelTail(body);
						string tail = body.Substring(run.Length);
						int id = nextId[current];
						nextId[current] = id + 1;
						bodies[current].Append(SiglusScript.EntryBreak);
						bodies[current].Append(SiglusScript.TokenFor(id));
						bodies[current].Append(run);
						output.TextLines += 1;
						if (SiglusScript.HasCommaOutsideQuotes(line.Content) == true) {
							output.ArgumentLists += 1;
						}
						// The indentation stays with the .ss: a token at column zero would
						// silently eat the indent on every argument list.
						ScriptLine replaced = new();
						replaced.Content = indent + SiglusScript.TokenFor(id) + tail;
						replaced.Ending = line.Ending;
						rewritten.Add(replaced);
					}
				}
			}

			foreach (string label in bodies.Keys) {
				output.Dialogues[keys[label]] = bodies[label].ToString();
			}
			output.Script = SiglusScript.WriteLines(rewritten);
			return output;
		}


		/// <summary>
		/// The labels whose regions hold text. Refuses a file that already carries id
		/// tokens (split once already) or text before its first label (no file to go in).
		/// </summary>
		private static HashSet<string> FindTextBearingLabels(List<ScriptLine> lines, List<bool> textFlags) {
			HashSet<string> textBearing = new(StringComparer.Ordinal);
			string walking = "";
			int number = 0;
			foreach (ScriptLine line in lines) {
				number += 1;
				string label = SiglusScript.LabelOf(line.Content);
				if (label.Length > 0) {
					walking = label;
				}
				if (SiglusScript.TryReadToken(line.Content, out int already) == true) {
					throw new InvalidOperationException("line " + number + " already holds " + SiglusScript.TokenFor(already)
						+ " - this file has been split once already.");
				}
				if (textFlags[number - 1] == true) {
					if (walking.Length == 0) {
						throw new InvalidOperationException("text on line " + number + " sits before the first #label, so it has no dialogue file to go in.");
					}
					textBearing.Add(walking);
				}
			}
			return textBearing;
		}


		/// <summary>
		/// Each text-bearing label's whole region as it reads in the source, code and text
		/// together, with any old pointer comments left out. Two scenes are the same block
		/// only when these match byte for byte.
		/// </summary>
		private static Dictionary<string, string> CollectRegions(List<ScriptLine> lines, HashSet<string> textBearing, out List<string> labelOrder) {
			Dictionary<string, StringBuilder> regions = new(StringComparer.Ordinal);
			labelOrder = new List<string>();
			string current = "";
			foreach (ScriptLine line in lines) {
				bool oldPointer = SiglusScript.PointerTarget(line.Content).Length > 0;
				if (oldPointer == false) {
					string label = SiglusScript.LabelOf(line.Content);
					if (label.Length > 0) {
						current = label;
					}
					if (current.Length > 0 && textBearing.Contains(current) == true) {
						if (regions.ContainsKey(current) == false) {
							regions.Add(current, new StringBuilder());
							labelOrder.Add(current);
						}
						regions[current].Append(line.Content);
						regions[current].Append(line.Ending);
					}
				}
			}
			Dictionary<string, string> collected = new(StringComparer.Ordinal);
			foreach (string label in labelOrder) {
				collected.Add(label, regions[label].ToString());
			}
			return collected;
		}


		/// <summary>
		/// Settles which dialogue file each text-bearing label writes to or shares: the
		/// label's own name for the first region seen, label_VER### for each different
		/// region after it, and an existing version's file when the region matches it. A new
		/// version is a message, not a warning: known, non-destructive, nothing broken.
		/// </summary>
		private static Dictionary<string, string> ResolveFileKeys(Dictionary<string, string> regions, List<string> labelOrder, string sceneName,
			Dictionary<string, List<LabelVersion>> versions, SceneSplit output) {
			Dictionary<string, string> keys = new(StringComparer.Ordinal);
			foreach (string label in labelOrder) {
				foreach (char bad in Path.GetInvalidFileNameChars()) {
					if (label.IndexOf(bad) >= 0) {
						throw new InvalidOperationException("label #" + label + " cannot be a file name.");
					}
				}
				if (versions.ContainsKey(label) == false) {
					versions.Add(label, new List<LabelVersion>());
				}
				List<LabelVersion> seen = versions[label];
				LabelVersion? matching = null;
				foreach (LabelVersion version in seen) {
					if (matching == null && string.Equals(version.Source, regions[label], StringComparison.Ordinal) == true) {
						matching = version;
					}
				}
				if (matching != null) {
					keys[label] = matching.FileKey;
					output.SharedKeys.Add(matching.FileKey);
					output.Messages.Add("#" + label + " is identical to the one in " + matching.OwnerScene + "; one dialogue file, "
						+ matching.FileKey + SiglusScript.DialogueExtension + ", serves both.");
				}
				if (matching == null) {
					LabelVersion version = new();
					version.FileKey = label;
					if (seen.Count > 0) {
						version.FileKey = label + "_VER" + seen.Count.ToString().PadLeft(3, '0');
						output.Messages.Add("#" + label + " differs from the one in " + seen[0].OwnerScene + "; its text goes to "
							+ version.FileKey + SiglusScript.DialogueExtension + " - the two are not the same block.");
					}
					version.OwnerScene = sceneName;
					version.Source = regions[label];
					seen.Add(version);
					keys[label] = version.FileKey;
					output.OwnedKeys.Add(version.FileKey);
				}
			}
			return keys;
		}


		/// <summary>
		/// The repair a pristine script needs before it can be cut: a voice call sharing a
		/// line with its text - "koe(001100231,2 )" at column zero, one or two whole numbers,
		/// then the nametag and run - is put on its own line, and the text follows on the
		/// next with the same ending. The old normalize step did this by hand; the splitter
		/// does it so an untouched extract splits like a prepared one. A voice call that is
		/// alone on its line, indented, or not shaped like that is left as it is.
		/// </summary>
		/// <param name="lines">The scene's lines.</param>
		/// <param name="warnings">Receives a line for each call shaped wrongly, which is left alone.</param>
		/// <param name="separated">How many calls were moved.</param>
		/// <returns>The lines, with each shared line now two.</returns>
		private static List<ScriptLine> SeparateVoiceCalls(List<ScriptLine> lines, List<string> warnings, out int separated) {
			List<ScriptLine> repaired = new();
			separated = 0;
			string defaultEnding = SiglusScript.FallbackBreak;
			if (lines.Count > 0 && lines[0].Ending.Length > 0) {
				defaultEnding = lines[0].Ending;
			}
			int number = 0;
			foreach (ScriptLine line in lines) {
				number += 1;
				int close = VoiceCallEnd(line.Content);
				if (close < 0 && line.Content.StartsWith("koe(", StringComparison.Ordinal) == true) {
					warnings.Add("line " + number + ": a voice call that is not one or two whole numbers in brackets; left as it is.");
				}
				bool shared = close >= 0 && close < line.Content.Length - 1;
				if (shared == false) {
					repaired.Add(line);
				}
				if (shared == true) {
					ScriptLine call = new();
					call.Content = line.Content.Substring(0, close + 1);
					call.Ending = line.Ending;
					if (call.Ending.Length == 0) {
						call.Ending = defaultEnding;
					}
					ScriptLine text = new();
					text.Content = line.Content.Substring(close + 1);
					text.Ending = line.Ending;
					repaired.Add(call);
					repaired.Add(text);
					separated += 1;
				}
			}
			return repaired;
		}


		/// <summary>
		/// The second repair an untouched script needs: bare Japanese nametags, dialogue and
		/// narration put in quotes, line by line, through SiglusTextRepair. Lines are changed
		/// in place.
		/// </summary>
		/// <param name="lines">The scene's lines, voice calls already separated.</param>
		/// <param name="warnings">Receives empty-nametag and unmatched-bracket reports.</param>
		/// <returns>How many lines changed.</returns>
		private static int QuoteBareText(List<ScriptLine> lines, List<string> warnings) {
			int changed = 0;
			int number = 0;
			foreach (ScriptLine line in lines) {
				number += 1;
				string repaired = SiglusTextRepair.QuoteLine(line.Content, number, warnings);
				if (string.Equals(repaired, line.Content, StringComparison.Ordinal) == false) {
					line.Content = repaired;
					changed += 1;
				}
			}
			return changed;
		}


		/// <summary>
		/// The third repair, after quoting: a quoted Japanese name that the glossary knows -
		/// "竜臥" as a nametag reads 【"竜臥"】 - becomes the quoted English name, so the split
		/// carries the names the translation uses. Only a whole quoted literal equal to the
		/// name is replaced; a name inside a longer line of narration is left to the translator.
		/// </summary>
		/// <returns>How many replacements were made.</returns>
		private static int RenameSpeakers(List<ScriptLine> lines, List<CharacterEntry> characters) {
			int renamed = 0;
			foreach (ScriptLine line in lines) {
				foreach (CharacterEntry character in characters) {
					bool usable = character.Jp.Length > 0 && character.En.Length > 0 && character.Jp != character.En;
					if (usable == true) {
						string wanted = "\"" + character.Jp + "\"";
						string replacement = "\"" + character.En + "\"";
						int at = line.Content.IndexOf(wanted, StringComparison.Ordinal);
						while (at >= 0) {
							line.Content = line.Content.Substring(0, at) + replacement + line.Content.Substring(at + wanted.Length);
							renamed += 1;
							at = line.Content.IndexOf(wanted, at + replacement.Length, StringComparison.Ordinal);
						}
					}
				}
			}
			return renamed;
		}


		/// <summary>
		/// Where a voice call at the start of a line closes: the index of its ')', or -1 when
		/// the line does not open with "koe(" holding one or two whole numbers.
		/// </summary>
		private static int VoiceCallEnd(string content) {
			int close = -1;
			if (content.StartsWith("koe(", StringComparison.Ordinal) == true) {
				int depth = 1;
				int at = 4;
				while (close < 0 && at < content.Length) {
					if (content[at] == '(') {
						depth += 1;
					}
					if (content[at] == ')') {
						depth -= 1;
						if (depth == 0) {
							close = at;
						}
					}
					at += 1;
				}
				if (close >= 0) {
					string inside = content.Substring(4, close - 4).Replace(" ", "").Replace("\t", "");
					string[] values = inside.Split(',');
					bool valid = values.Length == 1 || values.Length == 2;
					foreach (string value in values) {
						if (valid == true && int.TryParse(value, out int number) == false) {
							valid = false;
						}
					}
					if (valid == false) {
						close = -1;
					}
				}
			}
			return close;
		}


		/// <summary>
		/// Opens a label's dialogue body with its header line, once per label.
		/// </summary>
		private static void StartLabelBody(string label, Dictionary<string, StringBuilder> bodies, Dictionary<string, int> nextId) {
			if (bodies.ContainsKey(label) == false) {
				StringBuilder body = new();
				body.Append('#');
				body.Append(label);
				bodies.Add(label, body);
				nextId.Add(label, 0);
			}
		}
	}
}
