using System;
using System.Numerics;
using V12.Basic.Components;
using V12.Components;
using V12.Core.Core.Interfaces;
using V12.Core.Interfaces.Physics;
using V12.Core.Interfaces.Renderer;

namespace V12.Core.Systems
{
    public class PhysicsLocomotionSystem : IGameService
    {
        /// <summary>Seconds after which a held element whose pose stream went silent
        /// is released (the holding client vanished) and simulation resumes.</summary>
        private const long GrabHoldTimeoutMs = 3000;

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
            if (_physics == null) return;

            // Step physics before reading body state
            _physics.Step(deltaTime);

            // Process all active worlds via the ECS-style query API
            foreach (var world in _gameRoot.ActiveWorlds)
                ProcessWorld(world, deltaTime);
        }

        private void ProcessWorld(World world, float deltaTime)
        {

            foreach (var element in world.Root)
            {
                var bodyComp = element.GetComponent<PhysicsBodyComponent>();
                if (bodyComp == null)
                    continue;

                // Host-replicated body: never simulate locally. Create a kinematic
                // follower so the client keeps a collidable presence, and drive it
                // from the host-authoritative transform each tick. The receive path
                // (TryApplyToElement) writes the TransformComponent directly, NOT
                // element.LocalTransform, so the pose is read from the transform
                // component / world matrix, never LocalTransform.
                if (bodyComp.IsReplicated)
                {
                    // Stale client grab (holder vanished): resume host-driven following.
                    if (bodyComp.ClientHeld && AgeMs(bodyComp.LastGrabMoveMs) > GrabHoldTimeoutMs)
                        bodyComp.ClientHeld = false;

                    Vector3 trans;
                    Quaternion rot;
                    if (bodyComp.ClientHeld)
                    {
                        // A remote client drives this element: follow its pose
                        // instead of the host's (the holding client's local
                        // PickupSystem writes the same pose, so no fight).
                        trans = bodyComp.HeldPosition;
                        rot = bodyComp.HeldRotation == default ? Quaternion.Identity : bodyComp.HeldRotation;
                    }
                    else
                    {
                        var wt = element.WorldTransform;
                        if (!Matrix4x4.Decompose(wt, out _, out rot, out trans))
                        {
                            var tc = element.GetComponent<TransformComponent>();
                            if (tc != null)
                            {
                                trans = new Vector3(tc.X, tc.Y, tc.Z);
                                rot = Quaternion.CreateFromYawPitchRoll(tc.RY, tc.RX, tc.RZ);
                            }
                            else
                            {
                                trans = element.LocalTransform.Position;
                                rot = element.LocalTransform.Rotation;
                            }
                        }
                    }

                    if (bodyComp.Body == null)
                    {
                        var collider = element.GetComponent<ColliderComponent>();
                        var shape = collider?.Shape ?? MeshShape.Box;
                        var size = collider != null
                            ? new Vector3(collider.Width, collider.Height, collider.Depth)
                            : new Vector3(1f, 2f, 1f);
                        var desc = new PhysicsBodyDesc(
                            trans, rot == default ? Quaternion.Identity : rot,
                            shape, size,
                            isKinematic: true,
                            isCharacterController: false,
                            gravityScale: bodyComp.GravityScale);
                        bodyComp.Body = _physics.CreateBody(desc);
                    }
                    else
                    {
                        bodyComp.Body.Position = trans;
                        bodyComp.Body.Rotation = rot;
                    }
                    continue;
                }

                // Client-held element on the host (a remote client grabbed it):
                // pause local simulation and mirror the client's pose into the
                // body and element transform so the host renders the grab and
                // the pose replicates to every other peer.
                if (bodyComp.ClientHeld)
                {
                    if (AgeMs(bodyComp.LastGrabMoveMs) > GrabHoldTimeoutMs)
                    {
                        // Holder vanished — release and resume host simulation.
                        bodyComp.ClientHeld = false;
                        // Restore the body we flipped to kinematic while held.
                        if (bodyComp.Body != null && !bodyComp.Body.IsDynamic)
                            bodyComp.Body.SetKinematic(false);
                    }
                    else
                    {
                        var heldPos = bodyComp.HeldPosition;
                        var heldRot = bodyComp.HeldRotation == default ? Quaternion.Identity : bodyComp.HeldRotation;

                        if (bodyComp.Body == null)
                        {
                            var collider = element.GetComponent<ColliderComponent>();
                            var shape = collider?.Shape ?? MeshShape.Box;
                            var size = collider != null
                                ? new Vector3(collider.Width, collider.Height, collider.Depth)
                                : new Vector3(1f, 2f, 1f);
                            var desc = new PhysicsBodyDesc(
                                heldPos, heldRot, shape, size,
                                isKinematic: true,
                                isCharacterController: false,
                                gravityScale: bodyComp.GravityScale);
                            bodyComp.Body = _physics.CreateBody(desc);
                        }
                        else
                        {
                            // Flip the body to kinematic while held so position
                            // writes stick (dynamic bodies only take velocity
                            // writes in SyncToGodot) and gravity can't accumulate.
                            if (bodyComp.Body.IsDynamic)
                                bodyComp.Body.SetKinematic(true);
                            bodyComp.Body.Position = heldPos;
                            bodyComp.Body.Rotation = heldRot;
                            bodyComp.Body.LinearVelocity = Vector3.Zero;
                        }

                        // Element pose follows the client: LocalTransform also
                        // mirrors into the TransformComponent (dirty → component
                        // batch) so every peer converges on the held pose.
                        element.LocalTransform = new TRS
                        {
                            Position = heldPos,
                            Rotation = heldRot,
                            Scale = Vector3.One
                        };
                        continue;
                    }
                }

                var loco = element.GetComponent<LocomotionComponent>();
                var playerComp = element.GetComponent<PlayerComponent>();

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
                        isCharacterController: bodyComp.IsKinematic && loco != null,
                        gravityScale: bodyComp.GravityScale);
                    bodyComp.Body = _physics.CreateBody(desc);

                    // Host-authoritative movement is replicated through the component
                    // batch (TransformComponent setters MarkDirty), never the element
                    // batch (which carries no transform data and is ignored by peers).
                    // Ensure a TransformComponent exists so this body's motion reaches
                    // remote clients. Player pose is replicated via RemotePlayerManager,
                    // so the local player is skipped to avoid redundant traffic.
                    if (playerComp == null && element.GetComponent<TransformComponent>() == null)
                    {
                        element.AddComponent(new TransformComponent(lt.Position.X, lt.Position.Y, lt.Position.Z));
                    }

                    if (playerComp != null)
                    {
                        Console.WriteLine($"[PhysicsLocomotionSystem] Created physics body for player at ({lt.Position.X:F2}, {lt.Position.Y:F2}, {lt.Position.Z:F2})");
                    }
                }

