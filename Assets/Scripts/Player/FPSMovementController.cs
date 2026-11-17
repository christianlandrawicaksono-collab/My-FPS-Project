using UnityEngine;

namespace FPSFramework.Player
{
    /// <summary>
    /// Mengatur fisika dan logika pergerakan pemain FPS ala Tactical Shooter (CS / Valorant):
    /// - Snappy Acceleration & Sharp Friction (Counter-strafing responsif, tanpa licin).
    /// - Tiga kecepatan gerak: Run (default), Walk (Shift), dan Crouch (Ctrl).
    /// - Jump Buffering & Coyote Time (Lompatan 100% responsif, tidak pernah dropped input).
    /// - Auto-sync ukuran & posisi Visual_Capsule saat berdiri vs jongkok agar tidak tenggelam.
    /// - Deteksi halangan atap presisi (Physics.CheckCapsule): tetap jongkok jika di atas kepala ada rintangan.
    /// - Menyediakan data status gerak real-time via interface IFPSMovementState.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class FPSMovementController : MonoBehaviour, IFPSMovementState
    {
        [Header("Referensi Komponen")]
        [Tooltip("Komponen CharacterController bodi kapsul.")]
        [SerializeField] private CharacterController controller;

        [Tooltip("Pembaca input. Jika kosong, akan otomatis dicari di GameObject ini.")]
        [SerializeField] private FPSInputReader inputReader;

        [Tooltip("Transform bodi visual kapsul. Jika kosong, akan dicari otomatis di child.")]
        [SerializeField] private Transform visualMeshTransform;

        [Header("Kecepatan Gerak (Meters / Second)")]
        [Tooltip("Kecepatan lari standar (default movement di CS & Valorant).")]
        [SerializeField] private float runSpeed = 6.2f;

        [Tooltip("Kecepatan jalan perlahan saat menahan Shift (silent walk).")]
        [SerializeField] private float walkSpeed = 3.2f;

        [Tooltip("Kecepatan bergerak saat jongkok (crouch).")]
        [SerializeField] private float crouchSpeed = 2.2f;

        [Header("Akselerasi & Gesekan (Counter-Strafing Feel)")]
        [Tooltip("Kecepatan mencapai top speed di tanah (semakin tinggi, semakin snappy).")]
        [SerializeField] private float groundAcceleration = 50f;

        [Tooltip("Gaya pengereman saat melepas tombol atau menekan tombol lawan arah (counter-strafe).")]
        [SerializeField] private float groundDeceleration = 65f;

        [Tooltip("Kontrol arah saat berada di udara (terbatas agar tidak bisa manuver liar di udara).")]
        [SerializeField] private float airAcceleration = 12f;

        [Header("Lompat & Responsivitas (Jump Buffer & Coyote Time)")]
        [Tooltip("Ketinggian lompat dalam meter.")]
        [SerializeField] private float jumpHeight = 1.15f;

        [Tooltip("Kekuatan gravitasi (dibuat lebih tegas dari gravitasi bumi 9.8 agar jatuh lebih tegas).")]
        [SerializeField] private float gravity = 22f;

        [Tooltip("Gaya gravitasi kecil saat di tanah untuk menjaga karakter tetap menempel pada slope/turunan.")]
        [SerializeField] private float groundedDownwardForce = -2.0f;

        [Tooltip("Waktu toleransi input lompat sebelum mendarat (Jump Buffer).")]
        [SerializeField] private float jumpBufferTime = 0.2f;

        [Tooltip("Waktu toleransi lompat sesaat setelah meninggalkan permukaan tanah (Coyote Time).")]
        [SerializeField] private float coyoteTime = 0.15f;

        [Tooltip("Layer mask untuk cek pijakan tanah.")]
        [SerializeField] private LayerMask groundLayers = ~0;

        [Header("Pengaturan Crouch & Ceiling Check")]
        [SerializeField] private float standingHeight = 1.9f;
        [SerializeField] private Vector3 standingCenter = new Vector3(0f, 0.95f, 0f);

        [SerializeField] private float crouchHeight = 1.3f;
        [SerializeField] private Vector3 crouchCenter = new Vector3(0f, 0.65f, 0f);

        [Tooltip("Kecepatan transisi tinggi collider saat jongkok.")]
        [SerializeField] private float crouchTransitionSpeed = 12f;

        [Tooltip("Layer mask halangan atap untuk mengecek apakah pemain bisa berdiri kembali.")]
        [SerializeField] private LayerMask ceilingObstacleLayers = ~0;

        // Velocity & internal states
        private Vector3 _horizontalVelocity;
        private float _verticalVelocity;
        private bool _isGrounded;
        private bool _isCrouching;
        private bool _isWalking;
        private bool _isHeadObstructed;
        private float _currentColliderHeight;
        private Vector3 _currentColliderCenter;

        // Timers
        private float _jumpBufferTimer;
        private float _coyoteTimer;

        #region IFPSMovementState Implementation
        public Vector3 Velocity => _horizontalVelocity + Vector3.up * _verticalVelocity;
        public float CurrentHorizontalSpeed => _horizontalVelocity.magnitude;
        public float TargetMaxSpeed { get; private set; }
        public float SpeedRatio => Mathf.Clamp01(CurrentHorizontalSpeed / runSpeed);
        public bool IsGrounded => _isGrounded;
        public bool IsCrouching => _isCrouching;
        public bool IsWalking => _isWalking;
        public bool IsHeadObstructed => _isHeadObstructed;
        public bool MovementLocked { get; set; } = false;

        /// <summary>
        /// Menghitung inakurasi tembakan secara dinamis ala CS & Valorant.
        /// Nilai 0.0f = first-bullet accuracy sempurna saat diam.
        /// </summary>
        public float InaccuracyModifier
        {
            get
            {
                if (!_isGrounded) return 2.8f; // Sangat tidak akurat di udara

                float speed = CurrentHorizontalSpeed;
                // Deadzone counter-strafe: jika kecepatan di bawah 0.2 m/s, dianggap diam sempurna
                if (speed < 0.2f)
                {
                    return _isCrouching ? 0.0f : 0.0f;
                }

                if (_isCrouching)
                {
                    return Mathf.Lerp(0.05f, 0.25f, speed / crouchSpeed);
                }

                if (_isWalking)
                {
                    return Mathf.Lerp(0.15f, 0.45f, speed / walkSpeed);
                }

                // Lari normal
                return Mathf.Lerp(0.35f, 1.0f, speed / runSpeed);
            }
        }
        #endregion

        private void Awake()
        {
            if (controller == null)
            {
                controller = GetComponent<CharacterController>();
            }

            if (inputReader == null)
            {
                inputReader = GetComponent<FPSInputReader>();
            }

            // Cari visual mesh otomatis jika belum di-assign
            if (visualMeshTransform == null)
            {
                Transform childVisual = transform.Find("Visual_Capsule");
                if (childVisual != null)
                {
                    visualMeshTransform = childVisual;
                }
                else
                {
                    MeshRenderer mr = GetComponentInChildren<MeshRenderer>();
                    if (mr != null) visualMeshTransform = mr.transform;
                }
            }

            // Exclude Player layer from ceiling & ground check to prevent self-collision
            ceilingObstacleLayers &= ~(1 << gameObject.layer);
            groundLayers &= ~(1 << gameObject.layer);

            _currentColliderHeight = standingHeight;
            _currentColliderCenter = standingCenter;
            controller.height = standingHeight;
            controller.center = standingCenter;

            // Inisialisasi visual capsule awal
            SyncVisualMesh();
        }

        private void Update()
        {
            UpdateGroundStatus();
            if (MovementLocked)
            {
                _horizontalVelocity = Vector3.zero;
                _verticalVelocity = groundedDownwardForce;
                controller.Move(Vector3.up * _verticalVelocity * Time.deltaTime);
                return;
            }
            HandleCrouch();
            HandleHorizontalMovement();
            HandleVerticalMovement();
            ApplyFinalMovement();
        }

        /// <summary>
        /// Memeriksa status tanah dengan dual-check (controller.isGrounded + Physics sphere di telapak kaki)
        /// dan memperbarui timer Coyote Time untuk mencegah dropped jump input.
        /// </summary>
        private void UpdateGroundStatus()
        {
            bool ccGrounded = controller.isGrounded;

            // Ground detection presisi: kombinasi raycast ke bawah dan spherecast
            // Memastikan skinWidth (0.08m) CharacterController terjembatani sempurna dengan collider lantai
            Vector3 rayOrigin = transform.position + Vector3.up * 0.25f;
            bool rayGrounded = Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit groundHit, 0.40f, groundLayers, QueryTriggerInteraction.Ignore);
            bool sphereGrounded = Physics.SphereCast(rayOrigin, controller.radius * 0.7f, Vector3.down, out RaycastHit sphereHit, 0.30f, groundLayers, QueryTriggerInteraction.Ignore);

            _isGrounded = ccGrounded || ((rayGrounded || sphereGrounded) && _verticalVelocity <= 0.2f);

            if (_isGrounded)
            {
                _coyoteTimer = coyoteTime;
            }
            else
            {
                _coyoteTimer -= Time.deltaTime;
            }
        }

