using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using FPSFramework.Player;

namespace FPSFramework.Combat
{
    /// <summary>
    /// Membangun Map Taktis Penuh (Tactical 3-Lane Map: 60m x 75m)
    /// bergaya Stylized Low-Poly bertema de_inferno khas Counter-Strike:
    /// - T-Spawn: Alun-alun desa Italia (Piazza del Villaggio) dikelilingi rumah desa, lentera, dan pohon cemara.
    /// - Banana: Lorong melengkung dengan pagar desa, barikade peti kayu 'Car' dan 'Half-Wall'.
    /// - Bombsite B: Alun-alun reruntuhan batu dengan landmark utama Stone Fountain, pilar batu, dan peti kayu Coffin.
    /// - Mid Lane & Alt-Mid: Koridor panjang berpagar gedung bertingkat tinggi untuk duel jarak jauh (AWP).
    /// - Apartments & Balcony: Bangunan bertingkat dengan tangga luar (Prop_BuildingStairs) dan balkon menghadap Site A.
    /// - Bombsite A: Area terbuka dengan Pit, jajaran pilar Graveyard, guci batu, dan cover Triple Box.
    /// - CT Spawn & Speedway: Jalur rotasi penghubung cepat antar site di bagian belakang.
    /// </summary>
    [ExecuteAlways]
    public class TacticalArenaBuilder : MonoBehaviour
    {
        public const string ArenaRootName = "Tactical_3Lane_Map";

        [Header("Pengaturan Palet Warna Tuscan/Inferno")]
        [SerializeField] private Color wallStuccoColor = new Color(0.84f, 0.78f, 0.66f);     // Warm Italian Stucco / Plaster
        [SerializeField] private Color wallBrickColor = new Color(0.68f, 0.38f, 0.28f);      // Tuscan Terracotta Brick
        [SerializeField] private Color groundStoneColor = new Color(0.70f, 0.65f, 0.56f);    // Aged Cobblestone Paving
        [SerializeField] private Color stoneTrimColor = new Color(0.56f, 0.53f, 0.48f);      // Limestone Pillars & Arches
        [SerializeField] private Color terracottaRoofColor = new Color(0.74f, 0.34f, 0.22f); // Terracotta Clay Roof
        [SerializeField] private Color wroughtIronColor = new Color(0.22f, 0.24f, 0.26f);    // Black Wrought Iron Balcony
        [SerializeField] private Color woodCrateColor = new Color(0.58f, 0.44f, 0.28f);      // Rustic Oak Wood Crate
        [SerializeField] private Color interiorTileColor = new Color(0.62f, 0.58f, 0.50f);   // Interior Stone Tile

        [Header("Opsi Otomatis")]
        [SerializeField] private bool autoBuildOnStart = true;

        private static Shader _cachedUrpShader;
        private static Material _woodBrightMat;
        private static Material _woodDarkMat;

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
            if (existingMap == null || existingMap.transform.Find("Inferno_Fountain_SiteB") == null || existingMap.transform.Find("PlantZone_A_FloorPlate") == null)
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
            _cachedUrpShader = urpShader;

            // Load Materials
#if UNITY_EDITOR
            _woodBrightMat = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>("Assets/Models/Props/WoodCrates/Materials/Wood_bright.mat");
            _woodDarkMat = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>("Assets/Models/Props/WoodCrates/Materials/Wood_dark.mat");
#endif
            if (_woodBrightMat == null) _woodBrightMat = new Material(urpShader) { color = woodCrateColor, name = "Mat_WoodBrightFallback" };
            if (_woodDarkMat == null) _woodDarkMat = new Material(urpShader) { color = woodCrateColor * 0.7f, name = "Mat_WoodDarkFallback" };

            Material stuccoMat = new Material(urpShader) { color = wallStuccoColor, name = "Mat_ItalianStucco" };
            Material brickMat = new Material(urpShader) { color = wallBrickColor, name = "Mat_TerracottaBrick" };
            Material groundMat = new Material(urpShader) { color = groundStoneColor, name = "Mat_CobblestoneGround" };
            Material stoneMat = new Material(urpShader) { color = stoneTrimColor, name = "Mat_LimestoneTrim" };
            Material roofMat = new Material(urpShader) { color = terracottaRoofColor, name = "Mat_TerracottaRoof" };
            Material ironMat = new Material(urpShader) { color = wroughtIronColor, name = "Mat_WroughtIron" };
            Material interiorMat = new Material(urpShader) { color = interiorTileColor, name = "Mat_InteriorStone" };
            Material plantHazardMat = new Material(urpShader) { color = new Color(0.92f, 0.72f, 0.12f, 0.95f), name = "Mat_PlantHazard" };
            Material accentCyanMat = new Material(urpShader) { color = new Color(0.0f, 0.75f, 0.85f), name = "Mat_TechCore" };
            Material barrelDarkMat = new Material(urpShader) { color = new Color(0.22f, 0.20f, 0.18f), name = "Mat_BarrelDark" };
            Material barrelRedMat = new Material(urpShader) { color = new Color(0.70f, 0.22f, 0.20f), name = "Mat_BarrelRed" };
            Material bollardYellowMat = new Material(urpShader) { color = new Color(0.88f, 0.70f, 0.15f), name = "Mat_BollardYellow" };

            // ==========================================
            // 0. LANTAI DASAR DAN DINDING PERIMETER DESA INFERNO
            // ==========================================
            CreateBlock(mapRoot.transform, "Inferno_Floor", new Vector3(0f, -0.05f, 32.5f), new Vector3(60f, 0.1f, 75f), groundMat);

