// File: GlossariesMenu.cs
// Namespace: TranslationTools
using TALOREAL_NETCORE_API;

namespace TranslationTools {

	/// <summary>
	/// The selected checkpoint's glossary: its characters and its translation rules, each
	/// with its own menu, plus moving a glossary between checkpoints - out to another one
	/// you pick, or in from a checkpoint of the same game - and importing the old tool's.
	/// Long lists are shown through PagedPicker, never dumped above a menu.
	/// </summary>
	public static class GlossariesMenu {

		/// <summary>
		/// Shows the menu until the user chooses Back. A checkpoint with no glossary is
		/// offered one from a checkpoint of the same game first, if there is one.
		/// </summary>
		public static void Show() {
			Checkpoint? selected = CheckpointList.Selected();
			if (selected == null) {
				Console.WriteLine("No checkpoint is selected.");
				ConsoleExt.WaitForEnter("continue");
			}
			if (selected != null) {
				string folder = CheckpointInspector.FolderOf(selected.Path);
				if (Glossary.Exists(folder) == false) {
					OfferFromSameGame(selected, folder, true);
				}
				ConsoleSelectMenu menu = new(loops: true, numbered: false, clearOnRefresh: true);
				menu.AddOnDrawMenuAction(RefreshHeader);
				menu.AddChoice(new ConsoleMenuItem("Characters...").SetActionOnSelect(CharactersMenu.Show));
				menu.AddChoice(new ConsoleMenuItem("Translation rules...").SetActionOnSelect(RulesMenu.Show));
				menu.AddChoice(new ConsoleMenuItem("Copy this glossary to another checkpoint (replaces that one's)").SetActionOnSelect(CopyToAnother));
				menu.AddChoice(new ConsoleMenuItem("Take the glossary from a checkpoint of the same game (replaces this one's)").SetActionOnSelect(TakeFromSameGame));
				// "Import from the old tool's review.json" is delisted: it did its job. ImportOld
				// stays, so it can be listed again by adding the item back here.
				menu.AddChoice(new ConsoleMenuItem("Back"));
				menu.GetChoice();
			}
		}


		/// <summary>
		/// Offers to copy a same-game checkpoint's glossary here. Called when a checkpoint
		/// gets its game and when this menu opens on an empty glossary.
		/// </summary>
		/// <param name="checkpoint">The checkpoint that would receive the copy.</param>
		/// <param name="folder">Its folder.</param>
		/// <param name="pause">Whether to wait for Enter after the outcome.</param>
		public static void OfferFromSameGame(Checkpoint checkpoint, string folder, bool pause) {
			List<Checkpoint> sources = SameGameWithGlossary(checkpoint, folder);
			if (sources.Count > 0) {
				Checkpoint? source = sources[0];
				if (sources.Count > 1) {
					source = PickCheckpoint(sources, "Several checkpoints of this game have a glossary. Copy which one here?");
				}
				if (source != null) {
					bool yes = ConsoleExt.ReadValue<bool>("\"" + source.Label + "\" is the same game and has a glossary. Copy it to \"" + checkpoint.Label + "\"? (y/n): ", false);
					if (yes == true) {
						string problem = Glossary.CopyOver(CheckpointInspector.FolderOf(source.Path), folder);
						Report(folder, problem, "copied the glossary from \"" + source.Label + "\"");
						if (pause == true) {
							ConsoleExt.WaitForEnter("continue");
						}
					}
				}
			}
		}


		/// <summary>
		/// The selected checkpoint's folder, or empty when none is selected.
		/// </summary>
		public static string SelectedFolder() {
			Checkpoint? selected = CheckpointList.Selected();
			string folder = "";
			if (selected != null) {
				folder = CheckpointInspector.FolderOf(selected.Path);
			}
			return folder;
		}


