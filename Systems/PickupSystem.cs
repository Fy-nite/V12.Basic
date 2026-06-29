using System.Collections.Generic;
using System.Numerics;
using V12.Basic.Components;
using V12.Components;
using V12.Core.Core.Interfaces;
using V12.Core.Input;
using V12.Core.Interfaces.Physics;
using V12.Core.Interfaces.Renderer;

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
            var world = _gameRoot.SelectedWorld;
            if (world == null || _physics == null) return;

            var player = FindPlayer(world);
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

            // Shared state for laser visual
            HasRay = true;
            RayOrigin = origin;
            RayHitPoint = hitPoint;
            RayHitSomething = didHit;

            if (_interactHeld)
            {
                if (_grabbedBody != null)
                {
                    var followPos = hitPoint + _grabOffset;
                    var dist = Vector3.Distance(followPos, origin);
                    if (dist > GrabDistance)
                        followPos = origin + direction * GrabDistance + _grabOffset;
                    _grabbedBody.Position = followPos;
                    _grabbedBody.LinearVelocity = Vector3.Zero;
                }
                else if (hitBody != null && hitBody.IsDynamic)
                {
                    _grabbedBody = hitBody;
                    var bodyPos = _grabbedBody.Position;
                    _grabOffset = bodyPos - hitPoint;
                    _grabbedBody.SetKinematic(true);
                    _grabbedBody.LinearVelocity = Vector3.Zero;

                    _grabbedElement = FindElementByBody(world, _grabbedBody);
                    _originalParent = null;
                    if (_grabbedElement != null && player.GetComponent<VRPlayerComponent>() != null)
                    {
                        var hand = FindHand(world);
                        if (hand != null)
                        {
                            _originalParent = _grabbedElement.Parent;
                            if (_originalParent != null)
                                _originalParent.RemoveChild(_grabbedElement);
                            else
                                world.Root.Remove(_grabbedElement);
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
                        world.AddElement(_grabbedElement);

                    _grabbedElement = null;
                    _originalParent = null;
                }
                _grabbedBody = null;
            }
        }

        private static IWorldElement FindPlayer(V12.Core.World world)
        {
            IWorldElement? fallback = null;
            foreach (var e in world.Root)
            {
                var pc = e.GetComponent<PlayerComponent>();
                if (pc == null) continue;
                fallback ??= e;
                if (e.GetComponent<VRPlayerComponent>() != null)
                    return e;
            }
            return fallback;
        }

        private static IWorldElement? FindHand(V12.Core.World world)
        {
            foreach (var e in AllElements(world.Root))
                if (e.Name == "XR_RightHand")
                    return e;
            return null;
        }

        private static IWorldElement? FindElementByBody(V12.Core.World world, IPhysicsBody body)
        {
            foreach (var e in AllElements(world.Root))
            {
                var pbc = e.GetComponent<PhysicsBodyComponent>();
                if (pbc?.Body == body)
                    return e;
            }
            return null;
        }

        private static IEnumerable<IWorldElement> AllElements(IEnumerable<IWorldElement> elements)
        {
            foreach (var e in elements)
            {
                yield return e;
                foreach (var child in AllElements(e.Children))
                    yield return child;
            }
        }
    }
}
