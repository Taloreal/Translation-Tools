// File: Glossary.cs
// Namespace: TranslationTools
using System.Text;
using System.Text.Json;

namespace TranslationTools {

	/// <summary>
	/// One character: every name any script or version uses for them, which of those the
	/// translation writes, and what a translator needs to know about them. No language is
	/// recorded anywhere; a name is a name.
	/// </summary>
	public class CharacterEntry {

		/// <summary>Every name this character goes by, in any script or version. The written name is among them.</summary>
		public List<string> Names = new();

		/// <summary>The name the translation writes. Also the file's identity.</summary>
		public string Written = "";

		/// <summary>
		/// True while the written name came from VNDB rather than a script. The first script
		/// tag that joins the character becomes the written name without a question and
		/// clears this.
		/// </summary>
		public bool Provisional = false;

		/// <summary>Who they are, in one line.</summary>
		public string Role = "";

		/// <summary>Anything else, in one line.</summary>
		public string Notes = "";

		/// <summary>A longer description of how they speak and who they are; free lines.</summary>
		public string Profile = "";


		/// <summary>
		/// A list of names from one comma-separated line, trimmed, blanks and repeats dropped.
		/// </summary>
		public static List<string> SplitNames(string commaSeparated) {
			List<string> names = new();
			foreach (string piece in commaSeparated.Split(',')) {
				string name = piece.Trim();
				if (name.Length > 0 && Contains(names, name) == false) {
					names.Add(name);
				}
			}
			return names;
		}


		/// <summary>
		/// Whether a list holds a name, ignoring case.
		/// </summary>
		public static bool Contains(List<string> names, string name) {
			bool found = false;
			foreach (string known in names) {
				if (found == false && string.Equals(known, name.Trim(), StringComparison.OrdinalIgnoreCase)) {
					found = true;
				}
			}
			return found;
		}


		/// <summary>The names as one comma-separated line, the written name first.</summary>
		public string NamesText {
			get { return string.Join(", ", Ordered()); }
		}


		/// <summary>
		/// Whether a name is one of this character's, ignoring case.
		/// </summary>
		public bool Has(string name) {
			return Contains(Names, name);
		}


		/// <summary>
		/// Adds a name unless the character already has it.
		/// </summary>
		/// <returns>True when the name was new.</returns>
		public bool Add(string name) {
			bool added = false;
			if (name.Trim().Length > 0 && Has(name) == false) {
				Names.Add(name.Trim());
				added = true;
			}
			return added;
		}


		/// <summary>
		/// The names other than the written one.
		/// </summary>
		public List<string> Others() {
			List<string> others = new();
			foreach (string name in Names) {
				if (string.Equals(name, Written, StringComparison.OrdinalIgnoreCase) == false) {
					others.Add(name);
				}
			}
			return others;
		}


		/// <summary>
		/// The names with the written one first.
		/// </summary>
		public List<string> Ordered() {
			List<string> ordered = new();
			if (Written.Length > 0) {
				ordered.Add(Written);
			}
			ordered.AddRange(Others());
			return ordered;
		}
	}


	/// <summary>
	/// A checkpoint's glossary: its characters and its translation rules, kept inside the
	/// checkpoint as glossary\characters\&lt;written name in hex&gt;.txt and glossary\rules.txt,
	/// so each checkpoint owns its own copy and no two share one. Plain text, hand-editable:
	/// a character file is named lines then a "Profile:" block to the end; the rules file is
	/// one rule per line, in the order they apply. A name belongs to one character only:
	/// a name found on two is dropped from both when the glossary is read, and a save that
	/// would create such a clash is refused.
	/// </summary>
	public static class Glossary {

		/// <summary>The glossary folder's name, allowed at the top level of every checkpoint.</summary>
		public const string FolderName = "glossary";

		public const string CharactersFolder = "characters";
		public const string RulesFile = "rules.txt";

		private const string NamesName = "Names";
		private const string WritesName = "Writes";
		private const string ProvisionalName = "Provisional";
		private const string RoleName = "Role";
		private const string NotesName = "Notes";
		private const string ProfileHeader = "Profile:";

		// Older builds kept two languages and a list of aliases. Still read; written back
		// in the names shape on the next save.
		private const string OldJpName = "Jp";
		private const string OldEnName = "En";
		private const string OldAliasesName = "Aliases";

