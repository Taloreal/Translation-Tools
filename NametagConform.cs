// File: NametagConform.cs
// Namespace: TranslationTools
using System.Text;

namespace TranslationTools {

	/// <summary>
	/// Keeps a checkpoint's dialogue files true to its glossary: every speaker tag carries
	/// the written name of the character it belongs to. Runs after any save that touches a
	/// character's names, rewriting the tags whose text is one of that character's other
	/// names. Needs the checkpoint's speaker tag to find them; without one, nothing is
	/// rewritten and the report says why. A locked checkpoint is never written.
	/// </summary>
	public static class NametagConform {

		/// <summary>
		/// Saves a character and conforms the checkpoint's dialogue files to it. The save is
		/// refused outright when the checkpoint is locked and the files would change.
		/// </summary>
		/// <param name="checkpoint">The checkpoint whose glossary and files are touched.</param>
		/// <param name="entry">The character to save.</param>
		/// <param name="replaces">The written name the character had before, or empty for a new one.</param>
		/// <param name="report">What happened to the files, one or two sentences, on success.</param>
		/// <param name="retired">Names the character no longer has whose tags are rewritten too, such as an old spelling; null for none.</param>
		/// <returns>Empty on success, otherwise a plain sentence; nothing was written then.</returns>
		public static string SaveAndConform(Checkpoint checkpoint, CharacterEntry entry, string replaces, out string report, List<string>? retired = null) {
			report = "";
			if (retired == null) {
				retired = new List<string>();
			}
			string folder = CheckpointInspector.FolderOf(checkpoint.Path);
			NametagConvention? convention = NametagConvention.For(checkpoint);
			// Siglus wants its string literals quoted; quotes around a name in a tag are
			// harmless, so every name conform writes on Siglus goes in quoted.
			bool quoteNames = CheckpointInspector.Inspect(checkpoint.Path).Engine == CheckpointEngine.Siglus;
			int lines = 0;
			int files = 0;
			string problem = "";
			if (convention != null) {
				problem = Rewrite(checkpoint, entry, convention, retired, quoteNames, false, out lines, out files);
			}
			if (problem.Length == 0 && lines > 0 && checkpoint.Writable == false) {
				problem = "\"" + checkpoint.Label + "\" is locked, and this change would rewrite " + lines + " speaker tag(s) in " + files + " file(s). Unlock it first.";
			}
			if (problem.Length == 0) {
				problem = Glossary.SaveCharacter(folder, entry, replaces);
			}
			if (problem.Length == 0 && convention == null) {
				report = "No files were conformed: \"" + checkpoint.Label + "\" has no speaker tag learned, so its tags cannot be found.";
			}
			if (problem.Length == 0 && convention != null && lines == 0) {
				report = "Every speaker tag already reads " + entry.Written + "; no file changed.";
			}
			if (problem.Length == 0 && convention != null && lines > 0) {
				problem = Rewrite(checkpoint, entry, convention, retired, quoteNames, true, out lines, out files);
				if (problem.Length == 0) {
					report = lines + " speaker tag(s) in " + files + " file(s) now read " + entry.Written + ".";
					CheckpointLog.Warning(folder, "Glossary", report);
				}
			}
			return problem;
		}


