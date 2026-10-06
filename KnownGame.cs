// File: KnownGame.cs
// Namespace: TranslationTools
namespace TranslationTools {

	/// <summary>
	/// One game the build-mode service knows, with every archive it has seen for it.
	/// </summary>
	public class KnownGame {

		public string GameName = "";

		/// <summary>VNDB's id for the game, e.g. "v751"; empty when it was never canonized.</summary>
		public string VndbId = "";

		public List<KnownArchive> Archives = new();
	}

	/// <summary>
	/// One archive the service has seen: its fingerprint and the build mode found for it.
	/// </summary>
	public class KnownArchive {

		public string Fingerprint = "";

		/// <summary>One SHA-256 per 5% slice of the archive, 20 in all; empty when never reported.</summary>
		public string[] SegmentHashes = new string[0];

		public long SizeBytes;
		public int BuildMode;
		public string CompilerVersion = "";
		public int Reports;
		public int Disputes;

		/// <summary>True when confirmed more often than disputed.</summary>
		public bool Trusted {
			get { return Disputes < Reports; }
		}
	}
}
