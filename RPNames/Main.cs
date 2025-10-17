// ============== USING STATEMENTS ==============
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using System;
using System.Collections.Generic;
using System.IO; // Required for MemoryStream, BinaryWriter, BinaryReader
using System.Linq;
using System.Reflection;
using System.Text; // Required for StringBuilder
using TMPro;
// Required for the packet-based CodeTalker implementation
using CodeTalker.Networking;
using CodeTalker.Packets;
using Newtonsoft.Json;

// ============== NAMESPACE DEFINITIONS ==============
namespace RPNames
{
    public class CharacterTitleProfile
    {
        [JsonIgnore] public uint TargetNetID { get; set; }
        public string Title { get; set; } = "";
        public bool TitleOnNewLine { get; set; } = true;
        public bool AddGapAboveTitle { get; set; } = true;
        public BracketType BracketStyle { get; set; } = BracketType.Parentheses;
        public TextAnimationType TextAnimation { get; set; } = TextAnimationType.Static;
        public ColoringType Coloring { get; set; } = ColoringType.None;
        public float AnimationSpeed { get; set; } = 0.25f; // Default speed slowed down
        public int MarqueeWidth { get; set; } = 16;
        public string SingleHexColor { get; set; } = "FFFFFF";
        public string GradientStartColor { get; set; } = "FF0000";
        public string GradientEndColor { get; set; } = "0000FF";
        public bool AnimateGradient { get; set; } = false;
        public float GradientSpread { get; set; } = 10f;
        public float RainbowWaveSpread { get; set; } = 15f;
        public float ColorAnimationSpeed { get; set; } = 1f;
    }

    public enum TextAnimationType { Static, Scroll, Marquee, Typewriter }
    public enum ColoringType { None, SingleColor, Rainbow, Gradient, Wave, StaticRainbow }
    public enum BracketType { None, Parentheses, SquareBrackets, Tilde, Dash, Plus, Equals, Asterisk, Dollar, Hash, Exclamation, Pipe }

    // Manages the animation state for a single player
    internal class PlayerTitleAnimator
    {
        public uint NetId;
        public float AnimationTimer = 0f;
        public int AnimationIndex = 0;
        public bool IsAnimatingForward = true;
        public float RainbowHue = 0f;
        public Main.TypewriterState TypewriterState = Main.TypewriterState.Typing;
        public float TypewriterPauseTimer = 0f;
        public bool TypewriterCursorVisible = true;
        public float TypewriterBlinkTimer = 0f;

        public PlayerTitleAnimator(uint netId) { NetId = netId; }
    }

    namespace Packets
    {
        internal static class ProfileSerializer
        {
            public static void WriteProfile(BinaryWriter writer, CharacterTitleProfile profile)
            {
                if (profile == null) { writer.Write(false); return; }
                writer.Write(true);
                writer.Write(profile.Title);
                writer.Write(profile.TitleOnNewLine);
                writer.Write(profile.AddGapAboveTitle);
                writer.Write((byte)profile.BracketStyle);
                writer.Write((byte)profile.TextAnimation);
                writer.Write((byte)profile.Coloring);
                writer.Write(profile.AnimationSpeed);
                writer.Write(profile.MarqueeWidth);
                writer.Write(profile.SingleHexColor);
                writer.Write(profile.GradientStartColor);
                writer.Write(profile.GradientEndColor);
                writer.Write(profile.AnimateGradient);
                writer.Write(profile.GradientSpread);
                writer.Write(profile.RainbowWaveSpread);
                writer.Write(profile.ColorAnimationSpeed);
            }

            public static CharacterTitleProfile ReadProfile(BinaryReader reader)
            {
                if (!reader.ReadBoolean()) return null;
                return new CharacterTitleProfile
                {
                    Title = reader.ReadString(),
                    TitleOnNewLine = reader.ReadBoolean(),
                    AddGapAboveTitle = reader.ReadBoolean(),
                    BracketStyle = (BracketType)reader.ReadByte(),
                    TextAnimation = (TextAnimationType)reader.ReadByte(),
                    Coloring = (ColoringType)reader.ReadByte(),
                    AnimationSpeed = reader.ReadSingle(),
                    MarqueeWidth = reader.ReadInt32(),
                    SingleHexColor = reader.ReadString(),
                    GradientStartColor = reader.ReadString(),
                    GradientEndColor = reader.ReadString(),
                    AnimateGradient = reader.ReadBoolean(),
                    GradientSpread = reader.ReadSingle(),
                    RainbowWaveSpread = reader.ReadSingle(),
                    ColorAnimationSpeed = reader.ReadSingle()
                };
            }
        }

        public class UpdateTitleProfilePacket : BinaryPacketBase
        {
            public override string PacketSignature => ModInfo.GUID + "_UpdateProfile";
            public uint TargetNetID { get; set; }
            public CharacterTitleProfile Profile { get; set; }
            public UpdateTitleProfilePacket() { }
            public UpdateTitleProfilePacket(uint targetId, CharacterTitleProfile profile) { TargetNetID = targetId; Profile = profile; }

            public override byte[] Serialize() { using (var ms = new MemoryStream()) { using (var writer = new BinaryWriter(ms)) { writer.Write(TargetNetID); ProfileSerializer.WriteProfile(writer, Profile); } return ms.ToArray(); } }
            public override void Deserialize(byte[] data) { using (var ms = new MemoryStream(data)) { using (var reader = new BinaryReader(ms)) { TargetNetID = reader.ReadUInt32(); Profile = ProfileSerializer.ReadProfile(reader); } } }
        }

