using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using FPSFramework.Combat;

namespace FPSFramework.Player
{
    /// <summary>
    /// Sistem Penembakan Senjata FPS bernuansa Tactical Shooter (CS / Valorant).
    /// Mengintegrasikan:
    /// 1. Hitscan Raycast presisi tinggi.
    /// 2. Inakurasi dinamis berbasis pergerakan (IFPSMovementState).
    /// 3. Recoil spread & Viewmodel Kickback fisik.
    /// 4. Audio Feedback (Shoot, Reload, 3D Bullet Impact, Headshot, Bodyshot).
    /// 5. Visual Tracer Peluru.
    /// 6. VFX Muzzle Flash (Dynamic Light + Flash Quad).
    /// 7. VFX Impact Sparks (Percikan api pantulan di dinding/lantai).
    /// 8. VFX Bullet Hole Decal (Bekas lubang peluru bertekstur hangus di permukaan).
    /// 9. VFX Casing Ejection (Selongsong peluru terlempar dengan fisika).
    /// 10. Combat Hitbox Integration (Headshot vs Bodyshot damage).
    /// </summary>
    public class FPSWeapon : MonoBehaviour
    {
        [Header("Referensi Komponen")]
        [Tooltip("Kamera utama player. Jika kosong, akan mencari Camera.main.")]
        [SerializeField] private Camera playerCamera;

        [Tooltip("Transform moncong senjata tempat keluarnya peluru/tracer. Jika kosong, otomatis mendeteksi child 'SMG_K_Barrel'.")]
        [SerializeField] private Transform muzzleTransform;

        [Tooltip("Transform lubang selongsong keluar. Jika kosong, otomatis mendeteksi child 'SMG_K_Ejection Port'.")]
        [SerializeField] private Transform ejectionPortTransform;

        [Header("Combat & Damage Stats (SMG_K)")]
        [Tooltip("Damage dasar untuk tembakan badan.")]
        [SerializeField] private float baseDamage = 26f;

        [Tooltip("Jeda antar tembakan dalam detik (0.1s = 600 RPM).")]
        [SerializeField] private float fireRate = 0.1f;

        [Tooltip("Jarak maksimal peluru (meter).")]
        [SerializeField] private float range = 100f;

        [Tooltip("Kapasitas peluru satu magasin.")]
        [SerializeField] private int magazineSize = 30;

        [Tooltip("Waktu durasi reload dalam detik.")]
        [SerializeField] private float reloadDuration = 2.0f;

        [Header("Tactical Accuracy & Spread (CS / Valorant)")]
        [Tooltip("Inakurasi dasar saat berdiri diam (0 = laser pin-point).")]
        [SerializeField] private float baseSpread = 0.003f;

        [Tooltip("Inakurasi saat menembak terus menerus (spray bloom).")]
        [SerializeField] private float maxSpraySpread = 0.075f;

        [Tooltip("Pertambahan spread setiap 1 peluru ditembakkan.")]
        [SerializeField] private float sprayIncreasePerShot = 0.009f;

        [Tooltip("Kecepatan spread kembali stabil setelah berhenti menembak.")]
        [SerializeField] private float sprayRecoverySpeed = 7.5f;

        [Tooltip("Pengali inakurasi gerakan dari IFPSMovementState.")]
        [SerializeField] private float movementInaccuracyMultiplier = 0.07f;

        [Header("Tactical Camera Recoil & Spray Pattern")]
        [Tooltip("Hentakan sudut pitch kamera ke atas per peluru (derajat).")]
        [SerializeField] private float cameraPitchKick = 2.1f;

        [Tooltip("Goyangan sudut yaw kamera (kiri/kanan) per peluru (derajat).")]
        [SerializeField] private float cameraYawKick = 0.75f;

        [Tooltip("Waktu henti menembak agar recoil kembali ke peluru pertama (detik).")]
        [SerializeField] private float recoilResetDelay = 0.35f;

        [Header("Viewmodel Recoil Animation")]
        [Tooltip("Dorongan mundur senjata ke belakang saat menembak.")]
        [SerializeField] private float kickbackDistance = 0.065f;

        [Tooltip("Sentakan ke atas saat menembak.")]
        [SerializeField] private float kickbackUp = 0.028f;

        [Tooltip("Kecepatan posisi senjata kembali semula.")]
        [SerializeField] private float kickbackReturnSpeed = 18f;

