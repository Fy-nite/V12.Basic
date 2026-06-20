using Microsoft.Win32;
using System.Numerics;
using V12.Core;

namespace V12.Basic
{
    public class BasicRegistry
    {
        /// <summary>
        /// Registers all the systems for the basic template. This is called by the GameRoot when it initializes, and is where you should add any custom systems you create.
        /// </summary>
        /// <param name="gameRoot"></param>
        public static void RegisterAll(GameRoot gameRoot)
        {
            gameRoot.Registry.Register("LocomotionSystem", new V12.Core.Systems.LocomotionSystem(gameRoot));
            gameRoot.Registry.Register("CameraControlSystem", new V12.Core.Systems.CameraControlSystem(gameRoot));
            gameRoot.Registry.Register("PhysicsLocomotionSystem", new V12.Core.Systems.PhysicsLocomotionSystem(gameRoot));
            gameRoot.Registry.Register("PhysicsService", new V12.Core.Systems.PhysicsService());
        }
    }
}
