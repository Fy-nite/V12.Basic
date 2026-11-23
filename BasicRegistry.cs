using System.Numerics;
using V12.Core;

namespace V12.Basic
{
    public class BasicRegistry
    {
        public static void RegisterAll(GameRoot gameRoot)
        {
            // Registry order is update order. Gravity refreshes before the
            // physics service steps, whether AutoStep is on or PhysicsLocomotion
            // owns the step.
            V12.Gravity.Systems.GravityRegistry.RegisterAll(gameRoot);

            var physics = new V12.Physics.PhysicsService();
            gameRoot.Registry.Register("PhysicsService", physics);

            gameRoot.Registry.Register("LocomotionSystem", new V12.Core.Systems.LocomotionSystem(gameRoot));
            gameRoot.Registry.Register("PhysicsLocomotionSystem", new V12.Core.Systems.PhysicsLocomotionSystem(gameRoot));
            gameRoot.Registry.Register("ScriptSystem", new V12.Core.Systems.ScriptSystem(gameRoot));
            gameRoot.Registry.Register("PickupSystem", new V12.Core.Systems.PickupSystem(gameRoot));
            gameRoot.Registry.Register("ButtonSystem", new V12.Core.Systems.ButtonSystem(gameRoot));
            if (gameRoot.Registry.Get(nameof(V12.UI.IUIProvider)) == null)
                gameRoot.Registry.Register(nameof(V12.UI.IUIProvider), new V12.Basic.UI.PaperUIProvider(gameRoot));
        }
    }
}
