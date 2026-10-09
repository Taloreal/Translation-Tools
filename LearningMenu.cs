// File: LearningMenu.cs
// Namespace: TranslationTools
using System.Text;

using TALOREAL_NETCORE_API;

namespace TranslationTools {

	/// <summary>
	/// One name the dialogue files tag as a speaker: how often, where, and a few lines it
	/// speaks. Spellings that differ only in case are one name, shown by the commonest.
	/// </summary>
	public class SpokenName {

		/// <summary>The name as the tags most often spell it.</summary>
		public string Name = "";

		/// <summary>How many lines it speaks across the checkpoint.</summary>
		public int Lines = 0;

		/// <summary>The dialogue files it speaks in.</summary>
		public HashSet<string> Files = new(StringComparer.OrdinalIgnoreCase);

		/// <summary>Up to a few of its lines, bare, for showing who this is.</summary>
		public List<string> Samples = new();

		/// <summary>Each spelling seen and how often, to pick the commonest.</summary>
		public Dictionary<string, int> Spellings = new(StringComparer.Ordinal);
	}


	/// <summary>
	/// What follows a repeated line across the files: how many lines came after it, and
	/// how many of those opened a quotation. A line whose every follower opens one is a
	/// speaker's name, since nothing else in a script is always answered by speech.
	/// </summary>
	public class AfterLine {

		/// <summary>How many lines were seen after this one.</summary>
		public int Total = 0;

		/// <summary>How many of them opened with a quotation mark of any kind.</summary>
		public int Quoted = 0;


		/// <summary>True when at least one line followed and every one opened a quotation.</summary>
		public bool AllQuoted() {
			return Total > 0 && Quoted == Total;
		}
	}


	/// <summary>
	/// Glossaries → Learning: the two things a checkpoint learns from its own dialogue
	/// files before any pair is made, side by side. The speaker tag is learned from one
	/// file the user points at, confirmed once with the model and stored on the checkpoint
	/// (copyable to, takeable from, a checkpoint of the same game; forgettable). The names
	/// are learned by scanning every file through that tag: each name the glossary does
	/// not know is listed, and the user settles them one at a time - a new character, or
	/// another name of one the glossary has, with the model suggesting which. Everything
	/// acts on the selected checkpoint.
	/// </summary>
	public static class LearningMenu {

		/// <summary>How many of a name's lines are kept as samples.</summary>
		private const int SampleLines = 3;


		/// <summary>
		/// Shows the menu until the user chooses Back.
		/// </summary>
		public static void Show() {
			Checkpoint? checkpoint = CheckpointList.Selected();
			if (checkpoint == null) {
				Console.WriteLine("No checkpoint is selected.");
				ConsoleExt.WaitForEnter("continue");
			}
			if (checkpoint != null) {
				string folder = CheckpointInspector.FolderOf(checkpoint.Path);
				ConsoleSelectMenu menu = new(loops: true, numbered: false, clearOnRefresh: true);
				menu.AddOnDrawMenuAction((shown) => {
					CheckpointInfo info = CheckpointInfo.Load(folder);
					string current = "none; every line counts as untagged, and the names cannot be learned";
					NametagConvention? stored = NametagConvention.FromStored(info.SpeakerTag);
					if (stored != null) {
						current = stored.Opener + "Name" + stored.Closer;
					}
					if (TgdFeatures.Enabled == true) {
						current = NametagConvention.Tgd().Describe();
					}
					shown.SetPreChoiceText("-- Learning for " + checkpoint.Label + " --\n"
						+ "Speaker tag: " + current + "\n"
						+ "Characters in the glossary: " + Glossary.Characters(folder).Count + "\n");
				});
				menu.AddChoice(new ConsoleMenuItem("Fetch the cast from VNDB: names, roles and descriptions into the glossary (needs the game's VNDB id)...").SetActionOnSelect(() => { FetchCast(checkpoint, folder); }));
				menu.AddChoice(new ConsoleMenuItem("Learn the speaker tag from one of this checkpoint's dialogue files...").SetActionOnSelect(() => { TeachFromFile(checkpoint, folder); }));
				menu.AddChoice(new ConsoleMenuItem("Learn the names: scan every dialogue file for speakers the glossary does not know...").SetActionOnSelect(() => { LearnNames(checkpoint, folder); }));
				menu.AddChoice(new ConsoleMenuItem("Learn standalone name lines: a name on its own line above the text, found by repetition and the model...").SetActionOnSelect(() => { LearnLineNames(checkpoint, folder); }));
				menu.AddChoice(new ConsoleMenuItem("Settings: lines considered before stopping, stop share...").SetActionOnSelect(LearningSettingsMenu));
				menu.AddChoice(new ConsoleMenuItem("Copy the speaker tag to another checkpoint of the same game...").SetActionOnSelect(() => { CopyTagTo(checkpoint, folder); }));
				menu.AddChoice(new ConsoleMenuItem("Take the speaker tag from another checkpoint of the same game...").SetActionOnSelect(() => { TakeTagFrom(checkpoint, folder); }));
				menu.AddChoice(new ConsoleMenuItem("Forget the speaker tag: lines count as untagged again").SetActionOnSelect(() => { ForgetTag(folder); }));
				menu.AddChoice(new ConsoleMenuItem("Back"));
				menu.GetChoice();
			}
		}