        public class SyncAllProfilesPacket : BinaryPacketBase
        {
            public override string PacketSignature => ModInfo.GUID + "_SyncAll";
            public Dictionary<uint, CharacterTitleProfile> AllProfiles { get; set; }
            public SyncAllProfilesPacket() { }
            public SyncAllProfilesPacket(Dictionary<uint, CharacterTitleProfile> profiles) { AllProfiles = profiles; }

            public override byte[] Serialize() { using (var ms = new MemoryStream()) { using (var writer = new BinaryWriter(ms)) { writer.Write(AllProfiles.Count); foreach (var entry in AllProfiles) { writer.Write(entry.Key); ProfileSerializer.WriteProfile(writer, entry.Value); } } return ms.ToArray(); } }
            public override void Deserialize(byte[] data) { using (var ms = new MemoryStream(data)) { using (var reader = new BinaryReader(ms)) { int count = reader.ReadInt32(); AllProfiles = new Dictionary<uint, CharacterTitleProfile>(count); for (int i = 0; i < count; i++) { AllProfiles.Add(reader.ReadUInt32(), ProfileSerializer.ReadProfile(reader)); } } } }
        }

        public class RequestAllTitlesPacket : PacketBase { public override string PacketSourceGUID => ModInfo.GUID; }
    }

    [BepInPlugin(ModInfo.GUID, ModInfo.NAME, ModInfo.VERSION)]
    [BepInDependency("CodeTalker", BepInDependency.DependencyFlags.SoftDependency)]
    public class Main : BaseUnityPlugin
    {
        public static Main instance;
        internal static ManualLogSource Log;
        internal static bool IsReady = false;
        
        private static ConfigEntry<string> _characterProfilesJson;
        internal static Dictionary<int, CharacterTitleProfile> AllCharacterProfiles = new Dictionary<int, CharacterTitleProfile>();
        internal static int CurrentCharacterSlot = -1;
        
        private ConfigEntry<KeyCode> _menuKey;

        private Rect _windowRect = new Rect(20, 20, 340, 720);
        private CharacterTitleProfile _uiEditingProfile = new CharacterTitleProfile();
        private bool _showTextAnimPicker = false;
        private bool _showColoringPicker = false;
        private bool _showBracketPicker = false;
        private bool _showPresetPicker = false;
        private Vector2 _presetScrollPosition = Vector2.zero;
        internal static bool _isCodeTalkerLoaded = false;
        
        private static CharacterTitleProfile _copiedProfileBuffer = null;
        
        internal static Dictionary<uint, PlayerTitleAnimator> AllPlayerAnimators = new Dictionary<uint, PlayerTitleAnimator>();
        
        private Color _gradientStartColorCache;
        private Color _gradientEndColorCache;

        private static readonly List<string> _presetTitles = new List<string>();
        
        internal enum TypewriterState { Typing, Blinking, Backspacing }

        private bool _showSingleColorPicker = false;
        private bool _showGradientStartPicker = false;
        private bool _showGradientEndPicker = false;
        private Color _uiEditingColor;
        
        private enum MenuState { Closed, Opening, Open, Closing }
        private MenuState _menuState = MenuState.Closed;
        private float _animationProgress = 0f;
        private const float AnimationDuration = 0.25f;
        private bool _stylesInitialized = false;
        private GUISkin _modSkin;
        public static GUISkin OriginalSkin;

        private void Awake()
        {
            instance = this;
            Log = Logger;
            _menuKey = Config.Bind("1. General", "Menu Key", KeyCode.F8, "The key to press to show/hide the settings menu.");
            _characterProfilesJson = Config.Bind("2. Data", "CharacterProfiles", "{}", "Stores all title profiles for all character slots in JSON format.");
            
            LoadProfiles();
            PopulatePresetTitles();
            if (Type.GetType("CodeTalker.Networking.CodeTalkerNetwork, CodeTalker") != null) _isCodeTalkerLoaded = true;
            Harmony.CreateAndPatchAll(typeof(HarmonyPatches));
            Log.LogInfo($"[{ModInfo.NAME} v{ModInfo.VERSION}] has loaded.");
        }

