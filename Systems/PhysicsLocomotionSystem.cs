using System;
using System.Collections.Generic;
using System.Numerics;
using V12.Basic.Components;
using V12.Basic.Scene;
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

        /// <summary>Per-element airborne latch used for the dynamic-body ground check.</summary>
        private struct AirState { public bool Airborne; public bool Falling; }
        private readonly Dictionary<long, AirState> _airStates = new();

        public PhysicsLocomotionSystem(GameRoot gameRoot)
        {
            _gameRoot = gameRoot;
        }

        public void Initialize(GameRoot g)
        {
            // GameRoot picks the highest-priority registered backend. A host
            // can supply a fallback; the highest-priority backend wins.
            _physics = g.PhysicsBackend;
            if (_physics == null) return;

            // A backend may need one-time setup. The service Initialize
            // methods are idempotent.
            if (_physics is IGameService service) service.Initialize(g);

            // PhysicsLocomotionSystem owns the step order.
            _physics.AutoStep = false;
        }
        public void Update(GameRoot g) {}
        public void Update(float deltaTime)
        {
            if (_physics == null) return;

            // Step physics before reading body state. Backends that advance
            // themselves (AutoStep) are skipped.
            if (!_physics.AutoStep)
                _physics.Step(deltaTime);

            // Process the active ticking worlds (a host can pause a world with TickingEnabled = false)
            foreach (var world in _gameRoot.ActiveWorlds)
            {
                if (!world.TickingEnabled) continue;
                ProcessWorld(world, deltaTime);
            }
        }

        private void ProcessWorld(World world, float deltaTime)
        {

            foreach (var element in world.Enumerate())
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
                            mass: 1f,
                            type: PhysicsBodyType.Kinematic,
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
                                mass: 1f,
                                type: PhysicsBodyType.Kinematic,
                                gravityScale: bodyComp.GravityScale);
                            bodyComp.Body = _physics.CreateBody(desc);
                        }
                        else
                        {
                            // Flip the body to kinematic while held so position
                            // writes stick (dynamic bodies only take velocity
                            // writes) and gravity can't accumulate.
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
                var rigid = element.GetComponent<RigidBodyComponent>();
                var bodyType = ResolveBodyType(bodyComp, loco, element);

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
                        mass: rigid?.Mass ?? 1f,
                        type: bodyType,
                        isTrigger: false,
                        gravityScale: rigid?.GravityScale ?? bodyComp.GravityScale,
                        linearDamping: rigid?.LinearDamping ?? 0f,
                        angularDamping: rigid?.AngularDamping ?? 0f,
                        canSleep: rigid?.CanSleep ?? true,
                        lockRotation: bodyComp.LockRotation);
                    bodyComp.Body = _physics.CreateBody(desc);
                    bodyComp.ApplyPendingVelocity();

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

                if (bodyType == PhysicsBodyType.Character)
                {
                    // Character controller: the backend runs its collide-and-slide
                    // movement from LinearVelocity. Game code owns that velocity;
                    // LocomotionComponent, when present, is only a legacy driver.
                    var lt = element.LocalTransform;
                    if (loco != null)
                        bodyComp.Body.LinearVelocity = loco.Velocity;
                    lt.Position = bodyComp.Body.Position;
                    element.LocalTransform = lt;
                    if (loco != null)
                        loco.IsGrounded = bodyComp.Body.IsOnFloor;
                    if (!bodyComp.Body.LockRotation)
                        bodyComp.Body.Rotation = lt.Rotation;
                }
                else if (bodyType is PhysicsBodyType.Static or PhysicsBodyType.Kinematic)
                {
                    // Static/kinematic: element transform drives the physics body.
                    // Top-level kinematic bodies slide along walls via
                    // BodyTestMotion so they don't clip through the world.
                    // Nested kinematic bodies (e.g. grabbed boxes re-parented to a
                    // hand, whose local transform is identity) are teleported.
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
                else
                {
                    // Dynamic: driven by the physics backend.
                    // V12 only syncs XZ; the backend handles Y (gravity + jump).
                    bool jumped = false;
                    if (loco != null)
                    {
                        if (loco.Velocity.Y > 0.1f)
                        {
                            // Jump: apply upward impulse (gravity then bends the arc).
                            bodyComp.Body.AddForce(new Vector3(0, loco.Velocity.Y, 0));
                            loco.Velocity = new Vector3(loco.Velocity.X, 0, loco.Velocity.Z);
                            jumped = true;
                        }

                        // Sync XZ from V12 while preserving the body's vertical velocity.
                        // This MUST be read after the impulse above — a copy taken before
                        // it would overwrite the jump velocity and cancel the jump.
                        var currentBodyVel = bodyComp.Body.LinearVelocity;
                        bodyComp.Body.LinearVelocity = new Vector3(loco.Velocity.X, currentBodyVel.Y, loco.Velocity.Z);
                        
                        if (playerComp != null && (MathF.Abs(loco.Velocity.X) > 0.01f || MathF.Abs(loco.Velocity.Z) > 0.01f))
                        {
                            // Console.WriteLine($"[PhysicsLocomotionSystem] Setting player body velocity: ({loco.Velocity.X:F2}, {currentBodyVel.Y:F2}, {loco.Velocity.Z:F2})");
                        }
                    }

                    var lt = element.LocalTransform;
                    lt.Position = bodyComp.Body.Position;

                    var isPlayer = playerComp != null;
                    if (!isPlayer && !bodyComp.Body.LockRotation)
                        lt.Rotation = bodyComp.Body.Rotation;

                    element.LocalTransform = lt;

                    if (loco != null)
                    {
                        var readVel = bodyComp.Body.LinearVelocity;
                        loco.Velocity = new Vector3(readVel.X, 0, readVel.Z);

                        // Grounded latch. Velocity alone can't tell "resting" apart from
                        // "slow at the apex of a jump" (both have Y≈0), so the old
                        // `readVel.Y > -1` test reported grounded mid-air and allowed
                        // jump spam. Latch airborne on a jump or on clearly falling, and
                        // only clear it once we have been falling and then come to rest.
                        var state = _airStates.TryGetValue(element.Id, out var s) ? s : default;
                        if (jumped) { state.Airborne = true; state.Falling = false; }
                        if (state.Airborne)
                        {
                            if (readVel.Y < -0.5f) state.Falling = true;
                            if (state.Falling && MathF.Abs(readVel.Y) < 0.2f) { state.Airborne = false; state.Falling = false; }
                        }
                        else if (readVel.Y < -0.5f)
                        {
                            state.Airborne = true;
                            state.Falling = true;
                        }
                        _airStates[element.Id] = state;
                        loco.IsGrounded = !state.Airborne;
                    }
                }
            }
        }

        /// <summary>
        /// Resolves the effective body type. Legacy scenes expressed the character
        /// controller as a kinematic body plus a LocomotionComponent on a root
        /// element; that still maps to <see cref="PhysicsBodyType.Character"/>.
        /// </summary>
        private static PhysicsBodyType ResolveBodyType(
            PhysicsBodyComponent bodyComp, LocomotionComponent? loco, IWorldElement element)
        {
            if (bodyComp.BodyType != PhysicsBodyType.Kinematic)
                return bodyComp.BodyType;

            return loco != null && element.Parent == null
                ? PhysicsBodyType.Character
                : PhysicsBodyType.Kinematic;
        }

        private static long AgeMs(long epochMs)
        {
            var now = DateTime.UtcNow.Ticks / TimeSpan.TicksPerMillisecond;
            return now - epochMs;
        }
    }
}
