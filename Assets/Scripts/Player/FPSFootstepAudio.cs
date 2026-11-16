using UnityEngine;

namespace FPSFramework.Player
{
    /// <summary>
    /// Mengelola audio langkah kaki (footstep) dan benturan mendarat (landing)
    /// dengan filosofi Tactical Shooter (Counter-Strike / Valorant):
    /// - Akumulasi berbasis jarak tempuh (distance-based) untuk irama alami tanpa suara hantu saat counter-strafe.
    /// - Silent Walk: 100% senyap tanpa suara saat menahan Shift (IsWalking).
    /// - Crouch Footsteps: Nada lebih pelan dan irama lebih lambat saat jongkok.
    /// - Landing Impact: Suara benturan debum sepatu saat mendarat dari lompatan/jatuh.
    /// - Hybrid Audio: Menggunakan custom AudioClip jika diisi di Inspector,
    ///   atau otomatis menyintesis audio prosedural jika slot kosong (Zero-Download).
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class FPSFootstepAudio : MonoBehaviour
    {
        [Header("Referensi State Pergerakan")]
        [Tooltip("Sumber state pergerakan pemain (IFPSMovementState). Jika kosong, dicari otomatis.")]
        [SerializeField] private MonoBehaviour movementStateProvider;

        [Header("Jarak Langkah (Tactical Cadence)")]
        [Tooltip("Jarak tempuh (meter) untuk memicu 1 hentakan langkah saat berlari (default ~2.1m).")]
        [SerializeField] private float runStepInterval = 2.1f;

        [Tooltip("Jarak tempuh (meter) untuk memicu 1 hentakan langkah saat jongkok (default ~1.5m).")]
        [SerializeField] private float crouchStepInterval = 1.5f;

        [Tooltip("Jika aktif, berjalan perlahan (Shift / IsWalking) akan 100% hening (seperti di CS / Valorant).")]
        [SerializeField] private bool enableSilentWalk = true;

        [Tooltip("Ambang batas kecepatan horizontal minimum (m/s) sebelum akumulasi langkah dihitung.")]
        [SerializeField] private float minSpeedThreshold = 0.35f;

        [Header("Pengaturan Volume & Variasi")]
        [Range(0f, 1f)]
        [Tooltip("Volume suara langkah kaki saat berlari.")]
        [SerializeField] private float runVolume = 0.65f;

        [Range(0f, 1f)]
        [Tooltip("Pengali volume saat bergerak sambil jongkok (crouch).")]
        [SerializeField] private float crouchVolumeMultiplier = 0.45f;

        [Range(0f, 1f)]
        [Tooltip("Volume suara debum saat mendarat (landing).")]
        [SerializeField] private float landVolume = 0.85f;

        [Range(0f, 0.15f)]
        [Tooltip("Variasi pitch acak agar langkah kaki tidak terdengar monoton.")]
        [SerializeField] private float pitchVariation = 0.06f;

        [Tooltip("Waktu minimum melayang di udara (detik) sebelum benturan mendarat membunyikan suara.")]
        [SerializeField] private float minAirTimeToPlayLand = 0.14f;

        [Header("Custom Audio Clips (Opsional)")]
        [Tooltip("Koleksi audio klip langkah kaki. Jika kosong, sistem otomatis membuat audio sintetis berkualitas tinggi.")]
        [SerializeField] private AudioClip[] customFootstepClips;

        [Tooltip("Audio klip saat mendarat. Jika kosong, sistem otomatis membuat audio sintetis berkualitas tinggi.")]
        [SerializeField] private AudioClip customLandClip;

        // Komponen internal
        private AudioSource _audioSource;
        private IFPSMovementState _movementState;

        // Tracking state
        private float _distanceAccumulator;
        private bool _wasGroundedLastFrame = true;
        private float _airTimer;
        private bool _isLeftFoot;

        // Procedural Audio Clips (Fallback otomatis)
        private AudioClip[] _proceduralFootsteps;
        private AudioClip _proceduralLand;

        private void Awake()
        {
            // Konfigurasi AudioSource
            _audioSource = GetComponent<AudioSource>();
            if (_audioSource == null)
            {
                _audioSource = gameObject.AddComponent<AudioSource>();
            }

            _audioSource.playOnAwake = false;
            _audioSource.loop = false;
            _audioSource.spatialBlend = 0f; // 2D flat untuk sensasi first-person jernih di telinga player

            // Cari IFPSMovementState
            if (movementStateProvider != null && movementStateProvider is IFPSMovementState providerState)
            {
                _movementState = providerState;
            }
            else
            {
                _movementState = GetComponent<IFPSMovementState>() ?? GetComponentInParent<IFPSMovementState>();
            }

            // Buat procedural fallback clips jika klip kustom tidak ada
            GenerateProceduralAudioClips();
        }

        private void Update()
        {
            if (_movementState == null) return;

            bool isGrounded = _movementState.IsGrounded;

            if (!isGrounded)
            {
                // Sedang melayang di udara
                _airTimer += Time.deltaTime;
                _distanceAccumulator = 0f; // Reset akumulasi langkah saat melompat
            }
            else
            {
                // Momen mendarat (transisi dari udara ke tanah)
                if (!_wasGroundedLastFrame && _airTimer >= minAirTimeToPlayLand)
                {
                    PlayLandingSound();
                }

                _airTimer = 0f;

                // Logika langkah kaki di tanah
                HandleFootstepCycle();
            }

            _wasGroundedLastFrame = isGrounded;
        }

        /// <summary>
        /// Menghitung akumulasi jarak pergerakan kaki dan memicu audio langkah kaki.
        /// </summary>
        private void HandleFootstepCycle()
        {
            // Mekanik Silent Walk CS/Valorant: menahan Shift membuat langkah 100% hening
            if (enableSilentWalk && _movementState.IsWalking)
            {
                _distanceAccumulator = 0f;
                return;
            }

            float horizontalSpeed = _movementState.CurrentHorizontalSpeed;

            // Jika kecepatan terlalu rendah (misal: diam atau jiggle kecil), jangan hitung langkah
            if (horizontalSpeed < minSpeedThreshold)
            {
                // Redam akumulasi sisa secara perlahan agar tidak langsung memicu langkah saat bergerak 1 pixel nanti
                _distanceAccumulator = Mathf.MoveTowards(_distanceAccumulator, 0f, Time.deltaTime * 2f);
                return;
            }

            // Akumulasi jarak tempuh riil (kecepatan * delta time)
            _distanceAccumulator += horizontalSpeed * Time.deltaTime;

            float currentStepInterval = _movementState.IsCrouching ? crouchStepInterval : runStepInterval;

            if (_distanceAccumulator >= currentStepInterval)
            {
                PlayFootstep(_movementState.IsCrouching);
                _distanceAccumulator -= currentStepInterval;
            }
        }

        /// <summary>
        /// Memainkan satu langkah kaki dengan variasi dinamik.
        /// </summary>
        private void PlayFootstep(bool isCrouching)
        {
            AudioClip clipToPlay = GetFootstepClip();
            if (clipToPlay == null) return;

            float baseVolume = runVolume;
            if (isCrouching)
            {
                baseVolume *= crouchVolumeMultiplier;
            }

            // Bergantian kaki kiri dan kanan untuk variasi subtle nada
            _isLeftFoot = !_isLeftFoot;
            float footPitchOffset = _isLeftFoot ? -0.02f : 0.02f;
            float randomPitch = Random.Range(-pitchVariation, pitchVariation);

            _audioSource.pitch = 1f + footPitchOffset + randomPitch;
            _audioSource.panStereo = _isLeftFoot ? -0.06f : 0.06f; // Sentuhan stereo halus kaki kiri & kanan
            _audioSource.PlayOneShot(clipToPlay, baseVolume);
        }

        /// <summary>
        /// Memainkan suara debum saat kedua kaki mendarat di tanah.
        /// </summary>
        private void PlayLandingSound()
        {
            AudioClip landClip = GetLandClip();
            if (landClip == null) return;

            _audioSource.pitch = Random.Range(0.96f, 1.04f);
            _audioSource.panStereo = 0f;
            _audioSource.PlayOneShot(landClip, landVolume);
        }

        private AudioClip GetFootstepClip()
        {
            if (customFootstepClips != null && customFootstepClips.Length > 0)
            {
                int idx = Random.Range(0, customFootstepClips.Length);
                return customFootstepClips[idx];
            }

            if (_proceduralFootsteps != null && _proceduralFootsteps.Length > 0)
            {
                int idx = Random.Range(0, _proceduralFootsteps.Length);
                return _proceduralFootsteps[idx];
            }

            return null;
        }

        private AudioClip GetLandClip()
        {
            if (customLandClip != null)
            {
                return customLandClip;
            }

            return _proceduralLand;
        }

        /// <summary>
        /// Mensintesis audio klip prosedural di memori jika pengguna belum memasukkan aset audio kustom.
        /// Memproduksi gelombang perkusif langkah sepatu dan hentakan mendarat.
        /// </summary>
        private void GenerateProceduralAudioClips()
        {
            const int sampleRate = 44100;

            // 1. Buat 3 variasi langkah kaki (Footsteps A, B, C)
            _proceduralFootsteps = new AudioClip[3];
            float[] baseFreqs = new float[] { 135f, 150f, 125f };

            for (int i = 0; i < 3; i++)
            {
                int sampleCount = Mathf.RoundToInt(sampleRate * 0.10f); // 100 ms
                float[] data = new float[sampleCount];
                float baseFreq = baseFreqs[i];

                for (int s = 0; s < sampleCount; s++)
                {
                    float t = (float)s / sampleRate;
                    float decay = Mathf.Exp(-42f * t);

                    // Komponen frekuensi rendah tapak sepatu (pitch drop)
                    float currentFreq = Mathf.Lerp(baseFreq, baseFreq * 0.55f, t * 10f);
                    float tone = Mathf.Sin(2f * Mathf.PI * currentFreq * t);

                    // Komponen gesekan kasar sol sepatu (white noise lembut)
                    float noise = (Random.value * 2f - 1f) * Mathf.Exp(-95f * t) * 0.35f;

                    // Fade out di ujung sampel untuk mencegah click/pop
                    float fadeOut = Mathf.Clamp01((sampleCount - s) / 250f);

                    data[s] = (tone * 0.75f + noise) * decay * fadeOut;
                }

                _proceduralFootsteps[i] = AudioClip.Create($"Procedural_Step_{i}", sampleCount, 1, sampleRate, false);
                _proceduralFootsteps[i].SetData(data, 0);
            }

            // 2. Buat audio hentakan mendarat (Landing Thud)
            int landSampleCount = Mathf.RoundToInt(sampleRate * 0.18f); // 180 ms
            float[] landData = new float[landSampleCount];

            for (int s = 0; s < landSampleCount; s++)
            {
                float t = (float)s / sampleRate;
                float decay = Mathf.Exp(-20f * t);

                // Hentakan bass berat saat mendarat (80Hz turun ke 40Hz)
                float currentFreq = Mathf.Lerp(90f, 42f, t * 6f);
                float tone = Mathf.Sin(2f * Mathf.PI * currentFreq * t);

                // Debu & benturan gesekan sol
                float noise = (Random.value * 2f - 1f) * Mathf.Exp(-45f * t) * 0.45f;
                float fadeOut = Mathf.Clamp01((landSampleCount - s) / 400f);

                landData[s] = (tone * 0.82f + noise) * decay * fadeOut;
            }

            _proceduralLand = AudioClip.Create("Procedural_LandThud", landSampleCount, 1, sampleRate, false);
            _proceduralLand.SetData(landData, 0);
        }
    }
}
