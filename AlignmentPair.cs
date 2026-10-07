// File: AlignmentPair.cs
// Namespace: TranslationTools
using System.Globalization;
using System.Text;

namespace TranslationTools {

	/// <summary>
	/// One alignment: one dialogue file, by key, between two checkpoints. Kept as a folder under the Alignment root. The
	/// folder is named by the moment it was made; which checkpoints it pairs is in its info
	/// file, by serial, so a label rename or a re-point never orphans it. The canonical side
	/// is the locked reference whose numbering ascends on Apply; the other side follows it.
	///
	/// pair.info holds named lines, like a checkpoint's record: Created, CanonicalSerial,
	/// OtherSerial, Key, and the two labels as they were at the time, for reading only.
	/// </summary>
	public class AlignmentPair {

		/// <summary>The info file's name inside the pair folder.</summary>
		public const string InfoName = "pair.info";

		/// <summary>How the pair folder is named: the moment it was made, readable.</summary>
		public const string FolderFormat = "yyyy-MM-dd HH-mm-ss";

		private const string CreatedName = "Created";
		private const string CanonicalSerialName = "CanonicalSerial";
		private const string OtherSerialName = "OtherSerial";
		private const string CanonicalLabelName = "CanonicalLabel";
		private const string OtherLabelName = "OtherLabel";
		private const string KeyName = "Key";

		/// <summary>The pair's folder on disk.</summary>
		public string Folder = "";

		/// <summary>When the pair was made, as the folder name.</summary>
		public string Created = "";

		/// <summary>The locked reference checkpoint's serial.</summary>
		public string CanonicalSerial = "";

		/// <summary>The editable checkpoint's serial.</summary>
		public string OtherSerial = "";

		/// <summary>The dialogue file both sides hold, by key: its name without folder or extension.</summary>
		public string Key = "";

		/// <summary>The reference's label when the pair was made. For reading; the serial is what counts.</summary>
		public string CanonicalLabel = "";

		/// <summary>The editable side's label when the pair was made. For reading; the serial is what counts.</summary>
		public string OtherLabel = "";

		/// <summary>Lines from a newer build that this one does not understand, kept so a save does not lose them.</summary>
		public List<string> UnknownLines = new();


		/// <summary>
		/// Makes a new pair folder for two checkpoints, now.
		/// </summary>
		/// <param name="canonical">The locked reference.</param>
		/// <param name="other">The editable side.</param>
		/// <param name="key">The dialogue file to align, by key.</param>
		/// <param name="problem">Why not, when no folder was made; empty otherwise.</param>
		/// <returns>The pair, or null when problem says why.</returns>
		public static AlignmentPair? Create(Checkpoint canonical, Checkpoint other, string key, out string problem) {
			AlignmentPair? pair = null;
			string root = AlignmentRoot.Ensure(out problem);
			if (root.Length > 0) {
				string created = DateTime.Now.ToString(FolderFormat, CultureInfo.InvariantCulture);
				string folder = Path.Combine(root, created);
				if (Directory.Exists(folder) == true) {
					problem = "A pair was made this very second; wait a moment and try again.";
				}
				if (problem.Length == 0) {
					try {
						Directory.CreateDirectory(folder);
						pair = new AlignmentPair();
						pair.Folder = folder;
						pair.Created = created;
						pair.CanonicalSerial = canonical.Serial;
						pair.OtherSerial = other.Serial;
						pair.Key = key;
						pair.CanonicalLabel = canonical.Label;
						pair.OtherLabel = other.Label;
						pair.Save();
					}
					catch (Exception exception) {
						problem = "Could not make " + folder + ": " + exception.Message;
						pair = null;
					}
				}
			}
			return pair;
		}


