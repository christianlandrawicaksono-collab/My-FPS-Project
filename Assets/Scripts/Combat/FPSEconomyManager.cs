using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using FPSFramework.Player;

namespace FPSFramework.Combat
{
    /// <summary>
    /// Sistem Ekonomi & Buy Menu Taktis ala Tactical Shooter (CS2 / Valorant):
    /// 1. Pengelolaan Saldo Uang ($ Cash):
    ///    - Saldo awal Ronde 1: $800 (Pistol Round).
    ///    - Maksimal saldo: $16,000.
    ///    - Reward Kill: +$300 per eliminasi bot.
    ///    - Reward Kemenangan Ronde: +$3,250.
    ///    - Kompensasi Kekalahan Ronde: +$1,400 (+$500 per loss streak, max $3,400).
    ///    - Reward Penjinak Bom C4: +$300.
    /// 2. Buy Period & Buy Menu (Tombol 'B'):
    ///    - Menu pembelian dapat dibuka dengan tombol B selama fase FreezeTime dan awal ronde (20s).
    ///    - Kursor mouse otomatis terbuka saat belanja dan terkunci kembali saat ditutup.
    /// 3. Katalog Pembelian:
    ///    - Kevlar Vest ($650): 100 Armor penyerapan 50% damage badan.
    ///    - Helmet ($350 upgrade / $1,000 full): Melindungi dari instant-kill headshot.
    ///    - Defuse Kit ($400): Mempercepat waktu defuse bom dari 10s menjadi 5s.
    ///    - SMG-K Tactical ($1,500) & Refill Ammo ($200).
    ///    - Frag Grenade ($300).
    /// </summary>
    public class FPSEconomyManager : MonoBehaviour
    {
        public static FPSEconomyManager Instance { get; private set; }

        [Header("Pengaturan Finansial Awal")]
        [SerializeField] private int startingCash = 800;
        [SerializeField] private int maxCash = 16000;
        [SerializeField] private float buyPeriodDuration = 20.0f; // detik dari awal ronde

        [Header("Daftar Harga Barang ($)")]
        public const int CostKevlarVest = 650;
        public const int CostHelmet = 350;
        public const int CostFullKevlarHelmet = 1000;
        public const int CostDefuseKit = 400;
        public const int CostSMGK = 1500;
        public const int CostSMGAmmo = 200;
        public const int CostPistolAmmo = 100;
        public const int CostFragGrenade = 300;

        // Status Finansial
        public int CurrentCash { get; private set; }
        public bool HasDefuseKit { get; private set; } = false;
        public bool IsBuyMenuOpen { get; private set; } = false;
        public float BuyTimeRemaining { get; private set; } = 0f;
        public bool IsBuyPeriodActive => BuyTimeRemaining > 0f;
        public int LossStreak { get; private set; } = 0;

        // Static Events
        public static event System.Action<int, string> OnCashChanged; // newCash, reason
        public static event System.Action<bool> OnBuyMenuToggled;      // isOpen
        public static event System.Action<string> OnItemPurchased;     // itemName

        // Audio
        private AudioSource _audioSource;
        private static AudioClip _buySuccessClip;
        private static AudioClip _buyErrorClip;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            CurrentCash = startingCash;

            _audioSource = GetComponent<AudioSource>();
            if (_audioSource == null)
            {
                _audioSource = gameObject.AddComponent<AudioSource>();
            }
            _audioSource.playOnAwake = false;
            _audioSource.spatialBlend = 0f;

            if (_buySuccessClip == null) _buySuccessClip = GenerateBuySuccessClip();
            if (_buyErrorClip == null) _buyErrorClip = GenerateBuyErrorClip();
        }

        private void OnEnable()
        {
            EnemyHumanoidAI.OnAnyEnemyDied += HandleBotKilled;
            PlantedC4.OnAnyC4Defused += HandleBombDefused;
        }

        private void OnDisable()
        {
            EnemyHumanoidAI.OnAnyEnemyDied -= HandleBotKilled;
            PlantedC4.OnAnyC4Defused -= HandleBombDefused;
        }

        private void Update()
        {
            if (BuyTimeRemaining > 0f)
            {
                BuyTimeRemaining -= Time.deltaTime;
                if (BuyTimeRemaining <= 0f && IsBuyMenuOpen)
                {
                    CloseBuyMenu();
                }
            }

            HandleInput();
        }

        private void HandleInput()
        {
            if (Keyboard.current == null) return;

            // Tombol 'B' untuk toggle Buy Menu
            if (Keyboard.current.bKey.wasPressedThisFrame)
            {
                if (IsBuyMenuOpen)
                {
                    CloseBuyMenu();
                }
                else if (IsBuyPeriodActive)
                {
                    OpenBuyMenu();
                }
            }

            // Tombol 'ESC' untuk menutup Buy Menu jika sedang terbuka
            if (Keyboard.current.escapeKey.wasPressedThisFrame && IsBuyMenuOpen)
            {
                CloseBuyMenu();
            }
        }

