using System.Collections.Generic;
using UnityEngine;

namespace FPSFramework.Combat
{
    /// <summary>
    /// Menandai area resmi penanaman bom (Plant Zone) untuk Site A dan Site B:
    /// - Memiliki batas visual (Hazard Striping) di lantai dan penanda teks 3D.
    /// - Menyediakan API statis untuk mengecek apakah posisi pemain atau bot berada di dalam zona tanam.
    /// </summary>
    public class PlantZone : MonoBehaviour
    {
        public static readonly List<PlantZone> AllZones = new List<PlantZone>();

        [Header("Identitas Site")]
        [SerializeField] private string siteName = "Site A";
        [SerializeField] private Vector3 zoneSize = new Vector3(8f, 3f, 8f);

        public string SiteName { get => siteName; set => siteName = value; }
        public Vector3 ZoneSize { get => zoneSize; set => zoneSize = value; }

        public void ConfigureZone(string name, Vector3 size)
        {
            siteName = name;
            zoneSize = size;
        }

        private void OnEnable()
        {
            if (!AllZones.Contains(this)) AllZones.Add(this);
        }

        private void OnDisable()
        {
            AllZones.Remove(this);
        }

        /// <summary>
        /// Mengecek apakah posisi dunia berada di dalam salah satu zona penanaman C4.
        /// </summary>
        public static bool IsPositionInPlantZone(Vector3 worldPos, out string site)
        {
            foreach (var zone in AllZones)
            {
                if (zone == null) continue;

                Vector3 center = zone.transform.position;
                Vector3 half = zone.zoneSize * 0.5f;

                float dx = Mathf.Abs(worldPos.x - center.x);
                float dz = Mathf.Abs(worldPos.z - center.z);
                float dy = worldPos.y - center.y;

                if (dx <= half.x && dz <= half.z && dy >= -0.5f && dy <= zone.zoneSize.y)
                {
                    site = zone.siteName;
                    return true;
                }
            }

            // Fallback koordinat matematis jika trigger belum aktif di scene
            // Site A: X [16, 26], Z [53, 63]
            if (worldPos.x >= 16f && worldPos.x <= 26f && worldPos.z >= 53f && worldPos.z <= 63f)
            {
                site = "Site A";
                return true;
            }

            // Site B: X [-25, -15], Z [47, 57]
            if (worldPos.x >= -25f && worldPos.x <= -15f && worldPos.z >= 47f && worldPos.z <= 57f)
            {
                site = "Site B";
                return true;
            }

            site = "";
            return false;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = siteName.Contains("A") ? new Color(1f, 0.4f, 0.1f, 0.35f) : new Color(0.2f, 0.8f, 1f, 0.35f);
            Vector3 center = transform.position + Vector3.up * (zoneSize.y * 0.5f);
            Gizmos.DrawCube(center, zoneSize);
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireCube(center, zoneSize);
        }
    }
}
