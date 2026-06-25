using System.Numerics;
using V12.Basic.Components;
using V12.Components;
using V12.Core.Core.Interfaces;
using V12.Core.Input;
using V12.Core.Interfaces.Physics;

namespace V12.Core.Systems
{
    public class PickupSystem : IGameService, IInputHandler
    {
        private readonly GameRoot _gameRoot;
        private IPhysicsBackend _physics;
        private bool _interactHeld;
        private IPhysicsBody _grabbedBody;
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
                }
            }
            else if (_grabbedBody != null)
            {
                _grabbedBody.SetKinematic(false);
                _grabbedBody.LinearVelocity = Vector3.Zero;
                _grabbedBody = null;
            }
        }

        private static IWorldElement FindPlayer(V12.Core.World world)
        {
            foreach (var e in world.Root)
                if (e.GetComponent<PlayerComponent>() != null)
                    return e;
            return null;
        }
    }
}
