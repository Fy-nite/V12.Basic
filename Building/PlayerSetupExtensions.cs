using System.Numerics;
using V12.Basic.Components;
using V12.Components;
using V12.Core;
using V12.Core.Core.Interfaces;

namespace V12.Basic.Building
{
    /// <summary>One-call desktop player setup.</summary>
    public static class PlayerSetupExtensions
    {
        /// <summary>
        /// Builds a controllable desktop player: <see cref="PlayerComponent"/> +
        /// <see cref="LocomotionComponent"/> + capsule collider + dynamic body + capsule mesh.
        /// The <c>PlayerCamera3D</c> child (with its <see cref="CameraComponent"/>) is created
        /// automatically by <see cref="PlayerComponent.OnAttach"/> — no manual camera wiring.
        /// For a player that survives world switches, call this on
        /// <c>gameRoot.PersistentWorld</c>.
        /// </summary>
        public static Element SpawnPlayer3D(
            this World world,
            Vector3? at = null,
            float moveSpeed = 5f,
            float jumpStrength = 6f,
            float gravity = 20f,
            float radius = 0.3f,
            float height = 1.8f,
            string name = "Player")
        {
            var player = new Element(name);
            player.SetTransform(at ?? new Vector3(0f, 1.5f, 0f));

            player.AddComponent(new PlayerComponent());
            player.AddComponent(new LocomotionComponent
            {
                MoveSpeed = moveSpeed,
                JumpStrength = jumpStrength,
                Gravity = gravity,
            });
            player.AddCollider(MeshShape.Capsule, radius * 2f, height, radius * 2f);
            player.AddPhysics(kinematic: false);
            player.AddMesh(MeshShape.Capsule, radius * 2f, height, radius * 2f);

            world.AddElement(player);
            return player;
        }
    }
}