		/// <summary>
		/// Finds, and when asked rewrites, every tag in the checkpoint's dialogue files whose
		/// text is one of the character's names other than the written one.
		/// </summary>
		/// <param name="retired">Names no longer the character's whose tags are rewritten as well.</param>
		/// <param name="quoteNames">Whether a name written into a tag is wrapped in double quotes (Siglus).</param>
		/// <param name="write">False counts only; true writes the changed files.</param>
		/// <returns>Empty on success, otherwise which file failed and why.</returns>
		private static string Rewrite(Checkpoint checkpoint, CharacterEntry entry, NametagConvention convention, List<string> retired, bool quoteNames, bool write, out int lines, out int files) {
			lines = 0;
			files = 0;
			string problem = "";
			string dialogues = Path.Combine(CheckpointInspector.FolderOf(checkpoint.Path), CheckpointInspector.SplitFolder, NScripterSplit.DialoguesFolder);
			if (Directory.Exists(dialogues) == true) {
				string[] paths = Directory.GetFiles(dialogues, "*.txt");
				Array.Sort(paths, StringComparer.OrdinalIgnoreCase);
				foreach (string path in paths) {
					if (problem.Length == 0) {
						try {
							string text = SiglusScript.ScriptEncoding.GetString(File.ReadAllBytes(path));
							string[] raw = text.Split('\n');
							int changed = 0;
							for (int at = 0; at < raw.Length; at++) {
								string line = raw[at];
								string ending = "";
								if (line.EndsWith("\r") == true) {
									ending = "\r";
									line = line.Substring(0, line.Length - 1);
								}
								string rewritten = RewriteLine(line, entry, convention, retired, quoteNames);
								if (string.Equals(rewritten, line, StringComparison.Ordinal) == false) {
									raw[at] = rewritten + ending;
									changed++;
								}
							}
							if (changed > 0) {
								lines += changed;
								files++;
								if (write == true) {
									File.WriteAllBytes(path, SiglusScript.ScriptEncoding.GetBytes(string.Join("\n", raw)));
									CheckpointStamps.RefreshDialogue(CheckpointInspector.FolderOf(checkpoint.Path), Path.GetFileNameWithoutExtension(path));
								}
							}
						}
						catch (Exception exception) {
							problem = "Could not conform " + Path.GetFileName(path) + ": " + exception.Message;
						}
					}
				}
			}
			return problem;
		}


		/// <summary>
		/// One dialogue line with its tag's name replaced by the written one when the tag
		/// names this character by another name; the line unchanged otherwise. The tag's
		/// own spacing is kept, and quotes already around the name stay. On Siglus a name
		/// that had no quotes gets them, since quotes in a tag are harmless there and a
		/// Japanese name outside them is not.
		/// </summary>
		private static string RewriteLine(string line, CharacterEntry entry, NametagConvention convention, List<string> retired, bool quoteNames) {
			string result = line;
			if (NScripterSplit.TryReadPointer(line, out string pointer, out int index, out string rest) == true) {
				if (convention.Matches(rest, out string name) == true && (entry.Has(name) == true || CharacterEntry.Contains(retired, name) == true)) {
					bool differs = string.Equals(name, entry.Written, StringComparison.Ordinal) == false;
					if (convention.Standalone == true) {
						// The whole line is the tag: it becomes the written name plus the marker
						// that tells a translator what it is, marker added where it was missing.
						bool unmarked = rest.TrimEnd().EndsWith(NametagConvention.LineMarker.Trim(), StringComparison.Ordinal) == false;
						if (differs == true || unmarked == true) {
							result = pointer + entry.Written + NametagConvention.LineMarker;
						}
					}
					if (convention.Standalone == false && differs == true) {
						int open = rest.IndexOf(convention.Opener);
						int close = rest.IndexOf(convention.Closer, open + 1);
						string inner = rest.Substring(open + 1, close - open - 1);
						int at = inner.IndexOf(name, StringComparison.Ordinal);
						if (at >= 0) {
							bool quoted = at > 0 && inner[at - 1] == '"' && at + name.Length < inner.Length && inner[at + name.Length] == '"';
							string replacement = entry.Written;
							if (quoteNames == true && quoted == false) {
								replacement = "\"" + entry.Written + "\"";
							}
							string newInner = inner.Substring(0, at) + replacement + inner.Substring(at + name.Length);
							result = pointer + rest.Substring(0, open + 1) + newInner + rest.Substring(close);
						}
					}
				}
			}
			return result;
		}


