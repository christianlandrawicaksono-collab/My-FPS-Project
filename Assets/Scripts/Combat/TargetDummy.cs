using System.Collections;
using UnityEngine;

namespace FPSFramework.Combat
{
    /// <summary>
    /// Target Latihan Shooting Range Dummy ala Valorant / CS:GO.
    /// Memiliki:
    /// 1. Health System (100 HP).
    /// 2. Deteksi Hitbox (Headshot vs Bodyshot).
    /// 3. Reaksi Fisik Flinch (berguncang saat tertembak).
    /// 4. Auto-Respawn Loop (roboh saat mati, bangkit kembali setelah 2 detik).
    /// 5. Visual Health Status & Floating Info.
    /// </summary>
    public class TargetDummy : MonoBehaviour, IDamageable
    {
        [Header("Pengaturan Health")]
        [SerializeField] private float maxHealth = 100f;
        [SerializeField] private float respawnDelay = 2.0f;

        [Header("Reaksi Fisik")]
        [Tooltip("Sentakan rotasi saat terkena tembakan.")]
        [SerializeField] private float flinchAngle = 14f;
        [SerializeField] private float recoverySpeed = 10f;

        [Header("Tampilan Visual")]
        [SerializeField] private bool showWorldHP = true;
        [SerializeField] private Color normalColor = new Color(0.2f, 0.6f, 1.0f); // Biru dummy
        [SerializeField] private Color hitColor = Color.red;

        public float CurrentHealth { get; private set; }
        public bool IsDead { get; private set; }

        private Quaternion _initialRotation;
        private Quaternion _targetRotation;
        private Vector3 _initialPosition;

        private Renderer[] _renderers;
        private Collider[] _colliders;
        private MaterialPropertyBlock _propBlock;

        private void Awake()
        {
            CurrentHealth = maxHealth;
            _initialRotation = transform.localRotation;
            _targetRotation = _initialRotation;
            _initialPosition = transform.position;

            _renderers = GetComponentsInChildren<Renderer>();
            _colliders = GetComponentsInChildren<Collider>();
            _propBlock = new MaterialPropertyBlock();

            EnsureHitboxesExist();
        }

        private void Update()
        {
            // Smooth rotasi flinch / roboh
            transform.localRotation = Quaternion.Slerp(transform.localRotation, _targetRotation, Time.deltaTime * recoverySpeed);

            // Kembali tegak jika sedang hidup dan terkena flinch
            if (!IsDead && transform.localRotation != _initialRotation)
            {
                _targetRotation = Quaternion.Slerp(_targetRotation, _initialRotation, Time.deltaTime * 6f);
            }
        }

        /// <summary>
        /// Mengatur rotasi dasar dummy (misal saat berputar menghadap pemain via TargetDummyMovement).
        /// </summary>
        public void UpdateBaseRotation(Quaternion newRotation)
        {
            _initialRotation = newRotation;
            if (!IsDead)
            {
                _targetRotation = Quaternion.Slerp(_targetRotation, newRotation, Time.deltaTime * 8f);
            }
        }

        public static event System.Action<string, bool> OnDummyEliminated;

        public static void TriggerElimination(string victimName, bool wasHeadshot)
        {
            OnDummyEliminated?.Invoke(victimName, wasHeadshot);
        }

        private bool _lastHitWasHeadshot;

        public void TakeDamage(float damage, HitboxType hitbox, Vector3 hitPoint, Vector3 hitDirection)
        {
            if (IsDead) return;

            _lastHitWasHeadshot = (hitbox == HitboxType.Head);
            CurrentHealth -= damage;
            StartCoroutine(FlashColorRoutine());

            if (CurrentHealth <= 0f)
            {
                Die(hitDirection);
            }
            else
            {
                // Flinch: Dorong rotasi sedikit ke belakang
                Vector3 flinchAxis = Vector3.Cross(Vector3.up, hitDirection).normalized;
                if (flinchAxis == Vector3.zero) flinchAxis = transform.right;

                _targetRotation = _initialRotation * Quaternion.AngleAxis(flinchAngle, flinchAxis);
            }
        }

        private void Die(Vector3 hitDirection)
        {
            IsDead = true;
            CurrentHealth = 0f;

            // Picu event eliminasi untuk HUD banner & kill sound
            OnDummyEliminated?.Invoke(gameObject.name, _lastHitWasHeadshot);

            // Nonaktifkan collider saat mati agar tidak menghalangi peluru lain
            SetCollidersActive(false);

            // Roboh ke belakang (90 derajat)
            _targetRotation = _initialRotation * Quaternion.Euler(-90f, 0f, 0f);

            // Mulai hitung mundur auto-respawn
            StartCoroutine(RespawnRoutine());
        }