		/// <summary>
		/// Reads a pair from its folder.
		/// </summary>
		/// <param name="folder">The pair folder.</param>
		/// <param name="pair">The pair, complete only when this returns true.</param>
		/// <returns>True when the folder held an info file naming both serials.</returns>
		public static bool TryLoad(string folder, out AlignmentPair pair) {
			pair = new AlignmentPair();
			pair.Folder = folder;
			pair.Created = Path.GetFileName(folder.TrimEnd('\\', '/'));
			string path = Path.Combine(folder, InfoName);
			if (File.Exists(path) == true) {
				foreach (string line in File.ReadAllLines(path, Encoding.UTF8)) {
					int equals = line.IndexOf('=');
					if (equals > 0) {
						string name = line.Substring(0, equals);
						string value = line.Substring(equals + 1).Trim();
						bool known = pair.ReadLine(name, value);
						if (known == false) {
							pair.UnknownLines.Add(line);
						}
					}
				}
			}
			return pair.CanonicalSerial.Length > 0 && pair.OtherSerial.Length > 0 && pair.Key.Length > 0;
		}


		/// <summary>
		/// Every pair under the Alignment root that has a readable info file, newest first.
		/// </summary>
		public static List<AlignmentPair> All() {
			List<AlignmentPair> pairs = new();
			string root = AlignmentRoot.Folder;
			if (root.Length > 0 && Directory.Exists(root) == true) {
				List<string> folders = new(Directory.GetDirectories(root));
				folders.Sort(string.CompareOrdinal);
				folders.Reverse();
				foreach (string folder in folders) {
					if (TryLoad(folder, out AlignmentPair pair) == true) {
						pairs.Add(pair);
					}
				}
			}
			return pairs;
		}


		/// <summary>
		/// Writes the info file.
		/// </summary>
		public void Save() {
			List<string> lines = new();
			lines.Add(CreatedName + "=" + Created);
			lines.Add(CanonicalSerialName + "=" + CanonicalSerial);
			lines.Add(OtherSerialName + "=" + OtherSerial);
			lines.Add(KeyName + "=" + Key);
			lines.Add(CanonicalLabelName + "=" + CanonicalLabel);
			lines.Add(OtherLabelName + "=" + OtherLabel);
			lines.AddRange(UnknownLines);
			File.WriteAllText(Path.Combine(Folder, InfoName), string.Join("\n", lines) + "\n", new UTF8Encoding(false));
		}


		/// <summary>
		/// One line for a list: when it was made, then the editable side and the reference
		/// as they are labelled now, with "(missing)" for a serial no checkpoint has any more.
		/// </summary>
		public string Describe() {
			return Created + "   " + Key.PadRight(12) + " " + SideText(OtherSerial, OtherLabel) + "  ->  " + SideText(CanonicalSerial, CanonicalLabel) + " (reference)";
		}


		/// <summary>
		/// A side as the user sees it: its current label and serial, or the old label marked missing.
		/// </summary>
		private static string SideText(string serial, string labelThen) {
			string text = labelThen + " #" + serial + " (missing)";
			Checkpoint? now = CheckpointList.FindBySerial(serial);
			if (now != null) {
				text = now.Label + " #" + serial;
			}
			return text;
		}


		/// <summary>
		/// Stores one decoded line into its field.
		/// </summary>
		/// <returns>True when the name was one this build knows.</returns>
		private bool ReadLine(string name, string value) {
			bool known = true;
			if (name == CreatedName) {
				Created = value;
			}
			if (name == CanonicalSerialName) {
				CanonicalSerial = value;
			}
			if (name == OtherSerialName) {
				OtherSerial = value;
			}
			if (name == KeyName) {
				Key = value;
			}
			if (name == CanonicalLabelName) {
				CanonicalLabel = value;
			}
			if (name == OtherLabelName) {
				OtherLabel = value;
			}
			if (name != CreatedName && name != CanonicalSerialName && name != OtherSerialName && name != KeyName
				&& name != CanonicalLabelName && name != OtherLabelName) {
				known = false;
			}
			return known;
		}
	}
}
