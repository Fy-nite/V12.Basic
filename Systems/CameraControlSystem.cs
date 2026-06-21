using System;
using System.Numerics;
using V12.Core.Core.Interfaces;
using V12.Components;
using V12.Core.Input;
using V12.Basic.Components;

namespace V12.Core.Systems
{
    /// <summary>
    /// Handles camera rotation from V12 look input events.
    /// Operates on any element that has both a CameraComponent and a TransformComponent.
    /// Supports analog stick input (smooth, magnitude-scaled rotation) and digital
    /// keyboard input (binary 0/1 values) through the same event names.
    /// </summary>
    public class CameraControlSystem : IGameService, IInputHandler
    {
        private readonly GameRoot _gameRoot;
        private InputService? _input;
        private IWorldElement element;
        // Accumulated look input (supports analog range -1..1)
        private float _lookX;  // yaw:   positive = look right
        private float _lookY;  // pitch: positive = look up

        // Pitch clamping to prevent camera flipping
        private const float MaxPitch = MathF.PI / 2f - 0.05f; //  ~89 degrees
        private const float MinPitch = -MathF.PI / 2f + 0.05f;  // ~-89 degrees

        public CameraControlSystem(GameRoot gameRoot)
        {
            _gameRoot = gameRoot;
        }

        // ── IGameService ─────────────────────────────────────────────────────

        public void Update(GameRoot g) { }

        public void Initialize()
        {
            _input = _gameRoot.Registry.Get<InputService>();
            _input?.RegisterHandler(this);
        }

        public void Initialize(GameRoot g)
        {
            _input = g.Registry.Get<InputService>();
            _input?.RegisterHandler(this);
        }

        // ── IInputHandler ────────────────────────────────────────────────────

        public void OnInputEvent(InputEvent evt)
        {
            if (evt.Type != InputEventType.Axis)
                return;
            switch (evt.Name)
            {
                case "look_right": _lookX = (float)evt.Value; break;
                case "look_left": _lookX = -(float)evt.Value; break;
                case "look_up": _lookY = (float)evt.Value; break;
                case "look_down": _lookY = -(float)evt.Value; break;
            }
        }

        // ── Update ───────────────────────────────────────────────────────────

        public void Update(float deltaTime)
        {
            var world = _gameRoot.SelectedWorld;
            if (world == null) return;
            var player = world.FindElementWithComponent<PlayerComponent>();
            if (player == null)
                return;
            var cameraElement = player.FindChildByNameRecursive("PlayerCamera3D");
            if (cameraElement == null)
                return;
            element = player;
            var cam = cameraElement.GetComponent<CameraComponent>();
            var transform = cameraElement.GetComponent<TransformComponent>();
            if (cam == null || transform == null)
                return;

            var loco = player.GetComponent<LocomotionComponent>();
            float sensitivity = loco?.LookSensitivity ?? 1.2f;

            // Apply yaw (rotate around Y axis)
            if (MathF.Abs(_lookX) > 0.001f)
            {
                transform.RY += _lookX * sensitivity * deltaTime;
            }

            // Apply pitch (rotate around X axis) with clamping
            if (MathF.Abs(_lookY) > 0.001f)
            {
                float newPitch = transform.RX + _lookY * sensitivity * deltaTime;
                transform.RX = Math.Clamp(newPitch, MinPitch, MaxPitch);
            }

        }
    }
}