            // Dinding Luar Keliling (60m x 75m x 5.5m)
            CreateBlock(mapRoot.transform, "Outer_Wall_South", new Vector3(0f, 2.75f, -5f), new Vector3(60f, 5.5f, 1.2f), stuccoMat);
            CreateBlock(mapRoot.transform, "Outer_Wall_North", new Vector3(0f, 2.75f, 70f), new Vector3(60f, 5.5f, 1.2f), stuccoMat);
            CreateBlock(mapRoot.transform, "Outer_Wall_West", new Vector3(-30f, 2.75f, 32.5f), new Vector3(1.2f, 5.5f, 76f), stuccoMat);
            CreateBlock(mapRoot.transform, "Outer_Wall_East", new Vector3(30f, 2.75f, 32.5f), new Vector3(1.2f, 5.5f, 76f), stuccoMat);

            // Dinding Pemisah Utama (Banana & Mid, serta Mid & Long A)
            CreateBlock(mapRoot.transform, "Divider_Banana_Mid_1", new Vector3(-12f, 2.5f, 18f), new Vector3(1.2f, 5f, 16f), stuccoMat);
            CreateBlock(mapRoot.transform, "Divider_Banana_Mid_Arch", new Vector3(-12f, 4.5f, 28f), new Vector3(1.2f, 1f, 4f), stoneMat);
            CreateBlock(mapRoot.transform, "Divider_Banana_Mid_2", new Vector3(-12f, 2.5f, 46f), new Vector3(1.2f, 5f, 32f), brickMat);

            CreateBlock(mapRoot.transform, "Divider_Mid_LongA_1", new Vector3(12f, 2.5f, 20f), new Vector3(1.2f, 5f, 20f), stuccoMat);
            CreateBlock(mapRoot.transform, "Divider_Mid_LongA_Arch", new Vector3(12f, 4.5f, 32f), new Vector3(1.2f, 1f, 4f), stoneMat);
            CreateBlock(mapRoot.transform, "Divider_Mid_LongA_2", new Vector3(12f, 2.5f, 48f), new Vector3(1.2f, 5f, 28f), brickMat);

            // ==========================================
            // 1. T-SPAWN (PIAZZA DEL VILLAGGIO: Z = -4 s/d 12)
            // ==========================================
            // Rumah-rumah pedesaan Italia di sisi kiri dan kanan T-Spawn
            SpawnPrefab(mapRoot.transform, "TSpawn_House_L", "Assets/Models/Environment/Village/Prefabs/House_1.prefab", new Vector3(-18f, 0f, 1f), Quaternion.Euler(0f, 90f, 0f), Vector3.one * 1.1f, stuccoMat);
            SpawnPrefab(mapRoot.transform, "TSpawn_House_R", "Assets/Models/Environment/Village/Prefabs/House_2.prefab", new Vector3(18f, 0f, 1f), Quaternion.Euler(0f, -90f, 0f), Vector3.one * 1.1f, stuccoMat);

            // Pohon cemara pedesaan (Pine Trees)
            SpawnPrefab(mapRoot.transform, "TSpawn_Pine_1", "Assets/Models/Environment/Village/Prefabs/Pine Tree_1.prefab", new Vector3(-25f, 0f, -2f), Quaternion.identity, Vector3.one * 1.2f);
            SpawnPrefab(mapRoot.transform, "TSpawn_Pine_2", "Assets/Models/Environment/Village/Prefabs/Pine Tree_2.prefab", new Vector3(25f, 0f, -2f), Quaternion.identity, Vector3.one * 1.2f);

            // Bangku taman, pot bunga, dan lentera desa di T-Spawn
            SpawnPrefab(mapRoot.transform, "TSpawn_Bench", "Assets/Models/Environment/Village/Prefabs/Bench.prefab", new Vector3(0f, 0f, -3f), Quaternion.identity, Vector3.one * 1.2f);
            SpawnPrefab(mapRoot.transform, "TSpawn_Lantern_L", "Assets/Models/Environment/Village/Prefabs/Lantern.prefab", new Vector3(-6f, 0f, 6f), Quaternion.identity, Vector3.one * 1.2f);
            SpawnPrefab(mapRoot.transform, "TSpawn_Lantern_R", "Assets/Models/Environment/Village/Prefabs/Lantern.prefab", new Vector3(6f, 0f, 6f), Quaternion.identity, Vector3.one * 1.2f);
            SpawnPrefab(mapRoot.transform, "TSpawn_FlowerPot_1", "Assets/Models/Environment/Village/Prefabs/Flower Pot.prefab", new Vector3(-8f, 0f, 2f), Quaternion.identity, Vector3.one * 1.4f);
            SpawnPrefab(mapRoot.transform, "TSpawn_FlowerPot_2", "Assets/Models/Environment/Village/Prefabs/Flower Pot.prefab", new Vector3(8f, 0f, 2f), Quaternion.identity, Vector3.one * 1.4f);

            // Barikade staging peti kayu di T-Spawn
            SpawnWoodCrate(mapRoot.transform, "TSpawn_Crate_1", "Crate_01", new Vector3(-3.5f, 0.6f, -1.5f), Vector3.one * 1.2f, _woodBrightMat);
            SpawnWoodCrate(mapRoot.transform, "TSpawn_Crate_2", "Crate_04", new Vector3(3.5f, 0.6f, -1.5f), Vector3.one * 1.2f, _woodDarkMat);
            SpawnWoodCrate(mapRoot.transform, "TSpawn_Crate_Stack", "Crate_08", new Vector3(0f, 0.6f, 2.5f), Vector3.one * 1.3f, _woodBrightMat);

            // Gapura lengkung batu di gerbang keluar T-Spawn
            CreateBlock(mapRoot.transform, "TSpawn_Gate_Banana", new Vector3(-19f, 4.5f, 10f), new Vector3(6f, 1.0f, 1.2f), stoneMat);
            CreateBlock(mapRoot.transform, "TSpawn_Gate_Mid", new Vector3(0f, 4.5f, 10f), new Vector3(6f, 1.0f, 1.2f), stoneMat);
            CreateBlock(mapRoot.transform, "TSpawn_Gate_LaneA", new Vector3(19f, 4.5f, 10f), new Vector3(6f, 1.0f, 1.2f), stoneMat);

