// File: BuildModeService.cs
// Namespace: TranslationTools
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using TALOREAL_NETCORE_API;

namespace TranslationTools {

	/// <summary>
	/// The tool's side of the build-mode lookup service (Web\ in this repo). Fingerprints
	/// an archive, asks the service which build mode it needs, and reports what a
	/// round-trip or the user found out. Reports are signed with Secrets.ReportKey, which
	/// never travels: the signature covers exactly the fields sent, so a signature is good
	/// for one report and nothing else. Reporting happens only after the user has said yes
	/// once; that answer is remembered in Settings.
	/// </summary>
	public static class BuildModeService {

		private const string BaseUrlKey = "BuildModeService.BaseUrl";
		private const string ReportingAllowedKey = "BuildModeService.ReportingAllowed";
		private const string ReportingAskedKey = "BuildModeService.ReportingAsked";
		private const string GamesCacheKey = "BuildModeService.GamesCache";

		/// <summary>The games the service knows, from the last successful fetch or the cache.</summary>
		public static readonly List<KnownGame> KnownGames = new();

		/// <summary>Where KnownGames came from: "service", "cache", or "none". Shown to the user.</summary>
		public static string KnownGamesSource = "none";

		/// <summary>The outcome word for a report whose round-trip reproduced the archive.</summary>
		public const string OutcomeVerified = "verified";

		/// <summary>The outcome word for a report that the game did not run with this mode.</summary>
		public const string OutcomeFailed = "failed";

		/// <summary>How many slices an archive is cut into for its hash set.</summary>
		public const int SegmentCount = 20;

		/// <summary>How many of the 20 slices must match for two archives to count as the same game.</summary>
		public const int SegmentMatchesNeeded = 18;

		private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(30) };

		/// <summary>Where the service lives: Secrets.ServiceUrl unless a Settings override is set.</summary>
		public static string BaseUrl {
			get {
				string url = Secrets.ServiceUrl;
				bool stored = Settings.GetValue(BaseUrlKey, out string? saved);
				if (stored == true && saved != null && saved.Trim().Length > 0) {
					url = saved.Trim();
				}
				return url.TrimEnd('/');
			}
			set { Settings.SetValue(BaseUrlKey, value.Trim()); }
		}

		/// <summary>Whether the user has been asked about reporting yet.</summary>
		public static bool ReportingAsked {
			get {
				Settings.GetValue(ReportingAskedKey, out bool asked);
				return asked;
			}
		}

		/// <summary>The user's remembered answer. False until they say yes.</summary>
		public static bool ReportingAllowed {
			get {
				Settings.GetValue(ReportingAllowedKey, out bool allowed);
				return allowed;
			}
			set {
				Settings.SetValue(ReportingAllowedKey, value);
				Settings.SetValue(ReportingAskedKey, true);
			}
		}


		/// <summary>
		/// Fetches every game the service knows into KnownGames and caches the reply in
		/// Settings. When the service cannot be reached the cache is used instead, and when
		/// there is no cache the list is empty. Never throws: startup must not depend on the
		/// network.
		/// </summary>
		/// <returns>Empty when the service answered, otherwise a plain sentence saying what was used instead.</returns>
		public static string RefreshKnownGames() {
			string problem = "";
			string body = "";
			string fetched = Fetch(BaseUrl + "/games.php", null, null, out body);

			bool loaded = false;
			if (fetched.Length == 0) {
				loaded = ReadGames(body);
				if (loaded == true) {
					KnownGamesSource = "service";
					Settings.SetValue(GamesCacheKey, body);
				}
			}
			if (loaded == false) {
				string reason = fetched;
				if (reason.Length == 0) {
					reason = "The service's game list was not understood.";
				}
				bool cached = Settings.GetValue(GamesCacheKey, out string? saved);
				bool usedCache = cached == true && saved != null && ReadGames(saved) == true;
				if (usedCache == true) {
					KnownGamesSource = "cache";
					problem = reason + " Using the game list from last time.";
				}
				if (usedCache == false) {
					KnownGamesSource = "none";
					KnownGames.Clear();
					problem = reason + " No game list is available.";
				}
			}
			return problem;
		}


