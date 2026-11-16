using UnityEngine;

namespace FPSFramework.Player
{
    /// <summary>
    /// Menghubungkan data status pergerakan pemain (IFPSMovementState & FPSInputReader) 
    /// ke komponen Animator pada model karakter (Mixamo / 3rd Person / Full-body).
    /// Mengatur parameter Blend Tree: MoveX, MoveY, Speed, SpeedMultiplier, IsCrouching, IsGrounded, dan Die.
    /// </summary>
    public class FPSAnimationController : MonoBehaviour
    {
        [Header("Referensi Komponen")]
        [Tooltip("Komponen Animator pada model karakter. Jika kosong, akan dicari otomatis di child.")]
        [SerializeField] private Animator animator;

        [Tooltip("Pembaca input pemain. Jika kosong, dicari otomatis di GameObject ini.")]
        [SerializeField] private FPSInputReader inputReader;

        [Header("Pengaturan Damping Animasi")]
        [Tooltip("Waktu kehalusan transisi nilai float ke Animator (mencegah animasi patah-patah).")]
        [SerializeField] private float dampTime = 0.1f;

        [Tooltip("Kecepatan lari patokan untuk menghitung rasio kecepatan putar animasi lari vs jalan.")]
        [SerializeField] private float baseRunSpeed = 6.2f;

        // Hash ID parameter Animator untuk performa maksimal (tanpa alokasi string tiap frame)
        private static readonly int MoveXHash = Animator.StringToHash("MoveX");
        private static readonly int MoveYHash = Animator.StringToHash("MoveY");
        private static readonly int SpeedHash = Animator.StringToHash("Speed");
        private static readonly int SpeedMultiplierHash = Animator.StringToHash("SpeedMultiplier");
        private static readonly int IsCrouchingHash = Animator.StringToHash("IsCrouching");
        private static readonly int IsGroundedHash = Animator.StringToHash("IsGrounded");
        private static readonly int DieHash = Animator.StringToHash("Die");

        private IFPSMovementState _movementState;

        private void Awake()
        {
            // Cari Animator jika belum di-assign
            if (animator == null)
            {
                animator = GetComponentInChildren<Animator>();
            }

            // Cari Input Reader jika belum di-assign
            if (inputReader == null)
            {
                inputReader = GetComponent<FPSInputReader>();
            }

            // Cari state movement
            _movementState = GetComponent<IFPSMovementState>();
            if (_movementState == null)
            {
                _movementState = GetComponentInParent<IFPSMovementState>();
            }
        }

        private void Update()
        {
            if (animator == null || animator.runtimeAnimatorController == null) return;

            UpdateLocomotionParameters();
        }

        /// <summary>
        /// Mengirimkan data input dan kecepatan horizontal lokal ke Animator.
        /// </summary>
        private void UpdateLocomotionParameters()
        {
            Vector2 moveInput = inputReader != null ? inputReader.MoveInput : Vector2.zero;
            float currentSpeed = _movementState != null ? _movementState.CurrentHorizontalSpeed : 0f;
            bool isGrounded = _movementState == null || _movementState.IsGrounded;
            bool isCrouching = _movementState != null && _movementState.IsCrouching;

            // 1. Kirim input terarah ke 2D Blend Tree (MoveX = Strafe A/D, MoveY = Maju/Mundur W/S)
            // Menggunakan dampTime agar transisi arah gerakan mulus dan realistis
            animator.SetFloat(MoveXHash, moveInput.x, dampTime, Time.deltaTime);
            animator.SetFloat(MoveYHash, moveInput.y, dampTime, Time.deltaTime);

            // 2. Kecepatan horizontal riil
            animator.SetFloat(SpeedHash, currentSpeed);

            // 3. Multiplier kecepatan putar animasi:
            // Jika pemain berjalan pelan (Shift Walk), animasi langkah kaki otomatis melambat secara proporsional
            float speedMultiplier = 1.0f;
            if (currentSpeed > 0.1f)
            {
                speedMultiplier = Mathf.Clamp(currentSpeed / baseRunSpeed, 0.35f, 1.25f);
            }
            animator.SetFloat(SpeedMultiplierHash, speedMultiplier);

            // 4. Status Crouch dan Grounded
            animator.SetBool(IsCrouchingHash, isCrouching);
            animator.SetBool(IsGroundedHash, isGrounded);
        }

        /// <summary>
        /// Panggil fungsi ini saat pemain tereliminasi / mati untuk memicu animasi 'death from front headshot'.
        /// </summary>
        public void TriggerDeath()
        {
            if (animator != null)
            {
                animator.SetTrigger(DieHash);
            }
        }

        /// <summary>
        /// Menghubungkan animator baru secara dinamis jika model karakter diganti saat runtime.
        /// </summary>
        public void SetTargetAnimator(Animator newAnimator)
        {
            animator = newAnimator;
        }
    }
}
