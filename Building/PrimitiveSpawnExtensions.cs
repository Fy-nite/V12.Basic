using System.Numerics;
using V12.Components;
using V12.Core;
using V12.Core.Core.Interfaces;

namespace V12.Basic.Building
{
    /// <summary>
    /// One-call primitives: creates an element wired with collider + physics body + mesh +
    /// renderer, adds it to the world, and returns it for further tweaks.
    /// <c>dynamic:false</c> (default) gives static/level geometry; <c>dynamic:true</c> gives
    /// a body that gravity and forces act on.
    /// </summary>
    public static class PrimitiveSpawnExtensions
    {
        public static Element SpawnPrimitive(
            this World world,
            MeshShape shape,
            Vector3 size,
            Vector3? at = null,
            bool dynamic = false,
            string? name = null,
            Vector3? rotationDegrees = null,
            bool collider = true,
            bool mesh = true)
        {
            var element = new Element(name ?? shape.ToString());
            element.SetTransform(at ?? Vector3.Zero, rotationDegrees);

            if (collider) element.AddCollider(shape, size.X, size.Y, size.Z);
            element.AddPhysics(kinematic: !dynamic);
            if (mesh) element.AddMesh(shape, size.X, size.Y, size.Z);

            world.AddElement(element);
            return element;
        }

        public static Element SpawnBox(this World world, Vector3? at = null, float width = 1f, float height = 1f, float depth = 1f, bool dynamic = false, string? name = null, Vector3? rotationDegrees = null)
            => world.SpawnPrimitive(MeshShape.Box, new Vector3(width, height, depth), at, dynamic, name, rotationDegrees);

        /// <summary>Sphere; <paramref name="radius"/> is the true radius (diameter is passed to the mesh/collider).</summary>
        public static Element SpawnSphere(this World world, Vector3? at = null, float radius = 0.5f, bool dynamic = false, string? name = null)
            => world.SpawnPrimitive(MeshShape.Sphere, new Vector3(radius * 2f), at, dynamic, name);

        public static Element SpawnCapsule(this World world, Vector3? at = null, float radius = 0.3f, float height = 1.8f, bool dynamic = false, string? name = null)
            => world.SpawnPrimitive(MeshShape.Capsule, new Vector3(radius * 2f, height, radius * 2f), at, dynamic, name);

        public static Element SpawnCylinder(this World world, Vector3? at = null, float radius = 0.5f, float height = 1f, bool dynamic = false, string? name = null)
            => world.SpawnPrimitive(MeshShape.Cylinder, new Vector3(radius * 2f, height, radius * 2f), at, dynamic, name);

        public static Element SpawnPlane(this World world, Vector3? at = null, float width = 1f, float depth = 1f, bool dynamic = false, string? name = null)
            => world.SpawnPrimitive(MeshShape.Plane, new Vector3(width, 0.1f, depth), at, dynamic, name);
    }
}
