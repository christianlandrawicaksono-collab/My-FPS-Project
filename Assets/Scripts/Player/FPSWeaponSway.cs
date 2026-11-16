using UnityEngine;

namespace FPSFramework.Player
{
    /// <summary>
    /// Sistem Weapon Sway & Viewmodel Bobbing ala Tactical Shooter (CS / Valorant / CoD).
    /// Mengontrol gerak natural senjata agar tidak kaku:
    /// 1. Mouse Look Sway: Senjata berayun lembut mengikuti putaran mouse.
    /// 2. Movement Strafe Tilt: Senjata miring natural saat strafe ke kiri/kanan.
    /// 3. Step Bobbing: Ayunan langkah kaki berirama (Figure-8) saat jalan/lari.
    /// 4. Idle Breathing: Ayunan nafas halus saat diam.
    /// 5. Jump / Fall Inertia: Sentakan saat melompat dan mendarat.
    /// </summary>
    public class FPSWeaponSway : MonoBehaviour
    {
        [Header("Referensi Komponen")]
        [Tooltip("Input Reader player. Jika kosong, akan mencari di parent.")]
        [SerializeField] private FPSInputReader inputReader;

        [Header("Mouse Look Sway (Position)")]
        [Tooltip("Berapa jauh posisi senjata bergeser saat mouse bergerak.")]
        [SerializeField] private float swayAmount = 0.015f;

        [Tooltip("Batas maksimal pergeseran posisi senjata.")]
        [SerializeField] private float maxSwayDistance = 0.04f;

        [Header("Mouse Look Sway (Rotation)")]
        [Tooltip("Berapa derajat rotasi senjata berayun saat mouse berputar.")]
        [SerializeField] private float rotationSwayAmount = 2.0f;

        [Tooltip("Batas maksimal sudut rotasi sway.")]
        [SerializeField] private float maxRotationSwayAngle = 5.0f;

        [Header("Movement Strafe Tilt")]
        [Tooltip("Kemiringan senjata (Roll Z) saat strafe ke kiri/kanan (A/D).")]
        [SerializeField] private float strafeTiltAngle = 2.5f;

        [Header("Walk & Run Bobbing")]
        [Tooltip("Kecepatan ayunan langkah saat lari.")]
        [SerializeField] private float runBobSpeed = 10f;

        [Tooltip("Kecepatan ayunan langkah saat jalan pelan (Shift).")]
        [SerializeField] private float walkBobSpeed = 6.5f;

        [Tooltip("Amplitudo horizontal bobbing (kiri-kanan).")]
        [SerializeField] private float bobAmountX = 0.008f;

        [Tooltip("Amplitudo vertikal bobbing (naik-turun).")]
        [SerializeField] private float bobAmountY = 0.006f;

        [Header("Idle Breathing")]
        [Tooltip("Ayunan halus nafas saat diam.")]
        [SerializeField] private float idleBreathSpeed = 2f;
        [SerializeField] private float idleBreathAmount = 0.0015f;

        [Header("Jump Inertia")]
        [Tooltip("Penurunan senjata saat melompat/di udara.")]
        [SerializeField] private float jumpDipAmount = 0.025f;

        [Header("Kehalusan (Smoothing)")]
        [Tooltip("Kecepatan interpolasi posisi sway.")]
        [SerializeField] private float positionSmoothSpeed = 12f;

        [Tooltip("Kecepatan interpolasi rotasi sway.")]
        [SerializeField] private float rotationSmoothSpeed = 10f;

        [Header("Wall Collision Retraction (Anti-Clipping)")]
        [Tooltip("Aktifkan penarikan posisi senjata otomatis saat mendekati dinding agar moncong laras tidak amblas menembus tembok.")]
        [SerializeField] private bool enableWallRetraction = true;

        [Tooltip("Jarak deteksi dinding dari posisi kamera (dalam meter).")]
        [SerializeField] private float wallDetectionDistance = 0.52f;

        [Tooltip("Batas maksimal jarak senjata ditarik mundur (Z) ke arah dada.")]
        [SerializeField] private float maxRetractionDistance = 0.18f;

