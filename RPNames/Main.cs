// ============== USING STATEMENTS ==============

using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using CodeTalker.Networking;
using CodeTalker.Packets;
using HarmonyLib;
using Newtonsoft.Json;
using TMPro;
using UnityEngine;
// Required for the packet-based CodeTalker implementation

// ============== NAMESPACE DEFINITIONS ==============
namespace RPNames
{
    public enum AnimationType { Static, Scroll, Rainbow, Marquee }
    
    namespace Packets
    {
        public class SetRoleplayTitlePacket : PacketBase
        {
            public override string PacketSourceGUID => ModInfo.GUID;
            [JsonProperty] public ulong TargetID { get; set; } 
            [JsonProperty] public string DesiredTitle { get; set; }
            public SetRoleplayTitlePacket() { }
            public SetRoleplayTitlePacket(ulong targetId, string name) 
            {
                TargetID = targetId;
                DesiredTitle = name; 
            }
        }

        public class RequestAllTitlesPacket : PacketBase
        {
            public override string PacketSourceGUID => ModInfo.GUID;
        }
    }

    // ============== MAIN PLUGIN CLASS ==============
    [BepInPlugin(ModInfo.GUID, ModInfo.NAME, ModInfo.VERSION)]
    [BepInDependency("CodeTalker", BepInDependency.DependencyFlags.SoftDependency)]
    public class Main : BaseUnityPlugin
    {
        public class ModInfo
        {
            public const string GUID = RPNames.ModInfo.GUID;
            public const string NAME = RPNames.ModInfo.NAME;
            public const string VERSION = RPNames.ModInfo.VERSION;
        }

        internal static ManualLogSource Log;
        internal static bool IsReady = false;
        internal static ConfigEntry<string> RoleplayName;
        private ConfigEntry<KeyCode> _menuKey;
        internal static ConfigEntry<bool> AnimationEnabled;
        internal static ConfigEntry<AnimationType> SelectedAnimationType;
        internal static ConfigEntry<float> AnimationSpeed;
        internal static ConfigEntry<int> MarqueeWidth;

        private bool _showMenu = false;
        private Rect _windowRect = new Rect(20, 20, 320, 300);
        private string _nameInput = "";
        private bool _showAnimationPicker = false;
        internal static bool _isCodeTalkerLoaded = false;
        
        private bool _isAnimating = false;
        private string _animationText = "";
        private float _animationTimer = 0f;
        private int _animationIndex = 0;
        private bool _isAnimatingForward = true;
        private float _rainbowHue = 0f;

        private void Awake()
        {
            Log = Logger;
            
            RoleplayName = Config.Bind("1. General", "Saved Name", "", "Your saved roleplay name. Supports TextMeshPro color tags.");
            _menuKey = Config.Bind("1. General", "Menu Key", KeyCode.F8, "The key to press to show/hide the settings menu.");
            AnimationEnabled = Config.Bind("2. Animation", "Enable Animation", false, "Animate your roleplay name.");
            SelectedAnimationType = Config.Bind("2. Animation", "Animation Type", AnimationType.Scroll, "The type of animation to apply (Scroll, Rainbow, Marquee).");
            AnimationSpeed = Config.Bind("2. Animation", "Animation Speed", 0.15f, "Delay in seconds between animation frames (lower is faster).");
            MarqueeWidth = Config.Bind("2. Animation", "Marquee Width", 16, "The character width of the Marquee scrolling animation.");

            _nameInput = RoleplayName.Value;

            if (Type.GetType("CodeTalker.Networking.CodeTalkerNetwork, CodeTalker") != null)
            {
                _isCodeTalkerLoaded = true;
            }
            
            if (AnimationEnabled.Value && !string.IsNullOrEmpty(_nameInput))
            {
                StartAnimation();
            }
            
            Harmony.CreateAndPatchAll(typeof(HarmonyPatches));
            Log.LogInfo($"[{ModInfo.NAME} v{ModInfo.VERSION}] has loaded.");
        }
        
        // --- CORRECTED NETWORKING LOGIC ---

        // This runs ONLY ON THE HOST when a client requests to change their title.
        public static void OnTitleUpdateRequest(PacketHeader header, PacketBase packet)
        {
            if (packet is Packets.SetRoleplayTitlePacket titlePacket)
            {
                // The host, as the authority, immediately broadcasts this change to all clients (including itself).
                Log.LogInfo($"Host received title update from {header.SenderID}. Broadcasting to all.");
                CodeTalkerNetwork.SendNetworkPacket(titlePacket);
            }
        }

        // This runs ON EVERYONE (clients and host) when the host sends an authoritative broadcast.
        public static void OnTitleBroadcast(PacketHeader header, PacketBase packet)
        {
            if (packet is Packets.SetRoleplayTitlePacket titlePacket)
            {
                if (HarmonyPatches.PlayerTitles.ContainsKey(titlePacket.TargetID))
                {
                    HarmonyPatches.PlayerTitles[titlePacket.TargetID] = titlePacket.DesiredTitle;
                }
                else
                {
                    HarmonyPatches.PlayerTitles.Add(titlePacket.TargetID, titlePacket.DesiredTitle);
                }
            }
        }