        private void InitializeGUIStyles()
        {
            if (OriginalSkin == null) OriginalSkin = GUI.skin;
            _modSkin = Instantiate(GUI.skin);
            
            Texture2D MakeTex(Color col) 
            { 
                var tex = new Texture2D(1, 1); 
                tex.SetPixel(0, 0, col); 
                tex.Apply(); 
                tex.hideFlags = HideFlags.HideAndDontSave;
                return tex; 
            }
            
            _modSkin.window = new GUIStyle(GUI.skin.window) { normal = { background = MakeTex(new Color(0.1f, 0.12f, 0.15f, 0.97f)), textColor = new Color(0.9f, 0.9f, 0.9f) }, onNormal = { background = MakeTex(new Color(0.1f, 0.12f, 0.15f, 0.97f)), textColor = new Color(0.9f, 0.9f, 0.9f) }, padding = new RectOffset(15, 15, 30, 15), border = new RectOffset(2, 2, 2, 2), alignment = TextAnchor.UpperCenter, fontSize = 16, fontStyle = FontStyle.Bold };
            _modSkin.box = new GUIStyle(GUI.skin.box) { normal = { background = MakeTex(new Color(0.2f, 0.22f, 0.25f, 0.5f)) } };
            _modSkin.label.normal.textColor = new Color(0.85f, 0.85f, 0.85f);
            _modSkin.button = new GUIStyle(GUI.skin.button) { normal = { background = MakeTex(new Color(0.3f, 0.35f, 0.4f)), textColor = Color.white }, hover = { background = MakeTex(new Color(0.4f, 0.45f, 0.5f)), textColor = Color.white }, active = { background = MakeTex(new Color(0.2f, 0.25f, 0.3f)), textColor = Color.white }, onNormal = { background = MakeTex(new Color(0.5f, 0.55f, 0.6f)), textColor = Color.white }, onHover = { background = MakeTex(new Color(0.55f, 0.6f, 0.65f)), textColor = Color.white }, border = new RectOffset(4, 4, 4, 4), padding = new RectOffset(8, 8, 8, 8) };
            
            _modSkin.toggle = new GUIStyle(_modSkin.toggle) { padding = new RectOffset(25, 0, 3, 3) };
            Texture2D MakeToggleTex(bool on) { var tex = new Texture2D(16, 16); var bgColor = new Color(0.2f, 0.22f, 0.25f); var borderColor = new Color(0.4f, 0.45f, 0.5f); var checkColor = new Color(0.8f, 0.85f, 0.9f); for (int y = 0; y < 16; y++) for (int x = 0; x < 16; x++) { if (x < 1 || x > 14 || y < 1 || y > 14) tex.SetPixel(x, y, borderColor); else tex.SetPixel(x, y, bgColor); } if (on) for (int y = 4; y < 12; y++) for (int x = 4; x < 12; x++) tex.SetPixel(x, y, checkColor); tex.Apply(); tex.hideFlags = HideFlags.HideAndDontSave; return tex; }
            _modSkin.toggle.normal.background = MakeToggleTex(false); _modSkin.toggle.onNormal.background = MakeToggleTex(true); _modSkin.toggle.hover.background = MakeToggleTex(false); _modSkin.toggle.onHover.background = MakeToggleTex(true);
            
            _modSkin.textField = new GUIStyle(GUI.skin.textField) { normal = { background = MakeTex(new Color(0.05f, 0.05f, 0.05f)), textColor = Color.white }, padding = new RectOffset(5, 5, 5, 5) };
            _modSkin.horizontalSlider = new GUIStyle(GUI.skin.horizontalSlider) { normal = { background = MakeTex(new Color(0.15f, 0.17f, 0.2f)), }, fixedHeight = 10, border = new RectOffset(2, 2, 2, 2) };
            _modSkin.horizontalSliderThumb = new GUIStyle(GUI.skin.horizontalSliderThumb) { normal = { background = MakeTex(new Color(0.6f, 0.65f, 0.7f)) }, hover = { background = MakeTex(new Color(0.7f, 0.75f, 0.8f)) }, active = { background = MakeTex(new Color(0.5f, 0.55f, 0.6f)) }, fixedHeight = 18, fixedWidth = 10 };
            
            _stylesInitialized = true;
        }

        private void LoadProfiles() { try { var loadedProfiles = JsonConvert.DeserializeObject<Dictionary<int, CharacterTitleProfile>>(_characterProfilesJson.Value); if (loadedProfiles != null) AllCharacterProfiles = loadedProfiles; } catch { AllCharacterProfiles = new Dictionary<int, CharacterTitleProfile>(); } }
        private void SaveProfiles() { _characterProfilesJson.Value = JsonConvert.SerializeObject(AllCharacterProfiles, Formatting.Indented); Config.Save(); }

        private void PopulatePresetTitles() { _presetTitles.Clear(); _presetTitles.AddRange(new[] { "The Explorer", "The Patient", "Dragonslayer", "Scarab Lord", "The Undying", "The Insane", "Grand Marshal", "High Warlord", "Arena Master", "Salty", "Chef", "Guardian of Cenarius", "Hand of A'dal", "Master Angler" }); }
        
        public static void OnProfileUpdate(PacketHeader header, BinaryPacketBase packet)
        {
            if (packet is Packets.UpdateTitleProfilePacket profilePacket)
            {
                ApplyProfileUpdate(profilePacket.TargetNetID, profilePacket.Profile);
            }
        }
        
        public static void OnFullSync(PacketHeader header, BinaryPacketBase packet)
        {
            if (packet is Packets.SyncAllProfilesPacket syncPacket)
            {
                foreach (var entry in syncPacket.AllProfiles) ApplyProfileUpdate(entry.Key, entry.Value);
            }
        }
        
        private static void ApplyProfileUpdate(uint netId, CharacterTitleProfile profile)
        {
            if (profile == null || string.IsNullOrEmpty(profile.Title))
            {
                if (HarmonyPatches.PlayerProfiles.ContainsKey(netId)) HarmonyPatches.PlayerProfiles.Remove(netId);
                if (HarmonyPatches.CurrentPlayerTitles.ContainsKey(netId)) HarmonyPatches.CurrentPlayerTitles.Remove(netId);
                if (AllPlayerAnimators.ContainsKey(netId)) AllPlayerAnimators.Remove(netId);
            }
            else
            {
                profile.TargetNetID = netId;
                if (HarmonyPatches.PlayerProfiles.ContainsKey(netId)) HarmonyPatches.PlayerProfiles[netId] = profile;
                else HarmonyPatches.PlayerProfiles.Add(netId, profile);
                
                if (instance.ShouldAnimate(profile)) { if (!AllPlayerAnimators.ContainsKey(netId)) AllPlayerAnimators.Add(netId, new PlayerTitleAnimator(netId)); }
                else
                {
                    if (AllPlayerAnimators.ContainsKey(netId)) AllPlayerAnimators.Remove(netId);
                    string staticTitle = instance.ApplyColoring(profile.Title, profile, null);
                    if (HarmonyPatches.CurrentPlayerTitles.ContainsKey(netId)) HarmonyPatches.CurrentPlayerTitles[netId] = staticTitle;
                    else HarmonyPatches.CurrentPlayerTitles.Add(netId, staticTitle);
                }
            }
        }
        
