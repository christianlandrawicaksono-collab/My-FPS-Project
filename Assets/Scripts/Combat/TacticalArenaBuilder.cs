using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using FPSFramework.Player;

namespace FPSFramework.Combat
{
    /// <summary>
    /// Membangun Map Taktis Skala Penuh (Tactical 3-Lane Map: 60m x 75m)
    /// bergaya kompetitif ala Counter-Strike 2 (Dust 2 / Mirage) dan Valorant:
    /// - 3 Jalur Utama: Long A (Jarak Jauh), Mid Courtyard & Heaven Balcony (Vertikal), dan Close B (CQC).
    /// - Kontainer Kargo 20ft berwarna industri (Merah, Biru, Hijau Zaitun) dan tumpukan boks taktis.
    /// - Jalur rotasi penghubung (Connector A & Connector B) untuk strategi flanking.
    /// - Papan target interaktif ganti mode bot di T-Spawn.
    /// - Otomatis memposisikan bot tempur bersenjata di Long A dan dummy latihan di Close B.
    /// - Otomatis memindahkan player ke T-Spawn dan menonaktifkan ruangan lama.
    /// </summary>
    [ExecuteAlways]
    public class TacticalArenaBuilder : MonoBehaviour
    {
        public const string ArenaRootName = "Tactical_3Lane_Map";

        [Header("Pengaturan Warna & Visual")]
        [SerializeField] private Color wallColor = new Color(0.18f, 0.20f, 0.24f);         // Dark Slate Concrete
        [SerializeField] private Color groundColor = new Color(0.11f, 0.12f, 0.14f);       // Dark Asphalt
        [SerializeField] private Color crateColor = new Color(0.50f, 0.40f, 0.28f);        // Military Wood Crate
        [SerializeField] private Color containerRed = new Color(0.60f, 0.22f, 0.22f);      // Cargo Red
        [SerializeField] private Color containerBlue = new Color(0.20f, 0.35f, 0.58f);     // Cargo Blue
        [SerializeField] private Color containerGreen = new Color(0.26f, 0.36f, 0.24f);    // Military Olive
        [SerializeField] private Color balconyColor = new Color(0.22f, 0.25f, 0.29f);      // Carbon Deck

        [Header("Opsi Otomatis")]
        [SerializeField] private bool autoBuildOnStart = true;

        private void Start()
        {
            if (autoBuildOnStart && GameObject.Find(ArenaRootName) == null)
            {
                BuildArena();
            }
        }

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
        private static void AutoEnsureMapExistsEditor()
        {
            UnityEditor.EditorApplication.delayCall += () =>
            {
                if (Application.isPlaying) return;
                EnsureMapAndBotInScene();
            };
        }

        [UnityEditor.MenuItem("Tools/Build 3-Lane Tactical Map & Bot")]
        public static void MenuBuildTacticalMap()
        {
            ForceRebuildArena();
        }
#endif

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoEnsureMapExistsRuntime()
        {
            EnsureMapAndBotInScene();
        }

        public static void EnsureMapAndBotInScene()
        {
            Scene activeScene = SceneManager.GetActiveScene();
            if (activeScene.name != "Training") return;

            GameObject existingMap = GameObject.Find(ArenaRootName);
            if (existingMap == null || existingMap.transform.Find("SiteA_RadarHub_Roof") == null || existingMap.transform.Find("PlantZone_A_FloorPlate") == null)
            {
                ForceRebuildArena();
                return;
            }

            // Jika map sudah ada, pastikan pemain sudah berada di CT Spawn
            GameObject playerObj = GameObject.Find("Player_Capsule");
            if (playerObj != null && (playerObj.transform.position.z < 50f || Mathf.Abs(playerObj.transform.position.x) > 15f))
            {
                RepositionPlayerStatic(playerObj);
            }

            // Selalu pastikan kedua bot prajurit (Bot A & Bot B) ada dan berada di lantai
            EnsureEnemyBotsExist();
        }

        public static void ForceRebuildArena()
        {
            GameObject existingMap = GameObject.Find(ArenaRootName);
            if (existingMap != null)
            {
                if (Application.isPlaying) Destroy(existingMap);
                else DestroyImmediate(existingMap);
            }

            GameObject oldBuilder = GameObject.Find("TacticalMap_AutoBuilder");
            if (oldBuilder != null)
            {
                if (Application.isPlaying) Destroy(oldBuilder);
                else DestroyImmediate(oldBuilder);
            }

            string[] botNames = { "Enemy_Bot_Soldier", "Enemy_Bot_Soldier_A", "Enemy_Bot_Soldier_B" };
            foreach (var bName in botNames)
            {
                GameObject bObj = GameObject.Find(bName);
                if (bObj != null)
                {
                    if (Application.isPlaying) Destroy(bObj);
                    else DestroyImmediate(bObj);
                }
            }

            GameObject existingDummy = GameObject.Find("Target_Dummy");
            if (existingDummy != null)
            {
                if (Application.isPlaying) Destroy(existingDummy);
                else DestroyImmediate(existingDummy);
            }

            GameObject builderObj = new GameObject("TacticalMap_AutoBuilder");
            TacticalArenaBuilder builder = builderObj.AddComponent<TacticalArenaBuilder>();
            builder.BuildArena();

#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                Scene activeScene = SceneManager.GetActiveScene();
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(activeScene);
            }
#endif
        }

