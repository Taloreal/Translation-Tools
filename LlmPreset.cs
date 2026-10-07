// File: LlmPreset.cs
// Namespace: TranslationTools
using System.Globalization;

namespace TranslationTools {

	/// <summary>
	/// A named snapshot of every language-model connection value, so a user who switches
	/// between KoboldCpp and a hosted backend can go back and forth without retyping.
	/// The name is the identity. Stored as named lines, like a game install, so fields can
	/// be added later and lines this build does not recognise survive a save.
	/// </summary>
	public class LlmPreset {

		private const string NameName = "Name";
		private const string AddressName = "Address";
		private const string ModelName = "Model";
		private const string ApiKeyName = "ApiKey";
		private const string TemperatureName = "Temperature";
		private const string MaxTokensName = "MaxTokens";
		private const string TimeoutName = "TimeoutSeconds";
		private const string ChatEndpointName = "UseChatEndpoint";
		private const string SystemRoleName = "UseSystemRole";
		private const string ContextBeforeName = "ContextBefore";
		private const string ContextAfterName = "ContextAfter";

		/// <summary>The name of the preset that holds the defaults.</summary>
		public const string BuiltInName = "Built-in defaults";

		/// <summary>The preset's name, as the user typed it.</summary>
		public string Name = "";

		/// <summary>The address as stored: blank means the built-in one.</summary>
		public string Address = "";

		public string Model = "";

		public string ApiKey = "";

		public double Temperature = 0.3;

		public int MaxTokens = 16384;

		public int TimeoutSeconds = 300;

		public bool UseChatEndpoint = true;

		public bool UseSystemRole = true;

		public int ContextBefore = LlmClient.DefaultContextLines;

		public int ContextAfter = LlmClient.DefaultContextLines;

		/// <summary>Lines from a newer build that this one does not understand, kept so a save does not lose them.</summary>
		public List<string> UnknownLines = new();


		/// <summary>
		/// The preset every install starts from: the built-in address, no model name, no key,
		/// and the default numbers. Always offered when loading; never stored or deleted.
		/// </summary>
		public static LlmPreset BuiltIn() {
			LlmPreset preset = new();
			preset.Name = BuiltInName;
			return preset;
		}


		/// <summary>
		/// A preset holding the values the client is using right now.
		/// </summary>
		/// <param name="name">What to call it.</param>
		public static LlmPreset FromCurrent(string name) {
			LlmPreset preset = new();
			preset.Name = name;
			preset.Address = LlmClient.Address;
			preset.Model = LlmClient.Model;
			preset.ApiKey = LlmClient.ApiKey;
			preset.Temperature = LlmClient.Temperature;
			preset.MaxTokens = LlmClient.MaxTokens;
			preset.TimeoutSeconds = LlmClient.TimeoutSeconds;
			preset.UseChatEndpoint = LlmClient.UseChatEndpoint;
			preset.UseSystemRole = LlmClient.UseSystemRole;
			preset.ContextBefore = LlmClient.ContextBefore;
			preset.ContextAfter = LlmClient.ContextAfter;
			return preset;
		}


		/// <summary>
		/// Rebuilds a preset from the text Encode wrote.
		/// </summary>
		/// <param name="encoded">The stored text.</param>
		/// <param name="preset">The preset, complete only when this returns true.</param>
		/// <returns>True when the text held a name.</returns>
		public static bool TryDecode(string encoded, out LlmPreset preset) {
			preset = new LlmPreset();
			foreach (string line in encoded.Split('\n')) {
				int equals = line.IndexOf('=');
				if (equals > 0) {
					string name = line.Substring(0, equals);
					string value = line.Substring(equals + 1);
					bool known = preset.ReadLine(name, value);
					if (known == false) {
						preset.UnknownLines.Add(line);
					}
				}
			}
			return preset.Name.Length > 0;
		}


		/// <summary>
		/// Whether two names are the same preset: equal ignoring case.
		/// </summary>
		public static bool SameName(string first, string second) {
			return string.Equals(first.Trim(), second.Trim(), StringComparison.OrdinalIgnoreCase);
		}


		/// <summary>
		/// Makes the client use every value in this preset.
		/// </summary>
		public void Apply() {
			LlmClient.Address = Address;
			LlmClient.Model = Model;
			LlmClient.ApiKey = ApiKey;
			LlmClient.Temperature = Temperature;
			LlmClient.MaxTokens = MaxTokens;
			LlmClient.TimeoutSeconds = TimeoutSeconds;
			LlmClient.UseChatEndpoint = UseChatEndpoint;
			LlmClient.UseSystemRole = UseSystemRole;
			LlmClient.ContextBefore = ContextBefore;
			LlmClient.ContextAfter = ContextAfter;
		}


		/// <summary>
		/// The text TryDecode reads back: one "name=value" line per field.
		/// </summary>
		public string Encode() {
			List<string> lines = new();
			lines.Add(NameName + "=" + Name);
			lines.Add(AddressName + "=" + Address);
			lines.Add(ModelName + "=" + Model);
			lines.Add(ApiKeyName + "=" + ApiKey);
			lines.Add(TemperatureName + "=" + Temperature.ToString(CultureInfo.InvariantCulture));
			lines.Add(MaxTokensName + "=" + MaxTokens);
			lines.Add(TimeoutName + "=" + TimeoutSeconds);
			lines.Add(ChatEndpointName + "=" + UseChatEndpoint);
			lines.Add(SystemRoleName + "=" + UseSystemRole);
			lines.Add(ContextBeforeName + "=" + ContextBefore);
			lines.Add(ContextAfterName + "=" + ContextAfter);
			lines.AddRange(UnknownLines);
			return string.Join("\n", lines);
		}


		/// <summary>
		/// One line of a preset as the user sees it in a list: the name, then where it points.
		/// </summary>
		public string Describe() {
			string address = Address;
			if (address.Length == 0) {
				address = LlmClient.DefaultAddress;
			}
			string model = Model;
			if (model.Length == 0) {
				model = "(no model name)";
			}
			string endpoint = "completions";
			if (UseChatEndpoint == true) {
				endpoint = "chat";
			}
			return Name + "   " + address + "   " + model + "   " + endpoint;
		}


		/// <summary>
		/// Stores one decoded line into its field.
		/// </summary>
		/// <returns>True when the name was one this build knows.</returns>
		private bool ReadLine(string name, string value) {
			bool known = true;
			if (name == NameName) {
				Name = value.Trim();
			}
			if (name == AddressName) {
				Address = value;
			}
			if (name == ModelName) {
				Model = value;
			}
			if (name == ApiKeyName) {
				ApiKey = value;
			}
			if (name == TemperatureName) {
				double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out Temperature);
			}
			if (name == MaxTokensName) {
				int.TryParse(value, out MaxTokens);
			}
			if (name == TimeoutName) {
				int.TryParse(value, out TimeoutSeconds);
			}
			if (name == ChatEndpointName) {
				bool.TryParse(value, out UseChatEndpoint);
			}
			if (name == SystemRoleName) {
				bool.TryParse(value, out UseSystemRole);
			}
			if (name == ContextBeforeName) {
				int.TryParse(value, out ContextBefore);
			}
			if (name == ContextAfterName) {
				int.TryParse(value, out ContextAfter);
			}
			if (name != NameName && name != AddressName && name != ModelName && name != ApiKeyName
				&& name != TemperatureName && name != MaxTokensName && name != TimeoutName
				&& name != ChatEndpointName && name != SystemRoleName
				&& name != ContextBeforeName && name != ContextAfterName) {
				known = false;
			}
			return known;
		}
	}
}
