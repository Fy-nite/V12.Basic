using System;
using System.Collections.Generic;
using System.Numerics;
using V12.Components;
using V12.Core;
using V12.Core.Core.Interfaces;
using V12.Core.Input;
using V12.Core.Interfaces.Renderer;

namespace V12.Basic.Components
{
    public class PlayerComponent : ComponentBase, IPlayerControlComponent
    {
        private bool         _isLocalControlled    = true;
        private InputMethods _preferredInputMethod = InputMethods.Auto;
        private IWorldElement? _camera;
        private float _moveSpeed        = 4f;
        private float _sprintMultiplier = 1.9f;
        private float _jumpStrength     = 6f;
        private float _lookSensitivity  = 1.2f;
        private bool  _canJump          = true;
        public bool IsXrMode;
        private bool _isFlying;
        private float _flySpeed = 8f;
        private InputService? _input;
        private InputActionMap? _actions;
        private VRPlayerComponent? _vrComponent;

        private IWorldElement? _xrRoot;
        private IWorldElement? _xrHead;
        private IWorldElement? _xrLeftHand;
        private IWorldElement? _xrRightHand;
        private int _xrChildCacheTimer;

        private const float MaxPitch = MathF.PI / 2f - 0.05f;
        private const float MinPitch = -MathF.PI / 2f + 0.05f;

        private readonly object _mouseLock = new();
        private float _mouseDeltaX;
        private float _mouseDeltaY;
        private const float MouseSensitivity = 0.002f;

        private float _yaw;
        private float _pitch;

        public void AddMouseDelta(float x, float y)
        {
            lock (_mouseLock)
            {
                _mouseDeltaX += x;
                _mouseDeltaY += y;
            }
        }

        public override string Name        => "Player";
        public override string Description => "Unified VR/desktop player settings";

        public InputMethods RequiredInputMethod => InputMethods.Auto;

        public bool IsLocalControlled
        {
            get => _isLocalControlled;
            set { if (_isLocalControlled != value) { _isLocalControlled = value; MarkDirty(); } }
        }

        public InputMethods PreferredInputMethod
        {
            get => _preferredInputMethod;
            set { if (_preferredInputMethod != value) { _preferredInputMethod = value; MarkDirty(); } }
        }

        public float MoveSpeed
        {
            get => _moveSpeed;
            set { if (Math.Abs(_moveSpeed - value) > 0.0001f) { _moveSpeed = MathF.Max(0f, value); MarkDirty(); } }
        }

        public float SprintMultiplier
        {
            get => _sprintMultiplier;
            set { if (Math.Abs(_sprintMultiplier - value) > 0.0001f) { _sprintMultiplier = MathF.Max(1f, value); MarkDirty(); } }
        }

        public float JumpStrength
        {
            get => _jumpStrength;
            set { if (Math.Abs(_jumpStrength - value) > 0.0001f) { _jumpStrength = MathF.Max(0f, value); MarkDirty(); } }
        }

        public float LookSensitivity
        {
            get => _lookSensitivity;
            set { if (Math.Abs(_lookSensitivity - value) > 0.0001f) { _lookSensitivity = MathF.Max(0f, value); MarkDirty(); } }
        }

        public bool CanJump
        {
            get => _canJump;
            set { if (_canJump != value) { _canJump = value; MarkDirty(); } }
        }

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

        public override void OnAttach(IWorldElement worldElement)
        {
            base.OnAttach(worldElement);

            _input = GameRoot.Instance.Registry.Get<InputService>();

            _actions = new InputActionMap { Name = "PlayerActions" };
            _actions.BindVector2("Look", "look_right", "look_left", "look_up", "look_down");
            _actions.BindVector2("Move", "move_right", "move_left", "move_forward", "move_backward");
            _actions.BindButton("Jump", "jump");
            _actions.BindButton("Sprint", "run");
            _actions.BindButton("FlyToggle", "fly_toggle");
            _actions.BindAxis("FlyUp", "fly_up", "");
            _actions.BindAxis("FlyDown", "fly_down", "");
            _actions.Attach(_input);

            var lt = worldElement.LocalTransform;
            _yaw = 0f;
            _pitch = 0f;

            _vrComponent = worldElement.GetComponent<VRPlayerComponent>();
            if (_vrComponent != null || IsXrMode)
            {
                IsXrMode = true;
                _camera = null;
                CacheXrChildren();
            }
            else
            {
                _camera = worldElement.FindChildByName("PlayerCamera3D");
                if (_camera == null)
                {
                    _camera = new Element { Name = "PlayerCamera3D", Active = true };
                    _camera.AddComponent(new CameraComponent { Active = true, IsCurrent = true, FarClip = 5000f });
                    _camera.AddComponent(new AudioListenerComponent { Active = true });
                    _camera.LocalTransform = new TRS
                    {
                        Position = new Vector3(0, 1.7f, 0),
                        Rotation = Quaternion.Identity,
                        Scale = Vector3.One
                    };
                    worldElement.AddChild(_camera);
                }
            }
        }

