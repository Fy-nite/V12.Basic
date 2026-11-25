using System;
using System.Collections.Generic;
using System.Numerics;
using V12.Basic.Components;
using V12.Components;
using V12.Core;
using V12.Core.Core.Interfaces;
using V12.Core.Input;
using V12.Core.Interfaces.Physics;
using V12.Core.Interfaces.Renderer;

namespace V12.Basic.Grabbing
{
    /// <summary>
    /// Reusable desktop and XR grabber. Attach it to the player element, give the
    /// props a <see cref="GrabbableComponent"/> and a dynamic
    /// <see cref="PhysicsBodyComponent"/>, and it handles aim, hold, throw and the
    /// physics coupling. Desktop uses the current camera and the <c>interact</c>
    /// action plus left mouse; XR uses the tracked hands and the trigger or grip
    /// axes published by the host as <c>xr_&lt;side&gt;_trigger</c> and
    /// <c>xr_&lt;side&gt;_grip</c>.
    ///
    /// The held body stays dynamic. Each fixed tick its velocity is driven toward
    /// the hand or aim point, so it still collides with the world and can push
    /// other bodies instead of ghosting through them.
    /// </summary>
    [ComponentEntry("GAMEPLAY", "Grabber", SortOrder = 13, Icon = "hand-back-fist", SingleInstance = true)]
    public sealed class GrabberComponent : ComponentBase
    {
        private const int DesktopHand = 0;
        private const int LeftHand = 1;
        private const int RightHand = 2;

        // ── Tuning ──────────────────────────────────────────────────────────

        /// <summary>Desktop ray length in world units.</summary>
        public float Reach { get; set; } = 4.5f;

        /// <summary>XR ray length in world units.</summary>
        public float XrReach { get; set; } = 3f;

        /// <summary>Desktop hold distance when the hit is closer than this.</summary>
        public float HoldDistance { get; set; } = 2.2f;

        /// <summary>How hard the held body is pulled toward its target.</summary>
        public float FollowSharpness { get; set; } = 18f;

        /// <summary>Upper bound on the held body's speed before the mass falloff.</summary>
        public float MaxFollowSpeed { get; set; } = 18f;

        /// <summary>How much heavier bodies lag. 0 = mass ignored, 0.5 = speed
        /// scales with the square root of mass.</summary>
        public float MassSpeedFalloff { get; set; } = 0.35f;

        /// <summary>How hard a held body is rotated toward the hand or aim.</summary>
        public float RotationSharpness { get; set; } = 14f;

        /// <summary>Upper bound on angular speed while held, radians per second.</summary>
        public float MaxAngularSpeed { get; set; } = 14f;

        /// <summary>Scale applied to the release velocity.</summary>
        public float ThrowMultiplier { get; set; } = 1.15f;

        /// <summary>Release velocity is clamped to this length.</summary>
        public float MaxThrowSpeed { get; set; } = 18f;

        /// <summary>Release a held body once it is this far from its target for
        /// longer than <see cref="BreakDelay"/>.</summary>
        public float BreakDistance { get; set; } = 3.5f;

        /// <summary>Seconds a held body may be beyond the break distance.</summary>
        public float BreakDelay { get; set; } = 0.6f;

        /// <summary>Only dynamic bodies carrying a <see cref="GrabbableComponent"/>
        /// can be grabbed. Turn off for a sandbox that grabs every dynamic body.</summary>
        public bool RequireGrabbable { get; set; } = true;

        /// <summary>Use the XR grip axis as a second grab input.</summary>
        public bool UseGrip { get; set; } = true;

        public float TriggerThreshold { get; set; } = 0.55f;
        public float ReleaseThreshold { get; set; } = 0.3f;

        public bool EnableDesktop { get; set; } = true;
        public bool EnableXr { get; set; } = true;

        // Runtime-only: the player's own body when it lives on a separate
        // element. Kept out of the component property bag so a snapshot or save
        // never tries to serialize a live body reference.
        private PhysicsBodyComponent? _playerBody;

        /// <summary>
        /// Set when a controller keeps the player body on a separate element.
        /// The grabber uses it to skip the self-hit when the camera sits inside
        /// the capsule; otherwise it falls back to a body on its own element.
        /// </summary>
        public void SetPlayerBody(PhysicsBodyComponent? body) => _playerBody = body;