        private IEnumerator RespawnRoutine()
        {
            yield return new WaitForSeconds(respawnDelay);

            // Bangkit kembali
            CurrentHealth = maxHealth;
            _targetRotation = _initialRotation;

            yield return new WaitForSeconds(0.4f);

            SetCollidersActive(true);
            IsDead = false;
        }

        private void SetCollidersActive(bool active)
        {
            if (_colliders == null) return;
            foreach (var col in _colliders)
            {
                if (col != null) col.enabled = active;
            }
        }

        private IEnumerator FlashColorRoutine()
        {
            SetColor(hitColor);
            yield return new WaitForSeconds(0.08f);
            SetColor(normalColor);
        }

        private void SetColor(Color col)
        {
            if (_renderers == null) return;
            foreach (var r in _renderers)
            {
                if (r != null)
                {
                    r.GetPropertyBlock(_propBlock);
                    _propBlock.SetColor("_BaseColor", col);
                    _propBlock.SetColor("_Color", col);
                    r.SetPropertyBlock(_propBlock);
                }
            }
        }

        private void EnsureHitboxesExist()
        {
            // Jika dummy belum memiliki child FPSHitbox, otomatis buatkan struktur Hitbox standar
            FPSHitbox existing = GetComponentInChildren<FPSHitbox>();
            if (existing != null) return;

            Collider rootCol = GetComponent<Collider>();
            if (rootCol is CapsuleCollider capsule)
            {
                // Bagi collider menjadi Head dan Body otomatis
                rootCol.enabled = false;

                // Child Body
                GameObject bodyObj = new GameObject("Hitbox_Body");
                bodyObj.transform.SetParent(transform, false);
                bodyObj.transform.localPosition = new Vector3(0f, capsule.height * 0.35f, 0f);
                CapsuleCollider bodyCol = bodyObj.AddComponent<CapsuleCollider>();
                bodyCol.radius = capsule.radius;
                bodyCol.height = capsule.height * 0.7f;
                FPSHitbox bodyHitbox = bodyObj.AddComponent<FPSHitbox>();
                bodyHitbox.SetHitbox(HitboxType.Body, 1.0f);

                // Child Head
                GameObject headObj = new GameObject("Hitbox_Head");
                headObj.transform.SetParent(transform, false);
                headObj.transform.localPosition = new Vector3(0f, capsule.height * 0.85f, 0f);
                SphereCollider headCol = headObj.AddComponent<SphereCollider>();
                headCol.radius = capsule.radius * 0.8f;
                FPSHitbox headHitbox = headObj.AddComponent<FPSHitbox>();
                headHitbox.SetHitbox(HitboxType.Head, 4.0f);

                _colliders = GetComponentsInChildren<Collider>();
            }
        }

        private void OnGUI()
        {
            if (!showWorldHP || Camera.main == null) return;

            Vector3 headPos = transform.position + Vector3.up * 2.1f;
            Vector3 camPos = Camera.main.transform.position;
            Vector3 toHead = headPos - camPos;

            // Line of sight: sembunyikan teks jika terhalang dinding/objek lain
            if (Physics.Raycast(camPos, toHead.normalized, out RaycastHit wallHit, toHead.magnitude, ~0, QueryTriggerInteraction.Ignore))
            {
                if (!wallHit.transform.IsChildOf(transform) && wallHit.transform != transform) return;
            }

            Vector3 screenPos = Camera.main.WorldToScreenPoint(headPos);

            // Hanya gambar jika berada di depan kamera
            if (screenPos.z > 0.5f && screenPos.z < 35f)
            {
                float w = 120f;
                float h = 26f;
                float x = screenPos.x - w / 2f;
                float y = Screen.height - screenPos.y - h;

                // Teks status HP
                GUIStyle style = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 12,
                    fontStyle = FontStyle.Bold
                };

                if (IsDead)
                {
                    style.normal.textColor = Color.yellow;
                    GUI.Label(new Rect(x, y, w, h), "RESPAWNING...", style);
                }
                else
                {
                    style.normal.textColor = CurrentHealth > 30f ? Color.green : Color.red;
                    GUI.Label(new Rect(x, y, w, h), $"{CurrentHealth:F0} / {maxHealth:F0} HP", style);
                }
            }
        }
    }
}
