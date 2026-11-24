using System;
using System.Numerics;
using V12.Components;
using V12.Core;
using V12.Core.Core.Interfaces;

namespace V12.Basic.Components
{
    /// <summary>
    /// Keeps a world's float bubble centred on an anchor. V12 simulates and
    /// renders in local float coordinates; the double <see cref="World.Origin"/>
    /// carries the part of the position that cannot fit in a float. When the
    /// anchor drifts past <see cref="Threshold"/>, the whole world is shifted by
    /// the anchor's local position, so the anchor lands at the local origin.
    /// </summary>
    [ComponentEntry("PHYSICS", "Floating Origin", SortOrder = 6, Icon = "crosshair")]
    public sealed class FloatingOriginComponent : ComponentBase
    {
        /// <summary>Element to keep near the local origin. Defaults to the
        /// component's own element, which is the usual ship camera rig.</summary>
        public IWorldElement? Anchor { get; set; }

        /// <summary>Local distance from the bubble centre that triggers a
        /// re-centre. 2,000 units gives a 4,000-unit local bubble diameter.</summary>
        public float Threshold { get; set; } = 2000f;

        public bool Enabled { get; set; } = true;

        /// <summary>World to re-centre. Defaults to the world containing
        /// <see cref="Anchor"/> or this component's owner.</summary>
        public World? World { get; set; }

        /// <summary>Raised after the world bubble moved. The first argument is
        /// the new global origin; the second is the local delta that was
        /// subtracted from every element.</summary>
        public event Action<World, WorldOffset, Vector3>? Rebased;

        public FloatingOriginComponent()
        {
            Name = "Floating Origin";
        }

        public override void Update(float deltaTime)
        {
            if (!Enabled || Owner == null)
                return;

            var anchor = Anchor ?? Owner;
            var world = World ?? GameRoot.Instance?.GetWorldForElement(Owner);
            if (world == null)
                return;

            var body = anchor.GetComponent<PhysicsBodyComponent>()?.Body;
            Vector3 local;
            if (body != null)
            {
                local = body.Position;
            }
            else
            {
                var m = anchor.WorldTransform;
                local = new Vector3(m.M41, m.M42, m.M43);
            }

            float threshold = MathF.Max(1f, Threshold);
            if (local.LengthSquared() < threshold * threshold)
                return;

            if (world.RebaseOrigin(local))
                Rebased?.Invoke(world, world.Origin, local);
        }

        public override IWorldElement BuildUI() => new Element();
    }
}