        [Header("Audio SFX")]
        [SerializeField] private AudioClip shootSound;
        [SerializeField] private AudioClip reloadSound;
        [SerializeField] private AudioClip bulletImpactSound;
        [Tooltip("Suara headshot khas (DING/helm pecah).")]
        [SerializeField] private AudioClip headshotSound;
        [Tooltip("Suara benturan peluru pada badan/target.")]
        [SerializeField] private AudioClip fleshHitSound;

        /// <summary>
        /// Event yang dipanggil saat tembakan mengenai target damageable (bool isHeadshot).
        /// </summary>
        public event System.Action<bool> OnTargetHitEvent;

        /// <summary>
        /// Event yang dipanggil saat tembakan memberikan damage (damage, isHeadshot, hitPoint).
        /// </summary>
        public event System.Action<float, bool, Vector3> OnDamageDealtEvent;

        /// <summary>
        /// Event static global saat peluru mengenai target (bool isHeadshot).
        /// </summary>
        public static event System.Action<bool> OnAnyTargetHit;

        /// <summary>
        /// Event static global saat peluru memberikan damage (damage, isHeadshot, hitPoint).
        /// </summary>
        public static event System.Action<float, bool, Vector3> OnAnyDamageDealt;

        [Header("Tactical HUD & Inventory Info")]
        [SerializeField] private string weaponName = "SMG-K TACTICAL";
        [SerializeField] private int maxReserveAmmo = 90;

        [Header("Visual Effects - Tracer")]
        [Tooltip("Material untuk garis tracer peluru.")]
        [SerializeField] private Material tracerMaterial;
        [SerializeField] private float tracerDuration = 0.04f;

        [Header("Visual Effects - Muzzle Flash")]
        [SerializeField] private bool enableMuzzleFlash = true;
        [SerializeField] private Color muzzleFlashColor = new Color(1f, 0.75f, 0.25f);
        [SerializeField] private float muzzleFlashRange = 4.0f;

        [Header("Visual Effects - Casing Ejection")]
        [Tooltip("Prefab selongsong peluru (opsional, misal Bullet_SMG_A). Jika kosong, menggunakan procedural casing.")]
        [SerializeField] private GameObject casingPrefab;
        [SerializeField] private bool enableCasingEjection = true;
        [Tooltip("Berapa detik selongsong bertahan di lantai sebelum hilang.")]
        [SerializeField] private float casingLifetime = 5.0f;

        [Header("Visual Effects - Bullet Holes & Sparks")]
        [SerializeField] private bool enableBulletHoles = true;
        [SerializeField] private bool enableImpactSparks = true;

        // Runtime State
        public string WeaponName => weaponName;
        public int MagazineSize => magazineSize;
        public int CurrentAmmo { get; private set; }
        public int ReserveAmmo { get; private set; }
        public bool IsReloading { get; private set; }
        public float ReloadProgress => IsReloading && reloadDuration > 0f ? Mathf.Clamp01(_reloadTimer / reloadDuration) : 0f;
        public float CurrentSpread { get; private set; }
        public int BurstBulletIndex { get; private set; }
        public Vector2 CurrentSprayOffset { get; private set; }
        public bool FiringLocked { get; set; } = false;

        private float _reloadTimer;

