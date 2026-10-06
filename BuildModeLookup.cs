// File: BuildModeLookup.cs
// Namespace: TranslationTools
namespace TranslationTools {

	/// <summary>
	/// What the build-mode service knows about one archive. Reached is false when the
	/// service could not be asked at all; Found is false when it was asked and had no row.
	/// </summary>
	public class BuildModeLookup {

		public bool Reached;
		public bool Found;
		public string GameName = "";
		public int BuildMode;
		public string CompilerVersion = "";
		public int Reports;
		public int Disputes;

		/// <summary>A plain sentence when Reached is false, otherwise empty.</summary>
		public string Error = "";

		/// <summary>
		/// True when the service's answer is one the tool should act on without running
		/// its own round-trip: found, and not disputed as often as it was confirmed.
		/// </summary>
		public bool Trusted {
			get { return Reached == true && Found == true && Disputes < Reports; }
		}
	}
}
