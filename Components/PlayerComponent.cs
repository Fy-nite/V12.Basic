using System;
using System.Numerics;
using V12.Components;
using V12.Core;
using V12.Core.Core.Interfaces;
using V12.Core.Input;
using V12.Core.Systems;

namespace V12.Basic.Components
{
    public class PlayerComponent : ComponentBase, IPlayerControlComponent
    {
        private bool         _isLocalControlled    = true;
        private InputMethods _preferredInputMethod = InputMethods.Auto;
        private IWorldElement _camera;
        // ── Shared movement settings ──────────────────────────────────────────
        private float _moveSpeed        = 4f;
        private float _sprintMultiplier = 1.9f;
        private float _jumpStrength     = 6f;

        // ── Desktop-specific settings ─────────────────────────────────────────
        private float _lookSensitivity  = 1.2f;
        private bool  _canJump          = true;

        // ── VR-specific settings ──────────────────────────────────────────────
        private bool  _enableHandTracking   = true;
        private float _vrMoveSpeed          = 3f;
        private bool  _vrSmoothLocomotion   = true;

        // ── Fly mode ─────────────────────────────────────────────────────────────
        private bool _isFlying;
        private float _flySpeed = 8f;

        // ── Input state ──────────────────────────────────────────────────────────
        private InputService? _input;
        private InputActionMap _actions;

        private const float MaxPitch = MathF.PI / 2f - 0.05f;
        private const float MinPitch = -MathF.PI / 2f + 0.05f;

        // ─────────────────────────────────────────────────────────────────────

        public override string Name        => "Player";
        public override string Description => "Unified VR/desktop player settings";

        // ── IPlayerControlComponent ───────────────────────────────────────────

        public InputMethods RequiredInputMethod => InputMethods.Auto;

        public bool IsLocalControlled
        {
            get => _isLocalControlled;
            set { if (_isLocalControlled != value) { _isLocalControlled = value; MarkDirty(); } }
        }

        // ── Mode preference ───────────────────────────────────────────────────

        public InputMethods PreferredInputMethod
        {
            get => _preferredInputMethod;
            set { if (_preferredInputMethod != value) { _preferredInputMethod = value; MarkDirty(); SyncLocomotion(); } }
        }

        // ── Shared ────────────────────────────────────────────────────────────

        public float MoveSpeed
        {
            get => _moveSpeed;
            set { if (Math.Abs(_moveSpeed - value) > 0.0001f) { _moveSpeed = MathF.Max(0f, value); MarkDirty(); SyncLocomotion(); } }
        }

        public float SprintMultiplier
        {
            get => _sprintMultiplier;
            set { if (Math.Abs(_sprintMultiplier - value) > 0.0001f) { _sprintMultiplier = MathF.Max(1f, value); MarkDirty(); SyncLocomotion(); } }
        }

        public float JumpStrength
        {
            get => _jumpStrength;
            set { if (Math.Abs(_jumpStrength - value) > 0.0001f) { _jumpStrength = MathF.Max(0f, value); MarkDirty(); SyncLocomotion(); } }
        }

        // ── Desktop ───────────────────────────────────────────────────────────

        public float LookSensitivity
        {
            get => _lookSensitivity;
            set { if (Math.Abs(_lookSensitivity - value) > 0.0001f) { _lookSensitivity = MathF.Max(0f, value); MarkDirty(); SyncLocomotion(); } }
        }

        public bool CanJump
        {
            get => _canJump;
            set { if (_canJump != value) { _canJump = value; MarkDirty(); SyncLocomotion(); } }
        }

        // ── VR ────────────────────────────────────────────────────────────────

        public bool EnableHandTracking
        {
            get => _enableHandTracking;
            set { if (_enableHandTracking != value) { _enableHandTracking = value; MarkDirty(); SyncLocomotion(); } }
        }

        public float VRMoveSpeed
        {
            get => _vrMoveSpeed;
            set { if (Math.Abs(_vrMoveSpeed - value) > 0.0001f) { _vrMoveSpeed = MathF.Max(0f, value); MarkDirty(); SyncLocomotion(); } }
        }

        public bool VRSmoothLocomotion
        {
            get => _vrSmoothLocomotion;
            set { if (_vrSmoothLocomotion != value) { _vrSmoothLocomotion = value; MarkDirty(); SyncLocomotion(); } }
        }

        // ── Fly mode ─────────────────────────────────────────────────────────────