            // ==========================================
            // 2. JALUR KIRI: BANANA (B CHOKEPOINT: Z = 12 s/d 38)
            // ==========================================
            // Pagar kayu desa (Fence) di sepanjang dinding luar Banana
            SpawnPrefab(mapRoot.transform, "Banana_Fence_1", "Assets/Models/Environment/Village/Prefabs/Fence.prefab", new Vector3(-28.5f, 0f, 16f), Quaternion.Euler(0f, 90f, 0f), Vector3.one * 1.2f);
            SpawnPrefab(mapRoot.transform, "Banana_Fence_2", "Assets/Models/Environment/Village/Prefabs/Fence.prefab", new Vector3(-28.5f, 0f, 22f), Quaternion.Euler(0f, 90f, 0f), Vector3.one * 1.2f);
            SpawnPrefab(mapRoot.transform, "Banana_Fence_3", "Assets/Models/Environment/Village/Prefabs/Fence.prefab", new Vector3(-28.5f, 0f, 28f), Quaternion.Euler(0f, 90f, 0f), Vector3.one * 1.2f);

            // Barikade 'Half-Wall' dan 'Car' khas Banana menggunakan susunan peti kayu
            CreateBlock(mapRoot.transform, "Banana_HalfWall_Base", new Vector3(-21f, 0.6f, 27f), new Vector3(4.5f, 1.2f, 0.8f), stoneMat);
            SpawnWoodCrate(mapRoot.transform, "Banana_Car_Crate_1", "Crate_01", new Vector3(-16f, 0.6f, 23f), Vector3.one * 1.4f, _woodBrightMat);
            SpawnWoodCrate(mapRoot.transform, "Banana_Car_Crate_2", "Crate_05", new Vector3(-16f, 1.7f, 23f), Vector3.one * 1.1f, _woodDarkMat);
            SpawnWoodCrate(mapRoot.transform, "Banana_Car_Crate_3", "Crate_03", new Vector3(-17.5f, 0.6f, 23.5f), Vector3.one * 1.2f, _woodBrightMat);
            CreateBarrelCluster(mapRoot.transform, "Banana_Barrels_Sandbag", new Vector3(-25f, 0f, 20f), barrelDarkMat, barrelRedMat);

            // Lentera jalan di belokan Banana
            SpawnPrefab(mapRoot.transform, "Banana_Lantern_Choke", "Assets/Models/Environment/Village/Prefabs/Lantern.prefab", new Vector3(-14f, 0f, 32f), Quaternion.identity, Vector3.one * 1.2f);
            SpawnPrefab(mapRoot.transform, "Banana_PineTree", "Assets/Models/Environment/Village/Prefabs/Pine Tree_2.prefab", new Vector3(-28f, 0f, 35f), Quaternion.identity, Vector3.one * 1.3f);

            // ==========================================
            // 3. BOMBSITE B: THE FOUNTAIN RUINS (Z = 38 s/d 64, X = -28 s/d -12)
            // ==========================================
            CreateBlock(mapRoot.transform, "BSite_Courtyard_Floor", new Vector3(-20f, 0.02f, 51f), new Vector3(16f, 0.05f, 26f), interiorMat);

            // LANDMARK UTAMA: STONE FOUNTAIN DI TITIK PUSAT BOMBSITE B!
            GameObject fountainObj = SpawnPrefab(mapRoot.transform, "Inferno_Fountain_SiteB", "Assets/Models/Environment/TrimSheet/TrimSheet_Prefabs/Fountain.prefab", new Vector3(-19.5f, 0f, 52f), Quaternion.identity, Vector3.one * 1.35f, stoneMat);
            // Tambahkan platform batu dasar sebagai undakan air mancur
            SpawnPrefab(mapRoot.transform, "BSite_Fountain_Platform", "Assets/Models/Environment/TrimSheet/TrimSheet_Prefabs/Platform.prefab", new Vector3(-19.5f, 0f, 52f), Quaternion.identity, new Vector3(3.5f, 0.35f, 3.5f), stoneMat);

            // RUINS & CHURCH COLONNADE (Pilar batu kuno di sisi Utara dan Timur Site B)
            SpawnPrefab(mapRoot.transform, "BSite_Pillar_1", "Assets/Models/Environment/TrimSheet/TrimSheet_Prefabs/Pillar.prefab", new Vector3(-26f, 0f, 62f), Quaternion.identity, Vector3.one * 1.4f, stoneMat);
            SpawnPrefab(mapRoot.transform, "BSite_Pillar_2", "Assets/Models/Environment/TrimSheet/TrimSheet_Prefabs/PillarDeco.prefab", new Vector3(-21f, 0f, 62f), Quaternion.identity, Vector3.one * 1.4f, stoneMat);
            SpawnPrefab(mapRoot.transform, "BSite_Pillar_3", "Assets/Models/Environment/TrimSheet/TrimSheet_Prefabs/Pillar.prefab", new Vector3(-16f, 0f, 62f), Quaternion.identity, Vector3.one * 1.4f, stoneMat);
            SpawnPrefab(mapRoot.transform, "BSite_StoneRail_Ruins", "Assets/Models/Environment/TrimSheet/TrimSheet_Prefabs/Rail.prefab", new Vector3(-18.5f, 0f, 62f), Quaternion.identity, Vector3.one * 1.3f, stoneMat);

            // Rumah Pedesaan di sudut barat Bombsite B (Gereja / Ruins Facade)
            SpawnPrefab(mapRoot.transform, "BSite_Church_House", "Assets/Models/Environment/Village/Prefabs/House_3.prefab", new Vector3(-27f, 0f, 44f), Quaternion.Euler(0f, 90f, 0f), Vector3.one * 1.25f, stuccoMat);