		/// <summary>What the last read repaired, keyed by checkpoint folder, until a menu takes it.</summary>
		private static readonly Dictionary<string, string> repairNotes = new(StringComparer.OrdinalIgnoreCase);


		/// <summary>The glossary folder of a checkpoint folder.</summary>
		public static string FolderOf(string checkpointFolder) {
			return Path.Combine(checkpointFolder, FolderName);
		}


		/// <summary>
		/// Whether a checkpoint has any glossary content at all.
		/// </summary>
		public static bool Exists(string checkpointFolder) {
			return Characters(checkpointFolder).Count > 0 || Rules(checkpointFolder).Count > 0;
		}


		/// <summary>
		/// Every character, by file name order. A file that cannot be read is skipped. A name
		/// held by more than one character is dropped from all of them, the repair is written
		/// back and logged, and the note waits in TakeRepairNote for the Characters menu.
		/// </summary>
		public static List<CharacterEntry> Characters(string checkpointFolder) {
			List<CharacterEntry> entries = new();
			string folder = Path.Combine(FolderOf(checkpointFolder), CharactersFolder);
			if (Directory.Exists(folder) == true) {
				string[] files = Directory.GetFiles(folder, "*.txt");
				Array.Sort(files, StringComparer.OrdinalIgnoreCase);
				foreach (string file in files) {
					try {
						entries.Add(ReadCharacter(file));
					}
					catch (Exception) {
						// Skipped; the menu lists what reads.
					}
				}
				string repaired = RepairClashes(checkpointFolder, entries);
				if (repaired.Length > 0) {
					repairNotes[checkpointFolder] = repaired;
					CheckpointLog.Warning(checkpointFolder, "Glossary", repaired);
				}
			}
			return entries;
		}


		/// <summary>
		/// What the last read of this checkpoint's characters repaired, once; empty when nothing.
		/// </summary>
		public static string TakeRepairNote(string checkpointFolder) {
			string note = "";
			if (repairNotes.ContainsKey(checkpointFolder) == true) {
				note = repairNotes[checkpointFolder];
				repairNotes.Remove(checkpointFolder);
			}
			return note;
		}


		/// <summary>
		/// The character that has a name, or null.
		/// </summary>
		public static CharacterEntry? Find(List<CharacterEntry> characters, string name) {
			CharacterEntry? found = null;
			foreach (CharacterEntry entry in characters) {
				if (found == null && entry.Has(name) == true) {
					found = entry;
				}
			}
			return found;
		}


		/// <summary>
		/// Writes a character to its file, named after its written name, after checking that
		/// none of its names belongs to another character. When the written name changed,
		/// the old file goes.
		/// </summary>
		/// <param name="checkpointFolder">The checkpoint.</param>
		/// <param name="entry">The character to write.</param>
		/// <param name="replaces">The written name the character had before, or empty for a new one.</param>
		/// <returns>Empty on success, otherwise a plain sentence.</returns>
		public static string SaveCharacter(string checkpointFolder, CharacterEntry entry, string replaces) {
			string problem = "";
			entry.Written = entry.Written.Trim();
			if (entry.Written.Length == 0 && entry.Names.Count > 0) {
				entry.Written = entry.Names[0];
			}
			if (entry.Written.Length == 0) {
				problem = "A character needs at least one name.";
			}
			if (problem.Length == 0) {
				entry.Add(entry.Written);
				problem = ClashProblem(checkpointFolder, entry, replaces);
			}
			if (problem.Length == 0) {
				try {
					string folder = Path.Combine(FolderOf(checkpointFolder), CharactersFolder);
					Directory.CreateDirectory(folder);
					if (replaces.Length > 0 && string.Equals(replaces, entry.Written, StringComparison.Ordinal) == false) {
						RemoveCharacter(checkpointFolder, replaces);
					}
					WriteCharacter(Path.Combine(folder, FileNameFor(entry.Written)), entry);
					RemoveOldFile(folder, entry.Written);
				}
				catch (Exception exception) {
					problem = "Could not write the character: " + exception.Message;
				}
			}
			return problem;
		}