        [ContextMenu("Build 3-Lane Tactical Map")]
        public void BuildArena()
        {
            ClearArena();

            GameObject mapRoot = new GameObject(ArenaRootName);
            mapRoot.transform.position = Vector3.zero;

            // Inisialisasi Shader URP Lit
            Shader urpShader = Shader.Find("Universal Render Pipeline/Lit");
            if (urpShader == null) urpShader = Shader.Find("Standard");

            Material wallMat = new Material(urpShader) { color = wallColor, name = "Mat_Wall" };
            Material groundMat = new Material(urpShader) { color = groundColor, name = "Mat_Asphalt" };
            Material crateMat = new Material(urpShader) { color = crateColor, name = "Mat_Crate" };
            Material redContainerMat = new Material(urpShader) { color = containerRed, name = "Mat_ContainerRed" };
            Material blueContainerMat = new Material(urpShader) { color = containerBlue, name = "Mat_ContainerBlue" };
            Material greenContainerMat = new Material(urpShader) { color = containerGreen, name = "Mat_ContainerGreen" };
            Material yellowContainerMat = new Material(urpShader) { color = new Color(0.75f, 0.55f, 0.18f), name = "Mat_ContainerYellow" };
            Material balconyMat = new Material(urpShader) { color = balconyColor, name = "Mat_Balcony" };
            Material concreteMat = new Material(urpShader) { color = new Color(0.28f, 0.30f, 0.34f), name = "Mat_ConcreteBarrier" };
            Material interiorFloorMat = new Material(urpShader) { color = new Color(0.16f, 0.17f, 0.19f), name = "Mat_InteriorTile" };
            Material roofMat = new Material(urpShader) { color = new Color(0.14f, 0.15f, 0.17f), name = "Mat_CorrugatedRoof" };
            Material accentCyanMat = new Material(urpShader) { color = new Color(0.0f, 0.75f, 0.85f), name = "Mat_TechCore" };
            Material barrelRedMat = new Material(urpShader) { color = new Color(0.75f, 0.20f, 0.20f), name = "Mat_BarrelRed" };
            Material barrelYellowMat = new Material(urpShader) { color = new Color(0.80f, 0.65f, 0.15f), name = "Mat_BarrelYellow" };
            Material barrelDarkMat = new Material(urpShader) { color = new Color(0.18f, 0.22f, 0.25f), name = "Mat_BarrelDark" };
            Material barrelBlueMat = new Material(urpShader) { color = new Color(0.20f, 0.45f, 0.70f), name = "Mat_BarrelBlue" };
            Material bollardYellowMat = new Material(urpShader) { color = new Color(0.90f, 0.72f, 0.10f), name = "Mat_BollardYellow" };
            Material pipeSteelMat = new Material(urpShader) { color = new Color(0.35f, 0.38f, 0.42f), name = "Mat_SteelPipe" };
            Material plantHazardMat = new Material(urpShader) { color = new Color(0.92f, 0.72f, 0.12f, 0.95f), name = "Mat_PlantHazard" };

            // 0. Lantai Dasar Map Baru (60m x 75m)
            CreateBlock(mapRoot.transform, "Map_Floor", new Vector3(0f, -0.05f, 32.5f), new Vector3(60f, 0.1f, 75f), groundMat);

            // 1. Dinding Pembatas Luar (Boundary Enclosure: 60m x 75m x 5.5m)
            CreateBlock(mapRoot.transform, "Wall_Outer_Front", new Vector3(0f, 2.75f, -5f), new Vector3(60f, 5.5f, 1f), wallMat);
            CreateBlock(mapRoot.transform, "Wall_Outer_Back", new Vector3(0f, 2.75f, 70f), new Vector3(60f, 5.5f, 1f), wallMat);
            CreateBlock(mapRoot.transform, "Wall_Outer_Left", new Vector3(-30f, 2.75f, 32.5f), new Vector3(1f, 5.5f, 76f), wallMat);
            CreateBlock(mapRoot.transform, "Wall_Outer_Right", new Vector3(30f, 2.75f, 32.5f), new Vector3(1f, 5.5f, 76f), wallMat);

            // 2. Dinding Pemisah 3 Jalur & Pintu Lorong (Connectors)
            // Dinding Pemisah Kiri (Memisahkan Close B dan Mid Courtyard)
            CreateBlock(mapRoot.transform, "Divider_Left_Part1", new Vector3(-12f, 2.5f, 18f), new Vector3(1f, 5f, 16f), wallMat);
            CreateBlock(mapRoot.transform, "Divider_Left_Beam", new Vector3(-12f, 4.5f, 28f), new Vector3(1f, 1f, 4f), wallMat);
            CreateBlock(mapRoot.transform, "Divider_Left_Part2", new Vector3(-12f, 2.5f, 46f), new Vector3(1f, 5f, 32f), wallMat);

            // Dinding Pemisah Kanan (Memisahkan Long A dan Mid Courtyard)
            CreateBlock(mapRoot.transform, "Divider_Right_Part1", new Vector3(12f, 2.5f, 20f), new Vector3(1f, 5f, 20f), wallMat);
            CreateBlock(mapRoot.transform, "Divider_Right_Beam", new Vector3(12f, 4.5f, 32f), new Vector3(1f, 1f, 4f), wallMat);
            CreateBlock(mapRoot.transform, "Divider_Right_Part2", new Vector3(12f, 2.5f, 48f), new Vector3(1f, 5f, 28f), wallMat);

            // ==========================================
            // 3. T-SPAWN & STAGING BARRICADES (Z = -5 s/d 12)
            // ==========================================
            GameObject tbL = CreateBlock(mapRoot.transform, "TSpawn_BlastBarrier_L", new Vector3(-7f, 0.7f, 3f), new Vector3(4f, 1.4f, 0.8f), concreteMat);
            tbL.transform.localRotation = Quaternion.Euler(0f, 15f, 0f);
            GameObject tbR = CreateBlock(mapRoot.transform, "TSpawn_BlastBarrier_R", new Vector3(7f, 0.7f, 3f), new Vector3(4f, 1.4f, 0.8f), concreteMat);
            tbR.transform.localRotation = Quaternion.Euler(0f, -15f, 0f);

            CreateBlock(mapRoot.transform, "TSpawn_SupplyCrate_1", new Vector3(-3.5f, 0.6f, -2f), new Vector3(1.8f, 1.2f, 1.8f), crateMat);
            CreateBlock(mapRoot.transform, "TSpawn_SupplyCrate_2", new Vector3(3.5f, 0.6f, -2f), new Vector3(1.8f, 1.2f, 1.8f), crateMat);
            CreateBlock(mapRoot.transform, "TSpawn_PalletDeck", new Vector3(0f, 0.35f, -3f), new Vector3(2.4f, 0.7f, 1.4f), crateMat);

            // Gapura Balok T-Spawn menuju 3 Jalur
            CreateBlock(mapRoot.transform, "TSpawn_Arch_LaneB", new Vector3(-19f, 4.5f, 10f), new Vector3(6f, 1.0f, 0.8f), wallMat);
            CreateBlock(mapRoot.transform, "TSpawn_Arch_Mid", new Vector3(0f, 4.5f, 10f), new Vector3(6f, 1.0f, 0.8f), wallMat);
            CreateBlock(mapRoot.transform, "TSpawn_Arch_LaneA", new Vector3(19f, 4.5f, 10f), new Vector3(6f, 1.0f, 0.8f), wallMat);

            // Barel & Bollard Dekorasi T-Spawn
            CreateBarrelCluster(mapRoot.transform, "TSpawn_Barrels_L", new Vector3(-10f, 0f, 2f), barrelDarkMat, barrelYellowMat);
            CreateBarrelCluster(mapRoot.transform, "TSpawn_Barrels_R", new Vector3(10f, 0f, 2f), barrelDarkMat, barrelRedMat);
            CreateHorizontalBarrel(mapRoot.transform, "TSpawn_LyingBarrel", new Vector3(-1.8f, 0f, -2.8f), 35f, barrelRedMat);

            // Tiang Bollard Silinder Pengaman Gerbang T-Spawn
            CreateBollardPair(mapRoot.transform, "TSpawn_Bollard_LaneB", new Vector3(-16f, 0.55f, 9.5f), new Vector3(-22f, 0.55f, 9.5f), bollardYellowMat);
            CreateBollardPair(mapRoot.transform, "TSpawn_Bollard_Mid", new Vector3(-2.5f, 0.55f, 9.5f), new Vector3(2.5f, 0.55f, 9.5f), bollardYellowMat);
            CreateBollardPair(mapRoot.transform, "TSpawn_Bollard_LaneA", new Vector3(16f, 0.55f, 9.5f), new Vector3(22f, 0.55f, 9.5f), bollardYellowMat);

            // ==========================================
            // 4. JALUR KANAN: LONG A & SITE A RADAR CONTROL HUB
            // ==========================================
            GameObject contBlue = CreateBlock(mapRoot.transform, "Container_LongA_Blue", new Vector3(21f, 1.3f, 22f), new Vector3(2.4f, 2.6f, 6.0f), blueContainerMat);
            contBlue.transform.localRotation = Quaternion.Euler(0f, 12f, 0f);

            CreateBlock(mapRoot.transform, "LongA_Sandbag_Cover", new Vector3(16f, 0.55f, 24f), new Vector3(2.5f, 1.1f, 0.8f), concreteMat);
            CreateBlock(mapRoot.transform, "LongA_Pallet_1", new Vector3(26f, 0.4f, 26f), new Vector3(1.8f, 0.8f, 1.8f), crateMat);
            CreateBarrelCluster(mapRoot.transform, "LongA_Barrels_Blue", new Vector3(25f, 0f, 20f), barrelDarkMat, barrelRedMat);
            CreateHorizontalBarrel(mapRoot.transform, "LongA_LyingBarrel_Blue", new Vector3(23.5f, 0f, 18.5f), 15f, barrelDarkMat);

            CreateBlock(mapRoot.transform, "ConnectorA_Roadblock", new Vector3(16f, 0.55f, 32f), new Vector3(1.2f, 1.1f, 2.2f), concreteMat);
            CreateBarrelCluster(mapRoot.transform, "ConnectorA_Barrels", new Vector3(11f, 0f, 32f), barrelRedMat, barrelDarkMat);
            CreateBlock(mapRoot.transform, "Crates_LongA_Bottom", new Vector3(15f, 0.6f, 36f), new Vector3(2.2f, 1.2f, 2.2f), crateMat);
            CreateBlock(mapRoot.transform, "Crates_LongA_Top", new Vector3(15f, 1.7f, 36f), new Vector3(1.2f, 1.0f, 1.2f), crateMat);

            // A-Cubby (Lekukan dinding taktis)
            CreateBlock(mapRoot.transform, "LongA_Cubby_Wall", new Vector3(28f, 2.0f, 38f), new Vector3(3.5f, 4.0f, 0.8f), wallMat);
            CreateBarrelCluster(mapRoot.transform, "LongA_Barrels_Cubby", new Vector3(28f, 0f, 41f), barrelYellowMat, barrelRedMat);

            GameObject contRed = CreateBlock(mapRoot.transform, "Container_LongA_Red", new Vector3(24f, 1.3f, 46f), new Vector3(2.4f, 2.6f, 6.0f), redContainerMat);
            contRed.transform.localRotation = Quaternion.Euler(0f, -8f, 0f);
            CreateBlock(mapRoot.transform, "LongA_Red_Cover", new Vector3(20f, 0.6f, 47f), new Vector3(1.4f, 1.2f, 1.4f), crateMat);

            // --- SITE A RADAR CONTROL HUB (Gedung Tertutup X = 14 s/d 28, Z = 52 s/d 66) ---
            CreateBlock(mapRoot.transform, "SiteA_Floor", new Vector3(21f, 0.02f, 59f), new Vector3(14f, 0.05f, 14f), interiorFloorMat);
            // Dinding Selatan (Pintu Masuk Utama dari Long A)
            CreateBlock(mapRoot.transform, "SiteA_Wall_South_L", new Vector3(16.25f, 2.25f, 52f), new Vector3(4.5f, 4.5f, 0.8f), wallMat);
            CreateBlock(mapRoot.transform, "SiteA_Door_South_Beam", new Vector3(20.25f, 4.0f, 52f), new Vector3(3.5f, 1.0f, 0.8f), wallMat);
            CreateBlock(mapRoot.transform, "SiteA_Wall_South_R", new Vector3(25.0f, 2.25f, 52f), new Vector3(6.0f, 4.5f, 0.8f), wallMat);
            // Dinding Barat (Pintu Menuju Jembatan Catwalk Heaven)
            CreateBlock(mapRoot.transform, "SiteA_Wall_West_S", new Vector3(14f, 2.25f, 54.5f), new Vector3(0.8f, 4.5f, 5.0f), wallMat);
            CreateBlock(mapRoot.transform, "SiteA_Door_West_Beam", new Vector3(14f, 4.0f, 58.5f), new Vector3(0.8f, 1.0f, 3.0f), wallMat);
            CreateBlock(mapRoot.transform, "SiteA_Wall_West_N", new Vector3(14f, 2.25f, 63.0f), new Vector3(0.8f, 4.5f, 6.0f), wallMat);
            // Dinding Utara & Timur
            CreateBlock(mapRoot.transform, "SiteA_Wall_North", new Vector3(21f, 2.25f, 66f), new Vector3(14f, 4.5f, 0.8f), wallMat);
            CreateBlock(mapRoot.transform, "SiteA_Wall_East_Fill", new Vector3(28.5f, 2.25f, 59f), new Vector3(1.0f, 4.5f, 14f), wallMat);
            // Atap Gedung Site A
            CreateBlock(mapRoot.transform, "SiteA_RadarHub_Roof", new Vector3(21f, 4.6f, 59f), new Vector3(14f, 0.3f, 14f), roofMat);

            // Interior Site A
            CreateBlock(mapRoot.transform, "SiteA_Reactor_Core", new Vector3(21f, 1.5f, 59f), new Vector3(2.4f, 3.0f, 2.4f), wallMat);
            CreateBlock(mapRoot.transform, "SiteA_Reactor_Glow", new Vector3(21f, 1.8f, 59f), new Vector3(2.5f, 0.5f, 2.5f), accentCyanMat);
            CreateBlock(mapRoot.transform, "SiteA_Plant_Default", new Vector3(24.5f, 0.6f, 57f), new Vector3(2.2f, 1.2f, 2.2f), crateMat);
            CreateBlock(mapRoot.transform, "SiteA_Plant_Safe", new Vector3(17.5f, 0.6f, 62f), new Vector3(2.2f, 1.2f, 2.2f), crateMat);
            CreateBlock(mapRoot.transform, "SiteA_ServerRack", new Vector3(26f, 1.0f, 63f), new Vector3(1.2f, 2.0f, 2.4f), balconyMat);
            CreateBarrelCluster(mapRoot.transform, "SiteA_Barrels_Coolant", new Vector3(16f, 0f, 63f), barrelYellowMat, barrelDarkMat);
            CreateBarrelCluster(mapRoot.transform, "SiteA_Barrels_Outside", new Vector3(27f, 0f, 50f), barrelRedMat);
            CreateBarrelCluster(mapRoot.transform, "SiteA_Barrels_EastCorner", new Vector3(27f, 0f, 55f), barrelDarkMat, barrelBlueMat);

            // --- SITE A PLANT ZONE FLOOR MARKERS & HAZARD BRACKETS ---
            GameObject pzAFloor = CreateBlock(mapRoot.transform, "PlantZone_A_FloorPlate", new Vector3(21f, 0.03f, 59.5f), new Vector3(8.5f, 0.06f, 7.5f), plantHazardMat);
            PlantZone pzA = pzAFloor.AddComponent<PlantZone>();
            pzA.ConfigureZone("Site A", new Vector3(8.5f, 3.5f, 7.5f));

            // Border & Corner Brackets Site A
            CreateCornerHazardBrackets(mapRoot.transform, "SiteA_HazardBracket", new Vector3(21f, 0.06f, 59.5f), new Vector3(8.5f, 0.06f, 7.5f), bollardYellowMat, barrelDarkMat);
            // Sign Taktis Site A di dinding Utara
            CreateTacticalZoneSign(mapRoot.transform, "SiteA_WallSign", new Vector3(21f, 2.5f, 65.5f), "A", new Color(0.95f, 0.75f, 0.1f));

            // Pipa & Conduit Silinder Site A
            CreateCylinder(mapRoot.transform, "SiteA_Vertical_Conduit_L", new Vector3(14.5f, 2.25f, 65.5f), new Vector3(0.25f, 2.25f, 0.25f), pipeSteelMat);
            CreateCylinder(mapRoot.transform, "SiteA_Vertical_Conduit_R", new Vector3(27.5f, 2.25f, 65.5f), new Vector3(0.25f, 2.25f, 0.25f), pipeSteelMat);
            GameObject aPipe = CreateCylinder(mapRoot.transform, "SiteA_Ceiling_Pipe", new Vector3(17.5f, 4.2f, 59f), new Vector3(0.25f, 3.5f, 0.25f), pipeSteelMat);
            aPipe.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);

