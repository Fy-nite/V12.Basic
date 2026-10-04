using System;
using System.Collections.Generic;
using System.Linq;
using V12.Components;
using V12.Core;
using V12.Core.Core.Interfaces;

namespace V12.Basic.Scene
{
    /// <summary>
    /// Find elements in a loaded scene (e.g. a WorldML file or a pak world) by name, path,
    /// tag, or component. Name lookups are case-insensitive; there is no name index, so these
    /// are linear scans — fine for scene-sized trees.
    /// </summary>
    public static class SceneQueryExtensions
    {
        private static bool NameIs(IWorldElement element, string name)
            => string.Equals(element.Name, name, StringComparison.OrdinalIgnoreCase);

        /// <summary>Depth-first walk over every element in the world (roots and descendants).</summary>
        public static IEnumerable<IWorldElement> Enumerate(this World world)
        {
            if (world == null) yield break;
            foreach (var root in world.Root.ToArray())
                foreach (var element in Walk(root))
                    yield return element;
        }

        private static IEnumerable<IWorldElement> Walk(IWorldElement element)
        {
            yield return element;
            foreach (var child in element.Children.ToArray())
                foreach (var descendant in Walk(child))
                    yield return descendant;
        }

        /// <summary>First element with the given name (optionally searching descendants).</summary>
        public static IWorldElement? Find(this World world, string name, bool recursive = true)
        {
            if (world == null || string.IsNullOrEmpty(name)) return null;

            foreach (var root in world.Root.ToArray())
            {
                if (NameIs(root, name)) return root;
                if (recursive)
                {
                    var found = root.FindChildByNameRecursive(name);
                    if (found != null) return found;
                }
            }
            return null;
        }

        /// <summary>All elements with the given name.</summary>
        public static List<IWorldElement> FindAll(this World world, string name, bool recursive = true)
        {
            var results = new List<IWorldElement>();
            if (world == null || string.IsNullOrEmpty(name)) return results;

            if (!recursive)
            {
                foreach (var root in world.Root)
                    if (NameIs(root, name)) results.Add(root);
                return results;
            }

            foreach (var element in world.Enumerate())
                if (NameIs(element, name)) results.Add(element);
            return results;
        }

        /// <summary>Like <see cref="Find"/>, but throws a descriptive exception when missing.</summary>
        public static IWorldElement Require(this World world, string name)
            => world.Find(name)
               ?? throw new InvalidOperationException(
                   $"No element named '{name}' in world '{world?.WorldName ?? "<null>"}'.");

        /// <summary>All elements carrying a <see cref="TagComponent"/> with the given tag.</summary>
        public static List<IWorldElement> FindByTag(this World world, string tag)
        {
            if (world == null || string.IsNullOrEmpty(tag)) return new List<IWorldElement>();
            return world.Enumerate()
                .Where(e => e.GetComponent<TagComponent>()?.HasTag(tag) == true)
                .ToList();
        }

        /// <summary>Follows a slash-separated child path, e.g. <c>"Player/CameraPitch/Camera3D"</c>.</summary>
        public static IWorldElement? FindByPath(this World world, string path)
        {
            if (world == null || string.IsNullOrEmpty(path)) return null;

            var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 0) return null;

            IWorldElement? node = world.Root.ToArray().FirstOrDefault(e => NameIs(e, parts[0]));
            for (int i = 1; i < parts.Length && node != null; i++)
                node = node.Children.FirstOrDefault(c => NameIs(c, parts[i]));

            return node;
        }

        /// <summary>The component of type <typeparamref name="T"/> on the element with the given name.</summary>
        public static T? FindComponent<T>(this World world, string name) where T : class, IComponent
            => world.Find(name)?.GetComponent<T>();

        // ── GameRoot overloads (search every active world: persistent + selected) ──

        public static IEnumerable<IWorldElement> Enumerate(this GameRoot root)
        {
            if (root == null) yield break;
            foreach (var world in root.ActiveWorlds)
                foreach (var element in world.Enumerate())
                    yield return element;
        }

        public static IWorldElement? Find(this GameRoot root, string name)
            => root.Enumerate().FirstOrDefault(e => NameIs(e, name));

        public static IWorldElement Require(this GameRoot root, string name)
            => root.Find(name)
               ?? throw new InvalidOperationException($"No element named '{name}' in any active world.");

        public static List<IWorldElement> FindByTag(this GameRoot root, string tag)
            => root.Enumerate().Where(e => e.GetComponent<TagComponent>()?.HasTag(tag) == true).ToList();

        public static T? FindComponent<T>(this GameRoot root, string name) where T : class, IComponent
            => root.Find(name)?.GetComponent<T>();
    }
}
