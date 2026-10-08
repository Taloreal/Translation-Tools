// File: CharacterLanguageMenu.cs
// Namespace: TranslationTools
using TALOREAL_NETCORE_API;

namespace TranslationTools {

	/// <summary>
	/// The two Characters rows that use the model's target language: picking each
	/// character's written name the way that language displays a person, from the names
	/// the glossary already holds, and suggesting a target-language name, one character at
	/// a time, for the user to accept. The model sees names only, never notes or profiles.
	/// Both refuse when "Translate to" is not set on the Language model settings.
	/// </summary>
	public static class CharacterLanguageMenu {

		/// <summary>
		/// For every character, asks the model which of its names is the standard display
		/// form for readers of the target language and makes that the written name. The
		/// model only chooses among the glossary's names; an answer that is none of them
		/// changes nothing. Two failures in a row stop the asking.
		/// </summary>
		public static void PickWrittenNames() {
			Checkpoint? checkpoint = CheckpointList.Selected();
			string language = LlmClient.ToLanguage;
			if (checkpoint != null && language.Length == 0) {
				Finish("\"Translate to\" is not set. Set it under Settings -> Language model first.");
			}
			if (checkpoint != null && language.Length > 0) {
				string folder = CheckpointInspector.FolderOf(checkpoint.Path);
				List<CharacterEntry> cast = Glossary.Characters(folder);
				int changed = 0;
				int kept = 0;
				int unanswered = 0;
				int failuresInARow = 0;
				bool asking = true;
				int at = 0;
				foreach (CharacterEntry entry in cast) {
					at++;
					if (asking == true) {
						Console.WriteLine("Asking the model about " + at + " of " + cast.Count + ": " + entry.NamesText + " ...");
						string reply = LlmClient.Complete(PickSystemText(language), "Names: " + entry.NamesText, out string error, 0, 0);
						string picked = "";
						string reason = "";
						if (error.Length == 0) {
							picked = MatchName(reply, entry, out reason);
						}
						if (error.Length > 0 || picked.Length == 0) {
							unanswered++;
							failuresInARow++;
							string why = error;
							if (error.Length == 0) {
								why = "not one of the names: " + reason;
							}
							Console.WriteLine("  unchanged (" + why + ")");
						}
						if (error.Length == 0 && picked.Length > 0) {
							failuresInARow = 0;
							if (string.Equals(picked, entry.Written, StringComparison.Ordinal) == true && entry.Provisional == false) {
								kept++;
								Console.WriteLine("  keeps " + entry.Written + ": " + reason);
							}
							if (string.Equals(picked, entry.Written, StringComparison.Ordinal) == false || entry.Provisional == true) {
								string replaces = entry.Written;
								entry.Written = picked;
								entry.Provisional = false;
								string problem = NametagConform.SaveAndConform(checkpoint, entry, replaces, out string report);
								if (problem.Length > 0) {
									unanswered++;
									Console.WriteLine("  " + problem);
								}
								if (problem.Length == 0) {
									changed++;
									string line = replaces + " now writes " + picked + ": " + reason + " " + report;
									Console.WriteLine("  " + line);
									CheckpointLog.Warning(folder, "Glossary", line);
								}
							}
						}
						if (failuresInARow >= 2) {
							asking = false;
							Console.WriteLine("Two failed answers in a row; the model is not asked for the rest.");
						}
					}
				}
				string summary = "Written names the " + language + " way: " + changed + " changed, " + kept + " kept, " + unanswered + " unchanged for want of an answer, of " + cast.Count + ".";
				CheckpointLog.Warning(folder, "Glossary", summary);
				Finish(summary);
			}
		}