            // COFFIN / DARK SPOT (Tumpukan peti kayu gelap di sudut bombsite B)
            SpawnWoodCrate(mapRoot.transform, "BSite_Coffin_1", "Crate_02", new Vector3(-14f, 0.6f, 48f), Vector3.one * 1.4f, _woodDarkMat);
            SpawnWoodCrate(mapRoot.transform, "BSite_Coffin_2", "Crate_05", new Vector3(-14f, 1.7f, 48f), Vector3.one * 1.2f, _woodDarkMat);
            SpawnWoodCrate(mapRoot.transform, "BSite_Coffin_3", "Crate_09", new Vector3(-14f, 0.6f, 49.5f), Vector3.one * 1.3f, _woodDarkMat);

            // 1st / 2nd Oranges & Construction Box
            SpawnWoodCrate(mapRoot.transform, "BSite_Oranges_Box_1", "Crate_04", new Vector3(-25f, 0.6f, 56f), Vector3.one * 1.3f, _woodBrightMat);
            SpawnWoodCrate(mapRoot.transform, "BSite_Oranges_Box_2", "Crate_08", new Vector3(-25f, 1.6f, 56f), Vector3.one * 1.1f, _woodDarkMat);
            CreateBarrelCluster(mapRoot.transform, "BSite_Barrels_Oranges", new Vector3(-26.5f, 0f, 53f), barrelDarkMat, barrelRedMat);

            // BOMB PLANT ZONE SITE B (Berpusat di sekitar Fountain)
            GameObject pzBFloor = CreateBlock(mapRoot.transform, "PlantZone_B_FloorPlate", new Vector3(-19.5f, 0.03f, 52f), new Vector3(9.0f, 0.06f, 9.0f), plantHazardMat);
            PlantZone pzB = pzBFloor.AddComponent<PlantZone>();
            pzB.ConfigureZone("Site B", new Vector3(9.0f, 3.5f, 9.0f));
            CreateCornerHazardBrackets(mapRoot.transform, "SiteB_HazardBracket", new Vector3(-19.5f, 0.06f, 52f), new Vector3(9.0f, 0.06f, 9.0f), bollardYellowMat, barrelDarkMat);
            CreateTacticalZoneSign(mapRoot.transform, "SiteB_WallSign", new Vector3(-27.4f, 2.5f, 52f), "B", new Color(0.2f, 0.8f, 0.95f));
            Transform signB = mapRoot.transform.Find("SiteB_WallSign");
            if (signB != null) signB.localRotation = Quaternion.Euler(0f, 90f, 0f);

            // ==========================================
            // 4. JALUR TENGAH: MID LANE & ALT-MID (Z = 12 s/d 48, X = -8 s/d 10)
            // ==========================================
            // Gedung tinggi bertingkat (Town Pack) memagari Mid Lane
            SpawnPrefab(mapRoot.transform, "Mid_Townhouse_1", "Assets/Models/Environment/Town/Prefabs/Env_ResidentBuilding_01.prefab", new Vector3(-4f, 0f, 38f), Quaternion.Euler(0f, 90f, 0f), Vector3.one * 1.15f, brickMat);
            SpawnPrefab(mapRoot.transform, "Mid_Commercial_1", "Assets/Models/Environment/Town/Prefabs/Env_CommercialBuilding_01.prefab", new Vector3(6f, 0f, 24f), Quaternion.identity, Vector3.one * 1.15f, stuccoMat);

            // Monumen taman batu tengah Mid & Benches
            CreateBlock(mapRoot.transform, "Mid_Plaza_Planter", new Vector3(0f, 0.4f, 28f), new Vector3(4.0f, 0.8f, 3.0f), stoneMat);
            SpawnPrefab(mapRoot.transform, "Mid_Vase_Monument", "Assets/Models/Environment/TrimSheet/TrimSheet_Prefabs/Vase.prefab", new Vector3(0f, 0.8f, 28f), Quaternion.identity, Vector3.one * 1.4f, stoneMat);
            SpawnPrefab(mapRoot.transform, "Mid_Bench_Plaza", "Assets/Models/Environment/Village/Prefabs/Bench.prefab", new Vector3(0f, 0f, 25.5f), Quaternion.identity, Vector3.one * 1.2f);
            SpawnPrefab(mapRoot.transform, "Mid_Lantern_Post", "Assets/Models/Environment/Village/Prefabs/Lantern.prefab", new Vector3(3f, 0f, 28f), Quaternion.identity, Vector3.one * 1.3f);

            // Cover peti kayu di Mid Lane
            SpawnWoodCrate(mapRoot.transform, "Mid_Crate_Cover_W", "Crate_01", new Vector3(-6f, 0.6f, 22f), Vector3.one * 1.3f, _woodBrightMat);
            SpawnWoodCrate(mapRoot.transform, "Mid_Crate_Cover_E", "Crate_04", new Vector3(4f, 0.6f, 32f), Vector3.one * 1.3f, _woodDarkMat);

            // ==========================================
            // 5. APARTMENTS (APS) & BALCONY (Z = 32 s/d 54, X = 6 s/d 16)
            // ==========================================
            // Tangga outdoor naik ke lantai 2 apartemen (Prop_BuildingStairs)
            SpawnPrefab(mapRoot.transform, "Aps_Stairs_Access", "Assets/Models/Environment/Town/Prefabs/Prop_BuildingStairs.prefab", new Vector3(8.5f, 0f, 35f), Quaternion.Euler(0f, 180f, 0f), Vector3.one * 1.25f, ironMat);

            // Lantai 2 apartemen (Walkway dalam)
            CreateBlock(mapRoot.transform, "Aps_SecondFloor_Deck", new Vector3(10f, 3.2f, 44f), new Vector3(6f, 0.35f, 16f), interiorMat);
            CreateBlock(mapRoot.transform, "Aps_Hallway_Roof", new Vector3(10f, 5.8f, 44f), new Vector3(6.5f, 0.3f, 16.5f), roofMat);
            CreateBlock(mapRoot.transform, "Aps_Wall_West", new Vector3(7f, 4.5f, 44f), new Vector3(0.6f, 2.6f, 16f), stuccoMat);

