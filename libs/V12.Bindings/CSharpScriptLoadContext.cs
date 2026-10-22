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
        /// </summary>
        private static Assembly? FindShared(AssemblyName assemblyName)
        {
            foreach (var assembly in Default.Assemblies)
            {
                if (Matches(assembly, assemblyName)) return assembly;
            }

            // Pak DLLs and other runtime-compiled script sets are linked here;
            // they may live in a different collectible context than ours.
            foreach (var assembly in V12LinkedAssemblies.All)
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
