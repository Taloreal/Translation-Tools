// File: SiglusJoin.cs
// Namespace: TranslationTools
using System.Text;

namespace TranslationTools {

	/// <summary>
	/// What joining one scene produced: the rebuilt script and the accounting that says
	/// whether every id found a home.
	/// </summary>
	public class SceneJoin {

		/// <summary>The rebuilt .ss text, ready for the compiler.</summary>
		public string Script = "";

		/// <summary>Problems worth reporting that did not stop the join.</summary>
		public List<string> Warnings = new();

		/// <summary>How many id tokens were replaced by their dialogue run.</summary>
		public int Substitutions = 0;

		/// <summary>How many id tokens had no entry and were commented out.</summary>
		public int Missing = 0;

		/// <summary>How many id tokens had an entry holding no text and were commented out.</summary>
		public int Blanks = 0;

		/// <summary>How many dialogue entries the script never asked for.</summary>
		public int Unused = 0;

		/// <summary>How many prose lines were wrapped at a column.</summary>
		public int Wrapped = 0;
	}


	/// <summary>
	/// Join for Siglus: every working copy in split\ becomes a rebuilt .ss over the one in
	/// extract\, each ::id:: token replaced by the text that id holds in the dialogue file
	/// the pointer comment above it names. The pointer comments survive, so a joined script
	/// says where each label's text came from. Support files in extract\ are untouched.
	///
	/// A missing index, or an index with no text after it, is commented out whole as a
	/// //ERROR: line naming the file: the fault is found in the file, never shown in the
	/// game. The voice call above it is left alone on purpose - heard and not seen is the
	/// cheapest missing-line alarm there is. A pointer to a dialogue file that is not there
	/// is damage, and the join refuses before writing anything.
	///
	/// Word wrap is governed by a control code in the working copy, a comment the engine
	/// skips: "// wrap 60" wraps the prose lines after it at 60 display columns, "// wrap
	/// off" goes back to the checkpoint's governing column, and "// wrap 0" wraps nothing.
	/// The state resets to off at every #label and at file end, so a column cannot linger
	/// past the label it was set in. With no column in force nothing is wrapped and nothing
	/// is warned about.
	/// </summary>
	public static class SiglusJoin {

		/// <summary>The word after "//" that makes a comment a wrap control code.</summary>
		public const string WrapWord = "wrap";

		/// <summary>A governing column of this means no wrapping.</summary>
		public const int NoWrap = 0;


