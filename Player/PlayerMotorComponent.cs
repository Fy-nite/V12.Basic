using System;
using System.Collections.Generic;
using System.Numerics;
using MongoDB.Bson.Serialization.Attributes;
using V12.Basic.Components;
using V12.Components;
using V12.Core;
using V12.Core.Core.Interfaces;
using V12.Core.Input;
using V12.Core.Interfaces.Renderer;

namespace V12.Basic.Player;

/// <summary>
/// Shared desktop and XR player movement.
///
/// The component is attached to the floor rig named <c>Player</c>. It finds a
/// separate body element (default <c>PlayerBody</c>) and drives that body's
/// <see cref="LocomotionComponent"/>; the floor rig follows the body and carries
/// the desktop camera and XR rig. If the body sits on the same element, the
/// split-rig sync is skipped and the component drives that body directly.
///
/// Movement speed is shaped by an ADSR envelope (attack, decay, sustain,
/// release), giving the player weight without changing physics response.
/// </summary>
[ComponentEntry("GAMEPLAY", "Player Motor", SortOrder = 5, Icon = "person-running")]
public sealed class PlayerMotorComponent : ComponentBase
{
    // ── Movement ────────────────────────────────────────────────────────────

    public float MoveSpeed { get; set; } = 4.2f;
    public float SprintMultiplier { get; set; } = 1.55f;
    public float JumpSpeed { get; set; } = 5.2f;
    public float MouseSensitivity { get; set; } = 0.0024f;
    public float KeyLookSpeed { get; set; } = 1.8f;
    public float StickLookSpeed { get; set; } = 2f;
    public float TurnSpeed { get; set; } = 2.4f;

    /// <summary>Right-stick rest noise ignored by the XR smooth turn. Some
    /// controllers report 0.15-0.2 at rest, which is enough to yaw the whole
    /// rig if the threshold is too low.</summary>
    public float TurnDeadzone { get; set; } = 0.22f;

    /// <summary>Desktop camera height above the floor rig.</summary>
    public float EyeHeight { get; set; } = 1.6f;

    /// <summary>Half-height of the capsule body, used to put the floor rig under
    /// the body when the body is a separate element.</summary>
    public float BodyCenterHeight { get; set; } = 0.9f;

    /// <summary>Reset the player if the body falls below this Y.</summary>
    public float FallResetHeight { get; set; } = -8f;

    // ── ADSR speed envelope ─────────────────────────────────────────────────

    /// <summary>Seconds to ramp from rest to full speed.</summary>
    public float Attack { get; set; } = 0.22f;

    /// <summary>Seconds to settle from full speed to <see cref="Sustain"/>.</summary>
    public float Decay { get; set; } = 0.18f;

    /// <summary>Normalized speed held while input stays pressed, 0..1.</summary>
    public float Sustain { get; set; } = 0.75f;

    /// <summary>Seconds for the release tail to reach zero.</summary>
    public float Release { get; set; } = 0.28f;

    // ── Setup ──────────────────────────────────────────────────────────────

    public string BodyElementName { get; set; } = "PlayerBody";
    public string DesktopCameraName { get; set; } = "DesktopCamera";
    public bool EnableDesktop { get; set; } = true;
    public bool EnableXr { get; set; } = true;
    public bool HideHandsOnDesktop { get; set; } = true;

    // ── Runtime state ──────────────────────────────────────────────────────

    private readonly List<IWorldElement> _hands = new();
    private readonly MovementEnvelope _envelope = new();

    private InputActionMap? _actions;
    private InputService? _input;
    private GameRoot? _root;
    private IWorldElement? _owner;
    private IWorldElement? _desktopCamera;
    private IWorldElement? _head;
    private IWorldElement? _leftHand;
    private IWorldElement? _rightHand;
    private IWorldElement? _bodyElement;
    private PhysicsBodyComponent? _body;
    private LocomotionComponent? _loco;

    private Vector3 _spawnPosition;
    private Vector3 _spawnBodyPosition;
    private bool _spawnSeeded;
    private bool _splitRig;
    private bool _hasXr;
    private bool _isXr;
    private float _yaw;
    private float _pitch = -0.04f;
    private Vector3 _lastMoveDirection;
    private Vector3 _lastHeadLocal;
    private bool _headLocalSeeded;

