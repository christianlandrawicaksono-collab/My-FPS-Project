using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace FPSFramework.Player
{
    /// <summary>
    /// Sistem Weapon Overlay Camera (URP Camera Stacking) ala CS2 & Valorant:
    /// 1. Anti-Clipping 100%:
    ///    - Memisahkan render dunia (Base Camera) dan render senjata (Overlay Camera).
    ///    - Senjata digambar pada layer 'Viewmodel' dengan depth buffer independen sehingga tidak pernah termakan dinding.
    /// 2. Viewmodel FOV Independen:
    ///    - Memungkinkan FOV senjata diatur terpisah (default 68 derajat) agar proporsi visual senjata ergonomis.
    /// 3. Zero-Interference:
    ///    - Tidak mengganggu raycast penembakan, arah peluru tetap dari Camera.main ke crosshair.
    ///    - Menghilangkan AudioListener duplikat secara otomatis.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class FPSWeaponOverlayCamera : MonoBehaviour
    {
        [Header("Pengaturan Tampilan Senjata (Viewmodel)")]
        [Tooltip("Field of View khusus senjata (Standar CS / Valorant = 65f - 85f).")]
        [SerializeField] private float viewmodelFOV = 80f;

        [Tooltip("Jika dicentang, FOV senjata akan mengikuti FOV kamera utama.")]
        [SerializeField] private bool matchMainCameraFOV = true;

        [Tooltip("Nama layer khusus untuk senjata dan viewmodel.")]
        [SerializeField] private string viewmodelLayerName = "Viewmodel";

        private Camera _baseCamera;
        private Camera _overlayCamera;
        private UniversalAdditionalCameraData _baseCameraData;
        private UniversalAdditionalCameraData _overlayCameraData;
        private int _viewmodelLayerIndex = -1;

        public Camera OverlayCamera => _overlayCamera;
        public float ViewmodelFOV => viewmodelFOV;

        private void Awake()
        {
            InitializeOverlaySystem();
        }

        private void Start()
        {
            // Pastikan sinkronisasi kamera terpasang dengan benar pada awal scene
            EnsureCameraStackRegistered();
        }

        private void LateUpdate()
        {
            if (_overlayCamera != null && _baseCamera != null)
            {
                // Sinkronisasi FOV secara real-time jika opsi match dicentang
                if (matchMainCameraFOV)
                {
                    _overlayCamera.fieldOfView = _baseCamera.fieldOfView;
                }
            }
        }

        /// <summary>
        /// Menginisialisasi sistem URP Camera Stacking dan culling mask.
        /// </summary>
        public void InitializeOverlaySystem()
        {
            _baseCamera = GetComponent<Camera>();
            if (_baseCamera == null) _baseCamera = Camera.main;
            if (_baseCamera == null) return;

            _viewmodelLayerIndex = LayerMask.NameToLayer(viewmodelLayerName);
            if (_viewmodelLayerIndex == -1)
            {
                _viewmodelLayerIndex = 6; // Fallback ke layer indeks 6
            }

            // 1. Konfigurasi Base Camera: Kecualikan layer Viewmodel dari render dunia
            _baseCamera.cullingMask &= ~(1 << _viewmodelLayerIndex);

            _baseCameraData = _baseCamera.GetUniversalAdditionalCameraData();
            if (_baseCameraData != null)
            {
                _baseCameraData.renderType = CameraRenderType.Base;
            }

            // 2. Buat atau temukan child GameObject untuk Overlay Camera
            Transform overlayTransform = _baseCamera.transform.Find("Weapon_Overlay_Camera");
            GameObject overlayObj;
            if (overlayTransform != null)
            {
                overlayObj = overlayTransform.gameObject;
            }
            else
            {
                overlayObj = new GameObject("Weapon_Overlay_Camera");
                overlayObj.transform.SetParent(_baseCamera.transform, false);
            }

            overlayObj.transform.localPosition = Vector3.zero;
            overlayObj.transform.localRotation = Quaternion.identity;
            overlayObj.transform.localScale = Vector3.one;

            // 3. Konfigurasi komponen Camera pada Overlay Camera
            _overlayCamera = overlayObj.GetComponent<Camera>();
            if (_overlayCamera == null)
            {
                _overlayCamera = overlayObj.AddComponent<Camera>();
            }

            _overlayCamera.CopyFrom(_baseCamera);
            _overlayCamera.cullingMask = (1 << _viewmodelLayerIndex); // Hanya gambar layer Viewmodel
            _overlayCamera.nearClipPlane = 0.01f; // Near clip sangat tipis agar senjata tidak terpotong
            _overlayCamera.farClipPlane = 40f;
            _overlayCamera.fieldOfView = matchMainCameraFOV ? _baseCamera.fieldOfView : viewmodelFOV;

            // Hapus AudioListener jika ada agar tidak terjadi error peringatan duplikasi audio
            AudioListener al = overlayObj.GetComponent<AudioListener>();
            if (al != null)
            {
                if (Application.isPlaying) Destroy(al);
                else DestroyImmediate(al);
            }

            // 4. Konfigurasi URP Additional Camera Data pada Overlay Camera
            _overlayCameraData = _overlayCamera.GetUniversalAdditionalCameraData();
            if (_overlayCameraData != null)
            {
                _overlayCameraData.renderType = CameraRenderType.Overlay;
                ApplyClearDepth(_overlayCameraData);
            }

            _overlayCamera.clearFlags = CameraClearFlags.Depth;

            EnsureCameraStackRegistered();
        }

        private void EnsureCameraStackRegistered()
        {
            if (_baseCameraData != null && _overlayCamera != null)
            {
                if (!_baseCameraData.cameraStack.Contains(_overlayCamera))
                {
                    _baseCameraData.cameraStack.Add(_overlayCamera);
                }
            }

            if (_overlayCameraData != null)
            {
                ApplyClearDepth(_overlayCameraData);
            }
        }

        private void ApplyClearDepth(UniversalAdditionalCameraData camData)
        {
            if (camData == null) return;
            try
            {
                // Mengaktifkan m_ClearDepth pada Overlay Camera URP agar depth buffer dunia di-clear
                // Ini mencegah senjata tertutup/termakan oleh depth dinding saat berada dekat permukaan objek
                var field = typeof(UniversalAdditionalCameraData).GetField("m_ClearDepth", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (field != null)
                {
                    field.SetValue(camData, true);
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[FPSWeaponOverlayCamera] Gagal mengaktifkan clearDepth via reflection: {ex.Message}");
            }
        }

        /// <summary>
        /// Mengatur Field of View khusus senjata secara dinamis.
        /// </summary>
        public void SetViewmodelFOV(float fov)
        {
            viewmodelFOV = fov;
            if (_overlayCamera != null && !matchMainCameraFOV)
            {
                _overlayCamera.fieldOfView = fov;
            }
        }

        /// <summary>
        /// Menyelaraskan FOV kamera utama dan overlay viewmodel saat diubah dari menu pengaturan.
        /// </summary>
        public void SyncCameraFOV(float fov)
        {
            if (_baseCamera != null)
            {
                _baseCamera.fieldOfView = fov;
            }
            if (_overlayCamera != null && matchMainCameraFOV)
            {
                _overlayCamera.fieldOfView = fov;
            }
        }
    }
}