            // BALKON APARTEMEN YANG MENGHADAP LANGSUNG KE SITE A!
            CreateBlock(mapRoot.transform, "Aps_Balcony_Deck", new Vector3(13f, 3.2f, 54f), new Vector3(4.5f, 0.35f, 5.0f), ironMat);
            SpawnPrefab(mapRoot.transform, "Aps_Balcony_Rail_North", "Assets/Models/Environment/TrimSheet/TrimSheet_Prefabs/Rail.prefab", new Vector3(13f, 3.4f, 56.5f), Quaternion.identity, Vector3.one * 1.25f, stoneMat);
            SpawnPrefab(mapRoot.transform, "Aps_Balcony_Rail_East", "Assets/Models/Environment/TrimSheet/TrimSheet_Prefabs/Rail.prefab", new Vector3(15.2f, 3.4f, 54f), Quaternion.Euler(0f, 90f, 0f), Vector3.one * 1.25f, stoneMat);
            SpawnWoodCrate(mapRoot.transform, "Aps_Balcony_Crate", "Crate_06", new Vector3(12f, 3.8f, 53f), Vector3.one * 1.1f, _woodDarkMat);

            // ==========================================
            // 6. BOMBSITE A: GRAVEYARD, PIT & TRIPLE BOX (Z = 46 s/d 66, X = 14 s/d 28)
            // ==========================================
            CreateBlock(mapRoot.transform, "SiteA_Paved_Floor", new Vector3(21f, 0.02f, 58f), new Vector3(15f, 0.05f, 18f), groundMat);

            // GRAVEYARD COLONNADE (Barisan pilar batu di sepanjang dinding utara/timur Site A)
            SpawnPrefab(mapRoot.transform, "SiteA_Pillar_1", "Assets/Models/Environment/TrimSheet/TrimSheet_Prefabs/Pillar.prefab", new Vector3(16f, 0f, 65f), Quaternion.identity, Vector3.one * 1.4f, stoneMat);
            SpawnPrefab(mapRoot.transform, "SiteA_Pillar_2", "Assets/Models/Environment/TrimSheet/TrimSheet_Prefabs/PillarDeco.prefab", new Vector3(21f, 0f, 65f), Quaternion.identity, Vector3.one * 1.4f, stoneMat);
            SpawnPrefab(mapRoot.transform, "SiteA_Pillar_3", "Assets/Models/Environment/TrimSheet/TrimSheet_Prefabs/Pillar.prefab", new Vector3(26f, 0f, 65f), Quaternion.identity, Vector3.one * 1.4f, stoneMat);
            SpawnPrefab(mapRoot.transform, "SiteA_Graveyard_Rail_1", "Assets/Models/Environment/TrimSheet/TrimSheet_Prefabs/Rail.prefab", new Vector3(18.5f, 0f, 65f), Quaternion.identity, Vector3.one * 1.3f, stoneMat);
            SpawnPrefab(mapRoot.transform, "SiteA_Graveyard_Rail_2", "Assets/Models/Environment/TrimSheet/TrimSheet_Prefabs/Rail.prefab", new Vector3(23.5f, 0f, 65f), Quaternion.identity, Vector3.one * 1.3f, stoneMat);
            SpawnPrefab(mapRoot.transform, "SiteA_Graveyard_Vase_1", "Assets/Models/Environment/TrimSheet/TrimSheet_Prefabs/Vase.prefab", new Vector3(16f, 2.2f, 65f), Quaternion.identity, Vector3.one * 1.3f, stoneMat);
            SpawnPrefab(mapRoot.transform, "SiteA_Graveyard_Vase_2", "Assets/Models/Environment/TrimSheet/TrimSheet_Prefabs/Vase.prefab", new Vector3(26f, 2.2f, 65f), Quaternion.identity, Vector3.one * 1.3f, stoneMat);

            // PIT (Posisi pertahanan sunken di sudut barat daya Site A)
            CreateBlock(mapRoot.transform, "SiteA_Pit_Wall_N", new Vector3(26f, 0.65f, 44f), new Vector3(4.5f, 1.3f, 0.8f), stoneMat);
            CreateBlock(mapRoot.transform, "SiteA_Pit_Wall_W", new Vector3(23.5f, 0.65f, 41f), new Vector3(0.8f, 1.3f, 6.0f), stoneMat);
            SpawnWoodCrate(mapRoot.transform, "SiteA_Pit_Crate_1", "Crate_02", new Vector3(26f, 0.6f, 42f), Vector3.one * 1.3f, _woodBrightMat);
            SpawnWoodCrate(mapRoot.transform, "SiteA_Pit_Crate_2", "Crate_07", new Vector3(26f, 1.6f, 42f), Vector3.one * 1.1f, _woodDarkMat);

            // TRIPLE BOX (Tumpukan peti kayu di pusat Site A sebagai cover utama saat planting)
            SpawnWoodCrate(mapRoot.transform, "SiteA_Triple_1", "Crate_01", new Vector3(20f, 0.6f, 57f), Vector3.one * 1.4f, _woodBrightMat);
            SpawnWoodCrate(mapRoot.transform, "SiteA_Triple_2", "Crate_03", new Vector3(22f, 0.6f, 57f), Vector3.one * 1.4f, _woodBrightMat);
            SpawnWoodCrate(mapRoot.transform, "SiteA_Triple_3", "Crate_05", new Vector3(21f, 1.7f, 57f), Vector3.one * 1.3f, _woodDarkMat);

