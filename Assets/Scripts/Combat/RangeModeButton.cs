using System.Collections;
using UnityEngine;

namespace FPSFramework.Combat
{
    /// <summary>
    /// Papan / Tombol Target Interaktif di Arena Latihan (Shooting Range):
    /// Mengimplementasikan IDamageable. Saat ditembak dengan peluru,
    /// tombol ini otomatis mengubah mode gerak bot (Static, Linear, atau Jiggle Peek).
    /// </summary>
    public class RangeModeButton : MonoBehaviour, IDamageable
    {
        [Header("Target & Mode")]
        [Tooltip("Referensi ke TargetDummyMovement. Jika kosong, dicari otomatis di scene.")]
        [SerializeField] private TargetDummyMovement targetDummy;

        [Tooltip("Mode yang akan diaktifkan saat tombol ini tertembak.")]
        [SerializeField] private StrafeMode targetMode = StrafeMode.LinearStrafe;

        [Tooltip("Label teks papan nama mode.")]
        [SerializeField] private string modeLabel = "LINEAR STRAFE";

        [Header("Visual Feedback")]
        [SerializeField] private Color idleColor = new Color(0.2f, 0.35f, 0.5f);
        [SerializeField] private Color activeColor = new Color(0.2f, 0.85f, 0.45f);
        [SerializeField] private Color hitFlashColor = Color.white;

        private Renderer _renderer;
        private MaterialPropertyBlock _propBlock;
        private AudioSource _audioSource;
        private AudioClip _procClickClip;

        private void Awake()
        {
            _renderer = GetComponent<Renderer>();
            _propBlock = new MaterialPropertyBlock();

            _audioSource = GetComponent<AudioSource>();
            if (_audioSource == null)
            {
                _audioSource = gameObject.AddComponent<AudioSource>();
            }
            _audioSource.playOnAwake = false;
            _audioSource.spatialBlend = 0.8f; // 3D sound

            GenerateClickAudio();
        }

        private void Start()
        {
            if (targetDummy == null)
            {
                targetDummy = FindAnyObjectByType<TargetDummyMovement>();
            }

            UpdateColor();
        }

        private void Update()
        {
            UpdateColor();
        }

        public void TakeDamage(float damage, HitboxType hitbox, Vector3 hitPoint, Vector3 hitDirection)
        {
            if (targetDummy == null)
            {
                targetDummy = FindAnyObjectByType<TargetDummyMovement>();
            }

            if (targetDummy != null)
            {
                targetDummy.SetMode(targetMode);
            }

            // Audio feedback
            if (_procClickClip != null && _audioSource != null)
            {
                _audioSource.pitch = Random.Range(1.1f, 1.25f);
                _audioSource.PlayOneShot(_procClickClip, 0.8f);
            }

            // Flash visual
            StartCoroutine(FlashHitRoutine());
        }

        private IEnumerator FlashHitRoutine()
        {
            SetMeshColor(hitFlashColor);
            yield return new WaitForSeconds(0.08f);
            UpdateColor();
        }

        private void UpdateColor()
        {
            if (targetDummy == null) return;

            bool isActive = targetDummy.Mode == targetMode;
            SetMeshColor(isActive ? activeColor : idleColor);
        }

        private void SetMeshColor(Color col)
        {
            if (_renderer == null) return;

            _renderer.GetPropertyBlock(_propBlock);
            _propBlock.SetColor("_BaseColor", col);
            _propBlock.SetColor("_Color", col);
            _renderer.SetPropertyBlock(_propBlock);
        }

        private void OnGUI()
        {
            Camera cam = Camera.main;
            if (cam == null) return;

            Vector3 labelWorldPos = transform.position + Vector3.up * 0.7f;
            Vector3 screenPos = cam.WorldToScreenPoint(labelWorldPos);

            // Hanya gambar jika berada di depan kamera dan dalam jarak pandang wajar
            if (screenPos.z > 0.5f && screenPos.z < 25f)
            {
                float w = 150f;
                float h = 24f;
                float x = screenPos.x - w / 2f;
                float y = Screen.height - screenPos.y - h;

                bool isActive = (targetDummy != null && targetDummy.Mode == targetMode);

                GUIStyle style = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 11,
                    fontStyle = FontStyle.Bold
                };
                style.normal.textColor = isActive ? new Color(0.3f, 1f, 0.5f) : new Color(0.8f, 0.85f, 0.9f);

                string statusText = isActive ? $"[ {modeLabel} - ACTIVE ]" : $"[ SHOOT: {modeLabel} ]";
                GUI.Label(new Rect(x, y, w, h), statusText, style);
            }
        }

        private void GenerateClickAudio()
        {
            const int sampleRate = 44100;
            int sampleCount = Mathf.RoundToInt(sampleRate * 0.08f);
            float[] data = new float[sampleCount];

            for (int s = 0; s < sampleCount; s++)
            {
                float t = (float)s / sampleRate;
                float decay = Mathf.Exp(-45f * t);
                float tone = Mathf.Sin(2f * Mathf.PI * 950f * t);
                data[s] = tone * decay * 0.5f;
            }

            _procClickClip = AudioClip.Create("Procedural_ButtonBeep", sampleCount, 1, sampleRate, false);
            _procClickClip.SetData(data, 0);
        }
    }
}
