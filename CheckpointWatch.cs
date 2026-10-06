// File: CheckpointWatch.cs
// Namespace: TranslationTools
namespace TranslationTools {

	/// <summary>
	/// Keeps the on-disk state of the ONE checkpoint in focus. The folder is probed once
	/// when it comes into focus, then a single watcher on its top level marks the state
	/// stale when something there changes; the next read probes again. Nothing is polled,
	/// and a draw never touches the disk. When a watcher cannot be made (a network or
	/// removable path, say) the state simply stands until the checkpoint is refocused.
	/// </summary>
	public static class CheckpointWatch {

		private static string FocusedLabel = "";
		private static string FocusedPath = "";
		private static CheckpointState Cached = new();
		private static bool Stale = false;
		private static readonly List<FileSystemWatcher> Watchers = new();

		/// <summary>
		/// True while the tool itself is changing the focused checkpoint, so the burst of
		/// events that makes is not taken as a change worth re-probing for. The operation
		/// marks the state stale itself when it is done.
		/// </summary>
		public static bool Ignoring { get; set; } = false;

		/// <summary>
		/// The focused checkpoint's state, re-probed only if a watcher reported a change. A
		/// re-probe also re-creates the watchers, since a change may have added or removed
		/// one of the folders they sit on.
		/// </summary>
		public static CheckpointState State {
			get {
				if (Stale == true) {
					Stale = false;
					Cached = CheckpointInspector.Inspect(FocusedPath);
					Release();
					StartWatchers(FocusedPath);
					Gate();
				}
				return Cached;
			}
		}


		/// <summary>
		/// Makes a checkpoint the one in focus. A checkpoint already in focus at the same path
		/// costs nothing; a different one drops the old watcher, probes once and watches anew.
		/// </summary>
		/// <param name="checkpoint">The checkpoint to focus, or null to focus nothing.</param>
		public static void Focus(Checkpoint? checkpoint) {
			if (checkpoint == null) {
				Release();
				FocusedLabel = "";
				FocusedPath = "";
				Cached = new CheckpointState();
			}
			if (checkpoint != null) {
				bool same = CheckpointList.SameLabel(FocusedLabel, checkpoint.Label) == true
					&& string.Equals(FocusedPath, checkpoint.Path, StringComparison.OrdinalIgnoreCase) == true;
				if (same == false) {
					Release();
					FocusedLabel = checkpoint.Label;
					FocusedPath = checkpoint.Path;
					Cached = CheckpointInspector.Inspect(FocusedPath);
					Stale = false;
					StartWatchers(FocusedPath);
					Gate();
				}
			}
		}


		/// <summary>
		/// Locks the focused checkpoint if its freshly probed state says it needs recovery.
		/// </summary>
		private static void Gate() {
			Checkpoint? checkpoint = CheckpointList.Find(FocusedLabel);
			if (checkpoint != null) {
				RecoveryGate.Enforce(checkpoint, Cached);
			}
		}


		/// <summary>
		/// Forces one more probe on the next read, for after an action the tool itself ran on
		/// the focused checkpoint.
		/// </summary>
		public static void MarkStale() {
			Stale = true;
		}


		/// <summary>
		/// Stops watching. Called when focus moves and at exit.
		/// </summary>
		public static void Release() {
			foreach (FileSystemWatcher watcher in Watchers) {
				watcher.EnableRaisingEvents = false;
				watcher.Dispose();
			}
			Watchers.Clear();
		}


		/// <summary>
		/// Watches the container's top level and, when they exist, its extract\ and split\
		/// folders - the three levels the state is read from. backups\ is never watched. A
		/// folder a watcher cannot be made on is skipped; the state then stands until the
		/// checkpoint is refocused.
		/// </summary>
		private static void StartWatchers(string path) {
			string folder = CheckpointInspector.FolderOf(path);
			if (folder.Length > 0 && Directory.Exists(folder) == true) {
				StartOneWatcher(folder);
				StartOneWatcher(Path.Combine(folder, CheckpointInspector.ExtractFolder));
				StartOneWatcher(Path.Combine(folder, CheckpointInspector.SplitFolder));
			}
		}


		private static void StartOneWatcher(string folder) {
			if (Directory.Exists(folder) == true) {
				try {
					FileSystemWatcher watcher = new(folder, "*");
					watcher.IncludeSubdirectories = false;
					watcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName;
					watcher.Created += OnChanged;
					watcher.Deleted += OnChanged;
					watcher.Renamed += OnRenamed;
					watcher.Error += OnError;
					watcher.EnableRaisingEvents = true;
					Watchers.Add(watcher);
				}
				catch (Exception) {
					// No watcher on this folder; the next focus probes again.
				}
			}
		}


		private static void OnChanged(object sender, FileSystemEventArgs arguments) {
			if (Ignoring == false) {
				Stale = true;
			}
		}


		private static void OnRenamed(object sender, RenamedEventArgs arguments) {
			if (Ignoring == false) {
				Stale = true;
			}
		}


		/// <summary>
		/// The watcher's buffer overflowed during a burst of changes. Whatever happened, one
		/// probe catches up.
		/// </summary>
		private static void OnError(object sender, ErrorEventArgs arguments) {
			Stale = true;
		}
	}
}
