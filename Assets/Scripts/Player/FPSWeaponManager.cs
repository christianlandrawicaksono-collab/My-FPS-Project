using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using FPSFramework.Combat;

namespace FPSFramework.Player
{
    public enum WeaponSlot
    {
        Primary = 1,   // SMG-K
        Secondary = 2, // Pistol-A
        Grenade = 3,   // Frag Grenade
        C4 = 4         // Dynamite Multi (Plantable)
    }

    /// <summary>
    /// Pengelola Multi-Weapon Inventory & Weapon Switching (4 Slot):
    /// - Slot 1: Primary SMG-K (Otomatis, 30/90 peluru)
    /// - Slot 2: Secondary Pistol-A (Semi-otomatis, 12/36 peluru, 38 dmg)
    /// - Slot 3: Frag Grenade (1 granat gratis tiap ronde, lempar jauh/dekat)
    /// - Slot 4: C4 Dynamite Multi (Dapat ditanam di Plant Zone)
    /// - Input: Tombol 1-4 dan Mouse ScrollWheel.
    /// - Transisi: Animasi holster & equip halus dengan jeda tembak 0.22s.
    /// </summary>
    public class FPSWeaponManager : MonoBehaviour
    {
        public static FPSWeaponManager Instance { get; private set; }

        [Header("Slot Senjata 3D")]
        [SerializeField] private GameObject primaryWeaponObj;
        [SerializeField] private GameObject secondaryWeaponObj;
        [SerializeField] private GameObject grenadeViewmodelObj;
        [SerializeField] private GameObject c4ViewmodelObj;

        [Header("Prefab Proyektil Granat")]
        [SerializeField] private GameObject grenadeProjectilePrefab;

        [Header("Pengaturan Amunisi & Kuota")]
        [SerializeField] private int startingGrenadeCount = 1;
        [SerializeField] private float switchDuration = 0.22f;

        // State Inventory
        public WeaponSlot CurrentSlot { get; private set; } = WeaponSlot.Primary;
        public WeaponSlot ActiveSlot => CurrentSlot;
        public FPSWeapon ActiveGun { get; private set; }
        public int GrenadeCount { get; private set; } = 1;
        public int FragGrenades => GrenadeCount;
        public bool HasC4 { get; set; } = false;
        public bool IsSwitching { get; private set; } = false;

        // Static Events
        public static event System.Action<WeaponSlot> OnWeaponSwitched;
        public static event System.Action<int> OnGrenadeCountChanged;

        private FPSWeapon _primaryGun;
        private FPSWeapon _secondaryGun;
        private Camera _mainCamera;
        private Transform _weaponSwayTransform;
        private Coroutine _switchRoutine;
        private float _nextThrowTime;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }
            Instance = this;

            _mainCamera = Camera.main;
            if (_mainCamera == null) _mainCamera = GetComponentInChildren<Camera>();

