using System;
using UnityEngine;
using FPSFramework.Combat;

namespace FPSFramework.Player
{
    /// <summary>
    /// Mengelola kesehatan pemain (Player Health):
    /// - Mengimplementasikan IDamageable agar pemain dapat menerima damage dari tembakan bot musuh.
    /// - Menyediakan event untuk sinkronisasi HP dan efek flash merah di FPSTacticalHUD.
    /// - Mengatur status kematian dan respawn otomatis.
    /// </summary>
    public class PlayerHealth : MonoBehaviour, IDamageable
    {
        [Header("Pengaturan Health")]
        [SerializeField] private float maxHealth = 100f;
        [SerializeField] private bool autoRespawn = true;
        [SerializeField] private float respawnDelay = 3.5f;

        public float MaxHealth => maxHealth;
        public float CurrentHealth { get; private set; }
        public bool IsDead { get; private set; }
        public bool AutoRespawn
        {
            get => autoRespawn;
            set => autoRespawn = value;
        }

        public event Action<float, float> OnHealthChanged; // current, max
        public event Action<Vector3> OnDamaged;           // hitDirection
        public event Action OnDied;
        public event Action OnRespawned;

        private Vector3 _spawnPosition;
        private Quaternion _spawnRotation;

        private void Awake()
        {
            CurrentHealth = maxHealth;
            _spawnPosition = transform.position;
            _spawnRotation = transform.rotation;
        }

        public void TakeDamage(float damage, HitboxType hitbox, Vector3 hitPoint, Vector3 hitDirection)
        {
            if (IsDead) return;

            CurrentHealth = Mathf.Max(0f, CurrentHealth - damage);
            OnHealthChanged?.Invoke(CurrentHealth, maxHealth);
            OnDamaged?.Invoke(hitDirection);

            if (CurrentHealth <= 0f)
            {
                Die();
            }
        }

        private void Die()
        {
            IsDead = true;
            OnDied?.Invoke();

            if (autoRespawn)
            {
                Invoke(nameof(Respawn), respawnDelay);
            }
        }

        public void Respawn()
        {
            CurrentHealth = maxHealth;
            IsDead = false;

            CharacterController cc = GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            transform.position = _spawnPosition;
            transform.rotation = _spawnRotation;
            if (cc != null) cc.enabled = true;

            OnHealthChanged?.Invoke(CurrentHealth, maxHealth);
            OnRespawned?.Invoke();
        }

        public void ResetHealthForNewRound(Vector3 spawnPos, Quaternion spawnRot)
        {
            CancelInvoke(nameof(Respawn));
            CurrentHealth = maxHealth;
            IsDead = false;
            _spawnPosition = spawnPos;
            _spawnRotation = spawnRot;

            CharacterController cc = GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            transform.position = spawnPos;
            transform.rotation = spawnRot;
            if (cc != null) cc.enabled = true;

            OnHealthChanged?.Invoke(CurrentHealth, maxHealth);
            OnRespawned?.Invoke();
        }

        public void SetSpawnPoint(Vector3 pos, Quaternion rot)
        {
            _spawnPosition = pos;
            _spawnRotation = rot;
        }
    }
}