        [Tooltip("Jarak senjata diturunkan (Y) saat merapat ke dinding.")]
        [SerializeField] private float retractionDipAmount = 0.02f;

        [Tooltip("Sudut rotasi tunduk (Pitch) saat laras senjata mendekati dinding (Low-Ready feel).")]
        [SerializeField] private float wallTiltAngle = 4.5f;

        [Tooltip("Kecepatan transisi penarikan dan pemulihan senjata.")]
        [SerializeField] private float retractionSmoothSpeed = 14f;

        [Tooltip("Layer mask untuk mendeteksi dinding penghalang.")]
        [SerializeField] private LayerMask wallObstacleLayers = ~0;

        // Internal State
        private Vector3 _initialLocalPos;
        private Quaternion _initialLocalRot;
        private IFPSMovementState _movementState;
        private Transform _cameraTransform;

        private float _bobTimer;
        private float _currentJumpDip;
        private float _currentWallRetraction;

        private void Awake()
        {
            _initialLocalPos = transform.localPosition;
            _initialLocalRot = transform.localRotation;

            if (inputReader == null)
            {
                inputReader = GetComponentInParent<FPSInputReader>();
            }

            _movementState = GetComponentInParent<IFPSMovementState>();
            _cameraTransform = transform.parent != null ? transform.parent : (Camera.main != null ? Camera.main.transform : null);

            // Exclude Player layer and Ignore Raycast to prevent detecting the player's own body
            Transform rootPlayer = transform.root;
            if (rootPlayer != null)
            {
                wallObstacleLayers &= ~(1 << rootPlayer.gameObject.layer);
                Collider[] rootCols = rootPlayer.GetComponentsInChildren<Collider>();
                foreach (var c in rootCols)
                {
                    wallObstacleLayers &= ~(1 << c.gameObject.layer);
                }
            }

            int ignoreRaycastLayer = LayerMask.NameToLayer("Ignore Raycast");
            if (ignoreRaycastLayer >= 0)
            {
                wallObstacleLayers &= ~(1 << ignoreRaycastLayer);
            }
        }

        private void LateUpdate()
        {
            UpdatePositionSway();
            UpdateRotationSway();
        }

        private void UpdatePositionSway()
        {
            Vector2 mouseDelta = inputReader != null ? inputReader.LookInput : Vector2.zero;

            // 1. Mouse Position Sway (Inertia tertinggal berlawanan arah gerakan)
            float swayX = Mathf.Clamp(-mouseDelta.x * swayAmount, -maxSwayDistance, maxSwayDistance);
            float swayY = Mathf.Clamp(-mouseDelta.y * swayAmount, -maxSwayDistance, maxSwayDistance);
            Vector3 targetSwayPos = new Vector3(swayX, swayY, 0f);

            // 2. Bobbing (Langkah Kaki & Nafas)
            Vector3 targetBobPos = CalculateBobbing();

            // 3. Jump / Fall Dip
            if (_movementState != null && !_movementState.IsGrounded)
            {
                _currentJumpDip = Mathf.Lerp(_currentJumpDip, -jumpDipAmount, Time.deltaTime * 6f);
            }
            else
            {
                _currentJumpDip = Mathf.Lerp(_currentJumpDip, 0f, Time.deltaTime * 10f);
            }

            // 4. Wall Collision Retraction (Anti-Clipping)
            float targetRetraction = 0f;
            if (enableWallRetraction)
            {
                if (_cameraTransform == null && Camera.main != null)
                {
                    _cameraTransform = Camera.main.transform;
                }

                if (_cameraTransform != null)
                {
                    Vector3 rayOrigin = _cameraTransform.position + (_cameraTransform.right * 0.12f) - (_cameraTransform.up * 0.04f);
                    Vector3 rayDir = _cameraTransform.forward;

                    if (Physics.SphereCast(rayOrigin, 0.08f, rayDir, out RaycastHit wallHit, wallDetectionDistance, wallObstacleLayers, QueryTriggerInteraction.Ignore))
                    {
                        float hitDist = Mathf.Max(0.01f, wallHit.distance);
                        if (hitDist < wallDetectionDistance)
                        {
                            float penetration = wallDetectionDistance - hitDist;
                            targetRetraction = Mathf.Clamp(penetration * 1.15f, 0f, maxRetractionDistance);
                        }
                    }
                }
            }

            _currentWallRetraction = Mathf.Lerp(_currentWallRetraction, targetRetraction, Time.deltaTime * retractionSmoothSpeed);

            Vector3 wallOffset = new Vector3(
                _currentWallRetraction * 0.04f,
                -_currentWallRetraction * (retractionDipAmount / Mathf.Max(0.01f, maxRetractionDistance)),
                -_currentWallRetraction
            );

            Vector3 finalTargetPos = _initialLocalPos + targetSwayPos + targetBobPos + new Vector3(0f, _currentJumpDip, 0f) + wallOffset;
            transform.localPosition = Vector3.Lerp(transform.localPosition, finalTargetPos, Time.deltaTime * positionSmoothSpeed);
        }