		/// <summary>
		/// Pulls VNDB's cast into the glossary. VNDB's names are whole strings in no fixed
		/// shape, so the model is shown the whole cast once and asked, per character, for
		/// every form a script might use - given name, surname, full name either way round,
		/// with or without a space, in each language - the romanised given name first. A form
		/// two characters share, a family's surname, is dropped from both afterwards. Then a
		/// VNDB character that matches a glossary character by any form gets the missing
		/// forms, and a role or profile where those were empty; one matching nothing becomes
		/// a new character whose written name is provisional until a script tag joins it.
		/// A form another character holds is refused by the clash rule and reported, never
		/// forced. Without the model, VNDB's own strings are the forms.
		/// </summary>
		private static void FetchCast(Checkpoint checkpoint, string folder) {
			CheckpointInfo info = CheckpointInfo.Load(folder);
			if (info.VndbId.Length == 0) {
				Console.WriteLine("\"" + checkpoint.Label + "\" has no VNDB id recorded. Extract records one when the game is picked from VNDB's list.");
				ConsoleExt.WaitForEnter("continue");
			}
			if (info.VndbId.Length > 0) {
				Console.WriteLine("Asking VNDB for the characters of " + info.GameName + " (" + info.VndbId + ") ...");
				List<VndbCharacter> cast = VndbClient.Characters(info.VndbId, out string error);
				if (error.Length > 0) {
					Console.WriteLine(error);
				}
				if (error.Length == 0 && cast.Count == 0) {
					Console.WriteLine("VNDB lists no characters for this game.");
				}
				if (error.Length == 0 && cast.Count > 0) {
					Dictionary<string, List<string>> forms = WholeForms(cast);
					int answered = 0;
					int failed = 0;
					int failuresInARow = 0;
					bool asking = AlignmentHints.GaveUp == false;
					int at = 0;
					foreach (VndbCharacter character in cast) {
						at++;
						if (asking == true) {
							Console.WriteLine("Asking the model about character " + at + " of " + cast.Count + ": " + character.Name + " ...");
							System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
							string reply = LlmClient.Complete(FormsSystemText(), FormsUserText(character), out string modelError, 0, 0);
							clock.Stop();
							List<string> given = new();
							if (modelError.Length == 0) {
								given = ReadForms(reply, forms[character.Id]);
							}
							if (modelError.Length > 0 || given.Count == 0) {
								failed++;
								failuresInARow++;
								string why = modelError;
								if (modelError.Length == 0) {
									why = "no forms in the answer";
								}
								Console.WriteLine("  " + (clock.ElapsedMilliseconds / 1000) + " s, VNDB's own strings kept: " + why);
							}
							if (modelError.Length == 0 && given.Count > 0) {
								answered++;
								failuresInARow = 0;
								forms[character.Id] = given;
								Console.WriteLine("  " + (clock.ElapsedMilliseconds / 1000) + " s: " + string.Join(", ", given));
							}
							if (failuresInARow >= 2) {
								asking = false;
								Console.WriteLine("Two failed answers in a row; the model is not asked for the rest of the cast.");
							}
						}
					}
					string modelNote = "The model supplied forms for " + answered + " of " + cast.Count + " character(s); " + failed + " kept VNDB's own strings.";
					if (AlignmentHints.GaveUp == true) {
						modelNote = "The model is not being asked this session; VNDB's own strings are the forms.";
					}
					Console.WriteLine(modelNote);
					foreach (string note in DropShared(forms, cast)) {
						Console.WriteLine("  " + note);
					}
					int made = 0;
					int extended = 0;
					int refused = 0;
					foreach (VndbCharacter theirs in cast) {
						List<string> theirForms = forms[theirs.Id];
						List<CharacterEntry> known = Glossary.Characters(folder);
						CharacterEntry? mine = null;
						foreach (string name in theirForms) {
							if (mine == null) {
								mine = Glossary.Find(known, name);
							}
						}
						string replaces = "";
						List<string> added = new();
						if (mine != null) {
							replaces = mine.Written;
							foreach (string name in theirForms) {
								if (mine.Add(name) == true) {
									added.Add(name);
								}
							}
							if (mine.Role.Length == 0 && theirs.Role.Length > 0) {
								mine.Role = "VNDB role: " + theirs.Role;
							}
							if (mine.Profile.Length == 0) {
								mine.Profile = theirs.Description;
							}
						}
						if (mine == null) {
							mine = new CharacterEntry();
							mine.Written = theirForms[0];
							mine.Provisional = true;
							foreach (string name in theirForms) {
								mine.Add(name);
							}
							if (theirs.Role.Length > 0) {
								mine.Role = "VNDB role: " + theirs.Role;
							}
							mine.Profile = theirs.Description;
						}
						string problem = NametagConform.SaveAndConform(checkpoint, mine, replaces, out string report);
						if (problem.Length > 0) {
							refused++;
							Console.WriteLine("  " + theirs.Name + ": " + problem);
						}
						if (problem.Length == 0 && replaces.Length == 0) {
							made++;
							Console.WriteLine("  new, written name provisional until a script names them: " + mine.NamesText);
						}
						if (problem.Length == 0 && replaces.Length > 0) {
							extended++;
							string what = "nothing new";
							if (added.Count > 0) {
								what = "added " + string.Join(", ", added);
							}
							Console.WriteLine("  " + mine.Written + ": " + what);
						}
					}
					string summary = "VNDB cast: " + cast.Count + " listed, " + made + " new character(s), " + extended + " existing one(s) looked at, " + refused + " refused. " + modelNote;
					Console.WriteLine(summary);
					CheckpointLog.Warning(folder, "Glossary", summary);
				}
				ConsoleExt.WaitForEnter("continue");
			}
		}


		/// <summary>
		/// VNDB's own strings per character id: romanised name, original name, aliases.
		/// </summary>
		public static Dictionary<string, List<string>> WholeForms(List<VndbCharacter> cast) {
			Dictionary<string, List<string>> forms = new(StringComparer.OrdinalIgnoreCase);
			foreach (VndbCharacter character in cast) {
				forms[character.Id] = character.AllNames();
			}
			return forms;
		}


		private static string FormsSystemText() {
			return "You are given ONE character of a visual novel as the Visual Novel Database lists them: "
				+ "the romanised name, the name in its original script, and other names. Game scripts name a character in many forms: "
				+ "the given name alone, the surname alone, the full name in either order, with or without a space, in each language shown, and the nicknames listed. "
				+ "Reply with every form you can detect for this one person, comma-separated on one line, with the romanised given name FIRST. "
				+ "Use only the names given; never invent a name. Reply with that line and nothing else.";
		}


		private static string FormsUserText(VndbCharacter character) {
			StringBuilder text = new();
			text.Append("Romanised name: ").Append(character.Name).Append('\n');
			if (character.Original.Length > 0) {
				text.Append("Original name: ").Append(character.Original).Append('\n');
			}
			if (character.Aliases.Count > 0) {
				text.Append("Other names: ").Append(string.Join(", ", character.Aliases)).Append('\n');
			}
			return text.ToString();
		}


		/// <summary>
		/// The model's comma-separated forms for one character, in its order, then VNDB's
		/// own strings that it left out. Line breaks count as commas; a piece that reads as
		/// a sentence rather than a name, holding a full stop, is left out. Empty when the
		/// answer held no form at all.
		/// </summary>
		/// <param name="reply">The model's answer.</param>
		/// <param name="whole">VNDB's own strings for the character.</param>
		public static List<string> ReadForms(string reply, List<string> whole) {
			List<string> forms = new();
			string flat = reply.Replace("\r", "").Replace("\n", ",").Replace("*", "");
			foreach (string piece in CharacterEntry.SplitNames(flat)) {
				bool sentence = piece.Contains(". ") == true || piece.EndsWith(".") == true || piece.Length > 60;
				if (sentence == false && CharacterEntry.Contains(forms, piece) == false) {
					forms.Add(piece);
				}
			}
			if (forms.Count > 0) {
				foreach (string own in whole) {
					if (CharacterEntry.Contains(forms, own) == false) {
						forms.Add(own);
					}
				}
			}
			return forms;
		}


		/// <summary>
		/// Drops every form that more than one character holds, from all of them: a family's
		/// surname cannot name one of its members.
		/// </summary>
		/// <returns>One note per dropped form, naming who shared it.</returns>
		public static List<string> DropShared(Dictionary<string, List<string>> forms, List<VndbCharacter> cast) {
			List<string> notes = new();
			List<string> shared = new();
			foreach (VndbCharacter first in cast) {
				foreach (string form in forms[first.Id]) {
					int holders = 0;
					foreach (VndbCharacter other in cast) {
						if (CharacterEntry.Contains(forms[other.Id], form) == true) {
							holders++;
						}
					}
					if (holders > 1 && CharacterEntry.Contains(shared, form) == false) {
						shared.Add(form);
					}
				}
			}
			foreach (string form in shared) {
				List<string> who = new();
				foreach (VndbCharacter character in cast) {
					List<string> kept = new();
					foreach (string own in forms[character.Id]) {
						if (string.Equals(own, form, StringComparison.OrdinalIgnoreCase) == false) {
							kept.Add(own);
						}
					}
					if (kept.Count < forms[character.Id].Count) {
						who.Add(character.Name);
					}
					if (kept.Count > 0) {
						forms[character.Id] = kept;
					}
				}
				notes.Add("\"" + form + "\" is shared by " + string.Join(" and ", who) + "; dropped from all of them");
			}
			return notes;
		}