		/// <summary>
		/// The known archive whose hash set shares at least SegmentMatchesNeeded slices with
		/// a hash set, from KnownGames - the one with the most matches when several do. This
		/// is how a patched or translated copy of a known game is recognised.
		/// </summary>
		/// <param name="segmentHashes">The archive's hash set, from SegmentHashes.</param>
		/// <param name="game">The game the best match belongs to, when found.</param>
		/// <param name="matches">How many slices the best match shared; 0 when none qualified.</param>
		/// <returns>The best matching archive, or null.</returns>
		public static KnownArchive? FindKnownArchiveByHashSet(string[] segmentHashes, out KnownGame? game, out int matches) {
			KnownArchive? best = null;
			game = null;
			matches = 0;
			foreach (KnownGame candidate in KnownGames) {
				foreach (KnownArchive archive in candidate.Archives) {
					int shared = SharedSlices(segmentHashes, archive.SegmentHashes);
					if (shared >= SegmentMatchesNeeded && shared > matches) {
						best = archive;
						game = candidate;
						matches = shared;
					}
				}
			}
			return best;
		}


		/// <summary>
		/// How many slices two hash sets agree on, position by position. Sets of different
		/// lengths share nothing: their slices do not line up.
		/// </summary>
		public static int SharedSlices(string[] first, string[] second) {
			int shared = 0;
			if (first.Length == second.Length) {
				for (int index = 0; index < first.Length; index++) {
					if (first[index] == second[index]) {
						shared += 1;
					}
				}
			}
			return shared;
		}


		/// <summary>
		/// The archive the service knows under a fingerprint, from KnownGames, or null.
		/// </summary>
		/// <param name="fingerprint">The archive's fingerprint.</param>
		/// <param name="game">The game the archive belongs to, when found.</param>
		/// <returns>The archive, or null when no known game has it.</returns>
		public static KnownArchive? FindKnownArchive(string fingerprint, out KnownGame? game) {
			KnownArchive? found = null;
			game = null;
			foreach (KnownGame candidate in KnownGames) {
				foreach (KnownArchive archive in candidate.Archives) {
					if (found == null && archive.Fingerprint == fingerprint) {
						found = archive;
						game = candidate;
					}
				}
			}
			return found;
		}


		/// <summary>
		/// The archive's identity for the service: its SHA-256 as 64 lowercase hex characters.
		/// </summary>
		/// <param name="archivePath">The archive file.</param>
		/// <returns>The fingerprint.</returns>
		public static string Fingerprint(string archivePath) {
			byte[] digest;
			using (FileStream stream = File.OpenRead(archivePath)) {
				digest = SHA256.HashData(stream);
			}
			return ToLowerHex(digest);
		}


		/// <summary>
		/// The archive's hash set: the file cut into 20 slices of 5% each (the last takes
		/// the remainder), each slice hashed with SHA-256. Two copies of a game that differ
		/// only where a patch or translation touched them still agree on most slices.
		/// </summary>
		/// <param name="archivePath">The archive file.</param>
		/// <returns>20 lowercase hex hashes, in slice order.</returns>
		public static string[] SegmentHashes(string archivePath) {
			string[] hashes = new string[SegmentCount];
			using (FileStream stream = File.OpenRead(archivePath)) {
				long length = stream.Length;
				long sliceLength = length / SegmentCount;
				for (int index = 0; index < SegmentCount; index++) {
					long start = sliceLength * index;
					long count = sliceLength;
					if (index == SegmentCount - 1) {
						count = length - start;
					}
					hashes[index] = HashSlice(stream, start, count);
				}
			}
			return hashes;
		}


		/// <summary>
		/// The hash set as report.php stores it: the 20 hashes joined by commas.
		/// </summary>
		public static string JoinHashSet(string[] segmentHashes) {
			return string.Join(",", segmentHashes);
		}


