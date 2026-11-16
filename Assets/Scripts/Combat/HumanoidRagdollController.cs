using System;
using System.Collections.Generic;
using UnityEngine;

namespace FPSFramework.Combat
{
    /// <summary>
    /// Mengelola sistem fisika Ragdoll penuh (Full Physics Ragdoll) untuk musuh humanoid.
    /// Mengonfigurasi Rigidbody, Collider, dan CharacterJoint pada tulang Mixamo secara otomatis,
    /// serta menangani momentum peluru, ledakan granat/C4, dan seamless round reset.
    /// </summary>
    public class HumanoidRagdollController : MonoBehaviour
    {
        [System.Serializable]
        private class BoneTransformRecord
        {
            public Transform Transform;
            public Vector3 LocalPosition;
            public Quaternion LocalRotation;
            public Rigidbody Rigidbody;
            public Collider Collider;
            public CharacterJoint Joint;
        }

        [Header("Konfigurasi Ragdoll")]
        [SerializeField] private float totalMass = 75f;
        [SerializeField] private float bulletImpulseMultiplier = 14f;
        [SerializeField] private float headshotImpulseMultiplier = 22f;

        private Animator _animator;
        private CharacterController _charController;
        private readonly List<BoneTransformRecord> _boneRecords = new List<BoneTransformRecord>();
        private Rigidbody _rootHipsRb;
        private bool _isRagdollInitialized = false;

        public bool IsRagdollActive { get; private set; } = false;

        private void Awake()
        {
            InitializeRagdoll();
        }

        /// <summary>
        /// Menginisialisasi seluruh tulang humanoid dan menyusun komponen fisika.
        /// </summary>
        public void InitializeRagdoll()
        {
            if (_isRagdollInitialized) return;

            _animator = GetComponentInChildren<Animator>();
            _charController = GetComponent<CharacterController>();

            Transform hips = FindBoneRecursive(transform, "Hips");
            if (hips != null)
            {
                SetupMixamoHumanoidRagdoll(hips);
            }
            else
            {
                SetupFallbackRagdoll();
            }

            // Abaikan tabrakan internal antara CharacterController dengan semua collider tulang
            if (_charController != null)
            {
                foreach (var record in _boneRecords)
                {
                    if (record.Collider != null)
                    {
                        Physics.IgnoreCollision(_charController, record.Collider, true);
                    }
                }
            }

            // Default: Nonaktifkan ragdoll saat bot hidup
            SetRagdollState(false);
            _isRagdollInitialized = true;
        }

        private void SetupMixamoHumanoidRagdoll(Transform hips)
        {
            _boneRecords.Clear();

            // 1. Temukan seluruh tulang utama Mixamo
            Transform spine = FindBoneRecursive(hips, "Spine1") ?? FindBoneRecursive(hips, "Spine");
            Transform head = FindBoneRecursive(hips, "Head");

            Transform leftArm = FindBoneRecursive(hips, "LeftArm");
            Transform leftForeArm = FindBoneRecursive(hips, "LeftForeArm");
            Transform rightArm = FindBoneRecursive(hips, "RightArm");
            Transform rightForeArm = FindBoneRecursive(hips, "RightForeArm");

            Transform leftUpLeg = FindBoneRecursive(hips, "LeftUpLeg");
            Transform leftLeg = FindBoneRecursive(hips, "LeftLeg");
            Transform rightUpLeg = FindBoneRecursive(hips, "RightUpLeg");
            Transform rightLeg = FindBoneRecursive(hips, "RightLeg");

            // 2. Setup masing-masing bagian tubuh dengan collider, joint, dan massa
            _rootHipsRb = SetupBone(hips, null, BoneType.Pelvis, totalMass * 0.28f);

            Rigidbody spineRb = SetupBone(spine, _rootHipsRb, BoneType.Spine, totalMass * 0.26f);
            Rigidbody headRb = SetupBone(head, spineRb != null ? spineRb : _rootHipsRb, BoneType.Head, totalMass * 0.08f);

            Rigidbody lArmRb = SetupBone(leftArm, spineRb != null ? spineRb : _rootHipsRb, BoneType.Limb, totalMass * 0.045f);
            SetupBone(leftForeArm, lArmRb, BoneType.Limb, totalMass * 0.035f);

            Rigidbody rArmRb = SetupBone(rightArm, spineRb != null ? spineRb : _rootHipsRb, BoneType.Limb, totalMass * 0.045f);
            SetupBone(rightForeArm, rArmRb, BoneType.Limb, totalMass * 0.035f);

            Rigidbody lUpLegRb = SetupBone(leftUpLeg, _rootHipsRb, BoneType.Limb, totalMass * 0.11f);
            SetupBone(leftLeg, lUpLegRb, BoneType.Limb, totalMass * 0.065f);

            Rigidbody rUpLegRb = SetupBone(rightUpLeg, _rootHipsRb, BoneType.Limb, totalMass * 0.11f);
            SetupBone(rightLeg, rUpLegRb, BoneType.Limb, totalMass * 0.065f);
        }