        public void OpenBuyMenu()
        {
            if (!IsBuyPeriodActive) return;

            IsBuyMenuOpen = true;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            // Kunci penembakan dan kamera sementara saat belanja
            if (FPSWeaponManager.Instance != null)
            {
                FPSWeaponManager.Instance.SetWeaponsLocked(true);
            }

            FPSCameraController cam = FindAnyObjectByType<FPSCameraController>();
            if (cam != null) cam.CameraLookLocked = true;

            OnBuyMenuToggled?.Invoke(true);
        }

        public void CloseBuyMenu()
        {
            if (!IsBuyMenuOpen) return;

            IsBuyMenuOpen = false;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;

            // Buka kembali kunci penembakan HANYA jika bukan dalam fase FreezeTime ronde
            bool isFreezeTime = (TacticalRoundManager.Instance != null && TacticalRoundManager.Instance.State == RoundState.FreezeTime);
            if (FPSWeaponManager.Instance != null)
            {
                FPSWeaponManager.Instance.SetWeaponsLocked(isFreezeTime);
            }

            FPSCameraController cam = FindAnyObjectByType<FPSCameraController>();
            if (cam != null) cam.CameraLookLocked = false;

            OnBuyMenuToggled?.Invoke(false);
        }

        public void StartNewRoundBuyPeriod()
        {
            BuyTimeRemaining = buyPeriodDuration;
        }

        public void AddCash(int amount, string reason = "")
        {
            CurrentCash = Mathf.Clamp(CurrentCash + amount, 0, maxCash);
            OnCashChanged?.Invoke(CurrentCash, reason);
        }

        public bool DeductCash(int amount)
        {
            if (CurrentCash >= amount)
            {
                CurrentCash -= amount;
                OnCashChanged?.Invoke(CurrentCash, "");
                PlayPurchaseSound();
                return true;
            }

            PlayErrorSound();
            return false;
        }

        private void HandleBotKilled(EnemyHumanoidAI bot)
        {
            AddCash(300, "+$300 KILL REWARD");
        }

        private void HandleBombDefused(PlantedC4 c4)
        {
            AddCash(300, "+$300 BOMB DEFUSED");
        }

        /// <summary>
        /// Memberikan hadiah uang saat akhir ronde berdasarkan kemenangan atau kekalahan.
        /// </summary>
        public void RewardRoundOutcome(bool didPlayerWin)
        {
            if (didPlayerWin)
            {
                LossStreak = 0;
                AddCash(3250, "+$3,250 ROUND WON");
            }
            else
            {
                int compensation = Mathf.Min(1400 + (LossStreak * 500), 3400);
                LossStreak++;
                AddCash(compensation, $"+${compensation:N0} ROUND LOSS");
            }
        }

        #region Purchase Actions

        public bool TryBuyKevlarVest()
        {
            PlayerHealth health = FindAnyObjectByType<PlayerHealth>();
            if (health == null) return false;

            if (health.CurrentArmor >= 100f && !health.HasHelmet)
            {
                // Jika sudah punya armor penuh, bisa langsung beli helm saja
                return TryBuyHelmetOnly();
            }

            if (health.CurrentArmor >= 100f && health.HasHelmet)
            {
                return false; // Sudah memiliki proteksi maksimal
            }

            if (DeductCash(CostKevlarVest))
            {
                health.SetArmor(100f, health.HasHelmet);
                OnItemPurchased?.Invoke("KEVLAR VEST");
                return true;
            }
            return false;
        }

        public bool TryBuyHelmetOnly()
        {
            PlayerHealth health = FindAnyObjectByType<PlayerHealth>();
            if (health == null || health.HasHelmet) return false;

            if (DeductCash(CostHelmet))
            {
                health.EquipHelmet();
                OnItemPurchased?.Invoke("HELMET UPGRADE");
                return true;
            }
            return false;
        }

        public bool TryBuyFullKevlarAndHelmet()
        {
            PlayerHealth health = FindAnyObjectByType<PlayerHealth>();
            if (health == null) return false;

            if (health.CurrentArmor >= 100f && health.HasHelmet) return false;

            // Jika sudah punya vest, hanya bayar upgrade helm
            if (health.CurrentArmor >= 100f && !health.HasHelmet)
            {
                return TryBuyHelmetOnly();
            }

            int cost = health.HasHelmet ? CostKevlarVest : CostFullKevlarHelmet;
            if (DeductCash(cost))
            {
                health.SetArmor(100f, true);
                OnItemPurchased?.Invoke("KEVLAR + HELMET");
                return true;
            }
            return false;
        }