        /// <summary>
        /// Mengatur logika jongkok (crouch) dan deteksi rintangan atap presisi.
        /// Jika ada rintangan di atas kepala, pemain DIPAKSA TETAP JONGKOK meskipun tombol dilepas.
        /// </summary>
        private void HandleCrouch()
        {
            bool wantsToCrouch = inputReader != null && inputReader.IsCrouchActive;

            // Selalu cek apakah volume berdiri penuh di atas kepala terhalang rintangan
            _isHeadObstructed = CheckCeilingObstacle();

            // Jika di atas kepala terhalang atap, PAKSA TETAP CROUCH meskipun tombol sudah dilepas!
            if (_isHeadObstructed)
            {
                wantsToCrouch = true;
            }

            _isCrouching = wantsToCrouch;

            // Interpolasi ukuran CharacterController
            float targetHeight = _isCrouching ? crouchHeight : standingHeight;
            Vector3 targetCenter = _isCrouching ? crouchCenter : standingCenter;

            _currentColliderHeight = Mathf.Lerp(_currentColliderHeight, targetHeight, Time.deltaTime * crouchTransitionSpeed);
            _currentColliderCenter = Vector3.Lerp(_currentColliderCenter, targetCenter, Time.deltaTime * crouchTransitionSpeed);

            controller.height = _currentColliderHeight;
            controller.center = _currentColliderCenter;

            // Sinkronisasi visual mesh kapsul agar ukurannya pas dan tidak tembus ke bawah lantai
            SyncVisualMesh();
        }