		private static void RefreshHeader(ConsoleSelectMenu menu) {
			Checkpoint? selected = CheckpointList.Selected();
			string header = "-- Glossaries --\n";
			if (selected != null) {
				string folder = CheckpointInspector.FolderOf(selected.Path);
				CheckpointInfo info = CheckpointInfo.Load(folder);
				string game = "game not recorded yet";
				if (info.GameName.Length > 0) {
					game = info.GameName;
				}
				header += selected.Label + "  ·  " + game + "\n"
					+ Glossary.Characters(folder).Count + " characters, " + Glossary.Rules(folder).Count + " rules\n";
			}
			menu.SetPreChoiceText(header);
		}


		/// <summary>
		/// The other checkpoints whose game matches this one's and that have a glossary.
		/// Matched by VNDB id when both have one, else by name.
		/// </summary>
		private static List<Checkpoint> SameGameWithGlossary(Checkpoint checkpoint, string folder) {
			List<Checkpoint> sources = new();
			CheckpointInfo mine = CheckpointInfo.Load(folder);
			if (mine.GameName.Length > 0) {
				foreach (Checkpoint other in CheckpointList.All()) {
					bool different = CheckpointList.SameLabel(other.Label, checkpoint.Label) == false;
					if (different == true) {
						string otherFolder = CheckpointInspector.FolderOf(other.Path);
						CheckpointInfo theirs = CheckpointInfo.Load(otherFolder);
						bool sameGame = mine.VndbId.Length > 0 && theirs.VndbId.Length > 0
							? string.Equals(mine.VndbId, theirs.VndbId, StringComparison.OrdinalIgnoreCase)
							: CheckpointList.SameLabel(mine.GameName, theirs.GameName);
						if (sameGame == true && Glossary.Exists(otherFolder) == true) {
							// Two copies of one glossary are one choice. Only glossaries that
							// differ are worth asking about.
							bool duplicate = false;
							foreach (Checkpoint kept in sources) {
								if (Glossary.SameContent(CheckpointInspector.FolderOf(kept.Path), otherFolder) == true) {
									duplicate = true;
								}
							}
							if (duplicate == false) {
								sources.Add(other);
							}
						}
					}
				}
			}
			return sources;
		}


		/// <summary>
		/// A paged pick over checkpoints, or null for Back.
		/// </summary>
		private static Checkpoint? PickCheckpoint(List<Checkpoint> choices, string question) {
			Checkpoint? picked = null;
			List<string> rows = new();
			foreach (Checkpoint choice in choices) {
				rows.Add(choice.Label + "   " + choice.Path);
			}
			int index = PagedPicker.Pick(rows, question);
			if (index >= 0) {
				picked = choices[index];
			}
			return picked;
		}


		/// <summary>
		/// Copies the selected checkpoint's glossary out to a checkpoint the user picks,
		/// replacing whatever that one had.
		/// </summary>
		private static void CopyToAnother() {
			Checkpoint? selected = CheckpointList.Selected();
			if (selected != null) {
				List<Checkpoint> others = new();
				foreach (Checkpoint other in CheckpointList.All()) {
					if (CheckpointList.SameLabel(other.Label, selected.Label) == false) {
						others.Add(other);
					}
				}
				if (others.Count == 0) {
					Console.WriteLine("There is no other checkpoint to copy to.");
					ConsoleExt.WaitForEnter("continue");
				}
				if (others.Count > 0) {
					Checkpoint? target = PickCheckpoint(others, "Copy \"" + selected.Label + "\"'s glossary to which checkpoint? Its own glossary is replaced.");
					if (target != null) {
						string targetFolder = CheckpointInspector.FolderOf(target.Path);
						string had = "";
						if (Glossary.Exists(targetFolder) == true) {
							had = " It has " + Glossary.Characters(targetFolder).Count + " characters and " + Glossary.Rules(targetFolder).Count + " rules now, which go.";
						}
						bool yes = ConsoleExt.ReadValue<bool>("Replace \"" + target.Label + "\"'s glossary?" + had + " (y/n): ", false);
						if (yes == true) {
							string problem = Glossary.CopyOver(CheckpointInspector.FolderOf(selected.Path), targetFolder);
							Report(targetFolder, problem, "glossary replaced by a copy of \"" + selected.Label + "\"'s");
						}
						if (yes == false) {
							Console.WriteLine("Kept.");
						}
						ConsoleExt.WaitForEnter("continue");
					}
				}
			}
		}


