using BepuPhysics;
using V12.Core;
using V12.Core.Core.Interfaces;
using MongoDB.Bson.Serialization.Attributes;
using V12.Components;

namespace V12.Basic.Components
{
    public class PhysicsBodyComponent : ComponentBase
    {
        [BsonIgnore]
        public BodyHandle BodyHandle { get; set; }
        public bool IsKinematic { get; set; } = false;

        public PhysicsBodyComponent() { }

        public override IWorldElement BuildUI()
        {
            return new Element();
        }
    }
}