        private enum BoneType { Pelvis, Spine, Head, Limb }

        private Rigidbody SetupBone(Transform bone, Rigidbody connectedParentRb, BoneType type, float mass)
        {
            if (bone == null) return null;

            // Simpan snapshot transform awal untuk round reset
            BoneTransformRecord record = new BoneTransformRecord
            {
                Transform = bone,
                LocalPosition = bone.localPosition,
                LocalRotation = bone.localRotation
            };

            // Rigidbody
            Rigidbody rb = bone.GetComponent<Rigidbody>();
            if (rb == null) rb = bone.gameObject.AddComponent<Rigidbody>();
            rb.mass = mass;
            rb.linearDamping = 0.4f;
            rb.angularDamping = 1.0f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.Discrete;
            record.Rigidbody = rb;

            // Collider
            Collider col = bone.GetComponent<Collider>();
            if (col == null)
            {
                if (type == BoneType.Head)
                {
                    SphereCollider sphere = bone.gameObject.AddComponent<SphereCollider>();
                    sphere.radius = 0.14f;
                    sphere.center = new Vector3(0f, 0.08f, 0f);
                    col = sphere;
                }
                else if (type == BoneType.Pelvis)
                {
                    BoxCollider box = bone.gameObject.AddComponent<BoxCollider>();
                    box.size = new Vector3(0.32f, 0.22f, 0.26f);
                    col = box;
                }
                else if (type == BoneType.Spine)
                {
                    CapsuleCollider cap = bone.gameObject.AddComponent<CapsuleCollider>();
                    cap.radius = 0.18f;
                    cap.height = 0.38f;
                    cap.direction = 1; // Y-Axis
                    col = cap;
                }
                else // Limb
                {
                    CapsuleCollider cap = bone.gameObject.AddComponent<CapsuleCollider>();
                    cap.radius = 0.075f;
                    cap.height = 0.32f;
                    cap.direction = 0; // X or Z depending on bone orientation
                    col = cap;
                }
            }
            record.Collider = col;

            // Pasang FPSHitbox pada setiap bagian tulang agar tembakan terdeteksi dengan multiplier presisi
            FPSHitbox hitbox = bone.GetComponent<FPSHitbox>();
            if (hitbox == null) hitbox = bone.gameObject.AddComponent<FPSHitbox>();
            if (type == BoneType.Head)
            {
                hitbox.SetHitbox(HitboxType.Head, 4.0f);
            }
            else if (type == BoneType.Pelvis || type == BoneType.Spine)
            {
                hitbox.SetHitbox(HitboxType.Body, 1.0f);
            }
            else // Limb
            {
                hitbox.SetHitbox(HitboxType.Limb, 0.85f);
            }

            // CharacterJoint (kecuali Pelvis/Hips yang merupakan root)
            if (connectedParentRb != null)
            {
                CharacterJoint joint = bone.GetComponent<CharacterJoint>();
                if (joint == null) joint = bone.gameObject.AddComponent<CharacterJoint>();

                joint.connectedBody = connectedParentRb;
                joint.enableProjection = true; // Mencegah peregangan sendi yang tidak wajar
                joint.projectionDistance = 0.02f;
                joint.projectionAngle = 8f;

                // Batas gerakan sendi manusia
                joint.lowTwistLimit = new SoftJointLimit { limit = -25f };
                joint.highTwistLimit = new SoftJointLimit { limit = 25f };
                joint.swing1Limit = new SoftJointLimit { limit = 35f };
                joint.swing2Limit = new SoftJointLimit { limit = 35f };

                record.Joint = joint;

                // Abaikan tabrakan antar tulang yang saling terhubung langsung
                if (connectedParentRb.GetComponent<Collider>() != null && col != null)
                {
                    Physics.IgnoreCollision(col, connectedParentRb.GetComponent<Collider>(), true);
                }
            }

            _boneRecords.Add(record);
            return rb;
        }

        private void SetupFallbackRagdoll()
        {
            _boneRecords.Clear();
            Rigidbody rb = GetComponent<Rigidbody>();
            if (rb == null) rb = gameObject.AddComponent<Rigidbody>();
            rb.mass = totalMass;
            rb.linearDamping = 0.5f;

            _boneRecords.Add(new BoneTransformRecord
            {
                Transform = transform,
                LocalPosition = transform.localPosition,
                LocalRotation = transform.localRotation,
                Rigidbody = rb,
                Collider = GetComponent<Collider>()
            });
            _rootHipsRb = rb;
        }

