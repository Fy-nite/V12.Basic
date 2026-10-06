using System.Reflection;
using Contract.Runtime;
using V12.Core;

namespace V12.Bindings
{
    /// <summary>
    /// Bridges a Contract (.ct) script into a running V12 game. Compiles the
    /// source with the V12.* bindings registered, loads it into a
    /// <see cref="ContractRuntime"/>, runs the module's <c>Main</c>, and exposes
    /// per-frame hook invocation (<c>OnUpdate(float deltaTime)</c>) plus reload.
    ///
    /// Reload recompiles the source and re-runs <c>Main</c>. Any root elements
    /// spawned by the previous run are despawned first so the script can rebuild
    /// its world without duplicates. The runtime owns exactly one loaded module,
    /// so a host is created per script.
    ///
    /// Pass <c>linkedAssemblies</c> to reference real .NET assemblies directly
    /// (assembly-link): every public type in them becomes callable from the
    /// script by its CLR name, exactly as in C# — no <c>[ClassBinding]</c>
    /// wrapper needed. Scripts can also self-describe with
    /// <c>&lt;AssemblyRef("Name")&gt;</c>.
    /// </summary>
    public sealed class ContractV12Host
    {
        private readonly ContractRuntime _runtime;
        private readonly string _scriptPath;
        private readonly IReadOnlyList<Assembly> _linkedAssemblies;
        private readonly HashSet<long> _baselineIds = new();
        private bool _hasLoaded;

        /// <summary>The underlying Contract runtime (for advanced use).</summary>
        public ContractRuntime Runtime => _runtime;

        /// <summary>Path of the compiled .ct source.</summary>
        public string ScriptPath => _scriptPath;

        /// <summary>Create a host for one script and load it immediately.</summary>
        public static ContractV12Host Create(string scriptPath, IEnumerable<Assembly>? linkedAssemblies = null)
        {
            var host = new ContractV12Host(scriptPath, linkedAssemblies);
            host.Reload();
            return host;
        }

        public ContractV12Host(string scriptPath, IEnumerable<Assembly>? linkedAssemblies = null)
        {
            _scriptPath = scriptPath;
            _linkedAssemblies = (linkedAssemblies ?? Enumerable.Empty<Assembly>())
                .Where(a => a != null).Distinct().ToList();
            _runtime = new ContractRuntime();
            _runtime.RegisterBindingAssembly(typeof(V12Log).Assembly);
            foreach (var asm in _linkedAssemblies)
                _runtime.RegisterLinkedAssembly(asm);
        }

        /// <summary>
        /// Recompile and reload the script. World elements created by the
        /// previous run are despawned, then <c>Main</c> runs again.
        /// The previous module and its spawned elements are left untouched when
        /// compilation fails, so a broken save never wipes the running scene.
        /// </summary>
        public void Reload()
        {
            var module = ContractCompiler.CompileFileToModule(
                _scriptPath,
                out var diagnostics,
                new[] { typeof(V12Log).Assembly },
                _linkedAssemblies);

            if (module == null)
            {
                var errors = string.Join("\n", diagnostics.Diagnostics.Select(d => d.ToString()));
                throw new ContractCompileException(_scriptPath, errors);
            }

            if (_hasLoaded)
                ClearSpawnedElements();

            _runtime.Inner.LoadModule(module);

            if (!_hasLoaded)
                CaptureBaseline();

            var entry = ContractRuntime.FindEntry(module);
            if (entry != null)
                _runtime.CallMethod<object?>(entry);

            _hasLoaded = true;
        }

        /// <summary>
        /// Invoke the script's <c>OnUpdate(float deltaTime)</c> hook when it
        /// declares one. Returns true when the hook ran. Errors thrown inside
        /// the script propagate to the caller.
        /// </summary>
        public bool InvokeUpdate(float deltaTime)
        {
            var hook = FindHook("OnUpdate");
            if (hook == null) return false;
            _runtime.CallMethod<object?>(hook, deltaTime);
            return true;
        }

        /// <summary>Invoke any module method by qualified name (e.g. "Program.Hello").</summary>
        public object? Invoke(string qualifiedName, params object?[] args)
            => _runtime.CallMethod<object?>(qualifiedName, args);

        private string? FindHook(string methodName)
        {
            var reflector = _runtime.Reflector;
            if (reflector == null) return null;
            foreach (var type in reflector.GetTypes())
            {
                var method = type.GetMethod(methodName);
                if (method != null) return method.QualifiedName;
            }
            return null;
        }

        /// <summary>Snapshot which root elements already existed before the script ran.</summary>
        private void CaptureBaseline()
        {
            var root = GameRoot.Instance;
            if (root == null) return;
            foreach (var world in ActiveWorldsOf(root))
            {
                foreach (var el in world.Root)
                    _baselineIds.Add(el.Id);
            }
        }

        /// <summary>Despawn any root elements the script spawned since baseline.</summary>
        private void ClearSpawnedElements()
        {
            var root = GameRoot.Instance;
            if (root == null) return;
            foreach (var world in ActiveWorldsOf(root))
            {
                foreach (var el in world.Root.ToArray())
                {
                    if (!_baselineIds.Contains(el.Id))
                        world.RemoveElement(el);
                }
            }
        }

        private static IEnumerable<World> ActiveWorldsOf(GameRoot root)
        {
            yield return root.PersistentWorld;
            if (root.SelectedWorld != null)
                yield return root.SelectedWorld;
        }
    }

    /// <summary>Raised when a .ct script fails to compile.</summary>
    public sealed class ContractCompileException : Exception
    {
        public ContractCompileException(string scriptPath, string errors)
            : base($"Contract compile failed for '{scriptPath}':\n{errors}")
        {
        }
    }
}