		/// <summary>
		/// Takes a same-game checkpoint's glossary without a further question: choosing the
		/// item is the choice. Picks between several; says so when there is none.
		/// </summary>
		private static void TakeFromSameGame() {
			Checkpoint? selected = CheckpointList.Selected();
			if (selected != null) {
				string folder = CheckpointInspector.FolderOf(selected.Path);
				List<Checkpoint> sources = SameGameWithGlossary(selected, folder);
				Checkpoint? source = null;
				if (sources.Count == 0) {
					Console.WriteLine("No other checkpoint of this game has a glossary.");
				}
				if (sources.Count == 1) {
					source = sources[0];
				}
				if (sources.Count > 1) {
					source = PickCheckpoint(sources, "Take which checkpoint's glossary?");
				}
				if (source != null) {
					string problem = Glossary.CopyOver(CheckpointInspector.FolderOf(source.Path), folder);
					Report(folder, problem, "glossary replaced by a copy of \"" + source.Label + "\"'s");
				}
				if (sources.Count != 1 && source == null && sources.Count > 0) {
					Console.WriteLine("Nothing taken.");
				}
				ConsoleExt.WaitForEnter("continue");
			}
		}


		/// <summary>
		/// Imports the old tool's review.json into the selected checkpoint's glossary. Not in
		/// the menu any more - it served its purpose - but kept whole in case it is needed.
		/// </summary>
		public static void ImportOld() {
			Checkpoint? selected = CheckpointList.Selected();
			if (selected != null) {
				string folder = CheckpointInspector.FolderOf(selected.Path);
				string path = ConsoleExt.ReadLine("Path to the old tool's review.json (blank to cancel): ", -1, false).Trim().Trim('"');
				if (path.Length > 0 && File.Exists(path) == false) {
					Console.WriteLine("Nothing at " + path);
				}
				if (path.Length > 0 && File.Exists(path) == true) {
					string problem = Glossary.ImportReview(path, folder, out int characters, out int rules);
					if (problem.Length > 0) {
						Console.WriteLine(problem);
					}
					if (problem.Length == 0) {
						Console.WriteLine("Imported " + characters + " characters and " + rules + " rules. Characters with the same English name were replaced; rules were added.");
						CheckpointLog.Warning(folder, "Glossary", "imported " + characters + " characters and " + rules + " rules from " + path);
					}
				}
				if (path.Length > 0) {
					ConsoleExt.WaitForEnter("continue");
				}
			}
		}


		private static void Report(string folder, string problem, string done) {
			if (problem.Length > 0) {
				Console.WriteLine(problem);
			}
			if (problem.Length == 0) {
				Console.WriteLine("Done: " + done + ".");
				CheckpointLog.Warning(folder, "Glossary", done);
			}
		}
	}


	/// <summary>
	/// The characters of the selected checkpoint's glossary. The list is paged; picking a
	/// row shows that character in full.
	/// </summary>
	public static class CharactersMenu {

		public static void Show() {
			ConsoleSelectMenu menu = new(loops: true, numbered: false, clearOnRefresh: true);
			menu.AddOnDrawMenuAction(RefreshHeader);
			menu.AddChoice(new ConsoleMenuItem("List characters").SetActionOnSelect(List));
			menu.AddChoice(new ConsoleMenuItem("Add a character").SetActionOnSelect(Add));
			menu.AddChoice(new ConsoleMenuItem("Change a character").SetActionOnSelect(Change));
			menu.AddChoice(new ConsoleMenuItem("Remove a character").SetActionOnSelect(Remove));
			menu.AddChoice(new ConsoleMenuItem("Back"));
			menu.GetChoice();
		}


