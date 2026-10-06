// File: GameInstallList.cs
// Namespace: TranslationTools
using TALOREAL_NETCORE_API;

namespace TranslationTools {

	/// <summary>
	/// The installed games this machine knows, kept as one list in Settings. A checkpoint
	/// points at one by its folder; the install holds the launcher and its arguments, so a
	/// game installed once serves every checkpoint of it.
	/// </summary>
	public static class GameInstallList {

		private const string StoreName = "GameInstalls";

		private static readonly AutoListSetting<string> Store = new(StoreName);


		/// <summary>
		/// Every stored install, in stored order. An entry that cannot be read is skipped.
		/// </summary>
		public static List<GameInstall> All() {
			List<GameInstall> installs = new();
			foreach (string encoded in Store.Value) {
				if (GameInstall.TryDecode(encoded, out GameInstall install) == true) {
					installs.Add(install);
				}
			}
			return installs;
		}


		/// <summary>
		/// The install at a folder, or null.
		/// </summary>
		public static GameInstall? Find(string folder) {
			GameInstall? found = null;
			foreach (GameInstall install in All()) {
				if (found == null && GameInstall.SameFolder(install.Folder, folder) == true) {
					found = install;
				}
			}
			return found;
		}


		/// <summary>
		/// Stores an install, replacing the one at the same folder if there is one.
		/// </summary>
		/// <param name="install">The install to store.</param>
		/// <returns>True when stored; false when its folder is empty.</returns>
		public static bool Put(GameInstall install) {
			bool stored = false;
			if (install.Folder.Length > 0) {
				List<GameInstall> all = All();
				int index = IndexOf(all, install.Folder);
				if (index >= 0) {
					all[index] = install;
				}
				if (index < 0) {
					all.Add(install);
				}
				Save(all);
				stored = true;
			}
			return stored;
		}


		/// <summary>
		/// Removes the install at a folder, and clears the pointer on every checkpoint that
		/// used it, so their next Run asks again.
		/// </summary>
		/// <param name="folder">The install's folder.</param>
		/// <returns>True when removed; false when no install had the folder.</returns>
		public static bool Remove(string folder) {
			bool removed = false;
			List<GameInstall> all = All();
			int index = IndexOf(all, folder);
			if (index >= 0) {
				all.RemoveAt(index);
				Save(all);
				foreach (Checkpoint checkpoint in CheckpointList.All()) {
					if (GameInstall.SameFolder(checkpoint.GameFolder, folder) == true) {
						checkpoint.GameFolder = "";
						CheckpointList.Update(checkpoint.Label, checkpoint);
					}
				}
				removed = true;
			}
			return removed;
		}


		private static int IndexOf(List<GameInstall> all, string folder) {
			int found = -1;
			for (int index = 0; index < all.Count; index++) {
				if (found < 0 && GameInstall.SameFolder(all[index].Folder, folder) == true) {
					found = index;
				}
			}
			return found;
		}


		private static void Save(List<GameInstall> all) {
			string[] encoded = new string[all.Count];
			for (int index = 0; index < all.Count; index++) {
				encoded[index] = all[index].Encode();
			}
			Store.Value = encoded;
		}
	}
}
