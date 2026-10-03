using System;
using MongoDB.Bson.Serialization.Attributes;
using V12.Components;
using V12.Core;
using V12.Core.Core.Interfaces;
using V12.Core.Interfaces.Physics;
using V12.Core.Networking;

namespace V12.Basic.Components
{
    public class PhysicsBodyComponent : ComponentBase
    {
        /// <summary>
        /// The runtime physics body instance, provided by the active physics backend.
        /// This is a backend-specific object (e.g. GodotPhysicsBody) that cannot be
        /// serialized — it contains process-local handles (RIDs) and is recreated
        /// on the receiver side from the component's transform and settings.
        /// </summary>
        [BsonIgnore]
        public IPhysicsBody? Body { get; set; }
        public bool IsKinematic { get; set; } = false;
        public float GravityScale { get; set; } = 1f;

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
