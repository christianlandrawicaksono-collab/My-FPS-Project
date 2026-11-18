using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using FPSFramework.Player;
using FPSFramework.Combat;

namespace FPSFramework.UI
{
    public enum MenuState
    {
        MainMenu,
        InGame,
        Paused,
        Settings,
        Controls
    }

    /// <summary>
    /// Pengelola Antarmuka Terpadu (Unified Tactical Menu System):
    /// - Main Menu Title Screen (Start screen pembuka, Play, Settings, Controls, Quit).
    /// - In-Game Pause Menu (Tombol ESC saat match berjalan).
    /// - Panel Pengaturan Interaktif (Sensitivitas Mouse, FOV, Master/SFX Volume, Warna Crosshair).
    /// - Penyimpanan Pengaturan Permanen via PlayerPrefs.
    /// - Kartu Panduan Tombol (How to Play / Field Manual).
    /// </summary>
    public class FPSMenuManager : MonoBehaviour
    {
        private static FPSMenuManager _instance;
        public static FPSMenuManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindAnyObjectByType<FPSMenuManager>();
                    if (_instance == null)
                    {
                        GameObject go = new GameObject("FPSMenuManager");
                        _instance = go.AddComponent<FPSMenuManager>();
                    }
                }
                return _instance;
            }
            private set => _instance = value;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoEnsureMenuExists()
        {
            var _ = Instance;
        }

        [Header("State Menu")]
        [SerializeField] private MenuState initialState = MenuState.MainMenu;

        public MenuState CurrentState { get; private set; } = MenuState.MainMenu;
        public bool IsMainMenuActive => CurrentState == MenuState.MainMenu;
        public bool IsPaused => CurrentState == MenuState.Paused;
        public bool IsMatchStarted { get; private set; } = false;
        public bool IsCombatAllowed => CurrentState == MenuState.InGame;

        // State Pengaturan (PlayerPrefs Persistence)
        public float MouseSensitivity { get; private set; } = 1.5f;
        public bool InvertY { get; private set; } = false;
        public float FieldOfView { get; private set; } = 75f;
        public float MasterVolume { get; private set; } = 1.0f;
        public float SFXVolume { get; private set; } = 1.0f;
        public int CrosshairColorIndex { get; private set; } = 0;

        // Palet Warna Crosshair Kompetitif
        public static readonly Color[] CrosshairPalette = new Color[]
        {
            new Color(0.0f, 0.95f, 1.0f, 1.0f),  // 0: Cyan Neon (Default)
            new Color(0.15f, 0.95f, 0.35f, 1.0f), // 1: Emerald Green (CS Pro)
            new Color(1.0f, 0.25f, 0.25f, 1.0f),  // 2: Crimson Red
            new Color(1.0f, 0.85f, 0.15f, 1.0f),  // 3: Amber Yellow
            new Color(1.0f, 1.0f, 1.0f, 1.0f)     // 4: Clean White
        };

        public static readonly string[] CrosshairPaletteNames = new string[]
        {
            "CYAN NEON",
            "EMERALD GREEN",
            "CRIMSON RED",
            "AMBER YELLOW",
            "CLEAN WHITE"
        };

        // State Navigasi
        private MenuState _previousState = MenuState.MainMenu;

        // Temporary sliders for settings UI
        private float _tempSensitivity = 1.5f;
        private bool _tempInvertY = false;
        private float _tempFOV = 75f;
        private float _tempMasterVol = 1.0f;
        private float _tempSFXVol = 1.0f;
        private int _tempCrosshairColorIdx = 0;

        // Audio & Visual Assets
        private AudioSource _audioSource;
        private AudioClip _hoverClip;
        private AudioClip _clickClip;
        private AudioClip _startChimeClip;
        private AudioClip _pauseClip;
        private Texture2D _pixelTex;

        // UI Styles
        private GUIStyle _titleStyle;
        private GUIStyle _subTitleStyle;
        private GUIStyle _headerStyle;
        private GUIStyle _bodyStyle;
        private GUIStyle _buttonStyle;
        private GUIStyle _labelStyle;
        private GUIStyle _valueLabelStyle;
        private bool _stylesInitialized = false;

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;

            // Inisialisasi Audio
            _audioSource = GetComponent<AudioSource>();
            if (_audioSource == null)
            {
                _audioSource = gameObject.AddComponent<AudioSource>();
            }
            _audioSource.playOnAwake = false;
            _audioSource.spatialBlend = 0f;

            _hoverClip = GenerateTone(850f, 0.025f, 0.25f);
            _clickClip = GenerateTone(1250f, 0.045f, 0.5f);
            _pauseClip = GenerateTwoTone(500f, 750f, 0.08f, 0.5f);
            _startChimeClip = GenerateMajorChord(0.5f);

            // Inisialisasi Tekstur Pixel
            _pixelTex = new Texture2D(1, 1);
            _pixelTex.SetPixel(0, 0, Color.white);
            _pixelTex.Apply();

            // Muat Pengaturan Tersimpan
            LoadSettings();

            CurrentState = initialState;
            _previousState = CurrentState;
        }

        private void Start()
        {
            ApplySettingsToSystems();

            if (CurrentState == MenuState.MainMenu)
            {
                SetCursorMenuMode(true);
                IsMatchStarted = false;
                LockPlayerControls(true);
            }
            else
            {
                StartMatch();
            }
        }

        private void Update()
        {
            HandleKeyboardInput();
        }

        private void HandleKeyboardInput()
        {
            if (Keyboard.current == null) return;

            // Tombol ESC
            if (Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                // Jika Buy Menu sedang terbuka, biarkan Buy Menu yang menangani penutupan
                if (FPSEconomyManager.Instance != null && FPSEconomyManager.Instance.IsBuyMenuOpen)
                {
                    return;
                }

                if (CurrentState == MenuState.InGame)
                {
                    PauseMatch();
                }
                else if (CurrentState == MenuState.Paused)
                {
                    ResumeMatch();
                }
                else if (CurrentState == MenuState.Settings || CurrentState == MenuState.Controls)
                {
                    ReturnFromSubmenu();
                }
            }
        }

        #region State Transitions

        public void StartMatch()
        {
            CurrentState = MenuState.InGame;
            IsMatchStarted = true;
            Time.timeScale = 1.0f;
            SetCursorMenuMode(false);
            LockPlayerControls(false);

            PlaySound(_startChimeClip, 0.85f);
        }

        public void PauseMatch()
        {
            if (CurrentState != MenuState.InGame) return;

            CurrentState = MenuState.Paused;
            Time.timeScale = 0f;
            SetCursorMenuMode(true);
            LockPlayerControls(true);

            PlaySound(_pauseClip, 0.65f);
        }

        public void ResumeMatch()
        {
            if (CurrentState != MenuState.Paused) return;

            CurrentState = MenuState.InGame;
            Time.timeScale = 1.0f;
            SetCursorMenuMode(false);
            LockPlayerControls(false);

            PlaySound(_clickClip, 0.6f);
        }

        public void RestartMatch()
        {
            Time.timeScale = 1.0f;
            ResumeMatch();
            if (TacticalRoundManager.Instance != null)
            {
                TacticalRoundManager.Instance.RestartMatch();
            }
        }

        public void ReturnToMainMenu()
        {
            CurrentState = MenuState.MainMenu;
            IsMatchStarted = false;
            Time.timeScale = 1.0f;
            SetCursorMenuMode(true);
            LockPlayerControls(true);

            if (TacticalRoundManager.Instance != null)
            {
                TacticalRoundManager.Instance.RestartMatch();
            }

            PlaySound(_clickClip, 0.6f);
        }

        public void OpenSettings()
        {
            _previousState = CurrentState;
            _tempSensitivity = MouseSensitivity;
            _tempInvertY = InvertY;
            _tempFOV = FieldOfView;
            _tempMasterVol = MasterVolume;
            _tempSFXVol = SFXVolume;
            _tempCrosshairColorIdx = CrosshairColorIndex;

            CurrentState = MenuState.Settings;
            PlaySound(_clickClip, 0.6f);
        }

        public void OpenControls()
        {
            _previousState = CurrentState;
            CurrentState = MenuState.Controls;
            PlaySound(_clickClip, 0.6f);
        }

        public void ReturnFromSubmenu()
        {
            CurrentState = _previousState;
            PlaySound(_clickClip, 0.6f);
        }

        public void QuitGame()
        {
            PlaySound(_clickClip, 0.6f);
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private void SetCursorMenuMode(bool menuMode)
        {
            Cursor.lockState = menuMode ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = menuMode;
        }

        private void LockPlayerControls(bool locked)
        {
            if (FPSCameraController.Instance != null)
            {
                FPSCameraController.Instance.CameraLookLocked = locked;
            }

            if (FPSWeaponManager.Instance != null)
            {
                FPSWeaponManager.Instance.SetWeaponsLocked(locked);
            }

            FPSMovementController movement = FindAnyObjectByType<FPSMovementController>();
            if (movement != null)
            {
                movement.MovementLocked = locked;
            }
        }

        private void OnDisable()
        {
            Time.timeScale = 1.0f;
        }

        #endregion

        #region Settings Management & PlayerPrefs

        public void LoadSettings()
        {
            MouseSensitivity = PlayerPrefs.GetFloat("Settings_Sensitivity", 1.5f);
            InvertY = PlayerPrefs.GetInt("Settings_InvertY", 0) == 1;
            FieldOfView = PlayerPrefs.GetFloat("Settings_FOV", 75f);
            MasterVolume = PlayerPrefs.GetFloat("Settings_MasterVol", 1.0f);
            SFXVolume = PlayerPrefs.GetFloat("Settings_SFXVol", 1.0f);
            CrosshairColorIndex = PlayerPrefs.GetInt("Settings_CrosshairColor", 0);
            CrosshairColorIndex = Mathf.Clamp(CrosshairColorIndex, 0, CrosshairPalette.Length - 1);
        }

        public void SaveSettings()
        {
            MouseSensitivity = _tempSensitivity;
            InvertY = _tempInvertY;
            FieldOfView = _tempFOV;
            MasterVolume = _tempMasterVol;
            SFXVolume = _tempSFXVol;
            CrosshairColorIndex = _tempCrosshairColorIdx;

            PlayerPrefs.SetFloat("Settings_Sensitivity", MouseSensitivity);
            PlayerPrefs.SetInt("Settings_InvertY", InvertY ? 1 : 0);
            PlayerPrefs.SetFloat("Settings_FOV", FieldOfView);
            PlayerPrefs.SetFloat("Settings_MasterVol", MasterVolume);
            PlayerPrefs.SetFloat("Settings_SFXVol", SFXVolume);
            PlayerPrefs.SetInt("Settings_CrosshairColor", CrosshairColorIndex);
            PlayerPrefs.Save();

            ApplySettingsToSystems();
            PlaySound(_clickClip, 0.75f);
            ReturnFromSubmenu();
        }

        public void ResetSettingsToDefault()
        {
            _tempSensitivity = 1.5f;
            _tempInvertY = false;
            _tempFOV = 75f;
            _tempMasterVol = 1.0f;
            _tempSFXVol = 1.0f;
            _tempCrosshairColorIdx = 0;
            PlaySound(_clickClip, 0.6f);
        }

        public void ApplySettingsToSystems()
        {
            // 1. Sensitivitas Mouse & Invert Y
            if (FPSCameraController.Instance != null)
            {
                FPSCameraController.Instance.MouseSensitivity = MouseSensitivity;
                FPSCameraController.Instance.InvertY = InvertY;
            }

            // 2. Field of View (FOV)
            Camera mainCam = Camera.main;
            if (mainCam != null)
            {
                mainCam.fieldOfView = FieldOfView;
            }
            FPSWeaponOverlayCamera overlayCam = FindAnyObjectByType<FPSWeaponOverlayCamera>();
            if (overlayCam != null)
            {
                overlayCam.SyncCameraFOV(FieldOfView);
            }

            // 3. Audio Volume
            AudioListener.volume = MasterVolume;

            // 4. Crosshair Color
            FPSTacticalHUD hud = FindAnyObjectByType<FPSTacticalHUD>();
            if (hud != null && CrosshairColorIndex >= 0 && CrosshairColorIndex < CrosshairPalette.Length)
            {
                hud.CrosshairColor = CrosshairPalette[CrosshairColorIndex];
            }
        }

        #endregion

        #region IMGUI Rendering

        private void OnGUI()
        {
            InitStyles();

            switch (CurrentState)
            {
                case MenuState.MainMenu:
                    DrawMainMenu();
                    break;
                case MenuState.Paused:
                    DrawPauseMenu();
                    break;
                case MenuState.Settings:
                    DrawSettingsModal();
                    break;
                case MenuState.Controls:
                    DrawControlsModal();
                    break;
            }
        }

        private void DrawMainMenu()
        {
            // Dark gradient backdrop vignette di sisi kiri
            DrawSolidRect(new Rect(0, 0, Screen.width, Screen.height), new Color(0.02f, 0.03f, 0.05f, 0.45f));
            DrawSolidRect(new Rect(0, 0, 560f, Screen.height), new Color(0.04f, 0.05f, 0.07f, 0.94f));
            DrawBorder(new Rect(0, 0, 560f, Screen.height), new Color(0.0f, 0.85f, 1.0f, 0.4f), 1.5f);

            // Left vertical neon glow accent
            DrawSolidRect(new Rect(0, 0, 5f, Screen.height), new Color(0.0f, 0.95f, 1.0f, 1f));

            float startX = 64f;
            float startY = 100f;

            // Badge Turnamen Esports
            DrawSolidRect(new Rect(startX, startY, 210f, 26f), new Color(0.0f, 0.85f, 1.0f, 0.18f));
            DrawBorder(new Rect(startX, startY, 210f, 26f), new Color(0.0f, 0.85f, 1.0f, 0.7f), 1f);
            _labelStyle.normal.textColor = new Color(0.0f, 0.95f, 1.0f);
            _labelStyle.fontSize = 11;
            _labelStyle.fontStyle = FontStyle.Bold;
            GUI.Label(new Rect(startX + 10f, startY + 4f, 190f, 20f), "COMPETITIVE ARENA PROTOCOL", _labelStyle);

            startY += 40f;

            // Judul Game Besar
            _titleStyle.normal.textColor = Color.white;
            GUI.Label(new Rect(startX, startY, 480f, 54f), "PROJECT RETAKE", _titleStyle);

            startY += 48f;

            // Sub-teks Deskripsi Taktis
            _subTitleStyle.normal.textColor = new Color(0.65f, 0.72f, 0.8f);
            GUI.Label(new Rect(startX, startY, 460f, 24f), "FIRST TO 3 WINS // BOMB DEFUSAL ESPORTS", _subTitleStyle);

            startY += 65f;

            // Tombol-Tombol Navigasi Utama
            float btnW = 380f;
            float btnH = 58f;
            float spacing = 14f;

            if (DrawTacticalButton(new Rect(startX, startY, btnW, btnH), "PLAY MATCH", "Enter competitive arena and start Round 1", true))
            {
                StartMatch();
            }
            startY += btnH + spacing;

            if (DrawTacticalButton(new Rect(startX, startY, btnW, btnH), "SETTINGS", "Configure mouse sensitivity, optics & audio", false))
            {
                OpenSettings();
            }
            startY += btnH + spacing;

            if (DrawTacticalButton(new Rect(startX, startY, btnW, btnH), "CONTROLS & MANUAL", "View keyboard bindings and combat mechanics", false))
            {
                OpenControls();
            }
            startY += btnH + spacing;

            if (DrawTacticalButton(new Rect(startX, startY, btnW, btnH), "QUIT GAME", "Exit to desktop", false))
            {
                QuitGame();
            }

            // Footer build tag
            float footerY = Screen.height - 40f;
            _labelStyle.normal.textColor = new Color(0.4f, 0.45f, 0.5f);
            _labelStyle.fontSize = 11;
            _labelStyle.fontStyle = FontStyle.Normal;
            GUI.Label(new Rect(startX, footerY, 400f, 20f), "ENGINE: UNITY URP // VERSION 1.2 TOURNAMENT", _labelStyle);
        }

        private void DrawPauseMenu()
        {
            // Dimmed screen backdrop
            DrawSolidRect(new Rect(0, 0, Screen.width, Screen.height), new Color(0.02f, 0.03f, 0.05f, 0.75f));

            float w = 440f;
            float h = 370f;
            float x = (Screen.width - w) * 0.5f;
            float y = Mathf.Max(12f, (Screen.height - h) * 0.5f);

            // Box Container
            DrawSolidRect(new Rect(x, y, w, h), new Color(0.05f, 0.07f, 0.1f, 0.96f));
            DrawBorder(new Rect(x, y, w, h), new Color(0.0f, 0.85f, 1.0f, 0.7f), 1.5f);
            DrawSolidRect(new Rect(x, y, w, 4f), new Color(0.0f, 0.95f, 1.0f, 1f));

            // Header Pause
            _headerStyle.normal.textColor = Color.white;
            _headerStyle.alignment = TextAnchor.MiddleCenter;
            GUI.Label(new Rect(x, y + 14f, w, 26f), "MATCH PAUSED", _headerStyle);

            string roundInfo = TacticalRoundManager.Instance != null ?
                $"ROUND {TacticalRoundManager.Instance.CurrentRound} // SCORE: CT {TacticalRoundManager.Instance.PlayerScore} - {TacticalRoundManager.Instance.BotScore} T" :
                "TACTICAL TOURNAMENT MATCH";

            _subTitleStyle.normal.textColor = new Color(0.0f, 0.85f, 1.0f);
            _subTitleStyle.alignment = TextAnchor.MiddleCenter;
            GUI.Label(new Rect(x, y + 42f, w, 20f), roundInfo, _subTitleStyle);
            _subTitleStyle.alignment = TextAnchor.MiddleLeft;

            float btnW = 340f;
            float btnH = 38f;
            float btnX = x + (w - btnW) * 0.5f;
            float btnY = y + 72f;
            float spacing = 8f;

            if (DrawTacticalButton(new Rect(btnX, btnY, btnW, btnH), "RESUME MATCH", "Continue battle [ESC]", true))
            {
                ResumeMatch();
            }
            btnY += btnH + spacing;

            if (DrawTacticalButton(new Rect(btnX, btnY, btnW, btnH), "RESTART MATCH", "Reset tournament to Round 1", false))
            {
                RestartMatch();
            }
            btnY += btnH + spacing;

            if (DrawTacticalButton(new Rect(btnX, btnY, btnW, btnH), "SETTINGS", "Adjust mouse sensitivity & audio", false))
            {
                OpenSettings();
            }
            btnY += btnH + spacing;

            if (DrawTacticalButton(new Rect(btnX, btnY, btnW, btnH), "CONTROLS MANUAL", "View key bindings", false))
            {
                OpenControls();
            }
            btnY += btnH + spacing;

            if (DrawTacticalButton(new Rect(btnX, btnY, btnW, btnH), "RETURN TO MAIN MENU", "Quit current match to title", false))
            {
                ReturnToMainMenu();
            }
            btnY += btnH + spacing;

            if (DrawTacticalButton(new Rect(btnX, btnY, btnW, btnH), "QUIT GAME", "Exit to desktop", false))
            {
                QuitGame();
            }
        }

        private void DrawSettingsModal()
        {
            // Dimmed backdrop
            DrawSolidRect(new Rect(0, 0, Screen.width, Screen.height), new Color(0.02f, 0.03f, 0.05f, 0.82f));

            float w = 660f;
            float h = 450f;
            float x = (Screen.width - w) * 0.5f;
            float y = Mathf.Max(10f, (Screen.height - h) * 0.5f);

            // Main Panel
            DrawSolidRect(new Rect(x, y, w, h), new Color(0.05f, 0.07f, 0.1f, 0.96f));
            DrawBorder(new Rect(x, y, w, h), new Color(0.0f, 0.85f, 1.0f, 0.8f), 1.5f);
            DrawSolidRect(new Rect(x, y, w, 4f), new Color(0.0f, 0.95f, 1.0f, 1f));

            // Header
            _headerStyle.normal.textColor = Color.white;
            _headerStyle.alignment = TextAnchor.MiddleCenter;
            GUI.Label(new Rect(x, y + 14f, w, 26f), "TACTICAL SETTINGS & OPTICS", _headerStyle);
            _headerStyle.alignment = TextAnchor.MiddleLeft;

            float contentX = x + 30f;
            float contentW = w - 60f;
            float curY = y + 46f;

            // SECTION 1: MOUSE & INPUT CONTROLS
            DrawCategoryHeader(contentX, curY, contentW, "1. MOUSE & INPUT CONTROLS");
            curY += 22f;

            _tempSensitivity = DrawSlider(new Rect(contentX, curY, contentW, 26f), "MOUSE SENSITIVITY", _tempSensitivity, 0.2f, 4.0f, $"{_tempSensitivity:F2}x");
            curY += 30f;

            _tempInvertY = DrawToggle(new Rect(contentX, curY, contentW, 24f), "INVERT VERTICAL LOOK (Y-AXIS)", _tempInvertY);
            curY += 30f;

            // SECTION 2: OPTICS & DISPLAY
            DrawCategoryHeader(contentX, curY, contentW, "2. OPTICS & RETICLE DISPLAY");
            curY += 22f;

            _tempFOV = DrawSlider(new Rect(contentX, curY, contentW, 26f), "FIELD OF VIEW (FOV)", _tempFOV, 60f, 100f, $"{Mathf.RoundToInt(_tempFOV)} DEG");
            curY += 30f;

            // Crosshair Color Selector & Live Preview Box
            curY = DrawCrosshairColorSelector(contentX, curY, contentW);
            curY += 10f;

            // SECTION 3: AUDIO MASTER
            DrawCategoryHeader(contentX, curY, contentW, "3. AUDIO MASTER");
            curY += 22f;

            _tempMasterVol = DrawSlider(new Rect(contentX, curY, contentW, 26f), "MASTER VOLUME", _tempMasterVol, 0.0f, 1.0f, $"{Mathf.RoundToInt(_tempMasterVol * 100f)}%");
            curY += 30f;

            _tempSFXVol = DrawSlider(new Rect(contentX, curY, contentW, 26f), "SFX & COMBAT VOLUME", _tempSFXVol, 0.0f, 1.0f, $"{Mathf.RoundToInt(_tempSFXVol * 100f)}%");
            curY += 36f;

            // ACTION BUTTONS: [SAVE & APPLY], [RESET DEFAULTS], [BACK]
            float btnW = (contentW - 16f) / 3f;
            float btnH = 36f;

            if (DrawTacticalButton(new Rect(contentX, curY, btnW, btnH), "SAVE & APPLY", "", true))
            {
                SaveSettings();
            }

            if (DrawTacticalButton(new Rect(contentX + btnW + 8f, curY, btnW, btnH), "RESET DEFAULTS", "", false))
            {
                ResetSettingsToDefault();
            }

            if (DrawTacticalButton(new Rect(contentX + (btnW + 8f) * 2f, curY, btnW, btnH), "BACK [ESC]", "", false))
            {
                ReturnFromSubmenu();
            }
        }

        private void DrawControlsModal()
        {
            // Dimmed backdrop
            DrawSolidRect(new Rect(0, 0, Screen.width, Screen.height), new Color(0.02f, 0.03f, 0.05f, 0.82f));

            float w = 760f;
            float h = 450f;
            float x = (Screen.width - w) * 0.5f;
            float y = Mathf.Max(10f, (Screen.height - h) * 0.5f);

            // Main Panel
            DrawSolidRect(new Rect(x, y, w, h), new Color(0.05f, 0.07f, 0.1f, 0.96f));
            DrawBorder(new Rect(x, y, w, h), new Color(0.0f, 0.85f, 1.0f, 0.8f), 1.5f);
            DrawSolidRect(new Rect(x, y, w, 4f), new Color(0.0f, 0.95f, 1.0f, 1f));

            // Header
            _headerStyle.normal.textColor = Color.white;
            _headerStyle.alignment = TextAnchor.MiddleCenter;
            GUI.Label(new Rect(x, y + 14f, w, 26f), "TACTICAL CONTROLS & FIELD MANUAL", _headerStyle);
            _headerStyle.alignment = TextAnchor.MiddleLeft;

            float colW = 340f;
            float colH = 155f;
            float col1X = x + 25f;
            float col2X = x + w - colW - 25f;
            float row1Y = y + 46f;
            float row2Y = row1Y + colH + 10f;

            // 1. Movement Box
            DrawGuideBox(col1X, row1Y, colW, colH, "MOVEMENT CONTROLS", new string[,]
            {
                { "W, A, S, D", "Walk & Strafe" },
                { "LEFT SHIFT", "Tactical Sprint (+35% Spd)" },
                { "LEFT CTRL", "Crouch (Silent Walk)" },
                { "SPACEBAR", "Jump / Mantle Obstacle" },
                { "C KEY", "Combat Slide (From Sprint)" }
            });

            // 2. Weapon Inventory Box
            DrawGuideBox(col2X, row1Y, colW, colH, "WEAPON INVENTORY", new string[,]
            {
                { "1 / NUMPAD 1", "Slot 1: Primary SMG-K (30/90)" },
                { "2 / NUMPAD 2", "Slot 2: Secondary Pistol-A (12/36)" },
                { "3 / NUMPAD 3", "Slot 3: Combat Tactical Knife" },
                { "4 / NUMPAD 4", "Slot 4: Frag Grenade" },
                { "SCROLL WHEEL", "Seamless Fast Weapon Cycle" }
            });

            // 3. Combat Mechanics Box
            DrawGuideBox(col1X, row2Y, colW, colH, "COMBAT MECHANICS", new string[,]
            {
                { "LEFT CLICK", "Full-Auto Fire / Knife Slash" },
                { "RIGHT CLICK", "Heavy Knife Backstab (100 Dmg)" },
                { "GRENADE LMB", "High Velocity Long Throw (18 m/s)" },
                { "GRENADE RMB", "Underhand Short Roll (8 m/s)" },
                { "R KEY", "Reload Weapon Magazine" }
            });

            // 4. Tactical Objectives Box
            DrawGuideBox(col2X, row2Y, colW, colH, "OBJECTIVES & ECONOMY", new string[,]
            {
                { "B KEY", "Open Buy Menu ($800 starting cash)" },
                { "HOLD E KEY", "Defuse C4 Bomb (5.0s kit / 10s no kit)" },
                { "TAB KEY", "Match Scoreboard & Tournament Wins" },
                { "KNIFE SPEED", "+12% Sprint Velocity when equipped" },
                { "ARMOR", "Kevlar absorbs 50% damage + Helm HS save" }
            });

            // Back Button
            float btnW = 200f;
            float btnH = 34f;
            float btnX = x + (w - btnW) * 0.5f;
            float btnY = y + h - btnH - 12f;

            if (DrawTacticalButton(new Rect(btnX, btnY, btnW, btnH), "BACK [ESC]", "", true))
            {
                ReturnFromSubmenu();
            }
        }

        #endregion

        #region UI Helper Widgets

        private bool DrawTacticalButton(Rect rect, string text, string subtext, bool isPrimary)
        {
            Vector2 mousePos = Event.current.mousePosition;
            bool isHovered = rect.Contains(mousePos);

            Color bgCol = isPrimary ?
                (isHovered ? new Color(0.0f, 0.45f, 0.55f, 0.95f) : new Color(0.0f, 0.28f, 0.38f, 0.90f)) :
                (isHovered ? new Color(0.12f, 0.16f, 0.22f, 0.95f) : new Color(0.07f, 0.09f, 0.13f, 0.88f));

            Color borderCol = isPrimary ?
                (isHovered ? new Color(0.2f, 1.0f, 1.0f, 1.0f) : new Color(0.0f, 0.85f, 1.0f, 0.8f)) :
                (isHovered ? new Color(0.0f, 0.85f, 1.0f, 0.9f) : new Color(0.25f, 0.32f, 0.4f, 0.6f));

            // Background & Border
            DrawSolidRect(rect, bgCol);
            DrawBorder(rect, borderCol, isHovered ? 1.5f : 1f);

            // Left accent bar on hover
            if (isHovered || isPrimary)
            {
                DrawSolidRect(new Rect(rect.x, rect.y, 4f, rect.height), isPrimary ? new Color(0.0f, 0.95f, 1.0f) : new Color(0.0f, 0.85f, 1.0f));
            }

            // Text
            _buttonStyle.normal.textColor = isHovered ? Color.white : (isPrimary ? Color.white : new Color(0.85f, 0.9f, 0.95f));
            _buttonStyle.fontSize = isPrimary ? 14 : 13;
            _buttonStyle.fontStyle = FontStyle.Bold;

            float textY = string.IsNullOrEmpty(subtext) ? rect.y + (rect.height - 20f) * 0.5f : rect.y + 7f;
            GUI.Label(new Rect(rect.x + 16f, textY, rect.width - 24f, 22f), text, _buttonStyle);

            if (!string.IsNullOrEmpty(subtext))
            {
                _labelStyle.normal.textColor = isHovered ? new Color(0.7f, 0.9f, 1.0f) : new Color(0.48f, 0.55f, 0.65f);
                _labelStyle.fontSize = 10;
                _labelStyle.fontStyle = FontStyle.Normal;
                GUI.Label(new Rect(rect.x + 16f, textY + 20f, rect.width - 24f, 16f), subtext, _labelStyle);
            }

            // Click handling
            if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && isHovered)
            {
                Event.current.Use();
                return true;
            }

            return false;
        }

        private float DrawSlider(Rect rect, string label, float value, float minVal, float maxVal, string displayVal)
        {
            float labelW = 200f;
            float valW = 90f;
            float sliderW = rect.width - labelW - valW - 20f;

            _labelStyle.normal.textColor = Color.white;
            _labelStyle.fontSize = 12;
            _labelStyle.fontStyle = FontStyle.Bold;
            GUI.Label(new Rect(rect.x, rect.y + 4f, labelW, 24f), label, _labelStyle);

            // Slider Track Background
            float sliderX = rect.x + labelW + 10f;
            float sliderY = rect.y + 8f;
            float sliderH = 10f;

            DrawSolidRect(new Rect(sliderX, sliderY, sliderW, sliderH), new Color(0.12f, 0.15f, 0.2f, 1f));

            float ratio = Mathf.Clamp01((value - minVal) / (maxVal - minVal));
            DrawSolidRect(new Rect(sliderX, sliderY, sliderW * ratio, sliderH), new Color(0.0f, 0.85f, 1.0f, 1f));
            DrawBorder(new Rect(sliderX, sliderY, sliderW, sliderH), new Color(0.25f, 0.35f, 0.45f, 0.8f), 1f);

            // Handle Thumb
            float thumbX = sliderX + sliderW * ratio - 6f;
            DrawSolidRect(new Rect(thumbX, sliderY - 4f, 12f, sliderH + 8f), Color.white);
            DrawBorder(new Rect(thumbX, sliderY - 4f, 12f, sliderH + 8f), new Color(0.0f, 0.85f, 1.0f), 1f);

            // Click or Drag Detection
            Vector2 mousePos = Event.current.mousePosition;
            Rect hitRect = new Rect(sliderX - 8f, sliderY - 8f, sliderW + 16f, sliderH + 16f);
            if ((Event.current.type == EventType.MouseDown || Event.current.type == EventType.MouseDrag) && Event.current.button == 0 && hitRect.Contains(mousePos))
            {
                float newRatio = Mathf.Clamp01((mousePos.x - sliderX) / sliderW);
                value = minVal + newRatio * (maxVal - minVal);
                Event.current.Use();
            }

            // Value Tag
            _valueLabelStyle.normal.textColor = new Color(0.0f, 0.95f, 1.0f);
            _valueLabelStyle.fontSize = 12;
            _valueLabelStyle.fontStyle = FontStyle.Bold;
            _valueLabelStyle.alignment = TextAnchor.MiddleRight;
            GUI.Label(new Rect(rect.x + rect.width - valW, rect.y + 4f, valW, 24f), displayVal, _valueLabelStyle);
            _valueLabelStyle.alignment = TextAnchor.MiddleLeft;

            return value;
        }

        private bool DrawToggle(Rect rect, string label, bool val)
        {
            float boxSize = 22f;
            Rect boxRect = new Rect(rect.x + rect.width - boxSize - 10f, rect.y + 2f, boxSize, boxSize);

            _labelStyle.normal.textColor = Color.white;
            _labelStyle.fontSize = 12;
            _labelStyle.fontStyle = FontStyle.Bold;
            GUI.Label(new Rect(rect.x, rect.y + 4f, rect.width - boxSize - 20f, 24f), label, _labelStyle);

            DrawSolidRect(boxRect, val ? new Color(0.0f, 0.55f, 0.7f, 1f) : new Color(0.12f, 0.15f, 0.2f, 1f));
            DrawBorder(boxRect, val ? new Color(0.0f, 0.95f, 1.0f) : new Color(0.3f, 0.38f, 0.48f), 1.5f);

            if (val)
            {
                // Inner check dot
                DrawSolidRect(new Rect(boxRect.x + 4f, boxRect.y + 4f, boxSize - 8f, boxSize - 8f), Color.white);
            }

            if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && (rect.Contains(Event.current.mousePosition) || boxRect.Contains(Event.current.mousePosition)))
            {
                val = !val;
                PlaySound(_clickClip, 0.5f);
                Event.current.Use();
            }

            return val;
        }

        private float DrawCrosshairColorSelector(float startX, float startY, float totalW)
        {
            float labelW = 200f;
            _labelStyle.normal.textColor = Color.white;
            _labelStyle.fontSize = 12;
            _labelStyle.fontStyle = FontStyle.Bold;
            GUI.Label(new Rect(startX, startY + 4f, labelW, 22f), "CROSSHAIR COLOR", _labelStyle);

            float swatchX = startX + labelW + 10f;
            float swatchSize = 24f;
            float spacing = 6f;

            for (int i = 0; i < CrosshairPalette.Length; i++)
            {
                Rect swRect = new Rect(swatchX + i * (swatchSize + spacing), startY + 2f, swatchSize, swatchSize);
                bool isSelected = (_tempCrosshairColorIdx == i);

                DrawSolidRect(swRect, CrosshairPalette[i]);
                DrawBorder(swRect, isSelected ? Color.white : new Color(0.15f, 0.2f, 0.25f, 0.8f), isSelected ? 2.5f : 1f);

                if (isSelected)
                {
                    // Mini accent border indicator
                    DrawBorder(new Rect(swRect.x - 2f, swRect.y - 2f, swRect.width + 4f, swRect.height + 4f), new Color(0.0f, 0.95f, 1.0f), 1.5f);
                }

                if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && swRect.Contains(Event.current.mousePosition))
                {
                    _tempCrosshairColorIdx = i;
                    PlaySound(_clickClip, 0.5f);
                    Event.current.Use();
                }
            }

            // Live Crosshair Preview Box
            float previewSize = 44f;
            float previewX = startX + totalW - previewSize - 10f;
            float previewY = startY - 8f;
            Rect prevBox = new Rect(previewX, previewY, previewSize, previewSize);

            DrawSolidRect(prevBox, new Color(0.08f, 0.1f, 0.14f, 0.95f));
            DrawBorder(prevBox, new Color(0.3f, 0.4f, 0.5f, 0.8f), 1f);

            // Draw Crosshair Preview Lines inside box
            Color crossCol = CrosshairPalette[_tempCrosshairColorIdx];
            float cx = prevBox.x + previewSize * 0.5f;
            float cy = prevBox.y + previewSize * 0.5f;
            float gap = 3f;
            float len = 7f;
            float th = 2f;

            DrawSolidRect(new Rect(cx - len - gap, cy - th * 0.5f, len, th), crossCol); // Left
            DrawSolidRect(new Rect(cx + gap, cy - th * 0.5f, len, th), crossCol);         // Right
            DrawSolidRect(new Rect(cx - th * 0.5f, cy - len - gap, th, len), crossCol); // Top
            DrawSolidRect(new Rect(cx - th * 0.5f, cy + gap, th, len), crossCol);         // Bottom

            return startY + 30f;
        }

        private void DrawCategoryHeader(float x, float y, float w, string title)
        {
            DrawSolidRect(new Rect(x, y, w, 20f), new Color(0.0f, 0.85f, 1.0f, 0.12f));
            DrawSolidRect(new Rect(x, y, 3f, 20f), new Color(0.0f, 0.95f, 1.0f));

            _labelStyle.normal.textColor = new Color(0.0f, 0.95f, 1.0f);
            _labelStyle.fontSize = 11;
            _labelStyle.fontStyle = FontStyle.Bold;
            GUI.Label(new Rect(x + 10f, y + 2f, w - 20f, 18f), title, _labelStyle);
        }

        private void DrawGuideBox(float x, float y, float w, float h, string title, string[,] keyPairs)
        {
            DrawSolidRect(new Rect(x, y, w, h), new Color(0.07f, 0.09f, 0.13f, 0.92f));
            DrawBorder(new Rect(x, y, w, h), new Color(0.22f, 0.28f, 0.36f, 0.7f), 1f);
            DrawSolidRect(new Rect(x, y, w, 22f), new Color(0.0f, 0.85f, 1.0f, 0.15f));
            DrawSolidRect(new Rect(x, y, 3f, 22f), new Color(0.0f, 0.95f, 1.0f));

            _labelStyle.normal.textColor = new Color(0.0f, 0.95f, 1.0f);
            _labelStyle.fontSize = 11;
            _labelStyle.fontStyle = FontStyle.Bold;
            GUI.Label(new Rect(x + 10f, y + 2f, w - 20f, 18f), title, _labelStyle);

            float rowY = y + 26f;
            int count = keyPairs.GetLength(0);
            for (int i = 0; i < count; i++)
            {
                string key = keyPairs[i, 0];
                string desc = keyPairs[i, 1];

                // Key Badge
                _valueLabelStyle.normal.textColor = Color.white;
                _valueLabelStyle.fontSize = 10;
                _valueLabelStyle.fontStyle = FontStyle.Bold;
                GUI.Label(new Rect(x + 10f, rowY, 115f, 20f), key, _valueLabelStyle);

                // Description
                _labelStyle.normal.textColor = new Color(0.7f, 0.76f, 0.84f);
                _labelStyle.fontSize = 10;
                _labelStyle.fontStyle = FontStyle.Normal;
                GUI.Label(new Rect(x + 128f, rowY, w - 136f, 20f), desc, _labelStyle);

                rowY += 24f;
            }
        }

        private void DrawSolidRect(Rect rect, Color color)
        {
            Color old = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, _pixelTex);
            GUI.color = old;
        }

        private void DrawBorder(Rect rect, Color color, float thickness)
        {
            Color old = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, thickness), _pixelTex);
            GUI.DrawTexture(new Rect(rect.x, rect.y + rect.height - thickness, rect.width, thickness), _pixelTex);
            GUI.DrawTexture(new Rect(rect.x, rect.y, thickness, rect.height), _pixelTex);
            GUI.DrawTexture(new Rect(rect.x + rect.width - thickness, rect.y, thickness, rect.height), _pixelTex);
            GUI.color = old;
        }

        private void InitStyles()
        {
            if (_stylesInitialized) return;

            _titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 32,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft
            };

            _subTitleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft
            };

            _headerStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 18,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft
            };

            _bodyStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                fontStyle = FontStyle.Normal,
                alignment = TextAnchor.MiddleLeft
            };

            _buttonStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft
            };

            _labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                fontStyle = FontStyle.Normal,
                alignment = TextAnchor.MiddleLeft
            };

            _valueLabelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft
            };

            _stylesInitialized = true;
        }

        #endregion

        #region Procedural Audio Generation

        private void PlaySound(AudioClip clip, float volume)
        {
            if (clip != null && _audioSource != null)
            {
                _audioSource.pitch = 1.0f;
                _audioSource.PlayOneShot(clip, volume * SFXVolume);
            }
        }

        private AudioClip GenerateTone(float frequency, float duration, float amplitude)
        {
            int sampleRate = 44100;
            int totalSamples = Mathf.CeilToInt(sampleRate * duration);
            float[] samples = new float[totalSamples];

            for (int i = 0; i < totalSamples; i++)
            {
                float t = (float)i / sampleRate;
                float env = 1.0f - (t / duration); // Linear decay
                samples[i] = Mathf.Sin(2f * Mathf.PI * frequency * t) * amplitude * env;
            }

            AudioClip clip = AudioClip.Create("Proc_Tone", totalSamples, 1, sampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        private AudioClip GenerateTwoTone(float freq1, float freq2, float duration, float amplitude)
        {
            int sampleRate = 44100;
            int totalSamples = Mathf.CeilToInt(sampleRate * duration);
            float[] samples = new float[totalSamples];
            int half = totalSamples / 2;

            for (int i = 0; i < totalSamples; i++)
            {
                float t = (float)i / sampleRate;
                float freq = i < half ? freq1 : freq2;
                float env = 1.0f - ((float)i / totalSamples);
                samples[i] = Mathf.Sin(2f * Mathf.PI * freq * t) * amplitude * env;
            }

            AudioClip clip = AudioClip.Create("Proc_TwoTone", totalSamples, 1, sampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        private AudioClip GenerateMajorChord(float duration)
        {
            int sampleRate = 44100;
            int totalSamples = Mathf.CeilToInt(sampleRate * duration);
            float[] samples = new float[totalSamples];

            float[] freqs = { 440f, 554.37f, 659.25f }; // A Major (A4, C#5, E5)

            for (int i = 0; i < totalSamples; i++)
            {
                float t = (float)i / sampleRate;
                float env = Mathf.Exp(-t * 3.5f);
                float val = 0f;
                foreach (float f in freqs)
                {
                    val += Mathf.Sin(2f * Mathf.PI * f * t);
                }
                samples[i] = (val / freqs.Length) * 0.7f * env;
            }

            AudioClip clip = AudioClip.Create("Proc_MajorChord", totalSamples, 1, sampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        #endregion
    }
}
