// ============== USING STATEMENTS ==============
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using System;
using System.Linq; // Added for the animation type picker
// Required for the packet-based CodeTalker implementation
using CodeTalker.Networking;
using CodeTalker.Packets;
using Newtonsoft.Json; // CodeTalker uses Newtonsoft.Json for serialization

// ============== NAMESPACE DEFINITIONS ==============
namespace RoleplayTitles
{
    // Enum to define the available animation types.
    public enum AnimationType
    {
        Static,
        Scroll,
        Rainbow,
        Marquee
    }
    
    // Packet definitions for network communication.
    namespace Packets
    {
        public class SetRoleplayNamePacket : PacketBase
        {
            public override string PacketSourceGUID => ModInfo.GUID;
            [JsonProperty] public string DesiredName { get; set; }
            public SetRoleplayNamePacket() { }
            public SetRoleplayNamePacket(string name) { DesiredName = name; }
        }
    }

    // ============== MAIN PLUGIN CLASS ==============
    [BepInPlugin(ModInfo.GUID, ModInfo.NAME, ModInfo.VERSION)]
    [BepInDependency("CodeTalker", BepInDependency.DependencyFlags.SoftDependency)]
    public class Main : BaseUnityPlugin
    {
        internal static ManualLogSource Log;
        
        // --- Configuration & UI ---
        internal static ConfigEntry<string> RoleplayName;
        private ConfigEntry<KeyCode> _menuKey;
        
        // --- Animation Configuration ---
        internal static ConfigEntry<bool> AnimationEnabled;
        internal static ConfigEntry<AnimationType> SelectedAnimationType;
        internal static ConfigEntry<float> AnimationSpeed;
        internal static ConfigEntry<int> MarqueeWidth;

        private bool _showMenu = false;
        private Rect _windowRect = new Rect(20, 20, 320, 300);
        private string _nameInput = "";
        private bool _showAnimationPicker = false;
        internal static bool _isCodeTalkerLoaded = false; // Changed to internal
        
        // --- Animation State Variables ---
        private bool _isAnimating = false;
        private string _animationText = "";
        private float _animationTimer = 0f;
        
        private int _animationIndex = 0;
        private bool _isAnimatingForward = true;
        private float _rainbowHue = 0f;

        private void Awake()
        {
            Log = Logger;
            
            // --- Configuration Binding ---
            RoleplayName = Config.Bind("1. General", "Saved Name", "", "Your saved roleplay name. Supports TextMeshPro color tags.");
            _menuKey = Config.Bind("1. General", "Menu Key", KeyCode.F8, "The key to press to show/hide the settings menu.");
            
            // --- Animation Binding ---
            AnimationEnabled = Config.Bind("2. Animation", "Enable Animation", false, "Animate your roleplay name.");
            SelectedAnimationType = Config.Bind("2. Animation", "Animation Type", AnimationType.Scroll, "The type of animation to apply (Scroll, Rainbow, Marquee).");
            AnimationSpeed = Config.Bind("2. Animation", "Animation Speed", 0.15f, "Delay in seconds between animation frames (lower is faster).");
            MarqueeWidth = Config.Bind("2. Animation", "Marquee Width", 16, "The character width of the Marquee scrolling animation.");

            _nameInput = RoleplayName.Value;

            // CORRECTED: Only check for CodeTalker's existence here. Do not register listeners yet.
            if (Type.GetType("CodeTalker.Networking.CodeTalkerNetwork, CodeTalker") != null)
            {
                _isCodeTalkerLoaded = true;
                Log.LogInfo("CodeTalker found. Network features will be enabled when the player is ready.");
            }
            else
            {
                Log.LogWarning("CodeTalker not found. Multiplayer features will be disabled.");
            }
            
            if (AnimationEnabled.Value && !string.IsNullOrEmpty(_nameInput))
            {
                StartAnimation();
            }
            
            Harmony.CreateAndPatchAll(typeof(HarmonyPatches));
            Log.LogInfo($"[{ModInfo.NAME} v{ModInfo.VERSION}] has loaded.");
        }
        
        public static void OnNameChangeRequestReceived(PacketHeader header, PacketBase packet)
        {
            if (packet is Packets.SetRoleplayNamePacket namePacket)
            {
                foreach (Player player in FindObjectsOfType<Player>())
                {
                    if (player.connectionToClient != null && (ulong)player.connectionToClient.connectionId == header.SenderID)
                    {
                        player.Network_globalNickname = namePacket.DesiredName;
                        return;
                    }
                }
            }
        }
        
