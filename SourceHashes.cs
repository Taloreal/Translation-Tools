// File: SourceHashes.cs
// Namespace: TranslationTools
using System.Security.Cryptography;
using System.Text;

namespace TranslationTools {

	/// <summary>
	/// A record of what the sources looked like the last time they compiled: one SHA-256 per
	/// file under extract\, kept as checkpoint.hashes at the top of the checkpoint. A later
	/// check compares the files against it, and only a file whose hash changed sends the
	/// sources back to the compiler. Written by Build; the inspector ignores the file.
	/// </summary>
	public static class SourceHashes {

		/// <summary>The record's file name, allowed at the top level of every checkpoint.</summary>
		public const string FileName = "checkpoint.hashes";


		/// <summary>
		/// Hashes every file under a folder, recursively, as "hash  relative path" lines.
		/// </summary>
		/// <param name="folder">The folder to record, usually extract\.</param>
		/// <returns>The lines, sorted by path so two records of the same tree compare line for line.</returns>
		public static List<string> Record(string folder) {
			List<string> lines = new();
			foreach (string file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)) {
				string relative = Path.GetRelativePath(folder, file);
				lines.Add(HashFile(file) + "  " + relative);
			}
			lines.Sort(StringComparer.Ordinal);
			return lines;
		}


		/// <summary>
		/// Writes the record for a checkpoint's extract\ folder.
		/// </summary>
		/// <param name="checkpointFolder">The checkpoint's folder.</param>
		/// <returns>How many files were recorded.</returns>
		public static int Write(string checkpointFolder) {
			string extract = Path.Combine(checkpointFolder, CheckpointInspector.ExtractFolder);
			List<string> lines = Record(extract);
			File.WriteAllLines(Path.Combine(checkpointFolder, FileName), lines, new UTF8Encoding(false));
			return lines.Count;
		}


		/// <summary>
		/// The files under extract\ whose hash differs from the record, or are not in it, or
		/// are in it but gone. Empty when the sources are exactly as last compiled, or when
		/// there is no record yet.
		/// </summary>
		/// <param name="checkpointFolder">The checkpoint's folder.</param>
		/// <param name="hasRecord">False when no record exists; the result is then empty and means nothing.</param>
		/// <returns>The relative paths that changed.</returns>
		public static List<string> Changed(string checkpointFolder, out bool hasRecord) {
			List<string> changed = new();
			string recordPath = Path.Combine(checkpointFolder, FileName);
			hasRecord = File.Exists(recordPath);
			if (hasRecord == true) {
				Dictionary<string, string> recorded = new(StringComparer.OrdinalIgnoreCase);
				foreach (string line in File.ReadAllLines(recordPath)) {
					int gap = line.IndexOf("  ");
					if (gap > 0) {
						recorded[line.Substring(gap + 2)] = line.Substring(0, gap);
					}
				}
				string extract = Path.Combine(checkpointFolder, CheckpointInspector.ExtractFolder);
				Dictionary<string, string> now = new(StringComparer.OrdinalIgnoreCase);
				if (Directory.Exists(extract) == true) {
					foreach (string line in Record(extract)) {
						int gap = line.IndexOf("  ");
						now[line.Substring(gap + 2)] = line.Substring(0, gap);
					}
				}
				foreach (KeyValuePair<string, string> entry in now) {
					bool same = recorded.TryGetValue(entry.Key, out string? was) == true && was == entry.Value;
					if (same == false) {
						changed.Add(entry.Key);
					}
				}
				foreach (string path in recorded.Keys) {
					if (now.ContainsKey(path) == false) {
						changed.Add(path);
					}
				}
				changed.Sort(StringComparer.Ordinal);
			}
			return changed;
		}


		private static string HashFile(string path) {
			byte[] digest;
			using (FileStream stream = File.OpenRead(path)) {
				digest = SHA256.HashData(stream);
			}
			StringBuilder hex = new(64);
			foreach (byte current in digest) {
				hex.Append(current.ToString("x2"));
			}
			return hex.ToString();
		}
	}
}
