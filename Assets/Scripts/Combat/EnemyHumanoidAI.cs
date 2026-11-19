using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using FPSFramework.Player;

namespace FPSFramework.Combat
{
    public enum EnemyAIState
    {
        Patrol,
        Alert,
        Attack,
        PlantingC4,
        DefendingBomb,
        Dead
    }

    /// <summary>
    /// Kecerdasan Buatan Bot Humanoid 3D (Mixamo Ch15):
    /// 1. Berpatroli menelusuri rute strategis di map (NavMesh atau Fallback Waypoint).
    /// 2. Deteksi garis pandang (Line-of-Sight) dengan sudut pandang realistis dan jeda reaksi.
    /// 3. Menembak balik dalam pola burst (3 peluru) dengan akurasi terkalibrasi jarak.
    /// 4. Hitbox presisi bertingkat (Headshot 4.0x = Instant Kill, Body 1.0x).
    /// 5. Roboh saat mati, memicu event eliminasi di HUD, dan respawn di titik patroli lain.
    /// </summary>
    public class EnemyHumanoidAI : MonoBehaviour, IDamageable
    {
        [Header("Pengaturan Health & Status")]
        [SerializeField] private float maxHealth = 100f;
        [SerializeField] private float respawnDelay = 3.5f;
        [SerializeField] private bool autoRespawnOnDeath = false;
        [SerializeField] private bool showWorldHP = true;

        [Header("Deteksi Pandangan (Vision)")]
        [SerializeField] private float viewDistance = 38f;
        [SerializeField] private float fieldOfView = 115f;
        [SerializeField] private float reactionDelay = 0.35f;
        [SerializeField] private LayerMask visionObstacleLayers = ~0;

        [Header("Parameter Baku Tembak")]
        [SerializeField] private float damagePerShot = 14f;
        [SerializeField] private int burstShotCount = 3;
        [SerializeField] private float timeBetweenBurstShots = 0.12f;
        [SerializeField] private float burstCooldown = 1.4f;
        [SerializeField] private float baseInaccuracy = 0.045f;
        [SerializeField] private AudioClip shootSound;

        [Header("Pergerakan & Patroli")]
        [SerializeField] private float patrolSpeed = 3.2f;
        [SerializeField] private float waypointTolerance = 1.2f;

        // Static Events untuk Radar dan Round Management
        public static event System.Action<EnemyHumanoidAI, Vector3> OnAnyEnemyShot;
        public static event System.Action<EnemyHumanoidAI> OnAnyEnemyDied;
        public static event System.Action<EnemyHumanoidAI> OnAnyEnemySpawned;
        public static event System.Action<EnemyHumanoidAI> OnAnyEnemyDespawned;

        public float CurrentHealth { get; private set; }
        public bool IsDead => _currentState == EnemyAIState.Dead;
        public EnemyAIState State => _currentState;
        public bool IsFrozen { get; set; } = false;
        public bool WasHeadshot { get; private set; } = false;

        // Internal State
        private EnemyAIState _currentState = EnemyAIState.Patrol;
        private Transform _playerTransform;
        private PlayerHealth _playerHealth;
        private NavMeshAgent _navAgent;
        private AudioSource _audioSource;
        private AudioClip _procShootClip;
        private Light _muzzleLight;
        private Animator _animator;
        private CharacterController _charController;
        private float _stuckTimer;
        private Vector3 _lastMovePos;

        // Waypoints patroli di map
        private readonly List<Vector3> _patrolWaypoints = new List<Vector3>();
        private int _currentWaypointIndex = 0;

        // Timers
        private float _reactionTimer;
        private float _burstCooldownTimer;
        private bool _isBursting;

        [Header("T-Side C4 Bomb Mechanics")]
        [SerializeField] private bool hasC4 = false;
        private float _plantProgress = 0f;
        private GameObject _c4BackpackObj;

        public bool HasC4
        {
            get => hasC4;
            set
            {
                hasC4 = value;
                UpdateC4BackpackVisual();
            }
        }

        // Ragdoll & Rotasi
        private HumanoidRagdollController _ragdoll;
        private Quaternion _baseRotation;
        private Quaternion _flinchRotation;
        private Vector3 _initialPosition;
        private Vector3 _deathTargetPos;
        private Quaternion _deathTargetRot;

        private void Awake()
        {
            CurrentHealth = maxHealth;

            _ragdoll = GetComponent<HumanoidRagdollController>();
            if (_ragdoll == null)
            {
                _ragdoll = gameObject.AddComponent<HumanoidRagdollController>();
            }

            _charController = GetComponent<CharacterController>();
            if (_charController == null)
            {
                _charController = gameObject.AddComponent<CharacterController>();
            }
            _charController.radius = 0.32f;
            _charController.height = 1.35f;
            _charController.center = new Vector3(0f, 0.72f, 0f);
            _charController.stepOffset = 0.3f;
            _charController.slopeLimit = 45f;
            _charController.minMoveDistance = 0.001f;

            Vector3 initPos = transform.position;
            initPos.y = GetGroundedY(initPos);
            if (_charController != null) _charController.enabled = false;
            transform.position = initPos;
            if (_charController != null) _charController.enabled = true;

            _initialPosition = initPos;
            _lastMovePos = initPos;
            _baseRotation = transform.rotation;
            _flinchRotation = _baseRotation;
            _deathTargetRot = _baseRotation;
            _deathTargetPos = initPos;

            _animator = GetComponentInChildren<Animator>();
            if (_animator != null)
            {
                _animator.applyRootMotion = false;
            }

            _navAgent = GetComponent<NavMeshAgent>();
            if (_navAgent != null)
            {
                _navAgent.speed = patrolSpeed;
            }

            _audioSource = GetComponent<AudioSource>();
            if (_audioSource == null)
            {
                _audioSource = gameObject.AddComponent<AudioSource>();
            }
            _audioSource.playOnAwake = false;
            _audioSource.spatialBlend = 0.9f;

            SetupMuzzleLight();
            GenerateProceduralShootSound();
            EnsureHitboxesExist();
        }

