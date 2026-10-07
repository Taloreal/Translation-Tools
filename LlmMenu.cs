// File: LlmMenu.cs
// Namespace: TranslationTools
using System.Globalization;

using TALOREAL_NETCORE_API;

namespace TranslationTools {

	/// <summary>
	/// Settings -> "Language model...": every connection fact the LlmClient uses, each item
	/// showing its current value, plus a connection test. Values are typed in after the
	/// current one is shown; a blank answer keeps what is there.
	/// </summary>
	public static class LlmMenu {

		private static readonly ConsoleMenuItem AddressItem = new("");
		private static readonly ConsoleMenuItem ModelItem = new("");
		private static readonly ConsoleMenuItem KeyItem = new("");
		private static readonly ConsoleMenuItem TemperatureItem = new("");
		private static readonly ConsoleMenuItem TokensItem = new("");
		private static readonly ConsoleMenuItem TimeoutItem = new("");
		private static readonly ConsoleMenuItem EndpointItem = new("");
		private static readonly ConsoleMenuItem SystemRoleItem = new("");
		private static readonly ConsoleMenuItem BeforeItem = new("");
		private static readonly ConsoleMenuItem AfterItem = new("");


		/// <summary>
		/// Shows the menu until the user chooses Back.
		/// </summary>
		public static void Show() {
			ConsoleSelectMenu menu = new(loops: true, numbered: false, clearOnRefresh: true);
			menu.AddOnDrawMenuAction(Refresh);
			menu.AddChoice(AddressItem.SetActionOnSelect(SetAddress));
			menu.AddChoice(ModelItem.SetActionOnSelect(SetModel));
			menu.AddChoice(KeyItem.SetActionOnSelect(SetApiKey));
			menu.AddChoice(TemperatureItem.SetActionOnSelect(SetTemperature));
			menu.AddChoice(TokensItem.SetActionOnSelect(SetMaxTokens));
			menu.AddChoice(TimeoutItem.SetActionOnSelect(SetTimeout));
			menu.AddChoice(EndpointItem.SetActionOnSelect(ToggleEndpoint));
			menu.AddChoice(SystemRoleItem.SetActionOnSelect(ToggleSystemRole));
			menu.AddChoice(BeforeItem.SetActionOnSelect(SetContextBefore));
			menu.AddChoice(AfterItem.SetActionOnSelect(SetContextAfter));
			menu.AddChoice(new ConsoleMenuItem("Presets...").SetActionOnSelect(PresetsMenu));
			menu.AddChoice(new ConsoleMenuItem("Test the connection").SetActionOnSelect(TestConnection));
			menu.AddChoice(new ConsoleMenuItem("Back"));
			menu.GetChoice();
		}


		/// <summary>
		/// Puts each setting's current value into its item text.
		/// </summary>
		private static void Refresh(ConsoleSelectMenu menu) {
			string model = LlmClient.Model;
			if (model.Length == 0) {
				model = "(whatever the endpoint has loaded)";
			}
			string systemRole = "folded into the user message";
			if (LlmClient.UseSystemRole == true) {
				systemRole = "its own message";
			}
			AddressItem.SetText("Address: " + LlmClient.Address);
			ModelItem.SetText("Model: " + model);
			KeyItem.SetText("API key: " + LlmClient.ApiKeyForDisplay);
			TemperatureItem.SetText("Temperature: " + LlmClient.Temperature.ToString(CultureInfo.InvariantCulture));
			TokensItem.SetText("Most tokens in a reply: " + LlmClient.MaxTokens);
			TimeoutItem.SetText("Timeout (seconds): " + LlmClient.TimeoutSeconds);
			EndpointItem.SetText("Endpoint: " + LlmClient.CompletionPath);
			SystemRoleItem.SetText("System text goes as: " + systemRole);
			BeforeItem.SetText("Context lines before the line: " + LlmClient.ContextBefore);
			AfterItem.SetText("Context lines after the line: " + LlmClient.ContextAfter);
			menu.SetPreChoiceText("-- Language model --\nAn OpenAI-compatible endpoint: KoboldCpp on this machine, a hosted backend, or any other.\n");
		}

