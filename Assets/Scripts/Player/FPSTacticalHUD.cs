using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using FPSFramework.Combat;

namespace FPSFramework.Player
{
    /// <summary>
    /// Tactical Combat HUD & Floating Damage Numbers ala Tactical Shooter (CS2 / Valorant):
    /// 1. Floating 3D Damage Numbers (Popup text dengan drop shadow: Bodyshot putih, Headshot CRIT merah/emas).
    /// 2. Tactical Minimalist HUD (Status HP bar di kiri bawah, Ammo counter & Reload bar di kanan bawah).
    /// 3. Elimination Banner (Notifikasi eliminasi saat dummy tewas dengan kill cue sound).
    /// 4. Dynamic Crosshair dengan Follow Recoil CS2 (cl_crosshair_recoil 1) dan Hitmarker X.
    /// 5. Tombol F3 untuk toggle panel telemetri fisika debug.
    /// Didesain bersih, elegan, dan tanpa emotikon.
    /// </summary>
    public class FPSTacticalHUD : MonoBehaviour
    {
        [Header("Referensi Komponen")]
        [SerializeField] private FPSMovementController movementController;
        [SerializeField] private FPSWeapon weapon;
        [SerializeField] private PlayerHealth playerHealth;

        [Header("Floating 3D Damage Numbers")]
        [Tooltip("Aktifkan teks damage melayang saat peluru mengenai target.")]
        [SerializeField] private bool enableDamageNumbers = true;
        [SerializeField] private float damageNumberDuration = 0.85f;
        [SerializeField] private float floatUpSpeed = 1.3f;
        [SerializeField] private Color bodyDamageColor = Color.white;
        [SerializeField] private Color headDamageColor = new Color(1.0f, 0.25f, 0.25f); // Crimson Red

        [Header("CS2 Follow Recoil & Crosshair")]
        [Tooltip("Crosshair mengikuti arah hentakan peluru (CS2 Follow Recoil).")]
        [SerializeField] private bool followRecoil = true;
        [SerializeField] private float followRecoilSnappiness = 32f;
        [SerializeField] private Color crosshairColor = Color.cyan;

        [Header("Tactical Elimination Banner")]
        [Tooltip("Tampilkan banner eliminasi saat target tewas.")]
        [SerializeField] private bool enableEliminationBanner = true;
        [SerializeField] private float bannerDuration = 2.2f;

        [Header("Debug Telemetry")]
        [Tooltip("Tampilkan kotak telemetri fisika pergerakan (dapat di-toggle dengan tombol F3).")]
        [SerializeField] private bool showDebugTelemetry = false;

        // Struktur data untuk floating damage popup
        private class DamagePopup
        {
            public Vector3 WorldPos;
            public string Text;
            public bool IsHeadshot;
            public float RemainingTime;
            public float MaxDuration;
            public Vector3 Velocity;
        }

        private readonly List<DamagePopup> _activePopups = new List<DamagePopup>();

        // Struktur data untuk Tactical Esports Killfeed
        private class KillfeedEntry
        {
            public string Attacker;
            public string Weapon;
            public string Victim;
            public bool WasHeadshot;
            public float RemainingTime;
            public float SlideTimer;
        }

        private readonly List<KillfeedEntry> _activeKillfeed = new List<KillfeedEntry>();
        private const float KillfeedDuration = 5.0f;
        private const int MaxKillfeedEntries = 4;

        // Multi-Kill Streak State
        private int _killStreakCount = 0;
        private float _lastKillTime = -99f;
        private const float KillStreakWindow = 4.5f;

        // Elimination Banner State
        private float _bannerTimer;
        private string _bannerText = "";
        private bool _bannerIsHeadshot = false;

        // Hitmarker State
        private float _hitmarkerTimer;
        private bool _hitmarkerIsHeadshot;

        // Player Health & Damage Feedback State
        private PlayerHealth _playerHealth;
        private float _damageFlashTimer;

        // Crosshair Smoothing State
        private Vector2 _smoothCrosshairScreenPos;
        private bool _isCrosshairInitialized;
        private float _smoothCrosshairGap = 6f;

        // Audio & Visual Helpers
        private AudioSource _audioSource;
        private AudioClip _procKillChime;
        private AudioClip _procHeadshotClip;
        private AudioClip _procBodyshotClip;
        private Texture2D _pixelTex;

        // Styles
        private GUIStyle _hudTitleStyle;
        private GUIStyle _hudBigNumStyle;
        private GUIStyle _hudSubStyle;
        private GUIStyle _damageTextStyle;
        private GUIStyle _bannerStyle;
        private GUIStyle _debugStyle;
        private GUIStyle _scoreboardTimerStyle;
        private GUIStyle _scoreboardTeamStyle;
        private GUIStyle _scoreboardScoreStyle;
        private GUIStyle _scoreboardSubStyle;
        private GUIStyle _roundBannerBigStyle;
        private GUIStyle _roundBannerSubStyle;

        private void Awake()
        {
            if (movementController == null)
            {
                movementController = GetComponent<FPSMovementController>() ?? GetComponentInParent<FPSMovementController>();
            }

            if (weapon == null)
            {
                weapon = GetComponentInChildren<FPSWeapon>() ?? GetComponentInParent<FPSWeapon>();
            }

            if (playerHealth == null)
            {
                playerHealth = GetComponent<PlayerHealth>() ?? GetComponentInParent<PlayerHealth>();
            }
            _playerHealth = playerHealth;

            // Inisialisasi AudioSource untuk UI feedback
            _audioSource = GetComponent<AudioSource>();
            if (_audioSource == null)
            {
                _audioSource = gameObject.AddComponent<AudioSource>();
            }
            _audioSource.playOnAwake = false;
            _audioSource.spatialBlend = 0f;

            // Buat tekstur 1x1 untuk menggambar bar & kotak solid
            _pixelTex = new Texture2D(1, 1);
            _pixelTex.SetPixel(0, 0, Color.white);
            _pixelTex.Apply();

            GenerateKillChimeAudio();
            GenerateHitAudio();
        }

        private void Start()
        {
            // Pastikan Radar Tactical otomatis terpasang
            if (GetComponent<FPSTacticalRadar>() == null && GetComponentInParent<FPSTacticalRadar>() == null)
            {
                gameObject.AddComponent<FPSTacticalRadar>();
            }

            // Pastikan TacticalRoundManager aktif di scene
            if (FindAnyObjectByType<TacticalRoundManager>() == null)
            {
                GameObject roundMgr = new GameObject("TacticalRoundManager");
                roundMgr.AddComponent<TacticalRoundManager>();
            }
        }

        private void OnEnable()
        {
            FPSWeapon.OnAnyTargetHit += HandleTargetHit;
            FPSWeapon.OnAnyDamageDealt += HandleDamageDealt;
            TargetDummy.OnDummyEliminated += HandleDummyEliminated;

            if (_playerHealth == null)
            {
                _playerHealth = playerHealth ?? GetComponent<PlayerHealth>() ?? GetComponentInParent<PlayerHealth>() ?? FindAnyObjectByType<PlayerHealth>();
            }

            if (_playerHealth != null)
            {
                _playerHealth.OnDamaged += HandlePlayerDamaged;
            }
        }

        private void OnDisable()
        {
            FPSWeapon.OnAnyTargetHit -= HandleTargetHit;
            FPSWeapon.OnAnyDamageDealt -= HandleDamageDealt;
            TargetDummy.OnDummyEliminated -= HandleDummyEliminated;

            if (_playerHealth != null)
            {
                _playerHealth.OnDamaged -= HandlePlayerDamaged;
            }
        }