		/// <summary>
		/// The standalone learner's walk over the repeated lines, most frequent first, kept
		/// pure so it can be probed without a model: isName answers for one line, the lines
		/// considered and the names among them are counted, and the walk stops once the
		/// minimum has been considered and the share of names has fallen under the bar.
		/// </summary>
		/// <param name="byFrequency">The repeated lines with their counts, most frequent first.</param>
		/// <param name="isName">Says whether one line is a name.</param>
		/// <param name="minimum">How many lines are considered before the stop rule may end the walk.</param>
		/// <param name="stopPercent">The share of names, in percent, under which the walk stops.</param>
		/// <param name="considered">How many lines were looked at.</param>
		/// <param name="names">How many of those were names.</param>
		/// <param name="stoppedBecause">Why the walk ended, one phrase.</param>
		/// <returns>The lines judged names, in frequency order.</returns>
		public static List<KeyValuePair<string, int>> WalkRepeatedLines(List<KeyValuePair<string, int>> byFrequency, Func<string, bool> isName,
			int minimum, int stopPercent, out int considered, out int names, out string stoppedBecause) {
			considered = 0;
			names = 0;
			stoppedBecause = "the list ran out";
			List<KeyValuePair<string, int>> found = new();
			bool going = true;
			int at = 0;
			while (going == true && at < byFrequency.Count) {
				if (considered >= minimum && names * 100 < stopPercent * considered) {
					going = false;
					stoppedBecause = "after " + considered + " lines the share of names fell under " + stopPercent + "%";
				}
				if (going == true) {
					KeyValuePair<string, int> item = byFrequency[at];
					considered++;
					if (isName(item.Key) == true) {
						names++;
						found.Add(item);
					}
					at++;
				}
			}
			return found;
		}


		/// <summary>
		/// Says whether a repeated line is a character's name: the glossary answers without a
		/// call, the model answers the rest one line per call, and two failed calls in a row
		/// leave the model alone for the rest of the walk.
		/// </summary>
		private class LineJudge {

			public List<CharacterEntry> Cast = new();
			public Dictionary<string, AfterLine> After = new(StringComparer.Ordinal);
			public Dictionary<string, List<string>> Following = new(StringComparer.Ordinal);
			public int Known = 0;
			public int Asked = 0;
			public bool ModelGone = false;

			private int failuresInARow = 0;


			public bool IsName(string line) {
				bool name = false;
				bool known = Glossary.Find(Cast, line) != null;
				if (known == true) {
					Known++;
					name = true;
				}
				if (known == false && ModelGone == false) {
					Asked++;
					Console.WriteLine("Asking the model whether \"" + AlignmentLines.Preview(line, 60) + "\" is a name (" + Asked + ") ...");
					string reply = LlmClient.Complete(
						"You are given one line of text from a visual-novel script, as it was written, and sometimes what the script does after it. "
						+ "Reply YES if the line is nothing but a character's name - the name a game shows above the text box to say who is speaking - "
						+ "or NO if it is speech, narration, a sound, punctuation or anything else. One word on the first line.",
						Question(line), out string error, 0, 0);
					if (error.Length > 0) {
						failuresInARow++;
						if (failuresInARow >= 2) {
							ModelGone = true;
							Console.WriteLine("Two failed answers in a row; the model is not asked for the rest of the walk.");
						}
					}
					if (error.Length == 0) {
						failuresInARow = 0;
						name = reply.Trim().ToUpperInvariant().StartsWith("YES");
					}
				}
				return name;
			}


			/// <summary>
			/// The line for the model, with the evidence a reader would have: when every line
			/// after it opens a quotation, that is said, and one such line is shown.
			/// </summary>
			private string Question(string line) {
				string question = "Line: " + line;
				if (After.ContainsKey(line) == true && After[line].AllQuoted() == true) {
					question += "\nEvery one of the " + After[line].Total + " line(s) that follow it in the script opens a quotation";
					if (Following.ContainsKey(line) == true && Following[line].Count > 0) {
						question += ", for example: " + Following[line][0];
					}
					question += ".";
				}
				return question;
			}
		}


