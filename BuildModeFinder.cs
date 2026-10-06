// File: BuildModeFinder.cs
// Namespace: TranslationTools
namespace TranslationTools {

	/// <summary>
	/// How a Siglus archive's build mode is found, in order of cost: a known archive whose
	/// hash set matches, then the service by whole-file fingerprint, then the compiler's own
	/// round-trip. One place for it, used by Extract and by Build when a mode is missing.
	/// </summary>
	public static class BuildModeFinder {

		/// <summary>What was found about an archive.</summary>
		public class Finding {
			public int BuildMode = Checkpoint.NoBuildMode;

			/// <summary>True when the compiler's round-trip proved the mode here, so it may be reported.</summary>
			public bool VerifiedHere = false;

			/// <summary>Where the mode came from, for the screen: "a known archive", "the service", "the compiler", or "" when not found.</summary>
			public string Source = "";

			public string Fingerprint = "";
			public string[] HashSet = new string[0];
			public long SizeBytes = 0;

			/// <summary>The known game the archive matched, when it did.</summary>
			public KnownGame? MatchedGame = null;
		}


		/// <summary>
		/// Finds the build mode for an archive. The archive is only read. The round-trip, when
		/// it comes to that, takes minutes and streams through onLine.
		/// </summary>
		/// <param name="archivePath">A Scene.pck.</param>
		/// <param name="onLine">Receives progress and the compiler's output.</param>
		/// <returns>The finding; BuildMode is NoBuildMode when nothing reproduced the archive.</returns>
		public static Finding Find(string archivePath, Action<string> onLine) {
			Finding finding = new();
			onLine("Reading the archive...");
			finding.Fingerprint = BuildModeService.Fingerprint(archivePath);
			finding.HashSet = BuildModeService.SegmentHashes(archivePath);
			finding.SizeBytes = new FileInfo(archivePath).Length;

			KnownArchive? matched = BuildModeService.FindKnownArchiveByHashSet(finding.HashSet, out KnownGame? game, out int shared);
			if (matched == null) {
				matched = BuildModeService.FindKnownArchive(finding.Fingerprint, out game);
			}
			finding.MatchedGame = game;
			if (matched != null && matched.Trusted == true) {
				finding.BuildMode = matched.BuildMode;
				finding.Source = "a known archive";
				onLine("Build mode known for this archive.");
			}

			if (finding.BuildMode == Checkpoint.NoBuildMode) {
				BuildModeLookup lookup = BuildModeService.Lookup(finding.Fingerprint);
				if (lookup.Trusted == true) {
					finding.BuildMode = lookup.BuildMode;
					finding.Source = "the service";
					onLine("Build mode known to the service.");
				}
			}

			if (finding.BuildMode == Checkpoint.NoBuildMode && SiglusCompiler.Available == true) {
				onLine("Letting the compiler find the build mode by rebuilding the archive. This takes a while.");
				bool reproduced = SiglusCompiler.FindBuildMode(archivePath, onLine, out int mode);
				if (reproduced == true) {
					finding.BuildMode = mode;
					finding.VerifiedHere = true;
					finding.Source = "the compiler";
				}
			}
			return finding;
		}


		/// <summary>
		/// Sends a finding the compiler verified here to the service, if the user allows
		/// reporting. Findings that came from the service or a known archive are not re-reported.
		/// </summary>
		/// <param name="finding">The finding.</param>
		/// <param name="gameName">The game as the user named it.</param>
		/// <param name="vndbId">VNDB's id for it, or empty.</param>
		/// <param name="onLine">Receives the outcome.</param>
		public static void ReportIfVerified(Finding finding, string gameName, string vndbId, Action<string> onLine) {
			if (finding.VerifiedHere == true && SettingsMenu.EnsureReportingDecided() == true) {
				BuildModeReport report = BuildModeService.Report(finding.Fingerprint, finding.SizeBytes, gameName, finding.BuildMode,
					SiglusCompiler.Version(), BuildModeService.OutcomeVerified, finding.HashSet, vndbId);
				if (report.Accepted == true) {
					onLine("Reported to the build-mode service; the next person with this archive skips the wait.");
				}
				if (report.Accepted == false) {
					onLine("Could not report: " + report.Error);
				}
			}
		}
	}
}
