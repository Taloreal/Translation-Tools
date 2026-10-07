// File: NScripterSplit.cs
// Namespace: TranslationTools
using System.Text;

namespace TranslationTools {

	/// <summary>
	/// Split for NScripter: extract\0.txt -> split\. Every *label line starts a function,
	/// and each function becomes two files. functions\<label>.txt is the code, with every
	/// dialogue line replaced by a ::index:: pointer plus whatever followed the line's
	/// first backslash. dialogues\<label>.txt is the header line, then one line per
	/// pointer: the pointer, the nametag moved to the front, a backtick, the text.
	/// FunctionKey.txt and DialogueKey.txt list the files in lockstep, so line i of one
	/// pairs with line i of the other. Indexes restart at zero in every file. Lines before
	/// the first *label are kept verbatim in Preamble.txt so Join can put them back.
	///
	/// This is the old tool's AdvancedSplit, kept line for line in what it writes, so the
	/// editor and every older split still read. Empty lines are dropped. Everything is
	/// CP932.
	/// </summary>
	public static class NScripterSplit {

		public const string DialoguesFolder = "dialogues";
		public const string FunctionsFolder = "functions";
		public const string DialogueKeyFile = "DialogueKey.txt";
		public const string FunctionKeyFile = "FunctionKey.txt";
		public const string PreambleFile = "Preamble.txt";
		public const string StartMarker = ";<start>";
		public const string EndMarker = ";<end>";

		/// <summary>Comment that opens a choice block: the select-family command and its options.</summary>
		public const string ChoiceStart = ";start choices";

		/// <summary>Comment that closes a choice block.</summary>
		public const string ChoiceEnd = ";end choices";

		/// <summary>The commands whose quoted arguments are the player's choices, in the engine's standard forms.</summary>
		private static readonly string[] ChoiceCommands = new string[] { "select", "selgosub", "selnum", "csel" };

		/// <summary>The script's encoding. NScripter reads Shift-JIS; code page 932 is Windows' name for it.</summary>
		public static readonly Encoding ScriptEncoding = Encoding.GetEncoding(932);

		/// <summary>What a code line may start with, lower-cased. A line starting with anything else is dialogue.</summary>
		private const string CodeStarts = "abcdefghijklmnopqrstuvwxyz;\t ";

		private const string PointerFence = "::";
		private const int IndexDigits = 10;
		private const int PointerLength = 14;
		private const char NametagOpen = '【';
		private const char NametagClose = '】';


		/// <summary>
		/// Splits a script into a split folder, which must be absent or empty.
		/// </summary>
		/// <param name="scriptPath">The 0.txt to split.</param>
		/// <param name="splitFolder">Where the split goes; created here.</param>
		/// <param name="onLine">Receives progress.</param>
		/// <returns>Empty on success, otherwise a plain sentence.</returns>
		public static string Split(string scriptPath, string splitFolder, Action<string> onLine) {
			string problem = "";
			if (File.Exists(scriptPath) == false) {
				problem = "Nothing to split: " + scriptPath + " is missing.";
			}
			if (problem.Length == 0 && Directory.Exists(splitFolder) == true && Directory.EnumerateFileSystemEntries(splitFolder).GetEnumerator().MoveNext() == true) {
				problem = "The split folder is not empty: " + splitFolder;
			}
			if (problem.Length == 0) {
				try {
					List<string> lines = StampChoiceBlocks(ReadLines(scriptPath), out int stamped);
					if (stamped > 0) {
						onLine(stamped + " choice block(s) stamped so their options stay editable once translated.");
					}
					List<List<string>> functions = Functions(lines, out List<string> preamble);
					Directory.CreateDirectory(Path.Combine(splitFolder, DialoguesFolder));
					Directory.CreateDirectory(Path.Combine(splitFolder, FunctionsFolder));
					File.WriteAllLines(Path.Combine(splitFolder, PreambleFile), preamble, ScriptEncoding);
					List<string> functionKeys = new();
					List<string> dialogueKeys = new();
					List<ChoiceLocation> choices = new();
					int dialogueLines = 0;
					foreach (List<string> function in functions) {
						string label = LabelOf(function[0]);
						string functionKey = Path.Combine(FunctionsFolder, label + ".txt");
						string dialogueKey = Path.Combine(DialoguesFolder, label + ".txt");
						functionKeys.Add(functionKey);
						dialogueKeys.Add(dialogueKey);
						dialogueLines += WriteFunction(function, label, Path.Combine(splitFolder, functionKey), Path.Combine(splitFolder, dialogueKey), choices);
					}
					File.WriteAllLines(Path.Combine(splitFolder, FunctionKeyFile), functionKeys);
					File.WriteAllLines(Path.Combine(splitFolder, DialogueKeyFile), dialogueKeys);
					string checkpointFolder = Path.GetDirectoryName(Path.GetFullPath(splitFolder)) ?? splitFolder;
					ChoiceLocations.Write(checkpointFolder, choices);
					onLine("Split " + functions.Count + " functions, " + dialogueLines + " dialogue lines, " + preamble.Count + " preamble lines.");
					onLine(choices.Count + " choice block(s); where they are is in " + ChoiceLocations.FileName + ".");
				}
				catch (Exception exception) {
					problem = "Could not split: " + exception.Message;
				}
			}
			return problem;
		}