            // ==========================================
            // 5. JALUR TENGAH: MID COURTYARD, MID SECURITY OFFICE & HEAVEN CATWALK
            // ==========================================
            CreateBlock(mapRoot.transform, "Mid_CentralMonument", new Vector3(0f, 2.0f, 28f), new Vector3(2.4f, 4.0f, 2.4f), wallMat);
            CreateBlock(mapRoot.transform, "Mid_Monument_Curb", new Vector3(0f, 0.3f, 28f), new Vector3(4.4f, 0.6f, 4.4f), concreteMat);
            CreateBarrelCluster(mapRoot.transform, "Mid_Barrels_Monument_L", new Vector3(-2.2f, 0f, 26f), barrelDarkMat, barrelYellowMat);
            CreateBarrelCluster(mapRoot.transform, "Mid_Barrels_Monument_R", new Vector3(2.2f, 0f, 30f), barrelDarkMat, barrelRedMat);

            GameObject mwB = CreateBlock(mapRoot.transform, "Mid_Barricade_West", new Vector3(-5f, 0.55f, 22f), new Vector3(3.2f, 1.1f, 0.8f), concreteMat);
            mwB.transform.localRotation = Quaternion.Euler(0f, -10f, 0f);
            GameObject meB = CreateBlock(mapRoot.transform, "Mid_Barricade_East", new Vector3(5f, 0.55f, 22f), new Vector3(3.2f, 1.1f, 0.8f), concreteMat);
            meB.transform.localRotation = Quaternion.Euler(0f, 10f, 0f);
            CreateBlock(mapRoot.transform, "Mid_SniperBox", new Vector3(8f, 0.6f, 28f), new Vector3(2.0f, 1.2f, 2.0f), crateMat);
            CreateBarrelCluster(mapRoot.transform, "Mid_Barrels_WestBarricade", new Vector3(-8f, 0f, 22f), barrelRedMat, barrelYellowMat);
            CreateBarrelCluster(mapRoot.transform, "Mid_Barrels_UnderBridge", new Vector3(7f, 0f, 58.5f), barrelDarkMat, barrelYellowMat);

