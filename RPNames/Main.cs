// ============== USING STATEMENTS ==============
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using System;
using System.Collections.Generic;
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
        public string Title { get; set; } = "";
        public bool TitleOnNewLine { get; set; } = true;
        public bool AddGapAboveTitle { get; set; } = true;
        public BracketType BracketStyle { get; set; } = BracketType.Parentheses;
        public TextAnimationType TextAnimation { get; set; } = TextAnimationType.Static;
        public ColoringType Coloring { get; set; } = ColoringType.None;
        public float AnimationSpeed { get; set; } = 0.15f;
        public int MarqueeWidth { get; set; } = 16;
        public string SingleHexColor { get; set; } = "FFFFFF";
        public string GradientStartColor { get; set; } = "FF0000";
        public string GradientEndColor { get; set; } = "0000FF";
        public bool AnimateGradient { get; set; } = false;
        public float GradientSpread { get; set; } = 10f;
        public float RainbowWaveSpread { get; set; } = 15f;
    }

    public enum TextAnimationType { Static, Scroll, Marquee, Typewriter }
    public enum ColoringType { None, SingleColor, Rainbow, Gradient, Wave, StaticRainbow }
    public enum BracketType { None, Parentheses, SquareBrackets, Tilde, Dash, Plus, Equals, Asterisk, Dollar, Hash, Exclamation, Pipe }

    namespace Packets
    {
        public class SetRoleplayTitlePacket : PacketBase
        {
            public override string PacketSourceGUID => ModInfo.GUID;
            [JsonProperty] public uint TargetNetID { get; set; } 
            [JsonProperty] public string DesiredTitle { get; set; }
            public SetRoleplayTitlePacket() { }
            public SetRoleplayTitlePacket(uint targetId, string name) { TargetNetID = targetId; DesiredTitle = name; }
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
        
        private bool _isAnimating = false;
        private float _animationTimer = 0f;
        private int _animationIndex = 0;
        private bool _isAnimatingForward = true;
        private float _rainbowHue = 0f;
        
        private Color _gradientStartColorCache;
        private Color _gradientEndColorCache;

        private static readonly List<string> _presetTitles = new List<string>();
        
        private enum TypewriterState { Typing, Blinking, Backspacing }
        private TypewriterState _typewriterState = TypewriterState.Typing;
        private float _typewriterPauseTimer = 0f;
        private bool _typewriterCursorVisible = true;
        private float _typewriterBlinkTimer = 0f;
        
        private bool _showSingleColorPicker = false;
        private bool _showGradientStartPicker = false;
        private bool _showGradientEndPicker = false;
        private Color _uiEditingColor;
        
        private enum MenuState { Closed, Opening, Open, Closing }
        private MenuState _menuState = MenuState.Closed;
        private float _animationProgress = 0f;
        private const float AnimationDuration = 0.25f;
        private bool _stylesInitialized = false;
        private GUIStyle _windowStyle, _labelStyle, _buttonStyle, _toggleStyle, _textFieldStyle, _boxStyle, _sliderStyle, _sliderThumbStyle;

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
            Texture2D MakeTex(Color col)
            {
                var tex = new Texture2D(1, 1);
                tex.SetPixel(0, 0, col);
                tex.Apply();
                return tex;
            }

            _windowStyle = new GUIStyle(GUI.skin.window)
            {
                normal = { background = MakeTex(new Color(0.1f, 0.12f, 0.15f, 0.97f)), textColor = new Color(0.9f, 0.9f, 0.9f) },
                onNormal = { background = MakeTex(new Color(0.1f, 0.12f, 0.15f, 0.97f)), textColor = new Color(0.9f, 0.9f, 0.9f) },
                padding = new RectOffset(15, 15, 30, 15),
                border = new RectOffset(2, 2, 2, 2),
                alignment = TextAnchor.UpperCenter,
                fontSize = 16,
                fontStyle = FontStyle.Bold
            };

            _boxStyle = new GUIStyle(GUI.skin.box)
            {
                normal = { background = MakeTex(new Color(0.2f, 0.22f, 0.25f, 0.5f)) }
            };

            _labelStyle = new GUIStyle(GUI.skin.label)
            {
                normal = { textColor = new Color(0.85f, 0.85f, 0.85f) }
            };

            _buttonStyle = new GUIStyle(GUI.skin.button)
            {
                normal = { background = MakeTex(new Color(0.3f, 0.35f, 0.4f)), textColor = Color.white },
                hover = { background = MakeTex(new Color(0.4f, 0.45f, 0.5f)), textColor = Color.white },
                active = { background = MakeTex(new Color(0.2f, 0.25f, 0.3f)), textColor = Color.white },
                onNormal = { background = MakeTex(new Color(0.5f, 0.55f, 0.6f)), textColor = Color.white },
                onHover = { background = MakeTex(new Color(0.55f, 0.6f, 0.65f)), textColor = Color.white },
                border = new RectOffset(4, 4, 4, 4),
                padding = new RectOffset(8, 8, 8, 8)
            };

            _toggleStyle = new GUIStyle(GUI.skin.toggle)
            {
                padding = new RectOffset(25, 0, 3, 3)
            };
            
            Texture2D MakeToggleTex(bool on)
            {
                var tex = new Texture2D(16, 16);
                var bgColor = new Color(0.2f, 0.22f, 0.25f);
                var borderColor = new Color(0.4f, 0.45f, 0.5f);
                var checkColor = new Color(0.8f, 0.85f, 0.9f);
                for (int y = 0; y < 16; y++)
                {
                    for (int x = 0; x < 16; x++)
                    {
                        if (x == 0 || x == 15 || y == 0 || y == 15) tex.SetPixel(x, y, borderColor);
                        else tex.SetPixel(x, y, bgColor);
                    }
                }
                if (on)
                {
                    for (int y = 4; y < 12; y++)
                    for (int x = 4; x < 12; x++)
                        tex.SetPixel(x, y, checkColor);
                }
                tex.Apply();
                return tex;
            }

            _toggleStyle.normal.background = MakeToggleTex(false);
            _toggleStyle.onNormal.background = MakeToggleTex(true);
            _toggleStyle.hover.background = MakeToggleTex(false);
            _toggleStyle.onHover.background = MakeToggleTex(true);
            
            _textFieldStyle = new GUIStyle(GUI.skin.textField)
            {
                normal = { background = MakeTex(new Color(0.05f, 0.05f, 0.05f)), textColor = Color.white },
                padding = new RectOffset(5, 5, 5, 5)
            };

            _sliderStyle = new GUIStyle(GUI.skin.horizontalSlider)
            {
                normal = { background = MakeTex(new Color(0.15f, 0.17f, 0.2f)), },
                fixedHeight = 10,
                border = new RectOffset(2, 2, 2, 2)
            };

            _sliderThumbStyle = new GUIStyle(GUI.skin.horizontalSliderThumb)
            {
                normal = { background = MakeTex(new Color(0.6f, 0.65f, 0.7f)) },
                hover = { background = MakeTex(new Color(0.7f, 0.75f, 0.8f)) },
                active = { background = MakeTex(new Color(0.5f, 0.55f, 0.6f)) },
                fixedHeight = 18,
                fixedWidth = 10
            };
            
            _stylesInitialized = true;
        }
        
        private void LoadProfiles()
        {
            try
            {
                var loadedProfiles = JsonConvert.DeserializeObject<Dictionary<int, CharacterTitleProfile>>(_characterProfilesJson.Value);
                if (loadedProfiles != null) AllCharacterProfiles = loadedProfiles;
            } catch { AllCharacterProfiles = new Dictionary<int, CharacterTitleProfile>(); }
        }
        
        private void SaveProfiles()
        {
            _characterProfilesJson.Value = JsonConvert.SerializeObject(AllCharacterProfiles, Formatting.Indented);
            Config.Save();
        }

        private void PopulatePresetTitles()
        {
            _presetTitles.Clear();
            _presetTitles.Add("The Explorer"); _presetTitles.Add("The Patient"); _presetTitles.Add("Dragonslayer");
            _presetTitles.Add("Scarab Lord"); _presetTitles.Add("The Undying"); _presetTitles.Add("The Insane");
            _presetTitles.Add("Grand Marshal"); _presetTitles.Add("High Warlord"); _presetTitles.Add("Arena Master");
            _presetTitles.Add("Salty"); _presetTitles.Add("Chef"); _presetTitles.Add("Guardian of Cenarius");
            _presetTitles.Add("Hand of A'dal"); _presetTitles.Add("Master Angler"); _presetTitles.Add("Webfisher");
            _presetTitles.Add("Net Master"); _presetTitles.Add("Phisherman"); _presetTitles.Add("The Lurker");
        }
        
        public static void OnTitleUpdateRequest(PacketHeader header, PacketBase packet)
        {
            if (packet is Packets.SetRoleplayTitlePacket titlePacket) { CodeTalkerNetwork.SendNetworkPacket(titlePacket); }
        }
        
        public static void OnTitleBroadcast(PacketHeader header, PacketBase packet)
        {
            if (packet is Packets.SetRoleplayTitlePacket titlePacket)
            {
                if (HarmonyPatches.PlayerTitles.ContainsKey(titlePacket.TargetNetID)) { HarmonyPatches.PlayerTitles[titlePacket.TargetNetID] = titlePacket.DesiredTitle; }
                else { HarmonyPatches.PlayerTitles.Add(titlePacket.TargetNetID, titlePacket.DesiredTitle); }
            }
        }
        
        public static void OnSyncRequest(PacketHeader header, PacketBase packet)
        {
            if (packet is Packets.RequestAllTitlesPacket)
            {
                foreach (var entry in HarmonyPatches.PlayerTitles) { CodeTalkerNetwork.SendNetworkPacket(new Packets.SetRoleplayTitlePacket(entry.Key, entry.Value)); }
            }
        }

        public static void RequestTitleChange(string name)
        {
            if (!_isCodeTalkerLoaded || Player._mainPlayer == null) return;
            uint myId = Player._mainPlayer.netId;
            CodeTalkerNetwork.SendNetworkPacket(new Packets.SetRoleplayTitlePacket(myId, name));
        }
        
        public static void RequestFullTitleSync()
        {
            if (!_isCodeTalkerLoaded) return;
            CodeTalkerNetwork.SendNetworkPacket(new Packets.RequestAllTitlesPacket());
        }

        private void Update()
        {
            switch (_menuState)
            {
                case MenuState.Opening:
                    _animationProgress = Mathf.Clamp01(_animationProgress + Time.unscaledDeltaTime / AnimationDuration);
                    if (_animationProgress >= 1f) _menuState = MenuState.Open;
                    break;
                case MenuState.Closing:
                    _animationProgress = Mathf.Clamp01(_animationProgress - Time.unscaledDeltaTime / AnimationDuration);
                    if (_animationProgress <= 0f) _menuState = MenuState.Closed;
                    break;
            }

            if (Input.GetKeyDown(_menuKey.Value))
            {
                if (_menuState == MenuState.Open || _menuState == MenuState.Opening)
                {
                    _menuState = MenuState.Closing;
                    _showTextAnimPicker = _showColoringPicker = _showBracketPicker = _showPresetPicker = _showSingleColorPicker = _showGradientStartPicker = _showGradientEndPicker = false;
                }
                else
                {
                    _menuState = MenuState.Opening;
                    LoadUIFromProfile(CurrentCharacterSlot >= 0 ? CurrentCharacterSlot : 0);
                }
            }

            if (!_isAnimating || !IsReady || CurrentCharacterSlot == -1 || !AllCharacterProfiles.TryGetValue(CurrentCharacterSlot, out var currentProfile)) return;
            
            if (currentProfile.TextAnimation == TextAnimationType.Typewriter)
            {
                if (_typewriterState == TypewriterState.Blinking)
                {
                    _typewriterPauseTimer += Time.deltaTime;
                    _typewriterBlinkTimer += Time.deltaTime;
                    if (_typewriterBlinkTimer >= 0.5f)
                    {
                        _typewriterBlinkTimer = 0f;
                        _typewriterCursorVisible = !_typewriterCursorVisible;
                        string animatedText = GetAnimatedText(currentProfile);
                        string finalTitle = ApplyColoring(animatedText, currentProfile);
                        if (finalTitle != null) RequestTitleChange(finalTitle);
                    }
                    if (_typewriterPauseTimer >= 5f)
                    {
                        _typewriterPauseTimer = 0f;
                        _typewriterState = TypewriterState.Backspacing;
                    }
                    return;
                }
            }

            _animationTimer += Time.deltaTime;
            if (_animationTimer >= currentProfile.AnimationSpeed)
            {
                _animationTimer = 0f;

                if (currentProfile.TextAnimation == TextAnimationType.Typewriter)
                {
                    switch (_typewriterState)
                    {
                        case TypewriterState.Typing:
                            if (_animationIndex < currentProfile.Title.Length) { _animationIndex++; }
                            else { _typewriterState = TypewriterState.Blinking; _typewriterPauseTimer = 0f; _typewriterBlinkTimer = 0f; _typewriterCursorVisible = true; }
                            break;
                        case TypewriterState.Backspacing:
                            if (_animationIndex > 0) { _animationIndex--; }
                            else { _typewriterState = TypewriterState.Typing; }
                            break;
                    }
                }

                string animatedText = GetAnimatedText(currentProfile);
                string finalTitle = ApplyColoring(animatedText, currentProfile);
                if (finalTitle != null) RequestTitleChange(finalTitle);
            }
        }
        
        private string GetAnimatedText(CharacterTitleProfile profile)
        {
            if (string.IsNullOrEmpty(profile.Title)) return "";
            switch (profile.TextAnimation)
            {
                case TextAnimationType.Scroll:
                    if (_isAnimatingForward) { _animationIndex++; if (_animationIndex >= profile.Title.Length) { _animationIndex = profile.Title.Length; _isAnimatingForward = false; } }
                    else { _animationIndex--; if (_animationIndex <= 1) { _animationIndex = 1; _isAnimatingForward = true; } }
                    return profile.Title.Substring(0, _animationIndex);
                case TextAnimationType.Marquee:
                    string padding = new string(' ', profile.MarqueeWidth);
                    string fullMarqueeText = padding + profile.Title + padding;
                    _animationIndex = (_animationIndex + 1) % (fullMarqueeText.Length - profile.MarqueeWidth);
                    return fullMarqueeText.Substring(_animationIndex, profile.MarqueeWidth);
                case TextAnimationType.Typewriter:
                    string visibleText = profile.Title.Substring(0, _animationIndex);
                    if (_typewriterState == TypewriterState.Blinking) { return profile.Title + (_typewriterCursorVisible ? "|" : ""); }
                    return visibleText + "|";
                default: return profile.Title;
            }
        }
        
        internal string ApplyColoring(string text, CharacterTitleProfile profile)
        {
            if (string.IsNullOrEmpty(text)) return "";
            _rainbowHue = (_rainbowHue + 0.02f) % 1.0f;
            switch (profile.Coloring)
            {
                case ColoringType.SingleColor: return $"<color=#{profile.SingleHexColor.Replace("#", "")}>{text}</color>";
                case ColoringType.Rainbow: return $"<color=#{ColorUtility.ToHtmlStringRGB(Color.HSVToRGB(_rainbowHue, 1f, 1f))}>{text}</color>";
                case ColoringType.Gradient:
                    StringBuilder gradientBuilder = new StringBuilder();
                    if (profile.AnimateGradient)
                    {
                        for (int i = 0; i < text.Length; i++)
                        {
                            float phase = (_rainbowHue + (i / profile.GradientSpread));
                            float t = 1f - Mathf.Abs((phase * 2f) % 2f - 1f);
                            Color charColor = Color.Lerp(_gradientStartColorCache, _gradientEndColorCache, t);
                            gradientBuilder.Append($"<color=#{ColorUtility.ToHtmlStringRGB(charColor)}>{text[i]}</color>");
                        }
                    }
                    else
                    {
                        for (int i = 0; i < text.Length; i++)
                        {
                            float t = (text.Length <= 1) ? 0f : (float)i / (text.Length - 1);
                            Color charColor = Color.Lerp(_gradientStartColorCache, _gradientEndColorCache, t);
                            gradientBuilder.Append($"<color=#{ColorUtility.ToHtmlStringRGB(charColor)}>{text[i]}</color>");
                        }
                    }
                    return gradientBuilder.ToString();
                case ColoringType.Wave:
                    StringBuilder waveBuilder = new StringBuilder();
                    for (int i = 0; i < text.Length; i++)
                    {
                        float hue = (_rainbowHue + (i / profile.RainbowWaveSpread)) % 1.0f;
                        Color charColor = Color.HSVToRGB(hue, 1f, 1f);
                        waveBuilder.Append($"<color=#{ColorUtility.ToHtmlStringRGB(charColor)}>{text[i]}</color>");
                    }
                    return waveBuilder.ToString();
                case ColoringType.StaticRainbow:
                    StringBuilder staticBuilder = new StringBuilder();
                    for (int i = 0; i < text.Length; i++)
                    {
                        float hue = (i / profile.RainbowWaveSpread) % 1.0f;
                        Color charColor = Color.HSVToRGB(hue, 1f, 1f);
                        staticBuilder.Append($"<color=#{ColorUtility.ToHtmlStringRGB(charColor)}>{text[i]}</color>");
                    }
                    return staticBuilder.ToString();
                default: return text;
            }
        }
        
        internal void UpdateGradientCache(CharacterTitleProfile profile)
        {
            ColorUtility.TryParseHtmlString("#" + profile.GradientStartColor, out _gradientStartColorCache);
            ColorUtility.TryParseHtmlString("#" + profile.GradientEndColor, out _gradientEndColorCache);
        }

        internal bool ShouldAnimate(CharacterTitleProfile profile) => !string.IsNullOrEmpty(profile.Title) && (profile.TextAnimation != TextAnimationType.Static || profile.Coloring != ColoringType.None);
        
        internal void StartAnimation()
        {
            _isAnimating = true;
            _animationIndex = 0;
            _isAnimatingForward = true;
            _rainbowHue = 0f;
            _typewriterState = TypewriterState.Typing;
            _typewriterPauseTimer = 0f;
            _typewriterCursorVisible = true;
            _typewriterBlinkTimer = 0f;
        }

        internal void StopAnimation(CharacterTitleProfile profile) { _isAnimating = false; RequestTitleChange(ApplyColoring(profile.Title, profile)); }
        internal void ClearAnimation() { _isAnimating = false; RequestTitleChange(""); }

        private void OnGUI()
        {
            if (_menuState == MenuState.Closed) return;

            if (!_stylesInitialized) InitializeGUIStyles();

            GUI.skin.window = _windowStyle;
            GUI.skin.box = _boxStyle;
            GUI.skin.label = _labelStyle;
            GUI.skin.button = _buttonStyle;
            GUI.skin.toggle = _toggleStyle;
            GUI.skin.textField = _textFieldStyle;
            GUI.skin.horizontalSlider = _sliderStyle;
            GUI.skin.horizontalSliderThumb = _sliderThumbStyle;
            
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            float easeOut(float t) => 1 - Mathf.Pow(1 - t, 3);
            float currentProgress = easeOut(_animationProgress);

            float startX = -_windowRect.width;
            float endX = 20f;
            
            Rect animatedRect = _windowRect;
            animatedRect.x = Mathf.Lerp(startX, endX, currentProgress);

            _windowRect = GUILayout.Window(1863, animatedRect, DrawSettingsWindow, "RPNames Title Settings");
        }
        
        private void LoadUIFromProfile(int slot)
        {
            if (slot == -1) { _uiEditingProfile = new CharacterTitleProfile(); return; }
            if (!AllCharacterProfiles.TryGetValue(slot, out var profile)) { profile = new CharacterTitleProfile(); }
            _uiEditingProfile = JsonConvert.DeserializeObject<CharacterTitleProfile>(JsonConvert.SerializeObject(profile));
        }

        private void DrawSettingsWindow(int windowID)
        {
            if (CurrentCharacterSlot == -1)
            {
                GUILayout.FlexibleSpace();
                GUILayout.Label("Please load a character to edit their title profile.");
                GUILayout.FlexibleSpace();
                GUI.DragWindow();
                return;
            }
            
            GUILayout.Label($"Editing Profile for Slot: {CurrentCharacterSlot + 1}");
            
            _uiEditingProfile.Title = GUILayout.TextField(_uiEditingProfile.Title, 50);

            if (GUILayout.Button("Select a Preset")) { _showPresetPicker = !_showPresetPicker; }
            if (_showPresetPicker)
            {
                _presetScrollPosition = GUILayout.BeginScrollView(_presetScrollPosition, GUILayout.Height(100));
                foreach (string preset in _presetTitles)
                {
                    if (GUILayout.Button(preset)) { _uiEditingProfile.Title = preset; _showPresetPicker = false; }
                }
                GUILayout.EndScrollView();
            }
            
            _uiEditingProfile.TitleOnNewLine = GUILayout.Toggle(_uiEditingProfile.TitleOnNewLine, " Title on New Line");
            if (_uiEditingProfile.TitleOnNewLine)
            {
                _uiEditingProfile.AddGapAboveTitle = GUILayout.Toggle(_uiEditingProfile.AddGapAboveTitle, " Add Gap Above Title");
            }
            
            GUILayout.BeginHorizontal();
            GUILayout.Label("Bracket Style:", GUILayout.Width(120));
            if (GUILayout.Button(_uiEditingProfile.BracketStyle.ToString())) { _showBracketPicker = !_showBracketPicker; }
            GUILayout.EndHorizontal();

            if (_showBracketPicker)
            {
                string[] names = Enum.GetNames(typeof(BracketType));
                int sel = GUILayout.SelectionGrid((int)_uiEditingProfile.BracketStyle, names, 4);
                if (sel != (int)_uiEditingProfile.BracketStyle) { _uiEditingProfile.BracketStyle = (BracketType)sel; _showBracketPicker = false; }
            }

            GUILayout.Space(10);
            GUILayout.Label("--- Text Animation ---");
            GUILayout.BeginHorizontal();
            GUILayout.Label("Animation:", GUILayout.Width(120));
            if (GUILayout.Button(_uiEditingProfile.TextAnimation.ToString())) { _showTextAnimPicker = !_showTextAnimPicker; }
            GUILayout.EndHorizontal();

            if (_showTextAnimPicker)
            {
                string[] names = Enum.GetNames(typeof(TextAnimationType));
                int sel = GUILayout.SelectionGrid((int)_uiEditingProfile.TextAnimation, names, 3);
                if (sel != (int)_uiEditingProfile.TextAnimation) { _uiEditingProfile.TextAnimation = (TextAnimationType)sel; _showTextAnimPicker = false; }
            }
            if (_uiEditingProfile.TextAnimation == TextAnimationType.Marquee)
            {
                GUILayout.Label($"Marquee Width: {_uiEditingProfile.MarqueeWidth}");
                _uiEditingProfile.MarqueeWidth = (int)GUILayout.HorizontalSlider(_uiEditingProfile.MarqueeWidth, 5, 40);
            }
            
            GUILayout.Space(10);
            GUILayout.Label("--- Coloring ---");
            GUILayout.BeginHorizontal();
            GUILayout.Label("Effect:", GUILayout.Width(120));
            if (GUILayout.Button(_uiEditingProfile.Coloring.ToString())) { _showColoringPicker = !_showColoringPicker; }
            GUILayout.EndHorizontal();

            if (_showColoringPicker)
            {
                string[] names = Enum.GetNames(typeof(ColoringType));
                int sel = GUILayout.SelectionGrid((int)_uiEditingProfile.Coloring, names, 3);
                if (sel != (int)_uiEditingProfile.Coloring)
                {
                    _uiEditingProfile.Coloring = (ColoringType)sel;
                    _showColoringPicker = false;
                    _showSingleColorPicker = _showGradientStartPicker = _showGradientEndPicker = false;
                }
            }

            GUIStyle disclaimerStyle = new GUIStyle(_labelStyle) { fontStyle = FontStyle.Italic, normal = { textColor = Color.gray } };
            
            Action<Action<string>> DrawColorPicker = (setter) => {
                GUILayout.BeginVertical(_boxStyle);
                GUILayout.BeginHorizontal();
                GUILayout.BeginVertical(GUILayout.ExpandWidth(true));
                _uiEditingColor.r = GUILayout.HorizontalSlider(_uiEditingColor.r, 0, 1);
                _uiEditingColor.g = GUILayout.HorizontalSlider(_uiEditingColor.g, 0, 1);
                _uiEditingColor.b = GUILayout.HorizontalSlider(_uiEditingColor.b, 0, 1);
                GUILayout.EndVertical();
                var oldBgColor = GUI.backgroundColor;
                GUI.backgroundColor = _uiEditingColor;
                GUILayout.Box("", new GUIStyle { normal = { background = Texture2D.whiteTexture } }, GUILayout.Width(40), GUILayout.Height(40));
                GUI.backgroundColor = oldBgColor;
                GUILayout.EndHorizontal();
                setter(ColorUtility.ToHtmlStringRGB(_uiEditingColor));
                GUILayout.EndVertical();
            };

            if (_uiEditingProfile.Coloring == ColoringType.SingleColor)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("Hex Color", GUILayout.Width(80));
                _uiEditingProfile.SingleHexColor = GUILayout.TextField(_uiEditingProfile.SingleHexColor, 6);
                if (GUILayout.Button("Pick", GUILayout.Width(50)))
                {
                    _showSingleColorPicker = !_showSingleColorPicker;
                    _showGradientStartPicker = _showGradientEndPicker = false;
                    if (_showSingleColorPicker && !ColorUtility.TryParseHtmlString("#" + _uiEditingProfile.SingleHexColor, out _uiEditingColor)) _uiEditingColor = Color.white;
                }
                GUILayout.EndHorizontal();
                if (_showSingleColorPicker) DrawColorPicker(hex => _uiEditingProfile.SingleHexColor = hex);
            }
            if (_uiEditingProfile.Coloring == ColoringType.Gradient)
            {
                _uiEditingProfile.AnimateGradient = GUILayout.Toggle(_uiEditingProfile.AnimateGradient, "Animate Gradient");
                GUILayout.Label($"Gradient Spread: {_uiEditingProfile.GradientSpread:F1}");
                _uiEditingProfile.GradientSpread = GUILayout.HorizontalSlider(_uiEditingProfile.GradientSpread, 1f, 50f);
                GUI.enabled = !_uiEditingProfile.AnimateGradient;

                GUILayout.BeginHorizontal();
                GUILayout.Label("Start", GUILayout.Width(50));
                _uiEditingProfile.GradientStartColor = GUILayout.TextField(_uiEditingProfile.GradientStartColor, 6);
                if (GUILayout.Button("Pick", GUILayout.Width(50)))
                {
                    _showGradientStartPicker = !_showGradientStartPicker;
                    _showSingleColorPicker = _showGradientEndPicker = false;
                    if (_showGradientStartPicker && !ColorUtility.TryParseHtmlString("#" + _uiEditingProfile.GradientStartColor, out _uiEditingColor)) _uiEditingColor = Color.white;
                }
                GUILayout.EndHorizontal();
                if (_showGradientStartPicker) DrawColorPicker(hex => _uiEditingProfile.GradientStartColor = hex);

                GUILayout.BeginHorizontal();
                GUILayout.Label("End", GUILayout.Width(50));
                _uiEditingProfile.GradientEndColor = GUILayout.TextField(_uiEditingProfile.GradientEndColor, 6);
                if (GUILayout.Button("Pick", GUILayout.Width(50)))
                {
                    _showGradientEndPicker = !_showGradientEndPicker;
                    _showSingleColorPicker = _showGradientStartPicker = false;
                    if (_showGradientEndPicker && !ColorUtility.TryParseHtmlString("#" + _uiEditingProfile.GradientEndColor, out _uiEditingColor)) _uiEditingColor = Color.white;
                }
                GUILayout.EndHorizontal();
                if (_showGradientEndPicker) DrawColorPicker(hex => _uiEditingProfile.GradientEndColor = hex);
                
                GUI.enabled = true;
            }
            if (_uiEditingProfile.Coloring == ColoringType.Wave || _uiEditingProfile.Coloring == ColoringType.StaticRainbow)
            {
                GUILayout.Label($"Wave Spread: {_uiEditingProfile.RainbowWaveSpread:F1}");
                _uiEditingProfile.RainbowWaveSpread = GUILayout.HorizontalSlider(_uiEditingProfile.RainbowWaveSpread, 5f, 50f);
            }
            if (_uiEditingProfile.Coloring == ColoringType.Gradient || _uiEditingProfile.Coloring == ColoringType.SingleColor)
            {
                GUILayout.Label("(RRGGBB format, no #)", disclaimerStyle);
            }

            GUILayout.FlexibleSpace();
            
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Copy"))
            {
                _copiedProfileBuffer = JsonConvert.DeserializeObject<CharacterTitleProfile>(JsonConvert.SerializeObject(_uiEditingProfile));
                Log.LogInfo("Copied current UI settings to buffer.");
            }
            GUI.enabled = _copiedProfileBuffer != null;
            if (GUILayout.Button("Paste"))
            {
                if (_copiedProfileBuffer != null)
                {
                    _uiEditingProfile = JsonConvert.DeserializeObject<CharacterTitleProfile>(JsonConvert.SerializeObject(_copiedProfileBuffer));
                    Log.LogInfo("Pasted buffered settings into current UI.");
                }
            }
            GUI.enabled = true;
            GUILayout.EndHorizontal();
            
            if (GUILayout.Button("Set & Save Title"))
            {
                AllCharacterProfiles[CurrentCharacterSlot] = JsonConvert.DeserializeObject<CharacterTitleProfile>(JsonConvert.SerializeObject(_uiEditingProfile));
                SaveProfiles();
                
                var currentProfile = AllCharacterProfiles[CurrentCharacterSlot];
                UpdateGradientCache(currentProfile);
                if (ShouldAnimate(currentProfile)) { StartAnimation(); } 
                else { StopAnimation(currentProfile); }
                _menuState = MenuState.Closing;
            }
            if (GUILayout.Button("Clear Title"))
            {
                if (AllCharacterProfiles.ContainsKey(CurrentCharacterSlot))
                {
                    AllCharacterProfiles.Remove(CurrentCharacterSlot);
                }
                SaveProfiles();
                ClearAnimation();
                LoadUIFromProfile(CurrentCharacterSlot);
            }
            GUI.DragWindow();
        }
    }
    
    // ============== HARMONY PATCHES ==============
    [HarmonyPatch]
    internal static class HarmonyPatches
    {
        private static bool _listenersInitialized = false;
        internal static readonly Dictionary<uint, string> PlayerTitles = new Dictionary<uint, string>();
        private static readonly FieldInfo _globalNicknameTextMeshField = AccessTools.Field(typeof(Player), "_globalNicknameTextMesh");
        
        [HarmonyPostfix, HarmonyPatch(typeof(Player), "OnGameConditionChange")]
        private static void OnGameConditionChange_Postfix(Player __instance, GameCondition _newCondition)
        {
            if (__instance != Player._mainPlayer) return;
            Main.IsReady = (_newCondition == GameCondition.IN_GAME);
            if (Main.IsReady)
            {
                Main.CurrentCharacterSlot = ProfileDataManager._current.SelectedFileIndex;
                Main.Log.LogInfo($"Player loaded into character slot {Main.CurrentCharacterSlot + 1}. Applying title profile.");

                if (Main.AllCharacterProfiles.TryGetValue(Main.CurrentCharacterSlot, out var profile))
                {
                    Main.instance.UpdateGradientCache(profile);
                    if (Main.instance.ShouldAnimate(profile))
                    {
                        Main.instance.StartAnimation();
                    }
                    else
                    {
                        Main.RequestTitleChange(Main.instance.ApplyColoring(profile.Title, profile));
                    }
                }
                
                if (Main._isCodeTalkerLoaded && !__instance._isHostPlayer) { Main.RequestFullTitleSync(); }
            }
            else
            {
                if (Main.CurrentCharacterSlot != -1)
                {
                    Main.instance.ClearAnimation();
                }
                Main.CurrentCharacterSlot = -1;
            }
        }

        [HarmonyPostfix, HarmonyPatch(typeof(AtlyssNetworkManager), "OnStopClient")]
        private static void OnStopClient_Postfix()
        {
            Main.Log.LogInfo("Client stopped. Resetting character slot and pausing mod.");
            Main.IsReady = false;
            Main.CurrentCharacterSlot = -1;
            Main.instance.ClearAnimation();
        }

        [HarmonyPostfix, HarmonyPatch(typeof(Player), "OnStartAuthority")]
        private static void OnPlayerStart_Postfix(Player __instance)
        {
            if (!__instance.isLocalPlayer) return;
            if (Main._isCodeTalkerLoaded && !_listenersInitialized)
            {
                CodeTalkerNetwork.RegisterListener<Packets.SetRoleplayTitlePacket>(Main.OnTitleBroadcast);
                if (__instance._isHostPlayer)
                {
                    CodeTalkerNetwork.RegisterListener<Packets.SetRoleplayTitlePacket>(Main.OnTitleUpdateRequest);
                    CodeTalkerNetwork.RegisterListener<Packets.RequestAllTitlesPacket>(Main.OnSyncRequest);
                }
                _listenersInitialized = true;
            }
        }
        
        [HarmonyPostfix, HarmonyPatch(typeof(Player), "Handle_ClientParameters")]
        private static void Handle_ClientParameters_Postfix(Player __instance)
        {
            if (_globalNicknameTextMeshField.GetValue(__instance) is not TextMeshPro textMesh) return;
            
            if (!Main.IsReady && __instance.isLocalPlayer) { return; }
            
            CharacterTitleProfile localPlayerProfile;
            if (__instance.isLocalPlayer && Main.CurrentCharacterSlot != -1 && Main.AllCharacterProfiles.TryGetValue(Main.CurrentCharacterSlot, out var profile))
            {
                localPlayerProfile = profile;
            }
            else { localPlayerProfile = new CharacterTitleProfile(); }

            if (!textMesh.richText) textMesh.richText = true;

            string originalGlobalName = __instance._globalNickname;
            uint playerNetId = __instance.netId;
            PlayerTitles.TryGetValue(playerNetId, out string customTitle);
            
            string finalDisplayString;

            if (!string.IsNullOrEmpty(originalGlobalName) || !string.IsNullOrEmpty(customTitle))
            {
                string formattedTitle = "";
                if (!string.IsNullOrEmpty(customTitle))
                {
                    switch (localPlayerProfile.BracketStyle)
                    {
                        case BracketType.Parentheses: formattedTitle = $"({customTitle})"; break;
                        case BracketType.SquareBrackets: formattedTitle = $"[{customTitle}]"; break;
                        case BracketType.Tilde: formattedTitle = $"~{customTitle}~"; break;
                        case BracketType.Dash: formattedTitle = $"-{customTitle}-"; break;
                        case BracketType.Plus: formattedTitle = $"+{customTitle}+"; break;
                        case BracketType.Equals: formattedTitle = $"={customTitle}="; break;
                        case BracketType.Asterisk: formattedTitle = $"*{customTitle}*"; break;
                        case BracketType.Dollar: formattedTitle = $"${customTitle}$"; break;
                        case BracketType.Hash: formattedTitle = $"#{customTitle}#"; break;
                        case BracketType.Exclamation: formattedTitle = $"!{customTitle}!"; break;
                        case BracketType.Pipe: formattedTitle = $"|{customTitle}|"; break;
                        default: formattedTitle = customTitle; break;
                    }
                }
                
                string globalNamePart = string.IsNullOrEmpty(originalGlobalName) ? "" : "@" + originalGlobalName;
                string prefix = localPlayerProfile.AddGapAboveTitle ? "\n" : "";

                if (localPlayerProfile.TitleOnNewLine)
                {
                    finalDisplayString = $"{prefix}{globalNamePart}\n{formattedTitle}"; 
                }
                else
                {
                    finalDisplayString = $"{prefix}{globalNamePart} {formattedTitle}";
                }
            }
            else { finalDisplayString = ""; }

            if (string.IsNullOrEmpty(originalGlobalName) && !string.IsNullOrEmpty(finalDisplayString))
            {
                finalDisplayString = finalDisplayString.TrimStart('\n');
            }

            if (textMesh.text != finalDisplayString) { textMesh.text = finalDisplayString; }
        }
    }
}