        public static void OnSyncRequest(PacketHeader header, PacketBase packet)
        {
            if (packet is Packets.RequestAllTitlesPacket)
            {
                CodeTalkerNetwork.SendBinaryNetworkPacket(new Packets.SyncAllProfilesPacket(HarmonyPatches.PlayerProfiles));
            }
        }

        public static void SendTitleUpdate(CharacterTitleProfile profile)
        {
            uint myNetId = Player._mainPlayer?.netId ?? 0;
            if (myNetId == 0) return;

            if(_isCodeTalkerLoaded) CodeTalkerNetwork.SendBinaryNetworkPacket(new Packets.UpdateTitleProfilePacket(myNetId, profile));
            ApplyProfileUpdate(myNetId, profile); // Apply to self immediately
        }
        
        public static void RequestFullTitleSync() { if (_isCodeTalkerLoaded) CodeTalkerNetwork.SendNetworkPacket(new Packets.RequestAllTitlesPacket()); }

        private void Update()
        {
            switch (_menuState) { case MenuState.Opening: _animationProgress = Mathf.Clamp01(_animationProgress + Time.unscaledDeltaTime / AnimationDuration); if (_animationProgress >= 1f) _menuState = MenuState.Open; break; case MenuState.Closing: _animationProgress = Mathf.Clamp01(_animationProgress - Time.unscaledDeltaTime / AnimationDuration); if (_animationProgress <= 0f) _menuState = MenuState.Closed; break; }
            if (Input.GetKeyDown(_menuKey.Value)) { if (_menuState == MenuState.Open || _menuState == MenuState.Opening) { _menuState = MenuState.Closing; _showTextAnimPicker = _showColoringPicker = _showBracketPicker = _showPresetPicker = _showSingleColorPicker = _showGradientStartPicker = _showGradientEndPicker = false; } else { _menuState = MenuState.Opening; LoadUIFromProfile(CurrentCharacterSlot >= 0 ? CurrentCharacterSlot : 0); } }

            foreach (var animator in AllPlayerAnimators.Values.ToList())
            {
                if (!HarmonyPatches.PlayerProfiles.TryGetValue(animator.NetId, out var profile) || profile == null) { AllPlayerAnimators.Remove(animator.NetId); continue; }
                
                bool needsFrameUpdate = false;
                animator.AnimationTimer += Time.deltaTime;
                if (animator.AnimationTimer >= profile.AnimationSpeed) { animator.AnimationTimer = 0f; needsFrameUpdate = true; }

                if (ShouldAnimateColor(profile))
                {
                    animator.RainbowHue = (animator.RainbowHue + (Time.deltaTime * profile.ColorAnimationSpeed * 0.1f)) % 1.0f;
                    needsFrameUpdate = true;
                }
                
                if (profile.TextAnimation == TextAnimationType.Typewriter && animator.TypewriterState == TypewriterState.Blinking) { animator.TypewriterPauseTimer += Time.deltaTime; animator.TypewriterBlinkTimer += Time.deltaTime; if (animator.TypewriterBlinkTimer >= 0.5f) { animator.TypewriterBlinkTimer = 0f; animator.TypewriterCursorVisible = !animator.TypewriterCursorVisible; needsFrameUpdate = true; } if (animator.TypewriterPauseTimer >= 5f) { animator.TypewriterPauseTimer = 0f; animator.TypewriterState = TypewriterState.Backspacing; } }
                else if (needsFrameUpdate && profile.TextAnimation == TextAnimationType.Typewriter) { switch (animator.TypewriterState) { case TypewriterState.Typing: if (animator.AnimationIndex < profile.Title.Length) animator.AnimationIndex++; else { animator.TypewriterState = TypewriterState.Blinking; animator.TypewriterPauseTimer = animator.TypewriterBlinkTimer = 0f; animator.TypewriterCursorVisible = true; } break; case TypewriterState.Backspacing: if (animator.AnimationIndex > 0) animator.AnimationIndex--; else animator.TypewriterState = TypewriterState.Typing; break; } }
                
                if (needsFrameUpdate)
                {
                    string animatedText = GetAnimatedText(profile, animator);
                    string finalTitle = ApplyColoring(animatedText, profile, animator);
                    if (HarmonyPatches.CurrentPlayerTitles.ContainsKey(animator.NetId)) HarmonyPatches.CurrentPlayerTitles[animator.NetId] = finalTitle; else HarmonyPatches.CurrentPlayerTitles.Add(animator.NetId, finalTitle);
                }
            }
        }
        
        private string GetAnimatedText(CharacterTitleProfile profile, PlayerTitleAnimator animator)
        {
            if (string.IsNullOrEmpty(profile.Title)) return "";
            switch (profile.TextAnimation)
            {
                case TextAnimationType.Scroll: if (animator.IsAnimatingForward) { animator.AnimationIndex++; if (animator.AnimationIndex >= profile.Title.Length) { animator.AnimationIndex = profile.Title.Length; animator.IsAnimatingForward = false; } } else { animator.AnimationIndex--; if (animator.AnimationIndex <= 1) { animator.AnimationIndex = 1; animator.IsAnimatingForward = true; } } return profile.Title.Substring(0, animator.AnimationIndex);
                case TextAnimationType.Marquee: string padding = new string(' ', profile.MarqueeWidth); string fullMarqueeText = padding + profile.Title + padding; animator.AnimationIndex = (animator.AnimationIndex + 1) % (fullMarqueeText.Length - profile.MarqueeWidth); return fullMarqueeText.Substring(animator.AnimationIndex, profile.MarqueeWidth);
                case TextAnimationType.Typewriter: if(animator.AnimationIndex > profile.Title.Length) animator.AnimationIndex = profile.Title.Length; string visibleText = profile.Title.Substring(0, animator.AnimationIndex); if (animator.TypewriterState == TypewriterState.Blinking) return profile.Title + (animator.TypewriterCursorVisible ? "|" : ""); return visibleText + "|";
                default: return profile.Title;
            }
        }
        