        public override void OnDetach(IWorldElement worldElement)
        {
            _actions?.Detach(_input);
            _actions = null;
            _input = null;

            _camera = null;

            base.OnDetach(worldElement);
        }

        private void CacheXrChildren()
        {
            _xrRoot = null;
            _xrHead = null;
            _xrLeftHand = null;
            _xrRightHand = null;
            _xrChildCacheTimer = 0;

            if (Owner == null) return;

            foreach (var child in Owner.Children)
            {
                if (child.GetComponent<XRRootComponent>() != null)
                {
                    _xrRoot = child;
                    break;
                }
            }

            if (_xrRoot == null) return;

            foreach (var child in _xrRoot.Children)
            {
                if (child.GetComponent<XRHeadComponent>() != null)
                    _xrHead = child;
                else if (child.GetComponent<XRHandComponent>() is XRHandComponent hand)
                {
                    if (hand.Side == HandSide.Left)
                        _xrLeftHand = child;
                    else
                        _xrRightHand = child;
                }
            }
        }

        private int _frameCount;

        public override void Update(float deltaTime)
        {
            if (Owner == null) return;

            _actions?.Update(deltaTime);

            if (_actions != null && _actions.GetButtonDown("FlyToggle"))
                _isFlying = !_isFlying;

            if (_actions != null && ++_frameCount % 10 == 0)
            {
                var look = _actions.GetVector2("Look");
                var move = _actions.GetVector2("Move");
                //Console.WriteLine($"DBG look=({look.X:F2},{look.Y:F2}) move=({move.X:F2},{move.Y:F2}) flying={_isFlying} _yaw={_yaw:F2}");
            }

            _xrChildCacheTimer++;
            if (_xrChildCacheTimer > 60)
                CacheXrChildren();

            UpdateCamera(deltaTime);
            UpdateMovement(deltaTime);
        }

        private void UpdateCamera(float deltaTime)
        {
            if (IsXrMode)
            {
                // XR: the head-mounted display IS the camera. There is no look
                // input here — a right-stick yaw would rotate the whole world
                // around the player, which is disorienting and non-standard.
                return;
            }

            if (_camera == null) return;

            float mouseX, mouseY;
            lock (_mouseLock)
            {
                mouseX = _mouseDeltaX;
                mouseY = _mouseDeltaY;
                _mouseDeltaX = 0;
                _mouseDeltaY = 0;
            }

            Vector2 look = _actions?.GetVector2("Look") ?? Vector2.Zero;
            float sensitivity = _lookSensitivity;
            float yawDelta = (-look.X) * sensitivity * deltaTime + (-mouseX) * MouseSensitivity;
            float pitchDelta = look.Y * sensitivity * deltaTime + (-mouseY) * MouseSensitivity;

            if (MathF.Abs(yawDelta) >= 0.0001f || MathF.Abs(pitchDelta) >= 0.0001f)
            {
                _yaw += yawDelta;
                _pitch += pitchDelta;
                _pitch = Math.Clamp(_pitch, MinPitch, MaxPitch);
            }

            // Camera is a child of the Player element, so LocalTransform is relative.
            _camera.LocalTransform = new TRS
            {
                Position = new Vector3(0, 1.7f, 0),
                Rotation = Quaternion.CreateFromYawPitchRoll(0, _pitch, 0),
                Scale = Vector3.One
            };
        }

        /// <summary>
        /// Returns the aim ray origin and direction for the current mode.
        /// Desktop: from camera position/orientation.
        /// XR: from right-hand controller position/orientation.
        /// </summary>
        public (Vector3 origin, Vector3 direction) GetAimRay()
        {
            if (Owner == null)
                return (Vector3.Zero, -Vector3.UnitZ);

            if (IsXrMode && _xrRightHand != null)
            {
                var handLt = _xrRightHand.LocalTransform;
                var playerLt = Owner.LocalTransform;

                // World position = playerPos + R(playerYaw) * trackingPos
                // Same as what Godot produces from the Player→XR_Root→XR_Hand hierarchy.
                var handPos = Vector3.Transform(handLt.Position, playerLt.Rotation) + playerLt.Position;

                var handRot = playerLt.Rotation * handLt.Rotation;
                var fwd = Vector3.Transform(-Vector3.UnitZ, handRot);
                var dir = fwd.LengthSquared() > 0.001f ? Vector3.Normalize(fwd) : -Vector3.UnitZ;
                return (handPos, dir);
            }

            if (_camera == null)
                return (Vector3.Zero, -Vector3.UnitZ);

            var camWorld = _camera.WorldTransform;
            var camOrigin = new Vector3(camWorld.M41, camWorld.M42, camWorld.M43);
            var camFwd = new Vector3(-camWorld.M31, -camWorld.M32, -camWorld.M33);
            var camDir = camFwd.LengthSquared() > 0.001f ? Vector3.Normalize(camFwd) : -Vector3.UnitZ;
            return (camOrigin, camDir);
        }

