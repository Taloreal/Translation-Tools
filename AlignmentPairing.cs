// File: AlignmentPairing.cs
// Namespace: TranslationTools
using System.Text;

namespace TranslationTools {

	/// <summary>
	/// One decision of the walk: an editable-side index paired with a reference-side index,
	/// or a line on one side with no partner. How says who decided: auto (the text matched),
	/// nametag (the TGD anchor), user, or only (no partner).
	/// </summary>
	public class PairingEntry {

		/// <summary>The word for a line with no partner.</summary>
		public const string Only = "only";

		/// <summary>The editable side's index, or -1 when the reference line has no partner.</summary>
		public int Edit = -1;

		/// <summary>The reference side's index, or -1 when the editable line has no partner.</summary>
		public int Ref = -1;

		/// <summary>Who decided: "auto", "nametag", "user", or "only".</summary>
		public string How = "";
	}


	/// <summary>
	/// The walk's record for a pair: pairing.txt, one entry per line in walk order, written
	/// after every decision so a closed window loses nothing, and status.txt saying whether
	/// the walk is complete. Reopening a pair resumes at the first unpaired line on each
	/// side; Reset forgets everything.
	/// </summary>
	public class AlignmentPairing {

		/// <summary>The entries file inside the pair folder.</summary>
		public const string FileName = "pairing.txt";

		/// <summary>The status file inside the pair folder.</summary>
		public const string StatusName = "status.txt";

		/// <summary>The status word while the walk is unfinished.</summary>
		public const string Walking = "walking";

		/// <summary>The status word once every line on both sides has been decided.</summary>
		public const string Complete = "complete";

		/// <summary>The pair folder.</summary>
		public string Folder = "";

		/// <summary>Every decision, in walk order.</summary>
		public List<PairingEntry> Entries = new();

		/// <summary>Walking or Complete.</summary>
		public string Status = Walking;

		private readonly HashSet<int> pairedEdit = new();
		private readonly HashSet<int> pairedRef = new();


		/// <summary>
		/// Reads a pair folder's record; an absent file is an empty, walking record.
		/// </summary>
		public static AlignmentPairing Load(string folder) {
			AlignmentPairing pairing = new();
			pairing.Folder = folder;
			string path = Path.Combine(folder, FileName);
			if (File.Exists(path) == true) {
				foreach (string line in File.ReadAllLines(path, Encoding.UTF8)) {
					string[] parts = line.Split('\t');
					if (parts.Length >= 3 && line.StartsWith("#") == false) {
						PairingEntry entry = new();
						int.TryParse(parts[0], out entry.Edit);
						int.TryParse(parts[1], out entry.Ref);
						if (parts[0].Trim() == "-") {
							entry.Edit = -1;
						}
						if (parts[1].Trim() == "-") {
							entry.Ref = -1;
						}
						entry.How = parts[2].Trim();
						pairing.Remember(entry);
					}
				}
			}
			string statusPath = Path.Combine(folder, StatusName);
			if (File.Exists(statusPath) == true) {
				foreach (string line in File.ReadAllLines(statusPath, Encoding.UTF8)) {
					if (line.StartsWith("status=") == true) {
						pairing.Status = line.Substring("status=".Length).Trim();
					}
				}
			}
			return pairing;
		}


		/// <summary>How many entries pair a line on each side.</summary>
		public int PairedCount {
			get {
				int count = 0;
				foreach (PairingEntry entry in Entries) {
					if (entry.Edit >= 0 && entry.Ref >= 0) {
						count++;
					}
				}
				return count;
			}
		}

		/// <summary>How many editable-side lines have no partner.</summary>
		public int EditOnlyCount {
			get { return OnlyCount(true); }
		}

		/// <summary>How many reference-side lines have no partner.</summary>
		public int RefOnlyCount {
			get { return OnlyCount(false); }
		}


		/// <summary>Whether an editable-side index has been decided.</summary>
		public bool EditDecided(int index) {
			return pairedEdit.Contains(index);
		}


		/// <summary>Whether a reference-side index has been decided.</summary>
		public bool RefDecided(int index) {
			return pairedRef.Contains(index);
		}


		/// <summary>
		/// Records a decision and writes the file.
		/// </summary>
		public void Add(PairingEntry entry) {
			Remember(entry);
			Save();
		}


		/// <summary>
		/// Takes back the last decision and writes the file.
		/// </summary>
		/// <returns>The entry taken back, or null when there was none.</returns>
		public PairingEntry? UndoLast() {
			PairingEntry? last = null;
			if (Entries.Count > 0) {
				last = Entries[Entries.Count - 1];
				Entries.RemoveAt(Entries.Count - 1);
				pairedEdit.Remove(last.Edit);
				pairedRef.Remove(last.Ref);
				Status = Walking;
				Save();
			}
			return last;
		}


		/// <summary>
		/// Marks the walk complete and writes the status.
		/// </summary>
		public void MarkComplete() {
			Status = Complete;
			Save();
		}


		/// <summary>
		/// Forgets every decision: both files are deleted.
		/// </summary>
		public void Reset() {
			Entries.Clear();
			pairedEdit.Clear();
			pairedRef.Clear();
			Status = Walking;
			File.Delete(Path.Combine(Folder, FileName));
			File.Delete(Path.Combine(Folder, StatusName));
		}


		/// <summary>
		/// Writes both files whole.
		/// </summary>
		public void Save() {
			StringBuilder text = new();
			text.AppendLine("# editable index\treference index\thow   (- = no partner)");
			foreach (PairingEntry entry in Entries) {
				text.Append(IndexWord(entry.Edit)).Append('\t').Append(IndexWord(entry.Ref)).Append('\t').Append(entry.How).AppendLine();
			}
			File.WriteAllText(Path.Combine(Folder, FileName), text.ToString(), new UTF8Encoding(false));
			StringBuilder status = new();
			status.AppendLine("status=" + Status);
			status.AppendLine("paired=" + PairedCount);
			status.AppendLine("editable-only=" + EditOnlyCount);
			status.AppendLine("reference-only=" + RefOnlyCount);
			status.AppendLine("updated=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
			File.WriteAllText(Path.Combine(Folder, StatusName), status.ToString(), new UTF8Encoding(false));
		}


		private void Remember(PairingEntry entry) {
			Entries.Add(entry);
			if (entry.Edit >= 0) {
				pairedEdit.Add(entry.Edit);
			}
			if (entry.Ref >= 0) {
				pairedRef.Add(entry.Ref);
			}
		}


		private int OnlyCount(bool editSide) {
			int count = 0;
			foreach (PairingEntry entry in Entries) {
				bool editOnly = entry.Edit >= 0 && entry.Ref < 0;
				bool refOnly = entry.Ref >= 0 && entry.Edit < 0;
				if ((editSide == true && editOnly == true) || (editSide == false && refOnly == true)) {
					count++;
				}
			}
			return count;
		}


		private static string IndexWord(int index) {
			string word = "-";
			if (index >= 0) {
				word = index.ToString();
			}
			return word;
		}
	}
}
