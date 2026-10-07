// File: LlmClient.cs
// Namespace: TranslationTools
using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json;

using TALOREAL_NETCORE_API;

namespace TranslationTools {

	/// <summary>
	/// The tool's connection to a language model behind an OpenAI-compatible endpoint:
	/// KoboldCpp on this machine, a hosted backend such as Venice, or anything else that
	/// answers /v1/chat/completions or /v1/completions. Every connection fact is a
	/// per-machine Setting with a default, so a fresh install talks to a local KoboldCpp
	/// until told otherwise. The one thing callers do is Complete(): system text and user
	/// text in, the model's reply out. Caching, retries beyond a dropped socket, and the
	/// choice to stop asking a model that is down all belong to the caller.
	/// </summary>
	public static class LlmClient {

		private const string AddressKey = "Llm.Address";
		private const string ModelKey = "Llm.Model";
		private const string ApiKeyKey = "Llm.ApiKey";
		private const string TemperatureKey = "Llm.Temperature";
		private const string MaxTokensKey = "Llm.MaxTokens";
		private const string TimeoutKey = "Llm.TimeoutSeconds";
		private const string ChatEndpointKey = "Llm.UseChatEndpoint";
		private const string SystemRoleKey = "Llm.UseSystemRole";
		private const string ContextBeforeKey = "Llm.ContextBefore";
		private const string ContextAfterKey = "Llm.ContextAfter";

		/// <summary>Where a fresh install looks: KoboldCpp's usual port on this machine.</summary>
		public const string DefaultAddress = "http://localhost:5001";

		/// <summary>The most context lines a prompt may carry on either side of its line.</summary>
		public const int MostContextLines = 50;

		/// <summary>Context lines on either side when nothing has been set.</summary>
		public const int DefaultContextLines = 5;

		private static readonly HttpClient Client = new() { Timeout = Timeout.InfiniteTimeSpan };


		/// <summary>The endpoint's base address, without a trailing slash. Blank means the default.</summary>
		public static string Address {
			get {
				string address = DefaultAddress;
				bool stored = Settings.GetValue(AddressKey, out string? saved);
				if (stored == true && saved != null && saved.Trim().Length > 0) {
					address = saved.Trim();
				}
				if (address.StartsWith("http://") == false && address.StartsWith("https://") == false) {
					address = "http://" + address;
				}
				return address.TrimEnd('/');
			}
			set { Settings.SetValue(AddressKey, value.Trim()); }
		}

		/// <summary>The model name sent with every request. Blank lets the endpoint use whatever it has loaded.</summary>
		public static string Model {
			get { return ReadValue(ModelKey, "").Trim(); }
			set { Settings.SetValue(ModelKey, value.Trim()); }
		}

		/// <summary>Sent as "Authorization: Bearer ..." when not blank. A local KoboldCpp needs none; a hosted backend does.</summary>
		public static string ApiKey {
			get { return ReadValue(ApiKeyKey, "").Trim(); }
			set { Settings.SetValue(ApiKeyKey, value.Trim()); }
		}

		/// <summary>Sampling temperature, 0 to 2.</summary>
		public static double Temperature {
			get { return ReadValue(TemperatureKey, 0.3); }
			set { Settings.SetValue(TemperatureKey, value); }
		}

		/// <summary>The most tokens a reply may hold unless a call asks for fewer.</summary>
		public static int MaxTokens {
			get { return ReadValue(MaxTokensKey, 16384); }
			set { Settings.SetValue(MaxTokensKey, value); }
		}

		/// <summary>How long a call waits for the reply unless it asks for less.</summary>
		public static int TimeoutSeconds {
			get { return ReadValue(TimeoutKey, 300); }
			set { Settings.SetValue(TimeoutKey, value); }
		}

		/// <summary>True for /v1/chat/completions, false for /v1/completions.</summary>
		public static bool UseChatEndpoint {
			get { return ReadValue(ChatEndpointKey, true); }
			set { Settings.SetValue(ChatEndpointKey, value); }
		}

		/// <summary>On the chat endpoint: send the system text as its own message, or fold it into the user message.</summary>
		public static bool UseSystemRole {
			get { return ReadValue(SystemRoleKey, true); }
			set { Settings.SetValue(SystemRoleKey, value); }
		}