		/// <summary>
		/// The standalone name-line learner: counts every dialogue line byte for byte across
		/// the files, walks the repeated ones from the most frequent down with the glossary
		/// and the model saying which are names, stops where the names run out, and presents
		/// the ones the glossary lacks to settle one at a time. Once names were added, offers
		/// to make "a name on its own line" the checkpoint's speaker tag, and conforms the
		/// files so every name line carries its marker.
		/// </summary>
		private static void LearnLineNames(Checkpoint checkpoint, string folder) {
			Dictionary<string, int> counts = new(StringComparer.Ordinal);
			Dictionary<string, HashSet<string>> files = new(StringComparer.Ordinal);
			Dictionary<string, List<string>> following = new(StringComparer.Ordinal);
			Dictionary<string, AfterLine> followers = new(StringComparer.Ordinal);
			// Siglus wraps every text line in double quotes; those are the engine's, not a
			// quotation, and are set aside before a following line is read for one.
			bool quotedLines = CheckpointInspector.Inspect(checkpoint.Path).Engine == CheckpointEngine.Siglus;
			List<string> keys = AlignmentLines.DialogueKeys(checkpoint);
			keys.Sort(string.CompareOrdinal);
			Console.WriteLine("Counting every line of " + keys.Count + " dialogue file(s) ...");
			foreach (string key in keys) {
				string previous = "";
				foreach (string text in AlignmentLines.ReadTexts(AlignmentLines.DialoguePath(checkpoint, key))) {
					// A backtick ahead of a name (NScripter's text mode) and the quotes about it
					// (Siglus) are not the name: they are set aside here so the glossary can answer
					// and the model is asked about the name alone.
					string line = NametagConvention.LineName(text);
					if (line.Length > 0) {
						if (counts.ContainsKey(line) == false) {
							counts[line] = 0;
							files[line] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
							following[line] = new List<string>();
							followers[line] = new AfterLine();
						}
						counts[line]++;
						files[line].Add(key);
						if (previous.Length > 0 && following[previous].Count < SampleLines) {
							following[previous].Add(line);
						}
						if (previous.Length > 0) {
							followers[previous].Total++;
							if (OpensQuotation(text, quotedLines) == true) {
								followers[previous].Quoted++;
							}
						}
						previous = line;
					}
				}
			}
			List<KeyValuePair<string, int>> repeated = new();
			foreach (KeyValuePair<string, int> item in counts) {
				if (item.Value >= 2) {
					repeated.Add(item);
				}
			}
			repeated.Sort((first, second) => {
				int order = second.Value.CompareTo(first.Value);
				if (order == 0) {
					order = string.CompareOrdinal(first.Key, second.Key);
				}
				return order;
			});
			if (repeated.Count == 0) {
				Console.WriteLine("No line repeats anywhere in the files; there is nothing a standalone name could be.");
				ConsoleExt.WaitForEnter("continue");
			}
			if (repeated.Count > 0) {
				LineJudge judge = new();
				judge.Cast = Glossary.Characters(folder);
				judge.After = followers;
				judge.Following = following;
				judge.ModelGone = AlignmentHints.GaveUp;
				List<KeyValuePair<string, int>> found = WalkRepeatedLines(repeated, judge.IsName, LearningSettings.MinimumLines, LearningSettings.StopPercent,
					out int considered, out int names, out string why);
				Console.WriteLine(repeated.Count + " line(s) repeat; " + considered + " considered, " + names + " name(s) among them (" + judge.Known + " already in the glossary); stopped: " + why + ".");
				if (judge.ModelGone == true) {
					Console.WriteLine("The model could not be asked, so only glossary names counted; the list is not a verdict on the rest.");
				}
				int before = judge.Cast.Count;
				List<KeyValuePair<string, int>> candidates = new();
				int taken = 0;
				foreach (KeyValuePair<string, int> item in found) {
					if (Glossary.Find(judge.Cast, item.Key) == null) {
						// A name the model confirmed that every following line answers with a
						// quotation goes straight into the glossary under the script's own name:
						// nothing but a speaker is always followed by speech. The rest are settled by hand.
						bool quotedAfter = followers.ContainsKey(item.Key) == true && followers[item.Key].AllQuoted() == true;
						if (quotedAfter == true) {
							CharacterEntry entry = new();
							entry.Written = item.Key;
							entry.Add(item.Key);
							string problem = NametagConform.SaveAndConform(checkpoint, entry, "", out string report);
							string line = item.Key + ": added; every one of the " + followers[item.Key].Total + " line(s) after it opens a quotation (" + item.Value + " line(s)).";
							if (problem.Length > 0) {
								line = item.Key + ": " + problem;
								quotedAfter = false;
							}
							if (problem.Length == 0) {
								taken++;
							}
							Console.WriteLine("  " + line);
							CheckpointLog.Warning(folder, "Glossary", line);
						}
						if (quotedAfter == false) {
							candidates.Add(item);
						}
					}
				}
				if (taken > 0) {
					Console.WriteLine(taken + " name(s) went into the glossary on the quotations after them; " + candidates.Count + " left to settle.");
				}
				bool browsing = candidates.Count > 0;
				if (candidates.Count == 0) {
					ConsoleExt.WaitForEnter("continue");
				}
				while (browsing == true) {
					List<CharacterEntry> cast = Glossary.Characters(folder);
					List<string> rows = new();
					foreach (KeyValuePair<string, int> item in candidates) {
						rows.Add(item.Key + "   " + item.Value + " line(s) in " + files[item.Key].Count + " file(s)");
					}
					int picked = PagedPicker.Pick(rows, "Name lines the glossary does not know (" + candidates.Count + ") - pick one to settle it");
					if (picked < 0) {
						browsing = false;
					}
					if (picked >= 0) {
						SpokenName spoken = new();
						spoken.Name = candidates[picked].Key;
						spoken.Lines = candidates[picked].Value;
						spoken.Files = files[spoken.Name];
						spoken.Samples = following[spoken.Name];
						spoken.Spellings[spoken.Name] = spoken.Lines;
						SettleName(checkpoint, spoken, cast);
						if (Glossary.Find(Glossary.Characters(folder), spoken.Name) != null) {
							candidates.RemoveAt(picked);
						}
						if (candidates.Count == 0) {
							browsing = false;
						}
					}
				}
				List<CharacterEntry> after = Glossary.Characters(folder);
				CheckpointInfo info = CheckpointInfo.Load(folder);
				bool alreadyLine = string.Equals(info.SpeakerTag, NametagConvention.LineKind, StringComparison.Ordinal);
				if (after.Count > before && alreadyLine == false) {
					bool adopt = YesNoMenu.Ask("Read a name on its own line as " + checkpoint.Label + "'s speaker tag from here on?",
						"The line above a text line is its speaker when it is one of the glossary's names. Every such line gets " + NametagConvention.LineMarker.Trim() + " after it so a translator sees it.", true);
					if (adopt == true) {
						info.SpeakerTag = NametagConvention.LineKind;
						string problem = info.Save(folder);
						if (problem.Length > 0) {
							Console.WriteLine(problem);
						}
						if (problem.Length == 0) {
							MarkNameLines(checkpoint, folder, after);
						}
						ConsoleExt.WaitForEnter("continue");
					}
				}
				if (after.Count > before && alreadyLine == true) {
					MarkNameLines(checkpoint, folder, after);
					ConsoleExt.WaitForEnter("continue");
				}
			}
		}


		/// <summary>
		/// Conforms every character once, so each standalone name line carries the written
		/// name and its marker. The counts are summed and printed.
		/// </summary>
		private static void MarkNameLines(Checkpoint checkpoint, string folder, List<CharacterEntry> cast) {
			int marked = 0;
			foreach (CharacterEntry entry in cast) {
				string problem = NametagConform.SaveAndConform(checkpoint, entry, entry.Written, out string report);
				if (problem.Length > 0) {
					Console.WriteLine(entry.Written + ": " + problem);
				}
				if (problem.Length == 0 && report.StartsWith("Every") == false && report.StartsWith("No files") == false) {
					marked++;
				}
			}
			Console.WriteLine("Name lines conformed for " + cast.Count + " character(s); " + marked + " of them had lines to rewrite or mark.");
		}


		/// <summary>
		/// Whether a dialogue line's words open with a quotation mark of any kind, Japanese
		/// or Western. The marker and a leading backtick are set aside first; on Siglus the
		/// double quotes that wrap the whole line are the engine's and are set aside too.
		/// </summary>
		/// <param name="text">The line's text after the pointer.</param>
		/// <param name="quotedLines">True on Siglus, where every text line is a quoted literal.</param>
		public static bool OpensQuotation(string text, bool quotedLines) {
			string words = NametagConvention.StripMarker(text).Trim();
			if (words.StartsWith(NametagConvention.TextMode) == true) {
				words = words.Substring(1).Trim();
			}
			if (quotedLines == true) {
				words = NametagConvention.Unquote(words).Trim();
			}
			return words.Length > 0 && QuotationMarks.Contains(words[0]);
		}


		/// <summary>The marks a quotation may open with: Japanese corner brackets, Western doubles and singles, curly and straight, full-width and the guillemets.</summary>
		private const string QuotationMarks = "「『\"“‘'〝«‹＂＇";


		/// <summary>
		/// The two numbers the standalone learner stops on.
		/// </summary>
		private static void LearningSettingsMenu() {
			ConsoleMenuItem minimum = new("");
			ConsoleMenuItem percent = new("");
			ConsoleSelectMenu menu = new(loops: true, numbered: false, clearOnRefresh: true);
			menu.AddOnDrawMenuAction((shown) => {
				minimum.SetText("Lines considered before the stop rule may end the walk: " + LearningSettings.MinimumLines);
				percent.SetText("Stop once the share of names is under (percent): " + LearningSettings.StopPercent);
				shown.SetPreChoiceText("-- Learning settings --\n");
			});
			menu.AddChoice(minimum.SetActionOnSelect(() => {
				LlmMenu.SetWholeNumber("Lines considered before stopping", LearningSettings.MinimumLines, 1, LearningSettings.Most, (value) => { LearningSettings.MinimumLines = value; });
			}));
			menu.AddChoice(percent.SetActionOnSelect(() => {
				LlmMenu.SetWholeNumber("Stop share (percent)", LearningSettings.StopPercent, 1, 100, (value) => { LearningSettings.StopPercent = value; });
			}));
			menu.AddChoice(new ConsoleMenuItem("Back"));
			menu.GetChoice();
		}


