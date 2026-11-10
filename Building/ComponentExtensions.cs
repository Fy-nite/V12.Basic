using System;
using System.Numerics;
using V12.Basic.Components;
using V12.Components;
using V12.Components.Renderables;
using V12.Core;
using V12.Core.Core.Interfaces;
using V12.Core.Interfaces.Physics;
using V12.Core.Interfaces.Renderer;

namespace V12.Basic.Building
{
    /// <summary>
    /// Shorthands for attaching the standard components to an element. Each returns the
    /// created component so calls can chain, e.g.
    /// <c>element.AddCollider(MeshShape.Box).Width = 2f;</c>.
    /// </summary>
    public static class ElementExtensions
    {
        /// <summary>Returns the existing component of type <typeparamref name="T"/> or adds a new one.</summary>
        public static T GetOrAdd<T>(this IWorldElement element) where T : ComponentBase, new()
        {
            var existing = element.GetComponent<T>();
            if (existing != null) return existing;
            var created = new T();
            element.AddComponent(created);
            return created;
        }

        /// <summary>Returns a view over the element's own transform, or null.
        /// Non-generic so Contract scripts can call it across assembly-link
        /// (generic methods don't cross the v1 boundary).</summary>
        public static TransformHandle? GetTransform(this IWorldElement element)
            => element == null ? null : new TransformHandle(element);

        /// <summary>Returns a view over the element's own transform. No component
        /// is created: the pose lives on <see cref="IWorldElement.LocalTransform"/>.
        /// Non-generic so Contract scripts can call it across assembly-link:
        /// <c>this.Owner.GetOrAddTransform().RY = ...</c>.</summary>
        public static TransformHandle GetOrAddTransform(this IWorldElement element)
            => new(element);

        /// <summary>Returns the element's <see cref="MeshComponent"/>, or null (non-generic, script-callable).</summary>
        public static MeshComponent? GetMesh(this IWorldElement element)
            => element.GetComponent<MeshComponent>();

        /// <summary>Returns the element's <see cref="ColliderComponent"/>, or null (non-generic, script-callable).</summary>
        public static ColliderComponent? GetCollider(this IWorldElement element)
            => element.GetComponent<ColliderComponent>();

        /// <summary>Adds a <see cref="MeshComponent"/> and its matching <see cref="MeshRenderer"/>.</summary>
        public static MeshComponent AddMesh(this IWorldElement element, MeshShape shape, float width = 1f, float height = 1f, float depth = 1f)
        {
            var mesh = new MeshComponent(shape, width, height, depth);
            element.AddComponent(mesh);
            element.AddComponent(new MeshRenderer { Mesh = mesh });
            return mesh;
        }

        public static ColliderComponent AddCollider(this IWorldElement element, MeshShape shape, float width = 1f, float height = 1f, float depth = 1f, bool isTrigger = false)
        {
            var collider = new ColliderComponent(shape, width, height, depth, isTrigger);
            element.AddComponent(collider);
            return collider;
        }

        /// <summary>
        /// Adds a physics body. <paramref name="kinematic"/> = true for static/level geometry,
        /// false for dynamic bodies that gravity and forces act on.
        /// </summary>
        public static PhysicsBodyComponent AddPhysics(this IWorldElement element, bool kinematic = false, float gravityScale = 1f)
            => AddPhysics(element, kinematic ? PhysicsBodyType.Kinematic : PhysicsBodyType.Dynamic, gravityScale);

        /// <summary>
        /// Adds a physics body with an explicit <see cref="PhysicsBodyType"/>. Use
        /// <see cref="PhysicsBodyType.Character"/> for a move-and-slide controller,
        /// then drive it by setting <see cref="PhysicsBodyComponent.LinearVelocity"/>
        /// or calling <see cref="PhysicsBodyComponent.AddForce"/> from game code.
        /// </summary>
        public static PhysicsBodyComponent AddPhysics(this IWorldElement element, PhysicsBodyType type, float gravityScale = 1f)
        {
            var body = new PhysicsBodyComponent { BodyType = type, GravityScale = gravityScale };
            element.AddComponent(body);
            return body;
        }

        public static MaterialComponent AddMaterial(this IWorldElement element, float r = 1f, float g = 1f, float b = 1f, float a = 1f, float metallic = 0f, float roughness = 0.5f)
        {
            var material = new MaterialComponent(r, g, b, a, metallic, roughness);
            element.AddComponent(material);
            return material;
        }

        public static PointLightComponent AddPointLight(this IWorldElement element, float r = 1f, float g = 1f, float b = 1f, float range = 10f, float energy = 1f)
        {
            var light = new PointLightComponent(r, g, b, range, energy);
            element.AddComponent(light);
            return light;
        }

        public static SpotLightComponent AddSpotLight(this IWorldElement element, float r = 1f, float g = 1f, float b = 1f, float range = 15f, float energy = 1f, float angle = 45f, float softness = 0.5f)
        {
            var light = new SpotLightComponent(r, g, b, range, energy, angle, softness);
            element.AddComponent(light);
            return light;
        }

        public static GenericLightComponent AddDirectionalLight(this IWorldElement element, float r = 1f, float g = 1f, float b = 1f, float energy = 1f)
        {
            var light = new GenericLightComponent(r, g, b, energy) { Type = LightType.Directional };
            element.AddComponent(light);
            return light;
        }

        /// <summary>Adds a tag marker (<see cref="TagComponent"/>) with one or more tags.</summary>
        public static TagComponent AddTags(this IWorldElement element, params string[] tags)
        {
            var tag = new TagComponent(tags);
            element.AddComponent(tag);
            return tag;
        }

        /// <summary>
        /// Sets position and optional rotation in degrees on the element's own
        /// <see cref="IWorldElement.LocalTransform"/>. No TransformComponent is
        /// created or kept in sync because there is only one pose now.
        /// </summary>
        public static TransformHandle SetTransform(this IWorldElement element, Vector3 position, Vector3? rotationDegrees = null)
        {
            const float deg2rad = MathF.PI / 180f;
            var rotation = rotationDegrees ?? Vector3.Zero;

            var quaternion = Quaternion.CreateFromYawPitchRoll(
                rotation.Y * deg2rad,
                rotation.X * deg2rad,
                rotation.Z * deg2rad);

            element.LocalTransform = new TRS
            {
                Position = position,
                Rotation = quaternion,
                Scale = element.LocalTransform.Scale,
            };
            return new TransformHandle(element);
        }

        public static TransformHandle SetPosition(this IWorldElement element, float x, float y, float z)
            => element.SetTransform(new Vector3(x, y, z));

        public static TransformHandle SetRotation(this IWorldElement element, float xDegrees, float yDegrees, float zDegrees)
            => element.SetTransform(element.LocalTransform.Position, new Vector3(xDegrees, yDegrees, zDegrees));

        /// <summary>
        /// Adds a raycast-interactive <see cref="ButtonComponent"/>. Give the element a collider
        /// and a (kinematic) physics body so the aim ray can hit it.
        /// </summary>
        public static ButtonComponent AddButton(this IWorldElement element, string label = "Button", Action? onPressed = null)
        {
            var button = new ButtonComponent { Label = label, OnPressed = onPressed };
            element.AddComponent(button);
            return button;
        }

        /// <summary>
        /// Kept for source compatibility with old scene-loading code. Elements now
        /// own their pose directly, so there is no TransformComponent to copy back.
        /// </summary>
        [Obsolete("Elements own their pose in LocalTransform. This method is a no-op.")]
        public static void SyncLocalTransforms(this World world)
        {
        }
    }
}