        private void HandlePlayerDamaged(Vector3 dir)
        {
            _damageFlashTimer = 0.35f;
        }

        private void Update()
        {
            // Toggle panel telemetri dengan tombol F3 (New Input System)
            if (Keyboard.current != null && Keyboard.current.f3Key.wasPressedThisFrame)
            {
                showDebugTelemetry = !showDebugTelemetry;
            }

            // Cari weapon jika belum terhubung
            if (weapon == null)
            {
                weapon = GetComponentInChildren<FPSWeapon>() ?? GetComponentInParent<FPSWeapon>();
            }

            // Update hitmarker timer
            if (_hitmarkerTimer > 0f)
            {
                _hitmarkerTimer -= Time.deltaTime;
            }

            // Update banner timer
            if (_bannerTimer > 0f)
            {
                _bannerTimer -= Time.deltaTime;
            }

            // Update damage flash timer
            if (_damageFlashTimer > 0f)
            {
                _damageFlashTimer -= Time.deltaTime;
            }

            // Update floating damage popups
            UpdateDamagePopups();

            // Update killfeed timers
            for (int i = _activeKillfeed.Count - 1; i >= 0; i--)
            {
                KillfeedEntry kf = _activeKillfeed[i];
                kf.RemainingTime -= Time.deltaTime;
                if (kf.SlideTimer > 0f) kf.SlideTimer -= Time.deltaTime;

                if (kf.RemainingTime <= 0f)
                {
                    _activeKillfeed.RemoveAt(i);
                }
            }
        }

        private void UpdateDamagePopups()
        {
            for (int i = _activePopups.Count - 1; i >= 0; i--)
            {
                DamagePopup popup = _activePopups[i];
                popup.RemainingTime -= Time.deltaTime;

                if (popup.RemainingTime <= 0f)
                {
                    _activePopups.RemoveAt(i);
                    continue;
                }

                popup.WorldPos += popup.Velocity * Time.deltaTime;
                popup.Velocity = Vector3.Lerp(popup.Velocity, Vector3.up * 0.4f, Time.deltaTime * 3f);
            }
        }

        private void HandleTargetHit(bool isHeadshot)
        {
            _hitmarkerIsHeadshot = isHeadshot;
            _hitmarkerTimer = 0.16f;

            // Audio feedback instan
            if (_audioSource != null)
            {
                AudioClip clipToPlay = isHeadshot ? _procHeadshotClip : _procBodyshotClip;
                if (clipToPlay != null)
                {
                    _audioSource.pitch = isHeadshot ? Random.Range(1.02f, 1.08f) : Random.Range(0.96f, 1.02f);
                    _audioSource.PlayOneShot(clipToPlay, isHeadshot ? 0.95f : 0.75f);
                }
            }
        }

        private void HandleDamageDealt(float damage, bool isHeadshot, Vector3 hitPoint)
        {
            if (!enableDamageNumbers) return;

            // Beri sedikit offset acak agar burst berturut-turut tidak saling menumpuk
            Vector3 scatter = Random.insideUnitSphere * 0.12f;
            scatter.y = Mathf.Abs(scatter.y);

            DamagePopup popup = new DamagePopup
            {
                WorldPos = hitPoint + Vector3.up * 0.2f + scatter,
                Text = isHeadshot ? $"CRIT {damage:F0}" : $"-{damage:F0}",
                IsHeadshot = isHeadshot,
                RemainingTime = damageNumberDuration,
                MaxDuration = damageNumberDuration,
                Velocity = Vector3.up * floatUpSpeed + Camera.main.transform.right * Random.Range(-0.25f, 0.25f)
            };

            _activePopups.Add(popup);
        }

        private void HandleDummyEliminated(string dummyName, bool wasHeadshot)
        {
            // 1. Multi-Kill Streak Calculation
            if (Time.time - _lastKillTime <= KillStreakWindow)
            {
                _killStreakCount++;
            }
            else
            {
                _killStreakCount = 1;
            }
            _lastKillTime = Time.time;

            string streakText = "TARGET ELIMINATED";
            float streakPitch = 1.0f;

            switch (_killStreakCount)
            {
                case 1:
                    streakText = wasHeadshot ? "HEADSHOT ELIMINATION" : "TARGET ELIMINATED";
                    streakPitch = wasHeadshot ? 1.08f : 1.0f;
                    break;
                case 2:
                    streakText = wasHeadshot ? "DOUBLE KILL [HEADSHOT]" : "DOUBLE KILL";
                    streakPitch = 1.26f;
                    break;
                case 3:
                    streakText = wasHeadshot ? "TRIPLE KILL [HEADSHOT]" : "TRIPLE KILL";
                    streakPitch = 1.50f;
                    break;
                default:
                    streakText = $"MULTI KILL x{_killStreakCount}";
                    streakPitch = Mathf.Min(2.0f, 1.5f + (_killStreakCount - 3) * 0.15f);
                    break;
            }

            if (enableEliminationBanner)
            {
                _bannerIsHeadshot = wasHeadshot;
                _bannerText = streakText;
                _bannerTimer = bannerDuration;
            }

            // Play elimination audio chime dengan progresi chord CS2 / Valorant
            if (_procKillChime != null && _audioSource != null)
            {
                _audioSource.pitch = streakPitch;
                _audioSource.PlayOneShot(_procKillChime, 0.9f);
            }

            // 2. Tambahkan ke Tactical Esports Killfeed (Pojok Kanan Atas)
            string cleanVictim = dummyName.Replace("Enemy_", "").Replace("Target_", "").Replace("(Clone)", "").Replace("_", " ").Trim().ToUpper();
            if (string.IsNullOrEmpty(cleanVictim)) cleanVictim = "ENEMY";

            string currentWeapon = weapon != null ? weapon.WeaponName.ToUpper() : "SMG-K TACTICAL";

            KillfeedEntry entry = new KillfeedEntry
            {
                Attacker = "PLAYER",
                Weapon = currentWeapon,
                Victim = cleanVictim,
                WasHeadshot = wasHeadshot,
                RemainingTime = KillfeedDuration,
                SlideTimer = 0.22f
            };

            _activeKillfeed.Add(entry);
            if (_activeKillfeed.Count > MaxKillfeedEntries)
            {
                _activeKillfeed.RemoveAt(0);
            }
        }

        private void OnGUI()
        {
            InitStyles();

            // 0. Red Screen Damage Vignette Flash saat tertembak
            if (_damageFlashTimer > 0f)
            {
                DrawDamageFlashVignette();
            }

            // 1. Gambar Floating Damage Numbers di dunia 3D
            if (enableDamageNumbers)
            {
                DrawFloatingDamageNumbers();
            }

            // 2. Gambar Crosshair & Follow Recoil & Hitmarker
            DrawCrosshairAndHitmarker();

            // 3. Gambar HUD Pojok Kiri Bawah (Health)
            DrawHealthWidget();

            // 4. Gambar HUD Pojok Kanan Bawah (Weapon Slots & Ammo)
            DrawWeaponSlotsWidget();
            DrawAmmoWidget();

            // 4b. Gambar Defusal Meter / Prompt jika ada bom aktif
            DrawBombDefusalWidget();

            // 5. Gambar Elimination Banner
            if (_bannerTimer > 0f)
            {
                DrawEliminationBanner();
            }

            // 6. Gambar Debug Telemetry jika aktif (F3)
            if (showDebugTelemetry)
            {
                DrawDebugTelemetry();
            }

            // 7. Gambar Tactical Esports Killfeed di Pojok Kanan Atas
            DrawTacticalKillfeed();

            // 8. Gambar Top-Center Esports Scoreboard (First to 3 Wins)
            DrawTopCenterScoreboard();

            // 9. Gambar Round State Transition Banner
            DrawRoundStateBanner();
        }

