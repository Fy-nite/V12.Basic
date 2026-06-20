using System;
using V12.Components;
using V12.Core;
using V12.Core.Core.Interfaces;

namespace V12.Basic.Components
{
    /// <summary>
    /// Physics rigid-body parameters for an element.
    /// Actual physics simulation is handled by the Godot renderer;
    /// this component is purely data that describes how the body should behave.
    /// IsKinematic = true → moved by code, not by forces.
    /// </summary>
    public class RigidBodyComponent : ComponentBase
    {
        private float _mass           = 1f;
        private float _gravityScale   = 1f;
        private float _linearDamping  = 0f;
        private float _angularDamping = 0f;
        private bool  _isKinematic    = false;
        private bool  _canSleep       = true;

        public override string Name        => "RigidBody";
        public override string Description => "Physics rigid-body parameters";

        public float Mass
        {
            get => _mass;
            set { if (Math.Abs(_mass - value) > 0.0001f) { _mass = MathF.Max(0.001f, value); MarkDirty(); } }
        }
        public float GravityScale
        {
            get => _gravityScale;
            set { if (Math.Abs(_gravityScale - value) > 0.0001f) { _gravityScale = value; MarkDirty(); } }
        }
        public float LinearDamping
        {
            get => _linearDamping;
            set { if (Math.Abs(_linearDamping - value) > 0.0001f) { _linearDamping = MathF.Max(0f, value); MarkDirty(); } }
        }
        public float AngularDamping
        {
            get => _angularDamping;
            set { if (Math.Abs(_angularDamping - value) > 0.0001f) { _angularDamping = MathF.Max(0f, value); MarkDirty(); } }
        }
        /// <summary>If true the body is moved by code, not forces.</summary>
        public bool IsKinematic
        {
            get => _isKinematic;
            set { if (_isKinematic != value) { _isKinematic = value; MarkDirty(); } }
        }
        public bool CanSleep
        {
            get => _canSleep;
            set { if (_canSleep != value) { _canSleep = value; MarkDirty(); } }
        }

        public RigidBodyComponent() { }
        public RigidBodyComponent(float mass, float gravityScale = 1f, bool isKinematic = false)
        {
            _mass = mass; _gravityScale = gravityScale; _isKinematic = isKinematic;
        }
        public override IWorldElement BuildUI()
        {
            return new Element();
        }
        public override string ToString() =>
            $"RigidBody(Mass:{Mass:F2} Gravity:{GravityScale:F2} Kinematic:{IsKinematic})";
    }
}
