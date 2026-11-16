using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using FPSFramework.Player;

namespace FPSFramework.Combat
{
    /// <summary>
    /// Proyektil Granat Frag Taktis:
    /// - Menggunakan Rigidbody dengan pantulan fisika realistis.
    /// - Fuse timer 2.2 detik setelah dilepaskan.
    /// - Detonasi: Suara ledakan bass berat, kilatan cahaya, partikel api/asap,
    ///   dan Area of Effect (AoE) damage 6.5m dengan degradasi jarak.
    /// - Mendorong objek Rigidbody di sekitarnya.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class ThrownGrenade : MonoBehaviour
    {
        [Header("Pengaturan Fuse & Ledakan")]
        [SerializeField] private float fuseTime = 2.2f;
        [SerializeField] private float explosionRadius = 6.5f;
        [SerializeField] private float maxDamage = 100f;
        [SerializeField] private float explosionForce = 750f;

        [Header("Audio Ledakan")]
        [SerializeField] private AudioClip explosionSound;

        private Rigidbody _rb;
        private bool _hasExploded = false;
        private static AudioClip _procExplosionClip;

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            if (_rb != null)
            {
                _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                _rb.interpolation = RigidbodyInterpolation.Interpolate;
            }

            Collider col = GetComponent<Collider>();
            if (col == null)
            {
                SphereCollider sc = gameObject.AddComponent<SphereCollider>();
                sc.radius = 0.12f;
            }

            // Atur material pantulan
            PhysicsMaterial bounceMat = new PhysicsMaterial("GrenadeBounce")
            {
                bounciness = 0.45f,
                dynamicFriction = 0.6f,
                staticFriction = 0.6f,
                bounceCombine = PhysicsMaterialCombine.Average
            };
            if (col != null) col.sharedMaterial = bounceMat;

            if (_procExplosionClip == null)
            {
                _procExplosionClip = GenerateProceduralExplosionClip();
            }
        }

        private void Start()
        {
            StartCoroutine(FuseRoutine());
        }

        public void InitializeVelocity(Vector3 velocity, Vector3 angularVelocity)
        {
            if (_rb == null) _rb = GetComponent<Rigidbody>();
            _rb.linearVelocity = velocity;
            _rb.angularVelocity = angularVelocity;
        }

        private IEnumerator FuseRoutine()
        {
            yield return new WaitForSeconds(fuseTime);
            Explode();
        }

        private void Explode()
        {
            if (_hasExploded) return;
            _hasExploded = true;

            Vector3 blastCenter = transform.position;

            // 1. Audio Ledakan Spatial 3D
            PlayExplosionSound(blastCenter);

            // 2. Visual Ledakan (Light Flash, Fireball, Sparks, Shockwave)
            SpawnExplosionVFX(blastCenter);

            // 3. AoE Damage Calculation
            Collider[] hitColliders = Physics.OverlapSphere(blastCenter, explosionRadius);
            HashSet<GameObject> damagedRoots = new HashSet<GameObject>();

            foreach (var hitCol in hitColliders)
            {
                if (hitCol.isTrigger) continue;

                GameObject targetRoot = hitCol.transform.root.gameObject;
                if (damagedRoots.Contains(targetRoot)) continue;

                // Cek Line of Sight agar granat tidak menembus dinding tebal
                Vector3 targetCenter = hitCol.bounds.center;
                Vector3 dir = (targetCenter - blastCenter).normalized;
                float dist = Vector3.Distance(blastCenter, targetCenter);

                if (Physics.Raycast(blastCenter, dir, out RaycastHit hit, dist, LayerMask.GetMask("Default", "Ground", "Wall")))
                {
                    if (hit.collider != hitCol && !hit.transform.IsChildOf(targetRoot.transform))
                    {
                        // Terhalang dinding solid
                        continue;
                    }
                }

                IDamageable damageable = hitCol.GetComponentInParent<IDamageable>();
                if (damageable != null)
                {
                    float distanceRatio = Mathf.Clamp01(dist / explosionRadius);
                    // Damage falloff: 100% di pusat, 20% di tepi lingkaran
                    float damageToApply = Mathf.Lerp(maxDamage, maxDamage * 0.2f, distanceRatio);

                    damageable.TakeDamage(damageToApply, HitboxType.Body, targetCenter, dir);
                    damagedRoots.Add(targetRoot);
                }

                // Dorongan fisika Rigidbody (barel, debris)
                Rigidbody hitRb = hitCol.GetComponentInParent<Rigidbody>();
                if (hitRb != null && hitRb != _rb)
                {
                    hitRb.AddExplosionForce(explosionForce, blastCenter, explosionRadius, 1.0f, ForceMode.Impulse);
                }
            }

            Destroy(gameObject);
        }

