using Contract.Compiler.StandardLibrary;
using V12.Components;
using V12.Core.Core.Interfaces;

namespace V12.Bindings
{
    /// <summary>
    /// Component manipulation bindings. Callable from Contract as
    /// <c>V12.Components.AddMesh(id, 0, 2, 1, 1)</c> etc.
    /// Components are referenced by their type name ("Transform", "Mesh",
    /// "Material", "Collider") case-insensitively.
    /// </summary>
    [ClassBinding("V12.Components")]
    public static class V12Components
    {
        /// <summary>Add a TransformComponent to the element (repositions it if one exists).</summary>
        [MethodBinding]
        public static bool AddTransform(long elementId, float x, float y, float z)
        {
            var element = V12World.FindElement(elementId);
            if (element == null) return false;

            var transform = element.GetComponent<TransformComponent>();
            if (transform == null)
            {
                element.AddComponent(new TransformComponent(x, y, z));
                return true;
            }
            transform.SetPosition(x, y, z);
            return true;
        }

        /// <summary>Set an element's transform position (creates a TransformComponent if needed).</summary>
        [MethodBinding]
        public static bool SetPosition(long elementId, float x, float y, float z)
            => AddTransform(elementId, x, y, z);

        /// <summary>Add a MeshComponent. Shape: 0=Box, 1=Sphere, 2=Capsule, 3=Cylinder, 4=Plane.</summary>
        [MethodBinding]
        public static bool AddMesh(long elementId, int shape, float width, float height, float depth)
        {
            var element = V12World.FindElement(elementId);
            if (element == null) return false;
            if (!Enum.IsDefined(typeof(MeshShape), shape))
                shape = (int)MeshShape.Box;
            element.AddComponent(new MeshComponent((MeshShape)shape, width, height, depth));
            return true;
        }

        /// <summary>Change an existing mesh's shape (does not add a mesh).</summary>
        [MethodBinding]
        public static bool SetMeshShape(long elementId, int shape)
        {
            var mesh = V12World.FindElement(elementId)?.GetComponent<MeshComponent>();
            if (mesh == null) return false;
            if (!Enum.IsDefined(typeof(MeshShape), shape))
                shape = (int)MeshShape.Box;
            mesh.Shape = (MeshShape)shape;
            return true;
        }

        /// <summary>Add (or update) the element's material colour. Values are 0–1.</summary>
        [MethodBinding]
        public static bool SetColor(long elementId, float r, float g, float b, float a)
        {
            var element = V12World.FindElement(elementId);
            if (element == null) return false;

            var material = element.GetComponent<MaterialComponent>();
            if (material == null)
            {
                material = new MaterialComponent();
                element.AddComponent(material);
            }
            material.R = r;
            material.G = g;
            material.B = b;
            material.A = a;
            return true;
        }

        /// <summary>Add a ColliderComponent. Shape: 0=Box, 1=Sphere, 2=Capsule, 3=Cylinder, 4=Plane.</summary>
        [MethodBinding]
        public static bool AddCollider(long elementId, int shape, float width, float height, float depth)
        {
            var element = V12World.FindElement(elementId);
            if (element == null) return false;
            if (!Enum.IsDefined(typeof(MeshShape), shape))
                shape = (int)MeshShape.Box;
            element.AddComponent(new ColliderComponent((MeshShape)shape, width, height, depth));
            return true;
        }

        /// <summary>Whether the element has a component with the given type name.</summary>
        [MethodBinding]
        public static bool HasComponent(long elementId, string typeName)
            => V12World.FindElement(elementId)?.GetComponent(typeName) != null;

        /// <summary>Remove the first component whose type name matches.</summary>
        [MethodBinding]
        public static bool RemoveComponent(long elementId, string typeName)
        {
            var element = V12World.FindElement(elementId);
            if (element == null) return false;
            var component = element.GetComponent(typeName);
            if (component == null) return false;
            element.RemoveComponent(component);
            return true;
        }

        /// <summary>Comma-separated list of the element's component type names.</summary>
        [MethodBinding]
        public static string ComponentNames(long elementId)
        {
            var element = V12World.FindElement(elementId);
            if (element == null) return "";
            return string.Join(",", element.Components.Select(c => c.Name).Where(n => n != null));
        }

        [MethodBinding]
        public static bool SetActive(long elementId, bool active)
        {
            var element = V12World.FindElement(elementId);
            if (element == null) return false;
            element.Active = active;
            return true;
        }

        [MethodBinding]
        public static bool IsActive(long elementId)
            => V12World.FindElement(elementId)?.Active ?? false;
    }
}
