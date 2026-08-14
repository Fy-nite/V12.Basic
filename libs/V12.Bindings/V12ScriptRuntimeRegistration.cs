using V12.Core;

namespace V12.Bindings
{
    /// <summary>
    /// Populates the engine's <see cref="ScriptRuntimeRegistry"/> so
    /// <see cref="V12.Components.ScriptComponent"/> dispatches scripts by file
    /// extension: <c>.lua</c> → MoonSharp, <c>.ct</c> → Contract. MoonSharp is
    /// registered first and becomes the default runtime, which keeps inline
    /// extensionless Lua sources (the existing bootstrap style) working.
    /// </summary>
    public static class V12ScriptRuntimeRegistration
    {
        /// <summary>
        /// Install (or return the existing) script runtime registry on the game
        /// root. Registering the factory creates a throwaway runtime instance,
        /// so registrations must be cheap — both runtimes are empty until a
        /// script is loaded.
        /// </summary>
        public static ScriptRuntimeRegistry RegisterAll(GameRoot gameRoot)
        {
            var existing = gameRoot.Registry.Get<ScriptRuntimeRegistry>();
            if (existing != null) return existing;

            var registry = new ScriptRuntimeRegistry()
                .Register(() => new MoonSharpScriptRuntime())
                .Register(() => new ContractScriptRuntime());
            gameRoot.Registry.Register("ScriptRuntimeRegistry", registry);
            return registry;
        }
    }
}