		/// <summary>
		/// Joins every working copy in a split folder over the scripts in a source folder.
		/// Everything is joined in memory and encoded first; nothing is written until every
		/// scene has succeeded.
		/// </summary>
		/// <param name="splitFolder">split\: the working copies and dialogues\.</param>
		/// <param name="sourceFolder">extract\: the .ss files to write over.</param>
		/// <param name="governingColumn">The checkpoint's wrap column, used where no control code is in force; NoWrap for none.</param>
		/// <param name="warnings">Everything worth telling the user that did not stop the join.</param>
		/// <param name="onLine">Receives progress.</param>
		/// <returns>Empty on success, otherwise a plain sentence.</returns>
		public static string JoinFolder(string splitFolder, string sourceFolder, int governingColumn, List<string> warnings, Action<string> onLine) {
			string problem = "";
			string[] copies = new string[0];
			if (Directory.Exists(splitFolder) == false) {
				problem = "Nothing to join: " + splitFolder + " is missing.";
			}
			if (problem.Length == 0) {
				copies = Directory.GetFiles(splitFolder, "*" + SiglusScript.ScriptExtension);
				Array.Sort(copies, StringComparer.OrdinalIgnoreCase);
				if (copies.Length == 0) {
					problem = "Nothing to join: no " + SiglusScript.ScriptExtension + " files in " + splitFolder;
				}
			}

			string dialogueFolder = Path.Combine(splitFolder, SiglusScript.DialoguesFolder);
			Dictionary<string, string> dialogues = new(StringComparer.Ordinal);
			List<byte[]> encoded = new();
			List<string> names = new();
			int substitutions = 0;
			int missing = 0;
			int blanks = 0;
			int unused = 0;
			int wrapped = 0;
			int at = 0;
			while (problem.Length == 0 && at < copies.Length) {
				string name = Path.GetFileName(copies[at]);
				try {
					string scriptText = SiglusScript.ReadScript(copies[at]);
					problem = LoadPointedDialogues(scriptText, name, dialogueFolder, dialogues);
					if (problem.Length == 0) {
						SceneJoin join = JoinScene(scriptText, name, dialogues, governingColumn);
						encoded.Add(SiglusScript.ScriptEncoding.GetBytes(join.Script));
						names.Add(name);
						foreach (string warning in join.Warnings) {
							warnings.Add(name + ": " + warning);
						}
						substitutions += join.Substitutions;
						missing += join.Missing;
						blanks += join.Blanks;
						unused += join.Unused;
						wrapped += join.Wrapped;
					}
				}
				catch (Exception exception) {
					problem = name + ": " + exception.Message;
				}
				at += 1;
			}

			if (problem.Length == 0) {
				try {
					Directory.CreateDirectory(sourceFolder);
					for (int index = 0; index < names.Count; index++) {
						File.WriteAllBytes(Path.Combine(sourceFolder, names[index]), encoded[index]);
					}
					// The support files come across whole, replacing extract\'s: they are edited
					// in the split too - a new variable is declared in an .inc - and the
					// build has to see that edit. This also makes a rebuilt extract\ buildable.
					int copied = 0;
					foreach (string file in Directory.GetFiles(splitFolder)) {
						bool isScene = file.EndsWith(SiglusScript.ScriptExtension, StringComparison.OrdinalIgnoreCase);
						if (isScene == false) {
							File.Copy(file, Path.Combine(sourceFolder, Path.GetFileName(file)), true);
							copied += 1;
						}
					}
					onLine("Joined " + names.Count + " scenes: " + substitutions + " lines placed, " + wrapped + " wrapped, " + copied + " support files copied over.");
					if (missing > 0) {
						warnings.Add(missing + " index(es) have no entry in their dialogue file; commented out as //ERROR: MISSING DIALOGUE");
					}
					if (blanks > 0) {
						warnings.Add(blanks + " index(es) have no text after them; commented out as //ERROR: BLANK DIALOGUE");
					}
					if (unused > 0) {
						warnings.Add(unused + " dialogue entries are never asked for by any script");
					}
				}
				catch (Exception exception) {
					problem = "Could not write the joined scripts: " + exception.Message;
				}
			}
			return problem;
		}


		/// <summary>
		/// Rebuilds one scene from its working copy and its dialogue files.
		/// </summary>
		/// <param name="scriptText">The working copy's text.</param>
		/// <param name="sceneName">The scene's file name, for messages.</param>
		/// <param name="dialogues">Dialogue file text keyed by file key; every key the copy points at must be present.</param>
		/// <param name="governingColumn">The wrap column where no control code is in force; NoWrap for none.</param>
		/// <returns>The rebuilt script and the accounting for every id.</returns>
		public static SceneJoin JoinScene(string scriptText, string sceneName, IReadOnlyDictionary<string, string> dialogues, int governingColumn) {
			List<ScriptLine> lines = SiglusScript.ReadLines(scriptText);
			SceneJoin output = new();
			Dictionary<string, Dictionary<int, string>> maps = new(StringComparer.Ordinal);
			Dictionary<string, HashSet<int>> consumed = new(StringComparer.Ordinal);
			List<ScriptLine> rewritten = new();
			string current = "";
			int column = governingColumn;
			int number = 0;

			foreach (ScriptLine line in lines) {
				number += 1;
				string pointed = SiglusScript.PointerTarget(line.Content);
				bool isLabel = SiglusScript.LabelOf(line.Content).Length > 0;
				if (isLabel == true) {
					// The column resets at every label, so it cannot linger past the one
					// it was set in.
					column = governingColumn;
				}
				if (TryReadWrapCode(line.Content, governingColumn, out int setColumn) == true) {
					column = setColumn;
				}
				if (pointed.Length > 0) {
					current = pointed;
					LoadDialogue(pointed, dialogues, maps, consumed, output);
					rewritten.Add(line);
				}
				if (pointed.Length == 0) {
					bool hasToken = SiglusScript.TryReadToken(line.Content, out int id);
					if (hasToken == false) {
						rewritten.Add(line);
					}
					if (hasToken == true) {
						if (current.Length == 0) {
							throw new InvalidOperationException("line " + number + " holds " + SiglusScript.TokenFor(id)
								+ " before any pointer comment - the working copy is missing its " + SiglusScript.PointerFor("LABEL") + " line.");
						}
						rewritten.Add(Resolve(line, current, id, maps[current], consumed[current], column, output));
					}
				}
			}

			ReportUnused(maps, consumed, output);
			output.Script = SiglusScript.WriteLines(rewritten);
			return output;
		}