    public override string Name => "Player Motor";
    public override string Description => "Desktop and XR player movement with an ADSR speed envelope";

    public bool IsXr => _isXr;
    public float EnvelopeValue => _envelope.Value;
    public MovementEnvelopeStage EnvelopeStage => _envelope.Stage;
    public float CurrentSpeed { get; private set; }
    public IWorldElement? Head => _head;
    public IWorldElement? LeftHand => _leftHand;
    public IWorldElement? RightHand => _rightHand;
    public IWorldElement? BodyElement => _bodyElement;
    public PhysicsBodyComponent? Body => _body;
    public LocomotionComponent? Locomotion => _loco;

    /// <summary>Extra world velocity from a moving platform or other carrier.
    /// Set it before this component ticks.</summary>
    [BsonIgnore]
    public Vector3 ExternalVelocity { get; set; }

    /// <summary>Raised after a reset key or a fall reset. A game can reset its
    /// own world state here; the player is already back at spawn.</summary>
    public event Action? ResetRequested;

    public string DebugState =>
        $"move=({_moveX:0.00},{_moveZ:0.00}) env={_envelope.Value:0.00} " +
        $"speed={CurrentSpeed:0.00} body={_body != null} grounded={_loco?.IsGrounded}";

    private float _moveX;
    private float _moveZ;

    // ── Lifecycle ───────────────────────────────────────────────────────────

    public override void OnAttach(IWorldElement worldElement)
    {
        base.OnAttach(worldElement);
        _owner = worldElement;
        _root = GameRoot.Instance;
        _input = _root?.Registry.Get<InputService>();
        _spawnPosition = worldElement.LocalTransform.Position;
        _spawnBodyPosition = _spawnPosition + Vector3.UnitY * BodyCenterHeight;
        _spawnSeeded = false;

        EnsureBody();
        EnsureRig(force: true);
        BindInput();
        SetHandVisuals();
    }

    public override void OnDetach(IWorldElement worldElement)
    {
        if (_actions != null && _input != null)
            _actions.Detach(_input);

        _actions = null;
        _input = null;
        _owner = null;
        _desktopCamera = null;
        _head = null;
        _leftHand = null;
        _rightHand = null;
        _bodyElement = null;
        _body = null;
        _loco = null;
        base.OnDetach(worldElement);
    }

    private void BindInput()
    {
        if (_input == null)
        {
            GameRoot.Log?.Warning("[PlayerMotor] No InputService; player input disabled.");
            return;
        }

        var actions = new InputActionMap { Name = "PlayerMotor" };

        // XR left stick and desktop WASD/gamepad drive one pair of axes.
        actions.BindAxis("MoveX", "xr_left_stick_x", "");
        actions.AddAxis("MoveX", InputKey.D, InputKey.A);
        actions.AddAxis("MoveX", GamepadAxis.LeftX);

        actions.BindAxis("MoveZ", "", "xr_left_stick_y");
        actions.AddAxis("MoveZ", InputKey.S, InputKey.W);
        actions.AddAxis("MoveZ", GamepadAxis.LeftY);

        // Mouse travel is raw pixels and must not be clamped to a stick range.
        actions.BindDeltaAxis("MouseLookX", "mouse_dx", "");
        actions.BindDeltaAxis("MouseLookY", "mouse_dy", "");
        actions.BindAxis("LookX", InputKey.Right, InputKey.Left);
        actions.BindAxis("LookY", InputKey.Down, InputKey.Up);
        actions.BindAxis("StickLookX", GamepadAxis.RightX);
        actions.BindAxis("StickLookY", GamepadAxis.RightY);
        actions.BindAxis("TurnX", "xr_right_stick_x", "");

        actions.BindButton("Jump", InputKey.Space);
        actions.AddButton("Jump", GamepadButton.A);
        actions.AddButton("Jump", "xr_right_primary_click");

        actions.BindButton("Sprint", InputKey.LeftShift);
        actions.AddButton("Sprint", GamepadButton.LeftStick);

        actions.BindButton("Capture", "mouse_right");
        actions.BindButton("Reset", InputKey.Back);
        actions.AddButton("Reset", GamepadButton.Back);

        actions.Attach(_input);
        _actions = actions;
    }