		/// <summary>
		/// The endpoint's address: a menu of the built-in address, a new one typed in, or cancel.
		/// </summary>
		private static void SetAddress() {
			ConsoleSelectMenu menu = new(loops: false, numbered: false, clearOnRefresh: true);
			menu.SetPreChoiceText("Address, now: " + LlmClient.Address);
			menu.AddChoice(new ConsoleMenuItem("Use the built-in address " + LlmClient.DefaultAddress));
			menu.AddChoice(new ConsoleMenuItem("Type a new address..."));
			menu.AddChoice(new ConsoleMenuItem("Cancel"));
			int choice = menu.GetChoice();
			if (choice == 0) {
				LlmClient.Address = "";
				Console.WriteLine("Using " + LlmClient.Address);
				ConsoleExt.WaitForEnter("continue");
			}
			if (choice == 1) {
				string answer = ConsoleExt.ReadLine("New address (blank to cancel): ", -1, false).Trim();
				if (answer.Length > 0) {
					LlmClient.Address = answer;
					Console.WriteLine("Using " + LlmClient.Address);
					ConsoleExt.WaitForEnter("continue");
				}
			}
		}


		/// <summary>
		/// The model name: a menu of picking from the endpoint's own list, typing a name,
		/// sending no name at all, or cancel.
		/// </summary>
		private static void SetModel() {
			string current = LlmClient.Model;
			if (current.Length == 0) {
				current = "(none sent; the endpoint uses what it has loaded)";
			}
			ConsoleSelectMenu menu = new(loops: false, numbered: false, clearOnRefresh: true);
			menu.SetPreChoiceText("Model, now: " + current);
			menu.AddChoice(new ConsoleMenuItem("Pick from the endpoint's list..."));
			menu.AddChoice(new ConsoleMenuItem("Type a name..."));
			menu.AddChoice(new ConsoleMenuItem("Send no name (the endpoint uses what it has loaded)"));
			menu.AddChoice(new ConsoleMenuItem("Cancel"));
			int choice = menu.GetChoice();
			if (choice == 0) {
				PickModelFromEndpoint();
			}
			if (choice == 1) {
				string answer = ConsoleExt.ReadLine("Model name (blank to cancel): ", -1, false).Trim();
				if (answer.Length > 0) {
					LlmClient.Model = answer;
					Console.WriteLine("Model: " + LlmClient.Model);
					ConsoleExt.WaitForEnter("continue");
				}
			}
			if (choice == 2) {
				LlmClient.Model = "";
				Console.WriteLine("No model name will be sent.");
				ConsoleExt.WaitForEnter("continue");
			}
		}


		/// <summary>
		/// Asks the endpoint for its models and lets the user pick one from the paged list.
		/// </summary>
		private static void PickModelFromEndpoint() {
			Console.WriteLine("Asking " + LlmClient.Address + "/v1/models ...");
			List<string> names = LlmClient.ListModels(out string error);
			if (error.Length > 0) {
				Console.WriteLine(error);
				ConsoleExt.WaitForEnter("continue");
			}
			if (error.Length == 0 && names.Count == 0) {
				Console.WriteLine("The endpoint lists no models.");
				ConsoleExt.WaitForEnter("continue");
			}
			if (names.Count > 0) {
				int picked = PagedPicker.Pick(names, "Models the endpoint offers");
				if (picked >= 0) {
					LlmClient.Model = names[picked];
					Console.WriteLine("Model: " + LlmClient.Model);
					ConsoleExt.WaitForEnter("continue");
				}
			}
		}