            // BOMB PLANT ZONE SITE A
            GameObject pzAFloor = CreateBlock(mapRoot.transform, "PlantZone_A_FloorPlate", new Vector3(21f, 0.03f, 58.5f), new Vector3(8.5f, 0.06f, 7.5f), plantHazardMat);
            PlantZone pzA = pzAFloor.AddComponent<PlantZone>();
            pzA.ConfigureZone("Site A", new Vector3(8.5f, 3.5f, 7.5f));
            CreateCornerHazardBrackets(mapRoot.transform, "SiteA_HazardBracket", new Vector3(21f, 0.06f, 58.5f), new Vector3(8.5f, 0.06f, 7.5f), bollardYellowMat, barrelDarkMat);
            CreateTacticalZoneSign(mapRoot.transform, "SiteA_WallSign", new Vector3(21f, 2.5f, 65.5f), "A", new Color(0.95f, 0.75f, 0.1f));

            // ==========================================
            // 7. CT SPAWN COMMAND OUTPOST & SPEEDWAY (Z = 64 s/d 70, X = -12 s/d 12)
            // ==========================================
            CreateBlock(mapRoot.transform, "CTSpawn_PavedDeck", new Vector3(0f, 0.02f, 66.5f), new Vector3(24f, 0.05f, 7f), groundMat);

            // Pos komando CT & Radio
            CreateBlock(mapRoot.transform, "CT_CommandDesk", new Vector3(0f, 0.6f, 67.5f), new Vector3(3.2f, 1.2f, 1.2f), stoneMat);
            CreateCylinder(mapRoot.transform, "CT_Radio_Antenna", new Vector3(2f, 2.5f, 68f), new Vector3(0.15f, 2.5f, 0.15f), ironMat);
            SpawnWoodCrate(mapRoot.transform, "CT_Supply_1", "Crate_10", new Vector3(-2.5f, 0.6f, 67f), Vector3.one * 1.3f, _woodBrightMat);
            SpawnWoodCrate(mapRoot.transform, "CT_Supply_2", "Crate_12", new Vector3(2.5f, 0.6f, 67f), Vector3.one * 1.3f, _woodDarkMat);

            // Lentera & pohon di jalan CT Speedway
            SpawnPrefab(mapRoot.transform, "CT_Lantern_L", "Assets/Models/Environment/Village/Prefabs/Lantern.prefab", new Vector3(-6f, 0f, 65f), Quaternion.identity, Vector3.one * 1.3f);
            SpawnPrefab(mapRoot.transform, "CT_Lantern_R", "Assets/Models/Environment/Village/Prefabs/Lantern.prefab", new Vector3(6f, 0f, 65f), Quaternion.identity, Vector3.one * 1.3f);
            SpawnPrefab(mapRoot.transform, "CT_PineTree_Back", "Assets/Models/Environment/Village/Prefabs/Pine Tree_1.prefab", new Vector3(0f, 0f, 69f), Quaternion.identity, Vector3.one * 1.4f);

            // Setup Musuh 2 Bot Bersenjata T di T-Spawn
            EnsureEnemyBotsExist();

            // Pindahkan Pemain ke CT Spawn dan Nonaktifkan Objek Kamar Lama
            RepositionPlayerToCTSpawn();
            DeactivateOldRoomObjects();

            Debug.Log("[TacticalArenaBuilder] Stylized Low-Poly de_inferno Arena berhasil dibangun!");
        }

        private GameObject SpawnPrefab(Transform parent, string objName, string assetPath, Vector3 localPos, Quaternion localRot, Vector3 localScale, Material fallbackMat = null)
        {
            GameObject instance = null;
#if UNITY_EDITOR
            GameObject prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (prefab != null)
            {
                if (Application.isPlaying)
                {
                    instance = Instantiate(prefab, parent);
                }
                else
                {
                    instance = (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(prefab, parent);
                }
                instance.name = objName;
                instance.transform.localPosition = localPos;
                instance.transform.localRotation = localRot;
                instance.transform.localScale = localScale;
            }
#endif
            if (instance == null)
            {
                Shader s = _cachedUrpShader != null ? _cachedUrpShader : Shader.Find("Universal Render Pipeline/Lit");
                Material mat = fallbackMat != null ? fallbackMat : new Material(s) { color = wallStuccoColor };
                instance = CreateBlock(parent, objName + "_Fallback", localPos, localScale, mat);
                instance.transform.localRotation = localRot;
            }

            EnsureCollider(instance);
            FixRenderers(instance);
            return instance;
        }

        private GameObject SpawnWoodCrate(Transform parent, string objName, string crateMeshName, Vector3 localPos, Vector3 localScale, Material mat)
        {
            GameObject instance = null;
#if UNITY_EDITOR
            string fbxPath = $"Assets/Models/Props/WoodCrates/Meshes/{crateMeshName}.fbx";
            GameObject fbx = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
            if (fbx != null)
            {
                if (Application.isPlaying) instance = Instantiate(fbx, parent);
                else instance = (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(fbx, parent);

                instance.name = objName;
                instance.transform.localPosition = localPos;
                instance.transform.localRotation = Quaternion.identity;
                instance.transform.localScale = localScale;

                Renderer rend = instance.GetComponentInChildren<Renderer>();
                if (rend != null && mat != null) rend.sharedMaterial = mat;
            }
#endif
            if (instance == null)
            {
                instance = CreateBlock(parent, objName, localPos, localScale, mat != null ? mat : _woodBrightMat);
            }

            EnsureCollider(instance);
            FixRenderers(instance);
            return instance;
        }

        private static void EnsureCollider(GameObject obj)
        {
            if (obj == null) return;
            Collider col = obj.GetComponentInChildren<Collider>();
            if (col == null)
            {
                MeshFilter mf = obj.GetComponentInChildren<MeshFilter>();
                if (mf != null && mf.sharedMesh != null)
                {
                    MeshCollider mc = obj.AddComponent<MeshCollider>();
                    mc.sharedMesh = mf.sharedMesh;
                }
                else
                {
                    Renderer r = obj.GetComponentInChildren<Renderer>();
                    if (r != null)
                    {
                        BoxCollider box = obj.AddComponent<BoxCollider>();
                        box.center = obj.transform.InverseTransformPoint(r.bounds.center);
                        Vector3 sz = obj.transform.InverseTransformVector(r.bounds.size);
                        box.size = new Vector3(Mathf.Abs(sz.x), Mathf.Abs(sz.y), Mathf.Abs(sz.z));
                    }
                }
            }
        }

        public static void FixRenderers(GameObject obj)
        {
            if (obj == null) return;
            if (_cachedUrpShader == null)
            {
                _cachedUrpShader = Shader.Find("Universal Render Pipeline/Lit");
                if (_cachedUrpShader == null) _cachedUrpShader = Shader.Find("Standard");
            }

            Renderer[] rends = obj.GetComponentsInChildren<Renderer>(true);
            foreach (var r in rends)
            {
                if (r == null) continue;
                Material[] mats = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] != null && mats[i].shader != null && (!mats[i].shader.name.StartsWith("Universal Render Pipeline") || mats[i].shader.name.Contains("Error")))
                    {
                        Material newMat = new Material(_cachedUrpShader);
                        if (mats[i].HasProperty("_BaseMap") && mats[i].GetTexture("_BaseMap") != null)
                            newMat.SetTexture("_BaseMap", mats[i].GetTexture("_BaseMap"));
                        else if (mats[i].HasProperty("_MainTex") && mats[i].GetTexture("_MainTex") != null)
                            newMat.SetTexture("_BaseMap", mats[i].GetTexture("_MainTex"));

                        if (mats[i].HasProperty("_BumpMap") && mats[i].GetTexture("_BumpMap") != null)
                            newMat.SetTexture("_BumpMap", mats[i].GetTexture("_BumpMap"));

                        if (mats[i].HasProperty("_BaseColor"))
                            newMat.SetColor("_BaseColor", mats[i].GetColor("_BaseColor"));
                        else if (mats[i].HasProperty("_Color"))
                            newMat.SetColor("_BaseColor", mats[i].GetColor("_Color"));

                        mats[i] = newMat;
                        changed = true;
                    }
                }
                if (changed) r.sharedMaterials = mats;
            }
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

