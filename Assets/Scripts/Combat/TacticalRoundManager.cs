using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using FPSFramework.Player;

namespace FPSFramework.Combat
{
    public enum RoundState
    {
        FreezeTime,
        RoundActive,
        RoundEnd,
        MatchOver
    }

    /// <summary>
    /// Esports Round Management System (First to 3 Wins - CS2 / Valorant Style):
    /// - Format Pertandingan: Tim pertama yang mengumpulkan 3 kemenangan memenangkan pertandingan.
    /// - Lawan: 2 Bot Prajurit Humanoid (Bot A di Long A, Bot B di Mid Courtyard & Site B).
    /// - Siklus Ronde: FreezeTime (4s) -> RoundActive (105s) -> RoundEnd (3.5s) -> Auto-Reset bersih.
    /// - Sinkronisasi: Mengunci gerakan/tembakan saat FreezeTime, mereset HP, ammo, dan posisi tiap ronde.
    /// </summary>
    public class TacticalRoundManager : MonoBehaviour
    {
        public static TacticalRoundManager Instance { get; private set; }

        [Header("Format Turnamen")]
        [SerializeField] private int targetRoundWins = 3;
        [SerializeField] private float freezeTimeDuration = 4.0f; // detik
        [SerializeField] private float roundActiveDuration = 105.0f; // 1 menit 45 detik
        [SerializeField] private float roundEndDuration = 3.5f; // detik

        [Header("Titik Spawn Pemain & Bot")]
        [SerializeField] private Vector3 playerSpawnPos = new Vector3(0f, 1.0f, 66f);
        [SerializeField] private Vector3 botASpawnPos = new Vector3(4f, 0f, 2f);
        [SerializeField] private Vector3 botBSpawnPos = new Vector3(-4f, 0f, 2f);

        // State Pertandingan
        public RoundState State { get; private set; } = RoundState.FreezeTime;
        public int CurrentRound { get; private set; } = 1;
        public int PlayerScore { get; private set; } = 0;
        public int BotScore { get; private set; } = 0;
        public float RemainingTime { get; private set; } = 0f;
        public string LastRoundWinnerMessage { get; private set; } = "";
        public bool DidPlayerWinLastRound { get; private set; } = false;

        // Static Events
        public static event System.Action<RoundState> OnRoundStateChanged;
        public static event System.Action<int, int> OnScoreUpdated; // player, bot
        public static event System.Action<int> OnFreezeTimeCountdown; // detik tersisa

        // Entity references
        private PlayerHealth _playerHealth;
        private FPSMovementController _playerMovement;
        private FPSWeapon _playerWeapon;
        private readonly List<EnemyHumanoidAI> _activeBots = new List<EnemyHumanoidAI>();

        // Audio
        private AudioSource _audioSource;
        private AudioClip _beepShort;
        private AudioClip _beepHigh;
        private AudioClip _roundWinClip;
        private AudioClip _roundLoseClip;
        private int _lastCountdownSecond = -1;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            playerSpawnPos = new Vector3(0f, 1.0f, 66f);
            botASpawnPos = new Vector3(4f, 0f, 2f);
            botBSpawnPos = new Vector3(-4f, 0f, 2f);

            _audioSource = gameObject.AddComponent<AudioSource>();
            _audioSource.playOnAwake = false;

            GenerateProceduralSounds();
        }

        private bool _bombPlanted = false;
        private bool _bombDefused = false;
        private bool _bombExploded = false;

        private void HandleC4Planted(PlantedC4 c4, string site)
        {
            _bombPlanted = true;
        }

        private void HandleC4Defused(PlantedC4 c4)
        {
            _bombDefused = true;
        }

        private void HandleC4Exploded(PlantedC4 c4)
        {
            _bombExploded = true;
        }

        private void Start()
        {
            FindEntities();

            if (_playerHealth != null)
            {
                _playerHealth.AutoRespawn = false; // Ronde yang mengendalikan respawn
                _playerHealth.OnDied += HandlePlayerDied;
            }

            PlantedC4.OnAnyC4Planted += HandleC4Planted;
            PlantedC4.OnAnyC4Defused += HandleC4Defused;
            PlantedC4.OnAnyC4Exploded += HandleC4Exploded;

            StartCoroutine(MatchFlowRoutine());
        }