        // ── Runtime ─────────────────────────────────────────────────────────

        private sealed class HeldState
        {
            public required IPhysicsBody Body;
            public required IWorldElement Element;
            public required GrabbableComponent Grabbable;
            public required int Hand;
            public required float HoldDistance;
            public required Vector3 LocalOffset;
            public required Quaternion LocalRotation;
            public required float SavedGravityScale;
            public required bool SavedLockRotation;
            public required bool SnapRotation;
            public Vector3 PreviousTarget;
            public Vector3 TargetVelocity;
            public float StuckTime;
            public bool HasPreviousTarget;
        }

        private readonly HeldState?[] _held = new HeldState?[3];

        private InputActionMap? _actions;
        private InputService? _input;
        private IPhysicsBackend? _physics;
        private GameRoot? _root;
        private IWorldElement? _owner;
        private IWorldElement? _desktopCamera;
        private IWorldElement? _leftHand;
        private IWorldElement? _rightHand;

        private bool _hasXr;
        private bool _desktopReady;
        private float _rigScanTimer;
        private GrabbableComponent? _aimed;

        public override string Name => "Grabber";
        public override string Description => "Desktop and XR physics grabber";

        /// <summary>The grabbable currently under the desktop ray or either XR hand.</summary>
        public GrabbableComponent? Aimed => _aimed;

        public bool IsAiming => _aimed != null;
        public bool IsHolding => HeldCount > 0;
        public int HeldCount
        {
            get
            {
                int count = 0;
                foreach (var state in _held)
                    if (state != null) count++;
                return count;
            }
        }

        /// <summary>Element held by the desktop grab or the first occupied hand.</summary>
        public IWorldElement? HeldElement
        {
            get
            {
                foreach (var state in _held)
                    if (state != null) return state.Element;
                return null;
            }
        }

        public GrabbableComponent? HeldGrabbable
        {
            get
            {
                foreach (var state in _held)
                    if (state != null) return state.Grabbable;
                return null;
            }
        }

        public event Action<GrabberComponent, GrabbableComponent>? AimChanged;
        public event Action<GrabberComponent, GrabbableComponent>? Grabbed;
        public event Action<GrabberComponent, GrabbableComponent>? Released;

        // ── Lifecycle ───────────────────────────────────────────────────────

        public override void OnAttach(IWorldElement worldElement)
        {
            base.OnAttach(worldElement);
            _owner = worldElement;
            _root = GameRoot.Instance;
            _physics = _root?.PhysicsBackend;
            _input = _root?.Registry.Get<InputService>();
            BindInput();
            ResolveRig(force: true);
        }

        public override void OnDetach(IWorldElement worldElement)
        {
            ReleaseAll(throwHeld: false);
            if (_actions != null && _input != null)
                _actions.Detach(_input);
            _actions = null;
            _input = null;
            _physics = null;
            _owner = null;
            _desktopCamera = null;
            _leftHand = null;
            _rightHand = null;
            base.OnDetach(worldElement);
        }

        private void BindInput()
        {
            if (_input == null)
            {
                GameRoot.Log?.Warning("[Grabber] No InputService; grab input disabled.");
                return;
            }

            var actions = new InputActionMap { Name = "Grabber" };

            // Desktop. interact is R on the keyboard and X on a pad; the left
            // mouse is the same action for players who expect to click props.
            actions.BindButton("Grab", "mouse_left");
            actions.AddButton("Grab", "interact");

            // XR hands. The click edges are the authoritative grab state: an
            // analog trigger value is host-frame data and may be cleared between
            // fixed steps, while a held click stays latched in the action map.
            actions.BindAxis("LeftTrigger", "xr_left_trigger", "");
            actions.BindAxis("RightTrigger", "xr_right_trigger", "");
            actions.BindAxis("LeftGrip", "xr_left_grip", "");
            actions.BindAxis("RightGrip", "xr_right_grip", "");
            actions.BindButton("LeftTriggerClick", "xr_left_trigger_click");
            actions.BindButton("RightTriggerClick", "xr_right_trigger_click");
            actions.BindButton("LeftGripClick", "xr_left_grip_click");
            actions.BindButton("RightGripClick", "xr_right_grip_click");

            actions.Attach(_input);
            _actions = actions;
        }