        #region Render Subsystems

        private void DrawFloatingDamageNumbers()
        {
            Camera cam = Camera.main;
            if (cam == null) return;

            foreach (var popup in _activePopups)
            {
                Vector3 screenPoint = cam.WorldToScreenPoint(popup.WorldPos);
                if (screenPoint.z <= 0.4f) continue; // Di belakang kamera

                // Konversi koordinat layar ke GUI (0,0 di kiri atas)
                float screenX = screenPoint.x;
                float screenY = Screen.height - screenPoint.y;

                float progress = 1f - (popup.RemainingTime / popup.MaxDuration);
                float alpha = popup.RemainingTime < 0.3f ? (popup.RemainingTime / 0.3f) : 1f;

                Color col = popup.IsHeadshot ? headDamageColor : bodyDamageColor;
                col.a = alpha;

                // Scale punch saat awal muncul
                int fontSize = popup.IsHeadshot ? 20 : 16;
                if (progress < 0.2f)
                {
                    float punch = Mathf.Lerp(1.3f, 1.0f, progress / 0.2f);
                    fontSize = Mathf.RoundToInt(fontSize * punch);
                }

                _damageTextStyle.fontSize = fontSize;
                _damageTextStyle.fontStyle = popup.IsHeadshot ? FontStyle.Bold : FontStyle.Normal;

                Vector2 size = _damageTextStyle.CalcSize(new GUIContent(popup.Text));
                Rect rect = new Rect(screenX - size.x / 2f, screenY - size.y / 2f, size.x, size.y);

                DrawOutlineText(rect, popup.Text, _damageTextStyle, col, new Color(0f, 0f, 0f, alpha * 0.8f), 1.5f);
            }
        }

        private void DrawHealthWidget()
        {
            float w = 220f;
            float h = 64f;
            float x = 30f;
            float y = Screen.height - h - 30f;

            float currentHp = 100f;
            float maxHp = 100f;

            if (_playerHealth != null)
            {
                currentHp = _playerHealth.CurrentHealth;
                maxHp = _playerHealth.MaxHealth;
            }

            float hpRatio = Mathf.Clamp01(currentHp / Mathf.Max(1f, maxHp));

            Color accentCol = hpRatio > 0.5f ? new Color(0.2f, 0.75f, 0.45f, 1f) :
                              (hpRatio > 0.25f ? new Color(0.9f, 0.75f, 0.2f, 1f) : new Color(0.95f, 0.25f, 0.25f, 1f));

            // Background panel
            DrawSolidRect(new Rect(x, y, w, h), new Color(0.08f, 0.09f, 0.11f, 0.85f));
            DrawBorder(new Rect(x, y, w, h), new Color(0.25f, 0.28f, 0.32f, 0.6f), 1f);

            // Left accent bar
            DrawSolidRect(new Rect(x, y, 4f, h), accentCol);

            // Health Title & Value
            _hudTitleStyle.normal.textColor = new Color(0.7f, 0.75f, 0.8f);
            GUI.Label(new Rect(x + 14f, y + 8f, 80f, 20f), "HEALTH", _hudTitleStyle);

            _hudBigNumStyle.normal.textColor = accentCol;
            GUI.Label(new Rect(x + 14f, y + 24f, 90f, 32f), $"{Mathf.CeilToInt(currentHp)}", _hudBigNumStyle);

            // Horizontal Health Bar Fill
            float barX = x + 105f;
            float barY = y + 36f;
            float barW = 100f;
            float barH = 10f;

            DrawSolidRect(new Rect(barX, barY, barW, barH), new Color(0.18f, 0.2f, 0.24f, 1f));
            DrawSolidRect(new Rect(barX, barY, barW * hpRatio, barH), accentCol);
        }

        private void DrawDamageFlashVignette()
        {
            float alpha = Mathf.Clamp01(_damageFlashTimer / 0.35f) * 0.45f;
            Color flashCol = new Color(0.9f, 0.1f, 0.1f, alpha);

            float borderThick = 24f;
            // Top border
            DrawSolidRect(new Rect(0, 0, Screen.width, borderThick), flashCol);
            // Bottom border
            DrawSolidRect(new Rect(0, Screen.height - borderThick, Screen.width, borderThick), flashCol);
            // Left border
            DrawSolidRect(new Rect(0, 0, borderThick, Screen.height), flashCol);
            // Right border
            DrawSolidRect(new Rect(Screen.width - borderThick, 0, borderThick, Screen.height), flashCol);
        }

        private void DrawWeaponSlotsWidget()
        {
            if (FPSWeaponManager.Instance == null) return;

            FPSWeaponManager wm = FPSWeaponManager.Instance;
            float slotW = 54f;
            float slotH = 22f;
            float spacing = 6f;

            // Jika membawa C4 (T side), tampilkan 5 slot; untuk CT default 4 slot
            bool showC4 = wm.HasC4;
            int count = showC4 ? 5 : 4;
            float totalW = (slotW * count) + (spacing * (count - 1));
            float startX = Screen.width - totalW - 30f;
            float startY = Screen.height - 64f - 30f - slotH - 8f;

            string[] slotLabels = showC4
                ? new string[] { "1 SMG", "2 PSTL", "3 KNIF", "4 NADE", "5 C4" }
                : new string[] { "1 SMG", "2 PSTL", "3 KNIF", "4 NADE" };

            WeaponSlot[] slots = showC4
                ? new WeaponSlot[] { WeaponSlot.Primary, WeaponSlot.Secondary, WeaponSlot.Melee, WeaponSlot.Grenade, WeaponSlot.C4 }
                : new WeaponSlot[] { WeaponSlot.Primary, WeaponSlot.Secondary, WeaponSlot.Melee, WeaponSlot.Grenade };

            for (int i = 0; i < count; i++)
            {
                bool isActive = wm.ActiveSlot == slots[i];
                float x = startX + i * (slotW + spacing);
                Rect slotRect = new Rect(x, startY, slotW, slotH);

                bool isOutOfGrenades = (slots[i] == WeaponSlot.Grenade && wm.FragGrenades <= 0);

                Color bgColor = isActive ? new Color(0.12f, 0.22f, 0.28f, 0.95f) : new Color(0.06f, 0.07f, 0.09f, 0.75f);
                Color borderColor = isActive ? new Color(0.0f, 0.95f, 1.0f, 0.9f) : new Color(0.25f, 0.3f, 0.35f, 0.45f);
                Color txtColor = isActive ? Color.white : new Color(0.6f, 0.65f, 0.7f, 0.8f);

                if (isOutOfGrenades && !isActive)
                {
                    txtColor = new Color(0.38f, 0.42f, 0.45f, 0.5f);
                    borderColor = new Color(0.18f, 0.2f, 0.22f, 0.35f);
                }

                DrawSolidRect(slotRect, bgColor);
                DrawBorder(slotRect, borderColor, isActive ? 1.5f : 1.0f);

                _hudTitleStyle.normal.textColor = txtColor;
                _hudTitleStyle.alignment = TextAnchor.MiddleCenter;
                GUI.Label(slotRect, slotLabels[i], _hudTitleStyle);
                _hudTitleStyle.alignment = TextAnchor.MiddleLeft;
            }
        }

