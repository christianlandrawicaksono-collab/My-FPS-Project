using UnityEngine;
using UnityEngine.InputSystem;

namespace FPSFramework.Player
{
    /// <summary>
    /// Mengatur kontrol kamera First-Person:
    /// - Mouse look tanpa gimbal lock (Yaw pada player body, Pitch clamped pada camera).
    /// - Transisi ketinggian mata yang mulus saat berdiri vs jongkok.
    /// - Head bobbing taktis yang halus saat berjalan/berlari.
    /// - Penguncian kursor mouse (Cursor lock & unlock).
    /// </summary>
    public class FPSCameraController : MonoBehaviour
    {
        [Header("Referensi Objek")]
        [Tooltip("Transform dari root badan player (Capsule) yang akan berotasi secara horizontal (Yaw).")]
        [SerializeField] private Transform playerBody;

        [Tooltip("Transform pivot kamera yang posisinya akan dinaik-turunkan saat crouch.")]
        [SerializeField] private Transform cameraPivot;

        [Tooltip("Referensi input reader. Jika kosong, akan dicari di komponen bodi pemain.")]
        [SerializeField] private FPSInputReader inputReader;

        [Header("Sensitivitas Mouse")]
        [Range(0.1f, 10f)]
        [SerializeField] private float mouseSensitivityX = 1.5f;

        [Range(0.1f, 10f)]
        [SerializeField] private float mouseSensitivityY = 1.5f;

        [Tooltip("Balikkan sumbu Y (vertikal).")]
        [SerializeField] private bool invertY = false;

        [Header("Batas Sudut Vertikal (Pitch Clamp)")]
        [SerializeField] private float minPitchAngle = -75f;
        [SerializeField] private float maxPitchAngle = 70f;

        [Header("Ketinggian Mata (Eye Heights)")]
        [Tooltip("Posisi lokal Y kamera saat berdiri normal.")]
        [SerializeField] private float standingEyeHeight = 1.65f;

        [Tooltip("Posisi lokal Y kamera saat jongkok (crouch).")]
        [SerializeField] private float crouchingEyeHeight = 1.05f;

        [Tooltip("Offset maju kamera (Z) agar pas berada di depan mata/visor helm karakter.")]
        [SerializeField] private float forwardEyeOffset = 0.22f;

        [Tooltip("Kecepatan transisi tinggi kamera (semakin tinggi semakin responsif).")]
        [SerializeField] private float heightTransitionSpeed = 12f;

        [Header("Head Bobbing (Opsional)")]
        [SerializeField] private bool enableHeadBob = true;
        [SerializeField] private float bobWalkSpeed = 8f;
        [SerializeField] private float bobRunSpeed = 12f;
        [SerializeField] private float bobWalkAmount = 0.02f;
        [SerializeField] private float bobRunAmount = 0.04f;

        [Header("Camera Recoil Punch (CS/Valorant)")]
        [Tooltip("Kecepatan kamera menghentak saat menerima recoil.")]
        [SerializeField] private float recoilSnappiness = 18f;

        [Tooltip("Kecepatan kamera pulih kembali ke titik bidik awal.")]
        [SerializeField] private float recoilRecoverySpeed = 10f;

        [Header("Kursor")]
        [SerializeField] private bool lockCursorOnStart = true;

        // State internal
        private float _currentPitch = 0f;
        private float _currentEyeHeight;
        private float _targetEyeHeight;
        private float _bobTimer = 0f;
        private Vector3 _originalCameraLocalPos;
        private IFPSMovementState _movementState;

        private Vector2 _targetRecoil = Vector2.zero;
        private Vector2 _currentRecoil = Vector2.zero;

        private void Awake()
        {
            if (playerBody == null && transform.parent != null)
            {
                playerBody = transform.root;
            }

            if (cameraPivot == null)
            {
                cameraPivot = transform;
            }

            if (inputReader == null && playerBody != null)
            {
                inputReader = playerBody.GetComponent<FPSInputReader>();
            }

            if (playerBody != null)
            {
                _movementState = playerBody.GetComponent<IFPSMovementState>();
            }

            Camera cam = GetComponentInChildren<Camera>();
            if (cam == null && Camera.main != null) cam = Camera.main;
            if (cam != null && cam.nearClipPlane > 0.03f)
            {
                cam.nearClipPlane = 0.03f;
            }

            _originalCameraLocalPos = transform.localPosition;
            _currentEyeHeight = standingEyeHeight;
            _targetEyeHeight = standingEyeHeight;

            Transform targetHeightTransform = cameraPivot != null ? cameraPivot : transform;
            Vector3 initPos = targetHeightTransform.localPosition;
            initPos.y = standingEyeHeight;
            initPos.z = forwardEyeOffset;
            targetHeightTransform.localPosition = initPos;
        }

        private void Start()
        {
            if (lockCursorOnStart)
            {
                SetCursorLocked(true);
            }
        }

        private void Update()
        {
            HandleCursorToggle();
            HandleLookRotation();
            HandleEyeHeightTransition();
            HandleHeadBobbing();
        }

