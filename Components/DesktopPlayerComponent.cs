using System;
using V12.Components;
using V12.Core;
using V12.Core.Core.Interfaces;

namespace V12.Basic.Components
{
    /// <summary>
    /// Settings for a desktop (keyboard+mouse) player attached to an element.
    /// This is a pure-data component; a Godot controller reads these values
    /// and applies them on the main thread.
    /// </summary>
    public class DesktopPlayerComponent : ComponentBase, IPlayerControlComponent
    {
        private bool  _isLocalControlled = true;
        private float _moveSpeed         = 4f;   // meters per second
        private float _sprintMultiplier  = 1.9f;
        private float _jumpStrength      = 6f;   // impulse strength
        private float _lookSensitivity   = 1.2f; // multiplier for mouse delta
        private bool  _canJump           = true;

        public override string Name => "DesktopPlayer";
        public override string Description => "Keyboard+mouse player settings";

        /// <summary>If true this element should accept local input and drive movement.</summary>
        public bool IsLocalControlled { get => _isLocalControlled; set { if (_isLocalControlled != value) { _isLocalControlled = value; MarkDirty(); } } }

        /// <summary>Base movement speed in world units per second.</summary>
        public float MoveSpeed { get => _moveSpeed; set { if (Math.Abs(_moveSpeed - value) > 0.0001f) { _moveSpeed = MathF.Max(0f, value); MarkDirty(); } } }

        /// <summary>Scalar applied to MoveSpeed while sprinting.</summary>
        public float SprintMultiplier { get => _sprintMultiplier; set { if (Math.Abs(_sprintMultiplier - value) > 0.0001f) { _sprintMultiplier = MathF.Max(1f, value); MarkDirty(); } } }

        /// <summary>Jump impulse strength applied when jumping.</summary>
        public float JumpStrength { get => _jumpStrength; set { if (Math.Abs(_jumpStrength - value) > 0.0001f) { _jumpStrength = MathF.Max(0f, value); MarkDirty(); } } }

        /// <summary>Mouse look sensitivity multiplier.</summary>
        public float LookSensitivity { get => _lookSensitivity; set { if (Math.Abs(_lookSensitivity - value) > 0.0001f) { _lookSensitivity = MathF.Max(0f, value); MarkDirty(); } } }

        public bool CanJump { get => _canJump; set { if (_canJump != value) { _canJump = value; MarkDirty(); } } }

        // ── IPlayerControlComponent ───────────────────────────────────────────
        public InputMethods RequiredInputMethod => InputMethods.Desktop;
        // IsLocalControlled is already declared above; it satisfies the interface.

        public DesktopPlayerComponent() { }

        public DesktopPlayerComponent(bool isLocal, float moveSpeed = 4f, float sprintMultiplier = 1.9f)
        {
            _isLocalControlled = isLocal;
            _moveSpeed = MathF.Max(0f, moveSpeed);
            _sprintMultiplier = MathF.Max(1f, sprintMultiplier);
        }
        public override IWorldElement BuildUI()
        {
            return new Element();
        }
        public override string ToString() =>
            $"DesktopPlayer(Local:{IsLocalControlled} Move:{MoveSpeed:F2} Sprint:{SprintMultiplier:F2} Jump:{JumpStrength:F2} Sens:{LookSensitivity:F2})";
    }
}