		/// <summary>Script lines shown to the model before the line it is asked about, 0 to MostContextLines.</summary>
		public static int ContextBefore {
			get { return Math.Clamp(ReadValue(ContextBeforeKey, DefaultContextLines), 0, MostContextLines); }
			set { Settings.SetValue(ContextBeforeKey, Math.Clamp(value, 0, MostContextLines)); }
		}

		/// <summary>Script lines shown to the model after the line it is asked about, 0 to MostContextLines.</summary>
		public static int ContextAfter {
			get { return Math.Clamp(ReadValue(ContextAfterKey, DefaultContextLines), 0, MostContextLines); }
			set { Settings.SetValue(ContextAfterKey, Math.Clamp(value, 0, MostContextLines)); }
		}

		/// <summary>The API key as the user may see it: blank as "(none)", otherwise its ends with the middle starred.</summary>
		public static string ApiKeyForDisplay {
			get {
				string key = ApiKey;
				string shown = "(none)";
				if (key.Length > 0 && key.Length <= 8) {
					shown = new string('*', key.Length);
				}
				if (key.Length > 8) {
					shown = key.Substring(0, 4) + new string('*', key.Length - 8) + key.Substring(key.Length - 4);
				}
				return shown;
			}
		}

		/// <summary>The path a completion is posted to, after the address.</summary>
		public static string CompletionPath {
			get {
				string path = "/v1/completions";
				if (UseChatEndpoint == true) {
					path = "/v1/chat/completions";
				}
				return path;
			}
		}


		/// <summary>
		/// Sends one prompt and returns the model's reply. The system text and the user text
		/// are sent as two messages, one message, or one prompt according to the settings.
		/// A connection the server closed under us is retried once; nothing else is.
		/// </summary>
		/// <param name="system">Instructions to the model; may be empty.</param>
		/// <param name="user">The question or material.</param>
		/// <param name="error">Empty when a reply came back, otherwise a plain sentence.</param>
		/// <param name="maxTokens">A cap on the reply for this call; 0 uses the setting.</param>
		/// <param name="timeoutSeconds">A wait for this call; 0 uses the setting.</param>
		/// <returns>The reply text, or empty when error says why.</returns>
		public static string Complete(string system, string user, out string error, int maxTokens = 0, int timeoutSeconds = 0) {
			error = "";
			string reply = "";
			int tokens = MaxTokens;
			if (maxTokens > 0) {
				tokens = maxTokens;
			}
			int waitSeconds = TimeoutSeconds;
			if (timeoutSeconds > 0) {
				waitSeconds = timeoutSeconds;
			}
			waitSeconds = Math.Max(5, waitSeconds);
			string url = Address + CompletionPath;
			string body = RequestBody(system, user, tokens);

			bool done = false;
			int attempt = 0;
			while (done == false && attempt < 2) {
				attempt++;
				try {
					using (HttpRequestMessage message = new(HttpMethod.Post, url)) {
						message.Content = new StringContent(body, Encoding.UTF8, "application/json");
						AddAuthorization(message);
						using (CancellationTokenSource cancel = new(TimeSpan.FromSeconds(waitSeconds))) {
							using (HttpResponseMessage response = Client.Send(message, cancel.Token)) {
								string answer = "";
								using (StreamReader reader = new(response.Content.ReadAsStream(cancel.Token), Encoding.UTF8)) {
									answer = reader.ReadToEnd();
								}
								if (response.IsSuccessStatusCode == false) {
									error = "The endpoint answered HTTP " + ((int) response.StatusCode) + ": " + Shorten(answer, 400);
								}
								if (response.IsSuccessStatusCode == true) {
									reply = ReadReply(answer, out error);
								}
								done = true;
							}
						}
					}
				}
				catch (OperationCanceledException) {
					error = "No reply within " + waitSeconds + " seconds. Raise the timeout, or shorten the prompt.";
					done = true;
				}
				catch (HttpRequestException exception) {
					// A pooled connection the server had already closed. The second attempt opens a new one.
					error = "Could not reach " + url + ": " + exception.Message;
				}
				catch (Exception exception) {
					error = exception.GetType().Name + ": " + exception.Message;
					done = true;
				}
			}
			return reply;
		}


