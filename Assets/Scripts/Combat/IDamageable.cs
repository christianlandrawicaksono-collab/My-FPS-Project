using UnityEngine;

namespace FPSFramework.Combat
{
    public enum HitboxType
    {
        Body,
        Head,
        Limb
    }

    /// <summary>
    /// Kontrak interface untuk setiap objek/karakter yang dapat menerima damage.
    /// </summary>
    public interface IDamageable
    {
        void TakeDamage(float damage, HitboxType hitbox, Vector3 hitPoint, Vector3 hitDirection);
    }
}
