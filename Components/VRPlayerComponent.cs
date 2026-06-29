using System;
using V12.Components;
using V12.Core;
using V12.Core.Core.Interfaces;

namespace V12.Basic.Components
{
    public class VRPlayerComponent : ComponentBase, IPlayerControlComponent
    {
        private bool  _isLocalControlled    = true;
        private float _moveSpeed            = 4f;
        private float _sprintMultiplier     = 1.9f;
        private float _jumpStrength         = 6f;
        private float _lookSensitivity      = 1.2f;
        private bool  _canJump              = true;
        private bool  _enableHandTracking   = true;
        private float _vrMoveSpeed          = 3f;
        private bool  _vrSmoothLocomotion   = true;

        public override string Name => "VRPlayer";
        public override string Description => "VR player settings";

        public bool IsLocalControlled { get => _isLocalControlled; set { if (_isLocalControlled != value) { _isLocalControlled = value; MarkDirty(); } } }
        public float MoveSpeed { get => _moveSpeed; set { if (Math.Abs(_moveSpeed - value) > 0.0001f) { _moveSpeed = MathF.Max(0f, value); MarkDirty(); } } }
        public float SprintMultiplier { get => _sprintMultiplier; set { if (Math.Abs(_sprintMultiplier - value) > 0.0001f) { _sprintMultiplier = MathF.Max(1f, value); MarkDirty(); } } }
        public float JumpStrength { get => _jumpStrength; set { if (Math.Abs(_jumpStrength - value) > 0.0001f) { _jumpStrength = MathF.Max(0f, value); MarkDirty(); } } }
        public float LookSensitivity { get => _lookSensitivity; set { if (Math.Abs(_lookSensitivity - value) > 0.0001f) { _lookSensitivity = MathF.Max(0f, value); MarkDirty(); } } }
        public bool CanJump { get => _canJump; set { if (_canJump != value) { _canJump = value; MarkDirty(); } } }
        public bool EnableHandTracking { get => _enableHandTracking; set { if (_enableHandTracking != value) { _enableHandTracking = value; MarkDirty(); } } }
        public float VRMoveSpeed { get => _vrMoveSpeed; set { if (Math.Abs(_vrMoveSpeed - value) > 0.0001f) { _vrMoveSpeed = MathF.Max(0f, value); MarkDirty(); } } }
        public bool VRSmoothLocomotion { get => _vrSmoothLocomotion; set { if (_vrSmoothLocomotion != value) { _vrSmoothLocomotion = value; MarkDirty(); } } }

        public InputMethods RequiredInputMethod => InputMethods.XR;

        public VRPlayerComponent() { }

        public VRPlayerComponent(bool isLocal, float moveSpeed = 4f, float sprintMultiplier = 1.9f)
        {
            _isLocalControlled = isLocal;
            _moveSpeed = MathF.Max(0f, moveSpeed);
            _sprintMultiplier = MathF.Max(1f, sprintMultiplier);
        }

        public override IWorldElement BuildUI() => new Element();
        public override string ToString() =>
            $"VRPlayer(Local:{IsLocalControlled} Move:{MoveSpeed:F2} Sprint:{SprintMultiplier:F2} Jump:{JumpStrength:F2} VRMove:{VRMoveSpeed:F2})";
    }
}
