using System;
using System.Numerics;
using V12.Core;
using V12.Core.Core.Interfaces;
using V12.Core.Interfaces.Renderer;

namespace V12.Basic.Building
{
    /// <summary>
    /// Lightweight view over an element's own <see cref="IWorldElement.LocalTransform"/>.
    /// It exists so scripts and builders can keep the old <c>GetOrAddTransform().RY</c>
    /// style API without a TransformComponent storing a second copy of the pose.
    /// Nothing is attached to the element and nothing is clamped.
    /// </summary>
    public readonly struct TransformHandle
    {
        private const float Deg2Rad = MathF.PI / 180f;
        private const float Rad2Deg = 180f / MathF.PI;

        public IWorldElement Owner { get; }

        internal TransformHandle(IWorldElement owner)
        {
            Owner = owner ?? throw new ArgumentNullException(nameof(owner));
        }

        private TRS Pose
        {
            get => Owner.LocalTransform;
            set => Owner.LocalTransform = value;
        }

        public float X
        {
            get => Pose.Position.X;
            set => SetPosition(value, Pose.Position.Y, Pose.Position.Z);
        }

        public float Y
        {
            get => Pose.Position.Y;
            set => SetPosition(Pose.Position.X, value, Pose.Position.Z);
        }

        public float Z
        {
            get => Pose.Position.Z;
            set => SetPosition(Pose.Position.X, Pose.Position.Y, value);
        }

        /// <summary>Pitch in degrees.</summary>
        public float RotationX
        {
            get => ToEulerDegrees(Pose.Rotation).pitch;
            set
            {
                var e = ToEulerDegrees(Pose.Rotation);
                SetEulerDegrees(value, e.yaw, e.roll);
            }
        }

        /// <summary>Yaw in degrees.</summary>
        public float RotationY
        {
            get => ToEulerDegrees(Pose.Rotation).yaw;
            set
            {
                var e = ToEulerDegrees(Pose.Rotation);
                SetEulerDegrees(e.pitch, value, e.roll);
            }
        }

        /// <summary>Roll in degrees.</summary>
        public float RotationZ
        {
            get => ToEulerDegrees(Pose.Rotation).roll;
            set
            {
                var e = ToEulerDegrees(Pose.Rotation);
                SetEulerDegrees(e.pitch, e.yaw, value);
            }
        }

        /// <summary>Legacy alias for yaw in degrees.</summary>
        public float Rotation
        {
            get => RotationY;
            set => RotationY = value;
        }

        /// <summary>Pitch in radians.</summary>
        public float RX
        {
            get => RotationX * Deg2Rad;
            set => RotationX = value * Rad2Deg;
        }

        /// <summary>Yaw in radians.</summary>
        public float RY
        {
            get => RotationY * Deg2Rad;
            set => RotationY = value * Rad2Deg;
        }

        /// <summary>Roll in radians.</summary>
        public float RZ
        {
            get => RotationZ * Deg2Rad;
            set => RotationZ = value * Rad2Deg;
        }

        public Matrix4x4 Transform => ElementPlacement.ElementLocalMatrix(Owner);

        public void SetPosition(float x, float y, float z)
        {
            var pose = Pose;
            pose.Position = new Vector3(x, y, z);
            Pose = pose;
        }

        public void SetRotation(float xDegrees, float yDegrees, float zDegrees)
            => SetEulerDegrees(xDegrees, yDegrees, zDegrees);

        private void SetEulerDegrees(float pitch, float yaw, float roll)
        {
            var pose = Pose;
            pose.Rotation = Quaternion.CreateFromYawPitchRoll(
                yaw * Deg2Rad,
                pitch * Deg2Rad,
                roll * Deg2Rad);
            Pose = pose;
        }

        private static (float pitch, float yaw, float roll) ToEulerDegrees(Quaternion q)
        {
            float sinp = 2 * (q.W * q.X - q.Y * q.Z);
            float pitch = MathF.Abs(sinp) >= 1
                ? MathF.CopySign(MathF.PI / 2, sinp)
                : MathF.Asin(sinp);

            float yaw = MathF.Atan2(
                2 * (q.X * q.Z + q.W * q.Y),
                1 - 2 * (q.X * q.X + q.Y * q.Y));

            float roll = MathF.Atan2(
                2 * (q.X * q.Y + q.W * q.Z),
                1 - 2 * (q.X * q.X + q.Z * q.Z));

            return (pitch * Rad2Deg, yaw * Rad2Deg, roll * Rad2Deg);
        }

        public override string ToString()
            => $"TransformHandle(X:{X:F2}, Y:{Y:F2}, Z:{Z:F2}, Pitch:{RotationX:F2}, Yaw:{RotationY:F2}, Roll:{RotationZ:F2})";
    }
}