        public bool IsFlying
        {
            get => _isFlying;
            set { if (_isFlying != value) { _isFlying = value; MarkDirty(); } }
        }

        public float FlySpeed
        {
            get => _flySpeed;
            set { if (Math.Abs(_flySpeed - value) > 0.0001f) { _flySpeed = MathF.Max(0f, value); MarkDirty(); } }
        }

        private PhysicsBodyComponent _physicsBody;
        private LocomotionComponent _locomotion;

        // ── Constructors ──────────────────────────────────────────────────────

        public PlayerComponent() { }

        public PlayerComponent(
            InputMethods preferredMethod  = InputMethods.Auto,
            float        moveSpeed        = 4f,
            float        sprintMultiplier = 1.9f,
            bool         isLocalControlled = true)
        {
            _preferredInputMethod = preferredMethod;
            _moveSpeed            = MathF.Max(0f, moveSpeed);
            _sprintMultiplier     = MathF.Max(1f, sprintMultiplier);
            _isLocalControlled    = isLocalControlled;
        }

        // ── Lifecycle ──────────────────────────────────────────────────────────

        public override void OnAttach(IWorldElement worldElement)
        {
            base.OnAttach(worldElement);

            _input = GameRoot.Instance.Registry.Get<InputService>();

            _actions = new InputActionMap { Name = "PlayerActions" };
            _actions.BindVector2("Look", "look_right", "look_left", "look_up", "look_down");
            _actions.BindVector2("Move", "move_right", "move_left", "move_backward", "move_forward");
            _actions.BindButton("Jump", "jump");
            _actions.BindButton("Sprint", "run");
            _actions.BindButton("FlyToggle", "fly_toggle");
            _actions.BindAxis("FlyUp", "fly_up", "");
            _actions.BindAxis("FlyDown", "fly_down", "");
            _actions.Attach(_input);

            _physicsBody = worldElement.GetComponent<PhysicsBodyComponent>();
            if (_physicsBody == null)
            {
                _physicsBody = new PhysicsBodyComponent
                {
                    Active = true,
                    IsKinematic = false
                };
                worldElement.AddComponent(_physicsBody);
            }

            _locomotion = worldElement.GetComponent<LocomotionComponent>();
            if (_locomotion == null)
            {
                _locomotion = new LocomotionComponent
                {
                    Active = true
                };
                worldElement.AddComponent(_locomotion);
            }
            if (worldElement.GetComponent<TransformComponent>() == null)
                worldElement.AddComponent(new TransformComponent { Active = true });

            _camera = worldElement.FindChildByName("PlayerCamera3D");
            if (_camera == null)
            {
                _camera = new Element { Name = "PlayerCamera3D", Active = true };
                _camera.AddComponent(new CameraComponent { Active = true, IsCurrent = true });
                _camera.AddComponent(new AudioListenerComponent { Active = true });
                worldElement.AddChild(_camera);
            }
            SyncLocomotion();
        }

        public override void OnDetach(IWorldElement worldElement)
        {
            _actions?.Detach(_input);
            _actions = null;
            _input = null;

            if (_physicsBody != null)
            {
                worldElement.RemoveComponent(_physicsBody);
                _physicsBody = null;
            }
            if (_locomotion != null)
            {
                worldElement.RemoveComponent(_locomotion);
                _locomotion = null;
            }
            base.OnDetach(worldElement);
        }

        // ── Per-frame update ──────────────────────────────────────────────────

        public override void Update(float deltaTime)
        {
            if (Owner == null) return;

            _actions?.Update(deltaTime);

            if (_actions != null && _actions.GetButtonDown("FlyToggle"))
                _isFlying = !_isFlying;

            UpdateCamera(deltaTime);
            UpdateMovement(deltaTime);
        }

        private void UpdateCamera(float deltaTime)
        {
            if (_camera == null) return;
            var camTransform = _camera.GetComponent<TransformComponent>();
            if (camTransform == null) return;

            Vector2 look = _actions?.GetVector2("Look") ?? Vector2.Zero;
            if (MathF.Abs(look.X) < 0.001f && MathF.Abs(look.Y) < 0.001f) return;

            float sensitivity = _locomotion?.LookSensitivity ?? 1.2f;

            // Yaw → player body (visual rotation + movement direction)
            var playerTransform = Owner?.GetComponent<TransformComponent>();
            if (playerTransform != null)
                playerTransform.RY -= look.X * sensitivity * deltaTime;

            // Pitch → camera (look up/down, clamped)
            float newPitch = camTransform.RX + look.Y * sensitivity * deltaTime;
            camTransform.RX = Math.Clamp(newPitch, MinPitch, MaxPitch);
        }

