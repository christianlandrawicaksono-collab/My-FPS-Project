using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using FPSFramework.Combat;

namespace FPSFramework.Player
{
    /// <summary>
    /// Kontroler Senjata Jarak Dekat (Melee Combat Knife) ala Tactical Shooter (CS / Valorant):
    /// 1. Dual-Attack Mechanics:
    ///    - Klik Kiri (Quick Slash): Ayunan cepat, jeda 0.38s, 40 DMG (55 DMG Head/Back).
    ///    - Klik Kanan (Heavy Stab): Tusukan berat, jeda 0.85s, 65 DMG frontal, 120 DMG Backstab (Instakill).
    /// 2. Deteksi Arah Serangan (Backstab Detection):
    ///    - Membandingkan arah hadap korban dengan arah tebasan pisau (Dot Product > 0.45).
    /// 3. Animasi Viewmodel Prosedural:
    ///    - Animasi tebasan horizontal melengkung dan tusukan lurus dinamis tanpa rigging tambahan.
    /// 4. Audio Feedback Realistis:
    ///    - Equip foley logam, tebasan udara (whoosh), benturan daging (flesh hit), dan benturan dinding (impact spark).
    /// 5. Integrasi Penuh ke Sistem Tempur:
    ///    - Memanggil FPSHitbox, IDamageable, event damage HUD floating numbers, dan dorongan ragdoll.
    /// </summary>
    public class FPSKnife : MonoBehaviour
    {
        [Header("Pengaturan Serangan (Combat Stats)")]
        [Tooltip("Damage tebasan cepat klik kiri.")]
        [SerializeField] private float slashDamage = 40f;
        [Tooltip("Jeda waktu antar tebasan cepat (detik).")]
        [SerializeField] private float slashRate = 0.38f;
        [Tooltip("Jarak jangkauan tebasan cepat (meter).")]
        [SerializeField] private float slashRange = 2.2f;

        [Tooltip("Damage tusukan berat klik kanan.")]
        [SerializeField] private float stabDamage = 65f;
        [Tooltip("Damage tusukan dari belakang punggung musuh (Insta-kill).")]
        [SerializeField] private float backstabDamage = 120f;
        [Tooltip("Jeda waktu antar tusukan berat (detik).")]
        [SerializeField] private float stabRate = 0.85f;
        [Tooltip("Jarak jangkauan tusukan berat (meter).")]
        [SerializeField] private float stabRange = 1.85f;

        [Header("Audio SFX")]
        [SerializeField] private AudioClip equipSound;
        [SerializeField] private AudioClip slashSound;
        [SerializeField] private AudioClip stabSound;
        [SerializeField] private AudioClip fleshHitSound;
        [SerializeField] private AudioClip worldHitSound;

        [Header("Status")]
        public bool AttackLocked = false;
        public bool IsAttacking { get; private set; } = false;

        private Camera _playerCamera;
        private AudioSource _audioSource;
        private LayerMask _hitLayers;
        private float _nextAttackTime;

        // Viewmodel procedural animation state
        private Vector3 _defaultLocalPos;
        private Quaternion _defaultLocalRot;
        private Coroutine _activeAnimRoutine;

        private static AudioClip _procSlashClip;
        private static AudioClip _procStabClip;

        private void Awake()
        {
            _playerCamera = Camera.main;
            if (_playerCamera == null)
            {
                _playerCamera = GetComponentInParent<Camera>();
            }

            _audioSource = GetComponent<AudioSource>();
            if (_audioSource == null)
            {
                _audioSource = gameObject.AddComponent<AudioSource>();
            }
            _audioSource.playOnAwake = false;
            _audioSource.spatialBlend = 0f;

            // Zero-Damage Guard: Kecualikan layer Ignore Raycast dan layer Viewmodel
            int vmLayer = LayerMask.NameToLayer("Viewmodel");
            if (vmLayer != -1)
            {
                _hitLayers = ~(LayerMask.GetMask("Ignore Raycast") | (1 << vmLayer));
            }
            else
            {
                _hitLayers = ~LayerMask.GetMask("Ignore Raycast");
            }

            _defaultLocalPos = transform.localPosition;
            _defaultLocalRot = transform.localRotation;

            if (_procSlashClip == null)
            {
                _procSlashClip = GenerateSwooshClip(0.18f, 750f, 220f);
            }
            if (_procStabClip == null)
            {
                _procStabClip = GenerateSwooshClip(0.24f, 520f, 140f);
            }
        }

        private void Start()
        {
#if UNITY_EDITOR
            if (equipSound == null)
            {
                equipSound = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/FreeWeaponSFX_v1.1/Handgun/Foley/sfx_wpn_hgn_foley_equip_02.wav");
            }
            if (fleshHitSound == null)
            {
                fleshHitSound = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Sound/hit-flesh.wav");
            }
            if (worldHitSound == null)
            {
                worldHitSound = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Sound/Bullet_impact_2.wav");
            }
#endif
        }

