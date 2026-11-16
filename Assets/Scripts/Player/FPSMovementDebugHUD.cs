using UnityEngine;

namespace FPSFramework.Player
{
    /// <summary>
    /// HUD Debug sederhana (menggunakan OnGUI) untuk melihat status pergerakan
    /// secara real-time saat Play Mode tanpa perlu setup Canvas UI manual.
    /// Sangat berguna untuk memverifikasi akurasi CS/Valorant dan counter-strafing.
    /// </summary>
    public class FPSMovementDebugHUD : MonoBehaviour
    {
        [Header("Pengaturan Tampilan")]
        [SerializeField] private bool showHUD = true;
        [SerializeField] private FPSMovementController movementController;
        private FPSWeapon _weapon;

        [Header("CS2 Follow Recoil Setting (cl_crosshair_recoil 1)")]
        [Tooltip("Jika dicentang, crosshair akan bergerak dinamis mengikuti arah peluru recoil (fitur Follow Recoil CS2).")]
        [SerializeField] private bool followRecoil = true;

        [Tooltip("Kecepatan respon pergerakan crosshair mengikuti recoil.")]
        [SerializeField] private float followRecoilSnappiness = 32f;

        private Vector2 _smoothCrosshairScreenPos;
        private bool _isCrosshairPosInitialized = false;

        private void Awake()
        {
            if (movementController == null)
            {
                movementController = GetComponent<FPSMovementController>();
            }
        }

        private void Start()
        {
            _weapon = GetComponentInChildren<FPSWeapon>();
        }

        private void OnGUI()
        {
            // Jika FPSTacticalHUD terpasang, biarkan FPSTacticalHUD yang menangani seluruh tampilan modern
            if (GetComponent<FPSTacticalHUD>() != null) return;
            if (!showHUD || movementController == null) return;

            if (_weapon == null)
            {
                _weapon = GetComponentInChildren<FPSWeapon>();
            }

            // Panel background di kiri atas
            GUI.Box(new Rect(15, 15, 300, 260), "Tactical FPS Movement & Gunplay");

            GUILayout.BeginArea(new Rect(25, 45, 280, 220));

            // Kecepatan
            float speed = movementController.CurrentHorizontalSpeed;
            GUILayout.Label($"Speed: <b>{speed:F2} m/s</b> (Max: {movementController.TargetMaxSpeed:F1})");

            // Status Gerak
            string stateStr = "IDLE (Diam)";
            if (!movementController.IsGrounded) stateStr = "<color=red>IN AIR (Melompat)</color>";
            else if (movementController.IsCrouching) stateStr = "<color=orange>CROUCHING (Jongkok)</color>";
            else if (movementController.IsWalking) stateStr = "<color=cyan>WALKING (Jalan Pelan/Shift)</color>";
            else if (speed > 0.2f) stateStr = "<color=green>RUNNING (Lari/Default)</color>";

            GUILayout.Label($"State: {stateStr}");
            GUILayout.Label($"Grounded: {(movementController.IsGrounded ? "YES" : "NO")}");

            if (movementController.IsHeadObstructed)
            {
                GUILayout.Label("<color=red><b>⚠️ HEAD BLOCKED (Forced Crouch)</b></color>");
            }

            // Inakurasi Tembakan (CS / Valorant)
            float inaccuracy = movementController.InaccuracyModifier;
            string accColor = inaccuracy < 0.05f ? "lime" : (inaccuracy < 0.5f ? "yellow" : "red");
            string accStatus = inaccuracy < 0.05f ? "PERFECT ACCURACY" : (inaccuracy < 0.5f ? "MODERATE SPREAD" : "HIGH RECOIL / SPREAD");

            GUILayout.Label($"Inaccuracy: <color={accColor}><b>{inaccuracy:F2}x</b> ({accStatus})</color>");

            // Status Senjata (Ammo & Reload & Recoil Burst)
            if (_weapon != null)
            {
                if (_weapon.IsReloading)
                {
                    GUILayout.Label("Weapon: <color=yellow><b>RELOADING...</b></color>");
                }
                else
                {
                    string burstText = _weapon.BurstBulletIndex > 0 ? $"<color=orange>Burst: {_weapon.BurstBulletIndex}</color>" : "<color=lime>Tap-Fire (Ready)</color>";
                    GUILayout.Label($"Ammo: <b><color=white>{_weapon.CurrentAmmo}</color> / 30</b> | {burstText}");
                }
            }

            GUILayout.Space(4);
            GUILayout.Label("<size=11>Controls: WASD=Move | Shift=Walk | Ctrl=Crouch | LMB=Fire | R=Reload</size>");

            GUILayout.EndArea();

            // Dynamic Crosshair CS/Valorant Style (4 Lines + Center Gap)
            DrawDynamicCrosshair(inaccuracy);
        }

