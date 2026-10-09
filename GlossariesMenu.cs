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
				menu.AddChoice(new ConsoleMenuItem("Learning: the speaker tag, then the names, from this checkpoint's own files...").SetActionOnSelect(LearningMenu.Show));
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
					source = PickCheckpoint(sources, "Several checkpoints of this game have a glossary. Copy which one here?", "Do not copy");
				}
				if (source != null) {
					bool yes = YesNoMenu.Ask("\"" + source.Label + "\" is the same game and has a glossary. Copy it to \"" + checkpoint.Label + "\"?");
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
		private static Checkpoint? PickCheckpoint(List<Checkpoint> choices, string question, string backLabel = "Back") {
			Checkpoint? picked = null;
			List<string> rows = new();
			foreach (Checkpoint choice in choices) {
				rows.Add(choice.Label + "   " + choice.Path);
			}
			int index = PagedPicker.Pick(rows, question, backLabel);
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
						// Picking the target is the decision: the picker's title says its glossary
						// is replaced. A second question here defaulted to no and read as "did
						// not copy".
						string had = "";
						if (Glossary.Exists(targetFolder) == true) {
							had = " Its own " + Glossary.Characters(targetFolder).Count + " characters and " + Glossary.Rules(targetFolder).Count + " rules were replaced.";
						}
						string problem = Glossary.CopyOver(CheckpointInspector.FolderOf(selected.Path), targetFolder);
						Report(targetFolder, problem, "glossary replaced by a copy of \"" + selected.Label + "\"'s");
						if (problem.Length == 0) {
							Console.WriteLine("Copied to \"" + target.Label + "\"." + had);
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
	/// row shows that character in full. A character is a list of names and the one the
	/// translation writes; any save that changes names conforms the checkpoint's dialogue
	/// files through NametagConform, and the outcome is reported with the save.
	/// </summary>
	public static class CharactersMenu {

		/// <summary>What reading the glossary repaired while this menu was open, shown in its header.</summary>
		private static string repairs = "";


		public static void Show() {
			repairs = "";
			ConsoleSelectMenu menu = new(loops: true, numbered: false, clearOnRefresh: true);
			menu.AddOnDrawMenuAction(RefreshHeader);
			menu.AddChoice(new ConsoleMenuItem("List characters").SetActionOnSelect(List));
			menu.AddChoice(new ConsoleMenuItem("Add a character").SetActionOnSelect(Add));
			menu.AddChoice(new ConsoleMenuItem("Change a character").SetActionOnSelect(Change));
			menu.AddChoice(new ConsoleMenuItem("Remove a character").SetActionOnSelect(Remove));
			menu.AddChoice(new ConsoleMenuItem("Remove every character (asks first; there is no undo)").SetActionOnSelect(RemoveAll));
			string language = LlmClient.ToLanguage;
			if (language.Length == 0) {
				language = "target-language";
			}
			menu.AddChoice(new ConsoleMenuItem("Let the model pick each written name the " + language + " way, from the names it has...").SetActionOnSelect(CharacterLanguageMenu.PickWrittenNames));
			menu.AddChoice(new ConsoleMenuItem("Suggest a " + language + " name for each character, one at a time, yours to accept...").SetActionOnSelect(CharacterLanguageMenu.SuggestNames));
			menu.AddChoice(new ConsoleMenuItem("Back"));
			menu.GetChoice();
		}


		private static void RefreshHeader(ConsoleSelectMenu menu) {
			string folder = GlossariesMenu.SelectedFolder();
			int count = Glossary.Characters(folder).Count;
			string note = Glossary.TakeRepairNote(folder);
			if (note.Length > 0) {
				repairs += "Repaired on reading: " + note + ".\n";
			}
			menu.SetPreChoiceText("-- Characters (" + count + ") --\n" + repairs);
		}


		/// <summary>
		/// One row per character for the picker: the written name, then how many other
		/// names it has. The names themselves are in the full view, so the written one is
		/// never lost among them.
		/// </summary>
		/// <param name="entries">The characters, in the order to show.</param>
		/// <param name="lines">Lines spoken, by written name, from LineCounts.</param>
		private static List<string> Rows(List<CharacterEntry> entries, Dictionary<string, int> lines) {
			List<string> rows = new();
			foreach (CharacterEntry entry in entries) {
				int others = entry.Others().Count;
				string count = "no other name";
				if (others == 1) {
					count = "1 other name";
				}
				if (others > 1) {
					count = others + " other names";
				}
				string spoken = "";
				if (lines.ContainsKey(entry.Written) == true) {
					spoken = "   " + lines[entry.Written] + " line(s)";
				}
				rows.Add(entry.Written + ProvisionalMark(entry) + "   (" + count + ")" + spoken);
			}
			return rows;
		}


		/// <summary>
		/// How many dialogue lines each character speaks in the selected checkpoint, by
		/// written name, counting every tag that is one of the character's names. Empty when
		/// the checkpoint has no speaker tag learned, since the lines cannot be told apart.
		/// </summary>
		private static Dictionary<string, int> LineCounts(List<CharacterEntry> entries) {
			Dictionary<string, int> counts = new(StringComparer.OrdinalIgnoreCase);
			Checkpoint? checkpoint = CheckpointList.Selected();
			NametagConvention? convention = null;
			if (checkpoint != null) {
				convention = NametagConvention.For(checkpoint);
			}
			if (checkpoint != null && convention != null) {
				foreach (CharacterEntry entry in entries) {
					counts[entry.Written] = 0;
				}
				LearningMenu.Scan(checkpoint, convention, entries, out List<SpokenName> matched);
				foreach (SpokenName spoken in matched) {
					CharacterEntry? owner = Glossary.Find(entries, spoken.Name);
					if (owner != null) {
						counts[owner.Written] += spoken.Lines;
					}
				}
			}
			return counts;
		}


		/// <summary>
		/// " (provisional, from VNDB)" when no script has named this character yet, else empty.
		/// </summary>
		private static string ProvisionalMark(CharacterEntry entry) {
			string mark = "";
			if (entry.Provisional == true) {
				mark = " (provisional, from VNDB)";
			}
			return mark;
		}


		/// <summary>
		/// Which of a character's names the translation writes. One name needs no question;
		/// otherwise a paged pick, where Back keeps the current one.
		/// </summary>
		private static string PickWritten(CharacterEntry entry, string current) {
			string written = current;
			if (entry.Names.Count == 1) {
				written = entry.Names[0];
			}
			if (entry.Names.Count > 1) {
				int index = PagedPicker.Pick(entry.Names, "Which name does the translation write? Every speaker tag of this character is rewritten to it.");
				if (index >= 0) {
					written = entry.Names[index];
				}
			}
			return written;
		}


		/// <summary>
		/// One character's names: add one, respell one, remove one. Every action saves and
		/// conforms the files on the spot and reports what changed, so the menu above has
		/// nothing left to save when this returns.
		/// </summary>
		private static void NamesMenu(Checkpoint checkpoint, CharacterEntry entry) {
			ConsoleSelectMenu menu = new(loops: true, numbered: false, clearOnRefresh: true);
			menu.AddOnDrawMenuAction((shown) => {
				shown.SetPreChoiceText("-- Names of " + entry.Written + " --\n"
					+ "The translation writes: " + entry.Written + "\n"
					+ "Other names: " + string.Join(", ", entry.Others()) + "\n");
			});
			menu.AddChoice(new ConsoleMenuItem("Add a name (it can become the written one)...").SetActionOnSelect(() => { AddName(checkpoint, entry); }));
			menu.AddChoice(new ConsoleMenuItem("Respell a name (its speaker tags follow)...").SetActionOnSelect(() => { RespellName(checkpoint, entry); }));
			menu.AddChoice(new ConsoleMenuItem("Remove a name (not the written one)...").SetActionOnSelect(() => { RemoveName(checkpoint, entry); }));
			menu.AddChoice(new ConsoleMenuItem("Split a name off as its own character (not the written one)...").SetActionOnSelect(() => { SplitName(checkpoint, entry); }));
			menu.AddChoice(new ConsoleMenuItem("Back"));
			menu.GetChoice();
		}


		/// <summary>
		/// Takes one of the other names away from this character and makes it a character
		/// of its own, written as that name, for when a merge put two people under one
		/// entry. The glossary is corrected only: tags already rewritten by the earlier
		/// merge stay as they are, and the report says so.
		/// </summary>
		private static void SplitName(Checkpoint checkpoint, CharacterEntry entry) {
			List<string> others = entry.Others();
			if (others.Count == 0) {
				Finish("", entry.Written + " has no other name to split off.");
			}
			if (others.Count > 0) {
				int index = PagedPicker.Pick(others, "Split which name of " + entry.Written + " off as its own character?");
				if (index >= 0) {
					string name = others[index];
					entry.Names.Remove(name);
					string problem = NametagConform.SaveAndConform(checkpoint, entry, entry.Written, out string report);
					if (problem.Length > 0) {
						entry.Names.Add(name);
					}
					if (problem.Length == 0) {
						CharacterEntry made = new();
						made.Written = name;
						made.Add(name);
						problem = NametagConform.SaveAndConform(checkpoint, made, "", out string madeReport);
						if (problem.Length > 0) {
							entry.Names.Add(name);
							Glossary.SaveCharacter(CheckpointInspector.FolderOf(checkpoint.Path), entry, entry.Written);
						}
						if (problem.Length == 0) {
							CheckpointLog.Warning(CheckpointInspector.FolderOf(checkpoint.Path), "Glossary", name + " split off from " + entry.Written + " as its own character");
						}
					}
					Finish(problem, name + " is now its own character. Speaker tags the earlier merge rewrote to " + entry.Written + " stay as they are; a fresh split of the archive restores them.");
				}
			}
		}


		private static void AddName(Checkpoint checkpoint, CharacterEntry entry) {
			string name = ConsoleExt.ReadLine("Name to add (blank to cancel): ", -1, false).Trim();
			if (name.Length > 0 && entry.Has(name) == true) {
				Finish("", entry.Written + " already has the name " + name + ".");
			}
			if (name.Length > 0 && entry.Has(name) == false) {
				string replaces = entry.Written;
				entry.Add(name);
				bool make = YesNoMenu.Ask("Make " + name + " the name the translation writes for " + entry.Written + "?",
					"Every speaker tag of this character in the checkpoint would be rewritten to it.");
				bool wasProvisional = entry.Provisional;
				if (make == true) {
					entry.Written = name;
					entry.Provisional = false;
				}
				string problem = NametagConform.SaveAndConform(checkpoint, entry, replaces, out string report);
				if (problem.Length > 0) {
					entry.Names.Remove(name);
					entry.Written = replaces;
					entry.Provisional = wasProvisional;
				}
				Finish(problem, "Added " + name + ". " + report);
			}
		}


		/// <summary>
		/// Changes one name's spelling. Tags carrying the old spelling are rewritten to the
		/// written name like any other name of the character's, so a respelled written
		/// name carries its tags with it.
		/// </summary>
		private static void RespellName(Checkpoint checkpoint, CharacterEntry entry) {
			int index = PagedPicker.Pick(entry.Ordered(), "Respell which name of " + entry.Written + "?");
			if (index >= 0) {
				string old = entry.Ordered()[index];
				string typed = ConsoleExt.ReadLine("New spelling of " + old + " (blank to cancel): ", -1, false).Trim();
				if (typed.Length > 0 && string.Equals(typed, old, StringComparison.Ordinal) == false) {
					string replaces = entry.Written;
					List<string> names = new(entry.Names);
					entry.Names[entry.Names.IndexOf(old)] = typed;
					if (string.Equals(old, entry.Written, StringComparison.Ordinal) == true) {
						entry.Written = typed;
					}
					List<string> retired = new();
					retired.Add(old);
					string problem = NametagConform.SaveAndConform(checkpoint, entry, replaces, out string report, retired);
					if (problem.Length > 0) {
						entry.Names = names;
						entry.Written = replaces;
					}
					Finish(problem, old + " is now spelled " + typed + ". " + report);
				}
			}
		}


		private static void RemoveName(Checkpoint checkpoint, CharacterEntry entry) {
			List<string> others = entry.Others();
			if (others.Count == 0) {
				Finish("", entry.Written + " has no other name to remove. The written name stays; respell it or pick another written name instead.");
			}
			if (others.Count > 0) {
				int index = PagedPicker.Pick(others, "Remove which name of " + entry.Written + "? (the written name cannot be removed)");
				if (index >= 0) {
					string gone = others[index];
					entry.Names.Remove(gone);
					string problem = NametagConform.SaveAndConform(checkpoint, entry, entry.Written, out string report);
					if (problem.Length > 0) {
						entry.Names.Add(gone);
					}
					Finish(problem, "Removed the name " + gone + ". " + report);
				}
			}
		}


		/// <summary>
		/// A paged pick over the characters, or null for Back. The picker's sort row orders
		/// them by written name or by lines spoken; the order chosen is saved for next time.
		/// </summary>
		private static CharacterEntry? Pick(string question) {
			CharacterEntry? picked = null;
			List<CharacterEntry> entries = Glossary.Characters(GlossariesMenu.SelectedFolder());
			Dictionary<string, int> lines = LineCounts(entries);
			List<CharacterEntry> ordered = entries;
			Func<int, List<string>> rowsFor = (mode) => {
				ordered = CharacterSort.Order(entries, lines, mode);
				return Rows(ordered, lines);
			};
			int index = PagedPicker.Pick(CharacterSort.Modes, CharacterSort.Mode, rowsFor, question, out int pickedMode);
			CharacterSort.Mode = pickedMode;
			if (index >= 0) {
				picked = ordered[index];
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
			Checkpoint? checkpoint = CheckpointList.Selected();
			if (checkpoint != null) {
				CharacterEntry entry = new();
				entry.Names = CharacterEntry.SplitNames(ConsoleExt.ReadLine("Names, comma-separated, each as some script writes it (blank to cancel): ", -1, false));
				if (entry.Names.Count > 0) {
					entry.Written = PickWritten(entry, entry.Names[0]);
					entry.Role = ConsoleExt.ReadLine("Role, one line (blank for none): ", -1, false).Trim();
					entry.Notes = ConsoleExt.ReadLine("Notes, one line (blank for none): ", -1, false).Trim();
					entry.Profile = ReadProfile("");
					string problem = NametagConform.SaveAndConform(checkpoint, entry, "", out string report);
					Finish(problem, "Added " + entry.Written + ". " + report);
				}
			}
		}


		private static void Change() {
			Checkpoint? checkpoint = CheckpointList.Selected();
			CharacterEntry? entry = Pick("Change which character?");
			if (entry != null && checkpoint != null) {
				string replaces = entry.Written;
				ConsoleSelectMenu menu = new(loops: false, numbered: false, clearOnRefresh: true);
				menu.SetPreChoiceText(Describe(entry) + "\nChange what?");
				menu.AddChoice(new ConsoleMenuItem("Names: add, respell or remove one..."));
				menu.AddChoice(new ConsoleMenuItem("Which name the translation writes (rewrites this character's speaker tags)"));
				menu.AddChoice(new ConsoleMenuItem("Role"));
				menu.AddChoice(new ConsoleMenuItem("Notes"));
				menu.AddChoice(new ConsoleMenuItem("Profile"));
				menu.AddChoice(new ConsoleMenuItem("Cancel"));
				int field = menu.GetChoice();
				bool changed = true;
				if (field == 0) {
					NamesMenu(checkpoint, entry);
					changed = false;
				}
				if (field == 1) {
					entry.Written = PickWritten(entry, entry.Written);
					// Picked on purpose: no longer VNDB's placeholder, whichever name it is.
					entry.Provisional = false;
				}
				if (field == 2) {
					entry.Role = Ask("Role", entry.Role);
				}
				if (field == 3) {
					entry.Notes = Ask("Notes", entry.Notes);
				}
				if (field == 4) {
					entry.Profile = ReadProfile(entry.Profile);
				}
				if (field < 0 || field > 4) {
					changed = false;
				}
				if (changed == true) {
					string problem = NametagConform.SaveAndConform(checkpoint, entry, replaces, out string report);
					Finish(problem, "Changed " + entry.Written + ". " + report);
				}
			}
		}


		private static void Remove() {
			CharacterEntry? entry = Pick("Remove which character? Its file is deleted.");
			if (entry != null) {
				bool yes = YesNoMenu.Ask("Remove " + entry.Written + "?");
				if (yes == true) {
					Glossary.RemoveCharacter(GlossariesMenu.SelectedFolder(), entry.Written);
					Finish("", "Removed " + entry.Written + ".");
				}
			}
		}


		/// <summary>
		/// Deletes every character after one question that carries the count. Deliberate
		/// against the usual picking-is-the-decision rule: fifty entries have no undo.
		/// </summary>
		private static void RemoveAll() {
			string folder = GlossariesMenu.SelectedFolder();
			int count = Glossary.Characters(folder).Count;
			if (count == 0) {
				Finish("", "There are no characters to remove.");
			}
			if (count > 0) {
				bool yes = YesNoMenu.Ask("Delete all " + count + " character(s) of this glossary? There is no undo.");
				if (yes == true) {
					int removed = Glossary.RemoveAllCharacters(folder);
					CheckpointLog.Warning(folder, "Glossary", "removed every character (" + removed + ")");
					Finish("", "Removed " + removed + " character(s).");
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
			return entry.Written + ProvisionalMark(entry) + "\n  names: " + entry.NamesText + "\n  role: " + entry.Role + "\n  notes: " + entry.Notes + "\n  profile: " + entry.Profile.Replace("\r", "").Replace("\n", "\n           ");
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
			menu.AddChoice(new ConsoleMenuItem("Remove every rule (asks first; there is no undo)").SetActionOnSelect(RemoveAll));
			menu.AddChoice(new ConsoleMenuItem("Back"));
			menu.GetChoice();
		}


		/// <summary>
		/// Empties the rules after one question that carries the count.
		/// </summary>
		private static void RemoveAll() {
			string folder = GlossariesMenu.SelectedFolder();
			int count = Glossary.Rules(folder).Count;
			if (count == 0) {
				Finish("", "There are no rules to remove.");
			}
			if (count > 0) {
				bool yes = YesNoMenu.Ask("Delete all " + count + " rule(s) of this glossary? There is no undo.");
				if (yes == true) {
					string problem = Glossary.SaveRules(folder, new List<string>());
					if (problem.Length == 0) {
						CheckpointLog.Warning(folder, "Glossary", "removed every rule (" + count + ")");
					}
					Finish(problem, "Removed " + count + " rule(s).");
				}
			}
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
				bool yes = YesNoMenu.Ask("Remove it?");
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