        // ── Tick ────────────────────────────────────────────────────────────

        public override void Update(float deltaTime)
        {
            if (_actions == null)
                return;

            _actions.Update(deltaTime);
            _physics ??= _root?.PhysicsBackend;

            // Split-rig players name the capsule PlayerBody. Resolve it lazily so
            // attach order (motor before body parse) cannot leave the self-body
            // null and make the grab ray stop on the player's own capsule.
            if (_playerBody == null && _owner != null)
                _playerBody = _owner.GetRoot()
                    .FindChildByNameRecursive("PlayerBody")
                    ?.GetComponent<PhysicsBodyComponent>();

            bool xr = _root?.Registry.Get("XRTrackingService") != null;
            if (xr != _hasXr)
            {
                ReleaseAll(throwHeld: false);
                _hasXr = xr;
                ResolveRig(force: true);
            }

            _rigScanTimer -= deltaTime;
            if (_rigScanTimer <= 0f)
            {
                _rigScanTimer = 0.5f;
                ResolveRig(force: false);
            }

            if (_hasXr && EnableXr)
                UpdateXr();
            else if (!_hasXr && EnableDesktop)
                UpdateDesktop();

            UpdateHeld(deltaTime);
        }

        private void UpdateDesktop()
        {
            if (_owner == null || _desktopCamera == null || _physics == null)
                return;

            var (origin, direction, rotation) = CameraAim();
            var hit = Cast(origin, direction, Reach, out bool didHit);

            UpdateAim(didHit ? hit.Grabbable : null);

            bool held = _held[DesktopHand] != null;
            bool pressed = _actions!.GetButton("Grab");

            if (held)
            {
                if (!pressed)
                    Release(DesktopHand, throwHeld: true);
                return;
            }

            if (!pressed)
            {
                // Re-arm on release. This is what stops a held button from
                // immediately grabbing the next prop after a release or break.
                _desktopReady = true;
                return;
            }

            // Edge-trigger so holding the button after a release does not
            // immediately grab the next prop under the crosshair.
            if (!_desktopReady)
                return;

            if (!didHit || hit.Grabbable == null)
                return;

            TryGrab(DesktopHand, hit, origin, direction, rotation);
            _desktopReady = false;
        }

        private void UpdateXr()
        {
            if (_physics == null)
                return;

            GrabbableComponent? bestAim = null;
            float bestDistance = float.MaxValue;

            UpdateHand(LeftHand, _leftHand, "LeftTrigger", "LeftGrip",
                "LeftTriggerClick", "LeftGripClick", ref bestAim, ref bestDistance);
            UpdateHand(RightHand, _rightHand, "RightTrigger", "RightGrip",
                "RightTriggerClick", "RightGripClick", ref bestAim, ref bestDistance);

            UpdateAim(bestAim);
        }

        private void UpdateHand(
            int hand, IWorldElement? handElement,
            string triggerAction, string gripAction,
            string triggerClickAction, string gripClickAction,
            ref GrabbableComponent? bestAim, ref float bestDistance)
        {
            if (handElement == null || _actions == null)
                return;

            // A held click edge is stable across fixed steps. The analog values
            // remain as a fallback for a control that only publishes its axis.
            float analogTrigger = _actions.GetAxis(triggerAction);
            float analogGrip = UseGrip ? _actions.GetAxis(gripAction) : 0f;
            bool clickHeld = _actions.GetButton(triggerClickAction)
                || (UseGrip && _actions.GetButton(gripClickAction));
            float analog = MathF.Max(analogTrigger, analogGrip);

            bool held = _held[hand] != null;
            bool pressed = clickHeld || analog >= TriggerThreshold;
            bool released = !clickHeld && analog <= ReleaseThreshold;

            if (held)
            {
                if (released)
                    Release(hand, throwHeld: true);
                return;
            }

            if (!pressed)
                return;

            var (origin, direction, rotation) = HandAim(handElement);
            var hit = Cast(origin, direction, XrReach, out bool didHit);
            if (!didHit || hit.Grabbable == null)
                return;

            if (hit.Distance < bestDistance)
            {
                bestDistance = hit.Distance;
                bestAim = hit.Grabbable;
            }

            TryGrab(hand, hit, origin, direction, rotation);
        }