        private void OnDestroy()
        {
            if (_playerHealth != null)
            {
                _playerHealth.OnDied -= HandlePlayerDied;
            }

            PlantedC4.OnAnyC4Planted -= HandleC4Planted;
            PlantedC4.OnAnyC4Defused -= HandleC4Defused;
            PlantedC4.OnAnyC4Exploded -= HandleC4Exploded;
        }

        private void FindEntities()
        {
            if (_playerHealth == null) _playerHealth = FindAnyObjectByType<PlayerHealth>();
            if (_playerMovement == null) _playerMovement = FindAnyObjectByType<FPSMovementController>();
            if (_playerWeapon == null) _playerWeapon = FindAnyObjectByType<FPSWeapon>();

            // Pastikan kedua bot prajurit (Bot A & Bot B) dijamin ada di scene
            TacticalArenaBuilder.EnsureEnemyBotsExist();

            _activeBots.Clear();
            EnemyHumanoidAI[] bots = FindObjectsByType<EnemyHumanoidAI>();
            foreach (var b in bots)
            {
                if (b != null && !b.name.Contains("Dummy"))
                {
                    _activeBots.Add(b);
                }
            }
        }

        private IEnumerator MatchFlowRoutine()
        {
            while (PlayerScore < targetRoundWins && BotScore < targetRoundWins)
            {
                // Reset status bom dan bersihkan arena secara instan di awal FreezeTime
                _bombPlanted = false;
                _bombDefused = false;
                _bombExploded = false;
                PlantedC4.ClearAllPlantedC4();

                // 1. FASE FREEZETIME (4 Detik)
                State = RoundState.FreezeTime;
                RemainingTime = freezeTimeDuration;
                _lastCountdownSecond = -1;
                OnRoundStateChanged?.Invoke(State);

                // Pastikan referensi lengkap dan reset entitas
                ResetAllEntitiesForNewRound();
                LockControls(true);

                while (RemainingTime > 0f)
                {
                    int currentSec = Mathf.CeilToInt(RemainingTime);
                    if (currentSec != _lastCountdownSecond && currentSec > 0)
                    {
                        _lastCountdownSecond = currentSec;
                        OnFreezeTimeCountdown?.Invoke(currentSec);
                        PlayBeep(_beepShort, 0.6f);
                    }

                    RemainingTime -= Time.deltaTime;
                    yield return null;
                }

                // 2. FASE ROUND ACTIVE (105 Detik)
                State = RoundState.RoundActive;
                RemainingTime = roundActiveDuration;
                OnRoundStateChanged?.Invoke(State);
                PlayBeep(_beepHigh, 0.85f);
                LockControls(false);

                bool roundEnded = false;
                while (!roundEnded)
                {
                    // Jika bom belum ditanam, kurangi timer ronde standar (105s)
                    if (!_bombPlanted)
                    {
                        RemainingTime -= Time.deltaTime;
                    }
                    else
                    {
                        // Bom aktif: waktu ronde dikunci ke sisa waktu sekring bom C4
                        if (PlantedC4.ActiveC4 != null)
                        {
                            RemainingTime = PlantedC4.ActiveC4.RemainingTime;
                        }
                    }

                    // 1. Kondisi Bom Berhasil Dijinakkan (CT Menang Mutlak)
                    if (_bombDefused)
                    {
                        roundEnded = true;
                        HandleRoundVictory(true, "ROUND WON - BOMB DEFUSED");
                        break;
                    }

                    // 2. Kondisi Bom Meledak (T Menang Mutlak)
                    if (_bombExploded)
                    {
                        roundEnded = true;
                        HandleRoundVictory(false, "ROUND LOST - TARGET DESTROYED (C4 DETONATED)");
                        break;
                    }

                    // 3. Evaluasi Kematian Pemain CT
                    if (_playerHealth != null && _playerHealth.IsDead)
                    {
                        roundEnded = true;
                        HandleRoundVictory(false, "ROUND LOST - ELIMINATED");
                        break;
                    }

                    // 4. Evaluasi Eliminasi Bot Musuh T
                    if (AreAllBotsDead())
                    {
                        if (!_bombPlanted)
                        {
                            // Jika bom belum ditanam dan seluruh T tereliminasi -> CT Menang
                            roundEnded = true;
                            HandleRoundVictory(true, "ROUND WON - ALL ENEMIES ELIMINATED");
                            break;
                        }
                        // Jika bom SUDAH ditanam dan seluruh T tereliminasi, ronde BELUM selesai!
                        // Pemain CT masih harus menjinakkan bom sebelum meledak!
                    }

                    // 5. Evaluasi Waktu Ronde Habis (Hanya berlaku jika bom BELUM ditanam)
                    if (!_bombPlanted && RemainingTime <= 0f)
                    {
                        roundEnded = true;
                        HandleRoundVictory(true, "ROUND WON - TIME EXPIRED (SITE DEFENDED)");
                        break;
                    }

                    yield return null;
                }

                // 3. FASE ROUND END (3.5 Detik)
                State = RoundState.RoundEnd;
                RemainingTime = roundEndDuration;
                OnRoundStateChanged?.Invoke(State);
                LockControls(true);

                yield return new WaitForSeconds(roundEndDuration);

                // Cek Match Victory
                if (PlayerScore >= targetRoundWins || BotScore >= targetRoundWins)
                {
                    break;
                }

                CurrentRound++;
            }

            // 4. FASE MATCH OVER
            State = RoundState.MatchOver;
            OnRoundStateChanged?.Invoke(State);
            LockControls(true);
        }