        private void PlayExplosionSound(Vector3 pos)
        {
            GameObject soundObj = new GameObject("Grenade_Explosion_Audio");
            soundObj.transform.position = pos;

            AudioSource src = soundObj.AddComponent<AudioSource>();
            src.clip = explosionSound != null ? explosionSound : _procExplosionClip;
            src.spatialBlend = 1.0f; // 3D Spatial
            src.minDistance = 5f;
            src.maxDistance = 55f;
            src.rolloffMode = AudioRolloffMode.Logarithmic;
            src.volume = 1.0f;
            src.Play();

            Destroy(soundObj, 3.5f);
        }

        private void SpawnExplosionVFX(Vector3 pos)
        {
            GameObject vfxObj = new GameObject("Grenade_Explosion_VFX");
            vfxObj.transform.position = pos;

            // Flash Light dinamis
            Light flashLight = vfxObj.AddComponent<Light>();
            flashLight.type = LightType.Point;
            flashLight.color = new Color(1.0f, 0.65f, 0.25f);
            flashLight.range = 14f;
            flashLight.intensity = 8f;

            // Flash Sphere oranye mengembang cepat
            GameObject flashSphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            flashSphere.transform.SetParent(vfxObj.transform, false);
            flashSphere.transform.localScale = Vector3.one * 1.2f;
            Destroy(flashSphere.GetComponent<Collider>());

            Shader s = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Standard");
            Material glowMat = new Material(s);
            glowMat.color = new Color(1.0f, 0.5f, 0.1f, 1.0f);
            flashSphere.GetComponent<MeshRenderer>().sharedMaterial = glowMat;

            // Bekas hangus (Scorch Decal) di lantai jika dekat tanah
            if (Physics.Raycast(pos, Vector3.down, out RaycastHit groundHit, 3.0f))
            {
                GameObject scorch = GameObject.CreatePrimitive(PrimitiveType.Quad);
                scorch.name = "Grenade_Scorch_Decal";
                scorch.transform.position = groundHit.point + groundHit.normal * 0.02f;
                scorch.transform.rotation = Quaternion.LookRotation(groundHit.normal);
                scorch.transform.localScale = Vector3.one * 3.2f;
                Destroy(scorch.GetComponent<Collider>());

                Material scorchMat = new Material(s);
                scorchMat.color = new Color(0.08f, 0.08f, 0.08f, 0.85f);
                scorch.GetComponent<MeshRenderer>().sharedMaterial = scorchMat;
                Destroy(scorch, 25f);
            }

            // Animasi membesar dan redup dalam coroutine terpisah
            IEnumerator FadeRoutine()
            {
                float t = 0f;
                float duration = 0.45f;
                Vector3 initialScale = flashSphere.transform.localScale;
                Vector3 targetScale = Vector3.one * 4.5f;

                while (t < duration)
                {
                    t += Time.deltaTime;
                    float progress = t / duration;

                    if (flashLight != null)
                    {
                        flashLight.intensity = Mathf.Lerp(8f, 0f, progress);
                    }

                    if (flashSphere != null)
                    {
                        flashSphere.transform.localScale = Vector3.Lerp(initialScale, targetScale, Mathf.Sqrt(progress));
                        Color c = glowMat.color;
                        c.a = Mathf.Lerp(1.0f, 0f, progress);
                        glowMat.color = c;
                    }

                    yield return null;
                }

                Destroy(vfxObj);
            }

            StaticCoroutineRunner.StartStaticCoroutine(FadeRoutine());
        }

        private static AudioClip GenerateProceduralExplosionClip()
        {
            int sampleRate = 44100;
            float duration = 1.8f;
            int totalSamples = Mathf.FloorToInt(sampleRate * duration);
            float[] samples = new float[totalSamples];

            for (int i = 0; i < totalSamples; i++)
            {
                float time = (float)i / sampleRate;
                float envelope = Mathf.Exp(-time * 3.5f);

                // Dentuman bass rendah 55Hz - 110Hz + white noise gemuruh
                float subBass = Mathf.Sin(2f * Mathf.PI * (55f - 20f * Mathf.Clamp01(time)) * time);
                float punch = Mathf.Sin(2f * Mathf.PI * 110f * time) * Mathf.Exp(-time * 12f);
                float noise = (Random.value * 2f - 1f) * Mathf.Exp(-time * 4.0f);

                float combined = (subBass * 0.6f + punch * 0.4f + noise * 0.45f) * envelope;
                samples[i] = Mathf.Clamp(combined, -1f, 1f);
            }

            AudioClip clip = AudioClip.Create("Proc_Explosion_Bass", totalSamples, 1, sampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }
    }

    /// <summary>
    /// Runner helper untuk menjalankan coroutine visual efek ledakan yang berdurasi singkat.
    /// </summary>
    public class StaticCoroutineRunner : MonoBehaviour
    {
        private static StaticCoroutineRunner _instance;

        public static void StartStaticCoroutine(IEnumerator routine)
        {
            if (_instance == null)
            {
                GameObject obj = new GameObject("StaticCoroutineRunner");
                _instance = obj.AddComponent<StaticCoroutineRunner>();
                DontDestroyOnLoad(obj);
            }
            _instance.StartCoroutine(routine);
        }
    }
}
