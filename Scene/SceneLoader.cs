using System;
using System.IO;
using System.Reflection;
using V12.Core;
using V12.Core.Interfaces;
using V12.WorldML;

namespace V12.Basic.Scene
{
    /// <summary>
    /// Loads a WorldML scene into a <see cref="World"/> on the game root. The returned world
    /// can then be bound with <see cref="SceneBindingExtensions.Bind(World)"/> and queried with
    /// <see cref="SceneQueryExtensions"/>. (For <c>.v12pak</c> archives, use V12.Pak's
    /// <c>GameRoot.LoadPak</c> from the host — it registers its worlds on the same GameRoot.)
    /// </summary>
    public static class SceneLoader
    {
        public static World LoadSceneFromXml(this GameRoot root, string xml, string? worldName = null, bool select = true)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            if (string.IsNullOrWhiteSpace(xml)) throw new ArgumentException("Scene XML is empty.", nameof(xml));

            var parser = new WorldMLParser();
            var templates = root.Registry.Get<ITemplateProvider>();
            if (templates != null) parser.TemplateProvider = templates;

            var sceneRoot = parser.Parse(xml);
            var world = new World(worldName ?? sceneRoot.Name ?? "Scene");
            world.AddElement(sceneRoot);
            root.Worlds.Add(world);
            if (select) root.SelectWorld(world);
            return world;
        }

        public static World LoadSceneFromFile(this GameRoot root, string path, string? worldName = null, bool select = true)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            if (!File.Exists(path)) throw new FileNotFoundException($"Scene file not found: {path}", path);
            return root.LoadSceneFromXml(File.ReadAllText(path), worldName ?? Path.GetFileNameWithoutExtension(path), select);
        }

        /// <summary>Loads a scene from an embedded resource in the engine assembly.</summary>
        public static World LoadSceneFromResource(this GameRoot root, string resourceName, string? worldName = null, bool select = true)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            var xml = root.ReadResource(resourceName);
            if (string.IsNullOrWhiteSpace(xml))
                throw new InvalidOperationException($"Embedded scene resource '{resourceName}' was not found or is empty (engine assembly).");
            return root.LoadSceneFromXml(xml, worldName ?? resourceName, select);
        }

        /// <summary>Loads a scene from an embedded resource in a specific assembly (e.g. your game assembly).</summary>
        public static World LoadSceneFromResource(this GameRoot root, Assembly assembly, string resourceName, string? worldName = null, bool select = true)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            if (assembly == null) throw new ArgumentNullException(nameof(assembly));

            using var stream = assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException($"Embedded scene resource '{resourceName}' was not found in assembly '{assembly.GetName().Name}'.");
            using var reader = new StreamReader(stream);
            return root.LoadSceneFromXml(reader.ReadToEnd(), worldName ?? resourceName, select);
        }
    }
}