		/// <summary>
		/// Deletes a character's file.
		/// </summary>
		public static void RemoveCharacter(string checkpointFolder, string writtenName) {
			string folder = Path.Combine(FolderOf(checkpointFolder), CharactersFolder);
			string path = Path.Combine(folder, FileNameFor(writtenName));
			if (File.Exists(path) == true) {
				File.Delete(path);
			}
			RemoveOldFile(folder, writtenName);
		}


		/// <summary>
		/// Deletes every character file.
		/// </summary>
		/// <returns>How many were deleted.</returns>
		public static int RemoveAllCharacters(string checkpointFolder) {
			int removed = 0;
			string folder = Path.Combine(FolderOf(checkpointFolder), CharactersFolder);
			if (Directory.Exists(folder) == true) {
				foreach (string file in Directory.GetFiles(folder, "*.txt")) {
					File.Delete(file);
					removed++;
				}
			}
			return removed;
		}


		/// <summary>
		/// The rules, one per line, in order. Blank lines are not rules.
		/// </summary>
		public static List<string> Rules(string checkpointFolder) {
			List<string> rules = new();
			string path = Path.Combine(FolderOf(checkpointFolder), RulesFile);
			if (File.Exists(path) == true) {
				foreach (string line in File.ReadAllLines(path)) {
					if (line.Trim().Length > 0) {
						rules.Add(line.Trim());
					}
				}
			}
			return rules;
		}


		/// <summary>
		/// Writes the rules file whole.
		/// </summary>
		/// <returns>Empty on success, otherwise a plain sentence.</returns>
		public static string SaveRules(string checkpointFolder, List<string> rules) {
			string problem = "";
			try {
				Directory.CreateDirectory(FolderOf(checkpointFolder));
				File.WriteAllLines(Path.Combine(FolderOf(checkpointFolder), RulesFile), rules, new UTF8Encoding(false));
			}
			catch (Exception exception) {
				problem = "Could not write the rules: " + exception.Message;
			}
			return problem;
		}


		/// <summary>
		/// Whether two checkpoints' glossaries are the same: the same files with the same
		/// bytes. Two copies of one glossary are one glossary for choosing purposes.
		/// </summary>
		public static bool SameContent(string firstCheckpointFolder, string secondCheckpointFolder) {
			Dictionary<string, string> first = FilesOf(FolderOf(firstCheckpointFolder));
			Dictionary<string, string> second = FilesOf(FolderOf(secondCheckpointFolder));
			bool same = first.Count == second.Count;
			foreach (string relative in first.Keys) {
				if (same == true) {
					bool matched = second.ContainsKey(relative) == true
						&& File.ReadAllBytes(first[relative]).AsSpan().SequenceEqual(File.ReadAllBytes(second[relative]));
					if (matched == false) {
						same = false;
					}
				}
			}
			return same;
		}


		/// <summary>
		/// Every file under a glossary folder, keyed by its relative path, case-insensitive.
		/// </summary>
		private static Dictionary<string, string> FilesOf(string glossaryFolder) {
			Dictionary<string, string> files = new(StringComparer.OrdinalIgnoreCase);
			if (Directory.Exists(glossaryFolder) == true) {
				foreach (string file in Directory.GetFiles(glossaryFolder, "*", SearchOption.AllDirectories)) {
					files.Add(Path.GetRelativePath(glossaryFolder, file), file);
				}
			}
			return files;
		}


		/// <summary>
		/// Replaces one checkpoint's glossary with a copy of another's. The target's glossary
		/// folder is emptied first, so nothing of its old glossary survives.
		/// </summary>
		/// <returns>Empty on success, otherwise a plain sentence.</returns>
		public static string CopyOver(string fromCheckpointFolder, string toCheckpointFolder) {
			string problem = "";
			string from = FolderOf(fromCheckpointFolder);
			string to = FolderOf(toCheckpointFolder);
			if (Directory.Exists(from) == false) {
				problem = "There is no glossary to copy.";
			}
			if (problem.Length == 0) {
				try {
					Directory.CreateDirectory(to);
					FolderClearing.DeleteContents(to);
					CopyTree(from, to);
				}
				catch (Exception exception) {
					problem = "Could not copy the glossary: " + exception.Message;
				}
			}
			return problem;
		}


