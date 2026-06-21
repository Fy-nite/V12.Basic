using System;
using V12.Core;
using V12.Core.Core.Interfaces;
using V12.Core.Interfaces.Renderer;
using System.Numerics;
using V12.Components;

namespace V12.Basic.Components
{
    public enum CameraProjection { Perspective, Orthographic }

    /// <summary>
    /// Defines a camera view attached to an element.
    /// IsCurrent = true makes this the active scene camera when synced to Godot.
    /// </summary>
    public class CameraComponent : ComponentBase, ICameraRenderable
    {
        private CameraProjection _projection  = CameraProjection.Perspective;
        private float            _fov         = 75f;
        private float            _nearClip    = 0.05f;
        private float            _farClip     = 1000f;
        private float            _orthoSize   = 5f;
        private bool             _isCurrent   = false;

        public override string Name        => "Camera";
        public override string Description => "Scene camera";

        // Properties mapped from XML attributes to capture camera rotation
        public float RotX { get; set; }
        public float RotY { get; set; }
        public float RotZ { get; set; }

        private TransformComponent _createdTransform;

        public CameraProjection Projection
        {
            get => _projection;
            set { if (_projection != value) { _projection = value; MarkDirty(); } }
        }
        /// <summary>Vertical field-of-view in degrees (perspective mode only).</summary>
        public float Fov
        {
            get => _fov;
            set { if (Math.Abs(_fov - value) > 0.001f) { _fov = Math.Clamp(value, 1f, 179f); MarkDirty(); } }
        }
        public float NearClip
        {
            get => _nearClip;
            set { if (Math.Abs(_nearClip - value) > 0.0001f) { _nearClip = MathF.Max(0.001f, value); MarkDirty(); } }
        }
        public float FarClip
        {
            get => _farClip;
            set { if (Math.Abs(_farClip - value) > 0.001f) { _farClip = MathF.Max(_nearClip + 0.1f, value); MarkDirty(); } }
        }
        /// <summary>Half-size in world units for orthographic projection.</summary>
        public float OrthoSize
        {
            get => _orthoSize;
            set { if (Math.Abs(_orthoSize - value) > 0.001f) { _orthoSize = MathF.Max(0.01f, value); MarkDirty(); } }
        }
        /// <summary>If true, this camera becomes the active camera when the element enters the scene.</summary>
        public bool IsCurrent
        {
            get => _isCurrent;
            set { if (_isCurrent != value) { _isCurrent = value; MarkDirty(); } }
        }

        // ── ICameraRenderable & ITransformRenderable ──────────────────────────────
        public float FieldOfView => Fov;
        public float AspectRatio => 16f / 9f; // default aspect ratio
        float ICameraRenderable.NearClip => NearClip;
        float ICameraRenderable.FarClip => FarClip;
        bool ICameraRenderable.IsCurrent => IsCurrent;

        public RenderType RenderType => RenderType.Custom;

        public Matrix4x4 Transform
        {
            get
            {
                if (Owner != null)
                {
                    var t = Owner.GetComponent<TransformComponent>();
                    var s = Owner.GetComponent<ScaleComponent>();

                    Matrix4x4 scale = s != null ? Matrix4x4.CreateScale(s.ScaleX, s.ScaleY, s.ScaleZ) : Matrix4x4.Identity;

                    if (t != null)
                    {
                        return scale
                             * Matrix4x4.CreateFromYawPitchRoll(t.RY, t.RX, t.RZ)
                             * Matrix4x4.CreateTranslation(t.X, t.Y, t.Z);
                    }
                }
                return Matrix4x4.Identity;
            }
        }

        public bool IsWorldLocked => true;

        public Matrix4x4 WorldTransform => Transform; // for ISpatial fallback

        // ──────────────────────────────────────────────────────────────────────────

        public CameraComponent() { }
        public CameraComponent(float fov, float nearClip = 0.05f, float farClip = 1000f, bool isCurrent = false)
        {
            _fov = fov; _nearClip = nearClip; _farClip = farClip; _isCurrent = isCurrent;
        }

        public override void OnAttach(IWorldElement worldElement)
        {
            base.OnAttach(worldElement);

            var t = worldElement.GetComponent<TransformComponent>();
            if (t == null)
            {
                t = new TransformComponent
                {
                    X = 0,
                    Y = 1.7f,
                    Z = 0,
                    RotationX = RotX,
                    RotationY = RotY,
                    RotationZ = RotZ
                };
                worldElement.AddComponent(t);
                _createdTransform = t;
            }
            else
            {
                if (RotX != 0 || RotY != 0 || RotZ != 0)
                {
                    t.RotationX = RotX;
                    t.RotationY = RotY;
                    t.RotationZ = RotZ;
                }
            }

            var loco = worldElement.GetComponent<LocomotionComponent>();
            if (loco == null)
            {
                loco = new LocomotionComponent
                {
                    Active = true,
                    MoveSpeed = 4f,
                    LookSensitivity = 1.2f
                };
                worldElement.AddComponent(loco);
                //_createdLocomotion = loco;
            }
        }

        public override void OnDetach(IWorldElement worldElement)
        {
            if (_createdTransform != null)
            {
                worldElement.RemoveComponent(_createdTransform);
                _createdTransform = null;
            }
            //if (_createdLocomotion != null)
            //{
            //    worldElement.RemoveComponent(_createdLocomotion);
            //    _createdLocomotion = null;
            //}
            base.OnDetach(worldElement);
        }

        public override IWorldElement BuildUI()
        {
            return new Element();
        }
        public override string ToString() =>
            $"Camera(Proj:{Projection} FOV:{Fov:F1} Near:{NearClip:F3} Far:{FarClip:F1} Current:{IsCurrent})";
    }
}