		/// <summary>
		/// Every name the checkpoint's dialogue files tag as a speaker that the glossary does
		/// not already know under any of a character's names, most spoken first.
		/// </summary>
		/// <param name="checkpoint">The checkpoint whose split is scanned.</param>
		/// <param name="convention">How its files tag the speaker.</param>
		/// <param name="known">The glossary's characters.</param>
		/// <param name="matched">The names left out because a glossary character has them exactly, most spoken first.</param>
		public static List<SpokenName> Scan(Checkpoint checkpoint, NametagConvention convention, List<CharacterEntry> known, out List<SpokenName> matched) {
			Dictionary<string, SpokenName> byName = new(StringComparer.OrdinalIgnoreCase);
			List<string> keys = AlignmentLines.DialogueKeys(checkpoint);
			keys.Sort(string.CompareOrdinal);
			foreach (string key in keys) {
				foreach (AlignmentLine line in AlignmentLines.Read(AlignmentLines.DialoguePath(checkpoint, key), convention)) {
					if (line.HasNametag == true) {
						if (byName.ContainsKey(line.Name) == false) {
							byName[line.Name] = new SpokenName();
						}
						SpokenName spoken = byName[line.Name];
						spoken.Lines++;
						spoken.Files.Add(key);
						if (spoken.Samples.Count < SampleLines && line.Bare.Trim().Length > 0) {
							spoken.Samples.Add(line.Bare.Trim());
						}
						if (spoken.Spellings.ContainsKey(line.Name) == false) {
							spoken.Spellings[line.Name] = 0;
						}
						spoken.Spellings[line.Name]++;
					}
				}
			}
			matched = new List<SpokenName>();
			List<SpokenName> unknown = new();
			foreach (SpokenName spoken in byName.Values) {
				int best = 0;
				foreach (KeyValuePair<string, int> spelling in spoken.Spellings) {
					if (spelling.Value > best) {
						best = spelling.Value;
						spoken.Name = spelling.Key;
					}
				}
				if (Glossary.Find(known, spoken.Name) != null) {
					matched.Add(spoken);
				}
				if (Glossary.Find(known, spoken.Name) == null) {
					unknown.Add(spoken);
				}
			}
			unknown.Sort((first, second) => second.Lines.CompareTo(first.Lines));
			matched.Sort((first, second) => second.Lines.CompareTo(first.Lines));
			return unknown;
		}


		/// <summary>
		/// Whether a tag is made of Latin letters only. A Latin letter carries nothing on its
		/// own, so "Jo" inside "Josh" and "Jonathan" is an accident of spelling; a kana or
		/// kanji is a syllable or a word, so sharing a run of them is evidence. Containment
		/// is only tried where the units carry that information.
		/// </summary>
		public static bool IsRomanised(string tag) {
			bool romanised = tag.Trim().Length > 0;
			foreach (char letter in tag.Trim()) {
				bool latin = (letter >= 'a' && letter <= 'z') || (letter >= 'A' && letter <= 'Z');
				if (latin == false) {
					romanised = false;
				}
			}
			return romanised;
		}


		/// <summary>
		/// A name with every space, ASCII or ideographic, removed, for comparing shapes.
		/// </summary>
		public static string Shape(string name) {
			return name.Replace(" ", "").Replace("　", "").Trim();
		}


		/// <summary>
		/// The character a tag names by containment, when the match has no rival. Three
		/// tests: exactly one glossary character has a name that contains the tag or that
		/// the tag contains; that character's covering names are one shape once spaces are
		/// ignored, so spacing variants of one name are not rivals; and no other tag of the
		/// same scan contains the tag, since the script having a fuller form is evidence the
		/// short one is someone else. Romanised tags are never tried, see IsRomanised. VNDB
		/// is trusted otherwise: a short tag with one covering name and no rival is a match.
		/// </summary>
		/// <param name="tag">The scanned tag, not an exact glossary name.</param>
		/// <param name="cast">The glossary's characters.</param>
		/// <param name="allTags">Every tag the scan found, exact matches included.</param>
		/// <param name="why">Which test failed, or empty on a match.</param>
		/// <returns>The character, or null when any test fails.</returns>
		public static CharacterEntry? ContainmentMatch(string tag, List<CharacterEntry> cast, List<string> allTags, out string why) {
			why = "";
			CharacterEntry? match = null;
			string shape = Shape(tag);
			if (IsRomanised(tag) == true || shape.Length == 0) {
				why = "romanised tags are not matched by containment";
			}
			if (why.Length == 0) {
				// The script's own evidence first: a fuller tag in the same scan.
				foreach (string other in allTags) {
					string otherShape = Shape(other);
					bool fuller = string.Equals(otherShape, shape, StringComparison.OrdinalIgnoreCase) == false
						&& otherShape.IndexOf(shape, StringComparison.OrdinalIgnoreCase) >= 0;
					if (fuller == true && why.Length == 0) {
						why = "the script also tags " + other + ", a fuller form";
					}
				}
			}
			if (why.Length == 0) {
				List<CharacterEntry> covering = new();
				List<string> shapes = new();
				foreach (CharacterEntry entry in cast) {
					bool covers = false;
					foreach (string name in entry.Names) {
						string nameShape = Shape(name);
						bool related = nameShape.Length > 0
							&& (nameShape.IndexOf(shape, StringComparison.OrdinalIgnoreCase) >= 0 || shape.IndexOf(nameShape, StringComparison.OrdinalIgnoreCase) >= 0);
						if (related == true) {
							covers = true;
							if (CharacterEntry.Contains(shapes, nameShape) == false) {
								shapes.Add(nameShape);
							}
						}
					}
					if (covers == true) {
						covering.Add(entry);
					}
				}
				if (covering.Count == 0) {
					why = "no glossary name covers it";
				}
				if (covering.Count > 1) {
					why = covering.Count + " characters have a name covering it";
				}
				if (covering.Count == 1 && shapes.Count > 1) {
					why = covering[0].Written + " has " + shapes.Count + " different names covering it";
				}
				if (why.Length == 0) {
					match = covering[0];
				}
			}
			return match;
		}


