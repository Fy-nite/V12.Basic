using System.Collections.Generic;
using System.Numerics;
using V12.Basic.Components;
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

        // Client-authoritative grab of a host-replicated element: while set, the
        // client drives the element locally and streams poses to the host via the
        // RPC channel (see PhysicsBodyComponent.[Remote] Grab/GrabMove/Release).
        private bool _replicatedGrab;
        private Vector3 _lastSentPose;

        /// <summary>Minimum pose delta before another GrabMove RPC is sent.</summary>
        private const float GrabMoveEpsilon = 0.002f;
        /// <summary>Re-issue the Grab RPC at least this often so a dropped
        /// unreliable RPC self-heals instead of leaving the host simulating.</summary>
        private const long GrabRepublishMs = 1000;
        private long _lastGrabRepublishMs;

        // Shared with main thread for laser visual
        public volatile bool HasRay;
        public Vector3 RayOrigin;
        public Vector3 RayHitPoint;
        public bool RayHitSomething;

        private const float RayLength = 20f;
        private const float GrabDistance = 2.5f;

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
                    _grabbedBody.Position = followPos;
                    _grabbedBody.LinearVelocity = Vector3.Zero;

                    if (_replicatedGrab && _grabbedElement != null)
                    {
                        var pbc = _grabbedElement.GetComponent<PhysicsBodyComponent>();
                        if (pbc != null)
                            PublishGrabbedPose(pbc, followPos);
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
                    var pbc = hitElement?.GetComponent<PhysicsBodyComponent>();
                    _replicatedGrab = pbc != null && pbc.IsReplicated;
                    _originalParent = null;

                    if (_replicatedGrab)
                    {
                        // Client-authoritative grab of a host-owned element: drive
                        // it locally and tell the host to pause simulation. The
                        // host mirrors our poses and relays them to every peer.
                        pbc.ClientHeld = true;
                        pbc.HeldPosition = bodyPos;
                        pbc.HeldRotation = _grabbedBody.Rotation;
                        pbc.LastGrabMoveMs = NowMs();
                        _lastSentPose = new Vector3(float.NaN, float.NaN, float.NaN);
                        _lastGrabRepublishMs = 0;
                        RpcDispatcher.CallOn(_gameRoot, _grabbedElement, "Grab");
                        PublishGrabbedPose(pbc, bodyPos);
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
                if (_replicatedGrab && _grabbedElement != null)
                {
                    var pbc = _grabbedElement.GetComponent<PhysicsBodyComponent>();
                    if (pbc != null)
                    {
                        var finalPos = _grabbedBody.Position;
                        var finalRot = _grabbedBody.Rotation;
                        pbc.ClientHeld = false;
                        pbc.HeldPosition = finalPos;
                        pbc.HeldRotation = finalRot;
                        pbc.LastGrabMoveMs = NowMs();
                        // Tell the host where we let go; it resumes simulation
                        // from this pose (the follower stays kinematic here and
                        // goes back to host-driven following).
                        RpcDispatcher.CallOn(_gameRoot, _grabbedElement, "Release",
                            finalPos.X, finalPos.Y, finalPos.Z,
                            finalRot.X, finalRot.Y, finalRot.Z, finalRot.W);
                    }
                    _grabbedElement = null;
                    _grabbedBody = null;
                    _replicatedGrab = false;
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
                    _replicatedGrab = false;
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

        /// <summary>Update the shared grab state on the holding client and stream
        /// the pose to the host when it moved enough (unreliable RPCs that drop
        /// self-heal on the next move; the host watchdog handles a vanished holder).</summary>
        private void PublishGrabbedPose(PhysicsBodyComponent pbc, Vector3 followPos)
        {
            long now = NowMs();
            var rot = _grabbedBody.Rotation;

            // Keep the local render + follower in sync even before the host relays:
            // LocalTransform also mirrors into the TransformComponent (renderer
            // prefers it when present).
            _grabbedElement.LocalTransform = new TRS
            {
                Position = followPos,
                Rotation = rot,
                Scale = Vector3.One
            };

            pbc.HeldPosition = followPos;
            pbc.HeldRotation = rot;
            pbc.LastGrabMoveMs = now;

            if (Vector3.Distance(followPos, _lastSentPose) >= GrabMoveEpsilon || now - _lastGrabRepublishMs >= GrabRepublishMs)
            {
                _lastSentPose = followPos;
                _lastGrabRepublishMs = now;
                RpcDispatcher.CallOn(_gameRoot, _grabbedElement, "GrabMove",
                    followPos.X, followPos.Y, followPos.Z,
                    rot.X, rot.Y, rot.Z, rot.W);
            }
        }

        private static long NowMs() => System.DateTime.UtcNow.Ticks / System.TimeSpan.TicksPerMillisecond;

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
