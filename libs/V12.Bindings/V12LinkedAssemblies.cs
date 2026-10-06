using System.Reflection;
using V12.Core;

namespace V12.Bindings
{
    /// <summary>
    /// The set of real .NET assemblies linked into every V12 Contract script.
    ///
    /// By default this is the V12 engine assembly itself (<see cref="V12.dll"/>),
    /// so a <c>.ct</c> script can call the engine's public API directly by its
    /// CLR name (<c>new V12.Components.TransformComponent(...)</c>,
    /// <c>V12.Core.GameRoot.Instance</c>) with no <c>[ClassBinding]</c> wrapper.
    /// Additional assemblies can be registered with <see cref="Add"/> (e.g. a
    /// game's own DLL exposed to its scripts).
    /// </summary>
    public static class V12LinkedAssemblies
    {
        private static readonly object Gate = new();
        private static readonly List<Assembly> Extra = new();

        /// <summary>The V12 engine assembly (contains <c>V12.Core</c>, <c>V12.Components</c>, …).</summary>
        public static Assembly EngineAssembly { get; } = typeof(GameRoot).Assembly;

        /// <summary>Link an additional assembly into V12 Contract scripts.</summary>
        public static void Add(Assembly assembly)
        {
            if (assembly == null) return;
            lock (Gate)
            {
                if (!Extra.Contains(assembly))
                    Extra.Add(assembly);
            }
        }

        /// <summary>Every assembly linked into V12 Contract scripts (engine + extras), de-duplicated.</summary>
        public static IReadOnlyList<Assembly> All
        {
            get
            {
                lock (Gate)
                    return new[] { EngineAssembly }.Concat(Extra).Distinct().ToList();
            }
        }
    }
}