		private static void RefreshHeader(ConsoleSelectMenu menu) {
			menu.SetPreChoiceText("-- Characters (" + Glossary.Characters(GlossariesMenu.SelectedFolder()).Count + ") --\n");
		}


		/// <summary>
		/// One row per character for the picker: the English name, then the script's.
		/// </summary>
		private static List<string> Rows(List<CharacterEntry> entries) {
			List<string> rows = new();
			foreach (CharacterEntry entry in entries) {
				rows.Add(entry.En + "   " + entry.Jp);
			}
			return rows;
		}


		/// <summary>
		/// A paged pick over the characters, or null for Back.
		/// </summary>
		private static CharacterEntry? Pick(string question) {
			CharacterEntry? picked = null;
			List<CharacterEntry> entries = Glossary.Characters(GlossariesMenu.SelectedFolder());
			int index = PagedPicker.Pick(Rows(entries), question);
			if (index >= 0) {
				picked = entries[index];
			}
			return picked;
		}


		private static void List() {
			bool browsing = true;
			while (browsing == true) {
				CharacterEntry? entry = Pick("Characters - pick one to see it in full");
				if (entry == null) {
					browsing = false;
				}
				if (entry != null) {
					Console.WriteLine(Describe(entry));
					ConsoleExt.WaitForEnter("go back to the list");
				}
			}
		}


		private static void Add() {
			CharacterEntry entry = new();
			entry.En = ConsoleExt.ReadLine("English name (blank to cancel): ", -1, false).Trim();
			if (entry.En.Length > 0) {
				entry.Jp = ConsoleExt.ReadLine("Name as the script writes it: ", -1, false).Trim();
				entry.Aliases = ConsoleExt.ReadLine("Aliases, comma-separated (blank for none): ", -1, false).Trim();
				entry.Role = ConsoleExt.ReadLine("Role, one line (blank for none): ", -1, false).Trim();
				entry.Notes = ConsoleExt.ReadLine("Notes, one line (blank for none): ", -1, false).Trim();
				entry.Profile = ReadProfile("");
				Finish(Glossary.SaveCharacter(GlossariesMenu.SelectedFolder(), entry), "Added " + entry.En + ".");
			}
		}


		private static void Change() {
			CharacterEntry? entry = Pick("Change which character?");
			if (entry != null) {
				string folder = GlossariesMenu.SelectedFolder();
				string oldEn = entry.En;
				ConsoleSelectMenu menu = new(loops: false, numbered: false, clearOnRefresh: true);
				menu.SetPreChoiceText(Describe(entry) + "\nChange which field?");
				menu.AddChoice(new ConsoleMenuItem("English name"));
				menu.AddChoice(new ConsoleMenuItem("Name as the script writes it"));
				menu.AddChoice(new ConsoleMenuItem("Aliases"));
				menu.AddChoice(new ConsoleMenuItem("Role"));
				menu.AddChoice(new ConsoleMenuItem("Notes"));
				menu.AddChoice(new ConsoleMenuItem("Profile"));
				menu.AddChoice(new ConsoleMenuItem("Cancel"));
				int field = menu.GetChoice();
				bool changed = true;
				if (field == 0) {
					entry.En = Ask("English name", entry.En);
				}
				if (field == 1) {
					entry.Jp = Ask("Name as the script writes it", entry.Jp);
				}
				if (field == 2) {
					entry.Aliases = Ask("Aliases", entry.Aliases);
				}
				if (field == 3) {
					entry.Role = Ask("Role", entry.Role);
				}
				if (field == 4) {
					entry.Notes = Ask("Notes", entry.Notes);
				}
				if (field == 5) {
					entry.Profile = ReadProfile(entry.Profile);
				}
				if (field < 0 || field > 5) {
					changed = false;
				}
				if (changed == true) {
					string problem = Glossary.SaveCharacter(folder, entry);
					if (problem.Length == 0 && string.Equals(oldEn, entry.En, StringComparison.Ordinal) == false) {
						Glossary.RemoveCharacter(folder, oldEn);
					}
					Finish(problem, "Changed " + entry.En + ".");
				}
			}
		}


