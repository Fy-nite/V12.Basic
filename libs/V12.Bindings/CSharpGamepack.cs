using V12.Core;

namespace V12.Bindings
{
    /// <summary>
    /// An <see cref="IV12Gamepack"/> backed by a folder of C# sources. On
    /// <see cref="OnStart"/> the folder is compiled with
    /// <see cref="CSharpScriptCompiler"/>, the assembly is loaded, every
    /// <see cref="V12.Core.Core.Interfaces.IComponent"/> it declares becomes
    /// attachable through <see cref="CSharpComponentRegistry"/>, and the
    /// assembly is linked into Contract scripts so <c>.ct</c> code can
    /// reference the same component types.
    /// <para>
    /// This is the C# analogue of <see cref="ContractGamepack"/>: a gamepak can
    /// ship <c>scripts/*.cs</c> and the pak loader wraps the folder in one of
    /// these, so C# components flow through the normal two-phase lifecycle.
    /// </para>
    /// </summary>
    public sealed class CSharpGamepack : IV12Gamepack
    {
        private readonly string _sourceDirectory;
        private readonly string _gamepackName;
        private CSharpScriptHost? _host;

        /// <summary>The script set, available after <see cref="OnStart"/>.</summary>
        public CSharpScriptHost? Host => _host;

        /// <summary>Directory the component sources are compiled from.</summary>
        public string SourceDirectory => _sourceDirectory;

        public string Name => _gamepackName;

        /// <summary>Whether the source folder is watched for reloads. Default: true.</summary>
        public bool WatchForChanges { get; }

        public CSharpGamepack(string sourceDirectory, string? name = null, bool watch = true)
        {
            _sourceDirectory = sourceDirectory;
            _gamepackName = name ?? Path.GetFileName(sourceDirectory.TrimEnd(Path.DirectorySeparatorChar));
            WatchForChanges = watch;
        }

        /// <summary>Phase 1. Nothing to set up; compilation is deferred to OnStart.</summary>
        public void Initialize()
        {
        }

        /// <summary>
        /// Phase 2. Compile and load the sources, register their component
        /// types, and (when enabled) start watching for edits. A failed compile
        /// is logged and leaves an empty registry rather than taking the game
        /// down.
        /// </summary>
        public void OnStart()
        {
            var root = GameRoot.Instance;

            _host ??= new CSharpScriptHost(_sourceDirectory, _gamepackName);
            if (!_host.HasSources())
            {
                GameRoot.Log.Debug("C# gamepak '{Name}': no {Ext} sources in {Dir}", Name, CSharpScriptCompiler.Extension, _sourceDirectory);
                return;
            }

            _host.Reload();

            if (WatchForChanges)
            {
                _host.Watch();
                root?.Registry.RegisterOrReplace($"CSharpTick:{_gamepackName}", new ScriptTickService(_gamepackName, _ => _host.Pump()));
            }

            GameRoot.Log.Information("C# gamepak '{Name}' started ({Directory})", Name, _sourceDirectory);
        }
    }
}
