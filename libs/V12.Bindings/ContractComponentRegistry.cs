using Contract.Compiler.Diagnostics;
using Contract.Runtime;
using ObjectRT.Abstractions;
using ObjectRT.Runtime.Reflection;
using ObjektRT.Core.Model;
using V12.Core;
using V12.Core.Core.Interfaces;
using RtMethodInfo = ObjectRT.Runtime.Reflection.MethodInfo;

namespace V12.Bindings
{
    /// <summary>
    /// Compiles <c>.ct</c> files that declare component types and hands out
    /// <see cref="ContractComponent"/> instances bound to them. Each source file
    /// gets its own <see cref="ContractRuntime"/> (so its types live in one loaded
    /// module); every class type it declares is registered by name.
    ///
    /// Convention: a component type is a Contract with optional instance fields
    /// and any of the lifecycle methods <c>on_attach(owner)</c>,
    /// <c>on_update(dt)</c>, <c>on_detach(owner)</c>. Instances are created with
    /// <see cref="Create"/> and attached to an element; V12 then drives the hooks
    /// every frame, exposing the owning element as <c>V12.Script.Owner()</c>.
    /// </summary>
    public sealed class ContractComponentRegistry
    {
        /// <summary>Registry service key (retrieve with <c>Registry.Get&lt;ContractComponentRegistry&gt;(ServiceName)</c>).</summary>
        public const string ServiceName = "ContractComponents";

        private static readonly string[] HookNames = { "on_attach", "on_update", "on_detach" };

        /// <summary>A registered <c>.ct</c> component type plus how to drive it.</summary>
        public sealed class TypeEntry
        {
            internal TypeEntry(string typeName, ContractRuntime runtime, string sourcePath)
            {
                TypeName = typeName;
                Runtime = runtime;
                SourcePath = sourcePath;
            }

            /// <summary>The type's name as declared in the module (used for <c>AllocateObject</c> and calls).</summary>
            public string TypeName { get; }

            /// <summary>The runtime hosting this type's compiled module.</summary>
            public ContractRuntime Runtime { get; }

            /// <summary>The <c>.ct</c> file this type was compiled from.</summary>
            public string SourcePath { get; }

            internal RtMethodInfo? Ctor;
            internal RtMethodInfo? OnAttach;
            internal RtMethodInfo? OnUpdate;
            internal RtMethodInfo? OnDetach;

            /// <summary>True when the type declares at least one lifecycle hook.</summary>
            public bool HasLifecycle => OnAttach != null || OnUpdate != null || OnDetach != null;

            /// <summary>Allocates a VM instance of this type and runs its constructor.</summary>
            public object? Allocate()
            {
                var handle = Runtime.Inner.AllocateObject(TypeName);
                if (handle == null)
                {
                    Console.WriteLine($"[ContractComponents] '{TypeName}' could not be allocated (no matching VM type in {SourcePath}).");
                    return null;
                }

                if (Ctor != null)
                    Runtime.CallMethod<object?>(Ctor.QualifiedName, handle);
                return handle;
            }

            /// <summary>Invokes an instance method with <paramref name="handle"/> as the receiver.</summary>
            internal object? Invoke(RtMethodInfo? method, object? handle, params object?[] extra)
            {
                if (method == null || handle == null) return null;

                var args = BuildArgs(method, handle, extra);
                return Runtime.CallMethod<object?>(method.QualifiedName, args);
            }

            private static object?[] BuildArgs(RtMethodInfo method, object? handle, object?[] extra)
            {
                if (method.IsStatic)
                {
                    var statics = new object?[method.ParameterCount];
                    for (int i = 0; i < statics.Length; i++)
                        statics[i] = i < extra.Length ? extra[i] : null;
                    return statics;
                }

                // Instance methods carry 'this' as parameter 0, so the values we
                // actually pass are the remaining declared parameters.
                int declared = Math.Max(0, method.ParameterCount - 1);
                var args = new object?[1 + declared];
                args[0] = handle;
                for (int i = 0; i < declared; i++)
                    args[i + 1] = i < extra.Length ? extra[i] : null;
                return args;
            }
        }

        private readonly Dictionary<string, TypeEntry> _types = new(StringComparer.OrdinalIgnoreCase);
        private FileSystemWatcher? _watcher;

        /// <summary>Registry shared through the game root (also reachable via <see cref="Current"/>).</summary>
        public static ContractComponentRegistry? Current { get; private set; }

        /// <summary>Names of every registered component type.</summary>
        public IReadOnlyCollection<string> TypeNames => _types.Keys;

        /// <summary>Whether a component type with this name is registered.</summary>
        public bool Contains(string typeName) => _types.ContainsKey(typeName);

        /// <summary>Looks up a registered type by name.</summary>
        public bool TryGet(string typeName, out TypeEntry entry) => _types.TryGetValue(typeName, out entry!);