        private void OnEnable()
        {
            OnAnyEnemySpawned?.Invoke(this);
        }

        private void OnDisable()
        {
            OnAnyEnemyDespawned?.Invoke(this);
        }

        private void Start()
        {
            FindPlayerReference();
            if (_patrolWaypoints.Count == 0)
            {
                SetupTacticalWaypoints();
            }
        }

        private void Update()
        {
            if (IsDead)
            {
                if (_charController != null && _charController.enabled) _charController.enabled = false;
                if (_animator != null && _animator.enabled) _animator.enabled = false;
                if (_ragdoll == null || !_ragdoll.IsRagdollActive)
                {
                    // Fallback static lerp jika tidak menggunakan ragdoll
                    transform.position = Vector3.Lerp(transform.position, _deathTargetPos, Time.deltaTime * 6f);
                    transform.rotation = Quaternion.Slerp(transform.rotation, _deathTargetRot, Time.deltaTime * 6f);
                }
                return;
            }

            // Kunci gerakan & aksi selama FreezeTime ronde
            if (IsFrozen || (TacticalRoundManager.Instance != null && TacticalRoundManager.Instance.State == RoundState.FreezeTime))
            {
                if (_navAgent != null && _navAgent.isOnNavMesh) _navAgent.isStopped = true;
                if (_animator != null) { _animator.SetFloat("Speed", 0f); _animator.SetFloat("MoveY", 0f); }
                if (_charController != null && _charController.enabled && !_charController.isGrounded)
                {
                    _charController.Move(Vector3.down * 9.81f * Time.deltaTime);
                }
                return;
            }

            // Kembalikan sentakan flinch jika hidup
            if (transform.rotation != _baseRotation)
            {
                transform.rotation = Quaternion.Slerp(transform.rotation, _baseRotation, Time.deltaTime * 6f);
            }

            // Grounding guard: pastikan posisi bot selalu menempel di lantai arena (tidak melayang)
            if (_charController != null && _charController.enabled)
            {
                if (!_charController.isGrounded)
                {
                    _charController.Move(Vector3.down * 9.81f * Time.deltaTime);
                }
            }
            else
            {
                Vector3 curPos = transform.position;
                float floorY = GetGroundedY(curPos);
                if (Mathf.Abs(curPos.y - floorY) > 0.015f)
                {
                    curPos.y = Mathf.MoveTowards(curPos.y, floorY, Time.deltaTime * 12f);
                    transform.position = curPos;
                }
            }

            if (_playerTransform == null)
            {
                FindPlayerReference();
            }

            if (_burstCooldownTimer > 0f)
            {
                _burstCooldownTimer -= Time.deltaTime;
            }

            // Evaluasi penglihatan bot terhadap pemain
            bool canSeePlayer = CheckLineOfSight();

            switch (_currentState)
            {
                case EnemyAIState.Patrol:
                    // 1. Cek apakah bot pembawa bom sudah tiba di area plant resmi (Site A atau Site B)
                    if (hasC4 && PlantedC4.ActiveC4 == null && PlantZone.IsPositionInPlantZone(transform.position, out string site))
                    {
                        _currentState = EnemyAIState.PlantingC4;
                        _plantProgress = 0f;
                        if (_navAgent != null && _navAgent.isOnNavMesh) _navAgent.isStopped = true;
                        if (_animator != null) { _animator.SetFloat("Speed", 0f); _animator.SetFloat("MoveY", 0f); }
                        break;
                    }

                    // 2. Evaluasi penglihatan musuh
                    if (canSeePlayer)
                    {
                        _currentState = EnemyAIState.Alert;
                        _reactionTimer = reactionDelay;
                        if (_navAgent != null && _navAgent.isOnNavMesh) _navAgent.isStopped = true;
                        if (_animator != null) { _animator.SetFloat("Speed", 0f); _animator.SetFloat("MoveY", 0f); }
                    }
                    else
                    {
                        // 3. Cek pengambilan C4 yang terjatuh jika bot tidak memegang C4
                        if (!hasC4 && PlantedC4.ActiveC4 == null)
                        {
                            if (TryHandleDroppedC4Recovery()) break;
                        }

                        ExecutePatrolMovement();
                    }
                    break;

                case EnemyAIState.PlantingC4:
                    if (_animator != null) { _animator.SetFloat("Speed", 0f); _animator.SetFloat("MoveY", 0f); }
                    if (_charController != null && _charController.enabled && !_charController.isGrounded)
                    {
                        _charController.Move(Vector3.down * 9.81f * Time.deltaTime);
                    }

                    // Bot berkomitmen menanam bom (durasi 3.5 detik) sampai selesai atau gugur
                    _plantProgress += Time.deltaTime;
                    if (_plantProgress >= 3.5f)
                    {
                        if (PlantZone.IsPositionInPlantZone(transform.position, out string siteName))
                        {
                            PlantedC4.SpawnPlantedC4(transform.position, transform.rotation, siteName);
                            HasC4 = false;
                            _currentState = EnemyAIState.DefendingBomb;
                            Debug.Log($"[EnemyHumanoidAI] Bot {name} successfully planted C4 at {siteName}!");
                        }
                        else
                        {
                            // Bot tidak berada di zona resmi yang sah -> batalkan plant dan lanjutkan patroli
                            _currentState = EnemyAIState.Patrol;
                            _plantProgress = 0f;
                        }
                    }
                    break;

                case EnemyAIState.DefendingBomb:
                    if (canSeePlayer)
                    {
                        _currentState = EnemyAIState.Attack;
                    }
                    else
                    {
                        if (PlantedC4.ActiveC4 != null)
                        {
                            Vector3 bombPos = PlantedC4.ActiveC4.transform.position;
                            float distToBomb = Vector3.Distance(transform.position, bombPos);
                            if (distToBomb > 6.0f)
                            {
                                Vector3 toBomb = (bombPos - transform.position);
                                toBomb.y = 0f;
                                if (toBomb.sqrMagnitude > 0.05f)
                                {
                                    transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(toBomb.normalized), Time.deltaTime * 6f);
                                    if (_charController != null && _charController.enabled)
                                    {
                                        _charController.Move(toBomb.normalized * patrolSpeed * Time.deltaTime + Vector3.down * 9.81f * Time.deltaTime);
                                    }
                                }
                            }
                            else
                            {
                                FaceTarget(bombPos + Vector3.forward * 4f);
                            }
                        }
                    }
                    break;

                case EnemyAIState.Alert:
                    FaceTarget(_playerTransform.position);
                    if (_animator != null) { _animator.SetFloat("Speed", 0f); _animator.SetFloat("MoveY", 0f); }
                    if (_charController != null && _charController.enabled && !_charController.isGrounded)
                    {
                        _charController.Move(Vector3.down * 9.81f * Time.deltaTime);
                    }
                    if (!canSeePlayer)
                    {
                        _currentState = EnemyAIState.Patrol;
                        if (_navAgent != null && _navAgent.isOnNavMesh) _navAgent.isStopped = false;
                    }
                    else
                    {
                        _reactionTimer -= Time.deltaTime;
                        if (_reactionTimer <= 0f)
                        {
                            _currentState = EnemyAIState.Attack;
                        }
                    }
                    break;

                case EnemyAIState.Attack:
                    FaceTarget(_playerTransform.position);
                    if (_animator != null) { _animator.SetFloat("Speed", 0f); _animator.SetFloat("MoveY", 0f); }
                    if (_charController != null && _charController.enabled && !_charController.isGrounded)
                    {
                        _charController.Move(Vector3.down * 9.81f * Time.deltaTime);
                    }
                    if (!canSeePlayer)
                    {
                        _currentState = EnemyAIState.Alert;
                        _reactionTimer = 0.5f;
                    }
                    else if (!_isBursting && _burstCooldownTimer <= 0f)
                    {
                        StartCoroutine(FireBurstRoutine());
                    }
                    break;
            }
        }