        private void UpdateMovement(float deltaTime)
        {
            var element = Owner;
            if (element == null) return;

            var lt = element.LocalTransform;
            Vector3 pos = lt.Position;

            Vector2 move = _actions?.GetVector2("Move") ?? Vector2.Zero;
            bool sprint = _actions?.GetButton("Sprint") ?? false;

            // Debug output for movement
            if (MathF.Abs(move.X) > 0.01f || MathF.Abs(move.Y) > 0.01f)
            {
                // Console.WriteLine($"[PlayerComponent] Move input: ({move.X:F2}, {move.Y:F2}), _yaw: {_yaw:F2}");
            }

            float speed = _isFlying ? _flySpeed : _moveSpeed;
            if (sprint)
                speed *= _sprintMultiplier;

            if (IsXrMode)
            {
                var xrLoco = element.GetComponent<LocomotionComponent>();
                if (xrLoco != null)
                {
                    UpdateXrCharacterController(element, xrLoco, move, sprint, deltaTime);
                    return;
                }

                // Fallback (no LocomotionComponent): legacy direct move so the
                // player still responds to input.
                float bodyYaw = _yaw;
                Vector3 worldForward = _xrHead != null ? ForwardOf(_xrHead) : BodyForward(bodyYaw);
                worldForward.Y = 0f;
                float fwdLen = worldForward.Length();
                if (fwdLen > 0.001f) worldForward /= fwdLen;
                else worldForward = -Vector3.UnitZ;
                Vector3 worldRight = Vector3.Cross(worldForward, Vector3.UnitY);

                Vector3 xrMove = (worldForward * move.Y + worldRight * move.X) * speed;
                pos += xrMove * deltaTime;

                lt.Position = pos;
                lt.Rotation = Quaternion.CreateFromYawPitchRoll(_yaw, 0, 0);
                element.LocalTransform = lt;
                return;
            }

            if (_isFlying)
            {
                Vector3 flyForward = Vector3.UnitZ;
                Vector3 flyRight = Vector3.UnitX;
                if (MathF.Abs(move.X) > 0.001f || MathF.Abs(move.Y) > 0.001f)
                {
                    var rotationMatrix = Matrix4x4.CreateFromYawPitchRoll(_yaw, _pitch, 0);
                    flyForward = Vector3.Transform(-Vector3.UnitZ, rotationMatrix);
                    flyRight = Vector3.Transform(Vector3.UnitX, rotationMatrix);
                }

                float ascend = _actions?.GetAxis("FlyUp") ?? 0f;
                float descend = _actions?.GetAxis("FlyDown") ?? 0f;

                Vector3 flyMove = (flyForward * move.Y + flyRight * move.X + Vector3.UnitY * (ascend - descend))
                                  * speed * deltaTime;

                pos += flyMove;

                lt.Position = pos;
                element.LocalTransform = lt;
                return;
            }

            var loco = element.GetComponent<LocomotionComponent>();
            if (loco != null)
            {
                float cosY = MathF.Cos(_yaw);
                float sinY = MathF.Sin(_yaw);
                Vector3 forward = new Vector3(-sinY, 0, -cosY);
                Vector3 right = new Vector3(cosY, 0, -sinY);
                Vector3 moveDir = (forward * move.Y + right * move.X) * speed;
                loco.Velocity = new Vector3(moveDir.X, loco.Velocity.Y, moveDir.Z);
                
                if (MathF.Abs(move.X) > 0.01f || MathF.Abs(move.Y) > 0.01f)
                {
                    // Console.WriteLine($"[PlayerComponent] Setting loco.Velocity: ({loco.Velocity.X:F2}, {loco.Velocity.Y:F2}, {loco.Velocity.Z:F2})");
                }
            }
            else
            {
                Console.WriteLine($"[PlayerComponent] WARNING: No LocomotionComponent found on player element!");
            }

            // Sync element yaw rotation so LocomotionSystem can read it for movement direction
            lt.Rotation = Quaternion.CreateFromYawPitchRoll(_yaw, 0, 0);
            element.LocalTransform = lt;
        }

