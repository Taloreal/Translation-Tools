// File: LearningSettings.cs
// Namespace: TranslationTools
using TALOREAL_NETCORE_API;

namespace TranslationTools {

	/// <summary>
	/// The numbers the standalone name-line learner stops on. The learner walks the script's
	/// repeated lines from the most frequent down, asking the model whether each is a name;
	/// names sit at the top of that list, so once enough lines have been considered and the
	/// share of names among them falls under the bar, the cast is exhausted and it stops.
	/// A minimum of thirty-three rather than a cast's size, because a game's habits - three
	/// beat lines of dots at the top of TGD's list - sit among the names and must not end the
	/// walk before the cast has been reached; a bar of ninety, because the share falls under
	/// it within a handful of lines once the names run out, so no window is needed.
	/// </summary>
	public static class LearningSettings {

		private const string MinimumKey = "Learning.MinimumLines";
		private const string PercentKey = "Learning.StopPercent";

		/// <summary>The most either number may be set to.</summary>
		public const int Most = 1000;


		/// <summary>How many unique repeated lines are considered before the stop rule may end the walk; 33 when nothing is set.</summary>
		public static int MinimumLines {
			get { return Read(MinimumKey, 33); }
			set { Settings.SetValue(MinimumKey, Math.Clamp(value, 1, Most)); }
		}

		/// <summary>The share of names, in percent, under which the walk stops once the minimum is reached; 90 when nothing is set.</summary>
		public static int StopPercent {
			get { return Read(PercentKey, 90); }
			set { Settings.SetValue(PercentKey, Math.Clamp(value, 1, 100)); }
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