        /// <summary>
        /// Menyesuaikan skala Y dan posisi Y visual capsule agar presisi dengan CharacterController.
        /// Tidak akan mengubah skala jika target adalah model karakter humanoid (Animator / SkinnedMesh).
        /// </summary>
        private void SyncVisualMesh()
        {
            if (visualMeshTransform == null) return;

            // Proteksi: Jika visual mesh adalah model karakter 3D (memiliki SkinnedMesh atau Animator),
            // jangan ubah skala Y karena pose jongkok sudah diatur sepenuhnya oleh animasi!
            if (visualMeshTransform.GetComponent<Animator>() != null || 
                visualMeshTransform.GetComponentInChildren<SkinnedMeshRenderer>() != null)
            {
                return;
            }

            // Mesh kapsul standar Unity memiliki tinggi asli 2.0 meter
            float targetVisualScaleY = _currentColliderHeight / 2.0f;
            Vector3 vScale = visualMeshTransform.localScale;
            vScale.y = targetVisualScaleY;
            visualMeshTransform.localScale = vScale;

            Vector3 vPos = visualMeshTransform.localPosition;
            vPos.y = _currentColliderCenter.y;
            visualMeshTransform.localPosition = vPos;
        }

        /// <summary>
        /// Cek apakah ruang di atas kepala untuk posisi berdiri penuh (standingHeight) aman
        /// menggunakan Physics.CheckCapsule yang presisi (mendeteksi baik overlap maupun jarak).
        /// </summary>
        private bool CheckCeilingObstacle()
        {
            float radius = controller.radius * 0.90f;
            // Titik bawah di atas kaki
            Vector3 pointBottom = transform.position + Vector3.up * (radius + 0.1f);
            // Titik atas di ujung kepala posisi berdiri penuh
            Vector3 pointTop = transform.position + Vector3.up * (standingHeight - radius);

            if (pointTop.y <= pointBottom.y)
            {
                pointTop = pointBottom + Vector3.up * 0.05f;
            }

            return Physics.CheckCapsule(pointBottom, pointTop, radius, ceilingObstacleLayers, QueryTriggerInteraction.Ignore);
        }