		/// <summary>
		/// Reads a script's non-empty lines in CP932. Empty lines are not part of a split.
		/// </summary>
		/// <param name="path">The file.</param>
		/// <returns>The lines, without line endings.</returns>
		public static List<string> ReadLines(string path) {
			List<string> lines = new();
			foreach (string line in File.ReadAllLines(path, ScriptEncoding)) {
				if (line.Length > 0) {
					lines.Add(line);
				}
			}
			return lines;
		}


		/// <summary>
		/// The label of a *label header line: what follows the star, up to the first
		/// semicolon, tab or space.
		/// </summary>
		/// <param name="header">The header line.</param>
		/// <returns>The label.</returns>
		public static string LabelOf(string header) {
			string trimmed = header.Replace("\t", "");
			int end = trimmed.Length;
			int semicolon = trimmed.IndexOf(';');
			int space = trimmed.IndexOf(' ');
			if (semicolon > 0) {
				end = semicolon;
			}
			if (semicolon <= 0 && space > 0) {
				end = space;
			}
			return trimmed.Substring(1, end - 1);
		}


		/// <summary>
		/// Whether a line is dialogue rather than code: it starts with something other than
		/// an ASCII letter, a semicolon, a tab or a space.
		/// </summary>
		/// <param name="line">A non-empty line.</param>
		/// <returns>True for dialogue.</returns>
		public static bool IsDialogue(string line) {
			return line.Length > 0 && CodeStarts.IndexOf(char.ToLowerInvariant(line[0])) < 0;
		}


		/// <summary>
		/// The pointer for an index: "::" + ten digits + "::".
		/// </summary>
		/// <param name="index">The index.</param>
		/// <returns>The pointer.</returns>
		public static string PointerFor(int index) {
			return PointerFence + index.ToString().PadLeft(IndexDigits, '0') + PointerFence;
		}


		/// <summary>
		/// Reads the pointer at the front of a line, if there is one. The pointer's
		/// presence is what makes an entry; what follows it may be empty.
		/// </summary>
		/// <param name="line">The line.</param>
		/// <param name="pointer">The fourteen characters of the pointer.</param>
		/// <param name="index">The index inside it.</param>
		/// <param name="rest">Everything after the pointer, possibly empty.</param>
		/// <returns>True when the line starts with a pointer.</returns>
		public static bool TryReadPointer(string line, out string pointer, out int index, out string rest) {
			pointer = "";
			index = 0;
			rest = "";
			bool found = false;
			if (line.Length >= PointerLength) {
				string head = line.Substring(0, PointerLength);
				bool fenced = head.StartsWith(PointerFence) == true && head.EndsWith(PointerFence) == true;
				if (fenced == true && int.TryParse(head.Substring(2, IndexDigits), out index) == true) {
					pointer = head;
					rest = line.Substring(PointerLength);
					found = true;
				}
			}
			return found;
		}


