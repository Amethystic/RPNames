using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using AutoMapRoom.Wrappers;
using UnityEngine;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

namespace AutoMapRoom
{
    [BepInPlugin(ModInfo.GUID, ModInfo.NAME, ModInfo.VERSION)]
    public class Main : BaseUnityPlugin
    {
        // --- State Management ---
        public static List<string> triggersThisFrame = new List<string>();
        public static List<string> activeTriggers = new List<string>();
        public static string currentMapRoomName = "";
        public static string currentChatRoom = "";
        public static Dictionary<string, string> roomNameCache = new Dictionary<string, string>();
        public static bool _modDisabledGlobalChat = false;
        
        // --- Debounce State ---
        public const int FrameDebounceThreshold = 3;
        public static string pendingChatRoom = null;
        public static int pendingFrameCounter = 0;
        
        internal static ManualLogSource Log;

        // --- Configuration & UI ---
        internal static ConfigEntry<bool> ModEnabled;
        internal static ConfigEntry<bool> DebugLoggingEnabled;
        internal static ConfigEntry<bool> DisableGlobalOnRoomJoin;
        private ConfigEntry<KeyCode> _menuKey;
        private bool _showMenu = false;
        private Rect _windowRect = new Rect(20, 20, 280, 180);
        private bool _isBindingKey = false;
        private bool _ignoreToggleInputForFrame = false;

        private void Awake()
        {
            Log = Logger;
            
            ModEnabled = Config.Bind("General", "Enabled", true, "Globally enables or disables the auto room switching feature.");
            DebugLoggingEnabled = Config.Bind("General", "Debug Logging", false, "Enables verbose logging for troubleshooting.");
            DisableGlobalOnRoomJoin = Config.Bind("General", "Mute Global in Rooms", true, "Automatically mutes the global chat channel when you join a map/region room.");
            _menuKey = Config.Bind("General", "Menu Key", KeyCode.F8, "The key to press to show/hide the settings menu.");

            Harmony.CreateAndPatchAll(typeof(HarmonyPatches.Hook));
            Log.LogInfo("Auto Map Room plugin loaded and patched!");
        }

        private void Update()
        {
            if (_ignoreToggleInputForFrame)
            {
                _ignoreToggleInputForFrame = false;
                return;
            }
            
            if (Input.GetKeyDown(_menuKey.Value))
            {
                _showMenu = !_showMenu;
                if (!_showMenu)
                {
                    _isBindingKey = false;
                }
            }
        }

        private void OnGUI()
        {
            if (_showMenu)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                _windowRect = GUILayout.Window(1862, _windowRect, DrawSettingsWindow, "AutoMapRoom Settings");
            }
        }

