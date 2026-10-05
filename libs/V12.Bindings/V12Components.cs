using System.Reflection;
using Contract.Compiler.StandardLibrary;
using ObjektRT.Core.Attributes;
using V12.Components;
using V12.Core;
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

        /// <summary>Add or update a MeshComponent. Shape: 0=Box, 1=Sphere, 2=Capsule, 3=Cylinder, 4=Plane.</summary>
        [MethodBinding]
        public static bool AddMesh(long elementId, int shape, float width, float height, float depth)
        {
            var element = V12World.FindElement(elementId);
            if (element == null) return false;
            if (!Enum.IsDefined(typeof(MeshShape), shape))
                shape = (int)MeshShape.Box;

            var mesh = EnsureMesh(element);
            mesh.Shape = (MeshShape)shape;
            mesh.Width = width;
            mesh.Height = height;
            mesh.Depth = depth;
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

        /// <summary>Add or update a ColliderComponent. Shape: 0=Box, 1=Sphere, 2=Capsule, 3=Cylinder, 4=Plane.</summary>
        [MethodBinding]
        public static bool AddCollider(long elementId, int shape, float width, float height, float depth)
        {
            var element = V12World.FindElement(elementId);
            if (element == null) return false;
            if (!Enum.IsDefined(typeof(MeshShape), shape))
                shape = (int)MeshShape.Box;

            var collider = EnsureCollider(element);
            collider.Shape = (MeshShape)shape;
            collider.Width = width;
            collider.Height = height;
            collider.Depth = depth;
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

        // ── Contract (.ct) components ──────────────────────────────────

        /// <summary>Attach a component type defined in a .ct file (e.g. "Spin").</summary>
        [MethodBinding]
        public static bool AddContractComponent(long elementId, string typeName)
        {
            var element = V12World.FindElement(elementId);
            if (element == null || string.IsNullOrEmpty(typeName)) return false;
            var registry = ContractComponentRegistry.FromGameRoot();
            return registry != null && registry.Attach(element, typeName);
        }

        /// <summary>Comma-separated list of every registered .ct component type.</summary>
        [MethodBinding]
        public static string ContractComponentTypes()
            => ContractComponentRegistry.FromGameRoot() is { } r ? string.Join(",", r.TypeNames) : "";

        /// <summary>Attach a ScriptComponent that loads a .ct/.lua file (path relative to the asset resolver).</summary>
        [MethodBinding]
        public static bool AttachScript(long elementId, string source)
        {
            var element = V12World.FindElement(elementId);
            if (element == null) return false;
            element.AddComponent(new ScriptComponent { Source = source });
            return true;
        }

        // ── Generic component attach ───────────────────────────────────

        /// <summary>
        /// Attach (or ensure) a component by type name: built-ins ("Transform",
        /// "Mesh", "Collider", "Material", "Script") or a registered .ct type.
        /// </summary>
        [MethodBinding]
        public static bool AddComponent(long elementId, string typeName)
        {
            var element = V12World.FindElement(elementId);
            if (element == null || string.IsNullOrEmpty(typeName)) return false;

            switch (typeName.Trim().ToLowerInvariant())
            {
                case "transform": return EnsureTransform(element) != null;
                case "mesh": return EnsureMesh(element) != null;
                case "collider": return EnsureCollider(element) != null;
                case "material": return EnsureMaterial(element) != null;
                case "script":
                    if (element.GetComponent<ScriptComponent>() == null)
                        element.AddComponent(new ScriptComponent());
                    return true;
                default:
                    var registry = ContractComponentRegistry.FromGameRoot();
                    return registry != null && registry.Attach(element, typeName);
            }
        }

        // ── Component property access ──────────────────────────────────

        /// <summary>Read a float property (e.g. GetFloat(id, "Mesh", "Width")).</summary>
        [MethodBinding]
        public static float GetFloat(long elementId, string typeName, string field)
            => ComponentFloat(elementId, typeName, field, null) ?? 0f;

        /// <summary>Write a float property (e.g. SetFloat(id, "Material", "Metallic", 0.8)).</summary>
        [MethodBinding]
        public static bool SetFloat(long elementId, string typeName, string field, float value)
            => ComponentFloat(elementId, typeName, field, value).HasValue;

        /// <summary>Read a string property.</summary>
        [MethodBinding]
        public static string GetString(long elementId, string typeName, string field)
            => GetComponentProperty(elementId, typeName, field) as string ?? "";

        /// <summary>Write a string property.</summary>
        [MethodBinding]
        public static bool SetString(long elementId, string typeName, string field, string value)
            => TrySetComponentProperty(elementId, typeName, field, value);

        /// <summary>Read a bool property (e.g. GetBool(id, "Collider", "IsTrigger")).</summary>
        [MethodBinding]
        public static bool GetBool(long elementId, string typeName, string field)
            => GetComponentProperty(elementId, typeName, field) is bool b && b;

        /// <summary>Write a bool property.</summary>
        [MethodBinding]
        public static bool SetBool(long elementId, string typeName, string field, bool value)
            => TrySetComponentProperty(elementId, typeName, field, value);

        // ── Enumeration ────────────────────────────────────────────────

        /// <summary>Comma-separated ids of every element across active worlds.</summary>
        [MethodBinding]
        public static string ElementIdsCsv()
        {
            var root = GameRoot.Instance;
            return root == null ? "" : string.Join(",", root.FindElements(_ => true).Select(e => e.Id));
        }

        /// <summary>Comma-separated ids of an element's direct children.</summary>
        [MethodBinding]
        public static string ChildrenCsv(long elementId)
        {
            var element = V12World.FindElement(elementId);
            return element == null ? "" : string.Join(",", element.Children.Select(c => c.Id));
        }

        // ── Helpers ────────────────────────────────────────────────────

        private static TransformComponent EnsureTransform(IWorldElement element)
        {
            var transform = element.GetComponent<TransformComponent>();
            if (transform == null)
            {
                transform = new TransformComponent(0f, 0f, 0f);
                element.AddComponent(transform);
            }
            return transform;
        }

        private static MeshComponent EnsureMesh(IWorldElement element)
        {
            var mesh = element.GetComponent<MeshComponent>();
            if (mesh == null)
            {
                mesh = new MeshComponent(MeshShape.Box, 1f, 1f, 1f);
                element.AddComponent(mesh);
            }
            return mesh;
        }

        private static ColliderComponent EnsureCollider(IWorldElement element)
        {
            var collider = element.GetComponent<ColliderComponent>();
            if (collider == null)
            {
                collider = new ColliderComponent(MeshShape.Box, 1f, 1f, 1f);
                element.AddComponent(collider);
            }
            return collider;
        }

        private static MaterialComponent EnsureMaterial(IWorldElement element)
        {
            var material = element.GetComponent<MaterialComponent>();
            if (material == null)
            {
                material = new MaterialComponent();
                element.AddComponent(material);
            }
            return material;
        }

        private static IComponent? FindComponent(long elementId, string typeName)
        {
            var element = V12World.FindElement(elementId);
            if (element == null || string.IsNullOrEmpty(typeName)) return null;

            var byName = element.GetComponent(typeName);
            if (byName != null) return byName;

            return element.Components.FirstOrDefault(c =>
                string.Equals(c.GetType().Name, typeName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(c.GetType().Name, typeName + "Component", StringComparison.OrdinalIgnoreCase));
        }

        private static PropertyInfo? FindProperty(long elementId, string typeName, string field)
        {
            if (string.IsNullOrEmpty(field)) return null;
            return FindComponent(elementId, typeName)?.GetType()
                .GetProperty(field, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
        }

        private static object? GetComponentProperty(long elementId, string typeName, string field)
        {
            var component = FindComponent(elementId, typeName);
            var property = FindProperty(elementId, typeName, field);
            if (component == null || property == null || !property.CanRead) return null;
            try { return property.GetValue(component); } catch { return null; }
        }

        private static bool TrySetComponentProperty(long elementId, string typeName, string field, object? value)
        {
            var component = FindComponent(elementId, typeName);
            var property = FindProperty(elementId, typeName, field);
            if (component == null || property == null || !property.CanWrite) return false;
            try
            {
                var target = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
                property.SetValue(component, value == null ? null : Convert.ChangeType(value, target));
                return true;
            }
            catch { return false; }
        }

        private static float? ComponentFloat(long elementId, string typeName, string field, float? value)
        {
            if (value.HasValue)
                return TrySetComponentProperty(elementId, typeName, field, value.Value) ? value : null;

            var current = GetComponentProperty(elementId, typeName, field);
            try { return current == null ? 0f : Convert.ToSingle(current); }
            catch { return null; }
        }
    }
}
