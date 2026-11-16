using UnityEngine;
using UnityEngine.InputSystem;

namespace FPSFramework.Combat
{
    public enum StrafeMode
    {
        Static,
        LinearStrafe,
        JigglePeek
    }

    /// <summary>
    /// Mengatur pergerakan dinamis Target Dummy untuk latihan menembak (Modul D):
    /// 1. Static: Diam di posisi anchor awal (untuk latihan spray dasar).
    /// 2. Linear Strafe: Bergerak bolak-balik ke kiri dan kanan secara konstan (untuk tracking aim).
    /// 3. Jiggle Peek (ADAD): Gerakan lincah tak tentu dengan rem counter-strafe tajam ala CS2.
    /// Dilengkapi rotasi otomatis menghadap pemain dan deteksi batas dinding arena.
    /// </summary>
    [RequireComponent(typeof(TargetDummy))]
    public class TargetDummyMovement : MonoBehaviour
    {
        [Header("Mode Pergerakan")]
        [SerializeField] private StrafeMode strafeMode = StrafeMode.LinearStrafe;

        [Header("Parameter Gerakan")]
        [Tooltip("Kecepatan gerak lateral bot dalam meter per detik.")]
        [SerializeField] private float strafeSpeed = 3.6f;

        [Tooltip("Jarak maksimal jelajah bot ke kiri dan kanan dari titik awal (meter).")]
        [SerializeField] private float strafeRadius = 4.5f;

        [Tooltip("Apakah bot selalu memutar badan menghadap ke arah pandangan pemain.")]
        [SerializeField] private bool facePlayer = true;

        [Tooltip("Kecepatan perputaran badan menghadap pemain.")]
        [SerializeField] private float rotationSpeed = 8.0f;

        [Header("Parameter Jiggle-Peek (ADAD Counter-Strafe)")]
        [Tooltip("Durasi minimum bot melaju ke satu arah sebelum berganti arah (detik).")]
        [SerializeField] private float jiggleMinDuration = 0.3f;

        [Tooltip("Durasi maksimum bot melaju ke satu arah (detik).")]
        [SerializeField] private float jiggleMaxDuration = 1.0f;

        [Tooltip("Peluang bot melakukan rem mendadak (counter-strafe stop) saat berganti arah (0 - 1).")]
        [Range(0f, 1f)]
        [SerializeField] private float jiggleStopChance = 0.45f;

        [Tooltip("Durasi bot berhenti sesaat sebelum meluncur kembali (detik).")]
        [SerializeField] private float jiggleStopDuration = 0.25f;

        [Tooltip("Akselerasi pengereman dan lonjakan gerak ADAD (semakin tinggi, semakin snappy).")]
        [SerializeField] private float jiggleAcceleration = 40.0f;

        [Header("Deteksi Batas & Dinding")]
        [SerializeField] private float wallCheckDistance = 0.75f;
        [SerializeField] private LayerMask obstacleLayers = ~0;

        [Header("Kontrol Keyboard (F4)")]
        [Tooltip("Gunakan tombol F4 untuk mengganti mode secara langsung saat Play Mode.")]
        [SerializeField] private bool enableF4Cycle = true;

        // Runtime Variables
        private TargetDummy _dummy;
        private Vector3 _anchorPosition;
        private Vector3 _strafeAxis;
        private float _currentOffset;
        private float _linearDirection = 1f;

        // Jiggle state
        private float _jiggleTimer;
        private float _jiggleTargetSpeed;
        private float _jiggleCurrentSpeed;
        private bool _isCounterStrafeStopping;

        // Feedback timer
        private float _modeNotificationTimer;
        private string _modeNotificationText = "";

        public StrafeMode Mode => strafeMode;
        public string CurrentModeTag => strafeMode.ToString().ToUpper();

        private void Awake()
        {
            _dummy = GetComponent<TargetDummy>();
            _anchorPosition = transform.position;
            _strafeAxis = transform.right; // Default sumbu samping
        }