		/// <summary>
		/// Reads a wrap control code off a comment line: "// wrap 60", "// wrap off", "// wrap 0".
		/// </summary>
		/// <param name="line">The raw line.</param>
		/// <param name="governingColumn">What "off" means.</param>
		/// <param name="column">The column the code sets.</param>
		/// <returns>True when the line is a wrap control code.</returns>
		public static bool TryReadWrapCode(string line, int governingColumn, out int column) {
			column = governingColumn;
			bool isCode = false;
			string trimmed = line.Trim();
			if (trimmed.StartsWith(SiglusScript.CommentMarker, StringComparison.Ordinal) == true) {
				string body = trimmed.Substring(SiglusScript.CommentMarker.Length).Trim();
				if (body.StartsWith(WrapWord, StringComparison.OrdinalIgnoreCase) == true) {
					string argument = body.Substring(WrapWord.Length).Trim();
					bool separated = body.Length > WrapWord.Length && (body[WrapWord.Length] == ' ' || body[WrapWord.Length] == '\t');
					if (separated == true && argument.Length > 0) {
						if (string.Equals(argument, "off", StringComparison.OrdinalIgnoreCase) == true) {
							isCode = true;
						}
						if (isCode == false && int.TryParse(argument, out int parsed) == true && parsed >= 0) {
							column = parsed;
							isCode = true;
						}
					}
				}
			}
			return isCode;
		}


		/// <summary>
		/// Reads every dialogue file a working copy points at into the shared map, once
		/// each. A pointed file that is not there is damage.
		/// </summary>
		/// <returns>Empty when every file was found, otherwise a plain sentence.</returns>
		private static string LoadPointedDialogues(string scriptText, string sceneName, string dialogueFolder, Dictionary<string, string> dialogues) {
			string problem = "";
			foreach (ScriptLine line in SiglusScript.ReadLines(scriptText)) {
				string pointed = SiglusScript.PointerTarget(line.Content);
				if (problem.Length == 0 && pointed.Length > 0 && dialogues.ContainsKey(pointed) == false) {
					string path = Path.Combine(dialogueFolder, pointed + SiglusScript.DialogueExtension);
					if (File.Exists(path) == false) {
						problem = sceneName + " points at " + pointed + SiglusScript.DialogueExtension + ", which is not in " + dialogueFolder + ". The split is damaged.";
					}
					if (problem.Length == 0) {
						dialogues.Add(pointed, SiglusScript.ReadScript(path));
					}
				}
			}
			return problem;
		}


		/// <summary>
		/// Parses a pointed dialogue file into the maps, once per file key.
		/// </summary>
		private static void LoadDialogue(string fileKey, IReadOnlyDictionary<string, string> dialogues, Dictionary<string, Dictionary<int, string>> maps,
			Dictionary<string, HashSet<int>> consumed, SceneJoin output) {
			if (maps.ContainsKey(fileKey) == false) {
				if (dialogues.ContainsKey(fileKey) == false) {
					throw new InvalidOperationException("the script points at " + fileKey + SiglusScript.DialogueExtension + ", which was not supplied.");
				}
				maps.Add(fileKey, ParseDialogue(fileKey, dialogues[fileKey], output));
				consumed.Add(fileKey, new HashSet<int>());
			}
		}


