using UnityEngine;

namespace FPSFramework.Player
{
    /// <summary>
    /// Kontrak interface untuk status pergerakan pemain FPS.
    /// Didesain modular agar sistem senjata (weapon recoil, bullet spread bloom, crosshair)
    /// dapat membaca state pergerakan secara real-time tanpa coupling langsung.
    /// </summary>
    public interface IFPSMovementState
    {
        /// <summary>
        /// Vektor kecepatan karakter saat ini (world space).
        /// </summary>
        Vector3 Velocity { get; }

        /// <summary>
        /// Kecepatan horizontal (X dan Z) dalam meter per detik (m/s).
        /// </summary>
        float CurrentHorizontalSpeed { get; }

        /// <summary>
        /// Kecepatan maksimal yang ditargetkan untuk status gerak saat ini.
        /// </summary>
        float TargetMaxSpeed { get; }

        /// <summary>
        /// Rasio kecepatan saat ini terhadap kecepatan lari penuh (0.0 = diam, 1.0 = lari penuh).
        /// </summary>
        float SpeedRatio { get; }

        /// <summary>
        /// Apakah karakter sedang menapak di tanah.
        /// </summary>
        bool IsGrounded { get; }

        /// <summary>
        /// Apakah karakter sedang dalam kondisi jongkok (crouch).
        /// </summary>
        bool IsCrouching { get; }

        /// <summary>
        /// Apakah karakter sedang dalam mode jalan pelan (walking / shift).
        /// </summary>
        bool IsWalking { get; }

        /// <summary>
        /// Pengali inakurasi tembakan (0.0 = akurasi sempurna saat diam, 
        /// 0.2-0.4 = jalan pelan, 1.0 = lari, 2.5+ = melompat/di udara).
        /// Sangat krusial untuk mekanik menembak ala CS & Valorant.
        /// </summary>
        float InaccuracyModifier { get; }
    }
}