            // --- MID SECURITY OFFICE (Gedung Tertutup X = -10 s/d -2, Z = 34 s/d 46) ---
            CreateBlock(mapRoot.transform, "MidOffice_Floor", new Vector3(-6f, 0.02f, 40f), new Vector3(8f, 0.05f, 12f), interiorFloorMat);
            // Dinding Selatan (Pintu Masuk dari Mid Plaza)
            CreateBlock(mapRoot.transform, "MidOffice_Wall_South_L", new Vector3(-8.5f, 2.0f, 34f), new Vector3(3.0f, 4.0f, 0.8f), wallMat);
            CreateBlock(mapRoot.transform, "MidOffice_Door_South_Beam", new Vector3(-5.5f, 3.5f, 34f), new Vector3(3.0f, 1.0f, 0.8f), wallMat);
            CreateBlock(mapRoot.transform, "MidOffice_Wall_South_R", new Vector3(-3.0f, 2.0f, 34f), new Vector3(2.0f, 4.0f, 0.8f), wallMat);
            // Dinding Timur & Utara
            CreateBlock(mapRoot.transform, "MidOffice_Wall_East", new Vector3(-2f, 2.0f, 40f), new Vector3(0.8f, 4.0f, 12f), wallMat);
            CreateBlock(mapRoot.transform, "MidOffice_Wall_North", new Vector3(-6f, 2.0f, 46f), new Vector3(8f, 4.0f, 0.8f), wallMat);
            // Dinding Barat (Pintu Menuju Lorong Connector B)
            CreateBlock(mapRoot.transform, "MidOffice_Wall_West_S", new Vector3(-10f, 2.0f, 36.5f), new Vector3(0.8f, 4.0f, 5.0f), wallMat);
            CreateBlock(mapRoot.transform, "MidOffice_Door_West_Beam", new Vector3(-10f, 3.5f, 40.5f), new Vector3(0.8f, 1.0f, 3.0f), wallMat);
            CreateBlock(mapRoot.transform, "MidOffice_Wall_West_N", new Vector3(-10f, 2.0f, 44.0f), new Vector3(0.8f, 4.0f, 4.0f), wallMat);
            // Atap Kantor Keamanan
            CreateBlock(mapRoot.transform, "MidOffice_Roof", new Vector3(-6f, 4.1f, 40f), new Vector3(8f, 0.25f, 12f), roofMat);
            // Interior Kantor
            CreateBlock(mapRoot.transform, "MidOffice_ConsoleDesk", new Vector3(-6f, 0.55f, 38f), new Vector3(2.8f, 1.1f, 1.2f), wallMat);
            CreateBlock(mapRoot.transform, "MidOffice_SupplyCrate", new Vector3(-8f, 0.9f, 43f), new Vector3(1.6f, 1.8f, 1.6f), crateMat);
            CreateCylinder(mapRoot.transform, "MidOffice_SafetyBarrel", new Vector3(-8.5f, 0.58f, 35.5f), new Vector3(0.85f, 0.58f, 0.85f), barrelRedMat);

            // Balkon Heaven
            CreateBlock(mapRoot.transform, "Heaven_Deck", new Vector3(0f, 3.2f, 54f), new Vector3(12f, 0.4f, 6f), balconyMat);
            CreateBlock(mapRoot.transform, "Heaven_Pillar_L", new Vector3(-5f, 1.6f, 52f), new Vector3(0.6f, 3.2f, 0.6f), wallMat);
            CreateBlock(mapRoot.transform, "Heaven_Pillar_R", new Vector3(5f, 1.6f, 52f), new Vector3(0.6f, 3.2f, 0.6f), wallMat);
            CreateBlock(mapRoot.transform, "Heaven_Railing", new Vector3(0f, 3.8f, 51.1f), new Vector3(12f, 0.8f, 0.25f), wallMat);
            GameObject heavenRamp = CreateBlock(mapRoot.transform, "Heaven_AccessRamp", new Vector3(-6f, 1.6f, 46.5f), new Vector3(3.5f, 0.25f, 8.5f), balconyMat);
            heavenRamp.transform.localRotation = Quaternion.Euler(20f, 0f, 0f);
            CreateBarrelCluster(mapRoot.transform, "Heaven_Barrels_Cover", new Vector3(4.5f, 3.4f, 54.5f), barrelDarkMat, barrelRedMat);