		/// <summary>
		/// Cuts the lines into functions at every *label line. A header gets ";<start>"
		/// if it lacks it, and every function ends with ";<end>". Lines before the first
		/// label are the preamble.
		/// </summary>
		private static List<List<string>> Functions(List<string> lines, out List<string> preamble) {
			List<List<string>> functions = new();
			preamble = new List<string>();
			List<string>? current = null;
			foreach (string line in lines) {
				if (line[0] == '*') {
					current = new List<string>();
					string header = line;
					if (header.Contains(StartMarker) == false) {
						header += StartMarker;
					}
					current.Add(header);
					functions.Add(current);
				}
				if (line[0] != '*' && current == null) {
					preamble.Add(line);
				}
				if (line[0] != '*' && current != null) {
					current.Add(line);
				}
			}
			foreach (List<string> function in functions) {
				if (function[function.Count - 1] != EndMarker) {
					function.Add(EndMarker);
				}
			}
			return functions;
		}


		/// <summary>
		/// Writes one function's two files.
		/// </summary>
		/// <param name="function">The function's lines, header first.</param>
		/// <param name="label">The function's label, which keys both files.</param>
		/// <param name="functionPath">Where the code goes.</param>
		/// <param name="dialoguePath">Where the entries go.</param>
		/// <param name="choices">Receives one location per choice block found.</param>
		/// <returns>How many dialogue lines it held.</returns>
		private static int WriteFunction(List<string> function, string label, string functionPath, string dialoguePath, List<ChoiceLocation> choices) {
			int index = 0;
			using (StreamWriter code = new(functionPath, false, ScriptEncoding)) {
				using (StreamWriter dialogue = new(dialoguePath, false, ScriptEncoding)) {
					code.WriteLine(function[0]);
					dialogue.WriteLine(function[0]);
					bool inChoices = false;
					ChoiceLocation? block = null;
					for (int at = 1; at < function.Count; at++) {
						string line = function[at];
						// Inside a stamped choice block a line holding a quoted option is an
						// entry VERBATIM, code riding along, with the engine's English mode
						// opened and closed inside each option's quotes. The opener and the
						// stamps themselves are code. Each block is noted for checkpoint.choices.
						if (line.Trim() == ChoiceStart) {
							inChoices = true;
							block = new ChoiceLocation();
							block.FileKey = label;
						}
						if (line.Trim() == ChoiceEnd) {
							inChoices = false;
							if (block != null && block.Options > 0) {
								choices.Add(block);
							}
							block = null;
						}
						if (inChoices == true && block != null && block.Command.Length == 0) {
							block.Command = ChoiceCommandOf(line);
						}
						bool choiceOption = inChoices == true && line.Contains('"') == true && line.Trim() != ChoiceStart;
						if (choiceOption == true) {
							string pointer = PointerFor(index);
							dialogue.WriteLine(pointer + WrapOptions(line));
							code.WriteLine(pointer);
							if (block != null) {
								if (block.Options == 0) {
									block.FirstIndex = index;
								}
								block.LastIndex = index;
								// Options are quoted strings; a one-line form holds several on one line.
								int quotes = 0;
								foreach (char character in line) {
									if (character == '"') {
										quotes += 1;
									}
								}
								block.Options += quotes / 2;
							}
							index += 1;
						}
						if (choiceOption == false && (inChoices == true || IsDialogue(line) == false)) {
							code.WriteLine(line);
						}
						if (choiceOption == false && inChoices == false && IsDialogue(line) == true) {
							string pointer = PointerFor(index);
							string text = "`" + line.Replace("`", "");
							string tail = "";
							int slash = text.IndexOf('\\');
							if (slash >= 0) {
								tail = text.Substring(slash);
								text = text.Substring(0, slash);
							}
							text = NametagToFront(text);
							// A select line's text opens with a quote; the backtick goes inside it.
							text = text.Replace("`\"", "\"`");
							dialogue.WriteLine(pointer + text);
							code.WriteLine(pointer + tail);
							index += 1;
						}
					}
				}
			}
			return index;
		}


