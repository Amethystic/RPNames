using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

// It's good practice to add a using statement for the game's main namespace if it exists
// using ATLYSS; 

namespace AutoMod
{
    // Helper class to define a filtering rule. Placed in the namespace for global access.
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

    // Enum for the different types of word matching.
    public enum MatchType
    {
        Contains,   // *word*
        StartsWith, // word*
        EndsWith,   // *word
        Exact       // word
    }

    [BepInPlugin(ModInfo.GUID, ModInfo.NAME, ModInfo.VERSION)]
    public class Main : BaseUnityPlugin
    {
        internal static ManualLogSource Log;

        // --- Parsed Rule Lists & State ---
        internal static List<BlockRule> ParsedBlockRules = new List<BlockRule>();
        internal static List<string> ParsedAllowedPhrases = new List<string>();
        internal static List<Regex> ParsedRegexPatterns = new List<Regex>();
        internal static Dictionary<string, int> PlayerWarningLevels = new Dictionary<string, int>();
        internal static List<string> MonitoredChannels = new List<string>();

        // --- Configuration Entries ---
        internal static ConfigEntry<bool> AutoModEnabled;
        internal static ConfigEntry<string> MonitoredChatChannels;
        internal static ConfigEntry<string> BlockedWords;
        internal static ConfigEntry<string> AllowedPhrases;
        internal static ConfigEntry<string> RegexPatterns;
        internal static ConfigEntry<bool> EnableHostActions;
        internal static ConfigEntry<string> HostAction;
        internal static ConfigEntry<bool> WarningSystemEnabled;
        internal static ConfigEntry<int> WarningsUntilAction;
        internal static ConfigEntry<string> PublicWarningMessage;
        internal static ConfigEntry<bool> ResetWarningsOnDisconnect;

        private void Awake()
        {
            Log = Logger;

            // --- Bind Configurations ---
            AutoModEnabled = Config.Bind("1. General", "Enabled", true,
                "Enables the auto-moderator to block messages.");

            MonitoredChatChannels = Config.Bind("1. General", "Monitored Channels", "GLOBAL, ROOM",
                "Comma-separated list of chat channels to monitor. (e.g., GLOBAL, ROOM, PARTY, WHISPER). Case-insensitive.");

            BlockedWords = Config.Bind("2. Word Filters", "Blocked Words", "*badword*, rude*, *insult",
                "Comma-separated list of words/phrases to block. Use '*' for wildcards. Examples: '*word*' (contains), 'word*' (starts with), '*word' (ends with), 'word' (exact whole word).");

            AllowedPhrases = Config.Bind("2. Word Filters", "Allowed Phrases (Whitelist)", "grapefruit, have a nice day",
                "Comma-separated list of phrases that are exempt from being blocked, even if they contain a blocked word.");

            RegexPatterns = Config.Bind("3. Advanced Filters", "Regex Patterns", "",
                "Comma-separated list of Regex patterns for advanced filtering. Invalid patterns will be ignored.");

            EnableHostActions = Config.Bind("4. Punishments", "Enable Host Actions", true,
                "If enabled, the host will automatically take action against players.");

            HostAction = Config.Bind("4. Punishments", "Action Type", "Kick",
                new ConfigDescription("The action to take when a player reaches the warning limit.", new AcceptableValueList<string>("Kick", "Ban")));

            WarningSystemEnabled = Config.Bind("5. Warning System", "Enabled", true,
                "Enable the progressive warning system. If false, punishments are immediate.");

            WarningsUntilAction = Config.Bind("5. Warning System", "Warnings Until Action", 3,
                "Number of infractions a player can have before the 'Action Type' is triggered.");

            PublicWarningMessage = Config.Bind("5. Warning System", "Warning Message", "[SERVER] {PlayerName} has been warned for inappropriate language. ({WarnCount}/{MaxWarnings})",
                "The message sent to public chat when a player is warned. Use {PlayerName}, {WarnCount}, {MaxWarnings}. Leave empty to disable.");

            ResetWarningsOnDisconnect = Config.Bind("5. Warning System", "Reset Warnings On Disconnect", true,
                "If true, a player's warning count is cleared when they leave the server.");

            // --- Initialize Systems ---
            UpdateMonitoredChannelsList();
            UpdateBlockRulesList();
            UpdateAllowedPhrasesList();
            UpdateRegexPatternsList();

            Harmony.CreateAndPatchAll(typeof(HarmonyPatches));
            Log.LogInfo($"[{ModInfo.NAME} v{ModInfo.VERSION}] has loaded and patched successfully.");
        }

