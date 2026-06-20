using System;
using System;
using System.Numerics;
using V12.Components;
using V12.Core;
using V12.Core.Core.Interfaces;

namespace V12.Basic.Components
{
    public class LocomotionComponent : ComponentBase
    {
        public override string Name => "Locomotion";
        public override string Description => "State for player locomotion";

        public Vector3 Velocity { get; set; } = Vector3.Zero;
        public bool IsGrounded { get; set; } = true;

        public float MoveSpeed { get; set; } = 2.0f;
        public float JumpStrength { get; set; } = 3.0f;
        public float Gravity { get; set; } = 9.8f;
        public InputMethods PreferredInputMethod { get; internal set; }
        public float SprintMultiplier { get; internal set; }
        public float LookSensitivity { get; internal set; }
        public bool CanJump { get; internal set; }
        public float VRMoveSpeed { get; internal set; }
        public bool VRSmoothLocomotion { get; internal set; }
        public bool EnableHandTracking { get; internal set; }

        public override IWorldElement BuildUI()
        {
            return new Element();
        }
    }
}
