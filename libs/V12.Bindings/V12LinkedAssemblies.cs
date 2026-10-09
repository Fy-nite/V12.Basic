using System.Reflection;
using V12.Core;

namespace V12.Bindings
{
    /// <summary>
    /// The set of real .NET assemblies linked into every V12 Contract script.
    ///
    /// By default this is the V12 engine assembly itself (<see cref="V12.dll"/>)
    /// plus V12.Basic (the <c>ElementBuilder</c>, component extension
    /// shorthands, and spawn helpers), so a <c>.ct</c> script can call the
    /// engine's public API directly by its CLR name — the C#-style object
    /// surface: <c>new V12.Basic.Building.ElementBuilder("x").Box(1,1,1)</c>,
    /// <c>element.AddComponent(new V12.Components.PointLightComponent())</c>,
    /// <c>this.Owner.GetOrAddTransform().RY = ...</c> — with no
    /// <c>[ClassBinding]</c> wrapper needed. Additional assemblies can be
    /// registered with <see cref="Add"/> (e.g. a game's own DLL shipped inside
    /// a <c>.v12pak</c>, linked automatically when the pak loads).
    /// </summary>
    public static class V12LinkedAssemblies
    {
        private static readonly object Gate = new();
        private static readonly List<Assembly> Extra = new();

        /// <summary>The V12 engine assembly (contains <c>V12.Core</c>, <c>V12.Components</c>, ...).</summary>
        public static Assembly EngineAssembly { get; } = typeof(GameRoot).Assembly;

        /// <summary>V12.Basic (contains <c>V12.Basic.Building</c> — ElementBuilder, component extensions).</summary>
        public static Assembly BasicAssembly { get; } = typeof(V12.Basic.Building.ElementBuilder).Assembly;

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

        /// <summary>Every assembly linked into V12 Contract scripts (engine + V12.Basic + extras), de-duplicated.</summary>
        public static IReadOnlyList<Assembly> All
        {
            get
            {
                lock (Gate)
                    return new[] { EngineAssembly, BasicAssembly }.Concat(Extra).Distinct().ToList();
            }
        }
    }
}