		/// <summary>
		/// One character at a time: asks first whether one of the names is already fit for
		/// readers of the target language, and only when none is, asks for the name a
		/// translation would use. Each suggestion is the user's to accept: add it as the
		/// written name, add it as another name, skip, or stop the pass.
		/// </summary>
		public static void SuggestNames() {
			Checkpoint? checkpoint = CheckpointList.Selected();
			string language = LlmClient.ToLanguage;
			if (checkpoint != null && language.Length == 0) {
				Finish("\"Translate to\" is not set. Set it under Settings -> Language model first.");
			}
			if (checkpoint != null && language.Length > 0) {
				string folder = CheckpointInspector.FolderOf(checkpoint.Path);
				List<CharacterEntry> cast = Glossary.Characters(folder);
				int added = 0;
				int fit = 0;
				int skipped = 0;
				int failed = 0;
				int failuresInARow = 0;
				bool going = true;
				int at = 0;
				foreach (CharacterEntry entry in cast) {
					at++;
					if (going == true) {
						Console.WriteLine("Asking the model whether " + entry.NamesText + " already has a " + language + " form (" + at + " of " + cast.Count + ") ...");
						string reply = LlmClient.Complete(FitSystemText(language), "Names: " + entry.NamesText, out string error, 0, 0);
						string already = "";
						string reason = "";
						bool none = false;
						if (error.Length == 0) {
							already = MatchName(reply, entry, out reason);
							none = already.Length == 0 && reason.ToUpperInvariant().StartsWith("NONE") == true;
							if (already.Length == 0 && none == false) {
								string first = reply.Replace("\r", "").Trim().Split('\n')[0].Trim().ToUpperInvariant();
								none = first.StartsWith("NONE") == true;
							}
						}
						if (error.Length > 0 || (already.Length == 0 && none == false)) {
							failed++;
							failuresInARow++;
							string why = error;
							if (error.Length == 0) {
								why = "the answer was not understood: " + AlignmentLines.Preview(reply.Trim(), 120);
							}
							Console.WriteLine("  skipped (" + why + ")");
						}
						if (error.Length == 0 && already.Length > 0) {
							failuresInARow = 0;
							fit++;
							Console.WriteLine("  " + already + " already fits: " + reason);
						}
						if (error.Length == 0 && already.Length == 0 && none == true) {
							failuresInARow = 0;
							Console.WriteLine("Asking the model for a " + language + " name ...");
							string suggestion = LlmClient.Complete(SuggestSystemText(language), "Names: " + entry.NamesText, out string suggestError, 0, 0);
							string name = "";
							string why = "";
							if (suggestError.Length == 0) {
								string[] lines = suggestion.Replace("\r", "").Trim().Split('\n');
								name = lines[0].Trim().Trim('*', '"', '“', '”').Trim();
								if (lines.Length > 1) {
									why = lines[1].Trim();
								}
							}
							if (suggestError.Length > 0 || name.Length == 0) {
								failed++;
								failuresInARow++;
								Console.WriteLine("  no suggestion (" + suggestError + ")");
							}
							if (suggestError.Length == 0 && name.Length > 0 && entry.Has(name) == true) {
								fit++;
								Console.WriteLine("  " + name + " is already one of the names; nothing to add.");
							}
							if (suggestError.Length == 0 && name.Length > 0 && entry.Has(name) == false) {
								int choice = Offer(checkpoint, entry, name, why, language);
								if (choice == 0 || choice == 1) {
									added++;
								}
								if (choice == 2) {
									skipped++;
								}
								if (choice == 3) {
									going = false;
								}
							}
						}
						if (failuresInARow >= 2) {
							going = false;
							Console.WriteLine("Two failed answers in a row; the pass stops here.");
						}
					}
				}
				string summary = language + " names suggested: " + added + " added, " + fit + " already fit, " + skipped + " skipped, " + failed + " without an answer, of " + cast.Count + ".";
				CheckpointLog.Warning(folder, "Glossary", summary);
				Finish(summary);
			}
		}


