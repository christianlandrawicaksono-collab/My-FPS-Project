using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FPSFramework.Player
{
    /// <summary>
    /// Membaca input dari Unity New Input System dan mengeksposnya secara terstruktur.
    /// Mendukung keyboard/mouse dan gamepad dengan fallback aman.
    /// </summary>
    public class FPSInputReader : MonoBehaviour
    {
        [Header("Input Asset (Optional - Akan auto load jika kosong)")]
        [Tooltip("Asset Input Actions. Jika kosong, akan otomatis dicari di project.")]
        [SerializeField] private InputActionAsset inputActions;

        [Header("Mode Pengaturan")]
        [Tooltip("Jika dicentang, menekan tombol Crouch akan toggle (klik sekali jongkok, klik lagi berdiri). Jika tidak, harus ditahan (hold).")]
        [SerializeField] private bool toggleCrouch = false;

        [Tooltip("Jika dicentang, menekan tombol Walk (Shift) akan toggle.")]
        [SerializeField] private bool toggleWalk = false;

        // Input Values
        public Vector2 MoveInput { get; private set; }
        public Vector2 LookInput { get; private set; }
        public bool JumpTriggered { get; private set; }
        public bool IsCrouchActive { get; private set; }
        public bool IsWalkActive { get; private set; }
        public bool IsFireHeld { get; private set; }
        public bool FireTriggered { get; private set; }
        public bool ReloadTriggered { get; private set; }

        // Action references
        private InputActionMap _playerMap;
        private InputAction _moveAction;
        private InputAction _lookAction;
        private InputAction _jumpAction;
        private InputAction _crouchAction;
        private InputAction _sprintAction; // Digunakan sebagai Walk/Silent di tactical shooter
        private InputAction _attackAction;

        private bool _crouchToggleState;
        private bool _walkToggleState;

        private void Awake()
        {
            InitializeInputActions();
        }

        private void InitializeInputActions()
        {
            // Jika belum di-assign di Inspector, coba cari asset InputSystem_Actions
            if (inputActions == null)
            {
                inputActions = Resources.Load<InputActionAsset>("InputSystem_Actions");
            }

            if (inputActions != null)
            {
                _playerMap = inputActions.FindActionMap("Player", false);

                if (_playerMap != null)
                {
                    _moveAction = _playerMap.FindAction("Move", false);
                    _lookAction = _playerMap.FindAction("Look", false);
                    _jumpAction = _playerMap.FindAction("Jump", false);
                    _crouchAction = _playerMap.FindAction("Crouch", false);
                    _sprintAction = _playerMap.FindAction("Sprint", false);
                    _attackAction = _playerMap.FindAction("Attack", false);

                    BindActionCallbacks();
                    _playerMap.Enable();
                }
                else
                {
                    Debug.LogWarning("[FPSInputReader] Action Map 'Player' tidak ditemukan di InputActionAsset.");
                }
            }
        }

        private void BindActionCallbacks()
        {
            if (_jumpAction != null)
            {
                _jumpAction.performed += OnJumpPerformed;
            }

            if (_crouchAction != null)
            {
                _crouchAction.performed += OnCrouchPerformed;
                _crouchAction.canceled += OnCrouchCanceled;
            }

            if (_sprintAction != null)
            {
                _sprintAction.performed += OnWalkPerformed;
                _sprintAction.canceled += OnWalkCanceled;
            }
        }

        private void UnbindActionCallbacks()
        {
            if (_jumpAction != null)
            {
                _jumpAction.performed -= OnJumpPerformed;
            }

            if (_crouchAction != null)
            {
                _crouchAction.performed -= OnCrouchPerformed;
                _crouchAction.canceled -= OnCrouchCanceled;
            }

            if (_sprintAction != null)
            {
                _sprintAction.performed -= OnWalkPerformed;
                _sprintAction.canceled -= OnWalkCanceled;
            }
        }

        private void OnEnable()
        {
            if (_playerMap != null && !_playerMap.enabled)
            {
                _playerMap.Enable();
            }
        }

        private void OnDisable()
        {
            if (_playerMap != null && _playerMap.enabled)
            {
                _playerMap.Disable();
            }
        }

        private void OnDestroy()
        {
            UnbindActionCallbacks();
        }

        private void Update()
        {
            // Update Vector2 inputs continuous
            if (_moveAction != null)
            {
                MoveInput = _moveAction.ReadValue<Vector2>();
            }
            else
            {
                // Fallback ke Keyboard langsung jika belum terikat
                MoveInput = ReadKeyboardFallbackMove();
            }

            if (_lookAction != null)
            {
                LookInput = _lookAction.ReadValue<Vector2>();
            }
            else
            {
                // Fallback Mouse delta
                LookInput = ReadMouseFallbackLook();
            }

            // Update Fire input
            if (_attackAction != null)
            {
                IsFireHeld = _attackAction.IsPressed();
                FireTriggered = _attackAction.WasPressedThisFrame();
            }
            else if (Mouse.current != null)
            {
                IsFireHeld = Mouse.current.leftButton.isPressed;
                FireTriggered = Mouse.current.leftButton.wasPressedThisFrame;
            }
            else
            {
                IsFireHeld = false;
                FireTriggered = false;
            }

            // Update Reload input (Key R)
            if (Keyboard.current != null)
            {
                ReloadTriggered = Keyboard.current.rKey.wasPressedThisFrame;
            }
            else
            {
                ReloadTriggered = false;
            }
        }

        /// <summary>
        /// Mengambil status FireTriggered dan langsung me-reset-nya.
        /// </summary>
        public bool ConsumeFireTriggered()
        {
            if (FireTriggered)
            {
                FireTriggered = false;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Mengambil status ReloadTriggered dan langsung me-reset-nya.
        /// </summary>
        public bool ConsumeReloadTriggered()
        {
            if (ReloadTriggered)
            {
                ReloadTriggered = false;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Event yang dipanggil sesaat ketika tombol Jump ditekan.
        /// </summary>
        public event Action OnJumpEvent;

        /// <summary>
        /// Mengambil status JumpTriggered dan langsung me-reset-nya agar tidak dieksekusi ganda.
        /// </summary>
        public bool ConsumeJumpTriggered()
        {
            if (JumpTriggered)
            {
                JumpTriggered = false;
                return true;
            }
            return false;
        }

        private void OnJumpPerformed(InputAction.CallbackContext ctx)
        {
            JumpTriggered = true;
            OnJumpEvent?.Invoke();
        }

        private void OnCrouchPerformed(InputAction.CallbackContext ctx)
        {
            if (toggleCrouch)
            {
                _crouchToggleState = !_crouchToggleState;
                IsCrouchActive = _crouchToggleState;
            }
            else
            {
                IsCrouchActive = true;
            }
        }

        private void OnCrouchCanceled(InputAction.CallbackContext ctx)
        {
            if (!toggleCrouch)
            {
                IsCrouchActive = false;
            }
        }

        private void OnWalkPerformed(InputAction.CallbackContext ctx)
        {
            if (toggleWalk)
            {
                _walkToggleState = !_walkToggleState;
                IsWalkActive = _walkToggleState;
            }
            else
            {
                IsWalkActive = true;
            }
        }

        private void OnWalkCanceled(InputAction.CallbackContext ctx)
        {
            if (!toggleWalk)
            {
                IsWalkActive = false;
            }
        }

        /// <summary>
        /// Paksa nonaktifkan status crouch jika dibutuhkan (misal saat dipaksa berdiri).
        /// </summary>
        public void ResetCrouch()
        {
            _crouchToggleState = false;
            IsCrouchActive = false;
        }

        #region Fallback Input (Jaga-jaga jika InputActionAsset belum terpasang)
        private Vector2 ReadKeyboardFallbackMove()
        {
            float x = 0f;
            float y = 0f;
            if (Keyboard.current != null)
            {
                if (Keyboard.current.wKey.isPressed || Keyboard.current.upArrowKey.isPressed) y += 1f;
                if (Keyboard.current.sKey.isPressed || Keyboard.current.downArrowKey.isPressed) y -= 1f;
                if (Keyboard.current.aKey.isPressed || Keyboard.current.leftArrowKey.isPressed) x -= 1f;
                if (Keyboard.current.dKey.isPressed || Keyboard.current.rightArrowKey.isPressed) x += 1f;
                if (Keyboard.current.spaceKey.wasPressedThisFrame)
                {
                    JumpTriggered = true;
                    OnJumpEvent?.Invoke();
                }
                if (Keyboard.current.leftCtrlKey.isPressed || Keyboard.current.cKey.isPressed) IsCrouchActive = true;
                else if (!toggleCrouch) IsCrouchActive = false;
                if (Keyboard.current.leftShiftKey.isPressed) IsWalkActive = true;
                else if (!toggleWalk) IsWalkActive = false;
            }
            return new Vector2(x, y).normalized;
        }

        private Vector2 ReadMouseFallbackLook()
        {
            if (Mouse.current != null)
            {
                return Mouse.current.delta.ReadValue();
            }
            return Vector2.zero;
        }
        #endregion
    }
}