        internal string ApplyColoring(string text, CharacterTitleProfile profile, PlayerTitleAnimator animator)
        {
            if (string.IsNullOrEmpty(text)) return "";
            float hue = animator?.RainbowHue ?? 0f;

            switch (profile.Coloring)
            {
                case ColoringType.SingleColor: return $"<color=#{profile.SingleHexColor.Replace("#", "")}>{text}</color>";
                case ColoringType.Rainbow: return $"<color=#{ColorUtility.ToHtmlStringRGB(Color.HSVToRGB(hue, 1f, 1f))}>{text}</color>";
                case ColoringType.Gradient:
                    Color start = (profile.TargetNetID == Player._mainPlayer?.netId) ? _gradientStartColorCache : Color.white; Color end = (profile.TargetNetID == Player._mainPlayer?.netId) ? _gradientEndColorCache : Color.white;
                    if (profile.TargetNetID != Player._mainPlayer?.netId) { ColorUtility.TryParseHtmlString("#" + profile.GradientStartColor, out start); ColorUtility.TryParseHtmlString("#" + profile.GradientEndColor, out end); }
                    StringBuilder gradientBuilder = new StringBuilder();
                    if (profile.AnimateGradient) { for (int i = 0; i < text.Length; i++) { float phase = (hue + (i / profile.GradientSpread)); float t = 1f - Mathf.Abs((phase * 2f) % 2f - 1f); Color charColor = Color.Lerp(start, end, t); gradientBuilder.Append($"<color=#{ColorUtility.ToHtmlStringRGB(charColor)}>{text[i]}</color>"); } }
                    else { for (int i = 0; i < text.Length; i++) { float t = text.Length <= 1 ? 0f : (float)i / (text.Length - 1); Color charColor = Color.Lerp(start, end, t); gradientBuilder.Append($"<color=#{ColorUtility.ToHtmlStringRGB(charColor)}>{text[i]}</color>"); } }
                    return gradientBuilder.ToString();
                case ColoringType.Wave: StringBuilder waveBuilder = new StringBuilder(); for (int i = 0; i < text.Length; i++) { float h = (hue + (i / profile.RainbowWaveSpread)) % 1.0f; waveBuilder.Append($"<color=#{ColorUtility.ToHtmlStringRGB(Color.HSVToRGB(h, 1f, 1f))}>{text[i]}</color>"); } return waveBuilder.ToString();
                case ColoringType.StaticRainbow: StringBuilder staticBuilder = new StringBuilder(); for (int i = 0; i < text.Length; i++) { float h = (i / profile.RainbowWaveSpread) % 1.0f; staticBuilder.Append($"<color=#{ColorUtility.ToHtmlStringRGB(Color.HSVToRGB(h, 1f, 1f))}>{text[i]}</color>"); } return staticBuilder.ToString();
                default: return text;
            }
        }
        
        internal void UpdateGradientCache(CharacterTitleProfile profile) { ColorUtility.TryParseHtmlString("#" + profile.GradientStartColor, out _gradientStartColorCache); ColorUtility.TryParseHtmlString("#" + profile.GradientEndColor, out _gradientEndColorCache); }
        internal bool ShouldAnimate(CharacterTitleProfile profile) => profile != null && !string.IsNullOrEmpty(profile.Title) && (profile.TextAnimation != TextAnimationType.Static || ShouldAnimateColor(profile));
        internal bool ShouldAnimateColor(CharacterTitleProfile profile) => profile.Coloring == ColoringType.Rainbow || profile.Coloring == ColoringType.Wave || (profile.Coloring == ColoringType.Gradient && profile.AnimateGradient);

        private void OnGUI()
        {
            if (_menuState == MenuState.Closed) return;
            if (!_stylesInitialized) InitializeGUIStyles();
            
            var originalSkin = GUI.skin;
            GUI.skin = _modSkin;
            
            Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
            float easeOut(float t) => 1 - Mathf.Pow(1 - t, 3); float currentProgress = easeOut(_animationProgress); float startX = -_windowRect.width; float endX = 20f; Rect animatedRect = _windowRect; animatedRect.x = Mathf.Lerp(startX, endX, currentProgress);
            _windowRect = GUILayout.Window(1863, animatedRect, DrawSettingsWindow, "RPNames Title Settings");

            GUI.skin = originalSkin;
        }
        
        private void LoadUIFromProfile(int slot) { if (slot == -1) { _uiEditingProfile = new CharacterTitleProfile(); return; } if (!AllCharacterProfiles.TryGetValue(slot, out var profile)) { profile = new CharacterTitleProfile(); } _uiEditingProfile = JsonConvert.DeserializeObject<CharacterTitleProfile>(JsonConvert.SerializeObject(profile)); }