		/// <summary>
		/// The API key: a menu of typing a new one, sending none, or cancel. Shown masked.
		/// </summary>
		private static void SetApiKey() {
			ConsoleSelectMenu menu = new(loops: false, numbered: false, clearOnRefresh: true);
			menu.SetPreChoiceText("API key, now: " + LlmClient.ApiKeyForDisplay);
			menu.AddChoice(new ConsoleMenuItem("Type a new key..."));
			menu.AddChoice(new ConsoleMenuItem("Send no key"));
			menu.AddChoice(new ConsoleMenuItem("Cancel"));
			int choice = menu.GetChoice();
			if (choice == 0) {
				string answer = ConsoleExt.ReadLine("API key (blank to cancel): ", -1, false).Trim();
				if (answer.Length > 0) {
					LlmClient.ApiKey = answer;
					Console.WriteLine("Key: " + LlmClient.ApiKeyForDisplay);
					ConsoleExt.WaitForEnter("continue");
				}
			}
			if (choice == 1) {
				LlmClient.ApiKey = "";
				Console.WriteLine("No key will be sent.");
				ConsoleExt.WaitForEnter("continue");
			}
		}


		private static void SetTemperature() {
			Console.WriteLine("Current: " + LlmClient.Temperature.ToString(CultureInfo.InvariantCulture));
			string answer = ConsoleExt.ReadLine("Temperature, 0 to 2 (blank keeps it): ", -1, false).Trim();
			if (answer.Length > 0) {
				bool parsed = double.TryParse(answer, NumberStyles.Float, CultureInfo.InvariantCulture, out double value);
				if (parsed == true && value >= 0 && value <= 2) {
					LlmClient.Temperature = value;
					Console.WriteLine("Temperature: " + value.ToString(CultureInfo.InvariantCulture));
				}
				if (parsed == false || value < 0 || value > 2) {
					Console.WriteLine("That is not a number from 0 to 2. Nothing changed.");
				}
				ConsoleExt.WaitForEnter("continue");
			}
		}


		private static void SetMaxTokens() {
			SetWholeNumber("Most tokens in a reply", LlmClient.MaxTokens, 16, 1000000, (value) => { LlmClient.MaxTokens = value; });
		}


		private static void SetTimeout() {
			SetWholeNumber("Timeout in seconds", LlmClient.TimeoutSeconds, 5, 86400, (value) => { LlmClient.TimeoutSeconds = value; });
		}


		private static void SetContextBefore() {
			SetWholeNumber("Context lines before the line", LlmClient.ContextBefore, 0, LlmClient.MostContextLines, (value) => { LlmClient.ContextBefore = value; });
		}


		private static void SetContextAfter() {
			SetWholeNumber("Context lines after the line", LlmClient.ContextAfter, 0, LlmClient.MostContextLines, (value) => { LlmClient.ContextAfter = value; });
		}


		/// <summary>
		/// Shows a whole-number setting and reads a new one inside a range; blank keeps it.
		/// </summary>
		private static void SetWholeNumber(string label, int current, int least, int most, Action<int> store) {
			Console.WriteLine("Current: " + current);
			string answer = ConsoleExt.ReadLine(label + ", " + least + " to " + most + " (blank keeps it): ", -1, false).Trim();
			if (answer.Length > 0) {
				bool parsed = int.TryParse(answer, out int value);
				if (parsed == true && value >= least && value <= most) {
					store(value);
					Console.WriteLine(label + ": " + value);
				}
				if (parsed == false || value < least || value > most) {
					Console.WriteLine("That is not a whole number from " + least + " to " + most + ". Nothing changed.");
				}
				ConsoleExt.WaitForEnter("continue");
			}
		}


		private static void ToggleEndpoint() {
			LlmClient.UseChatEndpoint = LlmClient.UseChatEndpoint == false;
		}


		private static void ToggleSystemRole() {
			LlmClient.UseSystemRole = LlmClient.UseSystemRole == false;
		}