		/// <summary>
		/// Inside a choice block, an option needs the engine's English display mode opened
		/// and closed INSIDE its quotes - "`Choice`",*label - or a 1-byte option misreads the
		/// label after it. Done at split, like the other repairs, so the dialogue file shows the
		/// shape the translator keeps. Every quoted option without a backtick of its own gets the pair;
		/// it does a Japanese option no harm, so there is no telling the two apart. One
		/// already wrapped is untouched.
		/// </summary>
		public static string WrapOptions(string line) {
			string result = line;
			int at = 0;
			int open = result.IndexOf('"', at);
			while (open >= 0) {
				int close = result.IndexOf('"', open + 1);
				if (close < 0) {
					open = -1;
				}
				if (close >= 0) {
					string content = result.Substring(open + 1, close - open - 1);
					if (content.Contains('`') == false) {
						result = result.Substring(0, open + 1) + "`" + content + "`" + result.Substring(close);
						close += 2;
					}
					open = result.IndexOf('"', close + 1);
				}
			}
			return result;
		}



		/// <summary>
		/// Puts ";start choices" and ";end choices" around every select-family block: the
		/// opener line, then every following line that opens with a quote or follows a
		/// line ending in a comma - the engine's continuation. A block already stamped is
		/// left alone, so a re-split changes nothing.
		/// </summary>
		/// <param name="lines">The script's non-empty lines.</param>
		/// <param name="stamped">How many blocks were stamped on this pass.</param>
		/// <returns>The lines with the stamps in.</returns>
		private static List<string> StampChoiceBlocks(List<string> lines, out int stamped) {
			List<string> result = new();
			stamped = 0;
			int at = 0;
			while (at < lines.Count) {
				string line = lines[at];
				bool alreadyStamped = result.Count > 0 && result[result.Count - 1].Trim() == ChoiceStart;
				if (IsChoiceOpener(line) == true && alreadyStamped == false) {
					result.Add(ChoiceStart);
					result.Add(line);
					string previous = line;
					at += 1;
					bool continues = at < lines.Count && ContinuesChoice(previous, lines[at]);
					while (continues == true) {
						result.Add(lines[at]);
						previous = lines[at];
						at += 1;
						continues = at < lines.Count && ContinuesChoice(previous, lines[at]);
					}
					result.Add(ChoiceEnd);
					stamped += 1;
				}
				if (IsChoiceOpener(line) == false || alreadyStamped == true) {
					result.Add(line);
					at += 1;
				}
			}
			return result;
		}


		/// <summary>
		/// Whether a line's command is one of the choice commands: the first word, before
		/// any space, tab or quote, case-insensitive.
		/// </summary>
		private static bool IsChoiceOpener(string line) {
			return ChoiceCommandOf(line).Length > 0;
		}


		/// <summary>
		/// The choice command a line opens with, lower-cased, or empty when it is not one.
		/// </summary>
		private static string ChoiceCommandOf(string line) {
			string trimmed = line.TrimStart(' ', '\t');
			int end = 0;
			while (end < trimmed.Length && char.IsLetter(trimmed[end]) == true) {
				end += 1;
			}
			string word = trimmed.Substring(0, end).ToLowerInvariant();
			string found = "";
			foreach (string command in ChoiceCommands) {
				if (word == command) {
					found = command;
				}
			}
			return found;
		}


		/// <summary>
		/// Whether a line continues the choice block the previous line was in: the previous
		/// line ended with a comma, or this line opens with a quote (an option after a bare
		/// opener).
		/// </summary>
		private static bool ContinuesChoice(string previous, string line) {
			bool commaBefore = previous.TrimEnd(' ', '\t').EndsWith(",", StringComparison.Ordinal);
			bool optionNext = line.TrimStart(' ', '\t').StartsWith("\"", StringComparison.Ordinal);
			return commaBefore == true || optionNext == true;
		}


		/// <summary>
		/// Moves a 【name】 nametag to the front of the text, where the editor expects it.
		/// </summary>
		private static string NametagToFront(string text) {
			string moved = text;
			int open = text.IndexOf(NametagOpen);
			int close = text.IndexOf(NametagClose);
			if (open >= 0 && close >= 0 && open < close) {
				int length = close - open + 1;
				string nametag = text.Substring(open, length);
				moved = nametag + text.Remove(open, length);
			}
			return moved;
		}
	}
}