		/// <summary>
		/// Reads the old tool's review.json: its glossary entries become characters with the
		/// English name written, names that shared a canon token become each other's names,
		/// and its localization rules become rules. Existing characters with the same written
		/// name are replaced; rules are appended.
		/// </summary>
		/// <param name="reviewPath">The review.json.</param>
		/// <param name="checkpointFolder">The checkpoint to import into.</param>
		/// <param name="characters">How many characters were written.</param>
		/// <param name="rules">How many rules were added.</param>
		/// <returns>Empty on success, otherwise a plain sentence.</returns>
		public static string ImportReview(string reviewPath, string checkpointFolder, out int characters, out int rules) {
			characters = 0;
			rules = 0;
			string problem = "";
			try {
				using (JsonDocument document = JsonDocument.Parse(File.ReadAllText(reviewPath))) {
					JsonElement root = document.RootElement;
					Dictionary<string, List<string>> byToken = new(StringComparer.Ordinal);
					if (TryProperty(root, "namePairs", out JsonElement pairs) == true && pairs.ValueKind == JsonValueKind.Object) {
						foreach (JsonProperty pair in pairs.EnumerateObject()) {
							string token = pair.Value.GetString() ?? "";
							if (byToken.ContainsKey(token) == false) {
								byToken.Add(token, new List<string>());
							}
							byToken[token].Add(pair.Name);
						}
					}
					if (TryProperty(root, "glossary", out JsonElement glossary) == true && glossary.ValueKind == JsonValueKind.Array) {
						foreach (JsonElement item in glossary.EnumerateArray()) {
							CharacterEntry entry = new();
							entry.Written = Text(item, "en").Trim();
							entry.Add(entry.Written);
							entry.Add(Text(item, "jp"));
							foreach (string alias in CharacterEntry.SplitNames(Text(item, "aliases"))) {
								entry.Add(alias);
							}
							entry.Role = Text(item, "role");
							entry.Notes = Text(item, "notes");
							entry.Profile = Text(item, "profile");
							MergeTokenNames(entry, byToken);
							if (entry.Written.Length > 0 && SaveCharacter(checkpointFolder, entry, entry.Written).Length == 0) {
								characters += 1;
							}
						}
					}
					List<string> existing = Rules(checkpointFolder);
					if (TryProperty(root, "llm", out JsonElement llm) == true && TryProperty(llm, "localizationRules", out JsonElement ruleList) == true) {
						foreach (string rule in ReadRules(ruleList)) {
							if (existing.Contains(rule) == false) {
								existing.Add(rule);
								rules += 1;
							}
						}
					}
					if (rules > 0) {
						problem = SaveRules(checkpointFolder, existing);
					}
				}
			}
			catch (Exception exception) {
				problem = "Could not read " + reviewPath + ": " + exception.Message;
			}
			return problem;
		}


		/// <summary>
		/// The sentence a save gets when one of the character's names already belongs to
		/// another character; empty when every name is free.
		/// </summary>
		private static string ClashProblem(string checkpointFolder, CharacterEntry entry, string replaces) {
			string problem = "";
			string folder = Path.Combine(FolderOf(checkpointFolder), CharactersFolder);
			if (Directory.Exists(folder) == true) {
				foreach (string file in Directory.GetFiles(folder, "*.txt")) {
					if (problem.Length == 0) {
						try {
							CharacterEntry theirs = ReadCharacter(file);
							// The character being saved, under its old or new written name,
							// whatever file an older build gave it, is not another character.
							bool self = string.Equals(theirs.Written, entry.Written, StringComparison.OrdinalIgnoreCase)
								|| (replaces.Length > 0 && string.Equals(theirs.Written, replaces, StringComparison.OrdinalIgnoreCase));
							if (self == false) {
								foreach (string mine in entry.Names) {
									if (problem.Length == 0 && theirs.Has(mine) == true) {
										problem = "\"" + mine + "\" is already a name of " + theirs.Written + ". A name belongs to one character only.";
									}
								}
							}
						}
						catch (Exception) {
							// An unreadable file holds no names to clash with.
						}
					}
				}
			}
			return problem;
		}