		/// <summary>
		/// The menu for one suggestion. 0 added and written, 1 added as a name, 2 skipped,
		/// 3 stop. A refused save counts as skipped.
		/// </summary>
		private static int Offer(Checkpoint checkpoint, CharacterEntry entry, string name, string why, string language) {
			ConsoleSelectMenu menu = new(loops: false, numbered: false, clearOnRefresh: true);
			menu.SetPreChoiceText("-- " + entry.Written + " --\nNames: " + entry.NamesText + "\nThe model suggests the " + language + " name " + name + ": " + why + "\n");
			menu.AddChoice(new ConsoleMenuItem("Add " + name + " and make it the written name (rewrites this character's speaker tags)"));
			menu.AddChoice(new ConsoleMenuItem("Add " + name + " as another name only"));
			menu.AddChoice(new ConsoleMenuItem("Skip"));
			menu.AddChoice(new ConsoleMenuItem("Stop the pass"));
			int choice = menu.GetChoice();
			if (choice == 0 || choice == 1) {
				string replaces = entry.Written;
				bool wasProvisional = entry.Provisional;
				entry.Add(name);
				if (choice == 0) {
					entry.Written = name;
					entry.Provisional = false;
				}
				string problem = NametagConform.SaveAndConform(checkpoint, entry, replaces, out string report);
				if (problem.Length > 0) {
					entry.Names.Remove(name);
					entry.Written = replaces;
					entry.Provisional = wasProvisional;
					Console.WriteLine("  " + problem);
					choice = 2;
				}
				if (problem.Length == 0) {
					string line = "added " + name + " to " + replaces;
					if (choice == 0) {
						line += " as the written name";
					}
					Console.WriteLine("  " + line + ". " + report);
					CheckpointLog.Warning(CheckpointInspector.FolderOf(checkpoint.Path), "Glossary", line);
				}
			}
			if (choice < 0) {
				choice = 2;
			}
			return choice;
		}


		/// <summary>
		/// The name on the model's first line when it is one of the character's names
		/// exactly, ignoring case and quotes; empty otherwise. The second line is the reason.
		/// </summary>
		private static string MatchName(string reply, CharacterEntry entry, out string reason) {
			reason = "";
			string[] lines = reply.Replace("\r", "").Trim().Split('\n');
			string first = lines[0].Trim().Trim('*', '"', '“', '”').Trim();
			if (lines.Length > 1) {
				reason = lines[1].Trim();
			}
			string matched = "";
			foreach (string name in entry.Names) {
				if (matched.Length == 0 && string.Equals(name, first, StringComparison.OrdinalIgnoreCase)) {
					matched = name;
				}
			}
			if (matched.Length == 0) {
				reason = first;
			}
			return matched;
		}


		private static string PickSystemText(string language) {
			return "You are given every name a visual-novel character is known by, in one or more languages. "
				+ "Answer with the ONE name from the list that is the standard way to display this person to readers of " + language + ": "
				+ "the everyday short form, usually the given name, in that language's script. "
				+ "Use a full name only when it is the only name given, or when the name itself is a title that carries the person's standing, "
				+ "such as \"Son of God\" or \"Pharaoh, King of Egypt\", where shortening would lose what the name says. "
				+ "Reply with that name exactly as listed on the first line, and one short sentence of reason on the second.";
		}


		private static string FitSystemText(string language) {
			return "You are given every name a visual-novel character is known by, in one or more languages. "
				+ "Is one of them already an acceptable form for readers of " + language + "? "
				+ "Reply with that name exactly as listed on the first line, or NONE, then one short sentence of reason on the second.";
		}


		private static string SuggestSystemText(string language) {
			string from = "";
			if (LlmClient.FromLanguage.Length > 0) {
				from = " The names are from a " + LlmClient.FromLanguage + " script.";
			}
			return "You are given every name a visual-novel character is known by, none of them fit for readers of " + language + "." + from + " "
				+ "Give the name a translation into " + language + " would use for this person: a transliteration, or a translation of the meaning, "
				+ "whichever readers of " + language + " would expect for such a name. Keep a numbering or letter that marks an unnamed extra. "
				+ "Reply with the name alone on the first line, and one short sentence of reason on the second.";
		}


		private static void Finish(string text) {
			Console.WriteLine(text);
			ConsoleExt.WaitForEnter("continue");
		}
	}
}
