using System.Collections.Generic;
using System.Numerics;
using V12.Core.Core.Interfaces;
using V12.Core.Input;
using System;
using V12.Basic.Components;
using V12.Core.Interfaces.Renderer;

namespace V12.Core.Systems
{
    public class LocomotionSystem : IGameService, IInputHandler
    {
        private readonly GameRoot _gameRoot;
        private InputService? _input;

        private float _moveX, _moveY;
        private volatile bool _jumpRequested;
        private bool _isSprinting;

        public LocomotionSystem(GameRoot gameRoot)
        {
            _gameRoot = gameRoot;
        }

        public void Update(GameRoot g) {}
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
            if (evt.Type == InputEventType.Axis)
            {
                if (evt.Name == "move_right") { _moveX = (float)evt.Value; }
                if (evt.Name == "move_left" && (float)evt.Value > 0f) { _moveX = -(float)evt.Value; }
                if (evt.Name == "move_forward") { _moveY = -(float)evt.Value; }
                if (evt.Name == "move_backward" && (float)evt.Value > 0f) { _moveY = (float)evt.Value; }
            }

            if (evt.Type == InputEventType.ButtonDown && evt.Name == "run")
                _isSprinting = true;
            if (evt.Type == InputEventType.ButtonUp && evt.Name == "run")
                _isSprinting = false;

            if (evt.Type == InputEventType.ButtonDown && evt.Name == "jump")
                _jumpRequested = true;
        }

        public void Update(float deltaTime)
        {
            // Process all active worlds via the ECS-style query API
            foreach (var world in _gameRoot.ActiveWorlds)
                ProcessWorld(world, deltaTime);

            _jumpRequested = false;
        }

        private void ProcessWorld(World world, float deltaTime)
        {
            
            foreach (var element in world.Root)
            {
                if (element == null) continue;
                var loco = element.GetComponent<LocomotionComponent>();
                if (loco == null) continue;

                var lt = element.LocalTransform;
                Vector3 pos = lt.Position;
                var (yaw, pitch, _) = ToEulerAngles(lt.Rotation);

                float speed = loco.MoveSpeed;
                if (_isSprinting)
                    speed *= loco.SprintMultiplier;

                Vector3 forward = Vector3.UnitZ;
                Vector3 right = Vector3.UnitX;
                var bodyComp = element.GetComponent<PhysicsBodyComponent>();
                var rotationMatrix = bodyComp?.Body != null
                    ? Matrix4x4.CreateFromYawPitchRoll(yaw, 0, 0)
                    : Matrix4x4.CreateFromYawPitchRoll(yaw, pitch, 0);
                forward = Vector3.Transform(Vector3.UnitZ, rotationMatrix);
                right = Vector3.Transform(Vector3.UnitX, rotationMatrix);

                Vector3 moveDir = (forward * _moveY + right * _moveX) * speed;

                var bodyComponent = element.GetComponent<PhysicsBodyComponent>();
                if (bodyComponent != null)
                {
                    Vector3 vel = loco.Velocity;
                    vel.X = moveDir.X;
                    vel.Z = moveDir.Z;
                    loco.Velocity = vel;

                    if (_jumpRequested && loco.IsGrounded && loco.CanJump)
                    {
                        loco.Velocity = new Vector3(loco.Velocity.X, loco.JumpStrength, loco.Velocity.Z);
                    }
                }
                else
                {
                    loco.Velocity = moveDir;
                    pos += loco.Velocity * deltaTime;
                    lt.Position = pos;
                    element.LocalTransform = lt;
                }
            }
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
