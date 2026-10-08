// File: VndbClient.cs
// Namespace: TranslationTools
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace TranslationTools {

	/// <summary>
	/// One match from VNDB for a typed title.
	/// </summary>
	public class VndbCandidate {

		/// <summary>VNDB's id, e.g. "v751". The canonical identity of a game.</summary>
		public string Id = "";

		/// <summary>VNDB's romanised title. The canonical game name.</summary>
		public string Title = "";

		/// <summary>The title in its original script, when VNDB has one.</summary>
		public string OriginalTitle = "";

		/// <summary>The release date VNDB lists, as text; may be partial.</summary>
		public string Released = "";


		/// <summary>
		/// One line for a pick list: title · original title · year.
		/// </summary>
		public string Describe() {
			string text = Title;
			if (OriginalTitle.Length > 0) {
				text += "  ·  " + OriginalTitle;
			}
			if (Released.Length >= 4) {
				text += "  ·  " + Released.Substring(0, 4);
			}
			return text;
		}
	}


	/// <summary>
	/// One character of a game as VNDB lists them: the names VNDB knows, the role in this
	/// game, and the description, with VNDB's markup removed.
	/// </summary>
	public class VndbCharacter {

		/// <summary>VNDB's id, e.g. "c1234".</summary>
		public string Id = "";

		/// <summary>The romanised name.</summary>
		public string Name = "";

		/// <summary>The name in its original script; empty when VNDB has none.</summary>
		public string Original = "";

		/// <summary>Other names VNDB lists.</summary>
		public List<string> Aliases = new();

		/// <summary>VNDB's role word for this game: main, primary, side or appears; empty when unknown.</summary>
		public string Role = "";

		/// <summary>VNDB's description as plain text; empty when none.</summary>
		public string Description = "";


		/// <summary>
		/// Every name VNDB gives, romanised first, no repeats.
		/// </summary>
		public List<string> AllNames() {
			List<string> names = new();
			if (Name.Trim().Length > 0) {
				names.Add(Name.Trim());
			}
			if (Original.Trim().Length > 0 && CharacterEntry.Contains(names, Original) == false) {
				names.Add(Original.Trim());
			}
			foreach (string alias in Aliases) {
				if (alias.Trim().Length > 0 && CharacterEntry.Contains(names, alias) == false) {
					names.Add(alias.Trim());
				}
			}
			return names;
		}
	}


	/// <summary>
	/// Asks VNDB (the Visual Novel Database) which games a typed title might mean, so a
	/// game name can be canonized to VNDB's title and id rather than whatever spelling was
	/// typed, and which characters a game has, so a glossary can start from VNDB's cast.
	/// The public API needs no key; its search is fuzzy, so the answer is always a
	/// list for the user to pick from, never a match the tool makes itself.
	/// </summary>
	public static class VndbClient {

		private const string SearchUrl = "https://api.vndb.org/kana/vn";
		private const string CharacterUrl = "https://api.vndb.org/kana/character";

		/// <summary>Characters per page; VNDB's most.</summary>
		private const int CharacterPage = 100;

		private static readonly HttpClient Client = MakeClient();


		/// <summary>
		/// Every character VNDB lists for a game, page by page.
		/// </summary>
		/// <param name="vnId">VNDB's id for the game, e.g. "v751".</param>
		/// <param name="error">Empty when VNDB answered, otherwise a plain sentence.</param>
		/// <returns>The characters; empty when the game has none or VNDB could not be asked.</returns>
		public static List<VndbCharacter> Characters(string vnId, out string error) {
			List<VndbCharacter> characters = new();
			error = "";
			bool more = true;
			int page = 1;
			while (more == true && error.Length == 0) {
				string body = "";
				try {
					string request = "{\"filters\":[\"vn\",\"=\",[\"id\",\"=\"," + JsonSerializer.Serialize(vnId) + "]],"
						+ "\"fields\":\"id, name, original, aliases, description, vns.id, vns.role\",\"results\":" + CharacterPage + ",\"page\":" + page + "}";
					using (HttpRequestMessage message = new(HttpMethod.Post, CharacterUrl)) {
						message.Content = new StringContent(request, Encoding.UTF8, "application/json");
						using (HttpResponseMessage response = Client.Send(message)) {
							using (StreamReader reader = new(response.Content.ReadAsStream(), Encoding.UTF8)) {
								body = reader.ReadToEnd();
							}
							if (response.IsSuccessStatusCode == false) {
								error = "VNDB answered " + ((int) response.StatusCode) + ".";
							}
						}
					}
				}
				catch (Exception exception) {
					error = "Could not reach VNDB: " + exception.Message;
				}
				if (error.Length == 0) {
					error = ReadCharacters(body, vnId, characters, out more);
				}
				page++;
			}
			return characters;
		}


		/// <summary>
		/// Fills the list from VNDB's JSON: { "results": [ { "id", "name", "original", "aliases",
		/// "description", "vns": [ { "id", "role" } ] } ], "more": bool }. The role is the one
		/// for the asked game.
		/// </summary>
		/// <returns>Empty when understood, otherwise a plain sentence.</returns>
		public static string ReadCharacters(string body, string vnId, List<VndbCharacter> characters, out bool more) {
			string error = "";
			more = false;
			try {
				using (JsonDocument document = JsonDocument.Parse(body)) {
					if (document.RootElement.TryGetProperty("more", out JsonElement moreElement) == true) {
						more = moreElement.ValueKind == JsonValueKind.True;
					}
					if (document.RootElement.TryGetProperty("results", out JsonElement results) == true
						&& results.ValueKind == JsonValueKind.Array) {
						foreach (JsonElement item in results.EnumerateArray()) {
							VndbCharacter character = new();
							character.Id = ReadText(item, "id");
							character.Name = ReadText(item, "name");
							character.Original = ReadText(item, "original");
							character.Description = PlainText(ReadText(item, "description"));
							if (item.TryGetProperty("aliases", out JsonElement aliases) == true && aliases.ValueKind == JsonValueKind.Array) {
								foreach (JsonElement alias in aliases.EnumerateArray()) {
									if (alias.ValueKind == JsonValueKind.String) {
										character.Aliases.Add(alias.GetString() ?? "");
									}
								}
							}
							if (item.TryGetProperty("vns", out JsonElement vns) == true && vns.ValueKind == JsonValueKind.Array) {
								foreach (JsonElement vn in vns.EnumerateArray()) {
									if (string.Equals(ReadText(vn, "id"), vnId, StringComparison.OrdinalIgnoreCase)) {
										character.Role = ReadText(vn, "role");
									}
								}
							}
							if (character.Id.Length > 0 && character.Name.Length > 0) {
								characters.Add(character);
							}
						}
					}
				}
			}
			catch (JsonException) {
				error = "VNDB's reply was not understood.";
			}
			return error;
		}


		/// <summary>
		/// VNDB's description markup removed: [spoiler], [url=...], [b], [i], [s], [u],
		/// [raw], [code], [quote] and their closers go, their text stays.
		/// </summary>
		public static string PlainText(string marked) {
			StringBuilder plain = new();
			int at = 0;
			while (at < marked.Length) {
				bool dropped = false;
				if (marked[at] == '[') {
					int close = marked.IndexOf(']', at + 1);
					if (close > at) {
						string tag = marked.Substring(at + 1, close - at - 1).TrimStart('/');
						int equals = tag.IndexOf('=');
						if (equals >= 0) {
							tag = tag.Substring(0, equals);
						}
						string[] known = new string[] { "spoiler", "url", "b", "i", "s", "u", "raw", "code", "quote" };
						foreach (string word in known) {
							if (string.Equals(tag, word, StringComparison.OrdinalIgnoreCase)) {
								dropped = true;
							}
						}
						if (dropped == true) {
							at = close + 1;
						}
					}
				}
				if (dropped == false) {
					plain.Append(marked[at]);
					at++;
				}
			}
			return plain.ToString().Replace("\r", "").Trim();
		}


		/// <summary>
		/// The games whose titles resemble a typed one, best first.
		/// </summary>
		/// <param name="title">What the user typed.</param>
		/// <param name="most">How many candidates at most.</param>
		/// <param name="error">Empty when VNDB answered, otherwise a plain sentence.</param>
		/// <returns>The candidates; empty when none matched or VNDB could not be asked.</returns>
		public static List<VndbCandidate> Search(string title, int most, out string error) {
			List<VndbCandidate> candidates = new();
			error = "";
			string body = "";
			try {
				string request = "{\"filters\":[\"search\",\"=\"," + JsonSerializer.Serialize(title) + "],"
					+ "\"fields\":\"id, title, alttitle, released\",\"results\":" + most + "}";
				using (HttpRequestMessage message = new(HttpMethod.Post, SearchUrl)) {
					message.Content = new StringContent(request, Encoding.UTF8, "application/json");
					using (HttpResponseMessage response = Client.Send(message)) {
						using (StreamReader reader = new(response.Content.ReadAsStream(), Encoding.UTF8)) {
							body = reader.ReadToEnd();
						}
						if (response.IsSuccessStatusCode == false) {
							error = "VNDB answered " + ((int) response.StatusCode) + ".";
						}
					}
				}
			}
			catch (Exception exception) {
				error = "Could not reach VNDB: " + exception.Message;
			}

			if (error.Length == 0) {
				error = ReadCandidates(body, candidates);
			}
			return candidates;
		}


		/// <summary>
		/// Fills the list from VNDB's JSON: { "results": [ { "id", "title", "alttitle", "released" }, ... ] }.
		/// </summary>
		/// <returns>Empty when understood, otherwise a plain sentence.</returns>
		private static string ReadCandidates(string body, List<VndbCandidate> candidates) {
			string error = "";
			try {
				using (JsonDocument document = JsonDocument.Parse(body)) {
					if (document.RootElement.TryGetProperty("results", out JsonElement results) == true
						&& results.ValueKind == JsonValueKind.Array) {
						foreach (JsonElement item in results.EnumerateArray()) {
							VndbCandidate candidate = new();
							candidate.Id = ReadText(item, "id");
							candidate.Title = ReadText(item, "title");
							candidate.OriginalTitle = ReadText(item, "alttitle");
							candidate.Released = ReadText(item, "released");
							if (candidate.Id.Length > 0 && candidate.Title.Length > 0) {
								candidates.Add(candidate);
							}
						}
					}
				}
			}
			catch (JsonException) {
				error = "VNDB's reply was not understood.";
			}
			return error;
		}


		private static string ReadText(JsonElement root, string name) {
			string value = "";
			if (root.TryGetProperty(name, out JsonElement element) == true && element.ValueKind == JsonValueKind.String) {
				value = element.GetString() ?? "";
			}
			return value;
		}


		private static HttpClient MakeClient() {
			HttpClient client = new() { Timeout = TimeSpan.FromSeconds(15) };
			client.DefaultRequestHeaders.UserAgent.ParseAdd("TranslationTools/0.1");
			return client;
		}
	}
}
