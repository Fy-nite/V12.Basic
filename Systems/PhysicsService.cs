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
    public class PhysicsService : IGameService, IPhysicsBackend
    {
        public Simulation? Simulation { get; private set; }
        private BufferPool? _bufferPool;
        private ThreadDispatcher? _threadDispatcher;

        private readonly Dictionary<long, BepuPhysicsBody> _bodyMap = new();
        private long _nextBodyId;

        public void Initialize(GameRoot g)
        {
            _bufferPool = new BufferPool();
            _threadDispatcher = new ThreadDispatcher(Environment.ProcessorCount); 
            
            Simulation = Simulation.Create(_bufferPool, new NarrowPhaseCallbacks(), new PoseIntegratorCallbacks(new Vector3(0, -9.81f, 0)), new SolveDescription(8, 1));
        }

        public void Update(GameRoot g) {}
        public void Update(float deltaTime)
        {
            if (deltaTime <= 0) return;
            if (Simulation == null) return;
            Simulation.Timestep(deltaTime, _threadDispatcher);
        }

        void IPhysicsBackend.Step(float deltaTime)
        {
            Update(deltaTime);
        }

        IPhysicsBody IPhysicsBackend.CreateBody(in PhysicsBodyDesc desc)
        {
            TypedIndex shapeIndex = CreateShape(desc);

            var description = desc.IsKinematic
                ? BodyDescription.CreateKinematic(new RigidPose(desc.Position, desc.Rotation), new CollidableDescription(shapeIndex, 0.1f), new BodyActivityDescription(0.01f))
                : BodyDescription.CreateDynamic(new RigidPose(desc.Position, desc.Rotation), new BodyInertia { InverseMass = desc.Mass > 0 ? 1f / desc.Mass : 1f }, new CollidableDescription(shapeIndex, 0.1f), new BodyActivityDescription(0.01f));

            var handle = Simulation.Bodies.Add(description);
            long id = _nextBodyId++;
            var body = new BepuPhysicsBody(handle, Simulation, id);
            _bodyMap[id] = body;
            return body;
        }

        void IPhysicsBackend.DestroyBody(IPhysicsBody body)
        {
            if (body is BepuPhysicsBody b)
            {
                _bodyMap.Remove(b.Id);
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
                hit = new RaycastHit
                {
                    Point = ray.Origin + ray.Direction * hitHandler.T,
                    Normal = hitHandler.Normal,
                    Distance = hitHandler.T,
                    Body = null
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
            bool isKinematic = physicsComp != null ? physicsComp.IsKinematic : true;

            var description = isKinematic 
                ? BodyDescription.CreateKinematic(new RigidPose(pos, rot), new CollidableDescription(shapeIndex, 0.1f), new BodyActivityDescription(0.01f))
                : BodyDescription.CreateDynamic(new RigidPose(pos, rot), new BodyInertia { InverseMass = 1f }, new CollidableDescription(shapeIndex, 0.1f), new BodyActivityDescription(0.01f));
            
            return Simulation.Bodies.Add(description);
        }
    }

    internal class BepuPhysicsBody : IPhysicsBody
    {
        public BodyHandle Handle { get; }
        public Simulation Simulation { get; }
        public long Id { get; }

        public BepuPhysicsBody(BodyHandle handle, Simulation simulation, long id)
        {
            Handle = handle;
            Simulation = simulation;
            Id = id;
        }

        public Vector3 Position
        {
            get => Simulation.Bodies[Handle].Pose.Position;
            set { var b = Simulation.Bodies[Handle]; b.Pose.Position = value; }
        }

        public Quaternion Rotation
        {
            get => Simulation.Bodies[Handle].Pose.Orientation;
            set { var b = Simulation.Bodies[Handle]; b.Pose.Orientation = value; }
        }

        public Vector3 LinearVelocity
        {
            get => Simulation.Bodies[Handle].Velocity.Linear;
            set { var b = Simulation.Bodies[Handle]; b.Velocity.Linear = value; }
        }

        public void AddForce(Vector3 force)
        {
            var b = Simulation.Bodies[Handle];
            b.ApplyLinearImpulse(force);
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool AllowTest(CollidableReference collidable) => true;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool AllowTest(CollidableReference collidable, int childIndex) => true;

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
        public Vector3 Gravity;
        Vector3Wide gravityWide;

        public readonly AngularIntegrationMode AngularIntegrationMode => (AngularIntegrationMode)0;
        public readonly bool AllowSubsteppingForFocusBodies => false;
        public bool AllowSubstepsForUnconstrainedBodies => false;
        public readonly bool IntegrateVelocityForKinematics => false;

        public PoseIntegratorCallbacks(Vector3 gravity)
        {
            Gravity = gravity;
            gravityWide = default;
        }

        public void Initialize(Simulation simulation)
        {
            Vector3Wide.Broadcast(Gravity, out gravityWide);
        }

        public void PrepareForMultithreadedExecution(int workerCount) { }

        public void PrepareForIntegration(float dt) { }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void IntegrateVelocity(Vector<int> bodyIndices, Vector3Wide position, QuaternionWide orientation, BodyInertiaWide localInertia, Vector<int> integrationMask, int workerIndex, Vector<float> dt, ref BodyVelocityWide velocity)
        {
            Vector3Wide.Scale(gravityWide, dt, out var gravityDelta);
            Vector3Wide.Add(velocity.Linear, gravityDelta, out velocity.Linear);
        }
    }
}
