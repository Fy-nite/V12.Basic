using System;
using V12.Core;
using V12.Core.Core.Interfaces;
using V12.Core.Interfaces.Renderer;
using V12.Core.UI;
using System.Numerics;
using V12.Components;

namespace V12.Basic.Components
{
    public enum CameraProjection { Perspective, Orthographic }

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

        public float RotX { get; set; }
        public float RotY { get; set; }
        public float RotZ { get; set; }

        public CameraProjection Projection
        {
            get => _projection;
            set { if (_projection != value) { _projection = value; MarkDirty(); } }
        }
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
        public float OrthoSize
        {
            get => _orthoSize;
            set { if (Math.Abs(_orthoSize - value) > 0.001f) { _orthoSize = MathF.Max(0.01f, value); MarkDirty(); } }
        }
        public bool IsCurrent
        {
            get => _isCurrent;
            set { if (_isCurrent != value) { _isCurrent = value; MarkDirty(); } }
        }

        public float FieldOfView => Fov;
        public float AspectRatio => 16f / 9f;
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
                    var lt = Owner.LocalTransform;
                    return Matrix4x4.CreateScale(lt.Scale)
                         * Matrix4x4.CreateFromQuaternion(lt.Rotation)
                         * Matrix4x4.CreateTranslation(lt.Position);
                }
                return Matrix4x4.Identity;
            }
        }

        public bool IsWorldLocked => true;

        public Matrix4x4 WorldTransform => Transform;

        public CameraComponent() { }
        public CameraComponent(float fov, float nearClip = 0.05f, float farClip = 1000f, bool isCurrent = false)
        {
            _fov = fov; _nearClip = nearClip; _farClip = farClip; _isCurrent = isCurrent;
        }

        public override void OnAttach(IWorldElement worldElement)
        {
            base.OnAttach(worldElement);
            var lt = worldElement.LocalTransform;
            if (lt.Position == Vector3.Zero && lt.Rotation == Quaternion.Identity)
            {
                worldElement.LocalTransform = new TRS
                {
                    Position = new Vector3(0, 1.7f, 0),
                    Rotation = Quaternion.CreateFromYawPitchRoll(RotY, RotX, RotZ),
                    Scale = Vector3.One
                };
            }
        }

        public override IWorldElement BuildUI()
        {
            return new Element();
        }

        /// <summary>Generate editable camera fields for the inspector.</summary>
        public override void BuildInspector(IInspector inspector)
        {
            inspector.Section("Camera");
            inspector.Enum("Projection", () => Projection, v => Projection = v);
            inspector.Float("FOV", () => Fov, v => Fov = v);
            inspector.Float("Near Clip", () => NearClip, v => NearClip = v);
            inspector.Float("Far Clip", () => FarClip, v => FarClip = v);
            inspector.Float("Ortho Size", () => OrthoSize, v => OrthoSize = v);
            inspector.Bool("Is Current", () => IsCurrent, v => IsCurrent = v);
        }

        public override string ToString() =>
            $"Camera(Proj:{Projection} FOV:{Fov:F1} Near:{NearClip:F3} Far:{FarClip:F1} Current:{IsCurrent})";
    }
}
