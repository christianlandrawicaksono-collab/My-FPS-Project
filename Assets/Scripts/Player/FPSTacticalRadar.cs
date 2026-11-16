using System.Collections.Generic;
using UnityEngine;
using FPSFramework.Combat;

namespace FPSFramework.Player
{
    /// <summary>
    /// Tactical Radar & Minimap 2D (Rotating Radar ala CS2 / Valorant):
    /// - Posisi pojok kiri atas, orientasi pandangan pemain selalu ke atas (arah jam 12).
    /// - Radar Sonar Minimalis: Latar belakang glassmorphic bersih, indikator kompas dinamis (N, E, S, W).
    /// - Gunfire Ping Sonik: Musuh 100% stealth/tersembunyi dalam kabut perang; hanya muncul sebagai
    ///   titik merah kontras saat menembak, dan langsung lenyap seketika (0.18s) begitu berhenti menembak.
    /// </summary>
    public class FPSTacticalRadar : MonoBehaviour
    {
        [Header("Konfigurasi Radar")]
        [SerializeField] private float radarDiameter = 150f;
        [SerializeField] private float screenMarginX = 22f;
        [SerializeField] private float screenMarginY = 22f;
        [SerializeField] private float maxDetectionRange = 35f; // meter
        [SerializeField] private float gunfireLingerDuration = 0.18f; // detik (instan lenyap)

        [Header("Warna & Estetika Taktis")]
        [SerializeField] private Color radarBgColor = new Color(0.04f, 0.06f, 0.08f, 0.84f);
        [SerializeField] private Color radarBorderColor = new Color(0.0f, 0.85f, 1.0f, 0.85f);
        [SerializeField] private Color radarRingColor = new Color(0.0f, 0.85f, 1.0f, 0.16f);
        [SerializeField] private Color playerMarkerColor = new Color(0.0f, 0.95f, 1.0f, 1.0f);
        [SerializeField] private Color enemyGunfireColor = new Color(1.0f, 0.18f, 0.18f, 1.0f);
        [SerializeField] private Color compassTextColor = new Color(0.85f, 0.92f, 1.0f, 0.9f);

        // Internal structures
        private class GunfirePingData
        {
            public Vector3 WorldPosition;
            public float ShotTimestamp;
        }

        private readonly Dictionary<EnemyHumanoidAI, GunfirePingData> _activeGunfirePings = new Dictionary<EnemyHumanoidAI, GunfirePingData>();

        private Transform _playerTransform;
        private Camera _playerCamera;

        // Cached Procedural Textures
        private Texture2D _circleTex;
        private Texture2D _ringTex;
        private Texture2D _whitePixel;
        private GUIStyle _compassStyle;
        private GUIStyle _cardinalNorthStyle;

        private void Awake()
        {
            _playerTransform = transform;
            _playerCamera = Camera.main;
            if (_playerCamera == null)
            {
                _playerCamera = GetComponentInChildren<Camera>();
            }

            GenerateCachedTextures();
        }

        private void OnEnable()
        {
            EnemyHumanoidAI.OnAnyEnemyShot += HandleEnemyShot;
        }

        private void OnDisable()
        {
            EnemyHumanoidAI.OnAnyEnemyShot -= HandleEnemyShot;
        }

        private void Update()
        {
            if (_playerCamera == null)
            {
                _playerCamera = Camera.main;
            }
        }

        private void HandleEnemyShot(EnemyHumanoidAI bot, Vector3 shotPosition)
        {
            if (bot == null) return;

            if (!_activeGunfirePings.TryGetValue(bot, out GunfirePingData data))
            {
                data = new GunfirePingData();
                _activeGunfirePings[bot] = data;
            }

            data.WorldPosition = shotPosition;
            data.ShotTimestamp = Time.time;
        }

