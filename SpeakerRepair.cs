// File: SpeakerRepair.cs
// Namespace: TranslationTools
using TALOREAL_NETCORE_API;

namespace TranslationTools {

	/// <summary>
	/// Repairs one side's speaker tags from a COMPLETED alignment: every recorded pair was
	/// ruled to be one line, so it has one speaker, and the side the user declares the
	/// standard says who. The other line is conformed to that character's written name in
	/// the shared glossary - rewritten, given a tag, or stripped of one - on that line only,
	/// locked or not, since alignment is a known change. Nothing runs while a walk is still
	/// open, and nothing is paired here: the walk walks, this repairs.
	/// </summary>
	public static class SpeakerRepair {

		/// <summary>
		/// Asks which side is the standard, then repairs the other side's tags over the pair's
		/// file. Refuses until the pairing is complete, and when either side has no speaker
		/// tag learned, since a tag can be neither read nor written then.
		/// </summary>
		public static void Run(AlignmentPair pair, Checkpoint edit, Checkpoint reference) {
			AlignmentPairing pairing = AlignmentPairing.Load(pair.Folder);
			NametagConvention? editTags = NametagConvention.For(edit);
			NametagConvention? refTags = NametagConvention.For(reference);
			string problem = "";
			bool finished = pairing.Status == AlignmentPairing.Complete || pairing.Status == AlignmentPairing.Applied;
			if (finished == false) {
				problem = "The alignment of " + pair.Key + " is not complete (" + pairing.Status + "). Walk it to the end first; the repair trusts only a finished pairing.";
			}
			if (problem.Length == 0 && (editTags == null || refTags == null)) {
				problem = "Both checkpoints need a speaker tag learned (Glossaries -> Learning) before tags can be read on one side and written on the other.";
			}
			if (problem.Length > 0) {
				Console.WriteLine(problem);
				ConsoleExt.WaitForEnter("continue");
			}
			if (problem.Length == 0) {
				ConsoleSelectMenu menu = new(loops: false, numbered: false, clearOnRefresh: true);
				menu.SetPreChoiceText("-- Repair speaker tags from the alignment of " + pair.Key + " --\n"
					+ "Whose tags are the standard? The other side's tags are rewritten to match, line by line, over the "
					+ pairing.PairedCount + " recorded pair(s).\n");
				menu.AddChoice(new ConsoleMenuItem("\"" + reference.Label + "\" is the standard: repair \"" + edit.Label + "\""));
				menu.AddChoice(new ConsoleMenuItem("\"" + edit.Label + "\" is the standard: repair \"" + reference.Label + "\""));
				menu.AddChoice(new ConsoleMenuItem("Cancel"));
				int choice = menu.GetChoice();
				if (choice == 0) {
					Repair(pairing, reference, pair.RefKey, refTags!, false, edit, pair.Key, editTags!, true);
				}
				if (choice == 1) {
					Repair(pairing, edit, pair.Key, editTags!, true, reference, pair.RefKey, refTags!, false);
				}
			}
		}


		/// <summary>
		/// The pass itself: for every pair, the standard line's speaker is read through the
		/// glossary and the other line is made to agree.
		/// </summary>
		private static void Repair(AlignmentPairing pairing, Checkpoint standard, string standardKey, NametagConvention standardTags, bool standardIsEdit,
			Checkpoint repaired, string repairedKey, NametagConvention repairedTags, bool repairedIsEdit) {
			List<CharacterEntry> cast = Glossary.Characters(CheckpointInspector.FolderOf(standard.Path));
			Dictionary<int, AlignmentLine> standardLines = ByIndex(AlignmentLines.Read(AlignmentLines.DialoguePath(standard, standardKey), standardTags));
			Dictionary<int, AlignmentLine> repairedLines = ByIndex(AlignmentLines.Read(AlignmentLines.DialoguePath(repaired, repairedKey), repairedTags));
			int rewritten = 0;
			int added = 0;
			int removed = 0;
			int agreed = 0;
			int unknownTag = 0;
			int missingLine = 0;
			int failed = 0;
			Console.WriteLine("Repairing \"" + repaired.Label + "\" " + repairedKey + " against \"" + standard.Label + "\" " + standardKey + " ...");
			foreach (PairingEntry entry in pairing.Entries) {
				if (entry.Edit >= 0 && entry.Ref >= 0) {
					int standardIndex = standardIsEdit ? entry.Edit : entry.Ref;
					int repairedIndex = repairedIsEdit ? entry.Edit : entry.Ref;
					bool both = standardLines.ContainsKey(standardIndex) == true && repairedLines.ContainsKey(repairedIndex) == true;
					if (both == false) {
						missingLine++;
					}
					if (both == true) {
						AlignmentLine model = standardLines[standardIndex];
						AlignmentLine line = repairedLines[repairedIndex];
						CharacterEntry? who = null;
						bool readable = true;
						if (model.HasNametag == true) {
							who = Glossary.Find(cast, model.Name);
							if (who == null) {
								readable = false;
								unknownTag++;
								Console.WriteLine("  " + standardKey + " " + standardIndex + ": tag " + model.Name + " is not in the glossary; left alone");
							}
						}
						if (readable == true) {
							CharacterEntry? has = null;
							if (line.HasNametag == true) {
								has = Glossary.Find(cast, line.Name);
							}
							bool same = (who == null && line.HasNametag == false)
								|| (who != null && has != null && string.Equals(who.Written, has.Written, StringComparison.OrdinalIgnoreCase));
							if (same == true) {
								agreed++;
							}
							if (same == false) {
								string wanted = "";
								if (who != null) {
									wanted = who.Written;
								}
								bool hadTag = line.HasNametag;
								string was = Speaker(line);
								string problem = NametagConform.RewriteOne(repaired, repairedKey, repairedTags, line, wanted);
								if (problem.Length > 0) {
									failed++;
									Console.WriteLine("  " + problem);
								}
								if (problem.Length == 0) {
									if (wanted.Length == 0) {
										removed++;
									}
									if (wanted.Length > 0 && hadTag == false) {
										added++;
									}
									if (wanted.Length > 0 && hadTag == true) {
										rewritten++;
									}
									Console.WriteLine("  " + repairedKey + " " + repairedIndex + ": " + was + " -> " + Speaker(line));
								}
							}
						}
					}
				}
			}
			string summary = "Speaker repair of \"" + repaired.Label + "\" " + repairedKey + " from \"" + standard.Label + "\" " + standardKey + ": "
				+ rewritten + " tag(s) rewritten, " + added + " added, " + removed + " removed, " + agreed + " already agreed, "
				+ unknownTag + " standard tag(s) unknown to the glossary, " + missingLine + " pair(s) whose line was not found, " + failed + " failed.";
			Console.WriteLine(summary);
			CheckpointLog.Warning(CheckpointInspector.FolderOf(repaired.Path), "Align", summary);
			CheckpointLog.Warning(CheckpointInspector.FolderOf(standard.Path), "Align", summary);
			ConsoleExt.WaitForEnter("continue");
		}


		private static Dictionary<int, AlignmentLine> ByIndex(List<AlignmentLine> lines) {
			Dictionary<int, AlignmentLine> byIndex = new();
			foreach (AlignmentLine line in lines) {
				byIndex[line.Index] = line;
			}
			return byIndex;
		}


		private static string Speaker(AlignmentLine line) {
			string text = "(narration)";
			if (line.HasNametag == true) {
				text = "【" + line.Name + "】";
			}
			return text;
		}
	}
}
