using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

// Assumed Game-Specific classes: AtlyssNetworkManager, Player, ChatBehaviour, HostConsole

namespace AutoModeration
{
    public class WarningRecord
    {
        public DateTime Timestamp { get; set; }
        public string PlayerName { get; set; }
        public string SteamID { get; set; }
        public string TriggeringMessage { get; set; }
        public int WarnCount { get; set; }
        public int MaxWarnings { get; set; }

        public override string ToString()
        {
            return $"[{Timestamp:yyyy-MM-dd HH:mm:ss}] Player: {PlayerName} (ID: {SteamID}) | Warning {WarnCount}/{MaxWarnings} | Trigger: \"{TriggeringMessage}\"";
        }
    }

    public class BlockRule
    {
        public string Pattern { get; set; }
        public MatchType Type { get; set; }

        public bool IsMatch(string message)
        {
            switch (Type)
            {
                case MatchType.Contains:
                    return message.IndexOf(Pattern, StringComparison.OrdinalIgnoreCase) >= 0;
                case MatchType.StartsWith:
                    return message.StartsWith(Pattern, StringComparison.OrdinalIgnoreCase);
                case MatchType.EndsWith:
                    return message.EndsWith(Pattern, StringComparison.OrdinalIgnoreCase);
                case MatchType.Exact:
                    return Regex.IsMatch(message, @"\b" + Regex.Escape(Pattern) + @"\b", RegexOptions.IgnoreCase);
                default:
                    return false;
            }
        }
    }

    public enum MatchType
    {
        Contains,
        StartsWith,
        EndsWith,
        Exact
    }

    [BepInPlugin(ModInfo.GUID, ModInfo.NAME, ModInfo.VERSION)]
    public class Main : BaseUnityPlugin
    {
        internal static ManualLogSource Log;
        internal static string WarningLogPath;

        internal static List<BlockRule> ParsedBlockRules = new List<BlockRule>();
        internal static List<string> ParsedAllowedPhrases = new List<string>();
        internal static List<Regex> ParsedRegexPatterns = new List<Regex>();
        internal static Dictionary<string, int> PlayerWarningLevels = new Dictionary<string, int>();
        internal static List<string> MonitoredChannels = new List<string>();

        internal static ConfigEntry<bool> AutoModEnabled;
        internal static ConfigEntry<bool> DisableInSinglePlayer;
        internal static ConfigEntry<string> MonitoredChatChannels;
        internal static ConfigEntry<string> BlockedWords;
        internal static ConfigEntry<string> AllowedPhrases;
        internal static ConfigEntry<string> RegexPatterns;
        internal static ConfigEntry<bool> EnableHostActions;
        internal static ConfigEntry<string> HostAction;
        internal static ConfigEntry<bool> WarningSystemEnabled;
        internal static ConfigEntry<int> WarningsUntilAction;
        internal static ConfigEntry<bool> ResetWarningsOnDisconnect;

        private void Awake()
        {
            Log = Logger;
            
            string pluginFolder = Path.GetDirectoryName(Info.Location);
            WarningLogPath = Path.Combine(pluginFolder, "AutoMod_WarningLog.txt");

            AutoModEnabled = Config.Bind("1. General", "Enabled", true,
                "Enables the auto-moderator to block messages.");
            
            DisableInSinglePlayer = Config.Bind("1. General", "Disable in Single-Player", true,
                "istg if u use ts on singleplayer i am NOT helping u fix this do not PM me.");

            MonitoredChatChannels = Config.Bind("1. General", "Monitored Channels", "GLOBAL",
                "Comma-separated list of chat channels to monitor. (e.g., GLOBAL, ROOM, PARTY). Case-insensitive.");

            BlockedWords = Config.Bind("2. Word Filters", "Blocked Words", "*badword*, rude*, *insult",
                "Comma-separated list of words/phrases to block. Use '*' for wildcards.");

            AllowedPhrases = Config.Bind("2. Word Filters", "Allowed Phrases (Whitelist)", "grapefruit, have a nice day",
                "Comma-separated list of phrases that are exempt from being blocked.");

            RegexPatterns = Config.Bind("3. Advanced Filters", "Regex Patterns", "",
                "Comma-separated list of Regex patterns for advanced filtering.");

            EnableHostActions = Config.Bind("4. Punishments", "Enable Host Actions", true,
                "If enabled, the host will automatically take action against players.");

            HostAction = Config.Bind("4. Punishments", "Action Type", "Kick",
                new ConfigDescription("The action to take when a player reaches the warning limit.", new AcceptableValueList<string>("Kick", "Ban")));

            WarningSystemEnabled = Config.Bind("5. Warning System", "Enabled", true,
                "Enable the progressive warning system. If false, punishments are immediate.");

            WarningsUntilAction = Config.Bind("5. Warning System", "Warnings Until Action", 3,
                "Number of infractions a player can have before the 'Action Type' is triggered.");

            ResetWarningsOnDisconnect = Config.Bind("5. Warning System", "Reset Warnings On Disconnect", true,
                "If true, a player's warning count is cleared when they leave the server.");

            UpdateMonitoredChannelsList();
            UpdateBlockRulesList();
            UpdateAllowedPhrasesList();
            UpdateRegexPatternsList();

            Harmony.CreateAndPatchAll(typeof(HarmonyPatches));
            Log.LogInfo($"[{ModInfo.NAME} v{ModInfo.VERSION}] has loaded. Warning log saved to: {WarningLogPath}");
        }

