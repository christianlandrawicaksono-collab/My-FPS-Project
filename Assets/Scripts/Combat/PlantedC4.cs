using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using FPSFramework.Player;

namespace FPSFramework.Combat
{
    /// <summary>
    /// Objek C4 yang telah ditanam di Site A atau Site B:
    /// - Menggunakan aset 3D Dynamite_Multi dengan lampu indikator merah berkedip.
    /// - Menghitung mundur 45 detik dengan frekuensi audio bip yang semakin cepat.
    /// - Pemain CT dapat menjinakkan (Defuse) dengan menahan tombol [E] selama 5.0 detik.
    /// - Jika waktu habis (0 detik), memicu detonasi masif 24 meter yang memenangkan pihak T.
    /// </summary>
    public class PlantedC4 : MonoBehaviour
    {
        public static PlantedC4 ActiveC4 { get; private set; }

        [Header("Pengaturan Detonasi & Waktu")]
        [SerializeField] private float bombDuration = 45f;
        [SerializeField] private float defuseTimeRequired = 5.0f;
        [SerializeField] private float defuseRange = 2.4f;
        [SerializeField] private float explosionRadius = 24f;
        [SerializeField] private float explosionDamage = 400f;

        // State C4
        public float RemainingTime { get; private set; }
        public string SiteName { get; private set; } = "Site A";
        public bool IsDefused { get; private set; } = false;
        public bool HasDetonated { get; private set; } = false;
        public float CurrentDefuseProgress { get; private set; } = 0f;
        public bool IsBeingDefused { get; private set; } = false;
        public float RequiredDefuseTime => (FPSEconomyManager.Instance != null && FPSEconomyManager.Instance.HasDefuseKit) ? 5.0f : 10.0f;

        // Static Events
        public static event System.Action<PlantedC4, string> OnAnyC4Planted;
        public static event System.Action<PlantedC4> OnAnyC4Defused;
        public static event System.Action<PlantedC4> OnAnyC4Exploded;
        public static event System.Action<float, float> OnAnyDefuseProgress; // current, required

        // Audio & Visual Helpers
        private AudioSource _audioSource;
        private Light _indicatorLight;
        private AudioClip _beepClip;
        private AudioClip _defuseClickClip;
        private float _nextBeepTime = 0f;
        private Transform _playerTransform;

        public static PlantedC4 SpawnPlantedC4(Vector3 position, Quaternion rotation, string site)
        {
            GameObject c4Root = new GameObject("Planted_C4_Dynamite");
            c4Root.transform.position = position;
            c4Root.transform.rotation = rotation;

            // Instansiasi model 3D Dynamite_Multi jika tersedia
            GameObject fbxModel = null;
#if UNITY_EDITOR
            fbxModel = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Stylized Explosives/Prefabs/Dynamite_Multi.prefab");
            if (fbxModel == null)
            {
                fbxModel = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Stylized Explosives/Models/Dynamite_Multi.fbx");
            }
#endif
            if (fbxModel != null)
            {
                GameObject meshObj = Instantiate(fbxModel, c4Root.transform);
                meshObj.name = "Dynamite_Mesh";
                meshObj.transform.localPosition = Vector3.zero;
                meshObj.transform.localRotation = Quaternion.identity;
                meshObj.transform.localScale = Vector3.one * 1.0f;
            }
            else
            {
                // Fallback procedural visual bundle dinamit
                GameObject bundle = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                bundle.name = "Dynamite_Fallback";
                bundle.transform.SetParent(c4Root.transform, false);
                bundle.transform.localScale = new Vector3(0.35f, 0.15f, 0.35f);
                bundle.transform.localPosition = new Vector3(0f, 0.15f, 0f);
                SafeDestroy(bundle.GetComponent<Collider>());

                Shader s = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                Material mat = new Material(s) { color = new Color(0.85f, 0.18f, 0.18f) };
                bundle.GetComponent<MeshRenderer>().sharedMaterial = mat;
            }

            // Collider untuk raycast & deteksi
            BoxCollider box = c4Root.AddComponent<BoxCollider>();
            box.size = new Vector3(0.6f, 0.4f, 0.6f);
            box.center = new Vector3(0f, 0.2f, 0f);

            PlantedC4 c4Comp = c4Root.AddComponent<PlantedC4>();
            c4Comp.SiteName = site;

            FPSWeaponManager.FixRenderersMaterials(c4Root);

            return c4Comp;
        }