        // This runs ONLY ON THE HOST when a new client joins and requests all current titles.
        public static void OnSyncRequest(PacketHeader header, PacketBase packet)
        {
            if (packet is Packets.RequestAllTitlesPacket)
            {
                Log.LogInfo($"Received sync request from {header.SenderID}. Sending all current titles.");
                foreach (var entry in HarmonyPatches.PlayerTitles)
                {
                    // The host sends a broadcast for each known title.
                    CodeTalkerNetwork.SendNetworkPacket(new Packets.SetRoleplayTitlePacket(entry.Key, entry.Value));
                }
            }
        }

        // Call this when you want to change your own title.
        public static void RequestTitleChange(string name)
        {
            if (!_isCodeTalkerLoaded || Player._mainPlayer == null || Player._mainPlayer.connectionToClient == null) return;
            ulong myId = (ulong)Player._mainPlayer.connectionToClient.connectionId;
            
            // Send a packet to the server (even if we are the server). The host's listener will handle it.
            CodeTalkerNetwork.SendNetworkPacket(new Packets.SetRoleplayTitlePacket(myId, name));
        }
        
        public static void RequestFullTitleSync()
        {
            if (!_isCodeTalkerLoaded) return;
            CodeTalkerNetwork.SendNetworkPacket(new Packets.RequestAllTitlesPacket());
        }

        private void Update()
        {
            if (Input.GetKeyDown(_menuKey.Value))
            {
                _showMenu = !_showMenu;
                if (!_showMenu) _showAnimationPicker = false;
            }

            if (!_isAnimating || !IsReady) return;
            
            _animationTimer += Time.deltaTime;
            if (_animationTimer >= AnimationSpeed.Value)
            {
                _animationTimer = 0f;
                string nameToSend = "";

                switch (SelectedAnimationType.Value)
                {
                    case AnimationType.Scroll:
                        if (string.IsNullOrEmpty(_animationText)) break;
                        if (_isAnimatingForward) { _animationIndex++; if (_animationIndex >= _animationText.Length) { _animationIndex = _animationText.Length; _isAnimatingForward = false; } }
                        else { _animationIndex--; if (_animationIndex <= 1) { _animationIndex = 1; _isAnimatingForward = true; } }
                        nameToSend = _animationText.Substring(0, _animationIndex);
                        break;
                    case AnimationType.Rainbow:
                        if (string.IsNullOrEmpty(_animationText)) break;
                        _rainbowHue = (_rainbowHue + 0.02f) % 1.0f;
                        Color rainbowColor = Color.HSVToRGB(_rainbowHue, 1f, 1f);
                        string hexColor = "#" + ColorUtility.ToHtmlStringRGB(rainbowColor);
                        nameToSend = $"<color={hexColor}>{_animationText}</color>";
                        break;
                    case AnimationType.Marquee:
                        if (string.IsNullOrEmpty(_animationText)) break;
                        string padding = new string(' ', MarqueeWidth.Value);
                        string fullMarqueeText = padding + _animationText + padding;
                        _animationIndex = (_animationIndex + 1) % (fullMarqueeText.Length - MarqueeWidth.Value);
                        nameToSend = fullMarqueeText.Substring(_animationIndex, MarqueeWidth.Value);
                        break;
                }
                if (!string.IsNullOrEmpty(nameToSend)) { RequestTitleChange(nameToSend); }
            }
        }
        
        private void StartAnimation()
        {
            _isAnimating = true;
            _animationText = _nameInput;
            _animationIndex = 0;
            _isAnimatingForward = true;
            _rainbowHue = 0f;
        }

        private void StopAnimation(bool clearName = false)
        {
            _isAnimating = false;
            if (clearName) RequestTitleChange("");
            else RequestTitleChange(_nameInput);
        }

        private void OnGUI()
        {
            if (_showMenu)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                _windowRect = GUILayout.Window(1863, _windowRect, DrawSettingsWindow, "Roleplay Title Settings");
            }
        }

        private void DrawSettingsWindow(int windowID)
        {
            GUILayout.Label("Set your custom roleplay title.");
            GUILayout.Label("Appends to your @GlobalName.");
            GUILayout.Space(10);
            
            _nameInput = GUILayout.TextField(_nameInput, 50);

            AnimationEnabled.Value = GUILayout.Toggle(AnimationEnabled.Value, " Enable Animation");

            if (AnimationEnabled.Value)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("Animation Type:", GUILayout.Width(120));
                if (GUILayout.Button(SelectedAnimationType.Value.ToString())) { _showAnimationPicker = !_showAnimationPicker; }
                GUILayout.EndHorizontal();

                if (_showAnimationPicker)
                {
                    string[] animationNames = Enum.GetNames(typeof(AnimationType)).Where(n => n != "Static").ToArray();
                    int currentSelection = Array.IndexOf(animationNames, SelectedAnimationType.Value.ToString());
                    int newSelection = GUILayout.SelectionGrid(currentSelection, animationNames, 3);
                    if (newSelection != currentSelection && newSelection >= 0)
                    {
                        SelectedAnimationType.Value = (AnimationType)Enum.Parse(typeof(AnimationType), animationNames[newSelection]);
                        _showAnimationPicker = false;
                    }
                }

                if (SelectedAnimationType.Value == AnimationType.Marquee)
                {
                    GUILayout.Label($"Marquee Width: {MarqueeWidth.Value}");
                    MarqueeWidth.Value = (int)GUILayout.HorizontalSlider(MarqueeWidth.Value, 5, 40);
                }
            }
            
