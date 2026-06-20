using System;
using System.Numerics;
using BepuPhysics;
using V12.Basic.Components;
using V12.Components;
using V12.Core.Core.Interfaces;

namespace V12.Core.Systems
{
    public class PhysicsLocomotionSystem : IGameService
    {
        private GameRoot _gameRoot;
        private PhysicsService? _physicsService;

        public PhysicsLocomotionSystem(GameRoot gameRoot)
        {
            _gameRoot = gameRoot;
        }

        public void Initialize(GameRoot g)
        {
            _physicsService = g.Registry.Get<PhysicsService>();
        }
        public void Update(GameRoot g) {}
        public void Update(float deltaTime)
        {
            //Console.WriteLine("PhysicsLocomotionSystem Updating...");
            var world = _gameRoot.SelectedWorld;
            if (world == null || _physicsService == null) 
            {
                //Console.WriteLine($"Skipping update: world={world != null}, physicsService={_physicsService != null}");
                return;
            }

            foreach (var element in world.Root)
            {
                var bodyComp = element.GetComponent<PhysicsBodyComponent>();
                var transform = element.GetComponent<TransformComponent>();
                var loco = element.GetComponent<LocomotionComponent>();
                
                if (bodyComp != null)
                {
                    // Initialize body if not yet registered
                    if (bodyComp.BodyHandle.Value == 0)
                    {
                        bodyComp.BodyHandle = _physicsService.CreateBodyForElement(element);
                        Console.WriteLine($"Initialized body for {element.Name}: {bodyComp.BodyHandle.Value}");
                    }
                    
                    if (transform != null)
                    {
                        var bodyReference = _physicsService.Simulation.Bodies[bodyComp.BodyHandle];

                        if (bodyComp.IsKinematic)
                        {
                            // Sync transform -> physics body
                            bodyReference.Pose.Position = new System.Numerics.Vector3(transform.X, transform.Y, transform.Z);
                            bodyReference.Pose.Orientation = Quaternion.CreateFromYawPitchRoll(transform.RY, transform.RX, transform.RZ);
                        }
                        else
                        {
                            // Sync LocomotionComponent velocity to physics body
                            if (loco != null)
                            {
                                bodyReference.Velocity.Linear = new System.Numerics.Vector3(loco.Velocity.X, loco.Velocity.Y, loco.Velocity.Z);
                            }
                            
                            // Sync physics body -> transform
                            transform.X = bodyReference.Pose.Position.X;
                            transform.Y = bodyReference.Pose.Position.Y;
                            transform.Z = bodyReference.Pose.Position.Z;

                            // Sync orientation
                            var q = bodyReference.Pose.Orientation;
                            (float yaw, float pitch, float roll) = ToEulerAngles(q);
                            transform.RY = yaw;
                            transform.RX = pitch;
                            transform.RZ = roll;
                        }
                    }
                }
            }
        }

        private (float yaw, float pitch, float roll) ToEulerAngles(Quaternion q)
        {
            // Yaw (y-axis rotation)
            float siny_cosp = 2 * (q.W * q.Y + q.Z * q.X);
            float cosy_cosp = 1 - 2 * (q.Y * q.Y + q.Z * q.Z);
            float yaw = MathF.Atan2(siny_cosp, cosy_cosp);

            // Pitch (x-axis rotation)
            float sinp = 2 * (q.W * q.X - q.Y * q.Z);
            float pitch = Math.Abs(sinp) >= 1 ? MathF.CopySign(MathF.PI / 2, sinp) : MathF.Asin(sinp);

            // Roll (z-axis rotation)
            float sinr_cosp = 2 * (q.W * q.Z + q.X * q.Y);
            float cosr_cosp = 1 - 2 * (q.X * q.X + q.Z * q.Z);
            float roll = MathF.Atan2(sinr_cosp, cosr_cosp);

            return (yaw, pitch, roll);
        }
    }
}
