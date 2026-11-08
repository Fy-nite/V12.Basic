using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Collections.Generic;
using BepuPhysics;
using BepuPhysics.Collidables;
using BepuPhysics.CollisionDetection;
using BepuPhysics.Constraints;
using BepuUtilities;
using BepuUtilities.Memory;
using V12.Core.Core.Interfaces;
using V12.Components;
using V12.Core.Interfaces.Physics;
using V12.Core.Interfaces.Renderer;
using BepuPhysics.Trees;
using V12.Basic.Components;

namespace V12.Core.Systems
{
    /// <summary>Mutable gravity shared with the pose integrator.</summary>
    public sealed class GravityProvider
    {
        public Vector3 Value;
        public GravityProvider(Vector3 value) { Value = value; }
    }

    public class PhysicsService : IGameService, IPhysicsBackend
    {
        public Simulation? Simulation { get; private set; }
        private BufferPool? _bufferPool;
        private ThreadDispatcher? _threadDispatcher;

        private const float FixedTimestep = 1f / 60f;
        private const float MaxAccumulator = 0.25f;
        private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
        private double _lastStepTime;
        private float _accumulator;

        private readonly GravityProvider _gravity = new(new Vector3(0, -9.81f, 0));

        /// <summary>High priority so this backend wins over host fallbacks.</summary>
        public int Priority => 100;

        /// <summary>Global gravity applied to dynamic bodies.</summary>
        public Vector3 Gravity { get => _gravity.Value; set => _gravity.Value = value; }

        /// <summary>
        /// When false the owner (PhysicsLocomotionSystem) is responsible for
        /// calling <see cref="Step"/>. Prevents the simulation from being
        /// advanced twice per tick.
        /// </summary>
        public bool AutoStep { get; set; } = true;

        private readonly Dictionary<long, BepuPhysicsBody> _bodyMap = new();
        // Handle -> body, so a raycast hit's CollidableReference can be resolved back to
        // the IPhysicsBody (interaction systems match the hit body to an element).
        private readonly Dictionary<BodyHandle, BepuPhysicsBody> _bodiesByHandle = new();
        private long _nextBodyId;

        public void Initialize(GameRoot g)
        {
            if (Simulation != null) return;
            _bufferPool = new BufferPool();
            _threadDispatcher = new ThreadDispatcher(Environment.ProcessorCount);

            Simulation = Simulation.Create(_bufferPool, new NarrowPhaseCallbacks(), new PoseIntegratorCallbacks(_gravity), new SolveDescription(8, 1));
        }

        public void Update(GameRoot g) {}
        public void Update(float deltaTime)
        {
            if (AutoStep) Step(deltaTime);
        }

        void IPhysicsBackend.Step(float deltaTime) => Step(deltaTime);

        /// <summary>Advances the simulation and refreshes per-body derived state.</summary>
        public void Step(float deltaTime)
        {
            if (Simulation == null) return;

            // Use real elapsed time, not the worker loop's clamped dt. The loop
            // can run far faster than 1 kHz; its minimum clamp would otherwise
            // make simulated time (and movement speed) scale with loop FPS.
            var now = _clock.Elapsed.TotalSeconds;
            float realDelta = _lastStepTime == 0.0
                ? MathF.Max(deltaTime, 0f)
                : (float)(now - _lastStepTime);
            _lastStepTime = now;

            if (realDelta <= 0f) return;

            _accumulator = MathF.Min(_accumulator + realDelta, MaxAccumulator);

            int steps = 0;
            while (_accumulator >= FixedTimestep && steps < 8)
            {
                Simulation.Timestep(FixedTimestep, _threadDispatcher);
                _accumulator -= FixedTimestep;
                steps++;
            }

            if (steps == 0) return;

            ApplyBodySettings(FixedTimestep * steps);
            UpdateGrounded();
        }