            GrenadeCount = startingGrenadeCount;
        }

        private void Start()
        {
            SetupWeaponSlots();
            SelectSlotImmediate(WeaponSlot.Primary);
        }

        private void Update()
        {
            HandleSlotSwitchInput();
            HandleGrenadeInput();
        }

        private void SetupWeaponSlots()
        {
            // Temukan Weapon_Sway Transform
            FPSWeaponSway swayComp = GetComponentInChildren<FPSWeaponSway>();
            if (swayComp != null) _weaponSwayTransform = swayComp.transform;
            else if (_mainCamera != null) _weaponSwayTransform = _mainCamera.transform.Find("Weapon_Sway");
            if (_weaponSwayTransform == null) _weaponSwayTransform = transform;

            // 1. Setup Primary (SMG_K)
            if (primaryWeaponObj == null)
            {
                FPSWeapon existingWeapon = GetComponentInChildren<FPSWeapon>();
                if (existingWeapon != null)
                {
                    primaryWeaponObj = existingWeapon.gameObject;
                    _primaryGun = existingWeapon;
                }
            }
            else
            {
                _primaryGun = primaryWeaponObj.GetComponent<FPSWeapon>();
            }

            // 2. Setup Secondary (Pistol_A)
            if (secondaryWeaponObj == null)
            {
                secondaryWeaponObj = SpawnPistolSecondary();
                if (secondaryWeaponObj != null)
                {
                    _secondaryGun = secondaryWeaponObj.GetComponent<FPSWeapon>();
                }
            }

            // 3. Setup Grenade Viewmodel
            if (grenadeViewmodelObj == null)
            {
                grenadeViewmodelObj = SpawnGrenadeViewmodel();
            }

            // 4. Setup C4 Viewmodel
            if (c4ViewmodelObj == null)
            {
                c4ViewmodelObj = SpawnC4Viewmodel();
            }

            // Perbaiki material seluruh viewmodel ke URP Lit agar tidak berwarna pink
            FixRenderersMaterials(primaryWeaponObj);
            FixRenderersMaterials(secondaryWeaponObj);
            FixRenderersMaterials(grenadeViewmodelObj);
            FixRenderersMaterials(c4ViewmodelObj);
        }

        private GameObject SpawnPistolSecondary()
        {
            GameObject pistolPrefab = null;
#if UNITY_EDITOR
            pistolPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Low Poly Pistol Weapon Pack 1/Prefabs/Weapons/Pistol_A.prefab");
#endif
            if (pistolPrefab == null && _weaponSwayTransform != null)
            {
                Transform foundPistol = _weaponSwayTransform.Find("Pistol_A");
                if (foundPistol != null) return foundPistol.gameObject;
            }

            GameObject pistolObj;
            if (pistolPrefab != null)
            {
                pistolObj = Instantiate(pistolPrefab, _weaponSwayTransform);
                pistolObj.name = "Pistol_A_Secondary";
            }
            else
            {
                // Fallback sederhana jika prefab tidak ditemukan
                pistolObj = GameObject.CreatePrimitive(PrimitiveType.Cube);
                pistolObj.name = "Pistol_Fallback";
                pistolObj.transform.SetParent(_weaponSwayTransform, false);
                pistolObj.transform.localScale = new Vector3(0.08f, 0.14f, 0.28f);
                SafeDestroy(pistolObj.GetComponent<Collider>());
            }

            // Posisi & rotasi ergonomis Pistol di layar
            pistolObj.transform.localPosition = new Vector3(0.20f, -0.22f, 0.44f);
            pistolObj.transform.localRotation = Quaternion.identity;

            // Pasang komponen FPSWeapon untuk pistol
            FPSWeapon pistolWeapon = pistolObj.GetComponent<FPSWeapon>();
            if (pistolWeapon == null)
            {
                pistolWeapon = pistolObj.AddComponent<FPSWeapon>();
            }

            // Konfigurasi statistik Pistol_A:
            // 38 damage, 0.22s fire rate (semi-auto), 12/36 ammo, 1.6s reload, akurasi tinggi
            pistolWeapon.ConfigureWeaponStats(
                name: "PISTOL-A",
                damage: 38f,
                rateOfFire: 0.22f,
                magSize: 12,
                reserveAmmo: 36,
                reloadDur: 1.6f,
                spread: 0.0018f,
                maxSpread: 0.038f,
                pitchKick: 1.8f,
                yawKick: 0.35f,
                kickbackDist: 0.045f,
                kickbackUpDist: 0.02f
            );

            return pistolObj;
        }

        private GameObject SpawnGrenadeViewmodel()
        {
            GameObject nadePrefab = null;
#if UNITY_EDITOR
            nadePrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Cosmic_Retro_Grenades_Pack_1_Demo/Prefabs/Cosmic_Retro_Grenades_Pack_2.prefab");
#endif
            GameObject nadeObj;
            if (nadePrefab != null)
            {
                nadeObj = Instantiate(nadePrefab, _weaponSwayTransform);
                nadeObj.name = "Frag_Grenade_Viewmodel";
            }
            else
            {
                nadeObj = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                nadeObj.name = "Grenade_Fallback_Viewmodel";
                nadeObj.transform.SetParent(_weaponSwayTransform, false);
                nadeObj.transform.localScale = Vector3.one * 0.12f;
                SafeDestroy(nadeObj.GetComponent<Collider>());
            }

            nadeObj.transform.localPosition = new Vector3(0.18f, -0.20f, 0.42f);
            nadeObj.transform.localRotation = Quaternion.Euler(15f, -30f, 0f);
            nadeObj.transform.localScale = Vector3.one * 0.85f;

            // Hapus collider bawaan viewmodel agar tidak mengganggu raycast
            Collider[] cols = nadeObj.GetComponentsInChildren<Collider>();
            foreach (var c in cols) SafeDestroy(c);

            return nadeObj;
        }

        private GameObject SpawnC4Viewmodel()
        {
            GameObject c4Prefab = null;
#if UNITY_EDITOR
            c4Prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Stylized Explosives/Prefabs/Dynamite_Multi.prefab");
            if (c4Prefab == null)
            {
                c4Prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Stylized Explosives/Models/Dynamite_Multi.fbx");
            }
#endif
            GameObject c4Obj;
            if (c4Prefab != null)
            {
                c4Obj = Instantiate(c4Prefab, _weaponSwayTransform);
                c4Obj.name = "C4_Dynamite_Viewmodel";
            }
            else
            {
                c4Obj = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                c4Obj.name = "C4_Fallback_Viewmodel";
                c4Obj.transform.SetParent(_weaponSwayTransform, false);
                c4Obj.transform.localScale = new Vector3(0.2f, 0.08f, 0.2f);
                SafeDestroy(c4Obj.GetComponent<Collider>());
            }

            c4Obj.transform.localPosition = new Vector3(0.16f, -0.22f, 0.40f);
            c4Obj.transform.localRotation = Quaternion.Euler(25f, -20f, 0f);
            c4Obj.transform.localScale = Vector3.one * 0.75f;

            Collider[] cols = c4Obj.GetComponentsInChildren<Collider>();
            foreach (var c in cols) SafeDestroy(c);

            return c4Obj;
        }

        private void HandleSlotSwitchInput()
        {
            if (IsSwitching) return;

            // Tombol angka 1, 2, 3, 4 via New Input System
            if (Keyboard.current != null)
            {
                if (Keyboard.current.digit1Key.wasPressedThisFrame || Keyboard.current.numpad1Key.wasPressedThisFrame) SwitchToSlot(WeaponSlot.Primary);
                else if (Keyboard.current.digit2Key.wasPressedThisFrame || Keyboard.current.numpad2Key.wasPressedThisFrame) SwitchToSlot(WeaponSlot.Secondary);
                else if (Keyboard.current.digit3Key.wasPressedThisFrame || Keyboard.current.numpad3Key.wasPressedThisFrame) SwitchToSlot(WeaponSlot.Grenade);
                else if (Keyboard.current.digit4Key.wasPressedThisFrame || Keyboard.current.numpad4Key.wasPressedThisFrame) SwitchToSlot(WeaponSlot.C4);
            }

            // Mouse Scroll Wheel via New Input System
            if (Mouse.current != null)
            {
                float scroll = Mouse.current.scroll.ReadValue().y;
                if (Mathf.Abs(scroll) > 0.1f)
                {
                    int currentInt = (int)CurrentSlot;
                    if (scroll > 0f)
                    {
                        // Scroll ke atas -> mundur slot
                        currentInt--;
                        if (currentInt < 1) currentInt = 4;
                    }
                    else
                    {
                        // Scroll ke bawah -> maju slot
                        currentInt++;
                        if (currentInt > 4) currentInt = 1;
                    }

                    if (currentInt == 4 && !HasC4) currentInt = scroll > 0f ? 3 : 1;
                    if (currentInt == 3 && GrenadeCount <= 0) currentInt = scroll > 0f ? 2 : 1;

                    SwitchToSlot((WeaponSlot)currentInt);
                }
            }
        }

        public void SwitchToSlot(WeaponSlot targetSlot)
        {
            if (targetSlot == CurrentSlot || IsSwitching) return;

            // Validasi slot granat & C4
            if (targetSlot == WeaponSlot.Grenade && GrenadeCount <= 0)
            {
                return; // Tidak punya granat
            }
            if (targetSlot == WeaponSlot.C4 && !HasC4)
            {
                // Pemain CT tidak membawa C4 secara default (hanya jika mengambil bom)
                return;
            }

            if (_switchRoutine != null) StopCoroutine(_switchRoutine);
            _switchRoutine = StartCoroutine(SwitchWeaponRoutine(targetSlot));
        }

        private IEnumerator SwitchWeaponRoutine(WeaponSlot targetSlot)
        {
            IsSwitching = true;
            GameObject currentObj = GetWeaponObject(CurrentSlot);
            GameObject nextObj = GetWeaponObject(targetSlot);

            // 1. Kunci penembakan senjata aktif
            if (ActiveGun != null) ActiveGun.FiringLocked = true;

            // 2. Animasi Holster (Turunkan senjata lama)
            if (currentObj != null)
            {
                Vector3 origPos = currentObj.transform.localPosition;
                Vector3 downPos = origPos + Vector3.down * 0.14f;
                float t = 0f;
                float halfDur = switchDuration * 0.45f;

                while (t < halfDur)
                {
                    t += Time.deltaTime;
                    currentObj.transform.localPosition = Vector3.Lerp(origPos, downPos, t / halfDur);
                    yield return null;
                }
                currentObj.transform.localPosition = origPos;
                currentObj.SetActive(false);
            }

            // 3. Aktifkan senjata baru
            CurrentSlot = targetSlot;
            UpdateActiveGunReference();

            if (nextObj != null)
            {
                nextObj.SetActive(true);
                Vector3 targetPos = nextObj.transform.localPosition;
                Vector3 startPos = targetPos + Vector3.down * 0.14f;
                nextObj.transform.localPosition = startPos;

                float t = 0f;
                float halfDur = switchDuration * 0.55f;

                while (t < halfDur)
                {
                    t += Time.deltaTime;
                    nextObj.transform.localPosition = Vector3.Lerp(startPos, targetPos, t / halfDur);
                    yield return null;
                }
                nextObj.transform.localPosition = targetPos;
            }

            // 4. Buka kembali kunci penembakan
            if (ActiveGun != null) ActiveGun.FiringLocked = false;

            IsSwitching = false;
            OnWeaponSwitched?.Invoke(CurrentSlot);
        }

        private void SelectSlotImmediate(WeaponSlot slot)
        {
            CurrentSlot = slot;

            if (primaryWeaponObj != null) primaryWeaponObj.SetActive(slot == WeaponSlot.Primary);
            if (secondaryWeaponObj != null) secondaryWeaponObj.SetActive(slot == WeaponSlot.Secondary);
            if (grenadeViewmodelObj != null) grenadeViewmodelObj.SetActive(slot == WeaponSlot.Grenade);
            if (c4ViewmodelObj != null) c4ViewmodelObj.SetActive(slot == WeaponSlot.C4);

            UpdateActiveGunReference();
            if (ActiveGun != null) ActiveGun.FiringLocked = false;

            OnWeaponSwitched?.Invoke(CurrentSlot);
        }

        private void UpdateActiveGunReference()
        {
            if (CurrentSlot == WeaponSlot.Primary) ActiveGun = _primaryGun;
            else if (CurrentSlot == WeaponSlot.Secondary) ActiveGun = _secondaryGun;
            else ActiveGun = null;
        }

        private GameObject GetWeaponObject(WeaponSlot slot)
        {
            switch (slot)
            {
                case WeaponSlot.Primary: return primaryWeaponObj;
                case WeaponSlot.Secondary: return secondaryWeaponObj;
                case WeaponSlot.Grenade: return grenadeViewmodelObj;
                case WeaponSlot.C4: return c4ViewmodelObj;
                default: return primaryWeaponObj;
            }
        }

        private void HandleGrenadeInput()
        {
            if (CurrentSlot != WeaponSlot.Grenade || GrenadeCount <= 0 || IsSwitching) return;

            if (Mouse.current == null) return;

            // Klik Kiri: Lempar Jauh (18 m/s)
            if (Mouse.current.leftButton.wasPressedThisFrame && Time.time >= _nextThrowTime)
            {
                ThrowGrenade(18.0f);
            }
            // Klik Kanan: Lambung Dekat (8 m/s)
            else if (Mouse.current.rightButton.wasPressedThisFrame && Time.time >= _nextThrowTime)
            {
                ThrowGrenade(8.0f);
            }
        }

        private void ThrowGrenade(float speed)
        {
            _nextThrowTime = Time.time + 0.8f;
            GrenadeCount--;
            OnGrenadeCountChanged?.Invoke(GrenadeCount);

            Camera cam = _mainCamera != null ? _mainCamera : Camera.main;
            Vector3 spawnPos = cam.transform.position + cam.transform.forward * 0.5f + cam.transform.right * 0.15f;
            Vector3 throwDir = cam.transform.forward + cam.transform.up * 0.08f;
            throwDir.Normalize();

            // Spawn proyektil granat di dunia
            GameObject projectileObj;
            GameObject nadePrefab = null;
#if UNITY_EDITOR
            nadePrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Cosmic_Retro_Grenades_Pack_1_Demo/Prefabs/Cosmic_Retro_Grenades_Pack_2.prefab");
#endif
            if (nadePrefab != null)
            {
                projectileObj = Instantiate(nadePrefab, spawnPos, cam.transform.rotation);
                projectileObj.name = "Active_Thrown_Grenade";
            }
            else
            {
                projectileObj = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                projectileObj.name = "Active_Thrown_Grenade";
                projectileObj.transform.position = spawnPos;
                projectileObj.transform.localScale = Vector3.one * 0.22f;
            }

            ThrownGrenade nadeComp = projectileObj.GetComponent<ThrownGrenade>();
            if (nadeComp == null)
            {
                nadeComp = projectileObj.AddComponent<ThrownGrenade>();
            }

            Vector3 velocity = throwDir * speed;
            Vector3 angularVelocity = new Vector3(Random.Range(-10f, 10f), Random.Range(-10f, 10f), Random.Range(-10f, 10f));
            nadeComp.InitializeVelocity(velocity, angularVelocity);

            // Sembunyikan viewmodel granat dan otomatis ganti ke senjata utama
            if (grenadeViewmodelObj != null) grenadeViewmodelObj.SetActive(false);
            StartCoroutine(AutoSwitchAfterThrowRoutine());
        }

        private IEnumerator AutoSwitchAfterThrowRoutine()
        {
            yield return new WaitForSeconds(0.35f);
            SwitchToSlot(WeaponSlot.Primary);
        }

        /// <summary>
        /// Mengatur ulang loadout senjata dan amunisi saat awal ronde baru.
        /// </summary>
        public void ResetRoundLoadout()
        {
            GrenadeCount = startingGrenadeCount;
            OnGrenadeCountChanged?.Invoke(GrenadeCount);

            if (_primaryGun != null) _primaryGun.ResetAmmoAndRecoil();
            if (_secondaryGun != null) _secondaryGun.ResetAmmoAndRecoil();

            SelectSlotImmediate(WeaponSlot.Primary);
        }

        public static void FixRenderersMaterials(GameObject root)
        {
            if (root == null) return;
            Shader urpShader = Shader.Find("Universal Render Pipeline/Lit");
            if (urpShader == null) return;

            Renderer[] rends = root.GetComponentsInChildren<Renderer>(true);
            foreach (var r in rends)
            {
                Material[] mats = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    Material m = mats[i];
                    if (m != null && (m.shader == null || m.shader.name == "Standard" || m.shader.name.Contains("Error") || m.shader.name.Contains("InternalErrorShader") || !m.HasProperty("_BaseMap")))
                    {
                        Texture tex = m.HasProperty("_MainTex") ? m.GetTexture("_MainTex") : null;
                        Color col = m.HasProperty("_Color") ? m.color : Color.white;
                        m.shader = urpShader;
                        if (tex != null)
                        {
                            m.SetTexture("_BaseMap", tex);
                            m.SetTexture("_MainTex", tex);
                        }
                        m.SetColor("_BaseColor", col);
                        changed = true;
                    }
                }
                if (changed)
                {
                    r.sharedMaterials = mats;
                }
            }
        }

        private static void SafeDestroy(Object obj)
        {
            if (obj == null) return;
            if (Application.isPlaying) Destroy(obj);
            else DestroyImmediate(obj);
        }
    }
}