        /// <summary>
        /// Membersihkan seketika seluruh C4 tertanam dari arena dan mengosongkan referensi statis ActiveC4.
        /// </summary>
        public static void ClearAllPlantedC4()
        {
            ActiveC4 = null;
            PlantedC4[] allC4 = FindObjectsByType<PlantedC4>();
            foreach (var c in allC4)
            {
                if (c != null)
                {
                    c.name = "Planted_C4_Destroyed";
                    if (Application.isPlaying) Destroy(c.gameObject);
                    else DestroyImmediate(c.gameObject);
                }
            }
        }

        private void Awake()
        {
            ActiveC4 = this;
            RemainingTime = bombDuration;

            _audioSource = gameObject.AddComponent<AudioSource>();
            _audioSource.spatialBlend = 1.0f;
            _audioSource.minDistance = 3.0f;
            _audioSource.maxDistance = 50.0f;
            _audioSource.rolloffMode = AudioRolloffMode.Logarithmic;

            _indicatorLight = gameObject.AddComponent<Light>();
            _indicatorLight.type = LightType.Point;
            _indicatorLight.color = Color.red;
            _indicatorLight.range = 3.5f;
            _indicatorLight.intensity = 0f;

            _beepClip = GenerateProceduralBeepClip(950f, 0.08f);
            _defuseClickClip = GenerateProceduralDefuseClickClip();
        }

        private void Start()
        {
            GameObject playerObj = GameObject.Find("Player_Capsule");
            if (playerObj != null) _playerTransform = playerObj.transform;

            OnAnyC4Planted?.Invoke(this, SiteName);
        }

        private void Update()
        {
            if (IsDefused || HasDetonated) return;

            RemainingTime -= Time.deltaTime;

            // 1. Logika Audio Bip & Lampu LED Merah Kedip
            HandleBeepAndLight();

            // 2. Logika Defuse Pemain CT (Tahan [E])
            HandlePlayerDefuseInput();

            // 3. Cek Detonasi (Waktu Habis)
            if (RemainingTime <= 0f)
            {
                RemainingTime = 0f;
                Detonate();
            }
        }

        private void HandleBeepAndLight()
        {
            if (Time.time >= _nextBeepTime)
            {
                // Frekuensi bip:
                // 45s - 30s: tiap 1.0 detik
                // 30s - 15s: tiap 0.5 detik
                // 15s - 6s:  tiap 0.25 detik
                // 6s - 0s:   tiap 0.12 detik (Panic beep)
                float interval;
                if (RemainingTime > 30f) interval = 1.0f;
                else if (RemainingTime > 15f) interval = 0.5f;
                else if (RemainingTime > 6f) interval = 0.25f;
                else interval = 0.12f;

                _nextBeepTime = Time.time + interval;

                if (_beepClip != null && _audioSource != null)
                {
                    _audioSource.pitch = Mathf.Lerp(1.35f, 1.0f, RemainingTime / bombDuration);
                    _audioSource.PlayOneShot(_beepClip, 0.85f);
                }

                StartCoroutine(FlashIndicatorRoutine(0.09f));
            }
        }

        private IEnumerator FlashIndicatorRoutine(float duration)
        {
            if (_indicatorLight != null) _indicatorLight.intensity = 5.0f;
            yield return new WaitForSeconds(duration);
            if (_indicatorLight != null) _indicatorLight.intensity = 0f;
        }