    // ── Tick ────────────────────────────────────────────────────────────────

    public override void Update(float deltaTime)
    {
        if (_actions == null)
            return;

        _actions.Update(deltaTime);

        if (_actions.GetButtonDown("Reset"))
        {
            ResetToSpawn();
            ResetRequested?.Invoke();
        }

        if (_actions.GetButtonDown("Capture"))
            ToggleCapture();

        bool xrAvailable = _root?.Registry.Get("XRTrackingService") != null;
        if (xrAvailable != _hasXr)
        {
            _hasXr = xrAvailable;
            _headLocalSeeded = false;
            _envelope.Reset();
        }

        _isXr = _hasXr && EnableXr;

        EnsureBody();
        EnsureRig(force: false);

        if (_isXr)
        {
            ApplyRoomScaleOffset();
            UpdateXrTurn(deltaTime);
        }
        else if (EnableDesktop)
        {
            UpdateDesktopLook(deltaTime);
        }

        SyncRigToBody();
        Move(deltaTime);
        ApplyCamera();
        WatchForFall();
    }

    private void EnsureBody()
    {
        if (_bodyElement == null && _owner != null)
        {
            // A one-element player owns its body directly. A split rig is the
            // VR-friendly shape: floor rig named Player, capsule PlayerBody.
            if (_owner.GetComponent<PhysicsBodyComponent>() != null &&
                _owner.GetComponent<LocomotionComponent>() != null)
            {
                _bodyElement = _owner;
            }
            else
            {
                _bodyElement = _owner.GetRoot().FindChildByNameRecursive(BodyElementName);
            }
        }

        if (_bodyElement == null)
            return;

        _body ??= _bodyElement.GetComponent<PhysicsBodyComponent>();
        _loco ??= _bodyElement.GetComponent<LocomotionComponent>();

        if (_loco != null)
            _loco.UseExternalVelocity = true;

        if (!_spawnSeeded)
        {
            _spawnSeeded = true;
            _splitRig = !ReferenceEquals(_bodyElement, _owner);
            _spawnBodyPosition = _splitRig
                ? _spawnPosition + Vector3.UnitY * BodyCenterHeight
                : _spawnPosition;
        }
    }

    private void EnsureRig(bool force)
    {
        if (_owner == null)
            return;

        if (force || _desktopCamera == null)
            _desktopCamera = FindCamera(_owner);

        if (force || _head == null || _leftHand == null || _rightHand == null)
            FindHands(_owner);

        SetHandVisuals();
    }

    private static IWorldElement? FindCamera(IWorldElement owner)
    {
        var named = owner.FindChildByNameRecursive("DesktopCamera")
            ?? owner.FindChildByNameRecursive("PlayerCamera3D");
        if (named != null && named.GetComponent<CameraComponent>() != null)
            return named;

        foreach (var child in Descendants(owner))
            if (child.GetComponent<CameraComponent>() != null)
                return child;

        return null;
    }

    private void FindHands(IWorldElement owner)
    {
        _head = null;
        _leftHand = null;
        _rightHand = null;
        _hands.Clear();

        foreach (var child in Descendants(owner))
        {
            if (child.GetComponent<XRHeadComponent>() != null)
                _head ??= child;

            if (child.GetComponent<XRHandComponent>() is { } hand)
            {
                _hands.Add(child);
                if (hand.Side == HandSide.Left)
                    _leftHand = child;
                else
                    _rightHand = child;
            }
        }
    }

    private void SetHandVisuals()
    {
        bool visible = _isXr || !HideHandsOnDesktop;
        foreach (var hand in _hands)
            hand.Active = visible;
    }

    private static IEnumerable<IWorldElement> Descendants(IWorldElement root)
    {
        foreach (var child in root.Children)
        {
            yield return child;
            foreach (var nested in Descendants(child))
                yield return nested;
        }
    }

    // ── Look / turn ─────────────────────────────────────────────────────────