        private void ApplyBodySettings(float deltaTime)
        {
            foreach (var body in _bodyMap.Values)
            {
                if (!body.IsDynamic) continue;
                var b = Simulation!.Bodies[body.Handle];
                if (body.LockRotation)
                    b.Velocity.Angular = Vector3.Zero;
                if (body.LinearDamping > 0f)
                    b.Velocity.Linear *= MathF.Max(0f, 1f - body.LinearDamping * deltaTime);
                if (body.AngularDamping > 0f)
                    b.Velocity.Angular *= MathF.Max(0f, 1f - body.AngularDamping * deltaTime);
            }
        }

        private void UpdateGrounded()
        {
            foreach (var body in _bodyMap.Values)
            {
                if (!body.IsDynamic || body.GroundProbeDistance <= 0f) continue;

                var position = Simulation!.Bodies[body.Handle].Pose.Position;
                var handler = new RayHitHandler { IgnoreHandle = body.Handle };
                Simulation.RayCast(position, -Vector3.UnitY, body.GroundProbeDistance, _bufferPool!, ref handler);
                body.IsOnFloor = handler.HitFound && handler.T <= body.GroundProbeDistance;
            }
        }

        Vector3 IPhysicsBackend.MoveKinematic(IPhysicsBody body, Vector3 delta)
        {
            // This backend has no sliding: apply the full delta to the body.
            if (body is BepuPhysicsBody b)
                b.Position = b.Position + delta;
            return delta;
        }

        IPhysicsBody IPhysicsBackend.CreateBody(in PhysicsBodyDesc desc)
        {
            TypedIndex shapeIndex = CreateShape(desc);

            bool isCharacter = desc.Type == PhysicsBodyType.Character;
            bool isDynamic = desc.Type is PhysicsBodyType.Dynamic or PhysicsBodyType.Character;
            var collidable = new CollidableDescription(shapeIndex, 0.1f);
            var activity = new BodyActivityDescription(0.01f);
            float inverseMass = desc.Mass > 0f ? 1f / desc.Mass : 1f;

            var description = isDynamic
                ? BodyDescription.CreateDynamic(new RigidPose(desc.Position, desc.Rotation),
                    new BodyInertia { InverseMass = inverseMass }, collidable, activity)
                : BodyDescription.CreateKinematic(new RigidPose(desc.Position, desc.Rotation), collidable, activity);

            var handle = Simulation!.Bodies.Add(description);
            long id = _nextBodyId++;
            var body = new BepuPhysicsBody(handle, Simulation, id, collidable, inverseMass, activity)
            {
                IsDynamic = isDynamic,
                IsCharacterController = isCharacter,
                LockRotation = desc.LockRotation || isCharacter,
                LinearDamping = desc.LinearDamping,
                AngularDamping = desc.AngularDamping,
                GroundProbeDistance = isCharacter ? desc.Size.Y * 0.5f + 0.15f : 0f,
            };
            _bodyMap[id] = body;
            _bodiesByHandle[handle] = body;
            return body;
        }

        void IPhysicsBackend.DestroyBody(IPhysicsBody body)
        {
            if (body is BepuPhysicsBody b)
            {
                _bodyMap.Remove(b.Id);
                _bodiesByHandle.Remove(b.Handle);
                if (Simulation.Bodies.ActiveSet.Count > 0)
                {
                    Simulation.Bodies.Remove(b.Handle);
                }
            }
        }

        bool IPhysicsBackend.Raycast(in Ray ray, out RaycastHit hit)
        {
            hit = default;
            var hitHandler = new RayHitHandler();
            Simulation.RayCast(ray.Origin, ray.Direction, ray.MaxDistance, _bufferPool, ref hitHandler);

            if (hitHandler.HitFound)
            {
                // Resolve the hit collidable back to its IPhysicsBody so callers (the
                // button/pickup interaction systems) can match it to an element.
                IPhysicsBody? hitBody = null;
                var collidable = hitHandler.Collidable;
                if (collidable.Mobility != BepuPhysics.Collidables.CollidableMobility.Static
                    && _bodiesByHandle.TryGetValue(collidable.BodyHandle, out var hitBodyImpl))
                    hitBody = hitBodyImpl;

                hit = new RaycastHit
                {
                    Point = ray.Origin + ray.Direction * hitHandler.T,
                    Normal = hitHandler.Normal,
                    Distance = hitHandler.T,
                    Body = hitBody
                };
                return true;
            }
            return false;
        }