        private void DrawSettingsWindow(int windowID)
        {
            if (CurrentCharacterSlot == -1) { GUILayout.FlexibleSpace(); GUILayout.Label("Please load a character to edit their title profile."); GUILayout.FlexibleSpace(); GUI.DragWindow(); return; }
            GUILayout.Label($"Editing Profile for Slot: {CurrentCharacterSlot + 1}");
            _uiEditingProfile.Title = GUILayout.TextField(_uiEditingProfile.Title, 50);
            if (GUILayout.Button("Select a Preset")) _showPresetPicker = !_showPresetPicker;
            if (_showPresetPicker) { _presetScrollPosition = GUILayout.BeginScrollView(_presetScrollPosition, GUILayout.Height(100)); foreach (string preset in _presetTitles) if (GUILayout.Button(preset)) { _uiEditingProfile.Title = preset; _showPresetPicker = false; } GUILayout.EndScrollView(); }
            _uiEditingProfile.TitleOnNewLine = GUILayout.Toggle(_uiEditingProfile.TitleOnNewLine, " Title on New Line");
            if (_uiEditingProfile.TitleOnNewLine) _uiEditingProfile.AddGapAboveTitle = GUILayout.Toggle(_uiEditingProfile.AddGapAboveTitle, " Add Gap Above Title");
            GUILayout.BeginHorizontal(); GUILayout.Label("Bracket Style:", GUILayout.Width(120)); if (GUILayout.Button(_uiEditingProfile.BracketStyle.ToString())) _showBracketPicker = !_showBracketPicker; GUILayout.EndHorizontal();
            if (_showBracketPicker) { string[] names = Enum.GetNames(typeof(BracketType)); int sel = GUILayout.SelectionGrid((int)_uiEditingProfile.BracketStyle, names, 4); if (sel != (int)_uiEditingProfile.BracketStyle) { _uiEditingProfile.BracketStyle = (BracketType)sel; _showBracketPicker = false; } }
            GUILayout.Space(10); GUILayout.Label("--- Text Animation ---"); GUILayout.BeginHorizontal(); GUILayout.Label("Animation:", GUILayout.Width(120)); if (GUILayout.Button(_uiEditingProfile.TextAnimation.ToString())) _showTextAnimPicker = !_showTextAnimPicker; GUILayout.EndHorizontal();
            if (_showTextAnimPicker) { string[] names = Enum.GetNames(typeof(TextAnimationType)); int sel = GUILayout.SelectionGrid((int)_uiEditingProfile.TextAnimation, names, 3); if (sel != (int)_uiEditingProfile.TextAnimation) { _uiEditingProfile.TextAnimation = (TextAnimationType)sel; _showTextAnimPicker = false; } }
            if (_uiEditingProfile.TextAnimation != TextAnimationType.Static)
            {
                float uiSpeed = Mathf.Lerp(10f, 1f, Mathf.InverseLerp(0.05f, 0.5f, _uiEditingProfile.AnimationSpeed));
                GUILayout.Label($"Animation Speed: {uiSpeed:F1}");
                uiSpeed = GUILayout.HorizontalSlider(uiSpeed, 1f, 10f);
                _uiEditingProfile.AnimationSpeed = Mathf.Lerp(0.5f, 0.05f, Mathf.InverseLerp(1f, 10f, uiSpeed));
            }
            if (_uiEditingProfile.TextAnimation == TextAnimationType.Marquee) { GUILayout.Label($"Marquee Width: {_uiEditingProfile.MarqueeWidth}"); _uiEditingProfile.MarqueeWidth = (int)GUILayout.HorizontalSlider(_uiEditingProfile.MarqueeWidth, 5, 40); }
            GUILayout.Space(10); GUILayout.Label("--- Coloring ---"); GUILayout.BeginHorizontal(); GUILayout.Label("Effect:", GUILayout.Width(120)); if (GUILayout.Button(_uiEditingProfile.Coloring.ToString())) _showColoringPicker = !_showColoringPicker; GUILayout.EndHorizontal();
            if (_showColoringPicker) { string[] names = Enum.GetNames(typeof(ColoringType)); int sel = GUILayout.SelectionGrid((int)_uiEditingProfile.Coloring, names, 3); if (sel != (int)_uiEditingProfile.Coloring) { _uiEditingProfile.Coloring = (ColoringType)sel; _showColoringPicker = false; _showSingleColorPicker = _showGradientStartPicker = _showGradientEndPicker = false; } }
            GUIStyle disclaimerStyle = new GUIStyle(_modSkin.label) { fontStyle = FontStyle.Italic, normal = { textColor = Color.gray } };
            Action<Action<string>> DrawColorPicker = (setter) => { GUILayout.BeginVertical(_modSkin.box); GUILayout.BeginHorizontal(); GUILayout.BeginVertical(GUILayout.ExpandWidth(true)); _uiEditingColor.r = GUILayout.HorizontalSlider(_uiEditingColor.r, 0, 1); _uiEditingColor.g = GUILayout.HorizontalSlider(_uiEditingColor.g, 0, 1); _uiEditingColor.b = GUILayout.HorizontalSlider(_uiEditingColor.b, 0, 1); GUILayout.EndVertical(); var oldBgColor = GUI.backgroundColor; GUI.backgroundColor = _uiEditingColor; GUILayout.Box("", new GUIStyle { normal = { background = Texture2D.whiteTexture } }, GUILayout.Width(40), GUILayout.Height(40)); GUI.backgroundColor = oldBgColor; GUILayout.EndHorizontal(); setter(ColorUtility.ToHtmlStringRGB(_uiEditingColor)); GUILayout.EndVertical(); };
            if (_uiEditingProfile.Coloring == ColoringType.SingleColor) { GUILayout.BeginHorizontal(); GUILayout.Label("Hex Color", GUILayout.Width(80)); _uiEditingProfile.SingleHexColor = GUILayout.TextField(_uiEditingProfile.SingleHexColor, 6); if (GUILayout.Button("Pick", GUILayout.Width(50))) { _showSingleColorPicker = !_showSingleColorPicker; _showGradientStartPicker = _showGradientEndPicker = false; if (_showSingleColorPicker && !ColorUtility.TryParseHtmlString("#" + _uiEditingProfile.SingleHexColor, out _uiEditingColor)) _uiEditingColor = Color.white; } GUILayout.EndHorizontal(); if (_showSingleColorPicker) DrawColorPicker(hex => _uiEditingProfile.SingleHexColor = hex); }
            if (_uiEditingProfile.Coloring == ColoringType.Gradient) 
            { 
                _uiEditingProfile.AnimateGradient = GUILayout.Toggle(_uiEditingProfile.AnimateGradient, "Animate Gradient");
                if(_uiEditingProfile.AnimateGradient)
                {
                    GUILayout.Label($"Color Speed: {_uiEditingProfile.ColorAnimationSpeed:F1}");
                    _uiEditingProfile.ColorAnimationSpeed = GUILayout.HorizontalSlider(_uiEditingProfile.ColorAnimationSpeed, 0.1f, 5f);
                }
                GUILayout.Label($"Gradient Spread: {_uiEditingProfile.GradientSpread:F1}"); _uiEditingProfile.GradientSpread = GUILayout.HorizontalSlider(_uiEditingProfile.GradientSpread, 1f, 50f); 
                GUI.enabled = !_uiEditingProfile.AnimateGradient; 
                GUILayout.BeginHorizontal(); GUILayout.Label("Start", GUILayout.Width(50)); _uiEditingProfile.GradientStartColor = GUILayout.TextField(_uiEditingProfile.GradientStartColor, 6); if (GUILayout.Button("Pick", GUILayout.Width(50))) { _showGradientStartPicker = !_showGradientStartPicker; _showSingleColorPicker = _showGradientEndPicker = false; if (_showGradientStartPicker && !ColorUtility.TryParseHtmlString("#" + _uiEditingProfile.GradientStartColor, out _uiEditingColor)) _uiEditingColor = Color.white; } GUILayout.EndHorizontal(); if (_showGradientStartPicker) DrawColorPicker(hex => _uiEditingProfile.GradientStartColor = hex); 
                GUILayout.BeginHorizontal(); GUILayout.Label("End", GUILayout.Width(50)); _uiEditingProfile.GradientEndColor = GUILayout.TextField(_uiEditingProfile.GradientEndColor, 6); if (GUILayout.Button("Pick", GUILayout.Width(50))) { _showGradientEndPicker = !_showGradientEndPicker; _showSingleColorPicker = _showGradientStartPicker = false; if (_showGradientEndPicker && !ColorUtility.TryParseHtmlString("#" + _uiEditingProfile.GradientEndColor, out _uiEditingColor)) _uiEditingColor = Color.white; } GUILayout.EndHorizontal(); if (_showGradientEndPicker) DrawColorPicker(hex => _uiEditingProfile.GradientEndColor = hex); 
                GUI.enabled = true; 
            }
            if (_uiEditingProfile.Coloring == ColoringType.Wave || _uiEditingProfile.Coloring == ColoringType.Rainbow)
            {
                GUILayout.Label($"Color Speed: {_uiEditingProfile.ColorAnimationSpeed:F1}");
                _uiEditingProfile.ColorAnimationSpeed = GUILayout.HorizontalSlider(_uiEditingProfile.ColorAnimationSpeed, 0.1f, 5f);
            }
            if (_uiEditingProfile.Coloring == ColoringType.Wave || _uiEditingProfile.Coloring == ColoringType.StaticRainbow) { GUILayout.Label($"Wave Spread: {_uiEditingProfile.RainbowWaveSpread:F1}"); _uiEditingProfile.RainbowWaveSpread = GUILayout.HorizontalSlider(_uiEditingProfile.RainbowWaveSpread, 5f, 50f); }
            if (_uiEditingProfile.Coloring == ColoringType.Gradient || _uiEditingProfile.Coloring == ColoringType.SingleColor) GUILayout.Label("(RRGGBB format, no #)", disclaimerStyle);
            GUILayout.FlexibleSpace();
            GUILayout.BeginHorizontal(); if (GUILayout.Button("Copy")) { _copiedProfileBuffer = JsonConvert.DeserializeObject<CharacterTitleProfile>(JsonConvert.SerializeObject(_uiEditingProfile)); } GUI.enabled = _copiedProfileBuffer != null; if (GUILayout.Button("Paste")) { if (_copiedProfileBuffer != null) { _uiEditingProfile = JsonConvert.DeserializeObject<CharacterTitleProfile>(JsonConvert.SerializeObject(_copiedProfileBuffer)); } } GUI.enabled = true; GUILayout.EndHorizontal();
            if (GUILayout.Button("Set & Save Title")) { AllCharacterProfiles[CurrentCharacterSlot] = JsonConvert.DeserializeObject<CharacterTitleProfile>(JsonConvert.SerializeObject(_uiEditingProfile)); SaveProfiles(); var currentProfile = AllCharacterProfiles[CurrentCharacterSlot]; UpdateGradientCache(currentProfile); SendTitleUpdate(currentProfile); _menuState = MenuState.Closing; }
            if (GUILayout.Button("Clear Title")) { if (AllCharacterProfiles.ContainsKey(CurrentCharacterSlot)) AllCharacterProfiles.Remove(CurrentCharacterSlot); SaveProfiles(); SendTitleUpdate(null); LoadUIFromProfile(CurrentCharacterSlot); }
            GUI.DragWindow();
        }
    }
    