            if (GUILayout.Button("Set & Save Name"))
            {
                RoleplayName.Value = _nameInput;
                Config.Save();
                if (AnimationEnabled.Value && !string.IsNullOrEmpty(_nameInput) && SelectedAnimationType.Value != AnimationType.Static) { StartAnimation(); }
                else { StopAnimation(); }
                _showMenu = false;
            }

            if (GUILayout.Button("Clear Name"))
            {
                _nameInput = "";
                RoleplayName.Value = "";
                Config.Save();
                StopAnimation(clearName: true);
            }
            
            GUI.DragWindow();
        }
    }
    
    // ============== HARMONY PATCHES ==============
    [HarmonyPatch]
    internal static class HarmonyPatches
    {
        private static bool _listenersInitialized = false;
        internal static readonly Dictionary<ulong, string> PlayerTitles = new Dictionary<ulong, string>();
        private static readonly FieldInfo _globalNicknameTextMeshField = AccessTools.Field(typeof(Player), "_globalNicknameTextMesh");
        private static float _cleanupTimer = 0f;
        private const float CLEANUP_INTERVAL = 5f;
        
        [HarmonyPostfix, HarmonyPatch(typeof(Player), "OnGameConditionChange")]
        private static void OnGameConditionChange_Postfix(Player __instance, GameCondition _newCondition)
        {
            if (__instance != Player._mainPlayer) return;

            if (_newCondition == GameCondition.IN_GAME)
            {
                Main.IsReady = true;
                if (!Main.AnimationEnabled.Value || Main.SelectedAnimationType.Value == AnimationType.Static)
                {
                    string savedName = Main.RoleplayName.Value;
                    if (!string.IsNullOrEmpty(savedName)) { Main.RequestTitleChange(savedName); }
                }
                if (Main._isCodeTalkerLoaded && !__instance._isHostPlayer) { Main.RequestFullTitleSync(); }
            }
            else { Main.IsReady = false; }
        }

        [HarmonyPostfix, HarmonyPatch(typeof(Player), "OnStartAuthority")]
        private static void OnPlayerStart_Postfix(Player __instance)
        {
            if (!__instance.isLocalPlayer) return;

            if (Main._isCodeTalkerLoaded && !_listenersInitialized)
            {
                // Everyone listens for the authoritative broadcast from the host.
                CodeTalkerNetwork.RegisterListener<Packets.SetRoleplayTitlePacket>(Main.OnTitleBroadcast);

                if (__instance._isHostPlayer)
                {
                    // Only the host listens for change requests from clients and sync requests.
                    CodeTalkerNetwork.RegisterListener<Packets.SetRoleplayTitlePacket>(Main.OnTitleUpdateRequest);
                    CodeTalkerNetwork.RegisterListener<Packets.RequestAllTitlesPacket>(Main.OnSyncRequest);
                }
                _listenersInitialized = true;
            }
        }
        
        [HarmonyPostfix, HarmonyPatch(typeof(Player), "Update")]
        private static void PlayerUpdate_Postfix(Player __instance)
        {
            if (_globalNicknameTextMeshField.GetValue(__instance) is not TextMeshPro textMesh) return;
            
            string originalGlobalName = __instance._globalNickname;
            
            ulong playerId = (ulong)(__instance.netIdentity?.connectionToClient?.connectionId ?? 0);
            PlayerTitles.TryGetValue(playerId, out string customTitle);
            
            string finalDisplayString;

            if (!string.IsNullOrEmpty(originalGlobalName) && !string.IsNullOrEmpty(customTitle)) { finalDisplayString = $"@{originalGlobalName} ({customTitle})"; }
            else if (!string.IsNullOrEmpty(originalGlobalName)) { finalDisplayString = "@" + originalGlobalName; }
            else if (!string.IsNullOrEmpty(customTitle)) { finalDisplayString = customTitle; }
            else { finalDisplayString = ""; }

            if (textMesh.text != finalDisplayString) { textMesh.text = finalDisplayString; }

            bool shouldBeEnabled = !string.IsNullOrEmpty(finalDisplayString);
            if (textMesh.gameObject.activeSelf != shouldBeEnabled) { textMesh.gameObject.SetActive(shouldBeEnabled); }
        }
    }
}