		/// <summary>
		/// A dialogue file's entries by id. A repeated id keeps the first, with a warning.
		/// </summary>
		private static Dictionary<int, string> ParseDialogue(string fileKey, string text, SceneJoin output) {
			Dictionary<int, string> entries = new();
			foreach (ScriptLine line in SiglusScript.ReadLines(text)) {
				if (SiglusScript.TryReadToken(line.Content, out int id) == true) {
					if (entries.ContainsKey(id) == true) {
						output.Warnings.Add(fileKey + SiglusScript.DialogueExtension + " holds " + SiglusScript.TokenFor(id) + " more than once; keeping the first.");
					}
					if (entries.ContainsKey(id) == false) {
						// A standalone name line's marker is for the translator; it never reaches the script.
						entries.Add(id, NametagConvention.StripMarker(line.Content.Substring(SiglusScript.TokenLength)));
					}
				}
			}
			return entries;
		}


		/// <summary>
		/// One token line with its token replaced: by the run, or by an //ERROR: comment.
		/// Only the token itself is replaced; the operator tail the splitter left on the
		/// .ss side goes back byte for byte after any wrapped prose.
		/// </summary>
		private static ScriptLine Resolve(ScriptLine line, string fileKey, int id, Dictionary<int, string> map, HashSet<int> consumed, int column, SceneJoin output) {
			string indent = SiglusScript.LeadingWhitespace(line.Content);
			string body = line.Content.Substring(indent.Length);
			string afterToken = body.Substring(SiglusScript.TokenLength);
			string beforeTail = SiglusScript.PeelTail(afterToken);
			string tail = afterToken.Substring(beforeTail.Length);
			string fileName = fileKey + SiglusScript.DialogueExtension;

			bool present = map.TryGetValue(id, out string? run) == true && run != null;
			bool blank = present == true && run!.Trim().Length == 0;
			string rebuilt = "";
			if (present == true) {
				consumed.Add(id);
			}
			if (present == true && blank == false) {
				rebuilt = WrapProse(fileKey, id, run!, column, output) + beforeTail + tail;
				output.Substitutions += 1;
			}
			if (blank == true) {
				rebuilt = SiglusScript.CommentMarker + "ERROR: BLANK DIALOGUE - " + fileName + " holds this index with no text after it."
					+ " If that was not deliberate, the line to finish is " + fileName + " " + body;
				output.Blanks += 1;
				output.Warnings.Add(fileName + " holds " + SiglusScript.TokenFor(id) + " with no text after it - nothing is shown and nothing is waited for.");
			}
			if (present == false) {
				rebuilt = SiglusScript.CommentMarker + "ERROR: MISSING DIALOGUE - no entry for this index in " + fileName + " " + body;
				output.Missing += 1;
				output.Warnings.Add("no entry for " + SiglusScript.TokenFor(id) + " in " + fileName);
			}
			ScriptLine filled = new();
			filled.Content = indent + rebuilt;
			filled.Ending = line.Ending;
			return filled;
		}


		/// <summary>
		/// Wraps one entry's prose at the column in force. Only a single quoted string is
		/// rebuilt; an expression, a choice list or a hand-authored nl/r chain is source
		/// syntax and goes back byte for byte. No column means no change at all.
		/// </summary>
		private static string WrapProse(string fileKey, int id, string value, int column, SceneJoin output) {
			string result = value;
			if (column > NoWrap) {
				SiglusScript.TryReadEntry(SiglusScript.TokenFor(id) + value, out DialogueEntry entry);
				string prefix = "";
				if (entry.HasNametag == true) {
					prefix = SiglusScript.NametagOpen + entry.Nametag + SiglusScript.NametagClose;
				}
				string prose = entry.Text;
				string proseWithoutTail = SiglusScript.PeelTail(prose);
				string legacyTail = prose.Substring(proseWithoutTail.Length);
				if (IsSingleString(proseWithoutTail) == true) {
					string display = Unescape(proseWithoutTail.Substring(1, proseWithoutTail.Length - 2));
					List<string> segments = WrapDisplayText(display, column);
					bool overlong = false;
					foreach (string segment in segments) {
						if (segment.Length > column) {
							overlong = true;
						}
					}
					if (overlong == true) {
						output.Warnings.Add(fileKey + SiglusScript.DialogueExtension + " holds " + SiglusScript.TokenFor(id)
							+ " with an unbreakable segment over " + column + " display characters; shorten it by hand.");
					}
					// One segment means no boundary was needed: the original keeps its
					// exact escape spelling.
					if (segments.Count > 1) {
						StringBuilder rebuilt = new();
						for (int index = 0; index < segments.Count; index++) {
							if (index > 0) {
								rebuilt.Append("nl");
							}
							rebuilt.Append(Quote(segments[index]));
						}
						result = prefix + rebuilt.ToString() + legacyTail;
						output.Wrapped += 1;
					}
				}
			}
			return result;
		}