        private void HandlePlayerDefuseInput()
        {
            if (_playerTransform == null)
            {
                GameObject p = GameObject.Find("Player_Capsule");
                if (p != null) _playerTransform = p.transform;
                if (_playerTransform == null) return;
            }

            float dist = Vector3.Distance(transform.position, _playerTransform.position);
            bool isNearby = dist <= defuseRange;

            // Tahan tombol E untuk defuse via New Input System
            bool isPressingE = (Keyboard.current != null && Keyboard.current.eKey.isPressed);

            // Deteksi kepemilikan Defuse Kit (CS2 / Valorant rule: 5s with kit, 10s without kit)
            float requiredDefuseTime = RequiredDefuseTime;

            if (isNearby && isPressingE)
            {
                IsBeingDefused = true;
                CurrentDefuseProgress += Time.deltaTime;

                if (Time.frameCount % 18 == 0 && _defuseClickClip != null)
                {
                    _audioSource.PlayOneShot(_defuseClickClip, 0.5f);
                }

                OnAnyDefuseProgress?.Invoke(CurrentDefuseProgress, requiredDefuseTime);

                if (CurrentDefuseProgress >= requiredDefuseTime)
                {
                    CompleteDefusal();
                }
            }
            else
            {
                if (IsBeingDefused)
                {
                    IsBeingDefused = false;
                    CurrentDefuseProgress = 0f;
                    OnAnyDefuseProgress?.Invoke(0f, requiredDefuseTime);
                }
            }
        }

        public void StepBotDefuse(float deltaTime)
        {
            if (IsDefused || HasDetonated) return;

            IsBeingDefused = true;
            CurrentDefuseProgress += deltaTime;
            OnAnyDefuseProgress?.Invoke(CurrentDefuseProgress, defuseTimeRequired);

            if (CurrentDefuseProgress >= defuseTimeRequired)
            {
                CompleteDefusal();
            }
        }

        private void CompleteDefusal()
        {
            if (IsDefused || HasDetonated) return;
            IsDefused = true;
            IsBeingDefused = false;

            if (_indicatorLight != null)
            {
                _indicatorLight.color = Color.cyan;
                _indicatorLight.intensity = 3.0f;
            }

            Debug.Log("[PlantedC4] Bomb has been defused! CT Wins the round.");
            OnAnyC4Defused?.Invoke(this);
        }

        private void Detonate()
        {
            if (HasDetonated || IsDefused) return;
            HasDetonated = true;

            Vector3 blastCenter = transform.position;

            // 1. Suara Ledakan Raksasa
            AudioClip giantExplosionClip = GenerateGiantExplosionClip();
            GameObject soundObj = new GameObject("C4_MegaBlast_Audio");
            soundObj.transform.position = blastCenter;
            AudioSource src = soundObj.AddComponent<AudioSource>();
            src.clip = giantExplosionClip;
            src.spatialBlend = 0.5f;
            src.volume = 1.0f;
            src.Play();
            Destroy(soundObj, 5.0f);

            // 2. Visual Ledakan Raksasa
            SpawnMegaBlastVFX(blastCenter);

            // 3. AoE Damage Mematikan (24 meter)
            Collider[] colliders = Physics.OverlapSphere(blastCenter, explosionRadius);
            HashSet<GameObject> roots = new HashSet<GameObject>();

            foreach (var col in colliders)
            {
                if (col.isTrigger) continue;
                GameObject rootObj = col.transform.root.gameObject;
                if (roots.Contains(rootObj)) continue;

                IDamageable d = col.GetComponentInParent<IDamageable>();
                if (d != null)
                {
                    Vector3 targetCenter = col.bounds.center;
                    Vector3 dir = (targetCenter - blastCenter).normalized;
                    float dist = Vector3.Distance(blastCenter, targetCenter);
                    float ratio = Mathf.Clamp01(dist / explosionRadius);
                    float dmg = Mathf.Lerp(explosionDamage, 80f, ratio);
                    d.TakeDamage(dmg, HitboxType.Body, targetCenter, dir);
                    roots.Add(rootObj);

                    HumanoidRagdollController ragdoll = col.GetComponentInParent<HumanoidRagdollController>();
                    if (ragdoll != null)
                    {
                        ragdoll.ApplyExplosion(blastCenter, 2400f, explosionRadius, 1.8f);
                    }
                }

                Rigidbody rb = col.GetComponentInParent<Rigidbody>();
                if (rb != null)
                {
                    rb.AddExplosionForce(1800f, blastCenter, explosionRadius, 1.5f, ForceMode.Impulse);
                }
            }

            Debug.Log("[PlantedC4] Target destroyed! C4 Detonated! T Wins the round.");
            OnAnyC4Exploded?.Invoke(this);

            // Sembunyikan model dan hancurkan
            Destroy(gameObject, 0.2f);
        }