        // Pola Semprotan Taktikal SMG_K (30 Peluru) - Kurva Tajam & Menantang
        private static readonly Vector2[] SprayPattern = new Vector2[]
        {
            new Vector2(0.000f, 0.000f), // Peluru 1: 100% pin-point (reward tap-fire)
            new Vector2(0.001f, 0.012f), // Peluru 2: Mulai naik vertikal
            new Vector2(0.002f, 0.025f), // Peluru 3: Vertikal menanjak
            new Vector2(0.004f, 0.039f), // Peluru 4: Vertikal deras
            new Vector2(0.008f, 0.053f), // Peluru 5: Tarik mouse kuat ke bawah
            new Vector2(0.015f, 0.066f), // Peluru 6: Mulai belok kanan
            new Vector2(0.025f, 0.076f), // Peluru 7: Kanan atas
            new Vector2(0.035f, 0.083f), // Peluru 8: Kanan
            new Vector2(0.044f, 0.086f), // Peluru 9: Max Kanan Atas
            new Vector2(0.048f, 0.085f), // Peluru 10: Max Kanan
            new Vector2(0.045f, 0.083f), // Peluru 11: Berhenti di kanan
            new Vector2(0.036f, 0.081f), // Peluru 12: Banting balik ke kiri
            new Vector2(0.022f, 0.079f), // Peluru 13: Menuju tengah
            new Vector2(0.007f, 0.078f), // Peluru 14: Tengah
            new Vector2(-0.010f, 0.078f), // Peluru 15: Mulai memanjat ke kiri
            new Vector2(-0.026f, 0.080f), // Peluru 16: Kiri
            new Vector2(-0.039f, 0.082f), // Peluru 17: Kiri tajam
            new Vector2(-0.048f, 0.084f), // Peluru 18: Max Kiri
            new Vector2(-0.049f, 0.082f), // Peluru 19: Max Kiri
            new Vector2(-0.042f, 0.080f), // Peluru 20: Mulai belok balik
            new Vector2(-0.028f, 0.078f), // Peluru 21: Menuju tengah
            new Vector2(-0.010f, 0.077f), // Peluru 22: Tengah
            new Vector2(0.012f, 0.078f),  // Peluru 23: Ayunan kanan ke-2
            new Vector2(0.028f, 0.080f),  // Peluru 24: Kanan
            new Vector2(0.036f, 0.081f),  // Peluru 25: Kanan
            new Vector2(0.029f, 0.079f),  // Peluru 26: Stabilisasi
            new Vector2(0.014f, 0.078f),  // Peluru 27
            new Vector2(-0.006f, 0.077f), // Peluru 28
            new Vector2(-0.022f, 0.079f), // Peluru 29
            new Vector2(-0.012f, 0.078f)  // Peluru 30: Akhir magasin
        };

        private float _nextFireTime;
        private float _currentSprayBloom;
        private Vector3 _originalLocalPos;
        private Quaternion _originalLocalRot;
        private Vector3 _kickbackOffset;

        private AudioSource _audioSource;
        private FPSInputReader _inputReader;
        private IFPSMovementState _movementState;
        private FPSCameraController _cameraController;
        private LayerMask _hitLayers;

        // VFX Objects
        private Light _muzzleLight;
        private GameObject _muzzleFlashQuad;
        private ParticleSystem _impactSparks;
        private static Material _bulletHoleMaterial;
        private static readonly Queue<GameObject> _bulletHolePool = new Queue<GameObject>();
        private const int MaxBulletHoles = 50;

        private void Awake()
        {
            if (playerCamera == null)
            {
                playerCamera = Camera.main;
                if (playerCamera == null)
                {
                    playerCamera = GetComponentInParent<Camera>();
                }
            }

            if (playerCamera != null)
            {
                _cameraController = playerCamera.GetComponent<FPSCameraController>();
            }
            if (_cameraController == null)
            {
                _cameraController = GetComponentInParent<FPSCameraController>();
            }

            _audioSource = GetComponent<AudioSource>();
            if (_audioSource == null)
            {
                _audioSource = gameObject.AddComponent<AudioSource>();
            }
            _audioSource.playOnAwake = false;
            _audioSource.spatialBlend = 0f; // 2D sound for weapon viewmodel

            _inputReader = GetComponentInParent<FPSInputReader>();
            _movementState = GetComponentInParent<IFPSMovementState>();

            // Exclude Player layer agar tembakan tidak membentur kapsul sendiri
            _hitLayers = ~LayerMask.GetMask("Ignore Raycast");

            CurrentAmmo = magazineSize;
            ReserveAmmo = maxReserveAmmo;
            CurrentSpread = baseSpread;
            _originalLocalPos = transform.localPosition;
            _originalLocalRot = transform.localRotation;

            // Auto-detect barrel and ejection port jika belum diisi
            if (muzzleTransform == null)
            {
                Transform barrelChild = transform.Find("SMG_K_Barrel");
                if (barrelChild != null) muzzleTransform = barrelChild;
            }

            if (ejectionPortTransform == null)
            {
                Transform ejectChild = transform.Find("SMG_K_Ejection Port");
                if (ejectChild != null) ejectionPortTransform = ejectChild;
            }

            SetupMuzzleFlash();
            SetupImpactSparks();
            FixWeaponMaterials();
        }

        private void Start()
        {
            // Auto-load audio jika belum di-assign di Inspector
            if (shootSound == null) shootSound = Resources.Load<AudioClip>("Sound/AK_Sound");
            if (reloadSound == null) reloadSound = Resources.Load<AudioClip>("Sound/Reload-sound");
            if (bulletImpactSound == null) bulletImpactSound = Resources.Load<AudioClip>("Sound/Bullet_impact_2");
            if (headshotSound == null) headshotSound = Resources.Load<AudioClip>("Sound/headshot");
            if (fleshHitSound == null) fleshHitSound = Resources.Load<AudioClip>("Sound/hit-flesh");
        }

