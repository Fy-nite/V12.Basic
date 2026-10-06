using V12.Core;

namespace V12.Bindings
{
    /// <summary>
    /// An <see cref="IV12Gamepack"/> backed by a Contract (<c>.ct</c>) script.
    /// The script is compiled on <see cref="OnStart"/> (phase 2, after the game
    /// loop is ready), its <c>Main</c> runs immediately, and an optional
    /// <c>OnUpdate(float deltaTime)</c> hook is invoked every frame through
    /// <see cref="ScriptTickService"/> registered on <see cref="GameRoot.Registry"/>.
    /// Precompiled ObjektRT modules (<c>.orbt</c>/<c>.oil</c>) use
    /// <see cref="ObjektRTGamepak"/> instead.
    ///
    /// This is how <c>.ct</c> entrypoints ship inside a <c>.v12pak</c>: the pak
    /// loader wraps each declared script into a gamepak and registers it with
    /// the engine's <see cref="GamePak.GamepackLoader"/> so it flows through
    /// the same two-phase lifecycle as DLL gamepaks.
    /// </summary>
    public sealed class ContractGamepack : IV12Gamepack
    {
        private readonly string _scriptPath;
        private readonly string _gamepackName;
        private ContractV12Host? _host;

        /// <summary>Path of the compiled <c>.ct</c> source.</summary>
        public string ScriptPath => _scriptPath;

        /// <summary>The wrapped host, available after <see cref="OnStart"/>.</summary>
        public ContractV12Host? Host => _host;

        public string Name => _gamepackName;

        public ContractGamepack(string scriptPath, string? name = null)
        {
            _scriptPath = scriptPath;
            _gamepackName = name ?? Path.GetFileNameWithoutExtension(scriptPath);
        }

        /// <summary>Phase 1. Nothing to set up; compilation is deferred to OnStart.</summary>
        public void Initialize()
        {
        }

        /// <summary>
        /// Phase 2. Compile the script, run its <c>Main</c>, and register a
        /// per-frame tick service that drives <c>OnUpdate</c> when present.
        /// Compile failures throw <see cref="ContractCompileException"/>.
        /// </summary>
        public void OnStart()
        {
            var host = ContractV12Host.Create(_scriptPath, V12LinkedAssemblies.All);
            _host = host;

            var root = GameRoot.Instance;
            if (root == null) return;

            root.Registry.RegisterOrReplace($"ContractTick:{_gamepackName}", new ScriptTickService(_gamepackName, dt => host.InvokeUpdate(dt)));
            GameRoot.Log.Information("Contract gamepak '{Name}' started ({Path})", Name, _scriptPath);
        }
    }
}