        private void UpdateMovement(float deltaTime)
        {
            var element = Owner;
            if (element == null) return;
            var transform = element.GetComponent<TransformComponent>();
            if (transform == null) return;

            Vector2 move = _actions?.GetVector2("Move") ?? Vector2.Zero;
            //Console.WriteLine("move X {0} Move Y {1}",move.X, move.Y);
            bool sprint = _actions?.GetButton("Sprint") ?? false;
            bool jump = _actions?.GetButtonDown("Jump") ?? false;

            float speed = _isFlying ? _flySpeed : _moveSpeed;
            if (sprint)
                speed *= _sprintMultiplier;

            // ── Fly mode ────────────────────────────────────────────────────────
            if (_isFlying)
            {
                Vector3 flyForward = Vector3.UnitZ;
                Vector3 flyRight = Vector3.UnitX;
                if (MathF.Abs(move.X) > 0.001f || MathF.Abs(move.Y) > 0.001f)
                {
                    var rotationMatrix = Matrix4x4.CreateFromYawPitchRoll(transform.RY, transform.RX, 0);
                    flyForward = Vector3.Transform(Vector3.UnitZ, rotationMatrix);
                    flyRight = Vector3.Transform(Vector3.UnitX, rotationMatrix);
                }

                float ascend = _actions?.GetAxis("FlyUp") ?? 0f;
                float descend = _actions?.GetAxis("FlyDown") ?? 0f;

                Vector3 flyMove = (flyForward * move.Y + flyRight * move.X + Vector3.UnitY * (ascend - descend))
                                  * speed * deltaTime;

                transform.X += flyMove.X;
                transform.Y += flyMove.Y;
                transform.Z += flyMove.Z;

                if (_locomotion != null)
                    _locomotion.Velocity = Vector3.Zero;
                return;
            }

            float yaw = transform.RY;
            float cosY = MathF.Cos(yaw);
            float sinY = MathF.Sin(yaw);
            Vector3 forward = new Vector3(sinY, 0, cosY);
            Vector3 right = new Vector3(cosY, 0, -sinY);

            Vector3 moveDir = (forward * move.Y + right * move.X) * speed;
            //Console.WriteLine($"  move=({move.X},{move.Y}) speed={speed} fwd=({forward.X:F4},{forward.Y},{forward.Z:F4}) rt=({right.X:F4},{right.Y},{right.Z:F4}) playerYaw={yaw:F4} dir=({moveDir.X:F4},{moveDir.Y},{moveDir.Z:F4})");
            var bodyComponent = element.GetComponent<PhysicsBodyComponent>();
            if (bodyComponent != null && _locomotion != null)
            {
                _locomotion.Velocity = new Vector3(moveDir.X, _locomotion.Velocity.Y, moveDir.Z);
                if (jump && _locomotion.IsGrounded && _locomotion.CanJump)
                {
                    _locomotion.Velocity = new Vector3(_locomotion.Velocity.X, _jumpStrength, _locomotion.Velocity.Z);
                }
            }
            else
            {
                if (_locomotion != null)
                    _locomotion.Velocity = moveDir;
                transform.X += moveDir.X * deltaTime;
                transform.Y += moveDir.Y * deltaTime;
                transform.Z += moveDir.Z * deltaTime;
            }
        }

        private void SyncLocomotion()
        {
            if (_locomotion != null)
            {
                _locomotion.PreferredInputMethod = PreferredInputMethod;
                _locomotion.MoveSpeed = MoveSpeed;
                _locomotion.SprintMultiplier = SprintMultiplier;
                _locomotion.JumpStrength = JumpStrength;
                _locomotion.LookSensitivity = LookSensitivity;
                _locomotion.CanJump = CanJump;
                _locomotion.EnableHandTracking = EnableHandTracking;
                _locomotion.VRMoveSpeed = VRMoveSpeed;
                _locomotion.VRSmoothLocomotion = VRSmoothLocomotion;
                _locomotion.Active = Active;
            }
        }

        public override IWorldElement BuildUI()
        {
            return new Element();
        }
        public override string ToString() =>
            $"Player(Preferred:{PreferredInputMethod} Local:{IsLocalControlled} " +
            $"Move:{MoveSpeed:F2} Sprint:{SprintMultiplier:F2} Jump:{JumpStrength:F2})";
    }
}