        private void Update()
        {
            HandleRecovery();
            HandleInput();
            UpdateViewmodelAnimation();
        }

        private void HandleInput()
        {
            if (FiringLocked) return;
            if (_inputReader == null) return;

            // Trigger Reload
            if (_inputReader.ConsumeReloadTriggered() && !IsReloading && CurrentAmmo < magazineSize)
            {
                StartCoroutine(ReloadRoutine());
                return;
            }

            // Fire Tembakan
            if (_inputReader.IsFireHeld && Time.time >= _nextFireTime && !IsReloading)
            {
                if (CurrentAmmo > 0)
                {
                    Shoot();
                }
                else
                {
                    // Auto reload jika peluru habis
                    StartCoroutine(ReloadRoutine());
                }
            }
        }

        private void Shoot()
        {
            _nextFireTime = Time.time + fireRate;
            CurrentAmmo--;
            BurstBulletIndex++;

            // Hitung indeks pola semprotan
            int patternIdx = Mathf.Clamp(BurstBulletIndex - 1, 0, SprayPattern.Length - 1);
            Vector2 sprayOffset = SprayPattern[patternIdx];
            CurrentSprayOffset = sprayOffset;

            // Hitung total spread: Base + Spray Bloom + Gerakan (Counter-strafe = 0 movement penalty)
            float movePenalty = 0f;
            if (_movementState != null)
            {
                movePenalty = _movementState.InaccuracyModifier * movementInaccuracyMultiplier;
            }

            CurrentSpread = baseSpread + _currentSprayBloom + movePenalty;
            _currentSprayBloom = Mathf.Min(_currentSprayBloom + sprayIncreasePerShot, maxSpraySpread);

            // Play shoot audio
            if (shootSound != null)
            {
                _audioSource.pitch = Random.Range(0.96f, 1.04f); // Subtle pitch variation
                _audioSource.PlayOneShot(shootSound, 0.85f);
            }

            // Lapisan 1: Sentakan Camera Recoil Punch ke FPSCameraController
            if (_cameraController != null)
            {
                float pitchKick = -cameraPitchKick * Random.Range(0.85f, 1.15f);
                float yawKick = Random.Range(-cameraYawKick, cameraYawKick);
                _cameraController.AddRecoilKick(new Vector2(yawKick, pitchKick));
            }

            // Terapkan viewmodel kickback
            _kickbackOffset += new Vector3(
                Random.Range(-0.005f, 0.005f),
                kickbackUp,
                -kickbackDistance
            );

            // Trigger Muzzle Flash VFX
            if (enableMuzzleFlash)
            {
                StartCoroutine(MuzzleFlashRoutine());
            }

            // Trigger Casing Ejection VFX
            if (enableCasingEjection)
            {
                EjectCasing();
            }

            // Lapisan 2: Lakukan Raycast dengan offset Spray Pattern kurva
            ExecuteRaycast(CurrentSpread, sprayOffset);
        }

        private void ExecuteRaycast(float spreadAngle, Vector2 sprayOffset)
        {
            if (playerCamera == null) return;

            // Titik tengah layar (0.5, 0.5)
            Ray centerRay = playerCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));

            // Tambahkan deviasi acak berdasarkan spread + pergeseran Spray Pattern khas CS/Valorant
            Vector2 randomCircle = Random.insideUnitCircle * spreadAngle;
            Vector3 shootDirection = centerRay.direction 
                + (playerCamera.transform.right * (sprayOffset.x + randomCircle.x)) 
                + (playerCamera.transform.up * (sprayOffset.y + randomCircle.y));
            shootDirection.Normalize();

            Vector3 startPos = muzzleTransform != null ? muzzleTransform.position : transform.position + transform.forward * 0.3f;
            Vector3 targetPoint = centerRay.origin + (shootDirection * range);