        // ── Direction helpers ───────────────────────────────────────────────

        /// <summary>
        /// XR character-controller locomotion. Movement is head-relative using
        /// only the horizontal (yaw) component — looking up/down never pushes
        /// you vertically or tilts the direction. The body is driven through
        /// <see cref="LocomotionComponent.Velocity"/> by PhysicsLocomotionSystem,
        /// which slides it along walls and owns the element transform, so the
        /// camera tracks the slid body instead of teleporting ahead of it.
        /// Gravity, jump and acceleration smoothing are computed here.
        /// </summary>
        private void UpdateXrCharacterController(
            IWorldElement element, LocomotionComponent loco, Vector2 move, bool sprint, float deltaTime)
        {
            Vector3 worldForward = _xrHead != null ? ForwardOf(_xrHead) : BodyForward(_yaw);
            worldForward.Y = 0f;
            float fwdLen = worldForward.Length();
            if (fwdLen > 0.001f) worldForward /= fwdLen;
            else worldForward = -Vector3.UnitZ;
            Vector3 worldRight = Vector3.Cross(worldForward, Vector3.UnitY);

            float speed = _isFlying ? _flySpeed : _moveSpeed;
            if (sprint) speed *= _sprintMultiplier;

            Vector3 moveDir = worldForward * move.Y + worldRight * move.X;
            moveDir.Y = 0f;
            float len = moveDir.Length();
            if (len > 1f) moveDir /= len;
            Vector3 targetHVel = moveDir * speed;

            if (_isFlying)
            {
                float ascend = _actions?.GetAxis("FlyUp") ?? 0f;
                float descend = _actions?.GetAxis("FlyDown") ?? 0f;
                loco.Velocity = new Vector3(targetHVel.X, (ascend - descend) * speed, targetHVel.Z);
                loco.IsGrounded = false;
                return;
            }

            float accel = loco.Acceleration > 0f ? loco.Acceleration : 0f;
            if (accel > 0f)
            {
                loco.Velocity = new Vector3(
                    Approach(loco.Velocity.X, targetHVel.X, accel * deltaTime),
                    loco.Velocity.Y,
                    Approach(loco.Velocity.Z, targetHVel.Z, accel * deltaTime));
            }
            else
            {
                loco.Velocity = new Vector3(targetHVel.X, loco.Velocity.Y, targetHVel.Z);
            }

            if (loco.IsGrounded)
            {
                bool jump = loco.CanJump && loco.JumpStrength > 0f && (_actions?.GetButtonDown("Jump") ?? false);
                var v = loco.Velocity;
                if (jump)
                    v.Y = loco.JumpStrength;
                else
                    v.Y = 0f; // grounded: is_on_floor() + floor snapping keep us planted
                loco.Velocity = v;
            }
            else
            {
                var v = loco.Velocity;
                v.Y -= loco.Gravity * deltaTime;
                loco.Velocity = v;
            }
        }

        /// <summary>Moves <paramref name="current"/> toward <paramref name="target"/> by at most <paramref name="maxDelta"/>.</summary>
        private static float Approach(float current, float target, float maxDelta)
        {
            return current < target
                ? MathF.Min(current + maxDelta, target)
                : MathF.Max(current - maxDelta, target);
        }

        /// <summary>
        /// World-space forward (-Z) of an element, extracted from its world
        /// transform matrix rows and normalised — so any scale in the element's
        /// transform (e.g. the XR head's 0.1x visualiser scale) doesn't corrupt
        /// the direction. <c>Quaternion.CreateFromRotationMatrix</c> on a scaled
        /// matrix returns a bogus quaternion, which made head-relative movement
        /// only ever point forward/backward.
        /// </summary>
        private static Vector3 ForwardOf(IWorldElement el)
        {
            var m = el.WorldTransform;
            var fwd = new Vector3(-m.M31, -m.M32, -m.M33);
            float len = fwd.Length();
            return len > 1e-6f ? fwd / len : -Vector3.UnitZ;
        }

        /// <summary>World-space right (+X) of an element, scale-invariant.</summary>
        private static Vector3 RightOf(IWorldElement el)
        {
            var m = el.WorldTransform;
            var right = new Vector3(m.M11, m.M12, m.M13);
            float len = right.Length();
            return len > 1e-6f ? right / len : Vector3.UnitX;
        }

        private static Vector3 BodyForward(float yaw)
        {
            var bodyRot = Quaternion.CreateFromAxisAngle(Vector3.UnitY, yaw);
            return Vector3.Transform(-Vector3.UnitZ, bodyRot);
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