		/// <summary>
		/// The walk's correction: rewrites the tag of ONE dialogue line, adding one where the
		/// line had none, removing it, or replacing its name, and updates the line in memory.
		/// The lock is not consulted: alignment is a known change, and this touches one tag
		/// on one line that the user just ruled on, never the text.
		/// </summary>
		/// <param name="checkpoint">The checkpoint whose file is written.</param>
		/// <param name="key">The dialogue file's key.</param>
		/// <param name="convention">How that file tags its speaker.</param>
		/// <param name="line">The line, updated on success.</param>
		/// <param name="newName">The name the tag should carry, or empty to remove the tag.</param>
		/// <returns>Empty on success, otherwise a plain sentence; nothing was written then.</returns>
		public static string RewriteOne(Checkpoint checkpoint, string key, NametagConvention convention, AlignmentLine line, string newName) {
			string problem = "";
			bool quoteNames = CheckpointInspector.Inspect(checkpoint.Path).Engine == CheckpointEngine.Siglus;
			string path = AlignmentLines.DialoguePath(checkpoint, key);
			// Under the standalone kind the tag is a line of its own: the line to rewrite is
			// the tag line, whether this line is it or is the one it names. A tag line can be
			// renamed but not removed or added here, since that would delete or insert a line.
			int target = line.Index;
			if (convention.Standalone == true) {
				target = line.TagIndex;
				if (line.IsTagLine == true) {
					target = line.Index;
				}
				if (newName.Length == 0) {
					problem = "Line " + line.Index + ": a standalone name line cannot be removed by a repair; delete it in the file if it is wrong.";
				}
				if (problem.Length == 0 && target < 0) {
					problem = "Line " + line.Index + " has no name line above it; adding one would mean inserting a line, which a repair does not do.";
				}
			}
			try {
				string text = "";
				if (problem.Length == 0) {
					text = SiglusScript.ScriptEncoding.GetString(File.ReadAllBytes(path));
				}
				string[] raw = text.Split('\n');
				bool found = false;
				for (int at = 0; at < raw.Length && problem.Length == 0; at++) {
					string whole = raw[at];
					string ending = "";
					if (whole.EndsWith("\r") == true) {
						ending = "\r";
						whole = whole.Substring(0, whole.Length - 1);
					}
					if (found == false && NScripterSplit.TryReadPointer(whole, out string pointer, out int index, out string rest) == true && index == target) {
						found = true;
						string newRest = RetagRest(rest, convention, newName, quoteNames);
						raw[at] = pointer + newRest + ending;
						if (convention.Standalone == true) {
							line.Name = newName;
							if (line.IsTagLine == true) {
								line.Text = newRest;
							}
						}
						if (convention.Standalone == false) {
							line.Text = newRest;
							line.HasNametag = convention.Matches(newRest, out string name);
							line.Name = "";
							if (line.HasNametag == true) {
								line.Name = name;
							}
							line.Bare = AlignmentLines.BareText(newRest, convention);
						}
					}
				}
				if (problem.Length == 0 && found == false) {
					problem = "Line " + target + " was not found in " + Path.GetFileName(path) + ".";
				}
				if (found == true) {
					File.WriteAllBytes(path, SiglusScript.ScriptEncoding.GetBytes(string.Join("\n", raw)));
					CheckpointStamps.RefreshDialogue(CheckpointInspector.FolderOf(checkpoint.Path), key);
				}
			}
			catch (Exception exception) {
				problem = "Could not rewrite " + Path.GetFileName(path) + ": " + exception.Message;
			}
			return problem;
		}


		/// <summary>
		/// The text after the pointer with its tag added, removed or renamed. Quotes already
		/// around the name stay; on Siglus a name goes in quoted.
		/// </summary>
		private static string RetagRest(string rest, NametagConvention convention, string newName, bool quoteNames) {
			string result = rest;
			string quoted = newName;
			if (quoteNames == true && newName.Length > 0) {
				quoted = "\"" + newName + "\"";
			}
			if (convention.Standalone == true) {
				// A tag line is nothing but the name: the whole line is replaced, marker and all.
				if (newName.Length > 0) {
					result = newName + NametagConvention.LineMarker;
				}
			}
			string name = "";
			bool tagged = false;
			if (convention.Standalone == false) {
				tagged = convention.Matches(rest, out name);
			}
			if (convention.Standalone == false && tagged == false && newName.Length > 0) {
				result = convention.Opener + quoted + convention.Closer + rest;
			}
			if (tagged == true) {
				int open = rest.IndexOf(convention.Opener);
				int close = rest.IndexOf(convention.Closer, open + 1);
				if (newName.Length == 0) {
					result = rest.Substring(0, open) + rest.Substring(close + 1);
				}
				if (newName.Length > 0) {
					string inner = rest.Substring(open + 1, close - open - 1);
					int at = inner.IndexOf(name, StringComparison.Ordinal);
					string newInner = quoted;
					if (at >= 0) {
						bool hadQuotes = at > 0 && inner[at - 1] == '"' && at + name.Length < inner.Length && inner[at + name.Length] == '"';
						string replacement = newName;
						if (quoteNames == true && hadQuotes == false) {
							replacement = quoted;
						}
						newInner = inner.Substring(0, at) + replacement + inner.Substring(at + name.Length);
					}
					result = rest.Substring(0, open + 1) + newInner + rest.Substring(close);
				}
			}
			return result;
		}
	}
}