        private void UpdateAim(GrabbableComponent? aimed)
        {
            if (ReferenceEquals(_aimed, aimed))
                return;

            _aimed = aimed;
            try { AimChanged?.Invoke(this, aimed!); } catch { }
        }

        // ── Grab / release ──────────────────────────────────────────────────

        private readonly struct Hit
        {
            public required IPhysicsBody Body { get; init; }
            public required IWorldElement Element { get; init; }
            public required GrabbableComponent? Grabbable { get; init; }
            public required Vector3 Point { get; init; }
            public required float Distance { get; init; }
        }

        private Hit Cast(Vector3 origin, Vector3 direction, float reach, out bool didHit)
        {
            didHit = false;
            if (_physics == null)
                return default;

            var playerBody = _playerBody?.Body ?? _owner?.GetComponent<PhysicsBodyComponent>()?.Body;
            var start = origin;

            // The desktop camera sits inside the player capsule. A ray from the
            // eye hits the player's own body first, so step past it and re-cast.
            // XR rays start at the hand and normally take the first path.
            for (int attempt = 0; attempt < 4; attempt++)
            {
                var ray = new Ray { Origin = start, Direction = direction, MaxDistance = reach };
                if (!_physics.Raycast(ray, out var hit) || hit.Body == null)
                    return default;

                if (playerBody != null && ReferenceEquals(hit.Body, playerBody))
                {
                    start += direction * 0.6f;
                    continue;
                }

                var element = FindElementForBody(hit.Body);
                if (element == null)
                    return default;

                var grabbable = element.GetComponent<GrabbableComponent>();
                if (RequireGrabbable && grabbable == null)
                    return default;
                if (grabbable is { CanGrab: false })
                    return default;
                if (!hit.Body.IsDynamic)
                    return default;

                didHit = true;
                return new Hit
                {
                    Body = hit.Body,
                    Element = element,
                    Grabbable = grabbable,
                    Point = hit.Point,
                    Distance = hit.Distance + Vector3.Distance(origin, start),
                };
            }

            return default;
        }

        private void TryGrab(int hand, in Hit hit, Vector3 origin, Vector3 direction, Quaternion aimRotation)
        {
            // One body, one hand. A second hand grabbing the same body would
            // fight the first for the same body velocity.
            for (int i = 0; i < _held.Length; i++)
                if (_held[i]?.Body == hit.Body)
                    Release(i, throwHeld: false);

            var grabbable = hit.Grabbable ?? new GrabbableComponent();
            float hold = grabbable.HoldDistance > 0f
                ? grabbable.HoldDistance
                : hand == DesktopHand ? HoldDistance : hit.Distance;

            if (hand == DesktopHand)
                hold = MathF.Max(hold, MathF.Min(hit.Distance, Reach));
            else
                hold = hit.Distance;

            var holdPoint = origin + direction * hold;
            var bodyPosition = hit.Body.Position;
            var offset = bodyPosition - holdPoint;
            var localOffset = Vector3.Transform(offset, Quaternion.Conjugate(aimRotation));
            var localRotation = hit.Body.Rotation * Quaternion.Conjugate(aimRotation);

            var state = new HeldState
            {
                Body = hit.Body,
                Element = hit.Element,
                Grabbable = grabbable,
                Hand = hand,
                HoldDistance = hold,
                LocalOffset = localOffset,
                LocalRotation = localRotation,
                SavedGravityScale = hit.Body.GravityScale,
                SavedLockRotation = hit.Body.LockRotation,
                SnapRotation = grabbable.RotateWithHand && !grabbable.LockRotation,
                PreviousTarget = bodyPosition,
            };

            // The body stays dynamic. Gravity is disabled only while held so it
            // floats in the hand; collisions and response stay live.
            hit.Body.GravityScale = 0f;
            hit.Body.LockRotation = grabbable.LockRotation;
            hit.Body.LinearVelocity = Vector3.Zero;
            hit.Body.AngularVelocity = Vector3.Zero;

            _held[hand] = state;
            if (grabbable.Owner != null)
                grabbable.NotifyGrabbed(this, hit.Element);
            try { Grabbed?.Invoke(this, grabbable); } catch { }
        }