        private void UpdateRotationSway()
        {
            Vector2 mouseDelta = inputReader != null ? inputReader.LookInput : Vector2.zero;
            Vector2 moveInput = inputReader != null ? inputReader.MoveInput : Vector2.zero;

            // 1. Mouse Rotation Sway
            float pitch = Mathf.Clamp(mouseDelta.y * rotationSwayAmount, -maxRotationSwayAngle, maxRotationSwayAngle);
            float yaw = Mathf.Clamp(-mouseDelta.x * rotationSwayAmount, -maxRotationSwayAngle, maxRotationSwayAngle);

            // 2. Strafe Tilt (Roll saat strafe kiri/kanan)
            float roll = -moveInput.x * strafeTiltAngle;

            // 3. Wall Collision Tilt (Moncong senjata merunduk halus ala Low-Ready saat mepet tembok)
            float retractionRatio = Mathf.Clamp01(_currentWallRetraction / Mathf.Max(0.01f, maxRetractionDistance));
            float wallPitch = retractionRatio * wallTiltAngle;
            float wallRoll = -retractionRatio * (wallTiltAngle * 0.35f);
            Quaternion wallRotation = Quaternion.Euler(wallPitch, 0f, wallRoll);

            Quaternion targetRotation = Quaternion.Euler(pitch, yaw, roll);
            Quaternion finalTargetRot = _initialLocalRot * targetRotation * wallRotation;

            transform.localRotation = Quaternion.Slerp(transform.localRotation, finalTargetRot, Time.deltaTime * rotationSmoothSpeed);
        }

        private Vector3 CalculateBobbing()
        {
            float speedRatio = _movementState != null ? _movementState.SpeedRatio : 0f;
            bool isGrounded = _movementState == null || _movementState.IsGrounded;
            bool isWalking = _movementState != null && _movementState.IsWalking;

            if (isGrounded && speedRatio > 0.05f)
            {
                // Bergerak (Walk / Run)
                float currentBobSpeed = isWalking ? walkBobSpeed : runBobSpeed;
                _bobTimer += Time.deltaTime * currentBobSpeed;

                // Figure-8 pattern: horizontal gelombang tunggal, vertikal gelombang ganda (2 langkah per siklus)
                float bobX = Mathf.Sin(_bobTimer) * (bobAmountX * speedRatio);
                float bobY = Mathf.Cos(_bobTimer * 2f) * (bobAmountY * speedRatio);

                return new Vector3(bobX, bobY, 0f);
            }
            else
            {
                // Idle Breathing halus
                _bobTimer += Time.deltaTime * idleBreathSpeed;
                float breathX = Mathf.Sin(_bobTimer * 0.5f) * (idleBreathAmount * 0.5f);
                float breathY = Mathf.Sin(_bobTimer) * idleBreathAmount;

                return new Vector3(breathX, breathY, 0f);
            }
        }

        /// <summary>
        /// Mengatur ulang posisi dasar jika Weapon_Holder digeser di Inspector saat edit mode.
        /// </summary>
        public void SetBaseTransform(Vector3 pos, Quaternion rot)
        {
            _initialLocalPos = pos;
            _initialLocalRot = rot;
        }
    }
}