        private void Update()
        {
            // Tombol restart match saat pertandingan selesai via New Input System
            if (State == RoundState.MatchOver)
            {
                bool restartTriggered = (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame) ||
                                       (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame);
                if (restartTriggered)
                {
                    RestartMatch();
                }
            }
        }

        public void RestartMatch()
        {
            StopAllCoroutines();
            PlayerScore = 0;
            BotScore = 0;
            CurrentRound = 1;
            OnScoreUpdated?.Invoke(PlayerScore, BotScore);
            StartCoroutine(MatchFlowRoutine());
        }

        private void HandlePlayerDied()
        {
            if (State == RoundState.RoundActive)
            {
                // Akan dievaluasi di loop MatchFlowRoutine
            }
        }

        private void HandleRoundVictory(bool playerWon, string message)
        {
            DidPlayerWinLastRound = playerWon;
            LastRoundWinnerMessage = message;

            if (playerWon)
            {
                PlayerScore++;
                PlaySound(_roundWinClip, 0.9f);
            }
            else
            {
                BotScore++;
                PlaySound(_roundLoseClip, 0.9f);
            }

            OnScoreUpdated?.Invoke(PlayerScore, BotScore);
        }

        private bool AreAllBotsDead()
        {
            if (_activeBots.Count == 0)
            {
                FindEntities();
                if (_activeBots.Count == 0) return false;
            }

            int deadCount = 0;
            foreach (var bot in _activeBots)
            {
                if (bot == null || bot.IsDead)
                {
                    deadCount++;
                }
            }
            return deadCount >= _activeBots.Count;
        }

        private void LockControls(bool locked)
        {
            if (_playerMovement != null) _playerMovement.MovementLocked = locked;
            if (FPSWeaponManager.Instance != null)
            {
                FPSWeaponManager.Instance.SetWeaponsLocked(locked);
            }
            else if (_playerWeapon != null)
            {
                _playerWeapon.FiringLocked = locked;
            }

            foreach (var bot in _activeBots)
            {
                if (bot != null) bot.IsFrozen = locked;
            }
        }

        private void ResetAllEntitiesForNewRound()
        {
            // 0. Bersihkan seluruh bom tertanam & terjatuh secara instan
            PlantedC4.ClearAllPlantedC4();
            GameObject[] allObjs = FindObjectsByType<GameObject>();
            foreach (var go in allObjs)
            {
                if (go != null && go.name.StartsWith("Dropped_C4_Dynamite")) Destroy(go);
            }

            // Clean Respawn Architecture: Hancurkan bot lama yang rusak/ragdoll dan spawn bot baru yang 100% segar
            TacticalArenaBuilder.EnsureEnemyBotsExist(forceRespawn: true);
            FindEntities();

            // Cek apakah pemain bertahan hidup di ronde sebelumnya (Save Round / Carry-over)
            bool playerSurvived = (_playerHealth != null && !_playerHealth.IsDead);

            // Reward Ekonomi Ronde dan buka durasi Buy Period (CS2 / Valorant style)
            if (FPSEconomyManager.Instance != null)
            {
                if (CurrentRound > 1)
                {
                    FPSEconomyManager.Instance.RewardRoundOutcome(DidPlayerWinLastRound);
                }
                FPSEconomyManager.Instance.StartNewRoundBuyPeriod();

                // Reset Defuse Kit jika pemain gugur di ronde sebelumnya
                if (!playerSurvived)
                {
                    FPSEconomyManager.Instance.ResetKitOnDeath();
                }
            }

            // 1. Reset Pemain CT di CT Spawn
            if (_playerHealth != null)
            {
                _playerHealth.ResetHealthForNewRound(playerSpawnPos, Quaternion.Euler(0f, 180f, 0f));
                _playerHealth.ResetArmorForNewRound(playerSurvived);
            }

            if (_playerWeapon != null)
            {
                _playerWeapon.ResetAmmoAndRecoil();
            }

            // Reset Loadout Pemain via WeaponManager
            if (FPSWeaponManager.Instance != null)
            {
                if (!playerSurvived)
                {
                    FPSWeaponManager.Instance.ResetRoundLoadout();
                }
                else
                {
                    // Pemain selamat (Save Round): amunisi diisi ulang, senjata & armor terbawa
                    if (FPSWeaponManager.Instance.ActiveGun != null)
                    {
                        FPSWeaponManager.Instance.ActiveGun.ResetAmmoAndRecoil();
                    }
                }
            }

            // 2. Bersihkan bekas lubang peluru di arena
            FPSWeapon.ClearAllBulletHoles();

            // 3. Kunci kedua bot baru selama fase FreezeTime
            foreach (var bot in _activeBots)
            {
                if (bot != null)
                {
                    bot.IsFrozen = true;
                }
            }
        }

