using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Entities;
using CounterStrikeSharp.API.Modules.Utils;
using Newtonsoft.Json.Linq;
using System.Numerics;
using System.Reflection;
using static CounterStrikeSharp.API.Core.Listeners;

namespace DamagePrint;

public class DamagePrint : BasePlugin {
	public override string ModuleAuthor => "Tomgra";
	public override string ModuleName => "tDamagePrint";
	public override string ModuleVersion => "1.1.2";

	private Dictionary<CCSPlayerController, PlayerData> Players = new Dictionary<CCSPlayerController, PlayerData>();
	public static JObject? JsonMessage { get; private set; }

	public override void Load(bool hotReload) {
		CreateOrLoadJsonFile(ModuleDirectory + "/message.json");
		RegisterListener<Listeners.OnClientPutInServer>(OnClientPutInServer);
		RegisterListener<Listeners.OnClientDisconnect>(OnClientDisconnect);
		RegisterEventHandler<EventPlayerHurt>(Event_PlayerHurt);
		RegisterEventHandler<EventPlayerSpawn>(Event_PlayerSpawn);
		RegisterEventHandler<EventRoundEnd>(Event_RoundEnd);
	}

	private static void CreateOrLoadJsonFile(string filepath) {
		if (!File.Exists(filepath)) {
			var templateData = new JObject
			{
				["DamagePrint"] = new JObject
				{
					["message"] = "{Green}[Obrażenia] {OTHERNAME}: Zadano {DAMAGE} w {HITS}, " +
					"otrzymano {DAMAGERECIVED} w {HITSRECIVED}",
				},
			};
			File.WriteAllText(filepath, templateData.ToString());
			var jsonData = File.ReadAllText(filepath);
			JsonMessage = JObject.Parse(jsonData);
		} else {
			var jsonData = File.ReadAllText(filepath);
			JsonMessage = JObject.Parse(jsonData);
		}
	}

	[ConsoleCommand("css_tdp_reload")]
	[CommandHelper(minArgs: 0, usage: "", whoCanExecute: CommandUsage.SERVER_ONLY)]
	public void OnReloadConfig(CCSPlayerController? player, CommandInfo info) {
		CreateOrLoadJsonFile(ModuleDirectory + "/message.json");
		Server.PrintToConsole("[tDamagePrint] Config reloaded!");
	}

	private void OnClientPutInServer(int slot) {
		var player = Utilities.GetPlayerFromSlot(slot);
		if(player != null && player.IsValid) {
			PlayerData playerData = new PlayerData(player);
			Players.Add(player, playerData);
			foreach (var attacker in Utilities.GetPlayers()) {
				if (attacker != null && attacker.IsValid && Players.ContainsKey(attacker)) {
					Players[attacker].AddData(player);
				}
			}
		}
	}
	private void OnClientDisconnect(int slot) {
		var player = Utilities.GetPlayerFromSlot(slot);
		if(player != null && player.IsValid && Players.ContainsKey(player)) {
			foreach(var attacker in Utilities.GetPlayers()) {
				if(attacker != null && attacker.IsValid && Players.ContainsKey(attacker)) {
					Players[attacker].RemoveData(player);
				}
			}
			Players.Remove(player);
		}
	}
	private HookResult Event_PlayerHurt(EventPlayerHurt @event, GameEventInfo info) {
		var victim = @event.Userid;
		var attacker = @event.Attacker;
		if (victim != null && attacker != null && victim.IsValid && attacker.IsValid && Players.ContainsKey(attacker)) {
			Players[attacker].AddData(victim, @event.DmgHealth);
		}
		return HookResult.Continue;
	}
	private HookResult Event_PlayerSpawn(EventPlayerSpawn @event, GameEventInfo info) {
		var player = @event.Userid;
		if (player != null && player.IsValid && Players.ContainsKey(player)) {
			Players[player].ResetAllData();
			foreach (var attacker in Utilities.GetPlayers()) {
				if (attacker != null && attacker.IsValid && Players.ContainsKey(attacker)) {
					Players[attacker].AddData(player);
				}
			}
		}
		return HookResult.Continue;
	}
	private HookResult Event_RoundEnd(EventRoundEnd @event, GameEventInfo info) {
		foreach(var player in Utilities.GetPlayers()) {
			if(player != null && player.IsValid && !player.IsBot && player.TeamNum > 1 && Players.ContainsKey(player)) {
				foreach (var attacker in Utilities.GetPlayers()) {
					if(attacker != null && attacker.IsValid && attacker.TeamNum > 1 && attacker.TeamNum != player.TeamNum && Players.ContainsKey(attacker)) {
						if (JsonMessage != null && player != null && player.IsValid && !player.IsBot && JsonMessage.TryGetValue("DamagePrint", out var wMessage) && wMessage is JObject messageObject) {
							string message = messageObject["message"]?.ToString() ?? string.Empty;
							if (message != string.Empty) {
								message = message
								.Replace("{NAME}", player.PlayerName)
								.Replace("{OTHERNAME}", attacker.PlayerName)
								.Replace("{DAMAGE}", Players[player].Damage[attacker].ToString())
								.Replace("{HITS}", Players[player].Hits[attacker].ToString())
								.Replace("{DAMAGERECIVED}", Players[attacker].Damage[player].ToString())
								.Replace("{HITSRECIVED}", Players[attacker].Hits[player].ToString())
								//Z jakiegoś powodu, jeśli wiadomość zaczyna się od koloru, to jest on ignorowany
								.Replace("\n", "\n ");
								message = $" {message}";
								message = ReplaceColors(message);
								string[] s = message.Split("\n");
								for (int i = 0; i < s.Length; i++) player.PrintToChat(s[i]);
							}
						}
					}
				}
			}
		}
		return HookResult.Continue;
	}

	private string ReplaceColors(string message) {
		if (message.Contains('{')) {
			string modifiedValue = message;
			foreach (FieldInfo field in typeof(ChatColors).GetFields()) {
				string pattern = '{' + field.Name + '}';
				modifiedValue = modifiedValue.Replace(pattern, field.GetValue(null)!.ToString(), StringComparison.OrdinalIgnoreCase);
			}
			return modifiedValue;
		}
		return message;
	}
}

public class PlayerData {
	public Dictionary<CCSPlayerController, int> Hits { get; set; } = new Dictionary<CCSPlayerController, int>();
	public Dictionary<CCSPlayerController, int> Damage { get; set; } = new Dictionary<CCSPlayerController, int>();

	public PlayerData(CCSPlayerController player) {
		ResetAllData();
	}

	public void ResetAllData() {
		foreach(var player in Utilities.GetPlayers()) {
			AddData(player);
		}
	}
	public void AddData(CCSPlayerController player, int damage = 0) {
		if (player != null && player.IsValid) {
			if (!Hits.ContainsKey(player)){
				Hits.Add(player, damage == 0 ? 0 : 1);
				Damage.Add(player, damage);
			} else {
				Hits[player] = damage > 0 ? Hits[player] + 1 : 0;
				Damage[player] = damage > 0 ? Damage[player] + damage : 0;
			}
		}
	}
	public void RemoveData(CCSPlayerController player) {
		Hits.Remove(player);
		Damage.Remove(player);
	}
}