        private void DrawAmmoWidget()
        {
            FPSWeaponManager wm = FPSWeaponManager.Instance;
            if (wm != null)
            {
                if (wm.ActiveSlot == WeaponSlot.Melee)
                {
                    float kw = 240f;
                    float kh = 64f;
                    float kx = Screen.width - kw - 30f;
                    float ky = Screen.height - kh - 30f;

                    DrawSolidRect(new Rect(kx, ky, kw, kh), new Color(0.08f, 0.09f, 0.11f, 0.85f));
                    DrawBorder(new Rect(kx, ky, kw, kh), new Color(0.2f, 0.8f, 1.0f, 0.6f), 1f);
                    DrawSolidRect(new Rect(kx + kw - 4f, ky, 4f, kh), new Color(0.2f, 0.85f, 1.0f, 1f));

                    _hudTitleStyle.normal.textColor = new Color(0.3f, 0.85f, 1.0f);
                    GUI.Label(new Rect(kx + 14f, ky + 8f, 180f, 20f), "COMBAT TACTICAL KNIFE", _hudTitleStyle);

                    _hudBigNumStyle.normal.textColor = Color.white;
                    GUI.Label(new Rect(kx + 14f, ky + 24f, 85f, 34f), "MELEE", _hudBigNumStyle);

                    _hudSubStyle.normal.textColor = new Color(0.75f, 0.85f, 0.95f);
                    GUI.Label(new Rect(kx + 104f, ky + 33f, 130f, 24f), "[LMB] SLASH / [RMB] STAB", _hudSubStyle);
                    return;
                }

                if (wm.ActiveSlot == WeaponSlot.Grenade)
                {
                    float gw = 240f;
                    float gh = 64f;
                    float gx = Screen.width - gw - 30f;
                    float gy = Screen.height - gh - 30f;

                    DrawSolidRect(new Rect(gx, gy, gw, gh), new Color(0.08f, 0.09f, 0.11f, 0.85f));
                    DrawBorder(new Rect(gx, gy, gw, gh), new Color(0.95f, 0.65f, 0.15f, 0.6f), 1f);
                    DrawSolidRect(new Rect(gx + gw - 4f, gy, 4f, gh), new Color(0.95f, 0.65f, 0.15f, 1f));

                    _hudTitleStyle.normal.textColor = new Color(0.95f, 0.75f, 0.2f);
                    GUI.Label(new Rect(gx + 14f, gy + 8f, 180f, 20f), "TACTICAL FRAG GRENADE", _hudTitleStyle);

                    int nades = wm.FragGrenades;
                    _hudBigNumStyle.normal.textColor = nades > 0 ? Color.white : new Color(1f, 0.3f, 0.3f);
                    GUI.Label(new Rect(gx + 14f, gy + 24f, 40f, 34f), $"{nades}", _hudBigNumStyle);

                    _hudSubStyle.normal.textColor = new Color(0.6f, 0.65f, 0.7f);
                    GUI.Label(new Rect(gx + 54f, gy + 33f, 40f, 24f), "/ 1", _hudSubStyle);

                    _hudSubStyle.normal.textColor = nades > 0 ? new Color(0.85f, 0.85f, 0.85f) : new Color(1f, 0.35f, 0.35f);
                    GUI.Label(new Rect(gx + 95f, gy + 33f, 135f, 24f), nades > 0 ? "[LMB/RMB] THROW" : "OUT OF GRENADES", _hudSubStyle);
                    return;
                }

                if (wm.ActiveSlot == WeaponSlot.C4)
                {
                    float cw = 240f;
                    float ch = 64f;
                    float cx = Screen.width - cw - 30f;
                    float cy = Screen.height - ch - 30f;

                    DrawSolidRect(new Rect(cx, cy, cw, ch), new Color(0.08f, 0.09f, 0.11f, 0.85f));
                    DrawBorder(new Rect(cx, cy, cw, ch), new Color(1.0f, 0.3f, 0.25f, 0.6f), 1f);
                    DrawSolidRect(new Rect(cx + cw - 4f, cy, 4f, ch), new Color(1.0f, 0.25f, 0.25f, 1f));

                    _hudTitleStyle.normal.textColor = new Color(1.0f, 0.35f, 0.35f);
                    GUI.Label(new Rect(cx + 14f, cy + 8f, 180f, 20f), "C4 EXPLOSIVE DYNAMITE", _hudTitleStyle);

                    _hudBigNumStyle.normal.textColor = Color.white;
                    GUI.Label(new Rect(cx + 14f, cy + 24f, 40f, 34f), "1", _hudBigNumStyle);

                    _hudSubStyle.normal.textColor = new Color(1.0f, 0.75f, 0.2f);
                    GUI.Label(new Rect(cx + 60f, cy + 33f, 170f, 24f), "HOLD [LMB] IN SITE", _hudSubStyle);
                    return;
                }
            }

            FPSWeapon curWeapon = (wm != null && wm.ActiveGun != null) ? wm.ActiveGun : weapon;
            if (curWeapon == null) return;

            float w = 240f;
            float h = 64f;
            float x = Screen.width - w - 30f;
            float y = Screen.height - h - 30f;

            // Background panel
            DrawSolidRect(new Rect(x, y, w, h), new Color(0.08f, 0.09f, 0.11f, 0.85f));
            DrawBorder(new Rect(x, y, w, h), new Color(0.25f, 0.28f, 0.32f, 0.6f), 1f);

            // Right accent bar
            DrawSolidRect(new Rect(x + w - 4f, y, 4f, h), new Color(0.25f, 0.65f, 1f, 1f));

            // Weapon Name
            _hudTitleStyle.normal.textColor = new Color(0.7f, 0.75f, 0.8f);
            GUI.Label(new Rect(x + 14f, y + 8f, 180f, 20f), curWeapon.WeaponName.ToUpper(), _hudTitleStyle);

            if (curWeapon.IsReloading)
            {
                // Reload Status & Progress Fill
                _hudTitleStyle.normal.textColor = new Color(1f, 0.8f, 0.2f);
                GUI.Label(new Rect(x + 14f, y + 26f, 120f, 20f), "RELOADING", _hudTitleStyle);

                float barX = x + 14f;
                float barY = y + 46f;
                float barW = 180f;
                float barH = 6f;

                DrawSolidRect(new Rect(barX, barY, barW, barH), new Color(0.2f, 0.22f, 0.26f, 1f));
                DrawSolidRect(new Rect(barX, barY, barW * curWeapon.ReloadProgress, barH), new Color(1f, 0.8f, 0.2f, 1f));
            }
            else
            {
                // Current Ammo Number
                bool isLowAmmo = curWeapon.CurrentAmmo <= 7;
                Color ammoColor = isLowAmmo ? new Color(1f, 0.3f, 0.3f) : Color.white;

                if (isLowAmmo && Mathf.PingPong(Time.time * 4f, 1f) > 0.5f)
                {
                    ammoColor = new Color(1f, 0.7f, 0.7f);
                }

                _hudBigNumStyle.normal.textColor = ammoColor;
                GUI.Label(new Rect(x + 14f, y + 24f, 60f, 34f), $"{curWeapon.CurrentAmmo}", _hudBigNumStyle);

                // Reserve Ammo
                _hudSubStyle.normal.textColor = new Color(0.6f, 0.65f, 0.7f);
                GUI.Label(new Rect(x + 76f, y + 33f, 70f, 24f), $"/ {curWeapon.ReserveAmmo}", _hudSubStyle);

                // Low Ammo Prompt
                if (isLowAmmo && curWeapon.CurrentAmmo > 0)
                {
                    _hudSubStyle.normal.textColor = new Color(1f, 0.35f, 0.35f);
                    GUI.Label(new Rect(x + 140f, y + 33f, 85f, 24f), "RELOAD [R]", _hudSubStyle);
                }
                else if (curWeapon.CurrentAmmo == 0)
                {
                    _hudSubStyle.normal.textColor = new Color(1f, 0.25f, 0.25f);
                    GUI.Label(new Rect(x + 140f, y + 33f, 85f, 24f), "EMPTY [R]", _hudSubStyle);
                }
            }
        }