        private void UpdateMonitoredChannelsList()
        {
            if (string.IsNullOrWhiteSpace(MonitoredChatChannels.Value)) MonitoredChannels.Clear();
            else MonitoredChannels = MonitoredChatChannels.Value.Split(',')
                .Select(c => c.Trim().ToUpperInvariant())
                .Where(c => !string.IsNullOrEmpty(c))
                .ToList();
            Log.LogInfo($"Auto-moderator will monitor the following channels: {string.Join(", ", MonitoredChannels)}");
        }

        private void UpdateBlockRulesList()
        {
            ParsedBlockRules.Clear();
            if (string.IsNullOrWhiteSpace(BlockedWords.Value)) return;
            var patterns = BlockedWords.Value.Split(',');
            foreach (var pattern in patterns)
            {
                string trimmed = pattern.Trim();
                if (string.IsNullOrEmpty(trimmed)) continue;
                bool startsWithStar = trimmed.StartsWith("*");
                bool endsWithStar = trimmed.EndsWith("*");
                string corePattern = trimmed.Trim('*');
                if (startsWithStar && endsWithStar) ParsedBlockRules.Add(new BlockRule { Pattern = corePattern, Type = MatchType.Contains });
                else if (startsWithStar) ParsedBlockRules.Add(new BlockRule { Pattern = corePattern, Type = MatchType.EndsWith });
                else if (endsWithStar) ParsedBlockRules.Add(new BlockRule { Pattern = corePattern, Type = MatchType.StartsWith });
                else ParsedBlockRules.Add(new BlockRule { Pattern = trimmed, Type = MatchType.Exact });
            }
        }

        private void UpdateAllowedPhrasesList()
        {
            if (string.IsNullOrWhiteSpace(AllowedPhrases.Value)) ParsedAllowedPhrases.Clear();
            else ParsedAllowedPhrases = AllowedPhrases.Value.Split(',').Select(p => p.Trim()).Where(p => !string.IsNullOrEmpty(p)).ToList();
        }

        private void UpdateRegexPatternsList()
        {
            ParsedRegexPatterns.Clear();
            if (string.IsNullOrWhiteSpace(RegexPatterns.Value)) return;
            var patterns = RegexPatterns.Value.Split(',');
            foreach (var pattern in patterns)
            {
                try
                {
                    var trimmedPattern = pattern.Trim();
                    if (!string.IsNullOrEmpty(trimmedPattern)) ParsedRegexPatterns.Add(new Regex(trimmedPattern, RegexOptions.Compiled | RegexOptions.IgnoreCase));
                }
                catch (Exception ex) { Log.LogError($"[AUTOMOD] Invalid Regex pattern '{pattern}' skipped. Error: {ex.Message}"); }
            }
        }
    }

    [HarmonyPatch]
    internal static class HarmonyPatches
    {
        [HarmonyPrefix, HarmonyPatch(typeof(ChatBehaviour), "UserCode_Rpc_RecieveChatMessage__String__Boolean__ChatChannel")]
        internal static bool InterceptChatMessage_Prefix(ChatBehaviour __instance, string message, ChatBehaviour.ChatChannel _chatChannel)
        {
            if (Main.DisableInSinglePlayer.Value && AtlyssNetworkManager._current._soloMode)
            {
                return true;
            }

            if (!Main.AutoModEnabled.Value || !Main.MonitoredChannels.Contains(_chatChannel.ToString().ToUpperInvariant()))
            {
                return true;
            }

            try
            {
                string plainTextMessage = Regex.Replace(message, "<color=#([0-9a-fA-F]{6})>|</color>", string.Empty);

                foreach (string allowedPhrase in Main.ParsedAllowedPhrases)
                {
                    if (plainTextMessage.IndexOf(allowedPhrase, StringComparison.OrdinalIgnoreCase) >= 0) return true;
                }

                FieldInfo playerField = typeof(ChatBehaviour).GetField("_player", BindingFlags.Instance | BindingFlags.NonPublic);
                if (playerField?.GetValue(__instance) is Player playerWhoSentMessage)
                {
                    string playerName = playerWhoSentMessage._nickname ?? "Unknown Player";
                    string infractionReason = FindInfractionReason(plainTextMessage);

                    if (!string.IsNullOrEmpty(infractionReason))
                    {
                        string logMessage = string.Format(
                            "[AUTOMOD] Infraction by [{0}] in channel [{1}]. Reason: Matched {2}.",
                            playerName,
                            _chatChannel,
                            infractionReason
                        );
                        Main.Log.LogWarning(logMessage);

                        ProcessInfraction(playerWhoSentMessage, playerName, plainTextMessage);
                        return false;
                    }
                }
            }
            catch (Exception ex) { Main.Log.LogError($"[AUTOMOD] Error during message interception: {ex}"); }

            return true;
        }
        