        #region Config Update Methods
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
        #endregion
    }

    [HarmonyPatch]
    internal static class HarmonyPatches
    {
        [HarmonyPrefix]
        [HarmonyPatch(typeof(ChatBehaviour), "UserCode_Rpc_RecieveChatMessage__String__Boolean__ChatChannel")]
        internal static bool InterceptChatMessage_Prefix(ChatBehaviour __instance, string message, ChatBehaviour.ChatChannel _chatChannel)
        {
            // First, check if we should even be moderating this message
            if (!Main.AutoModEnabled.Value || !Main.MonitoredChannels.Contains(_chatChannel.ToString().ToUpperInvariant()))
            {
                return true; // Skip moderation
            }

            try
            {
                string plainTextMessage = Regex.Replace(message, "<color=#([0-9a-fA-F]{6})>|</color>", string.Empty);

                // Check allow list (whitelist)
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
                        Main.Log.LogWarning($"[AUTOMOD] Infraction by [{playerName}] in channel [{_chatChannel}]. Reason: Matched {infractionReason}.");
                        ProcessInfraction(playerWhoSentMessage, playerName);
                        return false; // Block the message
                    }
                }
            }
            catch (Exception ex) { Main.Log.LogError($"[AUTOMOD] Error during message interception: {ex}"); }

            return true; // Message is clean
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(HostConsole), "Destroy_PeerListEntry")]
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

        private static void ProcessInfraction(Player targetPlayer, string targetPlayerName)
        {
            if (Player._mainPlayer?._isHostPlayer != true) return;

            if (!Main.WarningSystemEnabled.Value)
            {
                if (Main.EnableHostActions.Value) TakeHostAction(targetPlayer, targetPlayerName);
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

            if (currentWarnings >= maxWarnings)
            {
                Main.Log.LogInfo($"[AUTOMOD] Player [{targetPlayerName}] reached {currentWarnings}/{maxWarnings} warnings. Taking action.");
                if (Main.EnableHostActions.Value) TakeHostAction(targetPlayer, targetPlayerName);
                Main.PlayerWarningLevels.Remove(playerId);
            }
            else
            {
                if (!string.IsNullOrEmpty(Main.PublicWarningMessage.Value))
                {
                    string warningMessage = Main.PublicWarningMessage.Value
                        .Replace("{PlayerName}", targetPlayerName)
                        .Replace("{WarnCount}", currentWarnings.ToString())
                        .Replace("{MaxWarnings}", maxWarnings.ToString());
                    HostConsole._current.Init_ServerMessage(warningMessage);
                }
            }
        }

        private static void TakeHostAction(Player targetPlayer, string targetPlayerName)
        {
            if (HostConsole._current == null || targetPlayer.connectionToClient == null) return;
            try
            {
                int connectionId = targetPlayer.connectionToClient.connectionId;
                string action = Main.HostAction.Value.ToLower();
                string command = action == "kick" ? $"/kick {connectionId}" : $"/ban {connectionId}";

                Main.Log.LogInfo($"[AUTOMOD] Host executing command: \"{command}\" on player [{targetPlayerName}].");
                HostConsole._current.Init_ServerMessage(command);
            }
            catch (Exception ex) { Main.Log.LogError($"[AUTOMOD] Failed to perform host action on [{targetPlayerName}]: {ex}"); }
        }
    }
}