using System;
using System.Numerics;
using MongoDB.Bson.Serialization.Attributes;
using V12.Components;
using V12.Core;
using V12.Core.Core.Interfaces;
using V12.Core.Interfaces.Physics;
using V12.Core.Networking;

namespace V12.Basic.Components
{
    /// <summary>
    /// Attaches a backend physics body to an element. Placement currently assumes
    /// a root-level element; a nested dynamic body is not transform-synced
    /// relative to its parent, so keep simulated bodies at the world root.
    /// </summary>
    [ComponentEntry("PHYSICS", "Physics Body", SortOrder = 3, Icon = "atom")]
    public class PhysicsBodyComponent : ComponentBase, IFloatingOriginAware
    {
        /// <summary>
        /// The runtime physics body instance, provided by the active physics backend.
        /// This is a backend-specific object that cannot be serialized: it contains
        /// process-local handles and is recreated on the receiver side from the
        /// component's transform and settings.
        /// </summary>
        [BsonIgnore]
        public IPhysicsBody? Body { get; set; }

        /// <summary>
        /// Generic physics mode. Set <see cref="PhysicsBodyType.Character"/> for a
        /// move-and-slide controller; gameplay code then drives it by writing
        /// <see cref="LinearVelocity"/> each tick.
        /// </summary>
        public PhysicsBodyType BodyType { get; set; } = PhysicsBodyType.Dynamic;

        /// <summary>Legacy alias for <see cref="BodyType"/>. Not serialized.</summary>
        [BsonIgnore]
        public bool IsKinematic
        {
            get => BodyType is PhysicsBodyType.Static or PhysicsBodyType.Kinematic or PhysicsBodyType.Character;
            set => BodyType = value ? PhysicsBodyType.Kinematic : PhysicsBodyType.Dynamic;
        }

        /// <summary>Gravity multiplier used when no <see cref="RigidBodyComponent"/> is present.</summary>
        public float GravityScale { get; set; } = 1f;

        /// <summary>
        /// When true, a top-level kinematic body is driven as continuous motion:
        /// the engine publishes its step velocity and lets the backend integrate the
        /// pose, so contacts see the surface speed and a rider is carried along.
        /// Use it for planets, platforms and other scripted movers. Leave false for
        /// bodies that must teleport or collide-and-slide (XR player, grabbed props).
        /// </summary>
        public bool ContinuousKinematicMotion { get; set; }

        /// <summary>
        /// When true (default) a <c>LocomotionComponent</c> writes its XZ velocity
        /// into this dynamic body every tick. Set false for a vessel or vehicle
        /// whose physics velocity must be preserved (orbital motion, spherical
        /// gravity, scripted flight).
        /// </summary>
        public bool SyncLocomotionVelocity { get; set; } = true;

        private Vector3 _pendingVelocity;

        /// <summary>
        /// Convenience pass-through to the runtime body's linear velocity. Values
        /// written before the backend body exists are queued and applied on creation,
        /// so gameplay code can set velocity in the same tick it adds the component.
        /// </summary>
        [BsonIgnore]
        public Vector3 LinearVelocity
        {
            get => Body?.LinearVelocity ?? _pendingVelocity;
            set
            {
                if (Body != null) Body.LinearVelocity = value;
                else _pendingVelocity = value;
            }
        }

        /// <summary>Applies a velocity written before the backend body existed.</summary>
        internal void ApplyPendingVelocity()
        {
            if (Body == null || _pendingVelocity == Vector3.Zero) return;
            Body.LinearVelocity = _pendingVelocity;
            _pendingVelocity = Vector3.Zero;
        }

        /// <summary>Keep the body upright and ignore angular motion.</summary>
        public bool LockRotation { get; set; }

        /// <summary>Convenience pass-through to the runtime body's angular velocity.</summary>
        [BsonIgnore]
        public Vector3 AngularVelocity
        {
            get => Body?.AngularVelocity ?? Vector3.Zero;
            set { if (Body != null) Body.AngularVelocity = value; }
        }

        /// <summary>True while a character controller is resting on the ground.</summary>
        [BsonIgnore]
        public bool IsOnFloor => Body?.IsOnFloor ?? false;

        /// <summary>Applies a continuous force (N) for the next physics step.</summary>
        public void AddForce(Vector3 force) => Body?.AddForce(force);

        /// <summary>Applies an instantaneous linear impulse (kg*m/s).</summary>
        public void AddImpulse(Vector3 impulse) => Body?.AddImpulse(impulse);

        /// <summary>Applies a continuous torque (N*m) for the next physics step.</summary>
        public void AddTorque(Vector3 torque) => Body?.AddTorque(torque);

        /// <summary>Applies an instantaneous angular impulse (kg*m^2/s).</summary>
        public void AddAngularImpulse(Vector3 impulse) => Body?.AddAngularImpulse(impulse);

        /// <summary>Runtime mass. Changing it recomputes the body inertia from its shape.</summary>
        public float BodyMass
        {
            get => Body?.Mass ?? 0f;
            set { if (Body != null) Body.Mass = value; }
        }

        /// <summary>
        /// True when this element was replicated from the host (received via WorldSync
        /// or a WorldDelta create). Followers on a client must not locally simulate
        /// replicated bodies — they run as kinematic followers driven by the pose of
        /// whichever peer simulates the element. Runtime-only, never serialized.
        /// </summary>
        [BsonIgnore]
        public bool IsReplicated { get; set; }

        public PhysicsBodyComponent() { }

        /// <summary>
        /// Release the backend body. Without this the simulation keeps the body
        /// alive (and colliding) after its element or component is gone, which is
        /// the leak you get when a world is closed.
        /// </summary>
        public override void OnDetach(IWorldElement worldElement)
        {
            if (Body is not null)
            {
                try
                {
                    GameRoot.Instance?.Registry.Get<IPhysicsBackend>()?.DestroyBody(Body);
                }
                catch
                {
                    // Teardown must not throw: the simulation may already be gone.
                }

                Body = null;
            }

            base.OnDetach(worldElement);
        }

        /// <summary>Move the backend body into the new bubble frame. Relative
        /// positions and velocities stay unchanged.</summary>
        public void OnOriginShift(Vector3 delta)
        {
            Body?.Position -= delta;
        }

        public override IWorldElement BuildUI()
        {
            return new Element();
        }
    }
}
