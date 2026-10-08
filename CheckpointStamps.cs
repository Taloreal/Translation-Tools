// File: CheckpointStamps.cs
// Namespace: TranslationTools
using System.Security.Cryptography;
using System.Text;

namespace TranslationTools {

	/// <summary>
	/// One dialogue file's alignment stamp: which checkpoint and file it was aligned with,
	/// when, and the content hashes the tool wrote, so a later reader can tell "aligned
	/// and untouched since" from "edited outside the tool".
	/// </summary>
	public class Stamp {

		/// <summary>The dialogue key the stamp is about.</summary>
		public string File = "";

		/// <summary>The other checkpoint's serial.</summary>
		public string Partner = "";

		/// <summary>The other side's dialogue key.</summary>
		public string PartnerFile = "";

		/// <summary>When the alignment was applied.</summary>
		public string Applied = "";

		/// <summary>The hash of the dialogue file as the tool last wrote it.</summary>
		public string Dialogue = "";

		/// <summary>The hash of the code file(s) carrying the file's tokens, as the tool last wrote them.</summary>
		public string Code = "";

		/// <summary>The code file(s), relative to the split folder, semicolon-separated.</summary>
		public string CodeFiles = "";
	}


	/// <summary>
	/// The stamps file at the top of a checkpoint, checkpoint.stamps: one block of named
	/// lines per dialogue key, a blank line between blocks. Apply writes a block; every
	/// write the tool itself makes to a stamped dialogue file refreshes its hash, so the
	/// tool's own writes never look like outside edits. Nothing reads it yet: the gate will.
	/// </summary>
	public static class CheckpointStamps {

		/// <summary>The file's name, allowed at the top level of every checkpoint.</summary>
		public const string FileName = "checkpoint.stamps";


		/// <summary>
		/// Every stamp in the file; an absent file is an empty list.
		/// </summary>
		public static List<Stamp> Load(string checkpointFolder) {
			List<Stamp> stamps = new();
			string path = Path.Combine(checkpointFolder, FileName);
			if (File.Exists(path) == true) {
				Stamp? current = null;
				foreach (string line in File.ReadAllLines(path, Encoding.UTF8)) {
					int equals = line.IndexOf('=');
					if (line.Trim().Length == 0) {
						current = null;
					}
					if (equals > 0) {
						string name = line.Substring(0, equals).Trim();
						string value = line.Substring(equals + 1).Trim();
						if (name == "File") {
							current = new Stamp();
							current.File = value;
							stamps.Add(current);
						}
						if (current != null && name == "Partner") {
							current.Partner = value;
						}
						if (current != null && name == "PartnerFile") {
							current.PartnerFile = value;
						}
						if (current != null && name == "Applied") {
							current.Applied = value;
						}
						if (current != null && name == "Dialogue") {
							current.Dialogue = value;
						}
						if (current != null && name == "Code") {
							current.Code = value;
						}
						if (current != null && name == "CodeFiles") {
							current.CodeFiles = value;
						}
					}
				}
			}
			return stamps;
		}


		/// <summary>
		/// The stamp for a key, or null.
		/// </summary>
		public static Stamp? Find(string checkpointFolder, string key) {
			Stamp? found = null;
			foreach (Stamp stamp in Load(checkpointFolder)) {
				if (found == null && string.Equals(stamp.File, key, StringComparison.OrdinalIgnoreCase)) {
					found = stamp;
				}
			}
			return found;
		}


		/// <summary>
		/// Writes a stamp, replacing any older one for the same key.
		/// </summary>
		/// <returns>Empty on success, otherwise a plain sentence.</returns>
		public static string Set(string checkpointFolder, Stamp stamp) {
			List<Stamp> stamps = Load(checkpointFolder);
			List<Stamp> kept = new();
			foreach (Stamp old in stamps) {
				if (string.Equals(old.File, stamp.File, StringComparison.OrdinalIgnoreCase) == false) {
					kept.Add(old);
				}
			}
			kept.Add(stamp);
			return Save(checkpointFolder, kept);
		}


		/// <summary>
		/// Recomputes the dialogue hash of a stamped key after the tool wrote its file. A
		/// key with no stamp is left alone.
		/// </summary>
		public static void RefreshDialogue(string checkpointFolder, string key) {
			Stamp? stamp = Find(checkpointFolder, key);
			if (stamp != null) {
				string path = Path.Combine(checkpointFolder, CheckpointInspector.SplitFolder, NScripterSplit.DialoguesFolder, key + ".txt");
				if (File.Exists(path) == true) {
					stamp.Dialogue = HashOf(path);
					Set(checkpointFolder, stamp);
				}
			}
		}


		/// <summary>
		/// The SHA-256 of a file's bytes, as lower-case hex.
		/// </summary>
		public static string HashOf(string path) {
			StringBuilder hex = new();
			using (SHA256 sha = SHA256.Create()) {
				foreach (byte piece in sha.ComputeHash(File.ReadAllBytes(path))) {
					hex.Append(piece.ToString("x2"));
				}
			}
			return hex.ToString();
		}


		/// <summary>
		/// One hash over several files, in the order given: the hash of their hashes.
		/// </summary>
		public static string HashOfAll(List<string> paths) {
			StringBuilder joined = new();
			foreach (string path in paths) {
				joined.Append(HashOf(path)).Append('\n');
			}
			StringBuilder hex = new();
			using (SHA256 sha = SHA256.Create()) {
				foreach (byte piece in sha.ComputeHash(Encoding.UTF8.GetBytes(joined.ToString()))) {
					hex.Append(piece.ToString("x2"));
				}
			}
			return hex.ToString();
		}


		private static string Save(string checkpointFolder, List<Stamp> stamps) {
			string problem = "";
			try {
				StringBuilder text = new();
				foreach (Stamp stamp in stamps) {
					text.Append("File=").Append(stamp.File).AppendLine();
					text.Append("Partner=").Append(stamp.Partner).AppendLine();
					text.Append("PartnerFile=").Append(stamp.PartnerFile).AppendLine();
					text.Append("Applied=").Append(stamp.Applied).AppendLine();
					text.Append("Dialogue=").Append(stamp.Dialogue).AppendLine();
					text.Append("Code=").Append(stamp.Code).AppendLine();
					text.Append("CodeFiles=").Append(stamp.CodeFiles).AppendLine();
					text.AppendLine();
				}
				File.WriteAllText(Path.Combine(checkpointFolder, FileName), text.ToString(), new UTF8Encoding(false));
			}
			catch (Exception exception) {
				problem = "Could not write " + FileName + ": " + exception.Message;
			}
			return problem;
		}
	}
}