                if (bodyComp.Body == null)
                    continue;

                if (bodyComp.IsKinematic)
                {
                    if (loco != null && element.Parent == null)
                    {
                        // Character controller (e.g. the XR player): a Godot
                        // CharacterBody3D owned by the backend. The ECS owns the
                        // velocity (gravity, jump, acceleration); the backend runs
                        // move_and_slide() on the main thread and reads the floor
                        // state back. The element transform mirrors the slid body
                        // so the camera never clips into a wall for a frame.
                        var lt = element.LocalTransform;
                        bodyComp.Body.LinearVelocity = loco.Velocity;
                        lt.Position = bodyComp.Body.Position;
                        element.LocalTransform = lt;
                        loco.IsGrounded = bodyComp.Body.IsOnFloor;
                        bodyComp.Body.Rotation = lt.Rotation;
                    }
                    else
                    {
                        // Static/kinematic: element transform drives the physics body.
                        // Top-level kinematic bodies slide along walls via
                        // BodyTestMotion so they don't clip through the world.
                        // Nested kinematic bodies (e.g. grabbed boxes re-parented
                        // to a hand, whose local transform is identity) are
                        // teleported directly as before.
                        var lt = element.LocalTransform;
                        var current = bodyComp.Body.Position;
                        var desired = lt.Position;

                        if (element.Parent == null)
                        {
                            var delta = desired - current;
                            if (delta.LengthSquared() > 1e-8f)
                            {
                                var applied = _physics.MoveKinematic(bodyComp.Body, delta);
                                lt.Position = current + applied;
                                element.LocalTransform = lt;
                            }
                        }
                        else
                        {
                            bodyComp.Body.Position = desired;
                        }
                        bodyComp.Body.Rotation = lt.Rotation;
                    }
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
                        
                        if (playerComp != null && (MathF.Abs(loco.Velocity.X) > 0.01f || MathF.Abs(loco.Velocity.Z) > 0.01f))
                        {
                            // Console.WriteLine($"[PhysicsLocomotionSystem] Setting player body velocity: ({loco.Velocity.X:F2}, {currentBodyVel.Y:F2}, {loco.Velocity.Z:F2})");
                        }
                    }

                    var lt = element.LocalTransform;
                    lt.Position = bodyComp.Body.Position;

                    var isPlayer = playerComp != null;
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

        private static long AgeMs(long epochMs)
        {
            var now = DateTime.UtcNow.Ticks / TimeSpan.TicksPerMillisecond;
            return now - epochMs;
        }
    }
}