		private static void Remove() {
			CharacterEntry? entry = Pick("Remove which character? Its file is deleted.");
			if (entry != null) {
				bool yes = ConsoleExt.ReadValue<bool>("Remove " + entry.En + "? (y/n): ", false);
				if (yes == true) {
					Glossary.RemoveCharacter(GlossariesMenu.SelectedFolder(), entry.En);
					Finish("", "Removed " + entry.En + ".");
				}
			}
		}


		/// <summary>
		/// Shows the current value, then reads a new one; blank keeps the current.
		/// </summary>
		private static string Ask(string what, string current) {
			Console.WriteLine(what + " now: " + current);
			string typed = ConsoleExt.ReadLine(what + " (blank to keep): ", -1, false).Trim();
			string value = current;
			if (typed.Length > 0) {
				value = typed;
			}
			return value;
		}


		/// <summary>
		/// Reads a profile as free lines until a line holding only a full stop.
		/// </summary>
		private static string ReadProfile(string current) {
			if (current.Length > 0) {
				Console.WriteLine("Profile now:\n" + current);
			}
			Console.WriteLine("Profile: type lines, then a line with only . to finish (nothing at all keeps the current one):");
			List<string> lines = new();
			bool done = false;
			while (done == false) {
				string line = Console.ReadLine() ?? ".";
				if (line.Trim() == ".") {
					done = true;
				}
				if (done == false) {
					lines.Add(line);
				}
			}
			string profile = current;
			if (lines.Count > 0) {
				profile = string.Join(Environment.NewLine, lines);
			}
			return profile;
		}


		private static string Describe(CharacterEntry entry) {
			return entry.En + "  (" + entry.Jp + ")\n  aliases: " + entry.Aliases + "\n  role: " + entry.Role + "\n  notes: " + entry.Notes + "\n  profile: " + entry.Profile.Replace("\r", "").Replace("\n", "\n           ");
		}


		private static void Finish(string problem, string done) {
			if (problem.Length > 0) {
				Console.WriteLine(problem);
			}
			if (problem.Length == 0) {
				Console.WriteLine(done);
			}
			ConsoleExt.WaitForEnter("continue");
		}
	}


	/// <summary>
	/// The translation rules of the selected checkpoint's glossary. Order is meaning: the
	/// list is paged and numbered, and a rule is picked from it.
	/// </summary>
	public static class RulesMenu {

		public static void Show() {
			ConsoleSelectMenu menu = new(loops: true, numbered: false, clearOnRefresh: true);
			menu.AddOnDrawMenuAction(RefreshHeader);
			menu.AddChoice(new ConsoleMenuItem("List rules").SetActionOnSelect(List));
			menu.AddChoice(new ConsoleMenuItem("Add a rule").SetActionOnSelect(Add));
			menu.AddChoice(new ConsoleMenuItem("Change a rule").SetActionOnSelect(Change));
			menu.AddChoice(new ConsoleMenuItem("Move a rule up").SetActionOnSelect(MoveUp));
			menu.AddChoice(new ConsoleMenuItem("Move a rule down").SetActionOnSelect(MoveDown));
			menu.AddChoice(new ConsoleMenuItem("Remove a rule").SetActionOnSelect(Remove));
			menu.AddChoice(new ConsoleMenuItem("Back"));
			menu.GetChoice();
		}


		private static void RefreshHeader(ConsoleSelectMenu menu) {
			menu.SetPreChoiceText("-- Translation rules (" + Glossary.Rules(GlossariesMenu.SelectedFolder()).Count + ") --\n");
		}