        private void UpdateHeld(float deltaTime)
        {
            if (_physics == null || _owner == null)
                return;

            float dt = MathF.Max(deltaTime, 1e-4f);

            for (int hand = 0; hand < _held.Length; hand++)
            {
                var state = _held[hand];
                if (state == null)
                    continue;

                if (state.Body.IsCharacterController)
                {
                    Release(hand, throwHeld: false);
                    continue;
                }

                if (!TryGetTarget(state, out var targetPosition, out var targetRotation))
                {
                    Release(hand, throwHeld: false);
                    continue;
                }

                if (!state.HasPreviousTarget)
                {
                    state.PreviousTarget = targetPosition;
                    state.HasPreviousTarget = true;
                }

                state.TargetVelocity = (targetPosition - state.PreviousTarget) / dt;
                state.PreviousTarget = targetPosition;

                var error = targetPosition - state.Body.Position;
                float massFalloff = MathF.Pow(MathF.Max(1f, state.Body.Mass), MassSpeedFalloff);
                float speedLimit = MathF.Max(1f, MaxFollowSpeed / massFalloff);
                var desired = error * FollowSharpness;
                if (desired.LengthSquared() > speedLimit * speedLimit)
                    desired = Vector3.Normalize(desired) * speedLimit;
                if (error.LengthSquared() < 0.0004f)
                    desired = Vector3.Zero;

                state.Body.LinearVelocity = desired;

                if (state.SnapRotation)
                {
                    var rotationError = targetRotation * Quaternion.Conjugate(state.Body.Rotation);
                    if (rotationError.W < 0f)
                        rotationError = Quaternion.Negate(rotationError);

                    float angle = 2f * MathF.Acos(Math.Clamp(rotationError.W, -1f, 1f));
                    if (angle > 0.0005f)
                    {
                        float sinHalf = MathF.Sqrt(MathF.Max(0f, 1f - rotationError.W * rotationError.W));
                        var axis = sinHalf > 1e-5f
                            ? new Vector3(rotationError.X, rotationError.Y, rotationError.Z) / sinHalf
                            : Vector3.Zero;
                        float speed = MathF.Min(angle * RotationSharpness, MaxAngularSpeed);
                        state.Body.AngularVelocity = axis * speed;
                    }
                    else
                    {
                        state.Body.AngularVelocity = Vector3.Zero;
                    }
                }
                else
                {
                    state.Body.AngularVelocity = Vector3.Zero;
                }

                if (error.LengthSquared() > BreakDistance * BreakDistance)
                {
                    state.StuckTime += dt;
                    if (state.StuckTime >= BreakDelay)
                        Release(hand, throwHeld: false);
                }
                else
                {
                    state.StuckTime = 0f;
                }
            }
        }

        private bool TryGetTarget(HeldState state, out Vector3 position, out Quaternion rotation)
        {
            position = state.Body.Position;
            rotation = state.Body.Rotation;

            switch (state.Hand)
            {
                case DesktopHand:
                    if (_desktopCamera == null)
                        return false;
                    var (origin, direction, aimRotation) = CameraAim();
                    var holdPoint = origin + direction * state.HoldDistance;
                    position = holdPoint + Vector3.Transform(state.LocalOffset, aimRotation);
                    rotation = aimRotation * state.LocalRotation;
                    return true;

                case LeftHand:
                    if (_leftHand == null)
                        return false;
                    return HandTarget(_leftHand, state, out position, out rotation);

                case RightHand:
                    if (_rightHand == null)
                        return false;
                    return HandTarget(_rightHand, state, out position, out rotation);
            }

            return false;
        }

        private static bool HandTarget(
            IWorldElement hand, HeldState state, out Vector3 position, out Quaternion rotation)
        {
            var (handPosition, _, handRotation) = PoseOf(hand.WorldTransform);
            position = handPosition + Vector3.Transform(state.LocalOffset, handRotation);
            rotation = handRotation * state.LocalRotation;
            return true;
        }

