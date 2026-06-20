using System.Collections.Generic;
using System.Numerics;
using V12.Core.Core.Interfaces;
using V12.Components;
using V12.Core.Input;
using System;
using V12.Basic.Components;

namespace V12.Core.Systems
{
    /// <summary>
    /// Handles entity movement from V12 movement input events.
    /// Camera rotation is handled separately by <see cref="CameraControlSystem"/>.
    /// Supports analog input magnitudes (0..1) for smooth movement.
    /// </summary>
    public class LocomotionSystem : IGameService, IInputHandler
    {
        private readonly GameRoot _gameRoot;
        private InputService? _input;

        // Input state tracked by handler (analog-ready, range -1..1)
        private float _moveX, _moveY;
        private bool _jumpRequested;
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
                // Movement: combine directional pairs into single axis values
                if (evt.Name == "move_right") { _moveX = (float)evt.Value; }
                if (evt.Name == "move_left") { _moveX = -(float)evt.Value; }
                if (evt.Name == "move_forward") { _moveY = -(float)evt.Value; }
                if (evt.Name == "move_backward") { _moveY = (float)evt.Value; }
            }

            // Sprint toggle
            if (evt.Type == InputEventType.ButtonDown && evt.Name == "run")
                _isSprinting = true;
            if (evt.Type == InputEventType.ButtonUp && evt.Name == "run")
                _isSprinting = false;

            // Jump
            if (evt.Type == InputEventType.ButtonDown && evt.Name == "jump")
                _jumpRequested = true;
        }

        public void Update(float deltaTime)
        {
            var world = _gameRoot.SelectedWorld;
            if (world == null) return;
            
            var vrInput = _gameRoot.Registry.Get<IVRInputProvider>();

            foreach (var element in world.Root)
            {
                if (element == null) continue;
                var loco = element.GetComponent<LocomotionComponent>();
                var transform = element.GetComponent<TransformComponent>();

                if (loco == null || transform == null) continue;

                // Calculate speed (with sprint multiplier)
                float speed = loco.MoveSpeed;
                if (_isSprinting)
                    speed *= loco.SprintMultiplier;

                // Move relative to head or camera orientation
                Vector3 forward = Vector3.UnitZ;
                Vector3 right = Vector3.UnitX;
                if (vrInput != null)
                {
                    forward = Vector3.Transform(Vector3.UnitZ, vrInput.HeadOrientation);
                    forward.Y = 0;
                    forward = Vector3.Normalize(forward);
                    right = Vector3.Transform(Vector3.UnitX, vrInput.HeadOrientation);
                    right.Y = 0;
                    right = Vector3.Normalize(right);
                }
                else
                {
                    // Rotate movement vectors by the entity's own yaw (horizontal only for grounded,
                    // full yaw+pitch for free-fly)
                    var bodyComp = element.GetComponent<PhysicsBodyComponent>();
                    if (bodyComp != null)
                    {
                        // Grounded: only yaw affects direction, movement stays on XZ plane
                        var rotationMatrix = Matrix4x4.CreateFromYawPitchRoll(transform.RY, 0, 0);
                        forward = Vector3.Transform(Vector3.UnitZ, rotationMatrix);
                        right = Vector3.Transform(Vector3.UnitX, rotationMatrix);
                    }
                    else
                    {
                        // Free-fly: yaw + pitch for full 3D movement (e.g. noclip camera)
                        var rotationMatrix = Matrix4x4.CreateFromYawPitchRoll(transform.RY, transform.RX, 0);
                        forward = Vector3.Transform(Vector3.UnitZ, rotationMatrix);
                        right = Vector3.Transform(Vector3.UnitX, rotationMatrix);
                    }
                }

                Vector3 moveDir = (forward * _moveY + right * _moveX) * speed;

                var bodyComponent = element.GetComponent<PhysicsBodyComponent>();
                if (bodyComponent != null)
                {
                    // Physics body: set horizontal velocity, preserve vertical (gravity)
                    loco.Velocity = new Vector3(moveDir.X, loco.Velocity.Y, moveDir.Z);

                    // Jump
                    if (_jumpRequested && loco.IsGrounded && loco.CanJump)
                    {
                        loco.Velocity = new Vector3(loco.Velocity.X, loco.JumpStrength, loco.Velocity.Z);
                    }
                }
                else
                {
                    // Kinematic/noclip free-fly (e.g. camera) moves in all three dimensions
                    loco.Velocity = moveDir;

                    // Apply velocity to position directly
                    transform.X += loco.Velocity.X * deltaTime;
                    transform.Y += loco.Velocity.Y * deltaTime;
                    transform.Z += loco.Velocity.Z * deltaTime;
                }
            }

            _jumpRequested = false;
        }
    }
}