		/// <summary>
		/// Drops every name that more than one character holds from all of them, writes the
		/// changed characters back, and deletes any left with no name. A character that lost
		/// its written name takes its first remaining one, and its file moves.
		/// </summary>
		/// <returns>One sentence per repair, joined; empty when there was no clash.</returns>
		private static string RepairClashes(string checkpointFolder, List<CharacterEntry> entries) {
			List<string> notes = new();
			List<string> clashing = new();
			for (int first = 0; first < entries.Count; first++) {
				for (int second = first + 1; second < entries.Count; second++) {
					foreach (string name in entries[first].Names) {
						if (entries[second].Has(name) == true && CharacterEntry.Contains(clashing, name) == false) {
							clashing.Add(name);
						}
					}
				}
			}
			foreach (string name in clashing) {
				List<string> holders = new();
				foreach (CharacterEntry entry in entries) {
					if (entry.Has(name) == true) {
						holders.Add(entry.Written);
					}
				}
				notes.Add("\"" + name + "\" was a name of " + string.Join(" and ", holders) + "; dropped from all of them");
			}
			if (clashing.Count > 0) {
				List<CharacterEntry> gone = new();
				foreach (CharacterEntry entry in entries) {
					bool touched = false;
					string wasWritten = entry.Written;
					List<string> kept = new();
					foreach (string name in entry.Names) {
						if (CharacterEntry.Contains(clashing, name) == true) {
							touched = true;
						}
						if (CharacterEntry.Contains(clashing, name) == false) {
							kept.Add(name);
						}
					}
					if (touched == true) {
						entry.Names = kept;
						if (kept.Count == 0) {
							RemoveCharacter(checkpointFolder, wasWritten);
							gone.Add(entry);
							notes.Add(wasWritten + " had no name left and was removed");
						}
						if (kept.Count > 0) {
							if (entry.Has(wasWritten) == false) {
								entry.Written = kept[0];
								notes.Add(wasWritten + " now writes " + entry.Written);
							}
							string folder = Path.Combine(FolderOf(checkpointFolder), CharactersFolder);
							if (string.Equals(wasWritten, entry.Written, StringComparison.Ordinal) == false) {
								RemoveCharacter(checkpointFolder, wasWritten);
							}
							WriteCharacter(Path.Combine(folder, FileNameFor(entry.Written)), entry);
						}
					}
				}
				foreach (CharacterEntry entry in gone) {
					entries.Remove(entry);
				}
			}
			return string.Join(". ", notes);
		}


		private static void WriteCharacter(string path, CharacterEntry entry) {
			StringBuilder text = new();
			text.Append(NamesName).Append('=').Append(entry.NamesText).AppendLine();
			text.Append(WritesName).Append('=').Append(entry.Written).AppendLine();
			if (entry.Provisional == true) {
				text.Append(ProvisionalName).Append("=true").AppendLine();
			}
			text.Append(RoleName).Append('=').Append(entry.Role).AppendLine();
			text.Append(NotesName).Append('=').Append(entry.Notes).AppendLine();
			text.Append(ProfileHeader).AppendLine();
			text.Append(entry.Profile);
			File.WriteAllText(path, text.ToString(), new UTF8Encoding(false));
		}


		private static CharacterEntry ReadCharacter(string path) {
			CharacterEntry entry = new();
			StringBuilder profile = new();
			bool inProfile = false;
			string oldJp = "";
			string oldEn = "";
			string oldAliases = "";
			foreach (string line in File.ReadAllLines(path)) {
				if (inProfile == true) {
					if (profile.Length > 0) {
						profile.AppendLine();
					}
					profile.Append(line);
				}
				if (inProfile == false && line.Trim() == ProfileHeader) {
					inProfile = true;
				}
				if (inProfile == false) {
					int equals = line.IndexOf('=');
					if (equals > 0) {
						string name = line.Substring(0, equals);
						string value = line.Substring(equals + 1);
						if (name == NamesName) {
							entry.Names = CharacterEntry.SplitNames(value);
						}
						if (name == WritesName) {
							entry.Written = value.Trim();
						}
						if (name == ProvisionalName) {
							entry.Provisional = value.Trim() == "true";
						}
						if (name == OldJpName) {
							oldJp = value.Trim();
						}
						if (name == OldEnName) {
							oldEn = value.Trim();
						}
						if (name == OldAliasesName) {
							oldAliases = value;
						}
						if (name == RoleName) {
							entry.Role = value;
						}
						if (name == NotesName) {
							entry.Notes = value;
						}
					}
				}
			}
			entry.Profile = profile.ToString();
			if (entry.Names.Count == 0) {
				// The older shape: English name written, script name and aliases the rest.
				entry.Add(oldEn);
				entry.Add(oldJp);
				foreach (string alias in CharacterEntry.SplitNames(oldAliases)) {
					entry.Add(alias);
				}
				entry.Written = oldEn;
			}
			if (entry.Written.Length == 0 && entry.Names.Count > 0) {
				entry.Written = entry.Names[0];
			}
			if (entry.Written.Length == 0) {
				entry.Written = Path.GetFileNameWithoutExtension(path);
			}
			entry.Add(entry.Written);
			return entry;
		}


