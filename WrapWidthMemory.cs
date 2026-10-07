// File: WrapWidthMemory.cs
// Namespace: TranslationTools
using TALOREAL_NETCORE_API;

namespace TranslationTools {

	/// <summary>
	/// Remembers the governing wrap width chosen for an archive, keyed by the archive's hash
	/// set - the twenty slice hashes the build-mode service matches on. A master that shares
	/// eighteen or more slices with a remembered one is the same game, patched or translated,
	/// and gets the same width without being asked. Kept as one list in Settings.
	/// </summary>
	public static class WrapWidthMemory {

		private const string StoreName = "WrapWidths";
		private const string ColumnName = "Column";
		private const string HashesName = "Hashes";

		private static readonly AutoListSetting<string> Store = new(StoreName);


		/// <summary>
		/// The width remembered for an archive like this one, if any.
		/// </summary>
		/// <param name="hashSet">The master's hash set, from BuildModeService.SegmentHashes.</param>
		/// <param name="column">The remembered width; NoWrapColumn when none is remembered.</param>
		/// <returns>True when a remembered archive shares enough slices.</returns>
		public static bool Find(string[] hashSet, out int column) {
			column = Checkpoint.NoWrapColumn;
			int best = 0;
			foreach (string encoded in Store.Value) {
				if (TryDecode(encoded, out string[] hashes, out int remembered) == true) {
					int shared = BuildModeService.SharedSlices(hashSet, hashes);
					if (shared >= BuildModeService.SegmentMatchesNeeded && shared > best) {
						best = shared;
						column = remembered;
					}
				}
			}
			return best > 0;
		}


		/// <summary>
		/// Remembers a width for an archive. An entry for an archive like this one is
		/// replaced, so the newest choice for a game wins.
		/// </summary>
		/// <param name="hashSet">The master's hash set.</param>
		/// <param name="column">The width chosen; NoWrapColumn to remember "none".</param>
		public static void Record(string[] hashSet, int column) {
			if (hashSet.Length == BuildModeService.SegmentCount) {
				List<string> kept = new();
				foreach (string encoded in Store.Value) {
					bool alike = TryDecode(encoded, out string[] hashes, out int remembered) == true
						&& BuildModeService.SharedSlices(hashSet, hashes) >= BuildModeService.SegmentMatchesNeeded;
					if (alike == false) {
						kept.Add(encoded);
					}
				}
				kept.Add(Encode(hashSet, column));
				Store.Value = kept.ToArray();
			}
		}


		private static string Encode(string[] hashSet, int column) {
			return ColumnName + "=" + column + "\n" + HashesName + "=" + string.Join(",", hashSet);
		}


		private static bool TryDecode(string encoded, out string[] hashSet, out int column) {
			hashSet = new string[0];
			column = Checkpoint.NoWrapColumn;
			bool hasHashes = false;
			foreach (string line in encoded.Split('\n')) {
				int equals = line.IndexOf('=');
				if (equals > 0) {
					string name = line.Substring(0, equals);
					string value = line.Substring(equals + 1);
					if (name == ColumnName && int.TryParse(value, out int parsed) == true && parsed >= 0) {
						column = parsed;
					}
					if (name == HashesName && value.Length > 0) {
						hashSet = value.Split(',');
						hasHashes = true;
					}
				}
			}
			return hasHashes;
		}
	}
}
