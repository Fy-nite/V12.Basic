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
    [ComponentEntry("PHYSICS", "Physics Body", SortOrder = 3, Icon = "atom")]
    public class PhysicsBodyComponent : ComponentBase
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

        /// <summary>Applies an impulse/force to the runtime body, if it exists.</summary>
        public void AddForce(Vector3 force) => Body?.AddForce(force);

        /// <summary>Applies an angular impulse to the runtime body, if it exists.</summary>
        public void AddTorque(Vector3 torque) => Body?.AddTorque(torque);

        /// <summary>
        /// True when this element was replicated from the host (received via WorldSync
        /// or a WorldDelta create). Clients must not locally simulate replicated
        /// bodies — they run as kinematic followers driven by the host's transform.
        /// Runtime-only, never serialized back.
        /// </summary>
        [BsonIgnore]
        public bool IsReplicated { get; set; }

        // ── Client-held grab state ──────────────────────────────────────────
        // Client-authoritative grab: a remote client drives this element while
        // held, and every peer (host included) follows the pose carried here
        // instead of local simulation / host transforms. The holding client
        // streams poses via the RPC channel; the host pauses simulation of the
        // element until it is released or the stream goes stale. Runtime-only.
        [BsonIgnore]
        public bool ClientHeld { get; set; }

        [BsonIgnore]
        public System.Numerics.Vector3 HeldPosition { get; set; }

        [BsonIgnore]
        public System.Numerics.Quaternion HeldRotation { get; set; }

        /// <summary>Epoch milliseconds of the last GrabMove received (staleness watchdog).</summary>
        [BsonIgnore]
        public long LastGrabMoveMs { get; set; }

        public PhysicsBodyComponent() { }

        /// <summary>Remote: a client grabbed this element — the host pauses its simulation.</summary>
        [Remote]
        public void Grab()
        {
            ClientHeld = true;
            LastGrabMoveMs = NowMs();
        }

        /// <summary>Remote: the holding client's latest world pose.</summary>
        [Remote]
        public void GrabMove(float x, float y, float z, float qx, float qy, float qz, float qw)
        {
            HeldPosition = new System.Numerics.Vector3(x, y, z);
            HeldRotation = new System.Numerics.Quaternion(qx, qy, qz, qw);
            LastGrabMoveMs = NowMs();
        }

        /// <summary>Remote: the holding client released the element at this pose.</summary>
        [Remote]
        public void Release(float x, float y, float z, float qx, float qy, float qz, float qw)
        {
            HeldPosition = new System.Numerics.Vector3(x, y, z);
            HeldRotation = new System.Numerics.Quaternion(qx, qy, qz, qw);
            LastGrabMoveMs = NowMs();
            ClientHeld = false;

            // On the host (non-replicated element) restore the body we flipped to
            // kinematic while held so simulation resumes. Replicated followers on
            // clients stay kinematic — they follow the host's transform.
            if (!IsReplicated)
                Body?.SetKinematic(false);
        }

        private static long NowMs() => DateTime.UtcNow.Ticks / TimeSpan.TicksPerMillisecond;

        public override IWorldElement BuildUI()
        {
            return new Element();
        }
    }
}