            // Jembatan Catwalk Elevated dari Heaven ke Site A
            CreateBlock(mapRoot.transform, "Heaven_Catwalk_Bridge", new Vector3(10f, 3.2f, 58.5f), new Vector3(8f, 0.35f, 3.0f), balconyMat);
            CreateBlock(mapRoot.transform, "Heaven_Catwalk_Rail_S", new Vector3(10f, 3.7f, 57.1f), new Vector3(8f, 0.7f, 0.15f), wallMat);
            CreateBlock(mapRoot.transform, "Heaven_Catwalk_Rail_N", new Vector3(10f, 3.7f, 59.9f), new Vector3(8f, 0.7f, 0.15f), wallMat);

            // ==========================================
            // 6. JALUR KIRI: CLOSE B & SITE B INDUSTRIAL WAREHOUSE
            // ==========================================
            CreateBlock(mapRoot.transform, "Container_CloseB_Green", new Vector3(-21f, 1.3f, 24f), new Vector3(2.4f, 2.6f, 6.0f), greenContainerMat);
            CreateBlock(mapRoot.transform, "CloseB_Forklift", new Vector3(-26f, 0.75f, 26f), new Vector3(2.0f, 1.5f, 3.2f), yellowContainerMat);
            CreateBlock(mapRoot.transform, "CloseB_Pallet_Cover", new Vector3(-16f, 0.55f, 24f), new Vector3(2.2f, 1.1f, 1.4f), crateMat);
            CreateBarrelCluster(mapRoot.transform, "CloseB_Barrels_Green", new Vector3(-24f, 0f, 22f), barrelYellowMat, barrelDarkMat);
            CreateBarrelCluster(mapRoot.transform, "CloseB_Entry_Barrels", new Vector3(-20f, 0f, 15f), barrelRedMat, barrelYellowMat);
            CreateBarrelCluster(mapRoot.transform, "ConnectorB_Barrels", new Vector3(-13f, 0f, 30f), barrelYellowMat, barrelDarkMat);

            // --- SITE B INDUSTRIAL WAREHOUSE (Gedung Tertutup X = -28 s/d -14, Z = 36 s/d 64) ---
            CreateBlock(mapRoot.transform, "BSite_Floor", new Vector3(-21f, 0.02f, 50f), new Vector3(14f, 0.05f, 28f), interiorFloorMat);
            // Dinding Selatan (Pintu Masuk Utama B)
            CreateBlock(mapRoot.transform, "B_Wall_Left", new Vector3(-24.125f, 2.25f, 38f), new Vector3(10.75f, 4.5f, 0.8f), wallMat);
            CreateBlock(mapRoot.transform, "B_Wall_Right", new Vector3(-13.875f, 2.25f, 38f), new Vector3(2.75f, 4.5f, 0.8f), wallMat);
            CreateBlock(mapRoot.transform, "B_DoorBeam", new Vector3(-17f, 4.0f, 38f), new Vector3(3.5f, 1.0f, 0.8f), wallMat);

            // Kolom Balok Baja Penyangga Interior
            CreateBlock(mapRoot.transform, "BSite_Pillar_1", new Vector3(-24f, 2.25f, 46f), new Vector3(0.8f, 4.5f, 0.8f), balconyMat);
            CreateBlock(mapRoot.transform, "BSite_Pillar_2", new Vector3(-18f, 2.25f, 46f), new Vector3(0.8f, 4.5f, 0.8f), balconyMat);

            // Kontainer Kuning & Tumpukan Boks Plant B
            CreateBlock(mapRoot.transform, "Container_BSite_Yellow", new Vector3(-24f, 1.3f, 55f), new Vector3(2.4f, 2.6f, 6.0f), yellowContainerMat);
            CreateBlock(mapRoot.transform, "BSite_Plant_Bottom", new Vector3(-18f, 0.6f, 52f), new Vector3(2.4f, 1.2f, 2.4f), crateMat);
            CreateBlock(mapRoot.transform, "BSite_Plant_Top", new Vector3(-18f, 1.7f, 52f), new Vector3(1.4f, 1.0f, 1.4f), crateMat);
            CreateHorizontalBarrel(mapRoot.transform, "BSite_LyingBarrel_Plant", new Vector3(-16.2f, 0f, 53.5f), -20f, barrelDarkMat);

            // Ninja Corner & Rak Gudang
            CreateBlock(mapRoot.transform, "BSite_ShelvingRack", new Vector3(-27f, 1.2f, 61f), new Vector3(1.2f, 2.4f, 3.5f), balconyMat);
            CreateBlock(mapRoot.transform, "BSite_Ninja_Crate", new Vector3(-24f, 0.6f, 62f), new Vector3(1.4f, 1.2f, 1.4f), crateMat);
            CreateBarrelCluster(mapRoot.transform, "BSite_Barrels_Mid", new Vector3(-21f, 0f, 42f), barrelDarkMat, barrelYellowMat);
            CreateBarrelCluster(mapRoot.transform, "BSite_Barrels_Ninja", new Vector3(-27f, 0f, 57f), barrelRedMat);

            // --- SITE B PLANT ZONE FLOOR MARKERS & HAZARD BRACKETS ---
            GameObject pzBFloor = CreateBlock(mapRoot.transform, "PlantZone_B_FloorPlate", new Vector3(-19.5f, 0.03f, 52f), new Vector3(8.5f, 0.06f, 8.5f), plantHazardMat);
            PlantZone pzB = pzBFloor.AddComponent<PlantZone>();
            pzB.ConfigureZone("Site B", new Vector3(8.5f, 3.5f, 8.5f));

            // Border & Corner Brackets Site B
            CreateCornerHazardBrackets(mapRoot.transform, "SiteB_HazardBracket", new Vector3(-19.5f, 0.06f, 52f), new Vector3(8.5f, 0.06f, 8.5f), bollardYellowMat, barrelDarkMat);
            // Sign Taktis Site B di dinding Barat
            CreateTacticalZoneSign(mapRoot.transform, "SiteB_WallSign", new Vector3(-27.4f, 2.5f, 52f), "B", new Color(0.2f, 0.8f, 0.95f));
            Transform signB = mapRoot.transform.Find("SiteB_WallSign");
            if (signB != null) signB.localRotation = Quaternion.Euler(0f, 90f, 0f);