        public static void SendNameToHost(string name)
        {
            if (!_isCodeTalkerLoaded) return;
            CodeTalkerNetwork.SendNetworkPacket(new Packets.SetRoleplayNamePacket(name));
        }

        private void Update()
        {
            if (Input.GetKeyDown(_menuKey.Value))
            {
                _showMenu = !_showMenu;
                if (!_showMenu) _showAnimationPicker = false;
            }

            if (!_isAnimating) return;
            
            _animationTimer += Time.deltaTime;
            if (_animationTimer >= AnimationSpeed.Value)
            {
                _animationTimer = 0f;
                string nameToSend = "";

                switch (SelectedAnimationType.Value)
                {
                    case AnimationType.Scroll:
                        if (string.IsNullOrEmpty(_animationText)) break;
                        if (_isAnimatingForward)
                        {
                            _animationIndex++;
                            if (_animationIndex >= _animationText.Length) { _animationIndex = _animationText.Length; _isAnimatingForward = false; }
                        }
                        else
                        {
                            _animationIndex--;
                            if (_animationIndex <= 1) { _animationIndex = 1; _isAnimatingForward = true; }
                        }
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

                if (!string.IsNullOrEmpty(nameToSend))
                {
                    SendNameToHost(nameToSend);
                }
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
            if (clearName) SendNameToHost("");
            else SendNameToHost(_nameInput);
        }

        private void OnGUI()
        {
            if (_showMenu)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                _windowRect = GUILayout.Window(1863, _windowRect, DrawSettingsWindow, "Roleplay Name Settings");
            }
        }

        private void DrawSettingsWindow(int windowID)
        {
            GUILayout.Label("Set your global roleplay name/title.");
            GUILayout.Label("Supports TextMeshPro tags, e.g., <color=red>Name</color>");
            GUILayout.Space(10);
            
            _nameInput = GUILayout.TextField(_nameInput, 50);

            AnimationEnabled.Value = GUILayout.Toggle(AnimationEnabled.Value, " Enable Animation");

            if (AnimationEnabled.Value)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("Animation Type:", GUILayout.Width(120));
                if (GUILayout.Button(SelectedAnimationType.Value.ToString()))
                {
                    _showAnimationPicker = !_showAnimationPicker;
                }
                GUILayout.EndHorizontal();

                if (_showAnimationPicker)
                {
                    string[] animationNames = Enum.GetNames(typeof(AnimationType)).Where(n => n != "Static").ToArray();
                    int currentSelection = Array.IndexOf(animationNames, SelectedAnimationType.Value.ToString());
                    int newSelection = GUILayout.SelectionGrid(currentSelection, animationNames, 3);
                    if (newSelection != currentSelection)
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

                if (AnimationEnabled.Value && !string.IsNullOrEmpty(_nameInput) && SelectedAnimationType.Value != AnimationType.Static)
                {
                    StartAnimation();
                }
                else
                {
                    StopAnimation();
                }
                
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

        [HarmonyPostfix, HarmonyPatch(typeof(Player), "OnStartAuthority")]
        private static void OnPlayerStart_Postfix(Player __instance)
        {
            if (!__instance.isLocalPlayer) return;

            // CORRECTED: This is the proper place to initialize network listeners.
            if (Main._isCodeTalkerLoaded && !_listenersInitialized)
            {
                Main.Log.LogInfo("Player has authority. Initializing CodeTalker listeners...");
                
                // Only the host needs to listen for name change requests.
                // We check the player's _isHostPlayer flag, which is now reliable.
                if (__instance._isHostPlayer)
                {
                    CodeTalkerNetwork.RegisterListener<Packets.SetRoleplayNamePacket>(Main.OnNameChangeRequestReceived);
                    Main.Log.LogInfo("Host detected. Listening for name change requests.");
                }
                _listenersInitialized = true;
            }
            
            // This part of the logic remains the same. It runs after listeners are set up.
            if (!Main.AnimationEnabled.Value || Main.SelectedAnimationType.Value == AnimationType.Static)
            {
                string savedName = Main.RoleplayName.Value;
                if (!string.IsNullOrEmpty(savedName))
                {
                    Main.Log.LogInfo("Player authority started. Sending saved roleplay name to host.");
                    Main.SendNameToHost(savedName);
                }
            }
        }
    }
}