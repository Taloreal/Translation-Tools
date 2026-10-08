// File: AlignmentSettings.cs
// Namespace: TranslationTools
using TALOREAL_NETCORE_API;

namespace TranslationTools {

	/// <summary>
	/// The alignment walk's own per-machine settings, changed from the Alignment menu: how
	/// far the speaker order is compared around a pair, and when matching order plus
	/// agreeing audio pair lines without asking anyone.
	/// </summary>
	public static class AlignmentSettings {

		private const string SpeakerWindowKey = "Alignment.SpeakerWindow";
		private const string AutoPairRunKey = "Alignment.AutoPairRun";
		private const string AutoPairClipsKey = "Alignment.AutoPairClips";
		private const string ShortLineNeighboursKey = "Alignment.ShortLineNeighbours";
		private const string LearnSpeakerLinesKey = "Alignment.LearnSpeakerLines";
		private const string ModelPairRunKey = "Alignment.ModelPairRun";

		/// <summary>The most lines any of these settings may count.</summary>
		public const int MostLines = 20;

		/// <summary>
		/// How many lines on each side of a pair the walk compares the speaker order over.
		/// When the order matches but the words differ, the line was probably replaced in one
		/// version, and the model is told so. 0 turns the check off.
		/// </summary>
		public static int SpeakerWindow {
			get { return Math.Clamp(Read(SpeakerWindowKey, 3), 0, MostLines); }
			set { Settings.SetValue(SpeakerWindowKey, Math.Clamp(value, 0, MostLines)); }
		}

		/// <summary>
		/// How many lines of matching speaker order, the current pair included, with the audio
		/// agreeing wherever a line in that run is voiced, pair the current lines WITHOUT
		/// asking the model or the user. 0 turns it off.
		/// </summary>
		public static int AutoPairRun {
			get { return Math.Clamp(Read(AutoPairRunKey, 10), 0, MostLines); }
			set { Settings.SetValue(AutoPairRunKey, Math.Clamp(value, 0, MostLines)); }
		}

		/// <summary>
		/// The fewest lines in that matched run that must share a voice clip on both sides
		/// before the pair is made without asking.
		/// </summary>
		public static int AutoPairClips {
			get { return Math.Clamp(Read(AutoPairClipsKey, 2), 1, MostLines); }
			set { Settings.SetValue(AutoPairClipsKey, Math.Clamp(value, 1, MostLines)); }
		}

		/// <summary>
		/// How many more lines of matching speaker order, around a pair whose text is identical
		/// but too short for the plain text match, let it pair on its own: a short line repeats
		/// too often to trust by its words alone.
		/// </summary>
		public static int ShortLineNeighbours {
			get { return Math.Clamp(Read(ShortLineNeighboursKey, 2), 1, MostLines); }
			set { Settings.SetValue(ShortLineNeighboursKey, Math.Clamp(value, 1, MostLines)); }
		}

		/// <summary>
		/// How many lines in a row whose words match but whose speaker tags differ, one of them
		/// Japanese, make the tool offer to learn the two tags as one character in the glossary.
		/// </summary>
		public static int LearnSpeakerLines {
			get { return Math.Clamp(Read(LearnSpeakerLinesKey, 2), 1, MostLines); }
			set { Settings.SetValue(LearnSpeakerLinesKey, Math.Clamp(value, 1, MostLines)); }
		}

		/// <summary>
		/// How many lines of matching speaker order, the current pair included, together with the
		/// model saying SAME and the audio agreeing on at least the fewest-shared-clips count in
		/// that run, pair the current lines without asking the user. 0 turns it off.
		/// </summary>
		public static int ModelPairRun {
			get { return Math.Clamp(Read(ModelPairRunKey, 5), 0, MostLines); }
			set { Settings.SetValue(ModelPairRunKey, Math.Clamp(value, 0, MostLines)); }
		}


		private static int Read(string key, int fallback) {
			int value = fallback;
			bool stored = Settings.GetValue(key, out int saved);
			if (stored == true) {
				value = saved;
			}
			return value;
		}
	}
}