        private void Start()
        {
            // Tentukan sumbu strafe tegak lurus terhadap arah pemain
            Camera cam = Camera.main;
            if (cam != null)
            {
                Vector3 toPlayer = cam.transform.position - _anchorPosition;
                toPlayer.y = 0f;
                if (toPlayer.sqrMagnitude > 0.1f)
                {
                    _strafeAxis = Vector3.Cross(Vector3.up, toPlayer.normalized).normalized;
                }
            }

            InitJiggleState();
        }

        private void Update()
        {
            // Toggle ganti mode dengan tombol F4 (New Input System)
            if (enableF4Cycle && Keyboard.current != null && Keyboard.current.f4Key.wasPressedThisFrame)
            {
                CycleMode();
            }

            if (_modeNotificationTimer > 0f)
            {
                _modeNotificationTimer -= Time.deltaTime;
            }

            // Hentikan pergerakan jika dummy sedang mati (roboh)
            if (_dummy != null && _dummy.IsDead)
            {
                return;
            }

            // Putar badan menghadap pemain
            if (facePlayer)
            {
                HandleFacePlayer();
            }

            // Eksekusi pergerakan sesuai mode aktif
            switch (strafeMode)
            {
                case StrafeMode.Static:
                    HandleStaticMovement();
                    break;
                case StrafeMode.LinearStrafe:
                    HandleLinearStrafeMovement();
                    break;
                case StrafeMode.JigglePeek:
                    HandleJigglePeekMovement();
                    break;
            }
        }

        private void HandleFacePlayer()
        {
            Camera cam = Camera.main;
            if (cam == null) return;

            Vector3 lookDir = cam.transform.position - transform.position;
            lookDir.y = 0f;

            if (lookDir.sqrMagnitude > 0.05f)
            {
                Quaternion targetRot = Quaternion.LookRotation(lookDir);
                Quaternion smoothRot = Quaternion.Slerp(transform.rotation, targetRot, Time.deltaTime * rotationSpeed);

                if (_dummy != null)
                {
                    _dummy.UpdateBaseRotation(smoothRot);
                }
                else
                {
                    transform.rotation = smoothRot;
                }
            }
        }

        private void HandleStaticMovement()
        {
            // Kembalikan posisi secara halus ke titik anchor
            if (Vector3.Distance(transform.position, _anchorPosition) > 0.02f)
            {
                _currentOffset = Mathf.MoveTowards(_currentOffset, 0f, strafeSpeed * Time.deltaTime);
                transform.position = _anchorPosition + (_strafeAxis * _currentOffset);
            }
        }

        private void HandleLinearStrafeMovement()
        {
            // Periksa rintangan dinding di depan arah gerak
            Vector3 rayOrigin = transform.position + Vector3.up * 0.9f;
            Vector3 moveDirection = _strafeAxis * _linearDirection;

            if (Physics.Raycast(rayOrigin, moveDirection, wallCheckDistance, obstacleLayers))
            {
                _linearDirection = -_linearDirection;
            }

            // Akumulasi jarak
            _currentOffset += _linearDirection * strafeSpeed * Time.deltaTime;

            // Batas radius jelajah
            if (Mathf.Abs(_currentOffset) >= strafeRadius)
            {
                _linearDirection = -Mathf.Sign(_currentOffset);
                _currentOffset = Mathf.Sign(_currentOffset) * strafeRadius;
            }

            transform.position = _anchorPosition + (_strafeAxis * _currentOffset);
        }