        private void DrawSettingsWindow(int windowID)
        {
            if (_isBindingKey)
            {
                if (Event.current.isKey && Event.current.keyCode != KeyCode.None)
                {
                    if (Event.current.type == EventType.KeyDown)
                    {
                        KeyCode pressedKey = Event.current.keyCode;
                        if (pressedKey == KeyCode.Escape)
                        {
                            _isBindingKey = false;
                        }
                        else
                        {
                            _menuKey.Value = pressedKey;
                            _isBindingKey = false;
                            Config.Save();
                            _ignoreToggleInputForFrame = true;
                        }
                    }
                    Event.current.Use();
                }
            }
            
            bool newEnabledState = GUILayout.Toggle(ModEnabled.Value, " Enable Auto Room Switching");
            if (newEnabledState != ModEnabled.Value)
            {
                ModEnabled.Value = newEnabledState;
                if (!newEnabledState && !string.IsNullOrEmpty(currentChatRoom))
                {
                    HarmonyPatches.Hook.UpdateChatRoom("");
                }
                else if (newEnabledState)
                {
                    pendingChatRoom = null;
                    pendingFrameCounter = 0;
                }
            }
            
            DebugLoggingEnabled.Value = GUILayout.Toggle(DebugLoggingEnabled.Value, " Enable Debug Logging");
            DisableGlobalOnRoomJoin.Value = GUILayout.Toggle(DisableGlobalOnRoomJoin.Value, " Mute Global in Rooms");

            GUILayout.Space(10);

            GUILayout.BeginHorizontal();
            GUILayout.Label("Open Menu Key:", GUILayout.Width(120));
            string buttonText = _isBindingKey ? "Press any key..." : _menuKey.Value.ToString();
            if (GUILayout.Button(buttonText, GUILayout.ExpandWidth(true)))
            {
                _isBindingKey = !_isBindingKey;
            }
            GUILayout.EndHorizontal();
            
            GUILayout.FlexibleSpace();
            GUI.DragWindow();
        }
    }

    namespace HarmonyPatches
    {
        [HarmonyPatch]
        internal static class Hook
        {
            private static readonly FieldInfo _inGlobalChatField;
            
            static Hook()
            {
                _inGlobalChatField = AccessTools.Field(typeof(ChatBehaviour), "_inGlobalChat");
            }
            
            [HarmonyReversePatch]
            [HarmonyPatch(typeof(ChatBehaviour), "Cmd_JoinChatRoom")]
            public static void Call_Cmd_JoinChatRoom(ChatBehaviour instance, string chatroom)
            {
                throw new NotImplementedException("This is a stub and should have been patched by Harmony.");
            }

            [HarmonyPostfix, HarmonyPatch(typeof(MapVisualOverrideTrigger), "OnTriggerStay")]
            private static void OnTriggerStay_Postfix(MapVisualOverrideTrigger __instance, Collider other)
            {
                if (!Main.ModEnabled.Value) return;
                if (string.IsNullOrWhiteSpace(__instance._reigonName) || !IsMainPlayer(other)) return;
                string roomName = FormatRoomName(__instance._reigonName);
                if (!Main.triggersThisFrame.Contains(roomName))
                {
                    Main.triggersThisFrame.Add(roomName);
                }
            }
            
            [HarmonyPostfix, HarmonyPatch(typeof(Player), "OnPlayerMapInstanceChange")]
            private static void OnMapInstanceChange_Postfix(Player __instance, MapInstance _new)
            {
                if (__instance != global::Player._mainPlayer) return;
                
                if (AtlyssNetworkManager._current._soloMode)
                {
                    if (Main.currentChatRoom != "") LogDebug("Singleplayer detected, disabling room features.");
                    Main.currentChatRoom = "";
                    Main._modDisabledGlobalChat = false;
                    return;
                }
                
                if (_new == null) return;
                
                Main.currentMapRoomName = FormatRoomName(_new._mapName);
                Main.activeTriggers.Clear();
                Main.pendingChatRoom = null;
                Main.pendingFrameCounter = 0;
            }

            [HarmonyPostfix, HarmonyPatch(typeof(Player), "Update")]
            private static void PlayerUpdate_Postfix(Player __instance)
            {
                if (!Main.ModEnabled.Value || __instance != global::Player._mainPlayer || AtlyssNetworkManager._current._soloMode) return;
                
                bool isInSanctum = Main.currentMapRoomName.Equals("Sanctum", StringComparison.OrdinalIgnoreCase);
                if (isInSanctum)
                {
                    if (Main.currentChatRoom != "")
                    {
                        UpdateChatRoom("");
                    }
                    return;
                }

                Main.activeTriggers.RemoveAll(room => !Main.triggersThisFrame.Contains(room));
                foreach (var room in Main.triggersThisFrame)
                {
                    if (!Main.activeTriggers.Contains(room))
                    {
                        Main.activeTriggers.Add(room);
                    }
                }
                Main.triggersThisFrame.Clear();

                string desiredRoom = Main.activeTriggers.LastOrDefault() ?? Main.currentMapRoomName ?? "";

                if (desiredRoom != Main.pendingChatRoom)
                {
                    Main.pendingChatRoom = desiredRoom;
                    Main.pendingFrameCounter = 0;
                }
                else
                {
                    Main.pendingFrameCounter++;
                }

                if (Main.pendingFrameCounter >= Main.FrameDebounceThreshold && Main.pendingChatRoom != Main.currentChatRoom)
                {
                    UpdateChatRoom(Main.pendingChatRoom);
                }
            }

            internal static void UpdateChatRoom(string desiredRoom)
            {
                if (string.IsNullOrEmpty(desiredRoom))
                {
                    LeaveRoom();
                }
                else
                {
                    JoinRoom(desiredRoom);
                }
                Main.currentChatRoom = desiredRoom;
            }

            private static string FormatRoomName(string regionName)
            {
                if (string.IsNullOrWhiteSpace(regionName)) return "";
                if (Main.roomNameCache.TryGetValue(regionName, out string cachedName)) return cachedName;

                const int maxRoomNameLength = 12;
                string spacelessName = regionName.Replace(" ", "");
                string formattedName;

                if (spacelessName.Length > maxRoomNameLength)
                {
                    StringBuilder acronymBuilder = new StringBuilder();
                    string[] words = regionName.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (string word in words)
                    {
                        if (char.IsLetterOrDigit(word[0]))
                        {
                            acronymBuilder.Append(char.ToUpper(word[0]));
                        }
                    }
                    formattedName = acronymBuilder.ToString();
                }
                else
                {
                    formattedName = spacelessName;
                }
                
                Main.roomNameCache[regionName] = formattedName;
                return formattedName;
            }
    
            private static bool IsMainPlayer(Collider other)
            {
                return global::Player._mainPlayer != null && other.gameObject.transform.root.gameObject == global::Player._mainPlayer.gameObject;
            }
    
            private static void JoinRoom(string roomName)
            {
                var player = Wrappers.Player.GetPlayer();
                if (player?._chatBehaviour != null)
                {
                    bool isGlobalChatEnabled = (bool)_inGlobalChatField.GetValue(player._chatBehaviour);

                    if (Main.DisableGlobalOnRoomJoin.Value && isGlobalChatEnabled)
                    {
                        // CRITICAL FIX: Directly set the value instead of calling the UI method.
                        _inGlobalChatField.SetValue(player._chatBehaviour, false);
                        Main._modDisabledGlobalChat = true;
                        LogDebug("Silently disabled Global Chat.");
                    }
                    
                    player._chatBehaviour._setChatChannel = ChatBehaviour.ChatChannel.ROOM;
                    Call_Cmd_JoinChatRoom(player._chatBehaviour, roomName);
                    LogDebug($"Switched to room: #{roomName}");
                }
            }
            
            private static void LeaveRoom()
            {
                var player = Wrappers.Player.GetPlayer();
                if (player?._chatBehaviour != null)
                {
                    bool isGlobalChatEnabled = (bool)_inGlobalChatField.GetValue(player._chatBehaviour);
                    
                    if (Main.DisableGlobalOnRoomJoin.Value && !isGlobalChatEnabled && Main._modDisabledGlobalChat)
                    {
                        // CRITICAL FIX: Directly set the value instead of calling the UI method.
                        _inGlobalChatField.SetValue(player._chatBehaviour, true);
                        Main._modDisabledGlobalChat = false;
                        LogDebug("Silently re-enabled Global Chat.");
                    }

                    player._chatBehaviour._setChatChannel = ChatBehaviour.ChatChannel.GLOBAL;
                    Call_Cmd_JoinChatRoom(player._chatBehaviour, "");
                    LogDebug("Returned to Global chat.");
                }
            }
            
            private static void LogDebug(string message)
            {
                if (Main.DebugLoggingEnabled.Value)
                {
                    Main.Log.LogInfo(message);
                }
            }
        }
    }
}