		/// <summary>
		/// Whether a tag is a numbered extra: a non-romanised stem followed by one letter A
		/// to Z or one digit, half-width or full-width, as in 幹部A, 幹部Ｂ or 刑事1.
		/// </summary>
		/// <param name="tag">The scanned tag.</param>
		/// <param name="stem">The tag without its ending, trimmed.</param>
		/// <param name="ending">The ending as half-width upper case, so Ａ and A are one.</param>
		/// <param name="digit">True for a digit ending, false for a letter.</param>
		public static bool FamilyEnding(string tag, out string stem, out char ending, out bool digit) {
			stem = "";
			ending = ' ';
			digit = false;
			bool family = false;
			string trimmed = tag.Trim();
			if (trimmed.Length >= 2) {
				char last = trimmed[trimmed.Length - 1];
				bool letter = last >= 'A' && last <= 'Z';
				bool wideLetter = last >= 'Ａ' && last <= 'Ｚ';
				bool number = last >= '0' && last <= '9';
				bool wideNumber = last >= '０' && last <= '９';
				if (letter == true || wideLetter == true || number == true || wideNumber == true) {
					stem = trimmed.Substring(0, trimmed.Length - 1).Trim();
					ending = last;
					if (wideLetter == true) {
						ending = (char) ('A' + (last - 'Ａ'));
					}
					if (wideNumber == true) {
						ending = (char) ('0' + (last - '０'));
					}
					digit = number == true || wideNumber == true;
					family = stem.Length > 0 && IsRomanised(stem) == false;
				}
			}
			return family;
		}


		/// <summary>
		/// The numbered families among the unknown tags: tags sharing a stem and an ending
		/// kind, letters with letters or digits with digits, with at least two different
		/// endings. Each family is a list of members; a member is the tags that differ only
		/// in the width of the ending, most spoken first. A stem with one ending is no family.
		/// </summary>
		public static List<List<List<SpokenName>>> Families(List<SpokenName> unknown) {
			Dictionary<string, Dictionary<char, List<SpokenName>>> byStem = new(StringComparer.OrdinalIgnoreCase);
			foreach (SpokenName spoken in unknown) {
				if (FamilyEnding(spoken.Name, out string stem, out char ending, out bool digit) == true) {
					string key = stem + "|" + (digit ? "digit" : "letter");
					if (byStem.ContainsKey(key) == false) {
						byStem[key] = new Dictionary<char, List<SpokenName>>();
					}
					if (byStem[key].ContainsKey(ending) == false) {
						byStem[key][ending] = new List<SpokenName>();
					}
					byStem[key][ending].Add(spoken);
				}
			}
			List<List<List<SpokenName>>> families = new();
			foreach (Dictionary<char, List<SpokenName>> endings in byStem.Values) {
				if (endings.Count >= 2) {
					List<char> order = new(endings.Keys);
					order.Sort();
					List<List<SpokenName>> family = new();
					foreach (char ending in order) {
						List<SpokenName> member = endings[ending];
						member.Sort((first, second) => second.Lines.CompareTo(first.Lines));
						family.Add(member);
					}
					families.Add(family);
				}
			}
			return families;
		}


		/// <summary>
		/// Settles what needs no pick: an exact match names a provisional character with the
		/// script's own tag; a numbered family, 幹部A and 幹部B, becomes one new character per
		/// member, since a script never means one person by two numbers; and a containment
		/// match without rival joins its character, the same way when provisional. Each is
		/// saved, conformed, printed and logged. Exact matches go first, most spoken first,
		/// so the written name is the script's commonest form; families next; containment
		/// last, with family tags left out of the scan's tags so they never count as a
		/// fuller form of their own stem.
		/// </summary>
		/// <returns>The tags still unknown afterwards.</returns>
		private static List<SpokenName> AutoAccept(Checkpoint checkpoint, string folder, List<SpokenName> matched, List<SpokenName> unknown) {
			List<CharacterEntry> cast = Glossary.Characters(folder);
			List<SpokenName> familyTags = new();
			foreach (List<List<SpokenName>> family in Families(unknown)) {
				FamilyEnding(family[0][0].Name, out string stem, out char firstEnding, out bool digit);
				List<string> endings = new();
				int made = 0;
				foreach (List<SpokenName> member in family) {
					CharacterEntry entry = new();
					entry.Written = member[0].Name;
					foreach (SpokenName spelling in member) {
						entry.Add(spelling.Name);
						familyTags.Add(spelling);
					}
					entry.Role = "unnamed role: " + stem;
					string problem = NametagConform.SaveAndConform(checkpoint, entry, "", out string report);
					FamilyEnding(member[0].Name, out string memberStem, out char ending, out bool memberDigit);
					if (problem.Length > 0) {
						Console.WriteLine("  " + member[0].Name + ": " + problem);
					}
					if (problem.Length == 0) {
						made++;
						endings.Add(ending.ToString());
					}
				}
				string line = "family " + stem + ": " + string.Join(", ", endings) + ", made " + made + " character(s)";
				Console.WriteLine("  " + line);
				CheckpointLog.Warning(folder, "Glossary", line);
			}
			foreach (SpokenName spoken in familyTags) {
				unknown.Remove(spoken);
			}
			List<string> allTags = new();
			foreach (SpokenName spoken in matched) {
				allTags.Add(spoken.Name);
			}
			foreach (SpokenName spoken in unknown) {
				allTags.Add(spoken.Name);
			}
			cast = Glossary.Characters(folder);
			foreach (SpokenName spoken in matched) {
				CharacterEntry? entry = Glossary.Find(cast, spoken.Name);
				if (entry != null && entry.Provisional == true) {
					string replaces = entry.Written;
					entry.Written = spoken.Name;
					entry.Provisional = false;
					string problem = NametagConform.SaveAndConform(checkpoint, entry, replaces, out string report);
					string line = replaces + ": the script's " + spoken.Name + " is now the written name (" + spoken.Lines + " line(s)). " + report;
					if (problem.Length > 0) {
						line = replaces + ": " + problem;
					}
					Console.WriteLine("  " + line);
					CheckpointLog.Warning(folder, "Glossary", line);
					cast = Glossary.Characters(folder);
				}
			}
			List<SpokenName> left = new();
			foreach (SpokenName spoken in unknown) {
				CharacterEntry? entry = ContainmentMatch(spoken.Name, cast, allTags, out string why);
				if (entry == null) {
					left.Add(spoken);
				}
				if (entry != null) {
					string replaces = entry.Written;
					entry.Add(spoken.Name);
					string written = "";
					if (entry.Provisional == true) {
						entry.Written = spoken.Name;
						entry.Provisional = false;
						written = ", and it is now the written name";
					}
					string problem = NametagConform.SaveAndConform(checkpoint, entry, replaces, out string report);
					string line = replaces + ": the script's " + spoken.Name + " joins as a name, covered by " + replaces + "'s names alone (" + spoken.Lines + " line(s))" + written + ". " + report;
					if (problem.Length > 0) {
						line = replaces + ": " + problem;
						left.Add(spoken);
					}
					Console.WriteLine("  " + line);
					CheckpointLog.Warning(folder, "Glossary", line);
					cast = Glossary.Characters(folder);
				}
			}
			return left;
		}