        public bool TryBuyDefuseKit()
        {
            if (HasDefuseKit) return false;

            if (DeductCash(CostDefuseKit))
            {
                HasDefuseKit = true;
                OnItemPurchased?.Invoke("DEFUSE KIT");
                return true;
            }
            return false;
        }

        public bool TryBuySMGK()
        {
            FPSWeaponManager wm = FPSWeaponManager.Instance;
            if (wm == null) return false;

            FPSWeapon smg = wm.GetComponentInChildren<FPSWeapon>();
            if (smg != null && smg.WeaponName.Contains("SMG"))
            {
                // Jika sudah memiliki SMG, beli isi ulang amunisi
                return TryBuySMGAmmo();
            }

            if (DeductCash(CostSMGK))
            {
                wm.SwitchToSlot(WeaponSlot.Primary);
                if (wm.ActiveGun != null)
                {
                    wm.ActiveGun.ResetAmmoAndRecoil();
                }
                OnItemPurchased?.Invoke("SMG-K TACTICAL");
                return true;
            }
            return false;
        }

        public bool TryBuySMGAmmo()
        {
            FPSWeaponManager wm = FPSWeaponManager.Instance;
            if (wm == null) return false;

            if (DeductCash(CostSMGAmmo))
            {
                if (wm.ActiveGun != null)
                {
                    wm.ActiveGun.ResetAmmoAndRecoil();
                }
                OnItemPurchased?.Invoke("SMG AMMO REFILL");
                return true;
            }
            return false;
        }

        public bool TryBuyPistolAmmo()
        {
            FPSWeaponManager wm = FPSWeaponManager.Instance;
            if (wm == null) return false;

            if (DeductCash(CostPistolAmmo))
            {
                // Isi ulang amunisi secondary
                OnItemPurchased?.Invoke("PISTOL AMMO");
                return true;
            }
            return false;
        }

        public bool TryBuyFragGrenade()
        {
            FPSWeaponManager wm = FPSWeaponManager.Instance;
            if (wm == null) return false;

            if (wm.FragGrenades >= 1) return false; // Maksimal 1 granat

            if (DeductCash(CostFragGrenade))
            {
                wm.AddGrenade(1); // Menambah granat tanpa mereset loadout atau slot senjata
                OnItemPurchased?.Invoke("FRAG GRENADE");
                return true;
            }
            return false;
        }

        public void ResetKitOnDeath()
        {
            HasDefuseKit = false;
        }

        #endregion

        #region Procedural Audio Feedback

        private void PlayPurchaseSound()
        {
            if (_buySuccessClip != null && _audioSource != null)
            {
                _audioSource.pitch = Random.Range(0.98f, 1.05f);
                _audioSource.PlayOneShot(_buySuccessClip, 0.75f);
            }
        }

        private void PlayErrorSound()
        {
            if (_buyErrorClip != null && _audioSource != null)
            {
                _audioSource.pitch = 0.9f;
                _audioSource.PlayOneShot(_buyErrorClip, 0.7f);
            }
        }

        private static AudioClip GenerateBuySuccessClip()
        {
            int sampleRate = 44100;
            float duration = 0.22f;
            int totalSamples = Mathf.FloorToInt(sampleRate * duration);
            float[] samples = new float[totalSamples];

            for (int i = 0; i < totalSamples; i++)
            {
                float time = (float)i / sampleRate;
                float progress = time / duration;
                float env = Mathf.Sin(progress * Mathf.PI);

                // Nada lonceng koin ceria (chord C mayor: 1046Hz & 1318Hz)
                float tone1 = Mathf.Sin(2f * Mathf.PI * 1046f * time);
                float tone2 = Mathf.Sin(2f * Mathf.PI * 1318f * time);
                samples[i] = (tone1 * 0.5f + tone2 * 0.5f) * env * 0.6f;
            }

            AudioClip clip = AudioClip.Create("Proc_Buy_Success", totalSamples, 1, sampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        private static AudioClip GenerateBuyErrorClip()
        {
            int sampleRate = 44100;
            float duration = 0.18f;
            int totalSamples = Mathf.FloorToInt(sampleRate * duration);
            float[] samples = new float[totalSamples];

            for (int i = 0; i < totalSamples; i++)
            {
                float time = (float)i / sampleRate;
                float env = Mathf.Exp(-time * 15f);

                // Buzzer nada rendah tumpul (150Hz)
                float tone = Mathf.Sin(2f * Mathf.PI * 150f * time);
                samples[i] = tone * env * 0.75f;
            }

            AudioClip clip = AudioClip.Create("Proc_Buy_Error", totalSamples, 1, sampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        #endregion
    }
}
