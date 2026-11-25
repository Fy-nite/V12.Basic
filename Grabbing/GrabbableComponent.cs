using System;
using V12.Components;
using V12.Core.Core.Interfaces;

namespace V12.Basic.Grabbing
{
    /// <summary>
    /// Marks an element's dynamic physics body as something a
    /// <see cref="GrabberComponent"/> may pick up. The component is optional when
    /// a grabber sets <c>RequireGrabbable = false</c>, but keeping it explicit
    /// stops a grab ray from picking up arbitrary dynamic bodies.
    /// </summary>
    [ComponentEntry("GAMEPLAY", "Grabbable", SortOrder = 12, Icon = "hand")]
    public class GrabbableComponent : ComponentBase
    {
        private bool _canGrab = true;
        private float _throwMultiplier = 1f;
        private bool _rotateWithHand = true;
        private bool _lockRotation;
        private float _holdDistance;

        public override string Name => "Grabbable";
        public override string Description => "Allows a grabber to pick this body up";

        /// <summary>When false the body ignores grab attempts without being removed.</summary>
        public bool CanGrab
        {
            get => _canGrab;
            set { if (_canGrab != value) { _canGrab = value; MarkDirty(); } }
        }

        /// <summary>Scales the release velocity. Set above 1 for a springy toss,
        /// below 1 for a heavy object that drops out of the hand.</summary>
        public float ThrowMultiplier
        {
            get => _throwMultiplier;
            set { if (Math.Abs(_throwMultiplier - value) > 0.0001f) { _throwMultiplier = MathF.Max(0f, value); MarkDirty(); } }
        }

        /// <summary>When false the object keeps its grabbed orientation while it
        /// follows the hand. Position still tracks.</summary>
        public bool RotateWithHand
        {
            get => _rotateWithHand;
            set { if (_rotateWithHand != value) { _rotateWithHand = value; MarkDirty(); } }
        }

        /// <summary>Keep the body upright while held. Overrides
        /// <see cref="RotateWithHand"/>.</summary>
        public bool LockRotation
        {
            get => _lockRotation;
            set { if (_lockRotation != value) { _lockRotation = value; MarkDirty(); } }
        }

        /// <summary>Optional per-object hold distance for desktop ray grabs.
        /// 0 uses the grabber's own value.</summary>
        public float HoldDistance
        {
            get => _holdDistance;
            set { if (Math.Abs(_holdDistance - value) > 0.0001f) { _holdDistance = MathF.Max(0f, value); MarkDirty(); } }
        }

        /// <summary>Raised after a grabber takes hold. Not serialized.</summary>
        public event Action<GrabberComponent, IWorldElement>? Grabbed;

        /// <summary>Raised after a grabber lets go. Not serialized.</summary>
        public event Action<GrabberComponent, IWorldElement>? Released;

        internal void NotifyGrabbed(GrabberComponent grabber, IWorldElement element)
        {
            try { Grabbed?.Invoke(grabber, element); } catch { }
        }

        internal void NotifyReleased(GrabberComponent grabber, IWorldElement element)
        {
            try { Released?.Invoke(grabber, element); } catch { }
        }
    }
}
