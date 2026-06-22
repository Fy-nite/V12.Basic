using System;
using System.Numerics;
using BepuPhysics;
using V12.Basic.Components;
using V12.Core.Core.Interfaces;
using V12.Core.Interfaces.Renderer;

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
            var world = _gameRoot.SelectedWorld;
            if (world == null || _physicsService == null) 
                return;

            foreach (var element in world.Root)
            {
                var bodyComp = element.GetComponent<PhysicsBodyComponent>();
                var loco = element.GetComponent<LocomotionComponent>();
                
                if (bodyComp != null)
                {
                    if (bodyComp.BodyHandle.Value == 0)
                    {
                        bodyComp.BodyHandle = _physicsService.CreateBodyForElement(element);
                        Console.WriteLine($"Initialized body for {element.Name}: {bodyComp.BodyHandle.Value}");
                    }
                    
                    var bodyReference = _physicsService.Simulation.Bodies[bodyComp.BodyHandle];

                    if (bodyComp.IsKinematic)
                    {
                        var lt = element.LocalTransform;
                        bodyReference.Pose.Position = lt.Position;
                        bodyReference.Pose.Orientation = lt.Rotation;
                    }
                    else
                    {
                        if (loco != null)
                        {
                            bodyReference.Velocity.Linear = loco.Velocity;
                        }

                        var lt = element.LocalTransform;
                        lt.Position = bodyReference.Pose.Position;
                        lt.Rotation = bodyReference.Pose.Orientation;
                        element.LocalTransform = lt;
                    }
                }
            }
        }
    }
}