		private static string HashSlice(FileStream stream, long start, long count) {
			stream.Position = start;
			byte[] buffer = new byte[81920];
			long remaining = count;
			using (IncrementalHash hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256)) {
				while (remaining > 0) {
					int wanted = (int)Math.Min(buffer.Length, remaining);
					int read = stream.Read(buffer, 0, wanted);
					if (read <= 0) {
						remaining = 0;
					}
					if (read > 0) {
						hasher.AppendData(buffer, 0, read);
						remaining -= read;
					}
				}
				return ToLowerHex(hasher.GetHashAndReset());
			}
		}


		/// <summary>
		/// Asks the service which build mode an archive needs.
		/// </summary>
		/// <param name="fingerprint">The archive's fingerprint, from Fingerprint.</param>
		/// <returns>What the service knows, or Reached = false with the reason.</returns>
		public static BuildModeLookup Lookup(string fingerprint) {
			BuildModeLookup answer = new();
			string url = BaseUrl + "/lookup.php?fingerprint=" + fingerprint;
			string body = "";
			string problem = Fetch(url, null, null, out body);

			if (problem.Length == 0) {
				problem = ReadLookup(body, answer);
			}
			if (problem.Length > 0) {
				answer.Reached = false;
				answer.Error = problem;
			}
			return answer;
		}


		/// <summary>
		/// Tells the service what was found out about an archive. Refuses locally, without
		/// sending anything, when the user has not allowed reporting or no key is compiled in.
		/// </summary>
		/// <param name="fingerprint">The archive's fingerprint.</param>
		/// <param name="sizeBytes">The archive's size.</param>
		/// <param name="gameName">What the user called the game.</param>
		/// <param name="buildMode">The mode number.</param>
		/// <param name="compilerVersion">The compiler's version line, e.g. "siglus-ssu 0.5.4".</param>
		/// <param name="outcome">OutcomeVerified or OutcomeFailed.</param>
		/// <param name="segmentHashes">The archive's hash set, from SegmentHashes; an empty array when not known.</param>
		/// <param name="vndbId">VNDB's id for the game, e.g. "v751", or empty when the name was not canonized.</param>
		/// <returns>The service's answer, or Accepted = false with the reason.</returns>
		public static BuildModeReport Report(string fingerprint, long sizeBytes, string gameName,
			int buildMode, string compilerVersion, string outcome, string[] segmentHashes, string vndbId) {

			BuildModeReport answer = new();
			string problem = "";

			if (ReportingAllowed == false) {
				problem = "Reporting is off; nothing was sent.";
			}
			if (problem.Length == 0 && Secrets.ReportKey.Length == 0) {
				problem = "No report key is compiled into this build; nothing was sent.";
			}

			if (problem.Length == 0) {
				string sizeText = sizeBytes.ToString();
				string modeText = buildMode.ToString();
				string hashSet = JoinHashSet(segmentHashes);
				string signature = Sign(fingerprint, sizeText, gameName, modeText, compilerVersion, outcome, hashSet, vndbId);

				Dictionary<string, string> fields = new();
				fields["fingerprint"] = fingerprint;
				fields["size_bytes"] = sizeText;
				fields["game_name"] = gameName;
				fields["build_mode"] = modeText;
				fields["compiler_version"] = compilerVersion;
				fields["outcome"] = outcome;
				fields["segment_hashes"] = hashSet;
				fields["vndb_id"] = vndbId;

				string body = "";
				problem = Fetch(BaseUrl + "/report.php", fields, signature, out body);
				if (problem.Length == 0) {
					problem = ReadReport(body, answer);
				}
			}

			if (problem.Length > 0) {
				answer.Accepted = false;
				answer.Error = problem;
			}
			return answer;
		}


		/// <summary>
		/// The signature report.php checks: the eight values joined by a newline, in this
		/// order, no newline at the end, HMAC-SHA256 with the compiled-in key, lowercase hex.
		/// </summary>
		/// <returns>The signature to send as X-Api-Key.</returns>
		public static string Sign(string fingerprint, string sizeText, string gameName,
			string modeText, string compilerVersion, string outcome, string hashSet, string vndbId) {

			string signedText = fingerprint + "\n"
				+ sizeText + "\n"
				+ gameName + "\n"
				+ modeText + "\n"
				+ compilerVersion + "\n"
				+ outcome + "\n"
				+ hashSet + "\n"
				+ vndbId;

			byte[] key = Encoding.UTF8.GetBytes(Secrets.ReportKey);
			byte[] message = Encoding.UTF8.GetBytes(signedText);
			byte[] digest = HMACSHA256.HashData(key, message);
			return ToLowerHex(digest);
		}


		/// <summary>
		/// Makes one request. A GET when fields is null, otherwise a form POST with the
		/// signature in the X-Api-Key header. Any reply body comes back through body, even
		/// on a refusal, so the caller can read the service's reason.
		/// </summary>
		/// <returns>Empty on an HTTP success, otherwise a plain sentence.</returns>
		private static string Fetch(string url, Dictionary<string, string>? fields, string? signature, out string body) {
			string problem = "";
			body = "";
			try {
				HttpMethod method = fields == null
					? HttpMethod.Get
					: HttpMethod.Post;
				HttpRequestMessage request = new(method, url);
				if (fields != null) {
					request.Content = new FormUrlEncodedContent(fields);
					if (signature != null) {
						request.Headers.Add("X-Api-Key", signature);
					}
				}
				using (HttpResponseMessage response = Client.Send(request)) {
					using (StreamReader reader = new(response.Content.ReadAsStream(), Encoding.UTF8)) {
						body = reader.ReadToEnd();
					}
					if (response.IsSuccessStatusCode == false) {
						problem = "The service answered " + ((int) response.StatusCode) + ": " + ServiceError(body);
					}
				}
			}
			catch (Exception exception) {
				problem = "Could not reach the service: " + exception.Message;
			}
			return problem;
		}


		/// <summary>
		/// Fills a lookup answer from lookup.php's JSON.
		/// </summary>
		/// <returns>Empty when the reply was understood, otherwise a plain sentence.</returns>
		private static string ReadLookup(string body, BuildModeLookup answer) {
			string problem = "";
			try {
				using (JsonDocument document = JsonDocument.Parse(body)) {
					JsonElement root = document.RootElement;
					bool ok = ReadBool(root, "ok");
					if (ok == false) {
						problem = ServiceError(body);
					}
					if (ok == true) {
						answer.Reached = true;
						answer.Found = ReadBool(root, "found");
						if (answer.Found == true) {
							answer.GameName = ReadText(root, "game_name");
							answer.BuildMode = ReadNumber(root, "build_mode");
							answer.CompilerVersion = ReadText(root, "compiler_version");
							answer.Reports = ReadNumber(root, "reports");
							answer.Disputes = ReadNumber(root, "disputes");
						}
					}
				}
			}
			catch (JsonException) {
				problem = "The service's reply was not understood.";
			}
			return problem;
		}


		/// <summary>
		/// Fills a report answer from report.php's JSON.
		/// </summary>
		/// <returns>Empty when the reply was understood, otherwise a plain sentence.</returns>
		private static string ReadReport(string body, BuildModeReport answer) {
			string problem = "";
			try {
				using (JsonDocument document = JsonDocument.Parse(body)) {
					JsonElement root = document.RootElement;
					bool ok = ReadBool(root, "ok");
					if (ok == false) {
						problem = ServiceError(body);
					}
					if (ok == true) {
						answer.Accepted = true;
						answer.Reports = ReadNumber(root, "reports");
						answer.Disputes = ReadNumber(root, "disputes");
					}
				}
			}
			catch (JsonException) {
				problem = "The service's reply was not understood.";
			}
			return problem;
		}


		/// <summary>
		/// Replaces KnownGames with the games in games.php's JSON.
		/// </summary>
		/// <returns>True when the reply was the service's game list; false leaves KnownGames untouched.</returns>
		private static bool ReadGames(string body) {
			bool understood = false;
			List<KnownGame> parsed = new();
			try {
				using (JsonDocument document = JsonDocument.Parse(body)) {
					JsonElement root = document.RootElement;
					bool ok = ReadBool(root, "ok");
					if (ok == true && root.TryGetProperty("games", out JsonElement games) == true
						&& games.ValueKind == JsonValueKind.Array) {
						foreach (JsonElement gameElement in games.EnumerateArray()) {
							KnownGame game = new();
							game.GameName = ReadText(gameElement, "game_name");
							game.VndbId = ReadText(gameElement, "vndb_id");
							if (gameElement.TryGetProperty("archives", out JsonElement archives) == true
								&& archives.ValueKind == JsonValueKind.Array) {
								foreach (JsonElement archiveElement in archives.EnumerateArray()) {
									KnownArchive archive = new();
									archive.Fingerprint = ReadText(archiveElement, "fingerprint");
									archive.SegmentHashes = ReadTextArray(archiveElement, "segment_hashes");
									archive.SizeBytes = ReadNumber(archiveElement, "size_bytes");
									archive.BuildMode = ReadNumber(archiveElement, "build_mode");
									archive.CompilerVersion = ReadText(archiveElement, "compiler_version");
									archive.Reports = ReadNumber(archiveElement, "reports");
									archive.Disputes = ReadNumber(archiveElement, "disputes");
									game.Archives.Add(archive);
								}
							}
							parsed.Add(game);
						}
						understood = true;
					}
				}
			}
			catch (JsonException) {
				understood = false;
			}
			if (understood == true) {
				KnownGames.Clear();
				KnownGames.AddRange(parsed);
			}
			return understood;
		}


		/// <summary>
		/// The "error" sentence out of a reply body. A body that is not the service's JSON
		/// (a hosting error page, for example) is described, not quoted.
		/// </summary>
		private static string ServiceError(string body) {
			string message = "not a reply from the service (the address may be wrong).";
			try {
				using (JsonDocument document = JsonDocument.Parse(body)) {
					string error = ReadText(document.RootElement, "error");
					if (error.Length > 0) {
						message = error;
					}
				}
			}
			catch (JsonException) {
				// Not JSON - leave the description in place.
			}
			return message;
		}


		private static bool ReadBool(JsonElement root, string name) {
			bool value = false;
			if (root.TryGetProperty(name, out JsonElement element) == true && element.ValueKind == JsonValueKind.True) {
				value = true;
			}
			return value;
		}


		private static string ReadText(JsonElement root, string name) {
			string value = "";
			if (root.TryGetProperty(name, out JsonElement element) == true && element.ValueKind == JsonValueKind.String) {
				value = element.GetString() ?? "";
			}
			return value;
		}


		private static string[] ReadTextArray(JsonElement root, string name) {
			List<string> values = new();
			if (root.TryGetProperty(name, out JsonElement element) == true && element.ValueKind == JsonValueKind.Array) {
				foreach (JsonElement item in element.EnumerateArray()) {
					if (item.ValueKind == JsonValueKind.String) {
						values.Add(item.GetString() ?? "");
					}
				}
			}
			return values.ToArray();
		}


		private static int ReadNumber(JsonElement root, string name) {
			int value = 0;
			if (root.TryGetProperty(name, out JsonElement element) == true && element.ValueKind == JsonValueKind.Number) {
				element.TryGetInt32(out value);
			}
			return value;
		}


		private static string ToLowerHex(byte[] bytes) {
			StringBuilder hex = new(bytes.Length * 2);
			foreach (byte current in bytes) {
				hex.Append(current.ToString("x2"));
			}
			return hex.ToString();
		}
	}
}