        private void DrawBombDefusalWidget()
        {
            if (PlantedC4.ActiveC4 == null)
            {
                // Cek jika ada bom C4 yang terjatuh di dekat pemain (Defend Dropped Bomb reminder)
                GameObject dropped = GameObject.Find("Dropped_C4_Dynamite");
                if (dropped != null)
                {
                    Camera c = Camera.main;
                    if (c != null && Vector3.Distance(c.transform.position, dropped.transform.position) <= 12f)
                    {
                        float pw = 300f;
                        float ph = 28f;
                        float px = (Screen.width - pw) * 0.5f;
                        float py = Screen.height * 0.65f;

                        DrawSolidRect(new Rect(px, py, pw, ph), new Color(0.15f, 0.05f, 0.05f, 0.85f));
                        DrawBorder(new Rect(px, py, pw, ph), new Color(1.0f, 0.3f, 0.3f, 0.8f), 1.0f);

                        _hudTitleStyle.normal.textColor = new Color(1.0f, 0.4f, 0.4f);
                        _hudTitleStyle.alignment = TextAnchor.MiddleCenter;
                        GUI.Label(new Rect(px, py, pw, ph), "[!] BOMB DROPPED - GUARD THE C4", _hudTitleStyle);
                        _hudTitleStyle.alignment = TextAnchor.MiddleLeft;
                    }
                }
                return;
            }

            PlantedC4 c4 = PlantedC4.ActiveC4;
            Camera cam = Camera.main;
            if (cam == null) return;

            float dist = Vector3.Distance(cam.transform.position, c4.transform.position);

            if (c4.IsBeingDefused)
            {
                // Banner Progress Defuse di tengah layar
                float bw = 320f;
                float bh = 54f;
                float bx = (Screen.width - bw) * 0.5f;
                float by = Screen.height * 0.62f;

                DrawSolidRect(new Rect(bx, by, bw, bh), new Color(0.04f, 0.08f, 0.12f, 0.92f));
                DrawBorder(new Rect(bx, by, bw, bh), new Color(0.0f, 0.95f, 1.0f, 0.85f), 1.5f);

                _hudTitleStyle.normal.textColor = new Color(0.0f, 0.95f, 1.0f);
                float progressRatio = Mathf.Clamp01(c4.CurrentDefuseProgress / 5.0f);
                float timeLeft = Mathf.Max(0f, 5.0f - c4.CurrentDefuseProgress);

                GUI.Label(new Rect(bx + 14f, by + 6f, 200f, 18f), "DEFUSING BOMB [E] ...", _hudTitleStyle);
                _hudSubStyle.normal.textColor = Color.white;
                GUI.Label(new Rect(bx + bw - 60f, by + 6f, 50f, 18f), $"{timeLeft:F1}s", _hudSubStyle);

                // Progress Bar
                float barX = bx + 14f;
                float barY = by + 28f;
                float barW = bw - 28f;
                float barH = 14f;

                DrawSolidRect(new Rect(barX, barY, barW, barH), new Color(0.12f, 0.18f, 0.24f, 1f));
                DrawSolidRect(new Rect(barX, barY, barW * progressRatio, barH), new Color(0.0f, 0.95f, 1.0f, 1f));
            }
            else if (dist <= 2.8f)
            {
                // Prompt Defuse jika pemain berada di dekat bom
                float pw = 240f;
                float ph = 32f;
                float px = (Screen.width - pw) * 0.5f;
                float py = Screen.height * 0.62f;

                DrawSolidRect(new Rect(px, py, pw, ph), new Color(0.06f, 0.08f, 0.1f, 0.88f));
                DrawBorder(new Rect(px, py, pw, ph), new Color(0.0f, 0.85f, 1.0f, 0.7f), 1.2f);

                _hudTitleStyle.normal.textColor = new Color(0.0f, 0.95f, 1.0f);
                _hudTitleStyle.alignment = TextAnchor.MiddleCenter;
                GUI.Label(new Rect(px, py, pw, ph), "[ HOLD E TO DEFUSE BOMB ]", _hudTitleStyle);
                _hudTitleStyle.alignment = TextAnchor.MiddleLeft;
            }
        }

        private void DrawEliminationBanner()
        {
            float alpha = Mathf.Clamp01(_bannerTimer / 0.5f);
            float w = 340f;
            float h = 42f;
            float x = (Screen.width - w) / 2f;
            float y = 50f;

            Color bgColor = new Color(0.08f, 0.09f, 0.12f, 0.9f * alpha);
            Color borderColor = _bannerIsHeadshot 
                ? new Color(1.0f, 0.25f, 0.25f, 0.9f * alpha) 
                : new Color(0.95f, 0.75f, 0.2f, 0.9f * alpha);

            DrawSolidRect(new Rect(x, y, w, h), bgColor);
            DrawBorder(new Rect(x, y, w, h), borderColor, 1.5f);

            _bannerStyle.normal.textColor = borderColor;
            GUI.Label(new Rect(x, y, w, h), $"[ {_bannerText} ]", _bannerStyle);
        }