        public bool Raycast(Vector3 origin, Vector3 direction, float maxDistance, out RayHit hit)
        {
            hit = default;
            var hitHandler = new RayHitHandler();
            Simulation.RayCast(origin, direction, maxDistance, _bufferPool, ref hitHandler);

            if (hitHandler.HitFound)
            {
                hit = new RayHit
                {
                    T = hitHandler.T,
                    Location = origin + direction * hitHandler.T,
                    Normal = hitHandler.Normal,
                    Collidable = hitHandler.Collidable
                };
                return true;
            }
            return false;
        }

        private TypedIndex CreateShape(in PhysicsBodyDesc desc)
        {
          
            switch (desc.Shape)
            {
                case MeshShape.Sphere:
                    return Simulation.Shapes.Add(new Sphere(desc.Size.X * 0.5f));
                case MeshShape.Capsule:
                    return Simulation.Shapes.Add(new Capsule(desc.Size.X * 0.5f, desc.Size.Y));
                case MeshShape.Cylinder:
                    return Simulation.Shapes.Add(new Cylinder(desc.Size.X * 0.5f, desc.Size.Y));
                case MeshShape.Plane:
                    return Simulation.Shapes.Add(new Box(desc.Size.X, 0.1f, desc.Size.Z));
                case MeshShape.Box:
                default:
                    return Simulation.Shapes.Add(new Box(desc.Size.X, desc.Size.Y, desc.Size.Z));
            }
        }

        private TypedIndex CreateShape(IWorldElement element, ColliderComponent collider)
        {
            switch (collider.Shape)
            {
                case MeshShape.Sphere:
                    return Simulation.Shapes.Add(new Sphere(collider.Width * 0.5f));
                case MeshShape.Capsule:
                    return Simulation.Shapes.Add(new Capsule(collider.Width * 0.5f, collider.Height));
                case MeshShape.Cylinder:
                    return Simulation.Shapes.Add(new Cylinder(collider.Width * 0.5f, collider.Height));
                case MeshShape.Plane:
                    return Simulation.Shapes.Add(new Box(collider.Width, 0.1f, collider.Depth));
                case MeshShape.Custom:
                    var meshRenderable = element.GetComponent<IMeshRenderable>();
                    if (meshRenderable != null)
                    {
                        return CreateMeshShape(meshRenderable);
                    }
                    goto default;
                case MeshShape.Box:
                default:
                    return Simulation.Shapes.Add(new Box(collider.Width, collider.Height, collider.Depth));
            }
        }

        private TypedIndex CreateMeshShape(IMeshRenderable mesh)
        {
            var points = mesh.MeshPoints;
            var indices = mesh.Indices;

            if (points == null || indices == null || indices.Length < 3)
            {
                return Simulation.Shapes.Add(new Box(1f, 1f, 1f));
            }

            int triangleCount = indices.Length / 3;
            _bufferPool.Take<BepuPhysics.Collidables.Triangle>(triangleCount, out var triangles);

            for (int i = 0; i < triangleCount; i++)
            {
                int i0 = (int)indices[i * 3];
                int i1 = (int)indices[i * 3 + 1];
                int i2 = (int)indices[i * 3 + 2];

                triangles[i] = new BepuPhysics.Collidables.Triangle(
                    new Vector3((float)points[i0 * 3], (float)points[i0 * 3 + 1], (float)points[i0 * 3 + 2]),
                    new Vector3((float)points[i1 * 3], (float)points[i1 * 3 + 1], (float)points[i1 * 3 + 2]),
                    new Vector3((float)points[i2 * 3], (float)points[i2 * 3 + 1], (float)points[i2 * 3 + 2])
                );
            }

            var bepuMesh = new BepuPhysics.Collidables.Mesh(triangles, Vector3.One, _bufferPool);
            return Simulation.Shapes.Add(bepuMesh);
        }