        public static void EnsureEnemyBotsExist(bool forceRespawn = false)
        {
            // Bersihkan dummy cyan, tombol mode, dan objek melayang di void
            CleanupStrayObjects();

            // Setup Bot A (T Attacker pembawa C4 menuju Site A Graveyard & Triple Box)
            List<Vector3> waypointsA = new List<Vector3>
            {
                new Vector3(4f, 0f, 2f),       // T-Spawn Base
                new Vector3(18f, 0f, 10f),     // Gate Lane A
                new Vector3(21f, 0f, 22f),     // Long A Street
                new Vector3(19f, 0f, 32f),     // Long A Connector A area
                new Vector3(20f, 0f, 44f),     // Long A North
                new Vector3(21f, 0f, 52f),     // Site A Entrance
                new Vector3(21f, 0f, 58.5f)    // Site A Plant Zone (Triple Box)
            };
            CreateSingleEnemyBot("Enemy_Bot_Soldier_A", new Vector3(4f, 0f, 2f), waypointsA, hasC4: true, forceRespawn: forceRespawn);

            // Setup Bot B (T Attacker pendukung menuju Banana dan Site B Fountain)
            List<Vector3> waypointsB = new List<Vector3>
            {
                new Vector3(-4f, 0f, 2f),      // T-Spawn Base
                new Vector3(-18f, 0f, 10f),    // Gate Lane B
                new Vector3(-20f, 0f, 22f),    // Banana Choke
                new Vector3(-18f, 0f, 32f),    // Banana Car / Half-Wall
                new Vector3(-17f, 0f, 40f),    // Site B Ruins Entrance
                new Vector3(-19.5f, 0f, 52f)   // Site B Plant Zone (Fountain)
            };
            CreateSingleEnemyBot("Enemy_Bot_Soldier_B", new Vector3(-4f, 0f, 2f), waypointsB, hasC4: false, forceRespawn: forceRespawn);
        }