            if (Physics.Raycast(centerRay.origin, shootDirection, out RaycastHit hit, range, _hitLayers))
            {
                targetPoint = hit.point;

                // Push Rigidbody jika objek memiliki fisika
                if (hit.rigidbody != null && !hit.rigidbody.isKinematic)
                {
                    hit.rigidbody.AddForceAtPosition(shootDirection * 250f, hit.point);
                }

                // Cek apakah mengenai Hitbox Combat (Target Dummy / Karakter)
                FPSHitbox hitbox = hit.collider.GetComponent<FPSHitbox>();
                if (hitbox == null) hitbox = hit.collider.GetComponentInParent<FPSHitbox>();

                IDamageable damageable = hitbox != null ? null : hit.collider.GetComponentInParent<IDamageable>();
                if (hitbox == null && damageable == null && hit.collider.transform.root != null)
                {
                    damageable = hit.collider.transform.root.GetComponentInChildren<IDamageable>();
                }

                if (hitbox != null)
                {
                    bool isHeadshot = hitbox.HitboxType == HitboxType.Head;
                    float finalDamage = baseDamage * hitbox.DamageMultiplier;
                    hitbox.ReceiveHit(baseDamage, hit.point, shootDirection);

                    // Audio feedback
                    if (isHeadshot && headshotSound != null)
                    {
                        _audioSource.PlayOneShot(headshotSound, 1.0f);
                    }
                    else if (fleshHitSound != null)
                    {
                        _audioSource.PlayOneShot(fleshHitSound, 0.9f);
                    }

                    // Picu event hitmarker & damage
                    OnTargetHitEvent?.Invoke(isHeadshot);
                    OnDamageDealtEvent?.Invoke(finalDamage, isHeadshot, hit.point);
                    OnAnyTargetHit?.Invoke(isHeadshot);
                    OnAnyDamageDealt?.Invoke(finalDamage, isHeadshot, hit.point);
                }
                else if (damageable != null)
                {
                    damageable.TakeDamage(baseDamage, HitboxType.Body, hit.point, shootDirection);
                    if (fleshHitSound != null)
                    {
                        _audioSource.PlayOneShot(fleshHitSound, 0.9f);
                    }

                    OnTargetHitEvent?.Invoke(false);
                    OnDamageDealtEvent?.Invoke(baseDamage, false, hit.point);
                    OnAnyTargetHit?.Invoke(false);
                    OnAnyDamageDealt?.Invoke(baseDamage, false, hit.point);
                }
                else
                {
                    // Tumbukan dinding / lantai / lingkungan umum
                    if (bulletImpactSound != null)
                    {
                        AudioSource.PlayClipAtPoint(bulletImpactSound, hit.point, 0.8f);
                    }

                    // Spawn Impact Sparks
                    if (enableImpactSparks)
                    {
                        SpawnImpactSparks(hit.point, hit.normal);
                    }

                    // Spawn Bullet Hole Decal
                    if (enableBulletHoles)
                    {
                        SpawnBulletHole(hit);
                    }
                }
            }

