// File: Glossary.cs
// Namespace: TranslationTools
using System.Text;
using System.Text.Json;

namespace TranslationTools {

	/// <summary>
	/// One character or term: the name as the script writes it, the name to use, and what
	/// a translator needs to know about them.
	/// </summary>
	public class CharacterEntry {

		/// <summary>The name as it appears in the script, e.g. the Japanese nametag.</summary>
		public string Jp = "";

		/// <summary>The name to use in the translation. Also the file's name.</summary>
		public string En = "";

		/// <summary>Other spellings and nicknames, comma-separated.</summary>
		public string Aliases = "";

		/// <summary>Who they are, in one line.</summary>
		public string Role = "";

		/// <summary>Anything else, in one line.</summary>
		public string Notes = "";

		/// <summary>A longer description of how they speak and who they are; free lines.</summary>
		public string Profile = "";
	}


	/// <summary>
	/// A checkpoint's glossary: its characters and its translation rules, kept inside the
	/// checkpoint as glossary\characters\&lt;English name&gt;.txt and glossary\rules.txt, so each
	/// checkpoint owns its own copy and no two share one. Plain text, hand-editable: a
	/// character file is named lines then a "Profile:" block to the end; the rules file is
	/// one rule per line, in the order they apply.
	/// </summary>
	public static class Glossary {

		/// <summary>The glossary folder's name, allowed at the top level of every checkpoint.</summary>
		public const string FolderName = "glossary";

		public const string CharactersFolder = "characters";
		public const string RulesFile = "rules.txt";

		private const string JpName = "Jp";
		private const string EnName = "En";
		private const string AliasesName = "Aliases";
		private const string RoleName = "Role";
		private const string NotesName = "Notes";
		private const string ProfileHeader = "Profile:";


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
		/// Every character, by file name order. A file that cannot be read is skipped.
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
			}
			return entries;
		}


		/// <summary>
		/// Writes a character to its file, named after its English name. A changed English
		/// name is a new file; the caller removes the old one.
		/// </summary>
		/// <returns>Empty on success, otherwise a plain sentence.</returns>
		public static string SaveCharacter(string checkpointFolder, CharacterEntry entry) {
			string problem = "";
			if (IsFileSafe(entry.En) == false) {
				problem = "The English name has to be usable as a file name.";
			}
			if (problem.Length == 0) {
				try {
					string folder = Path.Combine(FolderOf(checkpointFolder), CharactersFolder);
					Directory.CreateDirectory(folder);
					StringBuilder text = new();
					text.Append(JpName).Append('=').Append(entry.Jp).AppendLine();
					text.Append(EnName).Append('=').Append(entry.En).AppendLine();
					text.Append(AliasesName).Append('=').Append(entry.Aliases).AppendLine();
					text.Append(RoleName).Append('=').Append(entry.Role).AppendLine();
					text.Append(NotesName).Append('=').Append(entry.Notes).AppendLine();
					text.Append(ProfileHeader).AppendLine();
					text.Append(entry.Profile);
					File.WriteAllText(Path.Combine(folder, entry.En + ".txt"), text.ToString(), new UTF8Encoding(false));
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
		public static void RemoveCharacter(string checkpointFolder, string englishName) {
			string path = Path.Combine(FolderOf(checkpointFolder), CharactersFolder, englishName + ".txt");
			if (File.Exists(path) == true) {
				File.Delete(path);
			}
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
		/// Reads the old tool's review.json: its glossary entries become characters, names
		/// that shared a canon token become each other's aliases, and its localization
		/// rules become rules. Existing characters with the same English name are replaced;
		/// rules are appended.
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
							entry.Jp = Text(item, "jp");
							entry.En = Text(item, "en");
							entry.Aliases = Text(item, "aliases");
							entry.Role = Text(item, "role");
							entry.Notes = Text(item, "notes");
							entry.Profile = Text(item, "profile");
							entry.Aliases = MergeAliases(entry, byToken);
							if (entry.En.Length > 0 && SaveCharacter(checkpointFolder, entry).Length == 0) {
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


		private static CharacterEntry ReadCharacter(string path) {
			CharacterEntry entry = new();
			StringBuilder profile = new();
			bool inProfile = false;
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
						if (name == JpName) {
							entry.Jp = value;
						}
						if (name == EnName) {
							entry.En = value;
						}
						if (name == AliasesName) {
							entry.Aliases = value;
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
			if (entry.En.Length == 0) {
				entry.En = Path.GetFileNameWithoutExtension(path);
			}
			return entry;
		}


		private static bool IsFileSafe(string name) {
			bool safe = name.Trim().Length > 0;
			foreach (char bad in Path.GetInvalidFileNameChars()) {
				if (name.IndexOf(bad) >= 0) {
					safe = false;
				}
			}
			return safe;
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
		/// Names that shared this character's canon token, added to its aliases.
		/// </summary>
		private static string MergeAliases(CharacterEntry entry, Dictionary<string, List<string>> byToken) {
			List<string> aliases = new();
			foreach (string alias in entry.Aliases.Split(',')) {
				if (alias.Trim().Length > 0) {
					aliases.Add(alias.Trim());
				}
			}
			foreach (string token in byToken.Keys) {
				List<string> names = byToken[token];
				bool mine = names.Contains(entry.Jp) == true || names.Contains(entry.En) == true;
				if (mine == true) {
					foreach (string name in names) {
						bool known = name == entry.Jp || name == entry.En || aliases.Contains(name) == true;
						if (known == false) {
							aliases.Add(name);
						}
					}
				}
			}
			return string.Join(", ", aliases);
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