		/// <summary>
		/// Asks the endpoint which models it offers (GET /v1/models). Understands a bare
		/// array, or a "data" or "models" array, of strings or of objects with "id" or "name".
		/// </summary>
		/// <param name="error">Empty when the endpoint answered, otherwise a plain sentence.</param>
		/// <returns>The model names, or empty when error says why.</returns>
		public static List<string> ListModels(out string error) {
			List<string> names = new();
			error = "";
			string url = Address + "/v1/models";
			string answer = "";
			try {
				using (HttpRequestMessage message = new(HttpMethod.Get, url)) {
					AddAuthorization(message);
					using (CancellationTokenSource cancel = new(TimeSpan.FromSeconds(15))) {
						using (HttpResponseMessage response = Client.Send(message, cancel.Token)) {
							using (StreamReader reader = new(response.Content.ReadAsStream(cancel.Token), Encoding.UTF8)) {
								answer = reader.ReadToEnd();
							}
							if (response.IsSuccessStatusCode == false) {
								error = "The endpoint answered HTTP " + ((int) response.StatusCode) + ": " + Shorten(answer, 400);
							}
						}
					}
				}
			}
			catch (OperationCanceledException) {
				error = "No reply from " + url + " within 15 seconds.";
			}
			catch (Exception exception) {
				error = "Could not reach " + url + ": " + exception.Message;
			}

			if (error.Length == 0) {
				try {
					using (JsonDocument document = JsonDocument.Parse(answer)) {
						JsonElement list = document.RootElement;
						if (list.ValueKind == JsonValueKind.Object) {
							if (list.TryGetProperty("data", out JsonElement data) == true) {
								list = data;
							}
							if (list.ValueKind == JsonValueKind.Object && list.TryGetProperty("models", out JsonElement models) == true) {
								list = models;
							}
						}
						if (list.ValueKind == JsonValueKind.Array) {
							foreach (JsonElement item in list.EnumerateArray()) {
								string name = ModelNameOf(item);
								if (name.Length > 0) {
									names.Add(name);
								}
							}
						}
					}
				}
				catch (JsonException) {
					error = "The model list from " + url + " was not understood.";
				}
			}
			return names;
		}


		/// <summary>
		/// Sends a one-line prompt and times the round trip, for the Settings menu's test.
		/// </summary>
		/// <param name="error">Empty when a reply came back, otherwise a plain sentence.</param>
		/// <param name="milliseconds">How long the round trip took.</param>
		/// <returns>The reply, or empty when error says why.</returns>
		public static string Test(out string error, out long milliseconds) {
			Stopwatch clock = Stopwatch.StartNew();
			string reply = Complete("Answer in one short sentence.", "Say hello and name the model you are.", out error, 600, 120);
			clock.Stop();
			milliseconds = clock.ElapsedMilliseconds;
			return reply;
		}


		/// <summary>
		/// Builds the JSON body for one completion in the shape the settings call for.
		/// </summary>
		private static string RequestBody(string system, string user, int maxTokens) {
			string folded = user;
			if (system.Length > 0) {
				folded = system + "\n\n" + user;
			}
			using (MemoryStream stream = new()) {
				using (Utf8JsonWriter writer = new(stream)) {
					writer.WriteStartObject();
					if (Model.Length > 0) {
						writer.WriteString("model", Model);
					}
					writer.WriteNumber("temperature", Temperature);
					writer.WriteNumber("max_tokens", maxTokens);
					writer.WriteBoolean("stream", false);
					if (UseChatEndpoint == true) {
						writer.WriteStartArray("messages");
						if (UseSystemRole == true && system.Length > 0) {
							WriteMessage(writer, "system", system);
							WriteMessage(writer, "user", user);
						}
						if (UseSystemRole == false || system.Length == 0) {
							WriteMessage(writer, "user", folded);
						}
						writer.WriteEndArray();
					}
					if (UseChatEndpoint == false) {
						writer.WriteString("prompt", folded);
					}
					writer.WriteEndObject();
				}
				return Encoding.UTF8.GetString(stream.ToArray());
			}
		}


