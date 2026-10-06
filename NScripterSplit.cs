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
					List<string> lines = ReadLines(scriptPath);
					List<List<string>> functions = Functions(lines, out List<string> preamble);
					Directory.CreateDirectory(Path.Combine(splitFolder, DialoguesFolder));
					Directory.CreateDirectory(Path.Combine(splitFolder, FunctionsFolder));
					File.WriteAllLines(Path.Combine(splitFolder, PreambleFile), preamble, ScriptEncoding);
					List<string> functionKeys = new();
					List<string> dialogueKeys = new();
					int dialogueLines = 0;
					foreach (List<string> function in functions) {
						string label = LabelOf(function[0]);
						string functionKey = Path.Combine(FunctionsFolder, label + ".txt");
						string dialogueKey = Path.Combine(DialoguesFolder, label + ".txt");
						functionKeys.Add(functionKey);
						dialogueKeys.Add(dialogueKey);
						dialogueLines += WriteFunction(function, Path.Combine(splitFolder, functionKey), Path.Combine(splitFolder, dialogueKey));
					}
					File.WriteAllLines(Path.Combine(splitFolder, FunctionKeyFile), functionKeys);
					File.WriteAllLines(Path.Combine(splitFolder, DialogueKeyFile), dialogueKeys);
					onLine("Split " + functions.Count + " functions, " + dialogueLines + " dialogue lines, " + preamble.Count + " preamble lines.");
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
		/// <returns>How many dialogue lines it held.</returns>
		private static int WriteFunction(List<string> function, string functionPath, string dialoguePath) {
			int index = 0;
			using (StreamWriter code = new(functionPath, false, ScriptEncoding)) {
				using (StreamWriter dialogue = new(dialoguePath, false, ScriptEncoding)) {
					code.WriteLine(function[0]);
					dialogue.WriteLine(function[0]);
					for (int at = 1; at < function.Count; at++) {
						string line = function[at];
						if (IsDialogue(line) == false) {
							code.WriteLine(line);
						}
						if (IsDialogue(line) == true) {
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
