// File: TgdFeatures.cs
// Namespace: TranslationTools
using System.Diagnostics;

using TALOREAL_NETCORE_API;

namespace TranslationTools {

	/// <summary>
	/// The features that lean on TGD's own script structure (the nametag anchor, the voice
	/// cues, the nametag cross-check) and so cannot be supported for other games. They exist
	/// only when the tool runs under a debugger, and even then they are a switch in Settings,
	/// so the general path can be tested from Visual Studio too. Everything that shows them
	/// is labelled "TGD only".
	/// </summary>
	public static class TgdFeatures {

		private const string EnabledKey = "Tgd.Enabled";

		/// <summary>The label every TGD-dependent item carries.</summary>
		public const string Label = "TGD only";

		/// <summary>Whether the features exist at all this run: the tool was started under a debugger.</summary>
		public static bool Available {
			get { return Debugger.IsAttached; }
		}

		/// <summary>The switch, remembered on this machine. On by default, since the debugger is the gate.</summary>
		public static bool Wanted {
			get {
				bool wanted = true;
				bool stored = Settings.GetValue(EnabledKey, out bool saved);
				if (stored == true) {
					wanted = saved;
				}
				return wanted;
			}
			set { Settings.SetValue(EnabledKey, value); }
		}

		/// <summary>Whether the features act: available this run and switched on.</summary>
		public static bool Enabled {
			get { return Available == true && Wanted == true; }
		}
	}
}