        private void DrawDynamicCrosshair(float inaccuracy)
        {
            float totalSpread = inaccuracy;
            if (_weapon != null)
            {
                totalSpread += (_weapon.CurrentSpread * 15f) + (_weapon.BurstBulletIndex * 0.035f);
            }

            float baseGap = 6f;
            float gap = baseGap + (totalSpread * 22f); // Gap membesar saat lari / spray
            float length = 10f;
            float thickness = 2f;

            // Hitung perpindahan posisi crosshair jika Follow Recoil aktif (cl_crosshair_recoil 1 ala CS2)
            Vector2 targetCrosshairPos = new Vector2(Screen.width / 2f, Screen.height / 2f);

            if (followRecoil && _weapon != null)
            {
                Camera cam = Camera.main;
                if (cam != null)
                {
                    // Proyeksikan garis bidik raycast 3D langsung ke koordinat 2D layar
                    Vector3 worldAimPoint = _weapon.GetCurrentRecoilAimPoint(30f);
                    Vector3 screenPoint = cam.WorldToScreenPoint(worldAimPoint);

                    if (screenPoint.z > 0f)
                    {
                        // Konversi Screen Space (0,0 di kiri bawah) ke OnGUI Space (0,0 di kiri atas)
                        targetCrosshairPos = new Vector2(screenPoint.x, Screen.height - screenPoint.y);
                    }
                }
            }

            if (!_isCrosshairPosInitialized)
            {
                _smoothCrosshairScreenPos = targetCrosshairPos;
                _isCrosshairPosInitialized = true;
            }
            else
            {
                // Haluskan pergerakan crosshair saat recoil memanjat dan pulih
                _smoothCrosshairScreenPos = Vector2.Lerp(_smoothCrosshairScreenPos, targetCrosshairPos, Time.deltaTime * followRecoilSnappiness);
            }

            float cx = _smoothCrosshairScreenPos.x;
            float cy = _smoothCrosshairScreenPos.y;

            Color crosshairColor = totalSpread < 0.1f ? Color.cyan : (totalSpread < 0.5f ? Color.yellow : Color.red);
            GUI.color = crosshairColor;

            // Top line
            GUI.DrawTexture(new Rect(cx - thickness / 2f, cy - gap - length, thickness, length), Texture2D.whiteTexture);
            // Bottom line
            GUI.DrawTexture(new Rect(cx - thickness / 2f, cy + gap, thickness, length), Texture2D.whiteTexture);
            // Left line
            GUI.DrawTexture(new Rect(cx - gap - length, cy - thickness / 2f, length, thickness), Texture2D.whiteTexture);
            // Right line
            GUI.DrawTexture(new Rect(cx + gap, cy - thickness / 2f, length, thickness), Texture2D.whiteTexture);

            // Center tiny dot
            float dotSize = 2f;
            GUI.DrawTexture(new Rect(cx - dotSize / 2f, cy - dotSize / 2f, dotSize, dotSize), Texture2D.whiteTexture);

            // Draw Dynamic Hitmarker "X"
            DrawHitmarker(cx, cy);
        }

        private float _hitmarkerTimer;
        private bool _isHeadshotHit;

        private void OnEnable()
        {
            if (_weapon != null)
            {
                _weapon.OnTargetHitEvent += HandleTargetHit;
            }
        }

        private void OnDisable()
        {
            if (_weapon != null)
            {
                _weapon.OnTargetHitEvent -= HandleTargetHit;
            }
        }

        private void Update()
        {
            if (_hitmarkerTimer > 0f)
            {
                _hitmarkerTimer -= Time.deltaTime;
            }

            // Hook listener jika weapon baru ditemukan
            if (_weapon == null)
            {
                _weapon = GetComponentInChildren<FPSWeapon>();
                if (_weapon != null)
                {
                    _weapon.OnTargetHitEvent += HandleTargetHit;
                }
            }
        }

        private void HandleTargetHit(bool isHeadshot)
        {
            _isHeadshotHit = isHeadshot;
            _hitmarkerTimer = 0.18f;
        }

        private void DrawHitmarker(float cx, float cy)
        {
            if (_hitmarkerTimer <= 0f) return;

            float alpha = Mathf.Clamp01(_hitmarkerTimer / 0.18f);
            Color hitColor = _isHeadshotHit ? new Color(1f, 0.2f, 0.2f, alpha) : new Color(1f, 1f, 1f, alpha);
            GUI.color = hitColor;

            float hitGap = _isHeadshotHit ? 7f : 5f;
            float hitLength = _isHeadshotHit ? 9f : 6f;
            float thickness = 2.5f;

            // Simpan matriks GUI dan putar 45 derajat agar garis silang menjadi tanda 'X'
            Matrix4x4 matrixBackup = GUI.matrix;
            GUIUtility.RotateAroundPivot(45f, new Vector2(cx, cy));

            // 4 sayap diagonal hitmarker X
            GUI.DrawTexture(new Rect(cx - thickness / 2f, cy - hitGap - hitLength, thickness, hitLength), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(cx - thickness / 2f, cy + hitGap, thickness, hitLength), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(cx - hitGap - hitLength, cy - thickness / 2f, hitLength, thickness), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(cx + hitGap, cy - thickness / 2f, hitLength, thickness), Texture2D.whiteTexture);

            // Kembalikan matriks GUI semula
            GUI.matrix = matrixBackup;
        }
    }
}