		/// <summary>
		/// True when a value is one quoted string, rather than an expression such as
		/// "A"nl"B" or a choice argument list.
		/// </summary>
		public static bool IsSingleString(string value) {
			bool single = value.Length >= 2 && value[0] == '"' && value[value.Length - 1] == '"';
			bool escaped = false;
			int index = 1;
			while (single == true && index < value.Length - 1) {
				char current = value[index];
				bool wasEscaped = escaped;
				escaped = false;
				if (wasEscaped == false && current == '\\') {
					escaped = true;
				}
				if (wasEscaped == false && current == '"') {
					single = false;
				}
				index += 1;
			}
			return single == true && escaped == false;
		}


		/// <summary>
		/// Places bare text inside a Siglus string literal.
		/// </summary>
		public static string Quote(string value) {
			return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
		}


		/// <summary>
		/// Decodes only the two escape spellings the writer emits, backslash and quote. An
		/// unknown escape stays literal, so this can never invent an engine meaning.
		/// </summary>
		private static string Unescape(string value) {
			string decoded = value;
			if (value.Contains('\\') == true) {
				StringBuilder builder = new();
				int index = 0;
				while (index < value.Length) {
					char current = value[index];
					bool pair = current == '\\' && index + 1 < value.Length && (value[index + 1] == '\\' || value[index + 1] == '"');
					if (pair == true) {
						builder.Append(value[index + 1]);
						index += 2;
					}
					if (pair == false) {
						builder.Append(current);
						index += 1;
					}
				}
				decoded = builder.ToString();
			}
			return decoded;
		}


		/// <summary>
		/// Greedily wraps on ASCII-space boundaries, keeping every run of spaces. The space
		/// chosen as a boundary stays on the preceding segment: the window shows a break,
		/// but the backlog rejoins the runs and needs the space between its words.
		/// </summary>
		private static List<string> WrapDisplayText(string text, int column) {
			List<string> segments = new();
			StringBuilder current = new();
			int index = 0;
			bool trailing = false;
			while (index < text.Length && trailing == false) {
				int spacesStart = index;
				while (index < text.Length && text[index] == ' ') {
					index += 1;
				}
				string spaces = text.Substring(spacesStart, index - spacesStart);
				int wordStart = index;
				while (index < text.Length && text[index] != ' ') {
					index += 1;
				}
				string word = text.Substring(wordStart, index - wordStart);
				if (word.Length == 0) {
					// Trailing spaces have no following word to wrap before; they stay.
					current.Append(spaces);
					trailing = true;
				}
				if (word.Length > 0) {
					bool fits = current.Length == 0 || current.Length + spaces.Length + word.Length <= column;
					if (fits == true) {
						current.Append(spaces);
						current.Append(word);
					}
					if (fits == false) {
						current.Append(spaces);
						segments.Add(current.ToString());
						current.Clear();
						current.Append(word);
					}
				}
			}
			if (current.Length > 0 || text.Length == 0) {
				segments.Add(current.ToString());
			}
			return segments;
		}


		/// <summary>
		/// Counts and names every entry no token asked for.
		/// </summary>
		private static void ReportUnused(Dictionary<string, Dictionary<int, string>> maps, Dictionary<string, HashSet<int>> consumed, SceneJoin output) {
			foreach (string fileKey in maps.Keys) {
				foreach (int id in maps[fileKey].Keys) {
					if (consumed[fileKey].Contains(id) == false) {
						output.Unused += 1;
						output.Warnings.Add(fileKey + SiglusScript.DialogueExtension + " holds " + SiglusScript.TokenFor(id) + ", which the script never asks for.");
					}
				}
			}
		}
	}
}
