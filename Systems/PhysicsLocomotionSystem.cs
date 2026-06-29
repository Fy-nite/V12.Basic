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
                if (bodyComp == null)
                    continue;

                var loco = element.GetComponent<LocomotionComponent>();

                // Create a physics body for every element with a PhysicsBodyComponent,
                // regardless of whether it also has a LocomotionComponent.
                if (bodyComp.Body == null)
                {
                    var lt = element.LocalTransform;
                    var collider = element.GetComponent<ColliderComponent>();
                    var shape = collider?.Shape ?? MeshShape.Box;
                    var size = collider != null
                        ? new Vector3(collider.Width, collider.Height, collider.Depth)
                        : new Vector3(1f, 2f, 1f);
                    var desc = new PhysicsBodyDesc(
                        lt.Position, lt.Rotation == default ? Quaternion.Identity : lt.Rotation,
                        shape, size,
                        isKinematic: bodyComp.IsKinematic,
                        gravityScale: bodyComp.GravityScale);
                    bodyComp.Body = _physics.CreateBody(desc);
                }

                if (bodyComp.Body == null)
                    continue;

                if (bodyComp.IsKinematic)
                {
                    // Static/kinematic: element transform drives the physics body position
                    var lt = element.LocalTransform;
                    bodyComp.Body.Position = lt.Position;
                    bodyComp.Body.Rotation = lt.Rotation;
                }
                else
                {
                    // Dynamic: driven by Godot physics.
                    // V12 only syncs XZ — Godot handles Y (gravity + jump).
                    if (loco != null)
                    {
                        var currentBodyVel = bodyComp.Body.LinearVelocity;

                        if (loco.Velocity.Y > 0.1f)
                        {
                            // Jump: apply upward impulse (Godot's gravity takes over the arc)
                            bodyComp.Body.AddForce(new Vector3(0, loco.Velocity.Y, 0));
                            loco.Velocity = new Vector3(loco.Velocity.X, 0, loco.Velocity.Z);
                        }

                        // Sync XZ from V12; SyncToGodot preserves Godot's Y for dynamic bodies
                        bodyComp.Body.LinearVelocity = new Vector3(loco.Velocity.X, currentBodyVel.Y, loco.Velocity.Z);
                    }

                    var lt = element.LocalTransform;
                    lt.Position = bodyComp.Body.Position;

                    var isPlayer = element.GetComponent<PlayerComponent>() != null;
                    if (!isPlayer)
                        lt.Rotation = bodyComp.Body.Rotation;

                    element.LocalTransform = lt;

                    if (loco != null)
                    {
                        var readVel = bodyComp.Body.LinearVelocity;
                        loco.Velocity = new Vector3(readVel.X, 0, readVel.Z);
                        loco.IsGrounded = readVel.Y > -1.0f;
                    }
                }
            }
        }
    }
}