		/// <summary>
		/// The file a character is kept in: its written name's UTF-8 bytes as hex, so any
		/// name at all - "???" is a common placeholder speaker - has a file, and the name
		/// inside the file stays exactly as typed. The name is read from inside the file,
		/// never from the file name.
		/// </summary>
		public static string FileNameFor(string writtenName) {
			StringBuilder name = new();
			foreach (byte piece in Encoding.UTF8.GetBytes(writtenName.Trim())) {
				name.Append(piece.ToString("X2"));
			}
			return name.ToString() + ".txt";
		}


		/// <summary>
		/// The file an older build gave a character: the name itself. Empty when that name
		/// could not be a file name, since no such file can exist.
		/// </summary>
		private static string OldFileNameFor(string writtenName) {
			string name = writtenName.Trim() + ".txt";
			foreach (char bad in Path.GetInvalidFileNameChars()) {
				if (writtenName.IndexOf(bad) >= 0) {
					name = "";
				}
			}
			return name;
		}


		/// <summary>
		/// Deletes a character's file from an older build, if one exists, so a save under
		/// the hex name leaves no duplicate.
		/// </summary>
		private static void RemoveOldFile(string folder, string writtenName) {
			string old = OldFileNameFor(writtenName);
			if (old.Length > 0 && File.Exists(Path.Combine(folder, old)) == true) {
				File.Delete(Path.Combine(folder, old));
			}
		}


		private static void CopyTree(string from, string to) {
			Directory.CreateDirectory(to);
			foreach (string file in Directory.GetFiles(from)) {
				File.Copy(file, Path.Combine(to, Path.GetFileName(file)), true);
			}
			foreach (string directory in Directory.GetDirectories(from)) {
				CopyTree(directory, Path.Combine(to, Path.GetFileName(directory)));
			}
		}


		private static bool TryProperty(JsonElement element, string name, out JsonElement value) {
			value = default;
			return element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out value) == true;
		}


		private static string Text(JsonElement element, string name) {
			string text = "";
			if (TryProperty(element, name, out JsonElement value) == true && value.ValueKind == JsonValueKind.String) {
				text = value.GetString() ?? "";
			}
			return text;
		}


		/// <summary>
		/// Names that shared a canon token with one of this character's, added to its names.
		/// </summary>
		private static void MergeTokenNames(CharacterEntry entry, Dictionary<string, List<string>> byToken) {
			foreach (string token in byToken.Keys) {
				List<string> names = byToken[token];
				bool mine = false;
				foreach (string name in names) {
					if (entry.Has(name) == true) {
						mine = true;
					}
				}
				if (mine == true) {
					foreach (string name in names) {
						entry.Add(name);
					}
				}
			}
		}


		private static List<string> ReadRules(JsonElement ruleList) {
			List<string> rules = new();
			if (ruleList.ValueKind == JsonValueKind.String) {
				foreach (string line in (ruleList.GetString() ?? "").Replace("\r", "").Split('\n')) {
					if (line.Trim().Length > 0) {
						rules.Add(line.Trim());
					}
				}
			}
			if (ruleList.ValueKind == JsonValueKind.Array) {
				foreach (JsonElement item in ruleList.EnumerateArray()) {
					string text = "";
					if (item.ValueKind == JsonValueKind.String) {
						text = item.GetString() ?? "";
					}
					if (item.ValueKind == JsonValueKind.Object) {
						text = Text(item, "text");
					}
					if (text.Trim().Length > 0) {
						rules.Add(text.Trim());
					}
				}
			}
			return rules;
		}
	}
}