        private void DrawCrosshairAndHitmarker()
        {
            FPSWeapon curWeapon = (FPSWeaponManager.Instance != null && FPSWeaponManager.Instance.ActiveGun != null)
                ? FPSWeaponManager.Instance.ActiveGun
                : weapon;

            float inaccuracy = movementController != null ? movementController.InaccuracyModifier : 0f;
            float totalSpread = inaccuracy;

            if (curWeapon != null)
            {
                totalSpread += (curWeapon.CurrentSpread * 15f) + (curWeapon.BurstBulletIndex * 0.035f);
            }

            float baseGap = 6f;
            float targetGap = baseGap + (totalSpread * 22f);
            _smoothCrosshairGap = Mathf.Lerp(_smoothCrosshairGap, targetGap, Time.deltaTime * 22f);
            float gap = _smoothCrosshairGap;
            float length = 10f;
            float thickness = 2f;

            Vector2 targetCrosshairPos = new Vector2(Screen.width / 2f, Screen.height / 2f);

            if (followRecoil && curWeapon != null)
            {
                Camera cam = Camera.main;
                if (cam != null)
                {
                    Vector3 worldAimPoint = curWeapon.GetCurrentRecoilAimPoint(30f);
                    Vector3 screenPoint = cam.WorldToScreenPoint(worldAimPoint);

                    if (screenPoint.z > 0f)
                    {
                        targetCrosshairPos = new Vector2(screenPoint.x, Screen.height - screenPoint.y);
                    }
                }
            }

            if (!_isCrosshairInitialized)
            {
                _smoothCrosshairScreenPos = targetCrosshairPos;
                _isCrosshairInitialized = true;
            }
            else
            {
                _smoothCrosshairScreenPos = Vector2.Lerp(_smoothCrosshairScreenPos, targetCrosshairPos, Time.deltaTime * followRecoilSnappiness);
            }

            float cx = _smoothCrosshairScreenPos.x;
            float cy = _smoothCrosshairScreenPos.y;

            Color curColor = totalSpread < 0.1f ? crosshairColor : (totalSpread < 0.5f ? Color.yellow : Color.red);
            GUI.color = curColor;

            // 4 lines
            GUI.DrawTexture(new Rect(cx - thickness / 2f, cy - gap - length, thickness, length), _pixelTex);
            GUI.DrawTexture(new Rect(cx - thickness / 2f, cy + gap, thickness, length), _pixelTex);
            GUI.DrawTexture(new Rect(cx - gap - length, cy - thickness / 2f, length, thickness), _pixelTex);
            GUI.DrawTexture(new Rect(cx + gap, cy - thickness / 2f, length, thickness), _pixelTex);

            // Center tiny dot
            float dotSize = 2f;
            GUI.DrawTexture(new Rect(cx - dotSize / 2f, cy - dotSize / 2f, dotSize, dotSize), _pixelTex);

            // Draw Hitmarker "X"
            DrawHitmarker(cx, cy);

            GUI.color = Color.white;
        }

        private void DrawHitmarker(float cx, float cy)
        {
            if (_hitmarkerTimer <= 0f) return;

            float alpha = Mathf.Clamp01(_hitmarkerTimer / 0.18f);
            Color hitColor = _hitmarkerIsHeadshot ? new Color(1f, 0.25f, 0.25f, alpha) : new Color(1f, 1f, 1f, alpha);
            GUI.color = hitColor;

            float hitGap = _hitmarkerIsHeadshot ? 7f : 5f;
            float hitLength = _hitmarkerIsHeadshot ? 9f : 6f;
            float thickness = 2.5f;

            Matrix4x4 matrixBackup = GUI.matrix;
            GUIUtility.RotateAroundPivot(45f, new Vector2(cx, cy));

            GUI.DrawTexture(new Rect(cx - thickness / 2f, cy - hitGap - hitLength, thickness, hitLength), _pixelTex);
            GUI.DrawTexture(new Rect(cx - thickness / 2f, cy + hitGap, thickness, hitLength), _pixelTex);
            GUI.DrawTexture(new Rect(cx - hitGap - hitLength, cy - thickness / 2f, hitLength, thickness), _pixelTex);
            GUI.DrawTexture(new Rect(cx + hitGap, cy - thickness / 2f, hitLength, thickness), _pixelTex);

            GUI.matrix = matrixBackup;
        }

        private void DrawDebugTelemetry()
        {
            if (movementController == null) return;

            Rect boxRect = new Rect(25f, 25f, 280f, 180f);
            DrawSolidRect(boxRect, new Color(0.05f, 0.05f, 0.07f, 0.88f));
            DrawBorder(boxRect, new Color(0.3f, 0.35f, 0.4f, 0.8f), 1f);

            GUILayout.BeginArea(new Rect(35f, 35f, 260f, 160f));
            GUILayout.Label("PHYSICS TELEMETRY [F3 to Hide]", _hudTitleStyle);
            GUILayout.Space(4);

            float speed = movementController.CurrentHorizontalSpeed;
            GUILayout.Label($"Speed: {speed:F2} m/s (Max: {movementController.TargetMaxSpeed:F1})", _debugStyle);
            GUILayout.Label($"Grounded: {(movementController.IsGrounded ? "YES" : "NO")}", _debugStyle);
            GUILayout.Label($"Crouching: {(movementController.IsCrouching ? "YES" : "NO")}", _debugStyle);
            GUILayout.Label($"Walking (Shift): {(movementController.IsWalking ? "YES" : "NO")}", _debugStyle);
            GUILayout.Label($"Inaccuracy Mod: {movementController.InaccuracyModifier:F2}x", _debugStyle);

            GUILayout.EndArea();
        }

        private void DrawTacticalKillfeed()
        {
            if (_activeKillfeed.Count == 0) return;

            float w = 280f;
            float h = 26f;
            float margin = 20f;
            float baseX = Screen.width - w - margin;
            float startY = margin;

            for (int i = 0; i < _activeKillfeed.Count; i++)
            {
                KillfeedEntry entry = _activeKillfeed[i];
                float y = startY + (i * 32f);

                // Slide-in animasi dari kanan
                float slideOffset = 0f;
                if (entry.SlideTimer > 0f)
                {
                    slideOffset = (entry.SlideTimer / 0.22f) * 60f;
                }
                float x = baseX + slideOffset;

                // Alpha fade out saat sisa waktu < 0.6 detik
                float alpha = 1f;
                if (entry.RemainingTime < 0.6f)
                {
                    alpha = Mathf.Clamp01(entry.RemainingTime / 0.6f);
                }

                Rect cardRect = new Rect(x, y, w, h);

                // Solid dark background
                DrawSolidRect(cardRect, new Color(0.07f, 0.08f, 0.11f, 0.88f * alpha));
                DrawBorder(cardRect, new Color(0.22f, 0.25f, 0.30f, 0.65f * alpha), 1f);

                // Left accent bar
                DrawSolidRect(new Rect(x, y, 3.5f, h), new Color(0.25f, 0.85f, 0.95f, alpha));

                // Attacker (PLAYER)
                _hudTitleStyle.normal.textColor = new Color(0.35f, 0.90f, 0.98f, alpha);
                GUI.Label(new Rect(x + 10f, y + 4f, 65f, 18f), entry.Attacker, _hudTitleStyle);

                // Weapon badge
                float weaponX = x + 75f;
                _hudTitleStyle.normal.textColor = new Color(0.85f, 0.88f, 0.92f, 0.85f * alpha);
                string weaponTag = $"[{entry.Weapon}]";
                GUI.Label(new Rect(weaponX, y + 4f, 90f, 18f), weaponTag, _hudTitleStyle);

                // Headshot badge jika headshot
                float victimX = weaponX + 90f;
                if (entry.WasHeadshot)
                {
                    _hudTitleStyle.normal.textColor = new Color(1.0f, 0.25f, 0.25f, alpha);
                    GUI.Label(new Rect(victimX, y + 4f, 32f, 18f), "[HS]", _hudTitleStyle);
                    victimX += 34f;
                }

                // Victim
                _hudTitleStyle.normal.textColor = new Color(1.0f, 0.55f, 0.55f, alpha);
                GUI.Label(new Rect(victimX, y + 4f, 75f, 18f), entry.Victim, _hudTitleStyle);
            }
        }