		/// <summary>
		/// Asks the model which glossary character, if any, a tag belongs to.
		/// </summary>
		/// <param name="reason">The model's reason, or why it gave nothing.</param>
		/// <returns>The written name of the suggested character, or empty.</returns>
		public static string Suggest(string name, List<CharacterEntry> cast, out string reason) {
			string suggestion = "";
			reason = "";
			StringBuilder list = new();
			foreach (CharacterEntry entry in cast) {
				list.Append("Written: ").Append(entry.Written).Append(" - names: ").Append(entry.NamesText).Append('\n');
			}
			Console.WriteLine("Asking the model whether " + name + " is a character the glossary already has ...");
			string reply = LlmClient.Complete(
				"You are given one speaker tag from a visual-novel script, and the characters already in its glossary, each with every name they are known by, possibly in several languages. "
				+ "Reply on the first line with the written name of the character the tag belongs to, exactly as listed after \"Written:\", when the tag is a translation, transliteration, romanization, nickname or spelling variant of one of that character's names. "
				+ "Reply NONE when the tag is someone the glossary does not have. Then one short sentence of reason on the second line.",
				"Tag: " + name + "\n\nCharacters:\n" + list.ToString(), out string error, 0, 0);
			if (error.Length > 0) {
				reason = "the model could not be asked: " + error;
			}
			if (error.Length == 0) {
				string[] lines = reply.Replace("\r", "").Trim().Split('\n');
				string first = lines[0].Trim().Replace("*", "").Trim();
				if (lines.Length > 1) {
					reason = lines[1].Trim();
				}
				foreach (CharacterEntry entry in cast) {
					if (suggestion.Length == 0 && string.Equals(entry.Written, first, StringComparison.OrdinalIgnoreCase)) {
						suggestion = entry.Written;
					}
				}
				if (suggestion.Length == 0 && first.ToUpperInvariant().StartsWith("NONE") == false) {
					reason = "the model's answer was not understood: " + AlignmentLines.Preview(reply.Trim(), 120);
				}
				if (suggestion.Length == 0 && first.ToUpperInvariant().StartsWith("NONE") == true) {
					reason = "the model says this is someone new: " + reason;
				}
			}
			return suggestion;
		}


		/// <summary>
		/// The names pass: scans, lists what the glossary lacks, and settles each picked
		/// name through SettleName until the user goes back or nothing is left.
		/// </summary>
		private static void LearnNames(Checkpoint checkpoint, string folder) {
			NametagConvention? convention = NametagConvention.For(checkpoint);
			if (convention == null) {
				Console.WriteLine("\"" + checkpoint.Label + "\" has no speaker tag yet, so its names cannot be found. Learn the speaker tag first.");
				ConsoleExt.WaitForEnter("continue");
			}
			bool browsing = convention != null;
			while (browsing == true) {
				List<CharacterEntry> cast = Glossary.Characters(folder);
				Console.WriteLine("Scanning every dialogue file of " + checkpoint.Label + " for speakers ...");
				List<SpokenName> unknown = Scan(checkpoint, convention!, cast, out List<SpokenName> matched);
				List<SpokenName> names = AutoAccept(checkpoint, folder, matched, unknown);
				cast = Glossary.Characters(folder);
				if (names.Count == 0) {
					Console.WriteLine("Every speaker in the files is in the glossary (" + matched.Count + " name(s) matched exactly, " + (unknown.Count - names.Count) + " settled as families or by containment).");
					ConsoleExt.WaitForEnter("continue");
					browsing = false;
				}
				if (names.Count > 0) {
					List<string> rows = new();
					foreach (SpokenName spoken in names) {
						rows.Add(spoken.Name + "   " + spoken.Lines + " line(s) in " + spoken.Files.Count + " file(s)");
					}
					int picked = PagedPicker.Pick(rows, "Speakers the glossary does not know (" + names.Count + "; " + matched.Count + " matched exactly, " + (unknown.Count - names.Count) + " settled as families or by containment) - pick one to settle it");
					if (picked < 0) {
						browsing = false;
					}
					if (picked >= 0) {
						SettleName(checkpoint, names[picked], cast);
					}
				}
			}
		}


		/// <summary>
		/// One name's decision: new character, another name of an existing one (the model's
		/// suggestion first when it made one), or skip. A name joining a character is offered
		/// as its written name, and the files are conformed to the outcome.
		/// </summary>
		private static void SettleName(Checkpoint checkpoint, SpokenName spoken, List<CharacterEntry> cast) {
			StringBuilder facts = new();
			facts.Append("-- ").Append(spoken.Name).Append(" --\n");
			facts.Append(spoken.Lines).Append(" line(s) in ").Append(spoken.Files.Count).Append(" file(s)");
			if (spoken.Spellings.Count > 1) {
				facts.Append("; spelled ").Append(string.Join(", ", spoken.Spellings.Keys));
			}
			facts.Append('\n');
			foreach (string sample in spoken.Samples) {
				facts.Append("  ").Append(AlignmentLines.Preview(sample, Math.Max(20, Console.WindowWidth - 6))).Append('\n');
			}
			string suggestion = "";
			string reason = "";
			if (cast.Count > 0 && AlignmentHints.GaveUp == false) {
				suggestion = Suggest(spoken.Name, cast, out reason);
			}
			if (cast.Count > 0 && AlignmentHints.GaveUp == true) {
				reason = "the model is not being asked any more this session";
			}
			if (suggestion.Length > 0) {
				facts.Append("The model suggests this is ").Append(suggestion).Append(": ").Append(reason).Append('\n');
			}
			if (suggestion.Length == 0 && reason.Length > 0) {
				facts.Append("No suggestion from the model (").Append(reason).Append(")\n");
			}
			ConsoleSelectMenu menu = new(loops: false, numbered: false, clearOnRefresh: true);
			menu.SetPreChoiceText(facts.ToString());
			menu.AddChoice(new ConsoleMenuItem("New character: " + spoken.Name));
			if (suggestion.Length > 0) {
				menu.AddChoice(new ConsoleMenuItem("Add to " + suggestion + " as another name of theirs (the model's suggestion)"));
			}
			menu.AddChoice(new ConsoleMenuItem("Add to another character..."));
			menu.AddChoice(new ConsoleMenuItem("Skip"));
			int choice = menu.GetChoice();
			int addToOther = 1;
			if (suggestion.Length > 0) {
				addToOther = 2;
			}
			if (choice == 0) {
				CharacterEntry entry = new();
				entry.Written = spoken.Name;
				entry.Add(spoken.Name);
				string problem = NametagConform.SaveAndConform(checkpoint, entry, "", out string report);
				Finish(problem, "Added " + spoken.Name + ". " + report);
			}
			if (choice == 1 && suggestion.Length > 0) {
				JoinCharacter(checkpoint, spoken.Name, Glossary.Find(cast, suggestion));
			}
			if (choice == addToOther) {
				List<string> rows = new();
				foreach (CharacterEntry entry in cast) {
					rows.Add(entry.Written + "   (" + entry.Others().Count + " other name(s))");
				}
				int picked = PagedPicker.Pick(rows, "Add " + spoken.Name + " to which character?");
				if (picked >= 0) {
					JoinCharacter(checkpoint, spoken.Name, cast[picked]);
				}
			}
		}