		private static void WriteMessage(Utf8JsonWriter writer, string role, string content) {
			writer.WriteStartObject();
			writer.WriteString("role", role);
			writer.WriteString("content", content);
			writer.WriteEndObject();
		}


		/// <summary>
		/// Pulls the reply text out of a completion response: choices[0].message.content on
		/// the chat endpoint, choices[0].text on the other.
		/// </summary>
		private static string ReadReply(string answer, out string error) {
			string reply = "";
			error = "";
			try {
				using (JsonDocument document = JsonDocument.Parse(answer)) {
					bool found = false;
					if (document.RootElement.TryGetProperty("choices", out JsonElement choices) == true
						&& choices.ValueKind == JsonValueKind.Array && choices.GetArrayLength() > 0) {
						JsonElement first = choices[0];
						if (first.TryGetProperty("message", out JsonElement message) == true
							&& message.TryGetProperty("content", out JsonElement content) == true
							&& content.ValueKind == JsonValueKind.String) {
							reply = content.GetString() ?? "";
							found = true;
						}
						if (found == false && first.TryGetProperty("text", out JsonElement text) == true
							&& text.ValueKind == JsonValueKind.String) {
							reply = text.GetString() ?? "";
							found = true;
						}
					}
					if (found == false) {
						error = EmptyReplyReason(choices, answer);
					}
				}
			}
			catch (JsonException) {
				error = "The reply was not JSON: " + Shorten(answer, 400);
			}
			return reply;
		}


	/// <summary>
		/// Why a reply carried no text. A model that reasons before answering puts its thinking
		/// in reasoning_content and may run out of tokens before any answer; that is said
		/// plainly. Anything else shows the start of the raw reply.
		/// </summary>
		private static string EmptyReplyReason(JsonElement choices, string answer) {
			string reason = "The reply held no text: " + Shorten(answer, 400);
			if (choices.ValueKind == JsonValueKind.Array && choices.GetArrayLength() > 0) {
				JsonElement first = choices[0];
				bool reasoned = false;
				if (first.TryGetProperty("message", out JsonElement message) == true
					&& message.TryGetProperty("reasoning_content", out JsonElement thinking) == true
					&& thinking.ValueKind == JsonValueKind.String && (thinking.GetString() ?? "").Length > 0) {
					reasoned = true;
				}
				string finish = "";
				if (first.TryGetProperty("finish_reason", out JsonElement finishReason) == true && finishReason.ValueKind == JsonValueKind.String) {
					finish = finishReason.GetString() ?? "";
				}
				if (reasoned == true) {
					reason = "The model spent its reply budget reasoning and never answered";
					if (finish.Length > 0) {
						reason += " (finish_reason: " + finish + ")";
					}
					reason += ". Raise the most tokens in a reply, or pick a model that does not reason.";
				}
			}
			return reason;
		}


			private static string ModelNameOf(JsonElement item) {
			string name = "";
			if (item.ValueKind == JsonValueKind.String) {
				name = item.GetString() ?? "";
			}
			if (item.ValueKind == JsonValueKind.Object) {
				if (item.TryGetProperty("id", out JsonElement id) == true && id.ValueKind == JsonValueKind.String) {
					name = id.GetString() ?? "";
				}
				if (name.Length == 0 && item.TryGetProperty("name", out JsonElement named) == true && named.ValueKind == JsonValueKind.String) {
					name = named.GetString() ?? "";
				}
			}
			return name.Trim();
		}


		private static void AddAuthorization(HttpRequestMessage message) {
			string key = ApiKey;
			if (key.Length > 0) {
				if (key.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) == false) {
					key = "Bearer " + key;
				}
				message.Headers.TryAddWithoutValidation("Authorization", key);
			}
		}


		private static string Shorten(string text, int most) {
			string shown = text.Replace("\r", " ").Replace("\n", " ").Trim();
			if (shown.Length > most) {
				shown = shown.Substring(0, most) + "...";
			}
			return shown;
		}


		/// <summary>
		/// A setting of any stored type, or its fallback when nothing is stored.
		/// </summary>
		private static T ReadValue<T>(string key, T fallback) {
			T value = fallback;
			bool stored = Settings.GetValue<T>(key, out T? saved);
			if (stored == true && saved != null) {
				value = saved;
			}
			return value;
		}
	}
}
