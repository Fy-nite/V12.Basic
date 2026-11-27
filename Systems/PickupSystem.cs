using System.Collections.Generic;
using System.Numerics;
using V12.Basic.Components;
using V12.Basic.Grabbing;
using V12.Components;
using V12.Core.Core.Interfaces;
using V12.Core.Input;
using V12.Core.Interfaces.Physics;
using V12.Core.Interfaces.Renderer;
using V12.Core.Networking;

namespace V12.Core.Systems
{
    public class PickupSystem : IGameService, IInputHandler
    {
        private readonly GameRoot _gameRoot;
        private IPhysicsBackend _physics;
        private bool _interactHeld;
        private IPhysicsBody _grabbedBody;
        private IWorldElement _grabbedElement;
        private IWorldElement _originalParent;
        private Vector3 _grabOffset;

        // Grabbing a replicated element: this peer is not the simulating user yet,
        // so it drives the body locally for responsiveness and asks the host for
        // authority. Once granted, the element ticks, steps and replicates here.
        private bool _remoteGrab;

        // Shared with main thread for laser visual
        public volatile bool HasRay;
        public Vector3 RayOrigin;
        public Vector3 RayHitPoint;
        public bool RayHitSomething;

        private const float RayLength = 20f;
        private const float GrabDistance = 2.5f;

        // The newer GrabberComponent owns grabbing when it is present. Keep the
        // legacy interact path from fighting it for the same input and body.
        private float _grabberProbeTimer;
        private bool _grabberOwnsInput;

        public PickupSystem(GameRoot gameRoot)
        {
            _gameRoot = gameRoot;
        }

        public void Initialize(GameRoot g)
        {
            _physics = g.Registry.Get<IPhysicsBackend>();
            var input = g.Registry.Get<InputService>();
            input?.RegisterHandler(this);
        }

        public void Initialize() { }
        public void Update(GameRoot g) { }

        public void OnInputEvent(InputEvent evt)
        {
            if (evt.Name == "interact")
            {
                if (evt.Type == InputEventType.ButtonDown)
                    _interactHeld = true;
                else if (evt.Type == InputEventType.ButtonUp)
                    _interactHeld = false;
            }
        }