        private void DrawTopCenterScoreboard()
        {
            TacticalRoundManager rm = TacticalRoundManager.Instance;
            if (rm == null) return;

            float panelWidth = 370f;
            float panelHeight = 46f;
            float panelX = (Screen.width - panelWidth) * 0.5f;
            float panelY = 16f;

            // Background panel glassmorphic
            Rect panelRect = new Rect(panelX, panelY, panelWidth, panelHeight);
            DrawSolidRect(panelRect, new Color(0.04f, 0.06f, 0.09f, 0.88f));
            DrawBorder(panelRect, new Color(0.0f, 0.85f, 1.0f, 0.35f), 1.2f);

            // Garis pembatas bawah panel
            DrawSolidRect(new Rect(panelX, panelY + panelHeight - 2f, panelWidth, 2f), new Color(0.0f, 0.85f, 1.0f, 0.7f));

            // SISI KIRI: DEFENDERS (PLAYER CT)
            float leftSectionX = panelX + 10f;
            _scoreboardTeamStyle.normal.textColor = new Color(0.0f, 0.95f, 1.0f, 1.0f);
            GUI.Label(new Rect(leftSectionX, panelY + 6f, 66f, 16f), "CT (YOU)", _scoreboardTeamStyle);

            _scoreboardScoreStyle.normal.textColor = Color.white;
            GUI.Label(new Rect(leftSectionX + 66f, panelY + 8f, 24f, 26f), rm.PlayerScore.ToString(), _scoreboardScoreStyle);

            // 3 Kotak Pip Kemenangan Player
            float pipX = leftSectionX + 96f;
            float pipY = panelY + 18f;
            for (int i = 0; i < 3; i++)
            {
                Rect pipRect = new Rect(pipX + i * 11f, pipY, 8f, 10f);
                if (i < rm.PlayerScore)
                {
                    DrawSolidRect(pipRect, new Color(0.0f, 0.95f, 1.0f, 1.0f));
                }
                else
                {
                    DrawSolidRect(pipRect, new Color(0.2f, 0.25f, 0.3f, 0.45f));
                    DrawBorder(pipRect, new Color(0.4f, 0.5f, 0.6f, 0.4f), 1.0f);
                }
            }

            // BAGIAN TENGAH: TIMER & RONDE
            float centerSectionX = panelX + 138f;
            float centerSectionW = 94f;

            // Format Timer: MM:SS
            float timeSec = Mathf.Max(0f, rm.RemainingTime);
            int minutes = Mathf.FloorToInt(timeSec / 60f);
            int seconds = Mathf.FloorToInt(timeSec % 60f);
            string timeStr = $"{minutes:00}:{seconds:00}";

            Color timerColor = Color.white;
            string roundLabel = rm.State == RoundState.FreezeTime ? "PREPARATION" : $"ROUND {rm.CurrentRound}";

            if (PlantedC4.ActiveC4 != null)
            {
                // C4 Sedang Berdetak Aktif
                timeStr = $"{PlantedC4.ActiveC4.RemainingTime:F1}s";
                bool blink = Mathf.PingPong(Time.time * 6f, 1f) > 0.4f;
                timerColor = blink ? new Color(1.0f, 0.2f, 0.2f, 1.0f) : new Color(1.0f, 0.85f, 0.2f, 1.0f);
                roundLabel = $"BOMB {PlantedC4.ActiveC4.SiteName.ToUpper()}";
            }
            else if (rm.State == RoundState.FreezeTime)
            {
                timerColor = new Color(0.0f, 0.95f, 1.0f, 1.0f);
                timeStr = $"00:0{Mathf.CeilToInt(timeSec)}";
            }
            else if (timeSec <= 15f)
            {
                timerColor = new Color(1.0f, 0.3f, 0.25f, 1.0f);
            }

            _scoreboardTimerStyle.normal.textColor = timerColor;
            GUI.Label(new Rect(centerSectionX, panelY + 4f, centerSectionW, 22f), timeStr, _scoreboardTimerStyle);

            _scoreboardSubStyle.normal.textColor = new Color(0.7f, 0.8f, 0.9f, 0.8f);
            GUI.Label(new Rect(centerSectionX, panelY + 26f, centerSectionW, 16f), roundLabel, _scoreboardSubStyle);

            // SISI KANAN: ATTACKERS / BOTS T TEAM
            float rightSectionX = panelX + panelWidth - 10f;

            // 3 Kotak Pip Kemenangan Attackers
            float rPipX = rightSectionX - 128f;
            for (int i = 0; i < 3; i++)
            {
                Rect pipRect = new Rect(rPipX + i * 11f, pipY, 8f, 10f);
                if (i < rm.BotScore)
                {
                    DrawSolidRect(pipRect, new Color(1.0f, 0.3f, 0.25f, 1.0f));
                }
                else
                {
                    DrawSolidRect(pipRect, new Color(0.2f, 0.25f, 0.3f, 0.45f));
                    DrawBorder(pipRect, new Color(0.4f, 0.5f, 0.6f, 0.4f), 1.0f);
                }
            }

            // Skor Attackers
            _scoreboardScoreStyle.normal.textColor = Color.white;
            GUI.Label(new Rect(rightSectionX - 92f, panelY + 8f, 24f, 26f), rm.BotScore.ToString(), _scoreboardScoreStyle);

            // Label Attackers
            _scoreboardTeamStyle.normal.textColor = new Color(1.0f, 0.35f, 0.25f, 1.0f);
            GUI.Label(new Rect(rightSectionX - 66f, panelY + 6f, 66f, 16f), "T (BOTS)", _scoreboardTeamStyle);
        }