        private void OnGUI()
        {
            if (Event.current.type != EventType.Repaint) return;
            if (_playerCamera == null && Camera.main != null) _playerCamera = Camera.main;

            Transform camTransform = _playerCamera != null ? _playerCamera.transform : _playerTransform;
            if (camTransform == null) return;

            Vector3 playerPos = _playerTransform.position;
            float playerYaw = camTransform.eulerAngles.y;
            float playerYawRad = -playerYaw * Mathf.Deg2Rad;

            float radius = radarDiameter * 0.5f;
            Vector2 center = new Vector2(screenMarginX + radius, screenMarginY + radius);

            // 1. Gambar background lingkaran transparan
            DrawCircleFilled(center, radius, radarBgColor);

            // 2. Gambar cincin konsentris indikator jarak (15m dan 30m)
            float ring1Radius = radius * (15f / maxDetectionRange);
            float ring2Radius = radius * (30f / maxDetectionRange);
            DrawCircleHollow(center, ring1Radius, 1.0f, radarRingColor);
            DrawCircleHollow(center, ring2Radius, 1.0f, radarRingColor);

            // 3. Gambar garis sumbu taktis halus (Crosshair radar)
            DrawRadarAxis(center, radius);

            // 4. Gambar mata angin berputar (N, E, S, W)
            DrawCompassMarkers(center, radius - 12f, playerYaw);

            // 5. Gambar musuh yang sedang menembak (Gunfire Ping)
            DrawEnemyGunfirePings(center, radius, playerPos, playerYawRad);

            // 6. Gambar Vision Cone pemain di tengah menghadap atas (jam 12)
            DrawPlayerVisionCone(center, radius * 0.42f);

            // 7. Gambar titik ikon pemain di tengah
            DrawCircleFilled(center, 4.5f, playerMarkerColor);
            DrawCircleHollow(center, 5.5f, 1.2f, Color.white);

            // 8. Gambar bingkai border lingkaran luar radar
            DrawCircleHollow(center, radius, 2.0f, radarBorderColor);
        }

        private void DrawEnemyGunfirePings(Vector2 center, float radius, Vector3 playerPos, float playerYawRad)
        {
            float now = Time.time;
            float metersToPixels = (radius - 12f) / maxDetectionRange;

            foreach (var kvp in _activeGunfirePings)
            {
                GunfirePingData ping = kvp.Value;
                if (ping == null) continue;

                float elapsed = now - ping.ShotTimestamp;
                if (elapsed > gunfireLingerDuration) continue; // Instan lenyap begitu lewat durasi tembakan

                // Transform koordinat dunia ke lokal relatif pemain yang berputar
                Vector3 delta = ping.WorldPosition - playerPos;
                float rx = delta.x * Mathf.Cos(playerYawRad) - delta.z * Mathf.Sin(playerYawRad);
                float ry = delta.x * Mathf.Sin(playerYawRad) + delta.z * Mathf.Cos(playerYawRad);

                float distanceMeters = Mathf.Sqrt(rx * rx + ry * ry);
                float distPixels = distanceMeters * metersToPixels;

                // Clamp ke tepi radar jika musuh berada di luar radius 35m
                bool isClamped = distPixels > (radius - 8f);
                if (isClamped)
                {
                    distPixels = radius - 8f;
                }

                Vector2 dir = new Vector2(rx, ry).normalized;
                Vector2 blipPos = new Vector2(center.x + dir.x * distPixels, center.y - dir.y * distPixels);

                // Efek sonik Red Neon
                float pulse = Mathf.PingPong(elapsed * 18f, 1.0f);
                Color blipColor = enemyGunfireColor;
                blipColor.a = Mathf.Clamp01(1.0f - (elapsed / gunfireLingerDuration) * 0.3f);

                // Gambar ripple sonik tembakan
                float rippleRadius = 6.0f + pulse * 6.0f;
                DrawCircleHollow(blipPos, rippleRadius, 1.5f, new Color(1.0f, 0.25f, 0.25f, 0.6f * (1f - pulse)));

                // Gambar inti blip merah
                DrawCircleFilled(blipPos, 4.5f, blipColor);
                DrawCircleHollow(blipPos, 5.5f, 1.0f, Color.white);
            }
        }

        private void DrawPlayerVisionCone(Vector2 center, float length)
        {
            // Vision cone 60 derajat (-30 s/d +30 derajat dari arah atas / Y minus di GUI)
            Color coneColor = new Color(0.0f, 0.95f, 1.0f, 0.18f);
            Color edgeColor = new Color(0.0f, 0.95f, 1.0f, 0.55f);

            float rad30 = 30f * Mathf.Deg2Rad;
            Vector2 leftRay = new Vector2(center.x - Mathf.Sin(rad30) * length, center.y - Mathf.Cos(rad30) * length);
            Vector2 rightRay = new Vector2(center.x + Mathf.Sin(rad30) * length, center.y - Mathf.Cos(rad30) * length);

            // Garis pembatas sayap vision cone
            DrawLine(center, leftRay, 1.5f, edgeColor);
            DrawLine(center, rightRay, 1.5f, edgeColor);

            // Garis lengkung busur depan
            int arcSegments = 8;
            Vector2 prevPoint = leftRay;
            for (int i = 1; i <= arcSegments; i++)
            {
                float t = (float)i / arcSegments;
                float currentAngle = Mathf.Lerp(-30f, 30f, t) * Mathf.Deg2Rad;
                Vector2 arcPoint = new Vector2(center.x + Mathf.Sin(currentAngle) * length, center.y - Mathf.Cos(currentAngle) * length);
                DrawLine(prevPoint, arcPoint, 1.2f, edgeColor);
                prevPoint = arcPoint;
            }
        }