        private bool CheckLineOfSight()
        {
            if (_playerTransform == null) return false;

            Vector3 eyePos = transform.position + Vector3.up * 1.6f;
            Vector3 targetPos = _playerTransform.position + Vector3.up * 1.3f;
            Vector3 toPlayer = targetPos - eyePos;
            float distance = toPlayer.magnitude;

            if (distance > viewDistance) return false;

            // Sudut pandang (FOV)
            float angle = Vector3.Angle(transform.forward, toPlayer.normalized);
            if (angle > fieldOfView * 0.5f) return false;

            // Raycast cek penghalang dinding
            if (Physics.Raycast(eyePos, toPlayer.normalized, out RaycastHit hit, distance, visionObstacleLayers))
            {
                // Harus mengenai player (atau child player)
                return hit.collider.CompareTag("Player") || hit.transform.IsChildOf(_playerTransform) || hit.collider.GetComponentInParent<PlayerHealth>() != null;
            }

            return false;
        }

        private void ExecutePatrolMovement()
        {
            if (_patrolWaypoints.Count == 0) return;

            Vector3 targetWaypoint = _patrolWaypoints[_currentWaypointIndex];
            Vector3 myPos = transform.position;
            float distToWaypoint = Vector2.Distance(new Vector2(myPos.x, myPos.z),
                                                   new Vector2(targetWaypoint.x, targetWaypoint.z));

            if (distToWaypoint <= waypointTolerance)
            {
                // Jika bot membawa bom dan tiba di dalam zona resmi Plant (Site A atau Site B):
                if (hasC4 && PlantedC4.ActiveC4 == null && PlantZone.IsPositionInPlantZone(transform.position, out string detectedSite))
                {
                    _currentState = EnemyAIState.PlantingC4;
                    _plantProgress = 0f;
                    if (_navAgent != null && _navAgent.isOnNavMesh) _navAgent.isStopped = true;
                    if (_animator != null) { _animator.SetFloat("Speed", 0f); _animator.SetFloat("MoveY", 0f); }
                    return;
                }

                _currentWaypointIndex = (_currentWaypointIndex + 1) % _patrolWaypoints.Count;
                targetWaypoint = _patrolWaypoints[_currentWaypointIndex];
                _stuckTimer = 0f;
            }

            // Gerak via NavMeshAgent jika ada NavMesh di scene
            if (_navAgent != null && _navAgent.isOnNavMesh)
            {
                _navAgent.isStopped = false;
                _navAgent.SetDestination(targetWaypoint);
                _baseRotation = transform.rotation;
                if (_animator != null) { _animator.SetFloat("Speed", patrolSpeed); _animator.SetFloat("MoveY", 1.0f); }
                return;
            }

            Vector3 moveDir = (targetWaypoint - myPos);
            moveDir.y = 0f;

            if (moveDir.sqrMagnitude > 0.04f)
            {
                Vector3 moveNorm = moveDir.normalized;

                // Rotasi menghadap arah jalan
                Quaternion targetRot = Quaternion.LookRotation(moveNorm);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, Time.deltaTime * 6f);
                _baseRotation = transform.rotation;

                // Deteksi halangan dinding tepat di depan (Forward Obstacle Probe)
                Vector3 probePos = transform.position + Vector3.up * 0.85f;
                if (Physics.SphereCast(probePos, 0.28f, moveNorm, out RaycastHit wallHit, 0.75f, ~0, QueryTriggerInteraction.Ignore))
                {
                    if (wallHit.transform != transform && !wallHit.transform.IsChildOf(transform) &&
                        wallHit.collider.GetComponentInParent<PlayerHealth>() == null)
                    {
                        // Dinding terdeteksi menghalangi rute -> langsung alihkan ke waypoint berikutnya agar tidak tersangkut
                        _currentWaypointIndex = (_currentWaypointIndex + 1) % _patrolWaypoints.Count;
                        _stuckTimer = 0f;
                    }
                }

                // Gerak fisik via CharacterController (menjamin bot tidak dapat menembus dinding)
                if (_charController != null && _charController.enabled)
                {
                    Vector3 moveVelocity = moveNorm * patrolSpeed;
                    moveVelocity.y = -9.81f; // Gravitasi kontinu
                    _charController.Move(moveVelocity * Time.deltaTime);

                    // Deteksi jika bot macet/tersangkut di sudut rintangan
                    float movedDist = Vector2.Distance(new Vector2(transform.position.x, transform.position.z),
                                                       new Vector2(_lastMovePos.x, _lastMovePos.z));
                    if (movedDist < patrolSpeed * Time.deltaTime * 0.2f)
                    {
                        _stuckTimer += Time.deltaTime;
                        if (_stuckTimer > 1.2f)
                        {
                            _currentWaypointIndex = (_currentWaypointIndex + 1) % _patrolWaypoints.Count;
                            _stuckTimer = 0f;
                        }
                    }
                    else
                    {
                        _stuckTimer = 0f;
                    }
                    _lastMovePos = transform.position;
                }
                else
                {
                    // Fallback
                    Vector3 nextPos = Vector3.MoveTowards(transform.position, targetWaypoint, patrolSpeed * Time.deltaTime);
                    nextPos.y = GetGroundedY(nextPos);
                    transform.position = nextPos;
                }

                if (_animator != null) { _animator.SetFloat("Speed", patrolSpeed); _animator.SetFloat("MoveY", 1.0f); }
            }
            else
            {
                if (_animator != null) { _animator.SetFloat("Speed", 0f); _animator.SetFloat("MoveY", 0f); }
            }
        }

        private void FaceTarget(Vector3 targetWorldPos)
        {
            Vector3 dir = targetWorldPos - transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.05f)
            {
                Quaternion targetRot = Quaternion.LookRotation(dir.normalized);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, Time.deltaTime * 10f);
                _baseRotation = transform.rotation;
            }
        }

        private IEnumerator FireBurstRoutine()
        {
            _isBursting = true;

            for (int i = 0; i < burstShotCount; i++)
            {
                if (IsDead || _playerTransform == null) break;

                ExecuteSingleShot();
                yield return new WaitForSeconds(timeBetweenBurstShots);
            }

            _burstCooldownTimer = burstCooldown;
            _isBursting = false;
        }

        private void ExecuteSingleShot()
        {
            Vector3 muzzlePos = transform.position + transform.forward * 0.6f + Vector3.up * 1.3f;
            Transform rifle = FindChildRecursive(transform, "Bot_Rifle");
            if (rifle != null)
            {
                muzzlePos = rifle.position + rifle.forward * 0.35f;
            }

            Vector3 targetCenter = _playerTransform.position + Vector3.up * 1.3f;
            Vector3 shootDir = (targetCenter - muzzlePos).normalized;

            // Inakurasi terkalibrasi jarak
            float dist = Vector3.Distance(muzzlePos, targetCenter);
            float spread = baseInaccuracy + (dist * 0.0015f);
            shootDir += Random.insideUnitSphere * spread;
            shootDir.Normalize();

            // Muzzle flash light
            if (_muzzleLight != null)
            {
                StartCoroutine(MuzzleFlashRoutine());
            }

            // Suara tembakan
            AudioClip clip = shootSound != null ? shootSound : _procShootClip;
            if (clip != null && _audioSource != null)
            {
                _audioSource.pitch = Random.Range(0.95f, 1.05f);
                _audioSource.PlayOneShot(clip, 0.85f);
            }

            // Picu event tembakan musuh untuk Radar Taktis (Gunfire Ping)
            OnAnyEnemyShot?.Invoke(this, transform.position);

            // Raycast tembakan ke pemain
            if (Physics.Raycast(muzzlePos, shootDir, out RaycastHit hit, viewDistance * 1.2f))
            {
                PlayerHealth pHealth = hit.collider.GetComponentInParent<PlayerHealth>();
                if (pHealth != null)
                {
                    pHealth.TakeDamage(damagePerShot, HitboxType.Body, hit.point, shootDir);
                }
            }
        }

        private IEnumerator MuzzleFlashRoutine()
        {
            _muzzleLight.enabled = true;
            yield return new WaitForSeconds(0.04f);
            _muzzleLight.enabled = false;
        }

        public void TakeDamage(float damage, HitboxType hitbox, Vector3 hitPoint, Vector3 hitDirection)
        {
            if (IsDead) return;

            CurrentHealth = Mathf.Max(0f, CurrentHealth - damage);

            // Bot otomatis tersadar akan lokasi pemain saat tertembak
            if (_currentState == EnemyAIState.Patrol && _playerTransform != null)
            {
                _currentState = EnemyAIState.Alert;
                _reactionTimer = 0.15f;
            }

            if (CurrentHealth <= 0f)
            {
                Die(hitbox == HitboxType.Head, hitPoint, hitDirection);
            }
            else
            {
                // Sentakan flinch
                Vector3 flinchAxis = Vector3.Cross(Vector3.up, hitDirection).normalized;
                if (flinchAxis == Vector3.zero) flinchAxis = transform.right;
                _flinchRotation = _baseRotation * Quaternion.AngleAxis(12f, flinchAxis);
            }
        }

        public void ApplyExplosionForce(Vector3 blastOrigin, float force, float radius, float upwardsModifier = 0.8f)
        {
            if (_ragdoll != null)
            {
                _ragdoll.ApplyExplosion(blastOrigin, force, radius, upwardsModifier);
            }
        }

        /// <summary>
        /// Mengalibrasi parameter respon, akurasi, dan agresivitas tembakan bot sesuai tingkat kesulitan.
        /// </summary>
        public void SetDifficulty(BotDifficulty diff)
        {
            switch (diff)
            {
                case BotDifficulty.Recruit:
                    reactionDelay = 0.48f;
                    burstShotCount = 2;
                    burstCooldown = 1.8f;
                    baseInaccuracy = 0.080f;
                    damagePerShot = 10f;
                    break;

                case BotDifficulty.Tactical:
                    reactionDelay = 0.35f;
                    burstShotCount = 3;
                    burstCooldown = 1.3f;
                    baseInaccuracy = 0.045f;
                    damagePerShot = 14f;
                    break;

                case BotDifficulty.Veteran:
                    reactionDelay = 0.16f;
                    burstShotCount = 4;
                    burstCooldown = 0.9f;
                    baseInaccuracy = 0.018f;
                    damagePerShot = 16f;
                    break;
            }
        }

        private void Die(bool wasHeadshot, Vector3 hitPoint, Vector3 hitDir)
        {
            WasHeadshot = wasHeadshot;
            _currentState = EnemyAIState.Dead;
            CurrentHealth = 0f;

            if (_charController != null)
            {
                _charController.enabled = false;
            }

            if (_navAgent != null)
            {
                if (_navAgent.isOnNavMesh) _navAgent.isStopped = true;
                _navAgent.enabled = false;
            }

            // Aktifkan fisika Ragdoll penuh jika komponen tersedia
            if (_ragdoll != null)
            {
                _ragdoll.TriggerRagdoll(hitPoint, hitDir, wasHeadshot ? 24f : 16f, wasHeadshot);
            }
            else
            {
                // Fallback: reset pose animator dan rebahkan bot secara statis
                if (_animator != null)
                {
                    _animator.SetFloat("Speed", 0f);
                    _animator.SetFloat("MoveY", 0f);
                    _animator.SetFloat("MoveX", 0f);
                    _animator.Play("Locomotion_Standing", 0, 0f);
                    _animator.Update(0f);
                    _animator.enabled = false;
                }

                float dot = Vector3.Dot(transform.forward, hitDir);
                bool fallBackward = dot <= 0f;
                Quaternion tilt = fallBackward ? Quaternion.Euler(-90f, 0f, 0f) : Quaternion.Euler(90f, 0f, 0f);
                _deathTargetRot = _baseRotation * tilt;

                float groundY = GetGroundedY(transform.position);
                Vector3 slideDir = fallBackward ? -transform.forward : transform.forward;
                _deathTargetPos = transform.position + slideDir * 0.85f;
                _deathTargetPos.y = groundY + 0.12f;
                SetCollidersActive(false);
            }

            // Jika bot membawa bom dan mati sebelum plant, jatuhkan C4 ke tanah menggunakan model Dynamite_Multi
            if (hasC4 && PlantedC4.ActiveC4 == null)
            {
                HasC4 = false;
                SpawnDroppedC4(transform.position);
            }

            // Picu banner eliminasi & kill chime pada HUD pemain
            TargetDummy.TriggerElimination(gameObject.name, wasHeadshot);

            OnAnyEnemyDied?.Invoke(this);

            if (autoRespawnOnDeath)
            {
                StartCoroutine(RespawnRoutine());
            }
        }

        public void UpdateC4BackpackVisual()
        {
            // Bersihkan SEMUA ransel lama untuk mencegah duplikasi di hierarki
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                Transform child = transform.GetChild(i);
                if (child != null && child.name.StartsWith("Bot_C4_Backpack"))
                {
                    SafeDestroy(child.gameObject);
                }
            }
            _c4BackpackObj = null;

            if (hasC4)
            {
                GameObject c4Prefab = null;
#if UNITY_EDITOR
                c4Prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/Props/Explosives/Prefabs/Dynamite_Multi.prefab");
                if (c4Prefab == null)
                {
                    c4Prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/Props/Explosives/Models/Dynamite_Multi.fbx");
                }
#endif
                if (c4Prefab != null)
                {
                    _c4BackpackObj = Instantiate(c4Prefab, transform);
                    _c4BackpackObj.name = "Bot_C4_Backpack";
                }
                else
                {
                    _c4BackpackObj = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    _c4BackpackObj.name = "Bot_C4_Backpack";
                    _c4BackpackObj.transform.SetParent(transform, false);
                }

                Collider[] cols = _c4BackpackObj.GetComponentsInChildren<Collider>();
                foreach (var c in cols) SafeDestroy(c);

                _c4BackpackObj.transform.localPosition = new Vector3(0f, 1.15f, -0.22f);
                _c4BackpackObj.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
                _c4BackpackObj.transform.localScale = Vector3.one * 0.45f;
                FPSWeaponManager.FixRenderersMaterials(_c4BackpackObj);
            }
        }

        public static GameObject SpawnDroppedC4(Vector3 position)
        {
            GameObject c4Prefab = null;
#if UNITY_EDITOR
            c4Prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/Props/Explosives/Prefabs/Dynamite_Multi.prefab");
            if (c4Prefab == null)
            {
                c4Prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/Props/Explosives/Models/Dynamite_Multi.fbx");
            }
#endif
            GameObject dropped = null;
            if (c4Prefab != null)
            {
                dropped = Instantiate(c4Prefab);
                dropped.transform.localScale = Vector3.one * 0.45f;
            }
            else
            {
                dropped = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                dropped.transform.localScale = new Vector3(0.35f, 0.12f, 0.35f);
                Shader s = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                dropped.GetComponent<MeshRenderer>().sharedMaterial = new Material(s) { color = new Color(0.85f, 0.2f, 0.2f) };
            }

            dropped.name = "Dropped_C4_Dynamite";
            dropped.transform.position = position + Vector3.up * 0.12f;
            dropped.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);

            // Bersihkan collider internal lama agar tidak konflik fisik
            Collider[] oldCols = dropped.GetComponentsInChildren<Collider>();
            foreach (var c in oldCols) SafeDestroy(c);

            // Pasang trigger collider untuk pickup oleh bot musuh
            SphereCollider pickupTrigger = dropped.AddComponent<SphereCollider>();
            pickupTrigger.isTrigger = true;
            pickupTrigger.radius = 1.6f;

            // Pasang pulsing beacon light merah agar terlihat taktis di lantai
            GameObject beaconObj = new GameObject("C4_BeaconLight");
            beaconObj.transform.SetParent(dropped.transform, false);
            beaconObj.transform.localPosition = Vector3.up * 0.25f;
            Light l = beaconObj.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = new Color(1f, 0.2f, 0.2f);
            l.range = 3.5f;
            l.intensity = 2.0f;

            FPSWeaponManager.FixRenderersMaterials(dropped);
            return dropped;
        }

        private bool TryHandleDroppedC4Recovery()
        {
            GameObject dropped = GameObject.Find("Dropped_C4_Dynamite");
            if (dropped == null) return false;

            float dist = Vector3.Distance(transform.position, dropped.transform.position);
            // Ambil bom jika sudah dalam jarak dekat
            if (dist <= 1.8f)
            {
                HasC4 = true;
                SafeDestroy(dropped);
                Debug.Log($"[EnemyHumanoidAI] Bot {name} mengambil C4 yang terjatuh di tanah!");

                // Arahkan bot menuju Site terdekat untuk tanam bom
                string targetSite = transform.position.x > 0 ? "Site A" : "Site B";
                List<Vector3> route = targetSite == "Site A" ?
                    new List<Vector3> { new Vector3(19f, 0f, 32f), new Vector3(19f, 0f, 44f), new Vector3(20f, 0f, 52f), new Vector3(21f, 0f, 59.5f) } :
                    new List<Vector3> { new Vector3(-17f, 0f, 32f), new Vector3(-17f, 0f, 38f), new Vector3(-19.5f, 0f, 52f) };
                SetCustomWaypoints(route);
                return true;
            }

            // Jika dalam jarak 35m, dekati bom yang jatuh
            if (dist <= 35.0f)
            {
                Vector3 toDropped = dropped.transform.position - transform.position;
                toDropped.y = 0f;
                if (toDropped.sqrMagnitude > 0.04f)
                {
                    transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(toDropped.normalized), Time.deltaTime * 6f);
                    _baseRotation = transform.rotation;
                    if (_charController != null && _charController.enabled)
                    {
                        _charController.Move(toDropped.normalized * patrolSpeed * Time.deltaTime + Vector3.down * 9.81f * Time.deltaTime);
                    }
                    if (_animator != null) { _animator.SetFloat("Speed", patrolSpeed); _animator.SetFloat("MoveY", 1.0f); }
                    return true;
                }
            }

            return false;
        }

        private static void SafeDestroy(Object obj)
        {
            if (obj == null) return;
            if (Application.isPlaying) Destroy(obj);
            else DestroyImmediate(obj);
        }

        private IEnumerator RespawnRoutine()
        {
            yield return new WaitForSeconds(respawnDelay);

            // Pilih waypoint acak untuk respawn
            Vector3 spawnPos = _initialPosition;
            if (_patrolWaypoints.Count > 0)
            {
                _currentWaypointIndex = Random.Range(0, _patrolWaypoints.Count);
                spawnPos = _patrolWaypoints[_currentWaypointIndex];
            }

            if (Physics.Raycast(spawnPos + Vector3.up * 1.5f, Vector3.down, out RaycastHit groundHit, 4.0f))
            {
                spawnPos.y = groundHit.point.y;
            }

            if (_charController != null) _charController.enabled = false;
            transform.position = spawnPos;
            transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
            if (_charController != null) _charController.enabled = true;

            _baseRotation = transform.rotation;
            _flinchRotation = _baseRotation;
            _deathTargetRot = _baseRotation;
            _deathTargetPos = transform.position;

            CurrentHealth = maxHealth;
            _currentState = EnemyAIState.Patrol;

            if (_animator != null)
            {
                _animator.enabled = true;
                _animator.Rebind();
                _animator.Update(0f);
            }

            if (_navAgent != null)
            {
                _navAgent.enabled = true;
                if (_navAgent.isOnNavMesh)
                {
                    _navAgent.Warp(spawnPos);
                    _navAgent.isStopped = false;
                }
            }

            yield return new WaitForSeconds(0.3f);
            SetCollidersActive(true);
        }

        private void SetCollidersActive(bool active)
        {
            if (_charController != null) _charController.enabled = active;

            Collider[] cols = GetComponentsInChildren<Collider>();
            foreach (var c in cols)
            {
                if (_charController != null && c == _charController) continue;
                c.enabled = active;
            }
        }

        private void FindPlayerReference()
        {
            PlayerHealth p = FindAnyObjectByType<PlayerHealth>();
            if (p != null)
            {
                _playerHealth = p;
                _playerTransform = p.transform;
            }
            else if (Camera.main != null)
            {
                _playerTransform = Camera.main.transform;
            }
        }

        public void SetCustomWaypoints(List<Vector3> waypoints)
        {
            _patrolWaypoints.Clear();
            if (waypoints != null && waypoints.Count > 0)
            {
                _patrolWaypoints.AddRange(waypoints);
                _currentWaypointIndex = 0;
            }
        }

        public void ResetForRound(Vector3 spawnPos, Quaternion spawnRot)
        {
            StopAllCoroutines();
            _isBursting = false;
            _burstCooldownTimer = 0f;
            _reactionTimer = 0f;
            _stuckTimer = 0f;
            IsFrozen = false;

            spawnPos.y = GetGroundedY(spawnPos);

            CurrentHealth = maxHealth;
            _currentState = EnemyAIState.Patrol;
            _currentWaypointIndex = 0;
            _plantProgress = 0f;

            if (_charController != null) _charController.enabled = false;
            transform.position = spawnPos;
            transform.rotation = spawnRot;
            if (_charController != null) _charController.enabled = true;

            _initialPosition = spawnPos;
            _lastMovePos = spawnPos;
            _baseRotation = spawnRot;
            _flinchRotation = spawnRot;
            _deathTargetRot = spawnRot;
            _deathTargetPos = spawnPos;

            if (_ragdoll != null)
            {
                _ragdoll.ResetRagdoll();
            }
            else
            {
                if (_animator != null)
                {
                    _animator.enabled = true;
                    _animator.applyRootMotion = false;
                    _animator.Rebind();
                    _animator.Play("Locomotion_Standing", 0, 0f);
                    _animator.Update(0f);
                }
                SetCollidersActive(true);
            }

            if (_navAgent != null)
            {
                _navAgent.enabled = true;
                if (_navAgent.isOnNavMesh)
                {
                    _navAgent.Warp(spawnPos);
                    _navAgent.isStopped = false;
                }
            }
        }

        private float GetGroundedY(Vector3 checkPos)
        {
            // Raycast ke bawah dari atas kepala untuk mencari ketinggian lantai arena
            RaycastHit[] hits = Physics.RaycastAll(new Vector3(checkPos.x, checkPos.y + 1.2f, checkPos.z), Vector3.down, 4.0f, ~0, QueryTriggerInteraction.Ignore);
            float highestFloorY = -999f;
            bool foundFloor = false;

            foreach (var hit in hits)
            {
                // WAJIB: Abaikan jika mengenai collider bot ini sendiri
                if (hit.transform == transform || hit.transform.IsChildOf(transform)) continue;
                if (hit.collider.isTrigger) continue;

                // Abaikan jika collider milik pemain atau bot lain (IDamageable)
                if (hit.collider.GetComponentInParent<IDamageable>() != null) continue;

                // Permukaan tanah harus berada di bawah atau setinggi kaki bot saat ini (+0.35m toleransi)
                if (hit.point.y <= checkPos.y + 0.35f)
                {
                    if (!foundFloor || hit.point.y > highestFloorY)
                    {
                        highestFloorY = hit.point.y;
                        foundFloor = true;
                    }
                }
            }

            // Default level lantai arena adalah Y = 0.0f
            return foundFloor ? highestFloorY : 0f;
        }

        private void SetupTacticalWaypoints()
        {
            // Titik patroli strategis aman di koridor dan gedung terbuka
            _patrolWaypoints.Clear();
            _patrolWaypoints.Add(new Vector3(4f, 0f, 66f));      // CT Spawn Base
            _patrolWaypoints.Add(new Vector3(16f, 0f, 64f));     // CT to A Connector Doorway
            _patrolWaypoints.Add(new Vector3(21f, 0f, 58f));     // Site A Radar Hub Inside (Core)
            _patrolWaypoints.Add(new Vector3(20f, 0f, 52f));     // Site A South Doorway
            _patrolWaypoints.Add(new Vector3(19f, 0f, 44f));     // Long A Chokepoint
            _patrolWaypoints.Add(new Vector3(19f, 0f, 32f));     // Long A Connector A area
            _patrolWaypoints.Add(new Vector3(13f, 0f, 32f));     // Peeking Connector A
            _patrolWaypoints.Add(new Vector3(19f, 0f, 32f));     // Returning to Long A
            _patrolWaypoints.Add(new Vector3(19f, 0f, 44f));     // Long A North
            _patrolWaypoints.Add(new Vector3(20f, 0f, 52f));     // Site A Doorway
            _patrolWaypoints.Add(new Vector3(21f, 0f, 58f));     // Site A Radar Hub
            _patrolWaypoints.Add(new Vector3(16f, 0f, 64f));      // Returning to CT Highway
        }

        private void EnsureHitboxesExist()
        {
            // Pastikan bodi bot memiliki FPSHitbox untuk menerima damage jika peluru mengenai CharacterController
            FPSHitbox rootHitbox = GetComponent<FPSHitbox>();
            if (rootHitbox == null)
            {
                rootHitbox = gameObject.AddComponent<FPSHitbox>();
                rootHitbox.SetHitbox(HitboxType.Body, 1.0f);
            }

            FPSHitbox existingHead = null;
            FPSHitbox[] existingHitboxes = GetComponentsInChildren<FPSHitbox>();
            foreach (var hb in existingHitboxes)
            {
                if (hb.HitboxType == HitboxType.Head)
                {
                    existingHead = hb;
                    break;
                }
            }
            if (existingHead != null) return;

            // Cari tulang kepala Ch15
            Transform headBone = FindChildRecursive(transform, "Head");
            if (headBone != null)
            {
                SphereCollider sc = headBone.GetComponent<SphereCollider>();
                if (sc == null) sc = headBone.gameObject.AddComponent<SphereCollider>();
                sc.radius = 0.16f;
                sc.center = Vector3.zero;
                FPSHitbox hb = headBone.GetComponent<FPSHitbox>();
                if (hb == null) hb = headBone.gameObject.AddComponent<FPSHitbox>();
                hb.SetHitbox(HitboxType.Head, 4.0f);
            }

            // Cari tulang dada Ch15
            Transform spineBone = FindChildRecursive(transform, "Spine");
            if (spineBone != null)
            {
                CapsuleCollider cc = spineBone.GetComponent<CapsuleCollider>();
                if (cc == null) cc = spineBone.gameObject.AddComponent<CapsuleCollider>();
                cc.radius = 0.22f;
                cc.height = 0.65f;
                cc.direction = 1; // Y axis
                FPSHitbox hb = spineBone.GetComponent<FPSHitbox>();
                if (hb == null) hb = spineBone.gameObject.AddComponent<FPSHitbox>();
                hb.SetHitbox(HitboxType.Body, 1.0f);
            }

            // Fallback: jika model tidak memiliki bone hierarchy, buatkan kapsul hitbox kepala terpisah
            if (headBone == null)
            {
                GameObject headObj = new GameObject("Hitbox_Head");
                headObj.transform.SetParent(transform, false);
                headObj.transform.localPosition = new Vector3(0f, 1.6f, 0f);
                SphereCollider sc = headObj.AddComponent<SphereCollider>();
                sc.radius = 0.22f;
                FPSHitbox headHitbox = headObj.AddComponent<FPSHitbox>();
                headHitbox.SetHitbox(HitboxType.Head, 4.0f);
            }
        }

        private Transform FindChildRecursive(Transform parent, string nameSub)
        {
            if (parent.name.IndexOf(nameSub, System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return parent;
            }

            for (int i = 0; i < parent.childCount; i++)
            {
                Transform found = FindChildRecursive(parent.GetChild(i), nameSub);
                if (found != null) return found;
            }

            return null;
        }

        private void SetupMuzzleLight()
        {
            Transform rifle = FindChildRecursive(transform, "Bot_Rifle");
            Transform parentObj = rifle != null ? rifle : transform;

            GameObject lightObj = new GameObject("Bot_MuzzleLight");
            lightObj.transform.SetParent(parentObj, false);
            lightObj.transform.localPosition = rifle != null ? new Vector3(0f, 0f, 0.4f) : new Vector3(0f, 1.3f, 0.6f);

            _muzzleLight = lightObj.AddComponent<Light>();
            _muzzleLight.type = LightType.Point;
            _muzzleLight.color = new Color(1f, 0.75f, 0.3f);
            _muzzleLight.range = 3.5f;
            _muzzleLight.intensity = 2.0f;
            _muzzleLight.enabled = false;
        }

        private void GenerateProceduralShootSound()
        {
            const int sampleRate = 44100;
            int sampleCount = Mathf.RoundToInt(sampleRate * 0.10f);
            float[] data = new float[sampleCount];

            for (int s = 0; s < sampleCount; s++)
            {
                float t = (float)s / sampleRate;
                float decay = Mathf.Exp(-40f * t);
                float tone = Mathf.Sin(2f * Mathf.PI * Mathf.Lerp(220f, 90f, t * 12f) * t);
                float noise = (Random.value * 2f - 1f) * Mathf.Exp(-75f * t) * 0.4f;
                data[s] = (tone * 0.7f + noise) * decay;
            }

            _procShootClip = AudioClip.Create("Procedural_EnemyShot", sampleCount, 1, sampleRate, false);
            _procShootClip.SetData(data, 0);
        }

        private void OnGUI()
        {
            if (!showWorldHP || Camera.main == null || IsDead) return;

            Vector3 headPos = transform.position + Vector3.up * 2.1f;
            Vector3 camPos = Camera.main.transform.position;
            Vector3 toHead = headPos - camPos;

            // Line of sight: sembunyikan jika terhalang dinding/objek lain
            if (Physics.Raycast(camPos, toHead.normalized, out RaycastHit wallHit, toHead.magnitude, ~0, QueryTriggerInteraction.Ignore))
            {
                if (!wallHit.transform.IsChildOf(transform) && wallHit.transform != transform) return;
            }

            Vector3 screenPos = Camera.main.WorldToScreenPoint(headPos);

            if (screenPos.z > 0.5f && screenPos.z < 35f)
            {
                float w = 140f;
                float h = 24f;
                float x = screenPos.x - w / 2f;
                float y = Screen.height - screenPos.y - h;

                GUIStyle style = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 11,
                    fontStyle = FontStyle.Bold
                };
                style.normal.textColor = CurrentHealth > 40f ? new Color(1f, 0.35f, 0.35f) : Color.red;

                string stateTag = _currentState == EnemyAIState.Attack ? "FIRING" : (_currentState == EnemyAIState.Alert ? "ALERT" : "PATROL");
                GUI.Label(new Rect(x, y, w, h), $"[ BOT: {CurrentHealth:F0} HP | {stateTag} ]", style);
            }
        }
    }
}