            // Spawn Tracer line
            StartCoroutine(SpawnTracerRoutine(startPos, targetPoint));
        }

        #region VFX Implementations (Muzzle Flash, Sparks, Bullet Hole, Casing)

        private void SetupMuzzleFlash()
        {
            Transform muzzle = muzzleTransform != null ? muzzleTransform : transform;

            // Point Light untuk kilatan cahaya
            GameObject lightObj = new GameObject("MuzzleFlashLight");
            lightObj.transform.SetParent(muzzle, false);
            lightObj.transform.localPosition = Vector3.forward * 0.2f;

            _muzzleLight = lightObj.AddComponent<Light>();
            _muzzleLight.type = LightType.Point;
            _muzzleLight.color = muzzleFlashColor;
            _muzzleLight.range = muzzleFlashRange;
            _muzzleLight.intensity = 2.5f;
            _muzzleLight.enabled = false;

            // Quad mesh untuk visual kilatan api
            _muzzleFlashQuad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            _muzzleFlashQuad.name = "MuzzleFlashSprite";
            _muzzleFlashQuad.transform.SetParent(muzzle, false);
            _muzzleFlashQuad.transform.localPosition = Vector3.forward * 0.22f;
            _muzzleFlashQuad.transform.localScale = Vector3.one * 0.12f;
            Destroy(_muzzleFlashQuad.GetComponent<Collider>());

            Shader unlitShader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
            Material flashMat = new Material(unlitShader);
            flashMat.color = new Color(1f, 0.9f, 0.45f, 0.95f);
            _muzzleFlashQuad.GetComponent<MeshRenderer>().material = flashMat;
            _muzzleFlashQuad.SetActive(false);
        }

        private IEnumerator MuzzleFlashRoutine()
        {
            if (_muzzleLight != null) _muzzleLight.enabled = true;
            if (_muzzleFlashQuad != null)
            {
                _muzzleFlashQuad.transform.localRotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
                _muzzleFlashQuad.transform.localScale = Vector3.one * Random.Range(0.09f, 0.14f);
                _muzzleFlashQuad.SetActive(true);
            }

            yield return new WaitForSeconds(0.04f);

            if (_muzzleLight != null) _muzzleLight.enabled = false;
            if (_muzzleFlashQuad != null) _muzzleFlashQuad.SetActive(false);
        }

        private void SetupImpactSparks()
        {
            GameObject sparkObj = new GameObject("WeaponImpactSparks_Pool");
            _impactSparks = sparkObj.AddComponent<ParticleSystem>();

            var main = _impactSparks.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.2f, 0.35f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(4f, 8f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.015f, 0.035f);
            main.startColor = new Color(1f, 0.8f, 0.25f, 1f);
            main.gravityModifier = 4.5f;
            main.playOnAwake = false;

            var emission = _impactSparks.emission;
            emission.rateOverTime = 0;

            var shape = _impactSparks.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 40f;
            shape.radius = 0.02f;

            var renderer = sparkObj.GetComponent<ParticleSystemRenderer>();
            Shader unlitShader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
            renderer.material = new Material(unlitShader);
            renderer.material.color = new Color(1f, 0.85f, 0.3f);
        }

        private void SpawnImpactSparks(Vector3 position, Vector3 normal)
        {
            if (_impactSparks == null) return;
            _impactSparks.transform.position = position;
            _impactSparks.transform.rotation = Quaternion.LookRotation(normal);
            _impactSparks.Emit(Random.Range(8, 14));
        }

        private void SpawnBulletHole(RaycastHit hit)
        {
            if (_bulletHoleMaterial == null)
            {
                _bulletHoleMaterial = CreateBulletHoleMaterial();
            }

            GameObject hole = GameObject.CreatePrimitive(PrimitiveType.Quad);
            hole.name = "BulletHole";
            Destroy(hole.GetComponent<Collider>());

            hole.transform.position = hit.point + (hit.normal * 0.0015f);
            hole.transform.rotation = Quaternion.LookRotation(-hit.normal) * Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
            hole.transform.localScale = Vector3.one * Random.Range(0.045f, 0.065f);
            hole.transform.SetParent(hit.collider.transform);

            MeshRenderer mr = hole.GetComponent<MeshRenderer>();
            mr.material = _bulletHoleMaterial;

            _bulletHolePool.Enqueue(hole);
            if (_bulletHolePool.Count > MaxBulletHoles)
            {
                GameObject oldest = _bulletHolePool.Dequeue();
                if (oldest != null) Destroy(oldest);
            }

            Destroy(hole, 25f);
        }

        private Material CreateBulletHoleMaterial()
        {
            Shader unlitShader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
            Material mat = new Material(unlitShader);
            mat.mainTexture = GenerateHoleTexture();

            // Setup transparency
            mat.SetFloat("_Surface", 1);
            mat.SetFloat("_Blend", 0);
            mat.renderQueue = 3000;
            return mat;
        }

        private Texture2D GenerateHoleTexture()
        {
            int size = 64;
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            float center = size / 2f;
            float radius = size * 0.42f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dist = Vector2.Distance(new Vector2(x, y), new Vector2(center, center));
                    if (dist < radius * 0.45f)
                    {
                        // Core hole: dark black
                        tex.SetPixel(x, y, new Color(0.04f, 0.04f, 0.04f, 0.95f));
                    }
                    else if (dist < radius)
                    {
                        // Burnt rim
                        float t = (dist - radius * 0.45f) / (radius * 0.55f);
                        float a = Mathf.Lerp(0.85f, 0f, t);
                        tex.SetPixel(x, y, new Color(0.12f, 0.1f, 0.08f, a));
                    }
                    else
                    {
                        tex.SetPixel(x, y, Color.clear);
                    }
                }
            }
            tex.Apply();
            return tex;
        }

        private void EjectCasing()
        {
            Transform spawnPoint = ejectionPortTransform != null ? ejectionPortTransform : transform;
            Vector3 spawnPos = spawnPoint.position;

            GameObject casing;
            if (casingPrefab != null)
            {
                casing = Instantiate(casingPrefab, spawnPos, transform.rotation);
            }
            else
            {
                // Procedural brass casing cylinder
                casing = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                casing.name = "BrassCasing";
                casing.transform.position = spawnPos;
                casing.transform.localScale = new Vector3(0.015f, 0.025f, 0.015f);
                Shader litShader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                Material brassMat = new Material(litShader);
                brassMat.color = new Color(0.85f, 0.7f, 0.25f);
                casing.GetComponent<MeshRenderer>().material = brassMat;
            }

            // Pastikan casing memiliki Collider agar bisa bertubrukan dan bertengger di lantai
            Collider casingCol = casing.GetComponentInChildren<Collider>();
            if (casingCol == null)
            {
                // Prefab model seringkali belum memiliki Collider bawaan, jadi kita pasang BoxCollider otomatis
                casingCol = casing.AddComponent<BoxCollider>();
            }

            Rigidbody rb = casing.GetComponent<Rigidbody>();
            if (rb == null) rb = casing.AddComponent<Rigidbody>();
            rb.mass = 0.02f;
            // Gunakan ContinuousDynamic agar benda kecil berkecepatan tinggi tidak menembus lantai (tunneling)
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            // Abaikan tubrukan dengan player controller agar pergerakan tidak terhambat
            CharacterController playerCC = GetComponentInParent<CharacterController>();
            if (casingCol != null && playerCC != null)
            {
                Physics.IgnoreCollision(casingCol, playerCC);
            }

            // Dorongan selongsong ke kanan atas dan sedikit ke belakang
            Vector3 force = (transform.right * Random.Range(2.0f, 3.0f)) +
                            (transform.up * Random.Range(1.2f, 2.0f)) +
                            (-transform.forward * Random.Range(0.2f, 0.6f));

            rb.linearVelocity = force;
            rb.angularVelocity = Random.insideUnitSphere * 25f;

            // Selongsong bertahan di lantai sesuai durasi casingLifetime sebelum dihancurkan
            Destroy(casing, casingLifetime);
        }

        private IEnumerator SpawnTracerRoutine(Vector3 start, Vector3 end)
        {
            GameObject tracerObj = new GameObject("Tracer");
            LineRenderer line = tracerObj.AddComponent<LineRenderer>();

            line.startWidth = 0.02f;
            line.endWidth = 0.01f;
            line.positionCount = 2;
            line.SetPosition(0, start);
            line.SetPosition(1, end);

            // Material visual warna kuning/oranye proyektil
            if (tracerMaterial != null)
            {
                line.material = tracerMaterial;
            }
            else
            {
                line.material = new Material(Shader.Find("Sprites/Default"));
                line.startColor = new Color(1f, 0.85f, 0.4f, 0.8f);
                line.endColor = new Color(1f, 0.5f, 0.1f, 0.2f);
            }

            yield return new WaitForSeconds(tracerDuration);

            if (tracerObj != null)
            {
                Destroy(tracerObj);
            }
        }

        #endregion

        private IEnumerator ReloadRoutine()
        {
            if (ReserveAmmo <= 0 || CurrentAmmo >= magazineSize) yield break;

            IsReloading = true;
            _reloadTimer = 0f;
            BurstBulletIndex = 0;
            CurrentSprayOffset = Vector2.zero;

            if (reloadSound != null)
            {
                _audioSource.pitch = 1.0f;
                _audioSource.PlayOneShot(reloadSound, 0.9f);
            }

            while (_reloadTimer < reloadDuration)
            {
                _reloadTimer += Time.deltaTime;
                yield return null;
            }

            int needed = magazineSize - CurrentAmmo;
            int amountToLoad = Mathf.Min(needed, ReserveAmmo);
            CurrentAmmo += amountToLoad;
            ReserveAmmo -= amountToLoad;

            IsReloading = false;
            _reloadTimer = 0f;
        }

        private void HandleRecovery()
        {
            // Reset recoil spray pattern kembali ke peluru pertama jika pemain menjeda tembakan
            if (Time.time > _nextFireTime + recoilResetDelay)
            {
                BurstBulletIndex = 0;
            }

            // Pulihkan spray offset & bloom secara bertahap saat tidak menembak
            if (Time.time > _nextFireTime)
            {
                CurrentSprayOffset = Vector2.Lerp(CurrentSprayOffset, Vector2.zero, Time.deltaTime * sprayRecoverySpeed);
                _currentSprayBloom = Mathf.MoveTowards(_currentSprayBloom, 0f, sprayRecoverySpeed * Time.deltaTime);
            }

            // Selalu perbarui CurrentSpread secara real-time setiap frame
            float movePenalty = (_movementState != null) ? _movementState.InaccuracyModifier * movementInaccuracyMultiplier : 0f;
            CurrentSpread = baseSpread + _currentSprayBloom + movePenalty;
        }

        private void UpdateViewmodelAnimation()
        {
            // Kembalikan sentakan recoil kembali ke posisi default (0, 0, 0)
            _kickbackOffset = Vector3.Lerp(_kickbackOffset, Vector3.zero, Time.deltaTime * kickbackReturnSpeed);
            transform.localPosition = _originalLocalPos + _kickbackOffset;
        }

        /// <summary>
        /// Mengatur ulang amunisi dan recoil saat pergantian ronde.
        /// </summary>
        public void ResetAmmoAndRecoil()
        {
            StopAllCoroutines();
            IsReloading = false;
            CurrentAmmo = magazineSize;
            ReserveAmmo = maxReserveAmmo;
            BurstBulletIndex = 0;
            _currentSprayBloom = 0f;
            CurrentSprayOffset = Vector2.zero;
            CurrentSpread = baseSpread;
            _kickbackOffset = Vector3.zero;
        }

        /// <summary>
        /// Membersihkan seluruh bekas lubang peluru di arena saat pergantian ronde.
        /// </summary>
        public static void ClearAllBulletHoles()
        {
            while (_bulletHolePool.Count > 0)
            {
                GameObject hole = _bulletHolePool.Dequeue();
                if (hole != null)
                {
                    if (Application.isPlaying) Destroy(hole);
                    else DestroyImmediate(hole);
                }
            }
        }
        /// <summary>
        /// Mengonfigurasi parameter persenjataan secara dinamis (misal untuk pistol).
        /// </summary>
        public void ConfigureWeaponStats(
            string name,
            float damage,
            float rateOfFire,
            int magSize,
            int reserveAmmo,
            float reloadDur,
            float spread,
            float maxSpread,
            float pitchKick,
            float yawKick,
            float kickbackDist,
            float kickbackUpDist)
        {
            weaponName = name;
            baseDamage = damage;
            fireRate = rateOfFire;
            magazineSize = magSize;
            maxReserveAmmo = reserveAmmo;
            reloadDuration = reloadDur;
            baseSpread = spread;
            maxSpraySpread = maxSpread;
            cameraPitchKick = pitchKick;
            cameraYawKick = yawKick;
            kickbackDistance = kickbackDist;
            kickbackUp = kickbackUpDist;

            CurrentAmmo = magazineSize;
            ReserveAmmo = maxReserveAmmo;
            CurrentSpread = baseSpread;
        }

        /// <summary>
        /// Mengatur ulang posisi dasar viewmodel jika dipindahkan secara manual.
        /// </summary>
        public void SetDefaultPosition(Vector3 newLocalPos)
        {
            _originalLocalPos = newLocalPos;
        }

        /// <summary>
        /// Mengambil titik 3D world space arah semprotan recoil saat ini.
        /// Memungkinkan Crosshair Follow Recoil dihitung menggunakan Camera.WorldToScreenPoint
        /// sehingga posisi crosshair 100% presisi matematis dengan arah peluru.
        /// </summary>
        public Vector3 GetCurrentRecoilAimPoint(float distance = 40f)
        {
            Camera cam = playerCamera != null ? playerCamera : Camera.main;
            if (cam == null) return transform.position + transform.forward * distance;

            Ray centerRay = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            Vector3 recoilDir = centerRay.direction 
                + (cam.transform.right * CurrentSprayOffset.x) 
                + (cam.transform.up * CurrentSprayOffset.y);
            recoilDir.Normalize();

            return centerRay.origin + (recoilDir * distance);
        }

        public void FixWeaponMaterials()
        {
            Shader urpShader = Shader.Find("Universal Render Pipeline/Lit");
            if (urpShader == null) return;

            Renderer[] rends = GetComponentsInChildren<Renderer>(true);
            foreach (var r in rends)
            {
                Material[] mats = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    Material m = mats[i];
                    if (m != null && (m.shader == null || m.shader.name == "Standard" || m.shader.name.Contains("Error") || m.shader.name.Contains("InternalErrorShader") || !m.HasProperty("_BaseMap")))
                    {
                        Texture tex = m.HasProperty("_MainTex") ? m.GetTexture("_MainTex") : null;
                        Color col = m.HasProperty("_Color") ? m.color : Color.white;
                        m.shader = urpShader;
                        if (tex != null)
                        {
                            m.SetTexture("_BaseMap", tex);
                            m.SetTexture("_MainTex", tex);
                        }
                        m.SetColor("_BaseColor", col);
                        changed = true;
                    }
                }
                if (changed)
                {
                    r.sharedMaterials = mats;
                }
            }
        }
    }
}