        /// <summary>
        /// Logika pergerakan horizontal snappy (Counter-strafing).
        /// </summary>
        private void HandleHorizontalMovement()
        {
            Vector2 moveInput = inputReader != null ? inputReader.MoveInput : Vector2.zero;
            _isWalking = inputReader != null && inputReader.IsWalkActive && !_isCrouching;

            // Tentukan target kecepatan berdasarkan state
            if (_isCrouching)
            {
                TargetMaxSpeed = crouchSpeed;
            }
            else if (_isWalking)
            {
                TargetMaxSpeed = walkSpeed;
            }
            else
            {
                float baseRun = runSpeed;
                // Tactical Shooter Knife Sprint (+12% kecepatan lari saat memegang pisau)
                if (FPSWeaponManager.Instance != null && FPSWeaponManager.Instance.ActiveSlot == WeaponSlot.Melee)
                {
                    baseRun *= 1.12f;
                }
                TargetMaxSpeed = baseRun;
            }

            // Ubah arah input lokal ke arah world transform pemain
            Vector3 inputDirection = (transform.right * moveInput.x + transform.forward * moveInput.y).normalized;
            Vector3 targetVelocity = inputDirection * TargetMaxSpeed;

            // Hitung akselerasi vs deselerasi
            bool isBraking = moveInput.sqrMagnitude < 0.01f;
            float accelRate;

            if (_isGrounded)
            {
                // Jika sedang mengerem atau counter-strafe (berubah arah tajam), gunakan groundDeceleration
                bool isCounterStrafing = Vector3.Dot(_horizontalVelocity.normalized, targetVelocity.normalized) < 0.1f && !isBraking;
                accelRate = (isBraking || isCounterStrafing) ? groundDeceleration : groundAcceleration;
            }
            else
            {
                accelRate = airAcceleration;
            }

            _horizontalVelocity = Vector3.MoveTowards(_horizontalVelocity, targetVelocity, accelRate * Time.deltaTime);
        }

        /// <summary>
        /// Mengatur lompatan dengan Jump Buffer & Coyote Time, serta gravitasi taktis.
        /// </summary>
        private void HandleVerticalMovement()
        {
            // Catat input lompat ke dalam Jump Buffer
            if (inputReader != null && inputReader.ConsumeJumpTriggered())
            {
                _jumpBufferTimer = jumpBufferTime;
            }
            else if (_jumpBufferTimer > 0f)
            {
                _jumpBufferTimer -= Time.deltaTime;
            }

            // Eksekusi lompatan jika input lompat masih tersimpan dan karakter masih dalam masa toleransi tanah
            bool canJump = _jumpBufferTimer > 0f && _coyoteTimer > 0f && !_isHeadObstructed;
            if (canJump)
            {
                // Rumus kecepatan lompat dari ketinggian: v = sqrt(2 * g * h)
                _verticalVelocity = Mathf.Sqrt(2f * gravity * jumpHeight);
                _jumpBufferTimer = 0f;
                _coyoteTimer = 0f;
            }

            if (_isGrounded)
            {
                // Tetap berikan gaya ke bawah kecil agar ground snapping stabil saat jalan di turunan
                if (_verticalVelocity < 0f)
                {
                    _verticalVelocity = groundedDownwardForce;
                }
            }
            else
            {
                // Terapkan gravitasi saat di udara
                _verticalVelocity -= gravity * Time.deltaTime;
            }
        }

        /// <summary>
        /// Terapkan perpindahan akhir ke CharacterController.
        /// </summary>
        private void ApplyFinalMovement()
        {
            Vector3 finalMove = _horizontalVelocity + Vector3.up * _verticalVelocity;
            controller.Move(finalMove * Time.deltaTime);
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            // Visualisasi ceiling check kapsul berdiri di Scene View
            if (controller != null)
            {
                float radius = controller.radius * 0.90f;
                Vector3 pointBottom = transform.position + Vector3.up * (radius + 0.1f);
                Vector3 pointTop = transform.position + Vector3.up * (standingHeight - radius);

                Gizmos.color = _isHeadObstructed ? Color.red : Color.cyan;
                Gizmos.DrawWireSphere(pointBottom, radius);
                Gizmos.DrawWireSphere(pointTop, radius);
                Gizmos.DrawLine(pointBottom + Vector3.left * radius, pointTop + Vector3.left * radius);
                Gizmos.DrawLine(pointBottom + Vector3.right * radius, pointTop + Vector3.right * radius);
                Gizmos.DrawLine(pointBottom + Vector3.forward * radius, pointTop + Vector3.forward * radius);
                Gizmos.DrawLine(pointBottom + Vector3.back * radius, pointTop + Vector3.back * radius);
            }
        }
#endif
    }
}
