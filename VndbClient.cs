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
	/// Asks VNDB (the Visual Novel Database) which games a typed title might mean, so a
	/// game name can be canonized to VNDB's title and id rather than whatever spelling was
	/// typed. The public API needs no key; its search is fuzzy, so the answer is always a
	/// list for the user to pick from, never a match the tool makes itself.
	/// </summary>
	public static class VndbClient {

		private const string SearchUrl = "https://api.vndb.org/kana/vn";

		private static readonly HttpClient Client = MakeClient();


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