        /// <summary>
        /// Compiles a <c>.ct</c> file and registers every class type it declares,
        /// replacing any previously registered type with the same name (so a
        /// reload picks up edits). Returns the entries created.
        /// </summary>
        public IReadOnlyList<TypeEntry> LoadFile(string path)
        {
            var results = new List<TypeEntry>();
            if (!File.Exists(path))
            {
                Console.WriteLine($"[ContractComponents] File not found: {path}");
                return results;
            }

            ORBTModule? module;
            DiagnosticBag diagnostics;
            try
            {
                module = ContractCompiler.CompileFileToModule(path, out diagnostics, new[] { typeof(V12Log).Assembly });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ContractComponents] Failed to compile {Path.GetFileName(path)}: {ex.Message}");
                return results;
            }

            if (module == null)
            {
                Console.WriteLine($"[ContractComponents] Compile error in {Path.GetFileName(path)}:\n{string.Join("\n", diagnostics.Diagnostics)}");
                return results;
            }

            var runtime = new ContractRuntime();
            runtime.RegisterBindingAssembly(typeof(V12Log).Assembly);
            runtime.Inner.LoadModule(module);

            var reflector = runtime.Reflector;
            if (reflector == null) return results;

            foreach (var type in reflector.GetTypes())
            {
                if (!type.IsClass) continue;

                var entry = new TypeEntry(type.Name, runtime, path)
                {
                    Ctor = type.GetMethod(".ctor"),
                    OnAttach = type.GetMethod("on_attach"),
                    OnUpdate = type.GetMethod("on_update"),
                    OnDetach = type.GetMethod("on_detach"),
                };

                _types[type.Name] = entry;
                results.Add(entry);
                Console.WriteLine($"[ContractComponents] Registered '{type.Name}' from {Path.GetFileName(path)}" +
                    (entry.HasLifecycle ? "" : " (no lifecycle hooks)"));
            }

            if (results.Count == 0)
                Console.WriteLine($"[ContractComponents] No component types found in {Path.GetFileName(path)}");

            return results;
        }

        /// <summary>Compiles every <c>*.ct</c> under <paramref name="directory"/>. Returns the number of types registered.</summary>
        public int LoadDirectory(string directory, bool recursive = true)
        {
            if (!Directory.Exists(directory)) return 0;

            int count = 0;
            foreach (var file in Directory.EnumerateFiles(directory, "*.ct",
                         recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly))
                count += LoadFile(file).Count;
            return count;
        }

        /// <summary>Recompiles every known source file (hot reload). Returns the number of types registered.</summary>
        public int ReloadAll()
        {
            var files = _types.Values.Select(e => e.SourcePath).Distinct().ToList();
            int count = 0;
            foreach (var file in files)
                count += LoadFile(file).Count;
            return count;
        }

        /// <summary>
        /// Watches a directory for <c>.ct</c> edits and recompiles the registered
        /// types. Reloading affects newly created instances; live instances keep
        /// the module they were created with.
        /// </summary>
        public void Watch(string directory)
        {
            if (_watcher != null || !Directory.Exists(directory)) return;

            _watcher = new FileSystemWatcher(directory, "*.ct")
            {
                EnableRaisingEvents = true,
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.LastWrite
            };

            _watcher.Changed += (_, e) =>
            {
                Console.WriteLine($"[ContractComponents] Reloading after change: {e.Name}");
                ReloadAll();
            };
        }

        /// <summary>Creates a new instance of a registered component type, or null when unknown.</summary>
        public ContractComponent? Create(string typeName)
        {
            if (!_types.TryGetValue(typeName, out var entry))
            {
                Console.WriteLine($"[ContractComponents] No component type named '{typeName}'.");
                return null;
            }

            var handle = entry.Allocate();
            return handle == null ? null : new ContractComponent(entry, handle);
        }

        /// <summary>Creates a component of <paramref name="typeName"/> and attaches it to the element.</summary>
        public bool Attach(IWorldElement element, string typeName)
        {
            var component = Create(typeName);
            if (component == null) return false;
            element.AddComponent(component);
            return true;
        }

        /// <summary>Installs this registry as a game-root service.</summary>
        public void Register(GameRoot gameRoot)
        {
            Current = this;
            if (gameRoot.Registry.Get<ContractComponentRegistry>(ServiceName) == null)
                gameRoot.Registry.Register(ServiceName, this);
        }

        /// <summary>Convenience: the registry installed on the current game root (or <see cref="Current"/>).</summary>
        public static ContractComponentRegistry? FromGameRoot()
            => GameRoot.Instance?.Registry.Get<ContractComponentRegistry>(ServiceName) ?? Current;

        /// <summary>
        /// Builds a registry from <c>scripts/components</c> under the executable
        /// directory and the current directory, installs it as a service, and
        /// returns it. Missing directories are simply skipped.
        /// </summary>
        public static ContractComponentRegistry CreateAndRegister(GameRoot gameRoot)
        {
            var registry = new ContractComponentRegistry();
            registry.Register(gameRoot);

            // Prefer the working directory (the folder being edited during development);
            // fall back to the executable's own copy for a deployed game. Loading only
            // the first directory avoids registering every type twice.
            foreach (var root in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
            {
                var dir = Path.Combine(root, "scripts", "components");
                int count = registry.LoadDirectory(dir);
                if (count == 0) continue;

                Console.WriteLine($"[ContractComponents] Loaded {count} component type(s) from {dir}");
                registry.Watch(dir);
                break;
            }
            return registry;
        }
    }
}