        private void HandleJigglePeekMovement()
        {
            _jiggleTimer -= Time.deltaTime;

            if (_jiggleTimer <= 0f)
            {
                // Tentukan aksi berikutnya: Berhenti sesaat (counter-strafe) atau melesat ke arah berlawanan
                if (!_isCounterStrafeStopping && Random.value < jiggleStopChance)
                {
                    _isCounterStrafeStopping = true;
                    _jiggleTargetSpeed = 0f;
                    _jiggleTimer = Random.Range(0.12f, jiggleStopDuration);
                }
                else
                {
                    _isCounterStrafeStopping = false;
                    float newDirection = (Random.value > 0.5f) ? 1f : -1f;
                    if (Mathf.Abs(_currentOffset) > strafeRadius * 0.8f)
                    {
                        // Jika sudah dekat batas, paksa arah berbalik ke tengah
                        newDirection = -Mathf.Sign(_currentOffset);
                    }

                    _jiggleTargetSpeed = newDirection * strafeSpeed;
                    _jiggleTimer = Random.Range(jiggleMinDuration, jiggleMaxDuration);
                }
            }

            // Deteksi tabrakan dinding
            if (Mathf.Abs(_jiggleTargetSpeed) > 0.1f)
            {
                Vector3 rayOrigin = transform.position + Vector3.up * 0.9f;
                Vector3 moveDirection = _strafeAxis * Mathf.Sign(_jiggleTargetSpeed);

                if (Physics.Raycast(rayOrigin, moveDirection, wallCheckDistance, obstacleLayers))
                {
                    _jiggleTargetSpeed = -_jiggleTargetSpeed;
                    _isCounterStrafeStopping = false;
                    _jiggleTimer = Random.Range(jiggleMinDuration, jiggleMaxDuration);
                }
            }

            // Akselerasi snappy (Counter-strafing feel)
            _jiggleCurrentSpeed = Mathf.MoveTowards(_jiggleCurrentSpeed, _jiggleTargetSpeed, jiggleAcceleration * Time.deltaTime);
            _currentOffset += _jiggleCurrentSpeed * Time.deltaTime;

            // Kunci dalam batas radius
            if (Mathf.Abs(_currentOffset) > strafeRadius)
            {
                _currentOffset = Mathf.Sign(_currentOffset) * strafeRadius;
                _jiggleTargetSpeed = -_jiggleTargetSpeed;
            }

            transform.position = _anchorPosition + (_strafeAxis * _currentOffset);
        }

        private void InitJiggleState()
        {
            _jiggleCurrentSpeed = 0f;
            _jiggleTargetSpeed = strafeSpeed;
            _jiggleTimer = Random.Range(jiggleMinDuration, jiggleMaxDuration);
            _isCounterStrafeStopping = false;
        }

        /// <summary>
        /// Mengatur mode bot secara manual (misal saat tombol target ditembak).
        /// </summary>
        public void SetMode(StrafeMode newMode)
        {
            strafeMode = newMode;
            if (strafeMode == StrafeMode.JigglePeek)
            {
                InitJiggleState();
            }

            _modeNotificationText = $"BOT MODE: {CurrentModeTag}";
            _modeNotificationTimer = 2.0f;
        }

        /// <summary>
        /// Beralih ke mode berikutnya (Static -> Linear -> Jiggle -> Static).
        /// </summary>
        public void CycleMode()
        {
            StrafeMode nextMode = (StrafeMode)(((int)strafeMode + 1) % 3);
            SetMode(nextMode);
        }

        private void OnGUI()
        {
            // Notifikasi perubahan mode di layar atas
            if (_modeNotificationTimer > 0f)
            {
                float alpha = Mathf.Clamp01(_modeNotificationTimer / 0.5f);
                float w = 280f;
                float h = 32f;
                float x = (Screen.width - w) / 2f;
                float y = 95f;

                GUIStyle style = new GUIStyle(GUI.skin.box)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 12,
                    fontStyle = FontStyle.Bold
                };
                style.normal.textColor = new Color(0.3f, 0.9f, 1f, alpha);

                GUI.color = new Color(1f, 1f, 1f, alpha);
                GUI.Box(new Rect(x, y, w, h), $"[ {_modeNotificationText} (F4) ]", style);
                GUI.color = Color.white;
            }
        }
    }
}