        public BodyHandle CreateBodyForElement(IWorldElement element)
        {
            var collider = element.GetComponent<ColliderComponent>();
            TypedIndex shapeIndex;

            if (collider != null)
            {
                shapeIndex = CreateShape(element, collider);
            }
            else
            {
                shapeIndex = Simulation.Shapes.Add(new Box(1f, 2f, 1f));
            }
            
            var lt = element.LocalTransform;
            var pos = lt.Position;
            var rot = lt.Rotation;

            var physicsComp = element.GetComponent<PhysicsBodyComponent>();
            var rigid = element.GetComponent<RigidBodyComponent>();
            bool isKinematic = physicsComp == null || physicsComp.BodyType != PhysicsBodyType.Dynamic;
            float mass = rigid?.Mass ?? 1f;

            var description = isKinematic 
                ? BodyDescription.CreateKinematic(new RigidPose(pos, rot), new CollidableDescription(shapeIndex, 0.1f), new BodyActivityDescription(0.01f))
                : BodyDescription.CreateDynamic(new RigidPose(pos, rot), new BodyInertia { InverseMass = mass > 0f ? 1f / mass : 1f }, new CollidableDescription(shapeIndex, 0.1f), new BodyActivityDescription(0.01f));
            
            return Simulation.Bodies.Add(description);
        }
    }

    internal class BepuPhysicsBody : IPhysicsBody
    {
        public BodyHandle Handle { get; }
        public Simulation Simulation { get; }
        public long Id { get; }
        public bool IsDynamic { get; set; } = true;
        public bool IsCharacterController { get; set; }
        public bool IsOnFloor { get; set; }
        public bool LockRotation { get; set; }
        public float LinearDamping { get; set; }
        public float AngularDamping { get; set; }
        public float GroundProbeDistance { get; set; }

        private readonly CollidableDescription _collidable;
        private readonly float _inverseMass;
        private readonly BodyActivityDescription _activity;

        public BepuPhysicsBody(BodyHandle handle, Simulation simulation, long id,
            in CollidableDescription collidable, float inverseMass, in BodyActivityDescription activity)
        {
            Handle = handle;
            Simulation = simulation;
            Id = id;
            _collidable = collidable;
            _inverseMass = inverseMass;
            _activity = activity;
        }

        public Vector3 Position
        {
            get => Simulation.Bodies[Handle].Pose.Position;
            // Waking on pose/velocity writes is required: bodies sleep after
            // resting (BodyActivityDescription 0.01), and writing a sleeping body's
            // pose/velocity does NOT wake it, so it would never integrate again.
            set { var b = Simulation.Bodies[Handle]; b.Pose.Position = value; b.Awake = true; }
        }

        public Quaternion Rotation
        {
            get => Simulation.Bodies[Handle].Pose.Orientation;
            set { var b = Simulation.Bodies[Handle]; b.Pose.Orientation = value; b.Awake = true; }
        }

        public Vector3 LinearVelocity
        {
            get => Simulation.Bodies[Handle].Velocity.Linear;
            set { var b = Simulation.Bodies[Handle]; b.Velocity.Linear = value; b.Awake = true; }
        }

        public Vector3 AngularVelocity
        {
            get => Simulation.Bodies[Handle].Velocity.Angular;
            set { var b = Simulation.Bodies[Handle]; b.Velocity.Angular = value; b.Awake = true; }
        }

        public void AddForce(Vector3 force)
        {
            var b = Simulation.Bodies[Handle];
            b.ApplyLinearImpulse(force);
            // ApplyLinearImpulse explicitly does not wake the body; do it ourselves
            // so impulses (e.g. jump) are honoured by a resting body.
            b.Awake = true;
        }

        public void AddTorque(Vector3 torque)
        {
            var b = Simulation.Bodies[Handle];
            b.ApplyAngularImpulse(torque);
            b.Awake = true;
        }