        private void DrawCompassMarkers(Vector2 center, float radius, float playerYaw)
        {
            if (_compassStyle == null)
            {
                _compassStyle = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 11,
                    fontStyle = FontStyle.Bold
                };
                _compassStyle.normal.textColor = compassTextColor;
            }

            if (_cardinalNorthStyle == null)
            {
                _cardinalNorthStyle = new GUIStyle(_compassStyle);
                _cardinalNorthStyle.normal.textColor = new Color(0.0f, 0.95f, 1.0f, 1.0f);
            }

            // Cardinal points: N (0 deg), E (90 deg), S (180 deg), W (270 deg)
            string[] labels = { "N", "E", "S", "W" };
            float[] angles = { 0f, 90f, 180f, 270f };

            for (int i = 0; i < 4; i++)
            {
                float angleRel = (angles[i] - playerYaw) * Mathf.Deg2Rad;
                float px = center.x + Mathf.Sin(angleRel) * radius;
                float py = center.y - Mathf.Cos(angleRel) * radius;

                GUIStyle style = (i == 0) ? _cardinalNorthStyle : _compassStyle;
                GUI.Label(new Rect(px - 12f, py - 10f, 24f, 20f), labels[i], style);
            }
        }

        private void DrawRadarAxis(Vector2 center, float radius)
        {
            Color axisColor = new Color(0.0f, 0.85f, 1.0f, 0.12f);
            // Garis vertikal
            DrawLine(new Vector2(center.x, center.y - radius + 6f), new Vector2(center.x, center.y + radius - 6f), 1.0f, axisColor);
            // Garis horizontal
            DrawLine(new Vector2(center.x - radius + 6f, center.y), new Vector2(center.x + radius - 6f, center.y), 1.0f, axisColor);
        }

        private void DrawCircleFilled(Vector2 center, float radius, Color color)
        {
            Color prevColor = GUI.color;
            GUI.color = color;
            Rect rect = new Rect(center.x - radius, center.y - radius, radius * 2f, radius * 2f);
            GUI.DrawTexture(rect, _circleTex, ScaleMode.StretchToFill);
            GUI.color = prevColor;
        }

        private void DrawCircleHollow(Vector2 center, float radius, float thickness, Color color)
        {
            Color prevColor = GUI.color;
            GUI.color = color;
            Rect rect = new Rect(center.x - radius, center.y - radius, radius * 2f, radius * 2f);
            GUI.DrawTexture(rect, _ringTex, ScaleMode.StretchToFill);
            GUI.color = prevColor;
        }

        private void DrawLine(Vector2 pointA, Vector2 pointB, float width, Color color)
        {
            Color prevColor = GUI.color;
            GUI.color = color;

            Vector2 diff = pointB - pointA;
            float angle = Mathf.Atan2(diff.y, diff.x) * Mathf.Rad2Deg;
            float length = diff.magnitude;

            GUIUtility.RotateAroundPivot(angle, pointA);
            GUI.DrawTexture(new Rect(pointA.x, pointA.y - width * 0.5f, length, width), _whitePixel);
            GUIUtility.RotateAroundPivot(-angle, pointA);

            GUI.color = prevColor;
        }

        private void GenerateCachedTextures()
        {
            _whitePixel = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            _whitePixel.SetPixel(0, 0, Color.white);
            _whitePixel.Apply();

            int size = 128;
            _circleTex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            _circleTex.wrapMode = TextureWrapMode.Clamp;
            _circleTex.filterMode = FilterMode.Bilinear;

            _ringTex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            _ringTex.wrapMode = TextureWrapMode.Clamp;
            _ringTex.filterMode = FilterMode.Bilinear;

            Vector2 center = new Vector2(size * 0.5f, size * 0.5f);
            float maxR = size * 0.5f - 1.5f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dist = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center);
                    // Circle fill with soft anti-aliased edge
                    float fillAlpha = Mathf.Clamp01(maxR - dist + 0.5f);
                    _circleTex.SetPixel(x, y, new Color(1f, 1f, 1f, fillAlpha));

                    // Ring edge with smooth border
                    float ringDist = Mathf.Abs(dist - (maxR - 1.0f));
                    float ringAlpha = Mathf.Clamp01(1.5f - ringDist);
                    _ringTex.SetPixel(x, y, new Color(1f, 1f, 1f, ringAlpha));
                }
            }

            _circleTex.Apply();
            _ringTex.Apply();
        }

        private void OnDestroy()
        {
            if (_circleTex != null) Destroy(_circleTex);
            if (_ringTex != null) Destroy(_ringTex);
            if (_whitePixel != null) Destroy(_whitePixel);
        }
    }
}