        private void OnEnable()
        {
            transform.localPosition = _defaultLocalPos;
            transform.localRotation = _defaultLocalRot;
            PlayEquipSound();
        }

        private void Update()
        {
            if (AttackLocked) return;

            if (Mouse.current == null) return;

            // Klik Kiri: Quick Slash
            if (Mouse.current.leftButton.wasPressedThisFrame && Time.time >= _nextAttackTime)
            {
                PerformSlash();
            }
            // Klik Kanan: Heavy Stab
            else if (Mouse.current.rightButton.wasPressedThisFrame && Time.time >= _nextAttackTime)
            {
                PerformStab();
            }
        }

        public void PlayEquipSound()
        {
            if (equipSound != null && _audioSource != null)
            {
                _audioSource.pitch = Random.Range(0.97f, 1.03f);
                _audioSource.PlayOneShot(equipSound, 0.85f);
            }
        }

        private void PerformSlash()
        {
            _nextAttackTime = Time.time + slashRate;
            AudioClip clip = slashSound != null ? slashSound : _procSlashClip;
            if (clip != null && _audioSource != null)
            {
                _audioSource.pitch = Random.Range(1.05f, 1.15f);
                _audioSource.PlayOneShot(clip, 0.85f);
            }

            if (_activeAnimRoutine != null) StopCoroutine(_activeAnimRoutine);
            _activeAnimRoutine = StartCoroutine(SlashAnimationRoutine());

            ExecuteMeleeHitscan(isHeavyStab: false, slashRange, slashDamage);
        }

        private void PerformStab()
        {
            _nextAttackTime = Time.time + stabRate;
            AudioClip clip = stabSound != null ? stabSound : _procStabClip;
            if (clip != null && _audioSource != null)
            {
                _audioSource.pitch = Random.Range(0.92f, 1.02f);
                _audioSource.PlayOneShot(clip, 0.95f);
            }

            if (_activeAnimRoutine != null) StopCoroutine(_activeAnimRoutine);
            _activeAnimRoutine = StartCoroutine(StabAnimationRoutine());

            ExecuteMeleeHitscan(isHeavyStab: true, stabRange, stabDamage);
        }

        private void ExecuteMeleeHitscan(bool isHeavyStab, float range, float baseDmg)
        {
            Camera cam = _playerCamera != null ? _playerCamera : Camera.main;
            if (cam == null) return;

            Ray ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            Vector3 attackDir = ray.direction;

            // Gunakan SphereCast tipis (radius 0.18m) agar serangan pisau terasa responsif dan mudah mengenai musuh
            RaycastHit hit;
            bool didHit = Physics.SphereCast(ray.origin, 0.18f, attackDir, out hit, range, _hitLayers);

            if (!didHit)
            {
                // Fallback ke Raycast tajam jika spherecast tidak membentur
                didHit = Physics.Raycast(ray.origin, attackDir, out hit, range, _hitLayers);
            }

            if (didHit)
            {
                // Cek Hitbox musuh
                FPSHitbox hitbox = hit.collider.GetComponent<FPSHitbox>() ?? hit.collider.GetComponentInParent<FPSHitbox>();
                IDamageable damageable = hitbox != null ? null : hit.collider.GetComponentInParent<IDamageable>();
                if (hitbox == null && damageable == null && hit.collider.transform.root != null)
                {
                    damageable = hit.collider.transform.root.GetComponentInChildren<IDamageable>();
                }

                if (hitbox != null || damageable != null)
                {
                    // Deteksi Backstab: Bandingkan arah serangan dengan arah hadap korban
                    Transform victimRoot = hit.collider.transform.root;
                    Vector3 victimForward = victimRoot.forward;
                    victimForward.y = 0f;
                    victimForward.Normalize();

                    Vector3 attackFlat = attackDir;
                    attackFlat.y = 0f;
                    attackFlat.Normalize();

                    float backDot = Vector3.Dot(victimForward, attackFlat);
                    bool isBackstab = backDot > 0.42f; // Menyerang dari belakang/punggung korban

                    float finalDamage = baseDmg;
                    bool isCrit = false;

                    if (isHeavyStab)
                    {
                        finalDamage = isBackstab ? backstabDamage : stabDamage;
                        isCrit = isBackstab;
                    }
                    else
                    {
                        // Quick slash dari belakang memberikan bonus damage
                        finalDamage = isBackstab ? (baseDmg * 1.4f) : baseDmg;
                        isCrit = isBackstab || (hitbox != null && hitbox.HitboxType == HitboxType.Head);
                    }

                    if (hitbox != null)
                    {
                        hitbox.ReceiveHit(finalDamage, hit.point, attackDir);
                    }
                    else if (damageable != null)
                    {
                        damageable.TakeDamage(finalDamage, HitboxType.Body, hit.point, attackDir);
                    }

                    // Audio feedback benturan daging
                    if (fleshHitSound != null && _audioSource != null)
                    {
                        _audioSource.pitch = isCrit ? 1.15f : Random.Range(0.95f, 1.05f);
                        _audioSource.PlayOneShot(fleshHitSound, 1.0f);
                    }

                    // Picu event global agar floating damage numbers & hitmarker muncul di HUD
                    FPSWeapon.TriggerGlobalDamageFeedback(finalDamage, isCrit, hit.point);

                    // Berikan dorongan fisika jika mengenai ragdoll
                    HumanoidRagdollController ragdoll = hit.collider.GetComponentInParent<HumanoidRagdollController>();
                    if (ragdoll != null)
                    {
                        ragdoll.ApplyExplosion(hit.point, 400f, 1.5f, 0.8f);
                    }
                }
                else
                {
                    // Benturan dinding / lantai / obstacle
                    if (worldHitSound != null)
                    {
                        AudioSource.PlayClipAtPoint(worldHitSound, hit.point, 0.7f);
                    }
                }
            }
        }