    // ============== HARMONY PATCHES ==============
    [HarmonyPatch]
    internal static class HarmonyPatches
    {
        private static bool _listenersInitialized = false;
        internal static readonly Dictionary<uint, CharacterTitleProfile> PlayerProfiles = new Dictionary<uint, CharacterTitleProfile>();
        internal static readonly Dictionary<uint, string> CurrentPlayerTitles = new Dictionary<uint, string>();
        private static readonly FieldInfo _globalNicknameTextMeshField = AccessTools.Field(typeof(Player), "_globalNicknameTextMesh");
        
        [HarmonyPostfix, HarmonyPatch(typeof(Player), "OnGameConditionChange")]
        private static void OnGameConditionChange_Postfix(Player __instance, GameCondition _newCondition)
        {
            if (__instance != Player._mainPlayer) return;
            Main.IsReady = (_newCondition == GameCondition.IN_GAME);
            if (Main.IsReady)
            {
                Main.CurrentCharacterSlot = ProfileDataManager._current.SelectedFileIndex;
                if (Main.AllCharacterProfiles.TryGetValue(Main.CurrentCharacterSlot, out var profile))
                {
                    Main.instance.UpdateGradientCache(profile);
                    Main.SendTitleUpdate(profile);
                }
            }
            else 
            { 
                if (Main.CurrentCharacterSlot != -1) Main.SendTitleUpdate(null); 
                Main.CurrentCharacterSlot = -1; 
                PlayerProfiles.Clear(); 
                CurrentPlayerTitles.Clear(); 
                Main.AllPlayerAnimators.Clear();
                _listenersInitialized = false;
                if (Main.OriginalSkin != null) GUI.skin = Main.OriginalSkin;
            }
        }