        [HarmonyPostfix, HarmonyPatch(typeof(HostConsole), "Destroy_PeerListEntry")]
        internal static void OnPlayerDisconnect_Postfix(HostConsole __instance, int _connID)
        {
            if (!Main.ResetWarningsOnDisconnect.Value) return;
            var entry = __instance._peerListEntries.FirstOrDefault(e => e._dataID == _connID);
            if (entry?._peerPlayer != null && !string.IsNullOrEmpty(entry._peerPlayer._steamID))
            {
                if (Main.PlayerWarningLevels.ContainsKey(entry._peerPlayer._steamID))
                {
                    Main.PlayerWarningLevels.Remove(entry._peerPlayer._steamID);
                    Main.Log.LogInfo($"[AUTOMOD] Cleared warnings for disconnected player: {entry._peerPlayer._nickname}");
                }
            }
        }

        private static string FindInfractionReason(string message)
        {
            foreach (BlockRule rule in Main.ParsedBlockRules)
            {
                if (rule.IsMatch(message)) return $"word rule '{rule.Pattern}'";
            }
            foreach (Regex regex in Main.ParsedRegexPatterns)
            {
                if (regex.IsMatch(message)) return $"Regex pattern '{regex}'";
            }
            return string.Empty;
        }

        private static void ProcessInfraction(Player targetPlayer, string targetPlayerName, string triggeringMessage)
        {
            if (Player._mainPlayer?._isHostPlayer != true) return;

            if (!Main.WarningSystemEnabled.Value)
            {
                if (Main.EnableHostActions.Value) TakeHostAction(targetPlayer, targetPlayerName, triggeringMessage);
                return;
            }

            string playerId = targetPlayer._steamID;
            if (string.IsNullOrEmpty(playerId))
            {
                Main.Log.LogError($"[AUTOMOD] Cannot warn player [{targetPlayerName}] - they have no Steam ID.");
                return;
            }

            if (!Main.PlayerWarningLevels.ContainsKey(playerId)) Main.PlayerWarningLevels[playerId] = 0;
            Main.PlayerWarningLevels[playerId]++;
            int currentWarnings = Main.PlayerWarningLevels[playerId];
            int maxWarnings = Main.WarningsUntilAction.Value;

            var record = new WarningRecord
            {
                Timestamp = DateTime.Now,
                PlayerName = targetPlayerName,
                SteamID = playerId,
                TriggeringMessage = triggeringMessage,
                WarnCount = currentWarnings,
                MaxWarnings = maxWarnings
            };
            SaveWarningToFile(record);

            if (currentWarnings >= maxWarnings)
            {
                Main.Log.LogInfo($"[AUTOMOD] Player [{targetPlayerName}] reached {currentWarnings}/{maxWarnings} warnings. Taking action.");
                if (Main.EnableHostActions.Value) TakeHostAction(targetPlayer, targetPlayerName, triggeringMessage);
            }
        }
        
        private static void TakeHostAction(Player targetPlayer, string targetPlayerName, string triggeringMessage)
        {
            if (HostConsole._current == null || targetPlayer.connectionToClient == null) return;
            try
            {
                int connectionId = targetPlayer.connectionToClient.connectionId;
                string action = Main.HostAction.Value.ToLower();
                string command = action == "kick" ? $"/kick {connectionId}" : $"/ban {connectionId}";

                Main.Log.LogInfo($"[AUTOMOD] Host executing command: \"{command}\" on player [{targetPlayerName}].");
                HostConsole._current.Init_ServerMessage(command);
                
                string actionLog = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ACTION: Player {targetPlayerName} (ID: {targetPlayer._steamID}) was {action.ToUpper()}ed for accumulating too many warnings. Final straw: \"{triggeringMessage}\"";
                SaveWarningToFile(actionLog);
            }
            catch (Exception ex) { Main.Log.LogError($"[AUTOMOD] Failed to perform host action on [{targetPlayerName}]: {ex}"); }
        }

        private static void SaveWarningToFile(object record)
        {
            try
            {
                File.AppendAllText(Main.WarningLogPath, record.ToString() + Environment.NewLine);
            }
            catch (Exception ex)
            {
                Main.Log.LogError($"[AUTOMOD] Failed to write to warning log: {ex.Message}");
            }
        }
    }
}