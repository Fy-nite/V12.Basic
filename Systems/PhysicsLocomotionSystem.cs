using System;
using System.Numerics;
using V12.Basic.Components;
using V12.Components;
using V12.Core.Core.Interfaces;
using V12.Core.Interfaces.Physics;

namespace V12.Core.Systems
{
    public class PhysicsLocomotionSystem : IGameService
    {
        private GameRoot _gameRoot;
        private IPhysicsBackend? _physics;

        public PhysicsLocomotionSystem(GameRoot gameRoot)
        {
            _gameRoot = gameRoot;
        }

        public void Initialize(GameRoot g)
        {
            _physics = g.Registry.Get<IPhysicsBackend>();
        }
        public void Update(GameRoot g) {}
        public void Update(float deltaTime)
        {
            var world = _gameRoot.SelectedWorld;
            if (world == null || _physics == null) 
                return;

            // Step physics before reading body state
            _physics.Step(deltaTime);

            foreach (var element in world.Root)
            {
                var bodyComp = element.GetComponent<PhysicsBodyComponent>();
                var loco = element.GetComponent<LocomotionComponent>();
                
                if (bodyComp != null && bodyComp.Body == null && loco != null)
                {
                    var lt = element.LocalTransform;
                    var collider = element.GetComponent<ColliderComponent>();
                    var shape = collider?.Shape ?? MeshShape.Box;
                    var size = collider != null
                        ? new Vector3(collider.Width, collider.Height, collider.Depth)
                        : new Vector3(1f, 2f, 1f);
                    var desc = new PhysicsBodyDesc(
                        lt.Position, lt.Rotation, shape, size,
                        isKinematic: bodyComp.IsKinematic,
                        gravityScale: bodyComp.GravityScale);
                    bodyComp.Body = _physics.CreateBody(desc);
                    Console.WriteLine($"Initialized body for {element.Name}");
                }

                if (bodyComp?.Body == null)
                    continue;

                if (bodyComp.IsKinematic)
                {
                    var lt = element.LocalTransform;
                    bodyComp.Body.Position = lt.Position;
                    bodyComp.Body.Rotation = lt.Rotation;
                }
                else
                {
                    if (loco != null)
                    {
                        bodyComp.Body.LinearVelocity = loco.Velocity;
                    }

                    var lt = element.LocalTransform;
                    lt.Position = bodyComp.Body.Position;

                    // Only sync rotation from physics body for non-player elements
                    // (player rotation/yaw is controlled by PlayerComponent)
                    var isPlayer = element.GetComponent<PlayerComponent>() != null;
                    if (!isPlayer)
                    {
                        lt.Rotation = bodyComp.Body.Rotation;
                    }

                    element.LocalTransform = lt;

                    // Read velocity back from physics body to capture any modifications
                    // from Godot's physics tick (gravity, collisions) for loco state.
                    if (loco != null)
                    {
                        loco.Velocity = bodyComp.Body.LinearVelocity;
                    }
                }
            }
        }
    }
}
