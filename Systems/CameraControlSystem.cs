using System;
using System.Numerics;
using V12.Core.Core.Interfaces;
using V12.Core.Input;
using V12.Basic.Components;
using V12.Core.Interfaces.Renderer;

namespace V12.Core.Systems
{
    public class CameraControlSystem : IGameService, IInputHandler
    {
        private readonly GameRoot _gameRoot;
        private InputService? _input;
        private IWorldElement element;
        private float _lookX;
        private float _lookY;

        private const float MaxPitch = MathF.PI / 2f - 0.05f;
        private const float MinPitch = -MathF.PI / 2f + 0.05f;

        public CameraControlSystem(GameRoot gameRoot)
        {
            _gameRoot = gameRoot;
        }

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

        public void Update(float deltaTime)
        {
            // Player lives in PersistentWorld — search there first, fall back to SelectedWorld
            var player = _gameRoot.PersistentWorld.FindElementWithComponent<PlayerComponent>();
            if (player == null && _gameRoot.SelectedWorld != null)
                player = _gameRoot.SelectedWorld.FindElementWithComponent<PlayerComponent>();
            if (player == null)
                return;
            var cameraElement = player.FindChildByNameRecursive("PlayerCamera3D");
            if (cameraElement == null)
                return;
            element = player;
            var cam = cameraElement.GetComponent<CameraComponent>();
            if (cam == null)
                return;

            var loco = player.GetComponent<LocomotionComponent>();
            float sensitivity = loco?.LookSensitivity ?? 1.2f;

            var lt = cameraElement.LocalTransform;
            var (yaw, pitch, _) = ToEulerAngles(lt.Rotation);

            if (MathF.Abs(_lookX) > 0.001f)
                yaw += _lookX * sensitivity * deltaTime;

            if (MathF.Abs(_lookY) > 0.001f)
                pitch = Math.Clamp(pitch + _lookY * sensitivity * deltaTime, MinPitch, MaxPitch);

            lt.Rotation = Quaternion.CreateFromYawPitchRoll(yaw, pitch, 0);
            cameraElement.LocalTransform = lt;
        }

        private static (float yaw, float pitch, float roll) ToEulerAngles(Quaternion q)
        {
            float siny_cosp = 2 * (q.W * q.Y + q.Z * q.X);
            float cosy_cosp = 1 - 2 * (q.Y * q.Y + q.Z * q.Z);
            float yaw = MathF.Atan2(siny_cosp, cosy_cosp);
            float sinp = 2 * (q.W * q.X - q.Y * q.Z);
            float pitch = Math.Abs(sinp) >= 1 ? MathF.CopySign(MathF.PI / 2, sinp) : MathF.Asin(sinp);
            float sinr_cosp = 2 * (q.W * q.Z + q.X * q.Y);
            float cosr_cosp = 1 - 2 * (q.X * q.X + q.Z * q.Z);
            float roll = MathF.Atan2(sinr_cosp, cosr_cosp);
            return (yaw, pitch, roll);
        }
    }
}