            // Pipa & Conduit Silinder Site B
            CreateCylinder(mapRoot.transform, "BSite_Vertical_Conduit", new Vector3(-27.5f, 2.25f, 40f), new Vector3(0.25f, 2.25f, 0.25f), pipeSteelMat);
            GameObject bPipe1 = CreateCylinder(mapRoot.transform, "BSite_Ceiling_Pipe_1", new Vector3(-21f, 4.3f, 48f), new Vector3(0.3f, 6.5f, 0.3f), pipeSteelMat);
            bPipe1.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            GameObject bPipe2 = CreateCylinder(mapRoot.transform, "BSite_Ceiling_Pipe_2", new Vector3(-21f, 4.3f, 56f), new Vector3(0.3f, 6.5f, 0.3f), pipeSteelMat);
            bPipe2.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);

            // Dinding Belakang Gudang B (Pintu Menuju CT Highway)
            CreateBlock(mapRoot.transform, "BSite_Wall_North_L", new Vector3(-24f, 2.25f, 64f), new Vector3(8f, 4.5f, 0.8f), wallMat);
            CreateBlock(mapRoot.transform, "BSite_Door_North_Beam", new Vector3(-18f, 4.0f, 64f), new Vector3(3.0f, 1.0f, 0.8f), wallMat);
            CreateBlock(mapRoot.transform, "BSite_Wall_North_R", new Vector3(-14.5f, 2.25f, 64f), new Vector3(3.0f, 4.5f, 0.8f), wallMat);

            // Atap Balok Rafters Gudang B
            CreateBlock(mapRoot.transform, "BSite_Truss_1", new Vector3(-21f, 4.6f, 44f), new Vector3(14f, 0.4f, 0.8f), balconyMat);
            CreateBlock(mapRoot.transform, "BSite_Truss_2", new Vector3(-21f, 4.6f, 52f), new Vector3(14f, 0.4f, 0.8f), balconyMat);
            CreateBlock(mapRoot.transform, "BSite_Truss_3", new Vector3(-21f, 4.6f, 60f), new Vector3(14f, 0.4f, 0.8f), balconyMat);
            CreateBlock(mapRoot.transform, "BSite_Roof_Skylight_W", new Vector3(-26f, 4.8f, 51f), new Vector3(4f, 0.2f, 26f), roofMat);
            CreateBlock(mapRoot.transform, "BSite_Roof_Skylight_E", new Vector3(-16f, 4.8f, 51f), new Vector3(4f, 0.2f, 26f), roofMat);

            // ==========================================
            // 7. CT SPAWN COMMAND HUB (Z = 63 s/d 70, X = -12 s/d 12)
            // ==========================================
            CreateBlock(mapRoot.transform, "CTSpawn_Deck", new Vector3(0f, 0.02f, 66.5f), new Vector3(24f, 0.05f, 7f), interiorFloorMat);

            // Tangga Akses Belakang Heaven dari CT Spawn
            GameObject ctHeavenRamp = CreateBlock(mapRoot.transform, "CT_Heaven_Ramp", new Vector3(0f, 1.6f, 61.5f), new Vector3(3.5f, 0.25f, 4.0f), balconyMat);
            ctHeavenRamp.transform.localRotation = Quaternion.Euler(-20f, 0f, 0f);

            // Meja Komando & Radio CT
            CreateBlock(mapRoot.transform, "CT_CommandDesk", new Vector3(0f, 0.6f, 67f), new Vector3(3.2f, 1.2f, 1.2f), wallMat);
            CreateCylinder(mapRoot.transform, "CT_Radio_Antenna", new Vector3(2f, 2.5f, 68f), new Vector3(0.15f, 2.5f, 0.15f), pipeSteelMat);
            CreateBlock(mapRoot.transform, "CT_AmmoCrate_1", new Vector3(-2.5f, 0.6f, 67f), new Vector3(1.6f, 1.2f, 1.6f), crateMat);
            CreateBlock(mapRoot.transform, "CT_AmmoCrate_2", new Vector3(2.5f, 0.6f, 67f), new Vector3(1.6f, 1.2f, 1.6f), crateMat);

            // Tangki Tekanan Vertikal Industri
            CreateCylinder(mapRoot.transform, "CT_PressureTank_L", new Vector3(-12f, 1.75f, 68.5f), new Vector3(1.5f, 1.75f, 1.5f), pipeSteelMat);
            CreateCylinder(mapRoot.transform, "CT_PressureTank_R", new Vector3(12f, 1.75f, 68.5f), new Vector3(1.5f, 1.75f, 1.5f), pipeSteelMat);

            // Barel Taktis CT Spawn
            CreateBarrelCluster(mapRoot.transform, "CT_Barrels_L", new Vector3(-5f, 0f, 66f), barrelDarkMat, barrelYellowMat);
            CreateBarrelCluster(mapRoot.transform, "CT_Barrels_R", new Vector3(5f, 0f, 66f), barrelDarkMat, barrelRedMat);
            CreateHorizontalBarrel(mapRoot.transform, "CT_LyingBarrel", new Vector3(6.5f, 0f, 66.5f), 45f, barrelYellowMat);

            // Rintangan Bollard Jalur Rotasi CT Highway
            CreateBlock(mapRoot.transform, "CT_Highway_Bollard_L", new Vector3(-8f, 0.6f, 65f), new Vector3(1.2f, 1.2f, 1.0f), concreteMat);
            CreateBlock(mapRoot.transform, "CT_Highway_Bollard_R", new Vector3(8f, 0.6f, 65f), new Vector3(1.2f, 1.2f, 1.0f), concreteMat);
            CreateBollardPair(mapRoot.transform, "CT_Bollards_SiteA", new Vector3(13f, 0.55f, 64f), new Vector3(15f, 0.55f, 64f), bollardYellowMat);
            CreateBollardPair(mapRoot.transform, "CT_Bollards_SiteB", new Vector3(-13f, 0.55f, 64f), new Vector3(-15f, 0.55f, 64f), bollardYellowMat);

            // Setup Musuh 2 Bot Bersenjata T di T-Spawn
            EnsureEnemyBotsExist();

            // Pindahkan Pemain ke CT Spawn dan Nonaktifkan Objek Kamar Lama
            RepositionPlayerToCTSpawn();
            DeactivateOldRoomObjects();

            Debug.Log("[TacticalArenaBuilder] Multi-Room Tactical Map v2 with CT Spawn & Barrels berhasil dibangun!");
        }

        [ContextMenu("Clear Arena")]
        public void ClearArena()
        {
            GameObject existingNew = GameObject.Find(ArenaRootName);
            if (existingNew != null)
            {
                if (Application.isPlaying) Destroy(existingNew);
                else DestroyImmediate(existingNew);
            }

            GameObject existingOld = GameObject.Find("Tactical_Arena");
            if (existingOld != null)
            {
                if (Application.isPlaying) Destroy(existingOld);
                else DestroyImmediate(existingOld);
            }

            string[] botNames = { "Enemy_Bot_Soldier", "Enemy_Bot_Soldier_A", "Enemy_Bot_Soldier_B" };
            foreach (var bName in botNames)
            {
                GameObject bObj = GameObject.Find(bName);
                if (bObj != null)
                {
                    if (Application.isPlaying) Destroy(bObj);
                    else DestroyImmediate(bObj);
                }
            }

            GameObject existingDummy = GameObject.Find("Target_Dummy");
            if (existingDummy != null)
            {
                if (Application.isPlaying) Destroy(existingDummy);
                else DestroyImmediate(existingDummy);
            }
        }

        private void RepositionPlayerToCTSpawn()
        {
            GameObject playerObj = GameObject.Find("Player_Capsule");
            if (playerObj != null)
            {
                RepositionPlayerStatic(playerObj);
            }
        }

        private static void RepositionPlayerStatic(GameObject playerObj)
        {
            CharacterController cc = playerObj.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;

            // Player adalah CT (Defender), spawn di CT Spawn (Z = 66) menghadap selatan (180 deg)
            playerObj.transform.position = new Vector3(0f, 1f, 66f);
            playerObj.transform.rotation = Quaternion.Euler(0f, 180f, 0f);

            if (cc != null) cc.enabled = true;

            PlayerHealth ph = playerObj.GetComponent<PlayerHealth>();
            if (ph == null)
            {
                ph = playerObj.AddComponent<PlayerHealth>();
            }
            ph.SetSpawnPoint(new Vector3(0f, 1f, 66f), Quaternion.Euler(0f, 180f, 0f));

            // Reset rotasi Camera_Pivot jika ada
            Transform camPivot = playerObj.transform.Find("Camera_Pivot");
            if (camPivot != null)
            {
                camPivot.localRotation = Quaternion.identity;
            }

            // Pastikan FPSWeaponManager terpasang untuk sistem Switch Weapon (Slot 1-4)
            FPSWeaponManager wm = playerObj.GetComponent<FPSWeaponManager>();
            if (wm == null)
            {
                wm = playerObj.AddComponent<FPSWeaponManager>();
            }
        }

        private void DeactivateOldRoomObjects()
        {
            string[] oldObjNames = new string[]
            {
                "Wall 1", "Wall 2", "Wall 3", "Wall 4", "Ground",
                "Cube", "Cube (1)", "Cube (2)", "Cube (3)", "Cube (4)", "Cube (5)",
                "Tactical_Arena", "Target_Dummy", "Button_Static", "Button_Linear", "Button_Jiggle", "Idle", "Enemy_Bot_Soldier"
            };

            foreach (string objName in oldObjNames)
            {
                GameObject obj = GameObject.Find(objName);
                if (obj != null)
                {
                    if (Application.isPlaying) Destroy(obj);
                    else DestroyImmediate(obj);
                }
            }
        }

        public static void CleanupStrayObjects()
        {
            string[] trashNames = { "Target_Dummy", "Button_Static", "Button_Linear", "Button_Jiggle", "Idle", "Enemy_Bot_Soldier" };
            foreach (var tName in trashNames)
            {
                GameObject obj = GameObject.Find(tName);
                if (obj != null)
                {
                    if (Application.isPlaying) Destroy(obj);
                    else DestroyImmediate(obj);
                }
            }
        }

        public static void EnsureEnemyBotsExist()
        {
            // Bersihkan dummy cyan, tombol mode, dan objek melayang di void
            CleanupStrayObjects();

            // Setup Bot A (T Attacker pembawa C4 menuju Site A)
            List<Vector3> waypointsA = new List<Vector3>
            {
                new Vector3(4f, 0f, 2f),       // T-Spawn Base
                new Vector3(19f, 0f, 10f),     // Arch Lane A
                new Vector3(21f, 0f, 22f),     // Long A Container Blue
                new Vector3(19f, 0f, 32f),     // Long A Connector A area
                new Vector3(19f, 0f, 44f),     // Long A North
                new Vector3(20f, 0f, 52f),     // Site A Doorway
                new Vector3(21f, 0f, 59.5f)    // Site A Plant Zone (Plant C4)
            };
            CreateSingleEnemyBot("Enemy_Bot_Soldier_A", new Vector3(4f, 0f, 2f), waypointsA, hasC4: true);

            // Setup Bot B (T Attacker pendukung menuju Site B)
            List<Vector3> waypointsB = new List<Vector3>
            {
                new Vector3(-4f, 0f, 2f),      // T-Spawn Base
                new Vector3(-19f, 0f, 10f),    // Arch Lane B
                new Vector3(-21f, 0f, 24f),    // Close B Container Green
                new Vector3(-17f, 0f, 32f),    // Close B Corridor
                new Vector3(-17f, 0f, 38f),    // Site B Main Front Doorway
                new Vector3(-19.5f, 0f, 52f)   // Site B Plant Zone (Support / Attack)
            };
            CreateSingleEnemyBot("Enemy_Bot_Soldier_B", new Vector3(-4f, 0f, 2f), waypointsB, hasC4: false);
        }

        private static void CreateSingleEnemyBot(string botName, Vector3 spawnPos, List<Vector3> waypoints, bool hasC4 = false)
        {
            GameObject existingBot = GameObject.Find(botName);
            if (existingBot != null)
            {
                if (existingBot.transform.position.y > 0.05f)
                {
                    Vector3 p = existingBot.transform.position;
                    p.y = 0f;
                    existingBot.transform.position = p;
                }

                Transform rightHand = FindBoneRecursive(existingBot.transform, "RightHand");
                Transform rifle = FindBoneRecursive(existingBot.transform, "Bot_Rifle");
                if (rightHand != null && rifle != null && rifle.parent != rightHand)
                {
                    rifle.SetParent(rightHand, false);
                    rifle.localPosition = new Vector3(0.06f, 0.08f, 0.15f);
                    rifle.localRotation = Quaternion.Euler(0f, 90f, 0f);
                    rifle.localScale = new Vector3(0.06f, 0.12f, 0.55f);
                }

                EnemyHumanoidAI existingAI = existingBot.GetComponent<EnemyHumanoidAI>();
                if (existingAI != null)
                {
                    if (waypoints != null) existingAI.SetCustomWaypoints(waypoints);
                    existingAI.HasC4 = hasC4;
                }
                return;
            }

            GameObject botObj = new GameObject(botName);
            botObj.transform.position = spawnPos;

            Shader urpShader = Shader.Find("Universal Render Pipeline/Lit");
            if (urpShader == null) urpShader = Shader.Find("Standard");

            bool meshSpawned = false;

#if UNITY_EDITOR
            // Gunakan Model Mixamo Humanoid jika tersedia di project
            GameObject mixamoFbx = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Mixamo/Idle.fbx");
            if (mixamoFbx != null)
            {
                GameObject mixamoMesh = (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(mixamoFbx, botObj.transform);
                mixamoMesh.name = "Bot_MixamoMesh";
                mixamoMesh.transform.localPosition = Vector3.zero;
                mixamoMesh.transform.localRotation = Quaternion.identity;

                RuntimeAnimatorController animCtrl = UnityEditor.AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/Mixamo/Player_AnimatorController.controller");
                Animator anim = mixamoMesh.GetComponent<Animator>();
                if (anim != null && animCtrl != null)
                {
                    anim.runtimeAnimatorController = animCtrl;
                    anim.applyRootMotion = false;
                }

                meshSpawned = true;
            }
#endif

            if (!meshSpawned)
            {
                // Fallback: Model Prajurit Taktis Stylized
                GameObject meshObj = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                meshObj.name = "Body_Mesh";
                meshObj.transform.SetParent(botObj.transform, false);
                meshObj.transform.localPosition = new Vector3(0f, 0.95f, 0f);
                if (Application.isPlaying) Destroy(meshObj.GetComponent<Collider>());
                else DestroyImmediate(meshObj.GetComponent<Collider>());

                Material botMat = new Material(urpShader) { color = new Color(0.25f, 0.32f, 0.22f), name = "Mat_BotArmor" };
                meshObj.GetComponent<MeshRenderer>().sharedMaterial = botMat;

                // Helm & Visor Taktis Merah
                GameObject visorObj = GameObject.CreatePrimitive(PrimitiveType.Cube);
                visorObj.name = "Head_Visor";
                visorObj.transform.SetParent(meshObj.transform, false);
                visorObj.transform.localPosition = new Vector3(0f, 0.65f, 0.30f);
                visorObj.transform.localScale = new Vector3(0.5f, 0.2f, 0.25f);
                if (Application.isPlaying) Destroy(visorObj.GetComponent<Collider>());
                else DestroyImmediate(visorObj.GetComponent<Collider>());

                Material visorMat = new Material(urpShader) { color = new Color(0.95f, 0.2f, 0.2f), name = "Mat_BotVisor" };
                visorObj.GetComponent<MeshRenderer>().sharedMaterial = visorMat;
            }

            // Senjata di tangan bot: tempelkan ke tulang tangan kanan jika ada
            GameObject gunObj = GameObject.CreatePrimitive(PrimitiveType.Cube);
            gunObj.name = "Bot_Rifle";
            if (Application.isPlaying) Destroy(gunObj.GetComponent<Collider>());
            else DestroyImmediate(gunObj.GetComponent<Collider>());

            Material gunMat = new Material(urpShader) { color = new Color(0.12f, 0.12f, 0.14f), name = "Mat_BotRifle" };
            gunObj.GetComponent<MeshRenderer>().sharedMaterial = gunMat;

            Transform rightHandBone = FindBoneRecursive(botObj.transform, "RightHand");
            if (rightHandBone != null)
            {
                gunObj.transform.SetParent(rightHandBone, false);
                gunObj.transform.localPosition = new Vector3(0.06f, 0.08f, 0.15f);
                gunObj.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
                gunObj.transform.localScale = new Vector3(0.06f, 0.12f, 0.55f);
            }
            else
            {
                gunObj.transform.SetParent(botObj.transform, false);
                gunObj.transform.localPosition = new Vector3(0.35f, 1.1f, 0.45f);
                gunObj.transform.localScale = new Vector3(0.12f, 0.2f, 0.8f);
            }

            // Pasang otak AI dan waypoint kustom
            EnemyHumanoidAI botAI = botObj.AddComponent<EnemyHumanoidAI>();
            if (waypoints != null && waypoints.Count > 0)
            {
                botAI.SetCustomWaypoints(waypoints);
            }
            botAI.HasC4 = hasC4;
        }

        private static Transform FindBoneRecursive(Transform parent, string nameSub)
        {
            if (parent.name.IndexOf(nameSub, System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return parent;
            }

            for (int i = 0; i < parent.childCount; i++)
            {
                Transform found = FindBoneRecursive(parent.GetChild(i), nameSub);
                if (found != null) return found;
            }

            return null;
        }

        private GameObject CreateBlock(Transform parent, string blockName, Vector3 localPos, Vector3 localScale, Material mat)
        {
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = blockName;
            cube.transform.SetParent(parent, false);
            cube.transform.localPosition = localPos;
            cube.transform.localScale = localScale;

            if (mat != null)
            {
                MeshRenderer mr = cube.GetComponent<MeshRenderer>();
                if (mr != null) mr.sharedMaterial = mat;
            }

            return cube;
        }

        private GameObject CreateCylinder(Transform parent, string cylName, Vector3 localPos, Vector3 localScale, Material mat)
        {
            GameObject cyl = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            cyl.name = cylName;
            cyl.transform.SetParent(parent, false);
            cyl.transform.localPosition = localPos;
            cyl.transform.localScale = localScale;

            if (mat != null)
            {
                MeshRenderer mr = cyl.GetComponent<MeshRenderer>();
                if (mr != null) mr.sharedMaterial = mat;
            }

            return cyl;
        }

        private void CreateBarrelCluster(Transform parent, string clusterName, Vector3 centerPos, Material barrelMat, Material secondaryMat = null)
        {
            GameObject group = new GameObject(clusterName);
            group.transform.SetParent(parent, false);
            group.transform.localPosition = centerPos;

            Material secMat = secondaryMat != null ? secondaryMat : barrelMat;

            // Tong barel industri 55-gallon standar: diameter 0.85m, tinggi 1.15m
            CreateCylinder(group.transform, "Barrel_A", new Vector3(-0.35f, 0.58f, -0.25f), new Vector3(0.85f, 0.58f, 0.85f), barrelMat);
            CreateCylinder(group.transform, "Barrel_B", new Vector3(0.35f, 0.58f, -0.25f), new Vector3(0.85f, 0.58f, 0.85f), secMat);
            CreateCylinder(group.transform, "Barrel_C", new Vector3(0f, 0.58f, 0.35f), new Vector3(0.85f, 0.58f, 0.85f), barrelMat);
        }

        private GameObject CreateHorizontalBarrel(Transform parent, string barrelName, Vector3 localPos, float rotY, Material mat)
        {
            GameObject cyl = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            cyl.name = barrelName;
            cyl.transform.SetParent(parent, false);
            cyl.transform.localPosition = new Vector3(localPos.x, localPos.y + 0.425f, localPos.z);
            cyl.transform.localScale = new Vector3(0.85f, 0.58f, 0.85f);
            cyl.transform.localRotation = Quaternion.Euler(0f, rotY, 90f);

            if (mat != null)
            {
                MeshRenderer mr = cyl.GetComponent<MeshRenderer>();
                if (mr != null) mr.sharedMaterial = mat;
            }

            return cyl;
        }

        private void CreateBollardPair(Transform parent, string namePrefix, Vector3 leftPos, Vector3 rightPos, Material mat)
        {
            CreateCylinder(parent, namePrefix + "_L", leftPos, new Vector3(0.35f, 0.55f, 0.35f), mat);
            CreateCylinder(parent, namePrefix + "_R", rightPos, new Vector3(0.35f, 0.55f, 0.35f), mat);
        }

        private void CreateCornerHazardBrackets(Transform parent, string prefix, Vector3 center, Vector3 size, Material yellowMat, Material darkMat)
        {
            float hx = size.x * 0.5f;
            float hz = size.z * 0.5f;
            Vector3[] corners = new Vector3[]
            {
                center + new Vector3(-hx, 0f, -hz),
                center + new Vector3(hx, 0f, -hz),
                center + new Vector3(-hx, 0f, hz),
                center + new Vector3(hx, 0f, hz)
            };

            for (int i = 0; i < corners.Length; i++)
            {
                GameObject cornerObj = CreateBlock(parent, prefix + "_" + i, corners[i], new Vector3(0.6f, 0.14f, 0.6f), yellowMat);
                CreateBlock(cornerObj.transform, "Inner", Vector3.up * 0.04f, new Vector3(0.38f, 0.14f, 0.38f), darkMat);
            }
        }

        private void CreateTacticalZoneSign(Transform parent, string name, Vector3 pos, string letter, Color glowColor)
        {
            GameObject signRoot = new GameObject(name);
            signRoot.transform.SetParent(parent, false);
            signRoot.transform.localPosition = pos;

            Shader urpShader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            Material boardMat = new Material(urpShader) { color = new Color(0.12f, 0.14f, 0.16f) };
            Material glowMat = new Material(urpShader) { color = glowColor };

            // Papan dasar
            CreateBlock(signRoot.transform, "Backboard", Vector3.zero, new Vector3(1.8f, 1.2f, 0.12f), boardMat);
            // Border hazard
            CreateBlock(signRoot.transform, "Border_Top", new Vector3(0f, 0.55f, 0.07f), new Vector3(1.7f, 0.08f, 0.08f), glowMat);
            CreateBlock(signRoot.transform, "Border_Bot", new Vector3(0f, -0.55f, 0.07f), new Vector3(1.7f, 0.08f, 0.08f), glowMat);
            // Plat teks huruf site A / B
            CreateBlock(signRoot.transform, "Letter_Icon", new Vector3(0f, 0f, 0.08f), new Vector3(0.55f, 0.75f, 0.06f), glowMat);
        }
    }
}