    private void UpdateDesktopLook(float deltaTime)
    {
        if (_actions == null)
            return;

        _yaw -= _actions.GetAxis("MouseLookX") * MouseSensitivity;
        _pitch -= _actions.GetAxis("MouseLookY") * MouseSensitivity;

        _yaw -= _actions.GetAxis("LookX") * KeyLookSpeed * deltaTime;
        _pitch -= _actions.GetAxis("LookY") * KeyLookSpeed * deltaTime;

        _yaw -= _actions.GetAxis("StickLookX") * StickLookSpeed * deltaTime;
        _pitch -= _actions.GetAxis("StickLookY") * StickLookSpeed * deltaTime;
        _pitch = Math.Clamp(_pitch, -1.15f, 1.15f);
    }

    private void UpdateXrTurn(float deltaTime)
    {
        if (_actions == null)
            return;

        float turn = _actions.GetAxis("TurnX");
        float magnitude = MathF.Abs(turn);
        if (magnitude <= TurnDeadzone)
            return;

        // Normalise the part above the deadzone so the turn starts gently and
        // still reaches full speed at the edge of the stick.
        float scaled = (magnitude - TurnDeadzone) / MathF.Max(0.01f, 1f - TurnDeadzone);
        _yaw -= MathF.Sign(turn) * scaled * TurnSpeed * deltaTime;
    }

    // ── Movement ────────────────────────────────────────────────────────────