		/// <summary>
		/// One numbered row per rule, cut to the window's width for the picker.
		/// </summary>
		private static List<string> Rows(List<string> rules) {
			List<string> rows = new();
			int width = Math.Max(40, Console.WindowWidth) - 8;
			for (int index = 0; index < rules.Count; index++) {
				string rule = rules[index];
				if (rule.Length > width) {
					rule = rule.Substring(0, width - 1) + "~";
				}
				rows.Add((index + 1) + ". " + rule);
			}
			return rows;
		}


		/// <summary>
		/// A paged pick over the rules, or -1 for Back.
		/// </summary>
		private static int PickIndex(List<string> rules, string question) {
			return PagedPicker.Pick(Rows(rules), question);
		}


		private static void List() {
			bool browsing = true;
			while (browsing == true) {
				List<string> rules = Glossary.Rules(GlossariesMenu.SelectedFolder());
				int index = PickIndex(rules, "Rules - pick one to read it whole");
				if (index < 0) {
					browsing = false;
				}
				if (index >= 0) {
					Console.WriteLine((index + 1) + ". " + rules[index]);
					ConsoleExt.WaitForEnter("go back to the list");
				}
			}
		}


		private static void Add() {
			string rule = ConsoleExt.ReadLine("The rule, one line (blank to cancel): ", -1, false).Trim();
			if (rule.Length > 0) {
				string folder = GlossariesMenu.SelectedFolder();
				List<string> rules = Glossary.Rules(folder);
				rules.Add(rule);
				Finish(Glossary.SaveRules(folder, rules), "Added as rule " + rules.Count + ".");
			}
		}


		private static void Change() {
			string folder = GlossariesMenu.SelectedFolder();
			List<string> rules = Glossary.Rules(folder);
			int index = PickIndex(rules, "Change which rule?");
			if (index >= 0) {
				Console.WriteLine("Rule " + (index + 1) + " now: " + rules[index]);
				string typed = ConsoleExt.ReadLine("New wording (blank to keep): ", -1, false).Trim();
				if (typed.Length > 0) {
					rules[index] = typed;
					Finish(Glossary.SaveRules(folder, rules), "Changed rule " + (index + 1) + ".");
				}
			}
		}


		private static void MoveUp() {
			Move("Move which rule up?", -1);
		}


		private static void MoveDown() {
			Move("Move which rule down?", 1);
		}


		private static void Move(string question, int step) {
			string folder = GlossariesMenu.SelectedFolder();
			List<string> rules = Glossary.Rules(folder);
			int index = PickIndex(rules, question);
			int target = index + step;
			if (index >= 0 && target >= 0 && target < rules.Count) {
				string moving = rules[index];
				rules[index] = rules[target];
				rules[target] = moving;
				Finish(Glossary.SaveRules(folder, rules), "Rule " + (index + 1) + " is now rule " + (target + 1) + ".");
			}
			if (index >= 0 && (target < 0 || target >= rules.Count)) {
				Finish("", "It is already at that end.");
			}
		}


		private static void Remove() {
			string folder = GlossariesMenu.SelectedFolder();
			List<string> rules = Glossary.Rules(folder);
			int index = PickIndex(rules, "Remove which rule?");
			if (index >= 0) {
				Console.WriteLine("Rule " + (index + 1) + ": " + rules[index]);
				bool yes = ConsoleExt.ReadValue<bool>("Remove it? (y/n): ", false);
				if (yes == true) {
					rules.RemoveAt(index);
					Finish(Glossary.SaveRules(folder, rules), "Removed.");
				}
			}
		}


		private static void Finish(string problem, string done) {
			if (problem.Length > 0) {
				Console.WriteLine(problem);
			}
			if (problem.Length == 0) {
				Console.WriteLine(done);
			}
			ConsoleExt.WaitForEnter("continue");
		}
	}
}