        #region Animasi Prosedural Viewmodel Pisau

        private IEnumerator SlashAnimationRoutine()
        {
            IsAttacking = true;
            float duration = 0.28f;
            float elapsed = 0f;

            // Gerakan ayunan tebas: mundur, ayun tajam ke kiri-bawah, lalu kembali
            Vector3 slashOffset = new Vector3(-0.16f, -0.06f, 0.12f);
            Quaternion slashRot = Quaternion.Euler(20f, -45f, 35f);

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / duration;

                if (t < 0.35f)
                {
                    // Fase ayunan cepat ke depan-samping
                    float forwardT = t / 0.35f;
                    transform.localPosition = Vector3.Lerp(_defaultLocalPos, _defaultLocalPos + slashOffset, forwardT);
                    transform.localRotation = Quaternion.Slerp(_defaultLocalRot, _defaultLocalRot * slashRot, forwardT);
                }
                else
                {
                    // Fase pemulihan kembali ke posisi siaga
                    float recoverT = (t - 0.35f) / 0.65f;
                    transform.localPosition = Vector3.Lerp(_defaultLocalPos + slashOffset, _defaultLocalPos, recoverT);
                    transform.localRotation = Quaternion.Slerp(_defaultLocalRot * slashRot, _defaultLocalRot, recoverT);
                }

                yield return null;
            }

            transform.localPosition = _defaultLocalPos;
            transform.localRotation = _defaultLocalRot;
            IsAttacking = false;
        }

        private IEnumerator StabAnimationRoutine()
        {
            IsAttacking = true;
            float duration = 0.42f;
            float elapsed = 0f;

            // Gerakan tusukan tajam ke depan sumbu Z dengan rotasi menusuk
            Vector3 stabOffset = new Vector3(0.04f, 0.05f, 0.28f);
            Quaternion stabRot = Quaternion.Euler(-15f, 10f, -20f);

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / duration;

                if (t < 0.28f)
                {
                    // Tusuk cepat ke depan
                    float thrustT = t / 0.28f;
                    transform.localPosition = Vector3.Lerp(_defaultLocalPos, _defaultLocalPos + stabOffset, thrustT);
                    transform.localRotation = Quaternion.Slerp(_defaultLocalRot, _defaultLocalRot * stabRot, thrustT);
                }
                else
                {
                    // Tarik kembali ke posisi siaga
                    float pullT = (t - 0.28f) / 0.72f;
                    transform.localPosition = Vector3.Lerp(_defaultLocalPos + stabOffset, _defaultLocalPos, pullT);
                    transform.localRotation = Quaternion.Slerp(_defaultLocalRot * stabRot, _defaultLocalRot, pullT);
                }

                yield return null;
            }

            transform.localPosition = _defaultLocalPos;
            transform.localRotation = _defaultLocalRot;
            IsAttacking = false;
        }

        public void SetDefaultPosition(Vector3 newLocalPos)
        {
            _defaultLocalPos = newLocalPos;
            transform.localPosition = newLocalPos;
        }

        #endregion

        #region Procedural Audio Synthesis Helper

        private static AudioClip GenerateSwooshClip(float duration, float startFreq, float endFreq)
        {
            int sampleRate = 44100;
            int totalSamples = Mathf.FloorToInt(sampleRate * duration);
            float[] samples = new float[totalSamples];

            for (int i = 0; i < totalSamples; i++)
            {
                float time = (float)i / sampleRate;
                float progress = time / duration;
                float envelope = Mathf.Sin(progress * Mathf.PI);

                // Frekuensi desiran angin meluncur
                float currentFreq = Mathf.Lerp(startFreq, endFreq, progress);
                float wave = Mathf.Sin(2f * Mathf.PI * currentFreq * time);
                float noise = (Random.value * 2f - 1f) * 0.35f;

                samples[i] = Mathf.Clamp((wave * 0.65f + noise) * envelope * 0.75f, -1f, 1f);
            }

            AudioClip clip = AudioClip.Create("Proc_Knife_Swoosh", totalSamples, 1, sampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        #endregion
    }
}