        /// <summary>
        /// Mengatur mode fisika: Nonaktif saat hidup, Aktif penuh saat mati.
        /// </summary>
        public void SetRagdollState(bool active)
        {
            IsRagdollActive = active;

            if (_animator != null)
            {
                _animator.enabled = !active;
            }

            if (_charController != null)
            {
                _charController.enabled = !active;
            }

            foreach (var record in _boneRecords)
            {
                if (record.Rigidbody != null)
                {
                    if (active)
                    {
                        record.Rigidbody.isKinematic = false;
                        record.Rigidbody.linearVelocity = Vector3.zero;
                        record.Rigidbody.angularVelocity = Vector3.zero;
                        record.Rigidbody.useGravity = true;
                        record.Rigidbody.detectCollisions = true;
                    }
                    else
                    {
                        if (!record.Rigidbody.isKinematic)
                        {
                            record.Rigidbody.linearVelocity = Vector3.zero;
                            record.Rigidbody.angularVelocity = Vector3.zero;
                        }
                        record.Rigidbody.isKinematic = true;
                        record.Rigidbody.useGravity = false;
                        record.Rigidbody.detectCollisions = true;
                    }
                }
            }
        }

        /// <summary>
        /// Mengaktifkan ragdoll dan menerapkan impuls gaya tembakan peluru pada tubuh.
        /// </summary>
        public void TriggerRagdoll(Vector3 hitPoint, Vector3 hitDirection, float customForce = 0f, bool isHeadshot = false)
        {
            SetRagdollState(true);

            float forceToApply = customForce > 0f ? customForce : (isHeadshot ? headshotImpulseMultiplier : bulletImpulseMultiplier);
            Vector3 impulse = hitDirection.normalized * forceToApply;

            // Cari tulang terdekat dengan titik tembakan
            BoneTransformRecord targetBone = isHeadshot ? FindRecordByName("Head") : FindClosestBone(hitPoint);
            if (targetBone == null) targetBone = FindRecordByName("Hips") ?? (_boneRecords.Count > 0 ? _boneRecords[0] : null);

            if (targetBone != null && targetBone.Rigidbody != null)
            {
                targetBone.Rigidbody.AddForceAtPosition(impulse, hitPoint, ForceMode.Impulse);

                if (isHeadshot)
                {
                    // Tambahkan sentakan putaran kepala dramatis saat headshot
                    Vector3 torque = UnityEngine.Random.insideUnitSphere * (forceToApply * 1.5f);
                    targetBone.Rigidbody.AddTorque(torque, ForceMode.Impulse);
                }
            }
            else if (_rootHipsRb != null)
            {
                _rootHipsRb.AddForce(impulse, ForceMode.Impulse);
            }
        }

        /// <summary>
        /// Menerapkan daya ledak (Explosion Force) ke seluruh sendi ragdoll (Granat & C4).
        /// </summary>
        public void ApplyExplosion(Vector3 blastOrigin, float force, float radius, float upwardsModifier = 0.8f)
        {
            if (!IsRagdollActive)
            {
                SetRagdollState(true);
            }

            foreach (var record in _boneRecords)
            {
                if (record.Rigidbody != null)
                {
                    record.Rigidbody.AddExplosionForce(force, blastOrigin, radius, upwardsModifier, ForceMode.Impulse);
                }
            }
        }

        /// <summary>
        /// Mereset pose tulang dan mengembalikan status bot untuk ronde baru.
        /// </summary>
        public void ResetRagdoll()
        {
            // Pulihkan posisi & rotasi awal setiap sendi tulang
            foreach (var record in _boneRecords)
            {
                if (record.Transform != null)
                {
                    record.Transform.localPosition = record.LocalPosition;
                    record.Transform.localRotation = record.LocalRotation;
                }
            }

            SetRagdollState(false);

            // Rebind animator untuk mengunci kembali pose tegak
            if (_animator != null)
            {
                _animator.enabled = true;
                _animator.Rebind();
                _animator.Play("Locomotion_Standing", 0, 0f);
                _animator.Update(0f);
            }

            if (_charController != null)
            {
                _charController.enabled = true;
            }
        }

        private BoneTransformRecord FindClosestBone(Vector3 point)
        {
            BoneTransformRecord closest = null;
            float minDistSq = float.MaxValue;

            foreach (var record in _boneRecords)
            {
                if (record.Transform == null) continue;
                float distSq = (record.Transform.position - point).sqrMagnitude;
                if (distSq < minDistSq)
                {
                    minDistSq = distSq;
                    closest = record;
                }
            }

            return closest;
        }

        private BoneTransformRecord FindRecordByName(string partName)
        {
            foreach (var record in _boneRecords)
            {
                if (record.Transform != null && record.Transform.name.IndexOf(partName, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return record;
                }
            }
            return null;
        }

        private static Transform FindBoneRecursive(Transform parent, string nameSub)
        {
            if (parent == null) return null;
            if (parent.name.IndexOf(nameSub, StringComparison.OrdinalIgnoreCase) >= 0)
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
    }
}
