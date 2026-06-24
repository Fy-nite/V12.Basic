using V12.Components;
using V12.Core;
using V12.Core.Core.Interfaces;
using V12.Core.Interfaces.Physics;

namespace V12.Basic.Components
{
    public class PhysicsBodyComponent : ComponentBase
    {
        public IPhysicsBody? Body { get; set; }
        public bool IsKinematic { get; set; } = false;
        public float GravityScale { get; set; } = 1f;

        public PhysicsBodyComponent() { }

        public override IWorldElement BuildUI()
        {
            return new Element();
        }
    }
}
