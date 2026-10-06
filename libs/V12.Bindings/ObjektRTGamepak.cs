using Contract.Runtime;
using V12.Core;
using V12.Core.Core.Interfaces;

namespace V12.Bindings
{
    /// <summary>
    /// An <see cref="IV12Gamepack"/> backed by a precompiled ObjektRT module
    /// (<c>.orbt</c>/<c>.oil</c>). The module is loaded on
    /// <see cref="OnStart"/> with no compile step, its static <c>Main</c> runs
    /// immediately, and an optional <c>OnUpdate(float deltaTime)</c> hook ticks
    /// per frame through a <see cref="IGameService"/> on
    /// <see cref="GameRoot.Registry"/> — the same lifecycle as
    /// <see cref="ContractGamepack"/>, which serves Contract (<c>.ct</c>)
    /// entrypoints instead.
    /// </summary>
    public sealed class ObjektRTGamepak : IV12Gamepack
    {
        private readonly string _modulePath;
        private readonly string _gamepackName;
        private ContractRuntime? _runtime;

        /// <summary>Path of the compiled module.</summary>
        public string ModulePath => _modulePath;

        /// <summary>The host runtime, available after <see cref="OnStart"/>.</summary>
        public ContractRuntime? Runtime => _runtime;

        public string Name => _gamepackName;

        public ObjektRTGamepak(string modulePath, string? name = null)
        {
            _modulePath = modulePath;
            _gamepackName = name ?? Path.GetFileNameWithoutExtension(modulePath);
        }

        /// <summary>Phase 1. Nothing to set up; module loading is deferred to OnStart.</summary>
        public void Initialize()
        {
        }

        /// <summary>
        /// Phase 2. Load the compiled module, run its <c>Main</c>, and register
        /// a per-frame tick service that drives <c>OnUpdate</c> when present.
        /// Load failures throw.
        /// </summary>
        public void OnStart()
        {
            var runtime = new ContractRuntime();
            runtime.RegisterBindingAssembly(typeof(V12Log).Assembly);
            foreach (var asm in V12LinkedAssemblies.All)
                runtime.RegisterLinkedAssembly(asm);

            var module = ObjektRTModuleLoader.Load(runtime, _modulePath);
            runtime.Inner.LoadModule(module);
            _runtime = runtime;

            var root = GameRoot.Instance;
            if (root == null) return;

            var entry = ContractRuntime.FindEntry(module);
            if (entry != null)
                runtime.CallMethod<object?>(entry);

            root.Registry.RegisterOrReplace($"ObjektRTTick:{_gamepackName}", new ScriptTickService(_gamepackName, InvokeUpdate));
            GameRoot.Log.Information("ObjektRT gamepak '{Name}' started ({Path})", Name, _modulePath);
        }

        private void InvokeUpdate(float deltaTime)
        {
            var hook = FindHook("OnUpdate");
            if (hook == null || _runtime == null) return;
            _runtime.CallMethod<object?>(hook, deltaTime);
        }

        private string? FindHook(string methodName)
        {
            var reflector = _runtime?.Reflector;
            if (reflector == null) return null;
            foreach (var type in reflector.GetTypes())
            {
                var method = type.GetMethod(methodName);
                if (method != null) return method.QualifiedName;
            }
            return null;
        }
    }
}
