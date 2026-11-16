using UnityEngine;

namespace FPSFramework.Combat
{
    /// <summary>
    /// Komponen yang ditempelkan pada collider spesifik (Kepala, Badan, Kaki).
    /// Mengatur pengali damage (multiplier) dan meneruskan hit ke parent IDamageable.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class FPSHitbox : MonoBehaviour
    {
        [Header("Konfigurasi Hitbox")]
        [Tooltip("Jenis bagian tubuh.")]
        [SerializeField] private HitboxType hitboxType = HitboxType.Body;

        [Tooltip("Pengali damage (Head: 4.0x = Instant kill, Body: 1.0x, Limb: 0.75x).")]
        [SerializeField] private float damageMultiplier = 1.0f;

        private IDamageable _damageableTarget;

        public HitboxType HitboxType => hitboxType;
        public float DamageMultiplier => damageMultiplier;

        private void Awake()
        {
            _damageableTarget = GetComponentInParent<IDamageable>();
        }

        /// <summary>
        /// Menerima hit tembakan, mengalikan dengan multiplier hitbox, dan meneruskan ke target.
        /// </summary>
        public void ReceiveHit(float baseDamage, Vector3 hitPoint, Vector3 hitDirection)
        {
            if (_damageableTarget == null)
            {
                _damageableTarget = GetComponentInParent<IDamageable>();
            }

            float finalDamage = baseDamage * damageMultiplier;
            _damageableTarget?.TakeDamage(finalDamage, hitboxType, hitPoint, hitDirection);
        }

        public void SetHitbox(HitboxType type, float multiplier)
        {
            hitboxType = type;
            damageMultiplier = multiplier;
        }
    }
}