        public void SetKinematic(bool kinematic)
        {
            if (kinematic == !IsDynamic) return;

            IsDynamic = !kinematic;
            var pose = new RigidPose(Position, Rotation);
            var description = kinematic
                ? BodyDescription.CreateKinematic(pose, _collidable, _activity)
                : BodyDescription.CreateDynamic(pose, new BodyInertia { InverseMass = _inverseMass }, _collidable, _activity);
            Simulation.Bodies.ApplyDescription(Handle, description);
        }
    }

    public struct RayHit
    {
        public float T;
        public Vector3 Location;
        public Vector3 Normal;
        public CollidableReference Collidable;
    }

    struct RayHitHandler : IRayHitHandler
    {
        public bool HitFound;
        public float T;
        public Vector3 Normal;
        public CollidableReference Collidable;
        /// <summary>Optional body handle to ignore, e.g. the body casting its own ground probe.</summary>
        public BodyHandle? IgnoreHandle;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool AllowTest(CollidableReference collidable)
            => collidable.Mobility == CollidableMobility.Static
               || !IgnoreHandle.HasValue
               || collidable.BodyHandle != IgnoreHandle.Value;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool AllowTest(CollidableReference collidable, int childIndex)
            => collidable.Mobility == CollidableMobility.Static
               || !IgnoreHandle.HasValue
               || collidable.BodyHandle != IgnoreHandle.Value;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void OnRayHit(in RayData ray, ref float maximumT, float t, in Vector3 normal, CollidableReference collidable, int childIndex)
        {
            if (t < maximumT)
            {
                maximumT = t;
                HitFound = true;
                T = t;
                Normal = normal;
                Collidable = collidable;
            }
        }

        public void OnRayHit(in RayData ray, ref float maximumT, float t, Vector3 normal, CollidableReference collidable, int childIndex)
        {
            if (t < maximumT)
            {
                maximumT = t;
                HitFound = true;
                T = t;
                Normal = normal;
                Collidable = collidable;
            }
        }
    }

    public struct NarrowPhaseCallbacks : INarrowPhaseCallbacks
    {
        public void Initialize(Simulation simulation) { }
        public bool AllowContactGeneration(int workerIndex, CollidableReference a, CollidableReference b, ref float speculativeMargin) => true;
        public bool AllowContactGeneration(int workerIndex, CollidablePair pair, int childIndexA, int childIndexB) => true;

        public bool ConfigureContactManifold<TManifold>(int workerIndex, CollidablePair pair, ref TManifold manifold, out PairMaterialProperties pairMaterial) where TManifold : unmanaged, IContactManifold<TManifold>
        {
            pairMaterial = new PairMaterialProperties(1f, 0.1f, new SpringSettings(30, 1));
            return true;
        }

        public bool ConfigureContactManifold(int workerIndex, CollidablePair pair, int childIndexA, int childIndexB, ref ConvexContactManifold manifold)
        {
            return true;
        }
        public void Dispose() { }
    }

    public struct PoseIntegratorCallbacks : IPoseIntegratorCallbacks
    {
        private readonly GravityProvider _provider;
        Vector3Wide gravityWide;

        public readonly AngularIntegrationMode AngularIntegrationMode => (AngularIntegrationMode)0;
        public readonly bool AllowSubsteppingForFocusBodies => false;
        public bool AllowSubstepsForUnconstrainedBodies => false;
        public readonly bool IntegrateVelocityForKinematics => false;

        public PoseIntegratorCallbacks(GravityProvider provider)
        {
            _provider = provider;
            gravityWide = default;
        }

        public void Initialize(Simulation simulation) { }

        public void PrepareForMultithreadedExecution(int workerCount) { }

        public void PrepareForIntegration(float dt)
        {
            Vector3Wide.Broadcast(_provider.Value, out gravityWide);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void IntegrateVelocity(Vector<int> bodyIndices, Vector3Wide position, QuaternionWide orientation, BodyInertiaWide localInertia, Vector<int> integrationMask, int workerIndex, Vector<float> dt, ref BodyVelocityWide velocity)
        {
            Vector3Wide.Scale(gravityWide, dt, out var gravityDelta);
            Vector3Wide.Add(velocity.Linear, gravityDelta, out velocity.Linear);
        }
    }
}