		/// <summary>
		/// Named snapshots of every value above: save the current ones, load one, delete one.
		/// </summary>
		private static void PresetsMenu() {
			ConsoleSelectMenu menu = new(loops: true, numbered: false, clearOnRefresh: true);
			menu.AddOnDrawMenuAction((shown) => {
				shown.SetPreChoiceText("-- Language model presets --\nStored: " + LlmPresetList.All().Count + ", plus the built-in defaults\n");
			});
			menu.AddChoice(new ConsoleMenuItem("Save the current values as a preset...").SetActionOnSelect(SavePreset));
			menu.AddChoice(new ConsoleMenuItem("Load a preset...").SetActionOnSelect(LoadPreset));
			menu.AddChoice(new ConsoleMenuItem("Delete a preset...").SetActionOnSelect(DeletePreset));
			menu.AddChoice(new ConsoleMenuItem("Back"));
			menu.GetChoice();
		}


		/// <summary>
		/// Stores the values in use under a typed name; a name already in use is replaced after a yes.
		/// </summary>
		private static void SavePreset() {
			Console.WriteLine("Saving: " + LlmPreset.FromCurrent("").Describe().Trim());
			string name = ConsoleExt.ReadLine("Preset name (blank to cancel): ", -1, false).Trim();
			if (LlmPreset.SameName(name, LlmPreset.BuiltInName) == true) {
				Console.WriteLine("That name is the built-in preset's. Pick another.");
				ConsoleExt.WaitForEnter("continue");
				name = "";
			}
			if (name.Length > 0) {
				bool store = true;
				if (LlmPresetList.Find(name) != null) {
					store = ConsoleExt.ReadValue<bool>("A preset named " + name + " exists. Replace it? (y/n): ", false);
				}
				if (store == true) {
					LlmPresetList.Put(LlmPreset.FromCurrent(name));
					Console.WriteLine("Saved " + name + ".");
					ConsoleExt.WaitForEnter("continue");
				}
			}
		}


		/// <summary>
		/// Picks a preset from the list and makes every value in it the one in use.
		/// </summary>
		private static void LoadPreset() {
			List<LlmPreset> presets = new();
			presets.Add(LlmPreset.BuiltIn());
			presets.AddRange(LlmPresetList.All());
			int picked = PagedPicker.Pick(DescribeAll(presets), "Load which preset?");
			if (picked >= 0) {
				presets[picked].Apply();
				Console.WriteLine("Loaded " + presets[picked].Name + ": now " + LlmClient.Address + ", model " + LlmClient.Model);
				ConsoleExt.WaitForEnter("continue");
			}
		}


		/// <summary>
		/// Picks a preset from the list and removes it. The values in use are not touched.
		/// </summary>
		private static void DeletePreset() {
			List<LlmPreset> presets = LlmPresetList.All();
			int picked = PagedPicker.Pick(DescribeAll(presets), "Delete which preset? (the values in use stay as they are)");
			if (picked >= 0) {
				bool sure = ConsoleExt.ReadValue<bool>("Delete the preset " + presets[picked].Name + "? (y/n): ", false);
				if (sure == true) {
					LlmPresetList.Remove(presets[picked].Name);
					Console.WriteLine("Deleted " + presets[picked].Name + ".");
					ConsoleExt.WaitForEnter("continue");
				}
			}
		}


		private static List<string> DescribeAll(List<LlmPreset> presets) {
			List<string> rows = new();
			foreach (LlmPreset preset in presets) {
				rows.Add(preset.Describe());
			}
			return rows;
		}


		/// <summary>
		/// Sends a one-line prompt and shows the reply and how long it took.
		/// </summary>
		private static void TestConnection() {
			Console.WriteLine("Asking " + LlmClient.Address + LlmClient.CompletionPath + " ...");
			string reply = LlmClient.Test(out string error, out long milliseconds);
			if (error.Length > 0) {
				Console.WriteLine("No luck after " + milliseconds + " ms: " + error);
			}
			if (error.Length == 0) {
				Console.WriteLine("Reply in " + milliseconds + " ms:");
				Console.WriteLine("  " + reply.Trim());
			}
			ConsoleExt.WaitForEnter("continue");
		}
	}
}
