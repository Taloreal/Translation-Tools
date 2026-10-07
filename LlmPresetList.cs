// File: LlmPresetList.cs
// Namespace: TranslationTools
using TALOREAL_NETCORE_API;

namespace TranslationTools {

	/// <summary>
	/// The language-model presets this machine keeps, as one list in Settings. A preset
	/// is found by its name, ignoring case.
	/// </summary>
	public static class LlmPresetList {

		private const string StoreName = "LlmPresets";

		private static readonly AutoListSetting<string> Store = new(StoreName);


		/// <summary>
		/// Every stored preset, in stored order. An entry that cannot be read is skipped.
		/// </summary>
		public static List<LlmPreset> All() {
			List<LlmPreset> presets = new();
			foreach (string encoded in Store.Value) {
				if (LlmPreset.TryDecode(encoded, out LlmPreset preset) == true) {
					presets.Add(preset);
				}
			}
			return presets;
		}


		/// <summary>
		/// The preset with a name, or null.
		/// </summary>
		public static LlmPreset? Find(string name) {
			LlmPreset? found = null;
			foreach (LlmPreset preset in All()) {
				if (found == null && LlmPreset.SameName(preset.Name, name) == true) {
					found = preset;
				}
			}
			return found;
		}


		/// <summary>
		/// Stores a preset, replacing the one with the same name if there is one.
		/// </summary>
		/// <returns>True when stored; false when its name is blank.</returns>
		public static bool Put(LlmPreset preset) {
			bool stored = false;
			if (preset.Name.Trim().Length > 0) {
				List<LlmPreset> all = All();
				int index = IndexOf(all, preset.Name);
				if (index >= 0) {
					all[index] = preset;
				}
				if (index < 0) {
					all.Add(preset);
				}
				Save(all);
				stored = true;
			}
			return stored;
		}


		/// <summary>
		/// Removes the preset with a name.
		/// </summary>
		/// <returns>True when removed; false when no preset had the name.</returns>
		public static bool Remove(string name) {
			bool removed = false;
			List<LlmPreset> all = All();
			int index = IndexOf(all, name);
			if (index >= 0) {
				all.RemoveAt(index);
				Save(all);
				removed = true;
			}
			return removed;
		}


		private static int IndexOf(List<LlmPreset> all, string name) {
			int found = -1;
			for (int index = 0; index < all.Count; index++) {
				if (found < 0 && LlmPreset.SameName(all[index].Name, name) == true) {
					found = index;
				}
			}
			return found;
		}


		private static void Save(List<LlmPreset> all) {
			string[] encoded = new string[all.Count];
			for (int index = 0; index < all.Count; index++) {
				encoded[index] = all[index].Encode();
			}
			Store.Value = encoded;
		}
	}
}