        /// <summary>
        /// Menerima sentakan recoil dari senjata (kickAngle.y = pitch angkat atas, kickAngle.x = yaw geser samping).
        /// </summary>
        public void AddRecoilKick(Vector2 kickAngle)
        {
            _targetRecoil += kickAngle;
        }

        /// <summary>
        /// Mengatur rotasi mouse: horizontal memutar badan, vertikal memutar pivot kamera.
        /// Menggabungkan sentakan kamera recoil dengan pemulihan elastis (spring recovery).
        /// </summary>
        private void HandleLookRotation()
        {
            if (Cursor.lockState != CursorLockMode.Locked) return;

            Vector2 lookInput = inputReader != null ? inputReader.LookInput : Vector2.zero;

            // Faktor pengali konstanta agar sensitivitas terasa alami
            float sensitivityMultiplier = 0.1f;
            float mouseX = lookInput.x * mouseSensitivityX * sensitivityMultiplier;
            float mouseY = lookInput.y * mouseSensitivityY * sensitivityMultiplier * (invertY ? 1f : -1f);

            // 1. Update Recoil Spring Recovery
            _targetRecoil = Vector2.Lerp(_targetRecoil, Vector2.zero, Time.deltaTime * recoilRecoverySpeed);
            _currentRecoil = Vector2.Lerp(_currentRecoil, _targetRecoil, Time.deltaTime * recoilSnappiness);

            // 2. Vertikal (Pitch) pada kamera + Recoil Pitch
            _currentPitch = Mathf.Clamp(_currentPitch + mouseY, minPitchAngle, maxPitchAngle);
            float finalPitch = Mathf.Clamp(_currentPitch + _currentRecoil.y, minPitchAngle, maxPitchAngle);
            float finalYawRecoil = _currentRecoil.x;

            transform.localRotation = Quaternion.Euler(finalPitch, finalYawRecoil, 0f);

            // 3. Horizontal (Yaw) pada badan karakter
            if (playerBody != null)
            {
                playerBody.Rotate(Vector3.up * mouseX);
            }
        }

        /// <summary>
        /// Melakukan interpolasi halus posisi Y kamera antara berdiri dan jongkok.
        /// </summary>
        private void HandleEyeHeightTransition()
        {
            bool isCrouching = _movementState != null ? _movementState.IsCrouching : (inputReader != null && inputReader.IsCrouchActive);

            _targetEyeHeight = isCrouching ? crouchingEyeHeight : standingEyeHeight;
            _currentEyeHeight = Mathf.Lerp(_currentEyeHeight, _targetEyeHeight, Time.deltaTime * heightTransitionSpeed);

            Transform targetHeightTransform = cameraPivot != null ? cameraPivot : transform;
            Vector3 pos = targetHeightTransform.localPosition;
            pos.y = _currentEyeHeight;
            pos.z = forwardEyeOffset;
            targetHeightTransform.localPosition = pos;
        }

        /// <summary>
        /// Efek head bobbing halus saat bergerak di atas tanah.
        /// </summary>
        private void HandleHeadBobbing()
        {
            if (!enableHeadBob) return;

            bool isGrounded = _movementState == null || _movementState.IsGrounded;
            float speed = _movementState != null ? _movementState.CurrentHorizontalSpeed : 0f;

            if (isGrounded && speed > 0.5f)
            {
                bool isWalking = _movementState != null && _movementState.IsWalking;
                float bobFreq = isWalking ? bobWalkSpeed : bobRunSpeed;
                float bobAmount = isWalking ? bobWalkAmount : bobRunAmount;

                _bobTimer += Time.deltaTime * bobFreq;
                float bobOffsetY = Mathf.Sin(_bobTimer) * bobAmount;
                float bobOffsetX = Mathf.Cos(_bobTimer * 0.5f) * (bobAmount * 0.5f);

                transform.localPosition = new Vector3(_originalCameraLocalPos.x + bobOffsetX, _originalCameraLocalPos.y + bobOffsetY, _originalCameraLocalPos.z);
            }
            else
            {
                _bobTimer = 0f;
                transform.localPosition = Vector3.Lerp(transform.localPosition, _originalCameraLocalPos, Time.deltaTime * 6f);
            }
        }

        /// <summary>
        /// Tekan ESC untuk unlock mouse (memudahkan inspect di editor), klik mouse untuk lock kembali.
        /// </summary>
        private void HandleCursorToggle()
        {
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                SetCursorLocked(Cursor.lockState != CursorLockMode.Locked);
            }

            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame && Cursor.lockState != CursorLockMode.Locked)
            {
                SetCursorLocked(true);
            }
        }

        public void SetCursorLocked(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }

        public void SetPitch(float pitch)
        {
            _currentPitch = Mathf.Clamp(pitch, minPitchAngle, maxPitchAngle);
            transform.localRotation = Quaternion.Euler(_currentPitch, 0f, 0f);
        }
    }
}