        public void Update(float deltaTime)
        {
            if (_physics == null) return;

            if (!_grabberOwnsInput)
            {
                _grabberProbeTimer -= deltaTime;
                if (_grabberProbeTimer <= 0f)
                {
                    _grabberProbeTimer = 0.5f;
                    _grabberOwnsInput =
                        _gameRoot.FindElementWithComponent<GrabberComponent>() != null;
                }
            }
            if (_grabberOwnsInput) return;

            var player = FindPlayer();
            if (player == null) return;

            var playerComp = player.GetComponent<PlayerComponent>();
            if (playerComp == null) return;

            var aimRay = playerComp.GetAimRay();
            var origin = aimRay.origin;
            var direction = aimRay.direction;

            var ray = new Ray { Origin = origin, Direction = direction, MaxDistance = RayLength };

            bool didHit = _physics.Raycast(ray, out var hit);
            var hitBody = didHit ? hit.Body : null;
            var hitPoint = didHit ? hit.Point : origin + direction * RayLength;

            // Don't pick up the player's own body
            if (didHit && hitBody != null)
            {
                var playerBodyComp = player.GetComponent<PhysicsBodyComponent>();
                if (playerBodyComp?.Body == hitBody)
                {
                    didHit = false;
                    hitBody = null;
                }
            }

            IWorldElement hitElement = null;
            if (didHit && hitBody != null)
            {
                hitElement = _gameRoot.FindElement(e =>
                {
                    var pbc = e.GetComponent<PhysicsBodyComponent>();
                    return pbc?.Body == hitBody;
                });
            }

            // Shared state for laser visual
            HasRay = true;
            RayOrigin = origin;
            RayHitPoint = hitPoint;
            RayHitSomething = didHit;

            if (_interactHeld)
            {
                if (_grabbedBody != null)
                {
                    // Hold at a stable point along the aim ray: the ray often stops
                    // hitting the held object (it moved), and the hit-point fallback
                    // (ray end, 20m out) would yank it to the camera. Once grabbed,
                    // anchor to the clamped aim point instead of the hit point.
                    var followPos = origin + direction * GrabDistance + _grabOffset;

                    // The body is rebuilt when the host grants authority (the role
                    // flips from follower to dynamic), so re-resolve it instead of
                    // writing through a destroyed handle.
                    var livePbc = _grabbedElement?.GetComponent<PhysicsBodyComponent>();
                    if (livePbc?.Body != null && !ReferenceEquals(livePbc.Body, _grabbedBody))
                    {
                        _grabbedBody = livePbc.Body;
                        _grabbedBody.SetKinematic(true);
                        _grabbedBody.LinearVelocity = Vector3.Zero;
                    }

                    _grabbedBody.Position = followPos;
                    _grabbedBody.LinearVelocity = Vector3.Zero;

                    // The element pose is the render pose and, on a granted
                    // element, the pose the session replicates. Keeping it current
                    // every tick means the first update after a grant is correct.
                    if (_grabbedElement != null)
                    {
                        _grabbedElement.LocalTransform = new TRS
                        {
                            Position = followPos,
                            Rotation = _grabbedBody.Rotation,
                            Scale = Vector3.One
                        };
                    }
                }
                else if (hitBody != null && CanGrab(hitBody, hitElement))
                {
                    _grabbedBody = hitBody;
                    var bodyPos = _grabbedBody.Position;
                    _grabOffset = bodyPos - hitPoint;
                    _grabbedBody.SetKinematic(true);
                    _grabbedBody.LinearVelocity = Vector3.Zero;

                    _grabbedElement = hitElement;
                    _originalParent = null;
                    _remoteGrab = SimulationAuthority.NeedsClaim(_gameRoot, hitElement);

                    if (_remoteGrab)
                    {
                        // Ask the host to make this peer the simulating user. The
                        // body is driven locally in the meantime, so the grab feels
                        // immediate; a denied claim simply never arrives.
                        SimulationAuthority.Claim(_gameRoot, hitElement);

                        if (hitElement != null)
                        {
                            hitElement.LocalTransform = new TRS
                            {
                                Position = bodyPos,
                                Rotation = _grabbedBody.Rotation,
                                Scale = Vector3.One
                            };
                        }
                    }
                    else if (_grabbedElement != null && player.GetComponent<VRPlayerComponent>() != null)
                    {
                        var hand = _gameRoot.FindElementsWithComponent<VRPlayerComponent>()
                            .SelectMany(e => e.Children)
                            .FirstOrDefault(c => c.Name == "XR_RightHand")
                            ?? _gameRoot.FindElement(e => e.Name == "XR_RightHand");
                        if (hand != null)
                        {
                            _originalParent = _grabbedElement.Parent;
                            if (_originalParent != null)
                                _originalParent.RemoveChild(_grabbedElement);
                            else
                            {
                                var elWorld = _gameRoot.GetWorldForElement(_grabbedElement);
                                elWorld?.RemoveElement(_grabbedElement);
                            }
                            hand.AddChild(_grabbedElement);
                            _grabbedElement.LocalTransform = new TRS
                            {
                                Position = Vector3.Zero,
                                Rotation = Quaternion.Identity,
                                Scale = Vector3.One
                            };
                        }
                    }
                }
            }
            else if (_grabbedBody != null)
            {
                if (_remoteGrab && _grabbedElement != null)
                {
                    var finalPos = _grabbedBody.Position;
                    var finalRot = _grabbedBody.Rotation;

                    // Drop the held pose and hand the element back to the host,
                    // which resumes simulation from exactly here.
                    _grabbedElement.LocalTransform = new TRS
                    {
                        Position = finalPos,
                        Rotation = finalRot,
                        Scale = Vector3.One
                    };
                    _grabbedBody.SetKinematic(false);
                    _grabbedBody.LinearVelocity = Vector3.Zero;

                    SimulationAuthority.Release(_gameRoot, _grabbedElement);

                    _grabbedElement = null;
                    _grabbedBody = null;
                    _remoteGrab = false;
                }
                else
                {
                    _grabbedBody.SetKinematic(false);
                    _grabbedBody.LinearVelocity = Vector3.Zero;

                    if (_grabbedElement != null)
                    {
                        if (_grabbedElement.Parent != null)
                            _grabbedElement.Parent.RemoveChild(_grabbedElement);

                        _grabbedElement.LocalTransform = new TRS
                        {
                            Position = _grabbedBody.Position,
                            Rotation = Quaternion.Identity,
                            Scale = Vector3.One
                        };

                        if (_originalParent != null)
                            _originalParent.AddChild(_grabbedElement);
                        else
                        {
                            var targetWorld = _gameRoot.GetWorldForElement(_grabbedElement) ?? _gameRoot.SelectedWorld;
                            targetWorld?.AddElement(_grabbedElement);
                        }

                        _grabbedElement = null;
                        _originalParent = null;
                    }
                    _grabbedBody = null;
                    _remoteGrab = false;
                }
            }
        }

        /// <summary>An element is grabbable when its PhysicsBodyComponent is not
        /// kinematic (a dynamic object — local or host-replicated). Falls back to
        /// the body's dynamic flag when no component is found.</summary>
        private static bool CanGrab(IPhysicsBody body, IWorldElement hitElement)
        {
            var pbc = hitElement?.GetComponent<PhysicsBodyComponent>();
            if (pbc != null)
                return !pbc.IsKinematic;
            return body.IsDynamic;
        }

        private IWorldElement? FindPlayer()
        {
            // Use the ECS query API — searches PersistentWorld, SelectedWorld, etc.
            foreach (var e in _gameRoot.FindElementsWithComponent<PlayerComponent>())
            {
                if (e.GetComponent<VRPlayerComponent>() != null)
                    return e;
            }
            // Fallback to any player
            return _gameRoot.FindElementWithComponent<PlayerComponent>();
        }

    }
}