        private void PlayBeep(AudioClip clip, float vol)
        {
            if (clip != null && _audioSource != null)
            {
                _audioSource.pitch = 1.0f;
                _audioSource.PlayOneShot(clip, vol);
            }
        }

        private void PlaySound(AudioClip clip, float vol)
        {
            if (clip != null && _audioSource != null)
            {
                _audioSource.pitch = 1.0f;
                _audioSource.PlayOneShot(clip, vol);
            }
        }

        private void GenerateProceduralSounds()
        {
            _beepShort = CreateToneClip(650f, 0.08f);
            _beepHigh = CreateToneClip(1100f, 0.22f);
            _roundWinClip = CreateMajorChordClip(0.6f);
            _roundLoseClip = CreateMinorChordClip(0.7f);
        }

        private AudioClip CreateToneClip(float frequency, float duration)
        {
            int sampleRate = 44100;
            int sampleCount = Mathf.CeilToInt(sampleRate * duration);
            float[] samples = new float[sampleCount];

            for (int i = 0; i < sampleCount; i++)
            {
                float t = (float)i / sampleRate;
                float envelope = 1f - (float)i / sampleCount; // linear decay
                samples[i] = Mathf.Sin(2 * Mathf.PI * frequency * t) * envelope * 0.45f;
            }

            AudioClip clip = AudioClip.Create("Tone_" + frequency, sampleCount, 1, sampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        private AudioClip CreateMajorChordClip(float duration)
        {
            int sampleRate = 44100;
            int sampleCount = Mathf.CeilToInt(sampleRate * duration);
            float[] samples = new float[sampleCount];

            float[] freqs = { 523.25f, 659.25f, 783.99f, 1046.50f }; // C5 chord

            for (int i = 0; i < sampleCount; i++)
            {
                float t = (float)i / sampleRate;
                float envelope = Mathf.Clamp01(1f - (t / duration));
                float sample = 0f;
                for (int f = 0; f < freqs.Length; f++)
                {
                    sample += Mathf.Sin(2 * Mathf.PI * freqs[f] * t);
                }
                samples[i] = (sample / freqs.Length) * envelope * 0.4f;
            }

            AudioClip clip = AudioClip.Create("WinChord", sampleCount, 1, sampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        private AudioClip CreateMinorChordClip(float duration)
        {
            int sampleRate = 44100;
            int sampleCount = Mathf.CeilToInt(sampleRate * duration);
            float[] samples = new float[sampleCount];

            float[] freqs = { 220.0f, 261.63f, 311.13f }; // A Minor low

            for (int i = 0; i < sampleCount; i++)
            {
                float t = (float)i / sampleRate;
                float envelope = Mathf.Clamp01(1f - (t / duration));
                float sample = 0f;
                for (int f = 0; f < freqs.Length; f++)
                {
                    sample += Mathf.Sin(2 * Mathf.PI * freqs[f] * t);
                }
                samples[i] = (sample / freqs.Length) * envelope * 0.4f;
            }

            AudioClip clip = AudioClip.Create("LoseSting", sampleCount, 1, sampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }
    }
}
