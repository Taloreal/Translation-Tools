// File: BuildModeReport.cs
// Namespace: TranslationTools
namespace TranslationTools {

	/// <summary>
	/// The service's answer to one report. Accepted is false when it was refused or
	/// could not be reached; Error then says why in a plain sentence.
	/// </summary>
	public class BuildModeReport {

		public bool Accepted;
		public int Reports;
		public int Disputes;
		public string Error = "";
	}
}
