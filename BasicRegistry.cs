using System.Numerics;
using V12.Core;

namespace V12.Basic
{
    public class BasicRegistry
    {
        public static void RegisterAll(GameRoot gameRoot)
        {
            var physics = new V12.Core.Systems.PhysicsService();
            gameRoot.Registry.Register("LocomotionSystem", new V12.Core.Systems.LocomotionSystem(gameRoot));
            gameRoot.Registry.Register("PhysicsLocomotionSystem", new V12.Core.Systems.PhysicsLocomotionSystem(gameRoot));
            gameRoot.Registry.Register("PhysicsService", physics);
            gameRoot.Registry.Register("ScriptSystem", new V12.Core.Systems.ScriptSystem(gameRoot));
        }
    }
}
