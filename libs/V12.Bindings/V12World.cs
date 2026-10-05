using Contract.Compiler.StandardLibrary;
using ObjektRT.Core.Attributes;
using V12.Components;
using V12.Core;
using V12.Core.Core.Interfaces;

namespace V12.Bindings
{
    /// <summary>
    /// World and element structure bindings. Callable from Contract as
    /// <c>V12.World.SpawnElement("crate")</c> etc.
    /// Elements are referenced by their stable <c>long</c> id; 0 means
    /// "no such element". Spawned elements land in the selected world (or the
    /// persistent world when none is selected).
    /// </summary>
    [ClassBinding("V12.World")]
    public static class V12World
    {
        /// <summary>Spawn a root element in the current world and return its id (0 on failure).</summary>
        [MethodBinding]
        public static long SpawnElement(string name)
        {
            var world = TargetWorld();
            if (world == null) return 0;
            var element = new Element(name);
            world.AddElement(element);
            return element.Id;
        }

        /// <summary>Spawn a root element with a TransformComponent at a position.</summary>
        [MethodBinding]
        public static long SpawnElementAt(string name, float x, float y, float z)
        {
            var id = SpawnElement(name);
            if (id == 0) return 0;
            V12Components.AddTransform(id, x, y, z);
            return id;
        }

        /// <summary>Spawn an element as a child of <paramref name="parentId"/>.</summary>
        [MethodBinding]
        public static long SpawnChild(long parentId, string name)
        {
            var parent = FindElement(parentId);
            if (parent == null) return 0;
            var child = new Element(name);
            parent.AddChild(child);
            return child.Id;
        }

        /// <summary>Remove an element (root or child) from the world.</summary>
        [MethodBinding]
        public static bool Despawn(long elementId)
        {
            var element = FindElement(elementId);
            if (element == null) return false;

            if (element.Parent != null)
            {
                element.Parent.RemoveChild(element);
                return true;
            }

            var world = GameRoot.Instance?.GetWorldForElement(element);
            if (world != null)
            {
                world.RemoveElement(element);
                return true;
            }
            return false;
        }

        /// <summary>Whether an element with the given id exists in any active world.</summary>
        [MethodBinding]
        public static bool HasElement(long elementId)
            => FindElement(elementId) != null;

        /// <summary>First element (across active worlds) with a matching name, or 0.</summary>
        [MethodBinding]
        public static long FindByName(string name)
        {
            var element = GameRoot.Instance?.FindElement(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));
            return element?.Id ?? 0;
        }

        [MethodBinding]
        public static string GetName(long elementId)
            => FindElement(elementId)?.Name ?? "";

        [MethodBinding]
        public static bool SetName(long elementId, string name)
        {
            var element = FindElement(elementId);
            if (element == null) return false;
            element.Name = name;
            return true;
        }

        /// <summary>Set the element's world position (creates a TransformComponent if needed).</summary>
        [MethodBinding]
        public static bool SetPosition(long elementId, float x, float y, float z)
            => V12Components.SetPosition(elementId, x, y, z);

        [MethodBinding]
        public static float GetPositionX(long elementId)
            => FindTransform(elementId)?.X ?? 0f;

        [MethodBinding]
        public static float GetPositionY(long elementId)
            => FindTransform(elementId)?.Y ?? 0f;

        [MethodBinding]
        public static float GetPositionZ(long elementId)
            => FindTransform(elementId)?.Z ?? 0f;

        /// <summary>Number of root elements in the current world.</summary>
        [MethodBinding]
        public static int ElementCount()
            => TargetWorld()?.Root.Count ?? 0;

        /// <summary>Name of the current world.</summary>
        [MethodBinding]
        public static string WorldName()
            => TargetWorld()?.WorldName ?? "";

        /// <summary>Name of the currently selected world (may differ from the target spawn world).</summary>
        [MethodBinding]
        public static string SelectedWorldName()
            => GameRoot.Instance?.SelectedWorld?.WorldName ?? "";

        private static World? TargetWorld()
            => GameRoot.Instance?.SelectedWorld ?? GameRoot.Instance?.PersistentWorld;

        internal static IWorldElement? FindElement(long elementId)
        {
            var root = GameRoot.Instance;
            if (root == null || elementId <= 0) return null;
            return root.FindElements(e => e.Id == elementId).FirstOrDefault();
        }

        internal static TransformComponent? FindTransform(long elementId)
            => FindElement(elementId)?.GetComponent<TransformComponent>();
    }
}