		/// <summary>
		/// Adds a name to a character, offers it as the written name, saves and conforms.
		/// </summary>
		private static void JoinCharacter(Checkpoint checkpoint, string name, CharacterEntry? entry) {
			if (entry != null) {
				string replaces = entry.Written;
				entry.Add(name);
				bool make = entry.Provisional;
				if (entry.Provisional == true) {
					Console.WriteLine(entry.Written + " was provisional, from VNDB; the script's " + name + " becomes the written name.");
				}
				if (entry.Provisional == false) {
					make = YesNoMenu.Ask("Make " + name + " the name the translation writes for " + entry.Written + "?",
						"Every speaker tag of this character in " + checkpoint.Label + " would be rewritten to it.");
				}
				if (make == true) {
					entry.Written = name;
					entry.Provisional = false;
				}
				string problem = NametagConform.SaveAndConform(checkpoint, entry, replaces, out string report);
				Finish(problem, name + " is now a name of " + entry.Written + ". " + report);
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


		/// <summary>
		/// Picks a dialogue file, learns the tag from it, asks the model once, and stores
		/// the rule after the user's yes.
		/// </summary>
		private static void TeachFromFile(Checkpoint checkpoint, string folder) {
			List<string> keys = AlignmentLines.DialogueKeys(checkpoint);
			keys.Sort(string.CompareOrdinal);
			if (keys.Count == 0) {
				Console.WriteLine("\"" + checkpoint.Label + "\" has no dialogue files; split it first.");
				ConsoleExt.WaitForEnter("continue");
			}
			int picked = -1;
			if (keys.Count > 0) {
				picked = PagedPicker.Pick(keys, "Learn the speaker tag from which file of " + checkpoint.Label + "? (" + keys.Count + ")");
			}
			if (picked >= 0) {
				string path = AlignmentLines.DialoguePath(checkpoint, keys[picked]);
				NametagConvention? learned = NametagConvention.Learn(AlignmentLines.ReadTexts(path), Glossary.Characters(folder));
				if (learned == null) {
					Console.WriteLine("No speaker tag could be learned from " + keys[picked] + ". Without a glossary that needs ten tagged lines with four names each appearing twice; with one, five lines naming two known characters. Try a longer file, or fill the glossary first.");
					ConsoleExt.WaitForEnter("continue");
				}
				if (learned != null) {
					if (AlignmentHints.GaveUp == false) {
						learned.ConfirmWithModel(checkpoint.Label);
					}
					string names = "";
					int shown = 0;
					foreach (string name in learned.Names) {
						if (shown < 10) {
							if (names.Length > 0) {
								names += ", ";
							}
							names += name;
							shown++;
						}
					}
					bool adopt = YesNoMenu.Ask("Use " + learned.Opener + "Name" + learned.Closer + " as " + checkpoint.Label + "'s speaker tag?",
						"Learned from " + keys[picked] + ": " + learned.Describe() + "\nNames seen: " + names, true);
					if (adopt == true) {
						CheckpointInfo info = CheckpointInfo.Load(folder);
						info.SpeakerTag = learned.Stored;
						string problem = info.Save(folder);
						if (problem.Length > 0) {
							Console.WriteLine(problem);
						}
						if (problem.Length == 0) {
							Console.WriteLine("Stored. The walk and the names pass now read " + learned.Opener + "Name" + learned.Closer + " as the speaker on " + checkpoint.Label + ".");
						}
						ConsoleExt.WaitForEnter("continue");
					}
				}
			}
		}


		/// <summary>
		/// Writes this checkpoint's speaker tag into another checkpoint of the same game.
		/// </summary>
		private static void CopyTagTo(Checkpoint source, string sourceFolder) {
			CheckpointInfo info = CheckpointInfo.Load(sourceFolder);
			if (info.SpeakerTag.Length == 0) {
				Console.WriteLine("\"" + source.Label + "\" has no speaker tag to copy. Learn one first.");
				ConsoleExt.WaitForEnter("continue");
			}
			if (info.SpeakerTag.Length > 0) {
				List<Checkpoint> targets = SameGame(source, info.GameName, false);
				Checkpoint? target = AlignmentOperation.PickCheckpoint(targets, null, "Copy " + info.SpeakerTag[0] + "Name" + info.SpeakerTag[1] + " to which checkpoint of " + info.GameName + "?");
				if (target != null) {
					string targetFolder = CheckpointInspector.FolderOf(target.Path);
					CheckpointInfo targetInfo = CheckpointInfo.Load(targetFolder);
					targetInfo.SpeakerTag = info.SpeakerTag;
					string problem = targetInfo.Save(targetFolder);
					if (problem.Length > 0) {
						Console.WriteLine(problem);
					}
					if (problem.Length == 0) {
						Console.WriteLine("\"" + target.Label + "\" now reads " + info.SpeakerTag[0] + "Name" + info.SpeakerTag[1] + " as its speaker tag.");
					}
					ConsoleExt.WaitForEnter("continue");
				}
			}
		}


		/// <summary>
		/// Takes the speaker tag of another checkpoint of the same game that has one.
		/// </summary>
		private static void TakeTagFrom(Checkpoint target, string targetFolder) {
			CheckpointInfo info = CheckpointInfo.Load(targetFolder);
			List<Checkpoint> sources = SameGame(target, info.GameName, true);
			if (sources.Count == 0) {
				Console.WriteLine("No other checkpoint of " + info.GameName + " has a speaker tag.");
				ConsoleExt.WaitForEnter("continue");
			}
			if (sources.Count > 0) {
				Checkpoint? source = AlignmentOperation.PickCheckpoint(sources, null, "Take the speaker tag from which checkpoint of " + info.GameName + "?");
				if (source != null) {
					CheckpointInfo sourceInfo = CheckpointInfo.Load(CheckpointInspector.FolderOf(source.Path));
					info.SpeakerTag = sourceInfo.SpeakerTag;
					string problem = info.Save(targetFolder);
					if (problem.Length > 0) {
						Console.WriteLine(problem);
					}
					if (problem.Length == 0) {
						Console.WriteLine("\"" + target.Label + "\" now reads " + info.SpeakerTag[0] + "Name" + info.SpeakerTag[1] + " as its speaker tag.");
					}
					ConsoleExt.WaitForEnter("continue");
				}
			}
		}


		/// <summary>
		/// The other checkpoints whose info names the same game, optionally only those that
		/// have a speaker tag. A checkpoint with no game name matches nothing.
		/// </summary>
		private static List<Checkpoint> SameGame(Checkpoint except, string gameName, bool withTagOnly) {
			List<Checkpoint> same = new();
			if (gameName.Length > 0) {
				foreach (Checkpoint checkpoint in CheckpointList.All()) {
					if (checkpoint.Serial != except.Serial) {
						CheckpointInfo info = CheckpointInfo.Load(CheckpointInspector.FolderOf(checkpoint.Path));
						bool sameGame = string.Equals(info.GameName, gameName, StringComparison.OrdinalIgnoreCase);
						bool hasTag = info.SpeakerTag.Length == 2;
						if (sameGame == true && (withTagOnly == false || hasTag == true)) {
							same.Add(checkpoint);
						}
					}
				}
			}
			return same;
		}


		private static void ForgetTag(string folder) {
			CheckpointInfo info = CheckpointInfo.Load(folder);
			info.SpeakerTag = "";
			string problem = info.Save(folder);
			if (problem.Length > 0) {
				Console.WriteLine(problem);
			}
			if (problem.Length == 0) {
				Console.WriteLine("Forgotten. Every line of this checkpoint counts as untagged in the walk, and the names cannot be learned until a tag is.");
			}
			ConsoleExt.WaitForEnter("continue");
		}
	}
}