    private void Move(float deltaTime)
    {
        if (_owner == null || _actions == null)
            return;

        _moveX = Math.Clamp(_actions.GetAxis("MoveX"), -1f, 1f);
        _moveZ = Math.Clamp(_actions.GetAxis("MoveZ"), -1f, 1f);

        Vector3 forward = _isXr && _head != null
            ? HorizontalForward(_head)
            : BodyForward(_yaw);
        Vector3 right = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitY));
        Vector3 move = forward * (-_moveZ) + right * _moveX;

        if (move.LengthSquared() > 1f)
            move = Vector3.Normalize(move);

        float inputMagnitude = move.Length();
        bool moving = inputMagnitude > 0.01f;
        if (moving)
            _lastMoveDirection = move;

        _envelope.Attack = Attack;
        _envelope.Decay = Decay;
        _envelope.Sustain = Sustain;
        _envelope.Release = Release;
        _envelope.Update(deltaTime, moving);

        float speed = MoveSpeed
            * (_actions.GetButton("Sprint") ? SprintMultiplier : 1f)
            * _envelope.Value;

        // During the release tail there is no input direction, so keep the last
        // one. That is the difference between "stops instantly" and weight.
        var direction = moving ? move : _lastMoveDirection;
        var target = direction * speed + ExternalVelocity;
        if (_envelope.Value <= 0.0001f && !moving)
            _lastMoveDirection = Vector3.Zero;
        CurrentSpeed = new Vector2(target.X, target.Z).Length();

        if (_loco != null)
        {
            bool jump = _loco.IsGrounded && _loco.CanJump
                && _actions.GetButtonDown("Jump");
            if (jump)
                _loco.Velocity = new Vector3(_loco.Velocity.X, JumpSpeed, _loco.Velocity.Z);

            _loco.Velocity = new Vector3(target.X, _loco.Velocity.Y, target.Z);
        }

        var rotation = Quaternion.CreateFromYawPitchRoll(_yaw, 0f, 0f);

        if (_bodyElement != null)
        {
            var bodyLt = _bodyElement.LocalTransform;
            _bodyElement.LocalTransform = new TRS(bodyLt.Position, rotation, bodyLt.Scale);
        }

        var lt = _owner.LocalTransform;
        _owner.LocalTransform = new TRS(lt.Position, rotation, lt.Scale);
    }

    private void ApplyRoomScaleOffset()
    {
        if (!_isXr || _head == null || _body?.Body == null || _bodyElement == null)
        {
            _headLocalSeeded = false;
            return;
        }

        var headLocal = _head.LocalTransform.Position;
        if (!_headLocalSeeded)
        {
            _headLocalSeeded = true;
            _lastHeadLocal = headLocal;
            return;
        }

        var localDelta = new Vector3(
            headLocal.X - _lastHeadLocal.X,
            0f,
            headLocal.Z - _lastHeadLocal.Z);
        _lastHeadLocal = headLocal;

        if (localDelta.LengthSquared() < 1e-6f)
            return;

        // The tracked delta is room-space. Rotate it into the rig's current
        // facing so physical forward follows the player after a turn.
        var rigRotation = Quaternion.CreateFromYawPitchRoll(_yaw, 0f, 0f);
        var bodyPosition = _body.Body.Position
            + Vector3.Transform(localDelta, rigRotation);
        _body.Body.Position = bodyPosition;

        var bodyLt = _bodyElement.LocalTransform;
        _bodyElement.LocalTransform = new TRS(bodyPosition, bodyLt.Rotation, bodyLt.Scale);
    }

    private void SyncRigToBody()
    {
        if (_owner == null || _bodyElement == null || !_splitRig)
            return;

        var bodyPosition = _body?.Body?.Position ?? _bodyElement.LocalTransform.Position;
        var rotation = Quaternion.CreateFromYawPitchRoll(_yaw, 0f, 0f);

        var headOffset = _isXr && _head != null
            ? Vector3.Transform(_head.LocalTransform.Position, rotation)
            : Vector3.Zero;

        var rigPosition = new Vector3(
            bodyPosition.X - headOffset.X,
            bodyPosition.Y - BodyCenterHeight,
            bodyPosition.Z - headOffset.Z);

        var lt = _owner.LocalTransform;
        _owner.LocalTransform = new TRS(rigPosition, rotation, lt.Scale);
    }

    private void ApplyCamera()
    {
        if (_desktopCamera == null)
            return;

        if (_desktopCamera.GetComponent<CameraComponent>() is { } camera)
            camera.IsCurrent = !_isXr;

        if (_isXr)
            return;

        // Split rig: Player is already the floor origin, so the camera is at
        // full eye height. Single-element player: the body root is the capsule
        // centre, so subtract the half-height to reach the same eye line.
        float localEye = _splitRig ? EyeHeight : EyeHeight - BodyCenterHeight;
        _desktopCamera.LocalTransform = new TRS(
            new Vector3(0f, localEye, 0f),
            Quaternion.CreateFromYawPitchRoll(0f, _pitch, 0f),
            Vector3.One);
    }

    private void WatchForFall()
    {
        if (_body?.Body == null)
            return;

        if (_body.Body.Position.Y >= FallResetHeight)
            return;

        ResetToSpawn();
        ResetRequested?.Invoke();
    }

    public void ResetToSpawn()
    {
        if (_owner == null)
            return;

        _yaw = 0f;
        _pitch = -0.04f;
        _headLocalSeeded = false;
        _lastMoveDirection = Vector3.Zero;
        _envelope.Reset();
        ExternalVelocity = Vector3.Zero;

        var rotation = Quaternion.CreateFromYawPitchRoll(_yaw, 0f, 0f);
        _owner.LocalTransform = new TRS(_spawnPosition, rotation, Vector3.One);

        if (_bodyElement != null)
            _bodyElement.LocalTransform = new TRS(_spawnBodyPosition, rotation, Vector3.One);

        if (_body?.Body != null)
        {
            _body.Body.Position = _spawnBodyPosition;
            _body.Body.Rotation = rotation;
            _body.Body.LinearVelocity = Vector3.Zero;
            _body.Body.AngularVelocity = Vector3.Zero;
        }

        if (_loco != null)
            _loco.Velocity = Vector3.Zero;
    }

    private void ToggleCapture()
    {
        var capture = _root?.MouseCapture;
        if (capture != null)
            capture.SetCaptured(!capture.IsCaptured);
    }

    private static Vector3 HorizontalForward(IWorldElement element)
    {
        var m = element.WorldTransform;
        var forward = new Vector3(-m.M31, 0f, -m.M33);
        float length = forward.Length();
        return length > 1e-5f ? forward / length : -Vector3.UnitZ;
    }

    private static Vector3 BodyForward(float yaw)
    {
        var rotation = Quaternion.CreateFromYawPitchRoll(yaw, 0f, 0f);
        return Vector3.Transform(-Vector3.UnitZ, rotation);
    }
}
