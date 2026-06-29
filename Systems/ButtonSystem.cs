using System;
using System.Collections.Generic;
using System.Numerics;
using V12.Basic.Components;
using V12.Components;
using V12.Core;
using V12.Core.Core.Interfaces;
using V12.Core.Input;
using V12.Core.Interfaces.Physics;

namespace V12.Core.Systems
{
    public class ButtonSystem : IGameService, IInputHandler
    {
        private readonly GameRoot _gameRoot;
        private IPhysicsBackend _physics;
        private bool _initialized;
        private bool _interactDown;
        private bool _interactPrevious;

        private const float RayLength = 20f;

        public ButtonSystem(GameRoot gameRoot)
        {
            _gameRoot = gameRoot;
        }

        public void Initialize(GameRoot g) { }
        public void Initialize() { }
        public void Update(GameRoot g) { }

        private void EnsureInitialized()
        {
            if (_initialized) return;
            _initialized = true;
            _physics = _gameRoot.Registry.Get<IPhysicsBackend>();
            var input = _gameRoot.Registry.Get<InputService>();
            input?.RegisterHandler(this);
        }

        public void OnInputEvent(InputEvent evt)
        {
            if (evt.Name == "interact")
            {
                if (evt.Type == InputEventType.ButtonDown)
                    _interactDown = true;
            }
        }

        public void Update(float deltaTime)
        {
            EnsureInitialized();
            var world = _gameRoot.SelectedWorld;
            if (world == null || _physics == null) return;

            var player = FindPlayer(world);
            if (player == null) return;

            var playerComp = player.GetComponent<PlayerComponent>();
            if (playerComp == null) return;

            var aimRay = playerComp.GetAimRay();
            var ray = new Ray { Origin = aimRay.origin, Direction = aimRay.direction, MaxDistance = RayLength };

            bool didHit = _physics.Raycast(ray, out var hit);
            IWorldElement hitElement = null;
            ButtonComponent button = null;

            if (didHit && hit.Body != null)
            {
                hitElement = FindElementByBody(world, hit.Body);
                if (hitElement != null)
                    button = hitElement.GetComponent<ButtonComponent>();
            }

            // Press on rising edge of interact while pointing at a button
            if (_interactDown && !_interactPrevious && button != null)
            {
                button.Pressed = true;
                Console.WriteLine($"[Button] \"{button.Label}\" pressed");
                button.OnPressed?.Invoke();
            }
            else if (!_interactDown)
            {
                // Reset pressed state when interact is released
                if (button != null)
                    button.Pressed = false;
            }

            _interactPrevious = _interactDown;
            _interactDown = false;
        }

        private static IWorldElement FindPlayer(World world)
        {
            foreach (var e in world.Root)
            {
                var pc = e.GetComponent<PlayerComponent>();
                if (pc != null)
                    return e;
            }
            return null;
        }

        private static IWorldElement FindElementByBody(World world, IPhysicsBody body)
        {
            foreach (var e in AllElements(world.Root))
            {
                var pbc = e.GetComponent<PhysicsBodyComponent>();
                if (pbc?.Body == body)
                    return e;
            }
            return null;
        }

        private static IEnumerable<IWorldElement> AllElements(IEnumerable<IWorldElement> elements)
        {
            foreach (var e in elements)
            {
                yield return e;
                foreach (var child in AllElements(e.Children))
                    yield return child;
            }
        }
    }
}
