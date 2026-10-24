using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.Loader;

namespace V12.Bindings
{
    /// <summary>
    /// Collectible <see cref="AssemblyLoadContext"/> used to load user-compiled C#
    /// script assemblies. To keep type identity intact with the engine
    /// (<see cref="ComponentBase"/>, <see cref="IWorldElement"/>, etc.) the load
    /// context resolves assemblies that already exist in the default context, or
    /// that were linked with <see cref="V12LinkedAssemblies"/>, instead of loading
    /// a second copy.
    /// <para>
    /// When <see cref="AssemblyLoadContext.Unload"/> is called (hot reload) the
    /// context becomes collectible and GC can reclaim it once all instances from
    /// that assembly are gone.
    /// </para>
    /// </summary>
    public sealed class CSharpScriptLoadContext : AssemblyLoadContext
    {
        private readonly ConcurrentDictionary<string, Assembly> _resolved = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Creates a collectible context named <paramref name="name"/>.</summary>
        public CSharpScriptLoadContext(string name) : base(name, isCollectible: true)
        {
        }

        /// <summary>Load an assembly from the given DLL path into this context.</summary>
        public Assembly LoadFrom(string assemblyPath)
        {
            if (string.IsNullOrWhiteSpace(assemblyPath)) throw new ArgumentException("assemblyPath is required.", nameof(assemblyPath));
            using var stream = File.OpenRead(assemblyPath);
            return LoadFromStream(stream);
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            if (assemblyName?.Name == null) return null;
            if (_resolved.TryGetValue(assemblyName.Name, out var cached)) return cached;

            var shared = FindShared(assemblyName);
            if (shared != null) _resolved[assemblyName.Name] = shared;
            return shared;
        }

        /// <summary>
        /// Looks for an assembly of this identity that the host already loaded.
        /// Returning null lets the runtime fall back to the default context.
        /// <para>
        /// The linked engine assemblies are checked FIRST. Under a host that
        /// loads the game into its own context (Godot's PluginLoadContext, a
        /// test runner, ...) the default context can hold a second copy of
        /// <c>V12.dll</c>; resolving against that copy gives the script a
        /// different <c>IComponent</c> identity than the live engine, so every
        /// type fails the registry's assignability check.
        /// </para>
        /// </summary>
        private static Assembly? FindShared(AssemblyName assemblyName)
        {
            // The assemblies the host is actually running against.
            foreach (var assembly in V12LinkedAssemblies.All)
            {
                if (Matches(assembly, assemblyName)) return assembly;
            }

            // Anything else already loaded, wherever it lives. Skip collectible
            // script sets: an older generation with the same name must not be
            // handed to the new one (linked assemblies were checked above).
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (AssemblyLoadContext.GetLoadContext(assembly)?.IsCollectible == true) continue;
                if (Matches(assembly, assemblyName)) return assembly;
            }

            // Framework assemblies that are only visible through the default
            // context (System.Runtime and friends).
            foreach (var assembly in Default.Assemblies)
            {
                if (Matches(assembly, assemblyName)) return assembly;
            }

            return null;
        }

        private static bool Matches(Assembly assembly, AssemblyName name)
            => !assembly.IsDynamic &&
               (assembly.FullName == name.FullName ||
                string.Equals(assembly.GetName().Name, name.Name, StringComparison.OrdinalIgnoreCase));
    }
}