        private void DrawRoundStateBanner()
        {
            TacticalRoundManager rm = TacticalRoundManager.Instance;
            if (rm == null) return;

            if (rm.State == RoundState.FreezeTime)
            {
                // Banner Persiapan 4 Detik
                float bannerW = 320f;
                float bannerH = 50f;
                float bannerX = (Screen.width - bannerW) * 0.5f;
                float bannerY = 74f;

                DrawSolidRect(new Rect(bannerX, bannerY, bannerW, bannerH), new Color(0.04f, 0.08f, 0.12f, 0.88f));
                DrawBorder(new Rect(bannerX, bannerY, bannerW, bannerH), new Color(0.0f, 0.85f, 1.0f, 0.6f), 1.2f);

                int sec = Mathf.CeilToInt(rm.RemainingTime);
                _roundBannerBigStyle.normal.textColor = new Color(0.0f, 0.95f, 1.0f, 1.0f);
                GUI.Label(new Rect(bannerX, bannerY + 6f, bannerW, 24f), $"ROUND {rm.CurrentRound} - PREPARE", _roundBannerBigStyle);

                _roundBannerSubStyle.normal.textColor = Color.white;
                GUI.Label(new Rect(bannerX, bannerY + 28f, bannerW, 18f), $"COMBAT STARTS IN {sec} SECONDS", _roundBannerSubStyle);
            }
            else if (rm.State == RoundState.RoundEnd)
            {
                // Banner Kemenangan / Kekalahan Ronde
                float bannerW = 440f;
                float bannerH = 58f;
                float bannerX = (Screen.width - bannerW) * 0.5f;
                float bannerY = Screen.height * 0.28f;

                bool playerWon = rm.DidPlayerWinLastRound;
                Color boxColor = playerWon ? new Color(0.02f, 0.12f, 0.08f, 0.92f) : new Color(0.14f, 0.03f, 0.03f, 0.92f);
                Color borderColor = playerWon ? new Color(0.0f, 0.95f, 0.6f, 0.85f) : new Color(1.0f, 0.25f, 0.25f, 0.85f);
                Color textColor = playerWon ? new Color(0.2f, 1.0f, 0.7f, 1.0f) : new Color(1.0f, 0.35f, 0.35f, 1.0f);

                DrawSolidRect(new Rect(bannerX, bannerY, bannerW, bannerH), boxColor);
                DrawBorder(new Rect(bannerX, bannerY, bannerW, bannerH), borderColor, 1.5f);

                _roundBannerBigStyle.normal.textColor = textColor;
                GUI.Label(new Rect(bannerX, bannerY + 8f, bannerW, 26f), rm.LastRoundWinnerMessage, _roundBannerBigStyle);

                _roundBannerSubStyle.normal.textColor = Color.white;
                GUI.Label(new Rect(bannerX, bannerY + 34f, bannerW, 18f), $"SCORE: PLAYER {rm.PlayerScore} - {rm.BotScore} DEFENDERS", _roundBannerSubStyle);
            }
            else if (rm.State == RoundState.MatchOver)
            {
                // Banner Akhir Pertandingan (First to 3 Wins)
                float bannerW = 520f;
                float bannerH = 120f;
                float bannerX = (Screen.width - bannerW) * 0.5f;
                float bannerY = Screen.height * 0.35f;

                bool playerWonMatch = rm.PlayerScore >= 3;
                Color boxColor = playerWonMatch ? new Color(0.02f, 0.12f, 0.10f, 0.95f) : new Color(0.16f, 0.04f, 0.04f, 0.95f);
                Color borderColor = playerWonMatch ? new Color(0.0f, 0.95f, 1.0f, 0.9f) : new Color(1.0f, 0.25f, 0.25f, 0.9f);
                Color titleColor = playerWonMatch ? new Color(0.0f, 0.95f, 1.0f, 1.0f) : new Color(1.0f, 0.3f, 0.3f, 1.0f);

                DrawSolidRect(new Rect(bannerX, bannerY, bannerW, bannerH), boxColor);
                DrawBorder(new Rect(bannerX, bannerY, bannerW, bannerH), borderColor, 2.0f);

                _roundBannerBigStyle.normal.textColor = titleColor;
                string title = playerWonMatch ? "MATCH VICTORY" : "MATCH DEFEATED";
                GUI.Label(new Rect(bannerX, bannerY + 14f, bannerW, 34f), title, _roundBannerBigStyle);

                _roundBannerSubStyle.normal.textColor = Color.white;
                GUI.Label(new Rect(bannerX, bannerY + 54f, bannerW, 20f), $"FINAL SCORE: {rm.PlayerScore} - {rm.BotScore}", _roundBannerSubStyle);

                _roundBannerSubStyle.normal.textColor = new Color(0.0f, 0.85f, 1.0f, 0.9f);
                GUI.Label(new Rect(bannerX, bannerY + 82f, bannerW, 20f), "[ PRESS SPACE OR CLICK TO RESTART MATCH ]", _roundBannerSubStyle);
            }
        }

        #endregion

        #region Helper Drawing & Audio Generation

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
            // Top
            GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, thickness), _pixelTex);
            // Bottom
            GUI.DrawTexture(new Rect(rect.x, rect.y + rect.height - thickness, rect.width, thickness), _pixelTex);
            // Left
            GUI.DrawTexture(new Rect(rect.x, rect.y, thickness, rect.height), _pixelTex);
            // Right
            GUI.DrawTexture(new Rect(rect.x + rect.width - thickness, rect.y, thickness, rect.height), _pixelTex);
            GUI.color = old;
        }

        private void DrawOutlineText(Rect rect, string text, GUIStyle style, Color textColor, Color outlineColor, float offset)
        {
            style.normal.textColor = outlineColor;
            GUI.Label(new Rect(rect.x - offset, rect.y, rect.width, rect.height), text, style);
            GUI.Label(new Rect(rect.x + offset, rect.y, rect.width, rect.height), text, style);
            GUI.Label(new Rect(rect.x, rect.y - offset, rect.width, rect.height), text, style);
            GUI.Label(new Rect(rect.x, rect.y + offset, rect.width, rect.height), text, style);

            style.normal.textColor = textColor;
            GUI.Label(rect, text, style);
        }

        private void InitStyles()
        {
            if (_hudTitleStyle != null) return;

            _hudTitleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft
            };

            _hudBigNumStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 24,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft
            };

            _hudSubStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                fontStyle = FontStyle.Normal,
                alignment = TextAnchor.MiddleLeft
            };

            _damageTextStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 16,
                alignment = TextAnchor.MiddleCenter
            };

            _bannerStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };

            _debugStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                normal = { textColor = new Color(0.85f, 0.88f, 0.92f) }
            };

            _scoreboardTimerStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 18,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };

            _scoreboardTeamStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft
            };

            _scoreboardScoreStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 18,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };

            _scoreboardSubStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 10,
                fontStyle = FontStyle.Normal,
                alignment = TextAnchor.MiddleCenter
            };

            _roundBannerBigStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 20,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };

            _roundBannerSubStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
        }

        private void GenerateKillChimeAudio()
        {
            const int sampleRate = 44100;
            int sampleCount = Mathf.RoundToInt(sampleRate * 0.16f);
            float[] data = new float[sampleCount];

            for (int s = 0; s < sampleCount; s++)
            {
                float t = (float)s / sampleRate;
                float decay = Mathf.Exp(-24f * t);

                // Two-tone bell ping (660Hz harmonic)
                float tone1 = Mathf.Sin(2f * Mathf.PI * 660f * t);
                float tone2 = Mathf.Sin(2f * Mathf.PI * 1320f * t) * 0.4f;
                float fadeOut = Mathf.Clamp01((sampleCount - s) / 200f);

                data[s] = (tone1 + tone2) * decay * fadeOut * 0.5f;
            }

            _procKillChime = AudioClip.Create("Procedural_KillChime", sampleCount, 1, sampleRate, false);
            _procKillChime.SetData(data, 0);
        }

        private void GenerateHitAudio()
        {
            const int sampleRate = 44100;

            // 1. Procedural Headshot Ping (2200Hz metallic bell ping)
            int hsCount = Mathf.RoundToInt(sampleRate * 0.12f);
            float[] hsData = new float[hsCount];
            for (int s = 0; s < hsCount; s++)
            {
                float t = (float)s / sampleRate;
                float decay = Mathf.Exp(-32f * t);
                float tone1 = Mathf.Sin(2f * Mathf.PI * 2200f * t);
                float tone2 = Mathf.Sin(2f * Mathf.PI * 4400f * t) * 0.35f;
                hsData[s] = (tone1 + tone2) * decay * 0.45f;
            }
            _procHeadshotClip = AudioClip.Create("Procedural_HeadshotPing", hsCount, 1, sampleRate, false);
            _procHeadshotClip.SetData(hsData, 0);

            // 2. Procedural Bodyshot Thud (160Hz punchy flesh thud)
            int bsCount = Mathf.RoundToInt(sampleRate * 0.08f);
            float[] bsData = new float[bsCount];
            for (int s = 0; s < bsCount; s++)
            {
                float t = (float)s / sampleRate;
                float decay = Mathf.Exp(-45f * t);
                float tone = Mathf.Sin(2f * Mathf.PI * Mathf.Lerp(160f, 60f, t * 15f) * t);
                float noise = (Random.value * 2f - 1f) * 0.25f;
                bsData[s] = (tone * 0.7f + noise) * decay * 0.45f;
            }
            _procBodyshotClip = AudioClip.Create("Procedural_BodyshotThud", bsCount, 1, sampleRate, false);
            _procBodyshotClip.SetData(bsData, 0);
        }

        #endregion
    }
}