        [HarmonyPostfix, HarmonyPatch(typeof(AtlyssNetworkManager), "OnStopClient")]
        private static void OnStopClient_Postfix() 
        { 
            Main.IsReady = false; 
            Main.CurrentCharacterSlot = -1; 
            PlayerProfiles.Clear(); 
            CurrentPlayerTitles.Clear(); 
            Main.AllPlayerAnimators.Clear();
            _listenersInitialized = false;
            if (Main.OriginalSkin != null) GUI.skin = Main.OriginalSkin;
        }

        [HarmonyPostfix, HarmonyPatch(typeof(Player), "OnStartAuthority")]
        private static void OnPlayerStart_Postfix(Player __instance)
        {
            if (!__instance.isLocalPlayer || !Main._isCodeTalkerLoaded) return;
            
            if (!_listenersInitialized)
            {
                CodeTalkerNetwork.RegisterBinaryListener<Packets.UpdateTitleProfilePacket>(Main.OnProfileUpdate);
                if (__instance._isHostPlayer) CodeTalkerNetwork.RegisterListener<Packets.RequestAllTitlesPacket>(Main.OnSyncRequest);
                _listenersInitialized = true;
            }
            Main.RequestFullTitleSync();
        }
        
        [HarmonyPostfix, HarmonyPatch(typeof(Player), "Handle_ClientParameters")]
        private static void Handle_ClientParameters_Postfix(Player __instance)
        {
            if (_globalNicknameTextMeshField.GetValue(__instance) is not TextMeshPro textMesh) return;
            if (!textMesh.richText) textMesh.richText = true;
            
            CurrentPlayerTitles.TryGetValue(__instance.netId, out var customTitle);
            
            if (!PlayerProfiles.TryGetValue(__instance.netId, out var playerProfile))
            {
                playerProfile = new CharacterTitleProfile();
            }
            
            string originalGlobalName = __instance._globalNickname;
            string finalDisplayString;

            if (!string.IsNullOrEmpty(originalGlobalName) || !string.IsNullOrEmpty(customTitle))
            {
                string formattedTitle = "";
                if (!string.IsNullOrEmpty(customTitle))
                {
                    switch (playerProfile.BracketStyle) { case BracketType.Parentheses: formattedTitle = $"({customTitle})"; break; case BracketType.SquareBrackets: formattedTitle = $"[{customTitle}]"; break; case BracketType.Tilde: formattedTitle = $"~{customTitle}~"; break; case BracketType.Dash: formattedTitle = $"-{customTitle}-"; break; case BracketType.Plus: formattedTitle = $"+{customTitle}+"; break; case BracketType.Equals: formattedTitle = $"={customTitle}="; break; case BracketType.Asterisk: formattedTitle = $"*{customTitle}*"; break; case BracketType.Dollar: formattedTitle = $"${customTitle}$"; break; case BracketType.Hash: formattedTitle = $"#{customTitle}#"; break; case BracketType.Exclamation: formattedTitle = $"!{customTitle}!"; break; case BracketType.Pipe: formattedTitle = $"|{customTitle}|"; break; default: formattedTitle = customTitle; break; }
                }
                
                string globalNamePart = string.IsNullOrEmpty(originalGlobalName) ? "" : "@" + originalGlobalName;
                string prefix = playerProfile.AddGapAboveTitle && playerProfile.TitleOnNewLine ? "\n" : "";

                var parts = new List<string>();
                if (!string.IsNullOrEmpty(globalNamePart)) parts.Add(globalNamePart);
                if (!string.IsNullOrEmpty(formattedTitle)) parts.Add(formattedTitle);

                string separator = playerProfile.TitleOnNewLine ? "\n" : " ";
                finalDisplayString = $"{prefix}{string.Join(separator, parts)}";
            }
            else { finalDisplayString = ""; }
            
            if (textMesh.text != finalDisplayString) { textMesh.text = finalDisplayString; }
        }
    }
}