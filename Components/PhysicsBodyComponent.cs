using MongoDB.Bson.Serialization.Attributes;
using V12.Components;
using V12.Core;
using V12.Core.Core.Interfaces;
using V12.Core.Interfaces.Physics;

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

        public PhysicsBodyComponent() { }

        public override IWorldElement BuildUI()
        {
            return new Element();
        }
    }
}
