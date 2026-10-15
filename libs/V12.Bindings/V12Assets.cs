using System.IO;
using Contract.Compiler.StandardLibrary;
using ObjektRT.Core.Attributes;
using V12.Core;
using V12.Core.Interfaces;

namespace V12.Bindings
{
    /// <summary>
    /// Asset access bindings backed by the engine's registered
    /// <see cref="IAssetResolver"/>. Callable from Contract as
    /// <c>V12.Assets.ReadAllText("Assets/textures/models/block/dirt_0.json")</c>.
    ///
    /// Paths may be:
    /// <list type="bullet">
    /// <item><c>v12://&lt;pak&gt;/...</c> URIs (resolved through the mount),</item>
    /// <item>
    /// paths relative to a mounted pak root — a loaded <c>.v12pak</c> is
    /// mounted under its file name, so <c>"Assets/..."</c> addresses the
    /// extracted pak content without knowing the temp directory,
    /// </item>
    /// <item>absolute filesystem paths.</item>
    /// </list>
    /// A missing file/directory reads as "" / an empty list instead of
    /// throwing, so scripts can probe for optional content.
    /// </summary>
    [ClassBinding("V12.Assets")]
    public static class V12Assets
    {
        /// <summary>Resolve a path/URI to an absolute filesystem path, or "" when unresolvable.</summary>
        [MethodBinding]
        public static string Resolve(string path)
            => ResolveFile(path) ?? "";

        /// <summary>True when the path resolves to an existing file.</summary>
        [MethodBinding]
        public static bool Exists(string path)
            => ResolveFile(path) != null;

        /// <summary>Read a text file ("" when missing/unresolvable).</summary>
        [MethodBinding]
        public static string ReadAllText(string path)
        {
            var resolved = ResolveFile(path);
            return resolved == null ? "" : File.ReadAllText(resolved);
        }

        /// <summary>File names (not paths) directly inside a directory; empty when missing.</summary>
        [MethodBinding]
        public static string[] GetFiles(string directoryPath)
        {
            var dir = ResolveDirectory(directoryPath);
            if (dir == null) return Array.Empty<string>();
            var names = new List<string>();
            foreach (var file in Directory.GetFiles(dir))
            {
                var name = Path.GetFileName(file);
                if (!string.IsNullOrEmpty(name)) names.Add(name);
            }
            return names.ToArray();
        }

        /// <summary>Sub-directory names directly inside a directory; empty when missing.</summary>
        [MethodBinding]
        public static string[] GetDirectories(string directoryPath)
        {
            var dir = ResolveDirectory(directoryPath);
            if (dir == null) return Array.Empty<string>();
            var names = new List<string>();
            foreach (var sub in Directory.GetDirectories(dir))
            {
                var name = Path.GetFileName(sub);
                if (!string.IsNullOrEmpty(name)) names.Add(name);
            }
            return names.ToArray();
        }

        // ── Resolution ───────────────────────────────────────────────────

        private static string? ResolveFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;

            if (path.StartsWith("v12://", StringComparison.OrdinalIgnoreCase))
            {
                var resolver = GameRoot.Instance?.Registry?.Get<IAssetResolver>();
                var resolved = resolver?.Resolve(path) ?? path;
                return File.Exists(resolved) ? resolved : null;
            }

            if (Path.IsPathRooted(path))
                return File.Exists(path) ? path : null;

            // Relative: every mounted pak root, then the raw CWD-relative path.
            foreach (var root in MountPaths())
            {
                var candidate = Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(candidate)) return candidate;
            }
            return File.Exists(path) ? Path.GetFullPath(path) : null;
        }

        private static string? ResolveDirectory(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;

            if (path.StartsWith("v12://", StringComparison.OrdinalIgnoreCase))
            {
                var resolver = GameRoot.Instance?.Registry?.Get<IAssetResolver>();
                var resolved = resolver?.Resolve(path) ?? path;
                return Directory.Exists(resolved) ? resolved : null;
            }

            if (Path.IsPathRooted(path))
                return Directory.Exists(path) ? path : null;

            foreach (var root in MountPaths())
            {
                var candidate = Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar));
                if (Directory.Exists(candidate)) return candidate;
            }
            return Directory.Exists(path) ? Path.GetFullPath(path) : null;
        }

        private static IEnumerable<string> MountPaths()
        {
            if (GameRoot.Instance?.Registry?.Get<IAssetResolver>() is V12AssetResolver resolver)
                return resolver.GetMountPaths();
            return Array.Empty<string>();
        }
    }
}