        private void Release(int hand, bool throwHeld)
        {
            var state = _held[hand];
            if (state == null)
                return;

            _held[hand] = null;

            var body = state.Body;
            body.GravityScale = state.SavedGravityScale;
            body.LockRotation = state.SavedLockRotation;

            if (throwHeld)
            {
                var velocity = state.TargetVelocity * state.Grabbable.ThrowMultiplier * ThrowMultiplier;
                if (velocity.LengthSquared() > MaxThrowSpeed * MaxThrowSpeed)
                    velocity = Vector3.Normalize(velocity) * MaxThrowSpeed;
                body.LinearVelocity = velocity;
            }

            if (state.Grabbable.Owner != null)
                state.Grabbable.NotifyReleased(this, state.Element);
            try { Released?.Invoke(this, state.Grabbable); } catch { }

            if (hand == DesktopHand)
                _desktopReady = false;
        }

        /// <summary>Release every held body. Used on a reset or when the player
        /// switches between desktop and XR.</summary>
        public void ReleaseAll(bool throwHeld = false)
        {
            for (int hand = 0; hand < _held.Length; hand++)
                Release(hand, throwHeld);
            _desktopReady = true;
        }

        // ── Rig / aim helpers ───────────────────────────────────────────────

        private void ResolveRig(bool force)
        {
            if (_owner == null)
                return;

            if (force || _desktopCamera == null)
                _desktopCamera = FindCamera(_owner);

            if (force || _leftHand == null || _rightHand == null)
            {
                FindHands(_owner);
                if (force)
                    _desktopReady = true;
            }
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
            _leftHand = null;
            _rightHand = null;

            foreach (var child in Descendants(owner))
            {
                if (child.GetComponent<XRHandComponent>() is not { } hand)
                    continue;
                if (hand.Side == HandSide.Left) _leftHand = child;
                else _rightHand = child;
            }
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

        private IWorldElement? FindElementForBody(IPhysicsBody body)
        {
            return _root?.FindElement(element =>
            {
                var component = element.GetComponent<PhysicsBodyComponent>();
                return component?.Body != null && ReferenceEquals(component.Body, body);
            });
        }

        private (Vector3 origin, Vector3 direction, Quaternion rotation) CameraAim()
        {
            if (_desktopCamera == null)
                return (Vector3.Zero, -Vector3.UnitZ, Quaternion.Identity);

            return PoseOf(_desktopCamera.WorldTransform);
        }

        private (Vector3 origin, Vector3 direction, Quaternion rotation) HandAim(IWorldElement hand)
            => PoseOf(hand.WorldTransform);

        /// <summary>Extract a position, forward (-Z) and rotation from a world
        /// matrix without trusting its scale. Hand visualisers and models often
        /// carry a scale on the element that would corrupt a plain
        /// <see cref="Matrix4x4.Decompose"/>.</summary>
        private static (Vector3 origin, Vector3 direction, Quaternion rotation) PoseOf(Matrix4x4 matrix)
        {
            var origin = new Vector3(matrix.M41, matrix.M42, matrix.M43);

            var right = new Vector3(matrix.M11, matrix.M12, matrix.M13);
            var up = new Vector3(matrix.M21, matrix.M22, matrix.M23);
            var back = new Vector3(matrix.M31, matrix.M32, matrix.M33);

            if (right.LengthSquared() > 1e-8f) right = Vector3.Normalize(right); else right = Vector3.UnitX;
            if (up.LengthSquared() > 1e-8f) up = Vector3.Normalize(up); else up = Vector3.UnitY;
            if (back.LengthSquared() > 1e-8f) back = Vector3.Normalize(back); else back = Vector3.UnitZ;

            // Re-orthogonalise so a non-uniform scale cannot leave a skewed basis.
            var forward = -back;
            var correctedRight = Vector3.Normalize(Vector3.Cross(up, back));
            var correctedUp = Vector3.Cross(back, correctedRight);

            var basis = new Matrix4x4(
                correctedRight.X, correctedRight.Y, correctedRight.Z, 0f,
                correctedUp.X, correctedUp.Y, correctedUp.Z, 0f,
                back.X, back.Y, back.Z, 0f,
                0f, 0f, 0f, 1f);

            var rotation = Quaternion.CreateFromRotationMatrix(basis);
            return (origin, Vector3.Normalize(forward), rotation);
        }
    }
}