        private void SpawnMegaBlastVFX(Vector3 pos)
        {
            GameObject blastObj = new GameObject("C4_MegaBlast_VFX");
            blastObj.transform.position = pos;

            Light blastLight = blastObj.AddComponent<Light>();
            blastLight.type = LightType.Point;
            blastLight.color = new Color(1.0f, 0.45f, 0.15f);
            blastLight.range = 35f;
            blastLight.intensity = 15f;

            GameObject fireball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            fireball.transform.SetParent(blastObj.transform, false);
            fireball.transform.localScale = Vector3.one * 3f;
            Destroy(fireball.GetComponent<Collider>());

            Shader s = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Standard");
            Material mat = new Material(s) { color = new Color(1.0f, 0.4f, 0.05f, 1.0f) };
            fireball.GetComponent<MeshRenderer>().sharedMaterial = mat;

            IEnumerator MegaBlastRoutine()
            {
                float t = 0f;
                float dur = 0.85f;
                while (t < dur)
                {
                    t += Time.deltaTime;
                    float p = t / dur;
                    if (blastLight != null) blastLight.intensity = Mathf.Lerp(15f, 0f, p);
                    if (fireball != null)
                    {
                        fireball.transform.localScale = Vector3.Lerp(Vector3.one * 3f, Vector3.one * 14f, Mathf.Sqrt(p));
                        Color c = mat.color;
                        c.a = Mathf.Lerp(1.0f, 0f, p);
                        mat.color = c;
                    }
                    yield return null;
                }
                Destroy(blastObj);
            }

            StaticCoroutineRunner.StartStaticCoroutine(MegaBlastRoutine());
        }

        private void OnDestroy()
        {
            if (ActiveC4 == this) ActiveC4 = null;
        }

        private static AudioClip GenerateProceduralBeepClip(float frequency, float duration)
        {
            int sampleRate = 44100;
            int totalSamples = Mathf.FloorToInt(sampleRate * duration);
            float[] samples = new float[totalSamples];

            for (int i = 0; i < totalSamples; i++)
            {
                float t = (float)i / sampleRate;
                float envelope = Mathf.Sin(Mathf.PI * (t / duration));
                samples[i] = Mathf.Sin(2f * Mathf.PI * frequency * t) * envelope * 0.7f;
            }

            AudioClip clip = AudioClip.Create("Proc_C4_Beep", totalSamples, 1, sampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        private static AudioClip GenerateProceduralDefuseClickClip()
        {
            int sampleRate = 44100;
            float duration = 0.05f;
            int totalSamples = Mathf.FloorToInt(sampleRate * duration);
            float[] samples = new float[totalSamples];

            for (int i = 0; i < totalSamples; i++)
            {
                float t = (float)i / sampleRate;
                samples[i] = (Random.value * 2f - 1f) * Mathf.Exp(-t * 80f) * 0.5f;
            }

            AudioClip clip = AudioClip.Create("Proc_Defuse_Click", totalSamples, 1, sampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        private static AudioClip GenerateGiantExplosionClip()
        {
            int sampleRate = 44100;
            float duration = 3.5f;
            int totalSamples = Mathf.FloorToInt(sampleRate * duration);
            float[] samples = new float[totalSamples];

            for (int i = 0; i < totalSamples; i++)
            {
                float t = (float)i / sampleRate;
                float env = Mathf.Exp(-t * 1.8f);
                float sub = Mathf.Sin(2f * Mathf.PI * 40f * t) * 0.8f;
                float noise = (Random.value * 2f - 1f) * 0.6f;
                samples[i] = Mathf.Clamp((sub + noise) * env, -1f, 1f);
            }

            AudioClip clip = AudioClip.Create("Proc_Giant_Explosion", totalSamples, 1, sampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        private static void SafeDestroy(Object obj)
        {
            if (obj == null) return;
            if (Application.isPlaying) Destroy(obj);
            else DestroyImmediate(obj);
        }
    }
}