        private static void CreateSingleEnemyBot(string botName, Vector3 spawnPos, List<Vector3> waypoints, bool hasC4 = false, bool forceRespawn = false)
        {
            GameObject existingBot = GameObject.Find(botName);
            if (existingBot != null)
            {
                if (forceRespawn)
                {
                    existingBot.name = botName + "_Dying_" + System.Guid.NewGuid().ToString("N");
                    if (Application.isPlaying) Destroy(existingBot);
                    else DestroyImmediate(existingBot);
                }
                else
                {
                    if (existingBot.transform.position.y > 0.05f)
                    {
                        Vector3 p = existingBot.transform.position;
                        p.y = 0f;
                        existingBot.transform.position = p;
                    }

                    Transform rightHand = FindBoneRecursive(existingBot.transform, "RightHand");
                    if (rightHand != null)
                    {
                        for (int i = rightHand.childCount - 1; i >= 0; i--)
                        {
                            Transform child = rightHand.GetChild(i);
                            if (child.name.StartsWith("Bot_AK_Model") || child.name.StartsWith("Enemy_Gun_Mesh"))
                            {
                                if (Application.isPlaying) Destroy(child.gameObject);
                                else DestroyImmediate(child.gameObject);
                            }
                        }
                    }

                    EnemyHumanoidAI existingAI = existingBot.GetComponent<EnemyHumanoidAI>();
                    if (existingAI != null)
                    {
                        existingAI.SetPatrolWaypoints(waypoints);
                        existingAI.HasC4 = hasC4;
                    }
                    return;
                }
            }

            GameObject botObj = new GameObject(botName);
            botObj.transform.position = spawnPos;
            botObj.transform.rotation = Quaternion.identity;

            CharacterController cc = botObj.AddComponent<CharacterController>();
            cc.height = 1.8f;
            cc.radius = 0.4f;
            cc.center = new Vector3(0f, 0.9f, 0f);

            Shader urpShader = _cachedUrpShader != null ? _cachedUrpShader : (Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            bool meshSpawned = false;

#if UNITY_EDITOR
            GameObject mixamoFbx = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/Characters/Mixamo/Idle.fbx");
            if (mixamoFbx != null)
            {
                GameObject mixamoMesh = (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(mixamoFbx, botObj.transform);
                mixamoMesh.name = "Bot_MixamoMesh";
                mixamoMesh.transform.localPosition = Vector3.zero;
                mixamoMesh.transform.localRotation = Quaternion.identity;

                RuntimeAnimatorController animCtrl = UnityEditor.AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/Models/Characters/Mixamo/Player_AnimatorController.controller");
                Animator anim = mixamoMesh.GetComponent<Animator>();
                if (anim != null && animCtrl != null)
                {
                    anim.runtimeAnimatorController = animCtrl;
                    anim.applyRootMotion = false;
                }

                Collider[] mixamoCols = mixamoMesh.GetComponentsInChildren<Collider>();
                foreach (var c in mixamoCols) SafeDestroy(c);

                FixRenderers(mixamoMesh);
                meshSpawned = true;
            }
#endif

            if (!meshSpawned)
            {
                GameObject meshObj = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                meshObj.name = "Visual_Capsule";
                meshObj.transform.SetParent(botObj.transform, false);
                meshObj.transform.localPosition = new Vector3(0f, 0.9f, 0f);
                SafeDestroy(meshObj.GetComponent<Collider>());

                Material botMat = new Material(urpShader) { color = new Color(0.25f, 0.32f, 0.22f), name = "Mat_BotArmor" };
                meshObj.GetComponent<MeshRenderer>().sharedMaterial = botMat;

                GameObject visorObj = GameObject.CreatePrimitive(PrimitiveType.Cube);
                visorObj.name = "Visor";
                visorObj.transform.SetParent(meshObj.transform, false);
                visorObj.transform.localPosition = new Vector3(0f, 0.45f, 0.35f);
                visorObj.transform.localScale = new Vector3(0.5f, 0.15f, 0.2f);
                SafeDestroy(visorObj.GetComponent<Collider>());

                Material visorMat = new Material(urpShader) { color = new Color(0.95f, 0.2f, 0.2f), name = "Mat_BotVisor" };
                visorObj.GetComponent<MeshRenderer>().sharedMaterial = visorMat;
            }

            GameObject gunObj = GameObject.CreatePrimitive(PrimitiveType.Cube);
            gunObj.name = "Enemy_Gun_Mesh";
            gunObj.transform.SetParent(botObj.transform, false);
            gunObj.transform.localPosition = new Vector3(0.35f, 1.0f, 0.5f);
            gunObj.transform.localScale = new Vector3(0.1f, 0.15f, 0.7f);
            SafeDestroy(gunObj.GetComponent<Collider>());

            Material gunMat = new Material(urpShader) { color = new Color(0.12f, 0.12f, 0.14f), name = "Mat_BotRifle" };
            gunObj.GetComponent<MeshRenderer>().sharedMaterial = gunMat;

            HumanoidRagdollController ragdoll = botObj.AddComponent<HumanoidRagdollController>();
            ragdoll.InitializeRagdoll();

            EnemyHumanoidAI ai = botObj.AddComponent<EnemyHumanoidAI>();
            ai.SetPatrolWaypoints(waypoints);
            ai.HasC4 = hasC4;

            Debug.Log($"[TacticalArenaBuilder] Bot '{botName}' berhasil di-spawn di {spawnPos}. hasC4: {hasC4}");
        }

        private static Transform FindBoneRecursive(Transform parent, string boneName)
        {
            if (parent.name.Contains(boneName)) return parent;
            for (int i = 0; i < parent.childCount; i++)
            {
                Transform found = FindBoneRecursive(parent.GetChild(i), boneName);
                if (found != null) return found;
            }
            return null;
        }

        private static void SafeDestroy(Object obj)
        {
            if (obj == null) return;
            if (Application.isPlaying) Destroy(obj);
            else DestroyImmediate(obj);
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

            CreateCylinder(group.transform, "Barrel_A", new Vector3(-0.35f, 0.58f, -0.25f), new Vector3(0.85f, 0.58f, 0.85f), barrelMat);
            CreateCylinder(group.transform, "Barrel_B", new Vector3(0.35f, 0.58f, -0.25f), new Vector3(0.85f, 0.58f, 0.85f), secMat);
            CreateCylinder(group.transform, "Barrel_C", new Vector3(0f, 0.58f, 0.35f), new Vector3(0.85f, 0.58f, 0.85f), barrelMat);
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

            Shader urpShader = _cachedUrpShader != null ? _cachedUrpShader : (Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            Material boardMat = new Material(urpShader) { color = new Color(0.12f, 0.14f, 0.16f) };
            Material glowMat = new Material(urpShader) { color = glowColor };

            CreateBlock(signRoot.transform, "Backboard", Vector3.zero, new Vector3(1.8f, 1.2f, 0.12f), boardMat);
            CreateBlock(signRoot.transform, "Border_Top", new Vector3(0f, 0.55f, 0.07f), new Vector3(1.7f, 0.08f, 0.08f), glowMat);
            CreateBlock(signRoot.transform, "Border_Bot", new Vector3(0f, -0.55f, 0.07f), new Vector3(1.7f, 0.08f, 0.08f), glowMat);
            CreateBlock(signRoot.transform, "Letter_Icon", new Vector3(0f, 0f, 0.08f), new Vector3(0.55f, 0.75f, 0.06f), glowMat);
        }
    }
}