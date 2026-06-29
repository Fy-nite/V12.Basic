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
                    _camera.AddComponent(new CameraComponent { Active = true, IsCurrent = true });
                    _camera.AddComponent(new AudioListenerComponent { Active = true });
                    _camera.LocalTransform = new TRS
                    {
                        Position = new Vector3(0, 1.7f, 0),
                        Rotation = Quaternion.Identity,
                        Scale = Vector3.One
                    };
                    GameRoot.Instance.SelectedWorld?.AddElement(_camera);
                }
            }
        }

        public override void OnDetach(IWorldElement worldElement)
        {
            _actions?.Detach(_input);
            _actions = null;
            _input = null;

            if (_camera != null)
                GameRoot.Instance?.SelectedWorld?.RemoveElement(_camera);
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
                Vector2 xrLook = _actions?.GetVector2("Look") ?? Vector2.Zero;
                float xrYawDelta = (-xrLook.X) * _lookSensitivity * deltaTime;
                if (MathF.Abs(xrYawDelta) >= 0.0001f)
                    _yaw += xrYawDelta;
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

            // Camera is a root-level element, so its WorldTransform = LocalTransform.
            // We set the camera's world position and rotation directly.
            // Always update position to carry player movement to the renderer.
            Vector3 playerPos = Owner?.LocalTransform.Position ?? Vector3.Zero;
            _camera.LocalTransform = new TRS
            {
                Position = playerPos + new Vector3(0, 1.7f, 0),
                Rotation = Quaternion.CreateFromYawPitchRoll(_yaw, _pitch, 0),
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
                var m = _xrRightHand.WorldTransform;
                var handPos = new Vector3(m.M41, m.M42, m.M43);
                Quaternion handRot = Quaternion.CreateFromRotationMatrix(m);
                var fwd = Vector3.Transform(-Vector3.UnitZ, handRot);
                var dir = fwd.LengthSquared() > 0.001f ? Vector3.Normalize(fwd) : -Vector3.UnitZ;
                return (handPos, dir);
            }

            var playerPos = Owner.LocalTransform.Position;
            var camOrigin = playerPos + new Vector3(0, 1.7f, 0);
            var rot = Quaternion.CreateFromYawPitchRoll(_yaw, _pitch, 0);
            var camFwd = Vector3.Transform(-Vector3.UnitZ, rot);
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

            float speed = _isFlying ? _flySpeed : _moveSpeed;
            if (sprint)
                speed *= _sprintMultiplier;

            if (IsXrMode)
            {
                float bodyYaw = _yaw;

                if (_isFlying && _xrHead != null)
                {
                    var m = _xrHead.WorldTransform;
                    var headRot = Quaternion.CreateFromRotationMatrix(m);
                    Vector3 flyForward = Vector3.Transform(-Vector3.UnitZ, headRot);
                    Vector3 flyRight = Vector3.Transform(Vector3.UnitX, headRot);
                    float ascend = _actions?.GetAxis("FlyUp") ?? 0f;
                    float descend = _actions?.GetAxis("FlyDown") ?? 0f;
                    Vector3 flyMove = (flyForward * move.Y + flyRight * move.X + Vector3.UnitY * (ascend - descend))
                                      * speed * deltaTime;
                    pos += flyMove;
                }
                else
                {
                    var bodyRot = Quaternion.CreateFromAxisAngle(Vector3.UnitY, bodyYaw);
                    Vector3 worldForward = Vector3.Transform(-Vector3.UnitZ, bodyRot);
                    worldForward.Y = 0f;
                    float fwdLen = worldForward.Length();
                    if (fwdLen > 0.001f) worldForward /= fwdLen;
                    Vector3 worldRight = Vector3.Cross(worldForward, Vector3.UnitY);

                    Vector3 xrMove = (worldForward * move.Y + worldRight * move.X) * speed;
                    pos += xrMove * deltaTime;
                }

                lt.Position = pos;
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
            }

            // Sync element yaw rotation so LocomotionSystem can read it for movement direction
            lt.Rotation = Quaternion.CreateFromYawPitchRoll(_yaw, 0, 0);
            element.LocalTransform = lt;
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
