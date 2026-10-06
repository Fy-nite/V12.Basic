using System.Reflection;
using Contract.Compiler.Diagnostics;
using Contract.Runtime;
using ObjectRT.Abstractions;
using ObjektRT.Core.Model;
using V12.Core.Core.Interfaces;
using V12.Core.Interfaces;

namespace V12.Bindings
{
    /// <summary>
    /// <see cref="IScriptRuntime"/> implementation for Contract (.ct) sources.
    /// ScriptComponent dispatches to this runtime when a
    /// <see cref="V12.Core.ScriptRuntimeRegistry"/> maps ".ct" here.
    ///
    /// The source is compiled with the V12.* bindings registered and loaded into
    /// a <see cref="ContractRuntime"/>. Script hooks are plain top-level methods
    /// found by name: <c>on_init()</c> runs at load, <c>on_update(deltaTime: float)</c>
    /// ticks per frame, and any other method is reachable via
    /// <see cref="Call(string, object[])"/>. A failed compile is logged and the
    /// previous module is kept (mirrors MoonSharp's catch-and-log behaviour).
    ///
    /// The owning element is exposed to scripts as <c>V12.Script.Owner()</c>;
    /// global values set via <see cref="SetGlobal"/> are held engine-side (Contract
    /// has no dynamic global scope) and are retrievable with <see cref="GetGlobal"/>.
    ///
    /// Pass <c>linkedAssemblies</c> to reference real .NET assemblies directly
    /// (assembly-link); scripts may also self-describe with
    /// <c>&lt;AssemblyRef("Name")&gt;</c>.
    /// </summary>
    public sealed class ContractScriptRuntime : IScriptRuntime, IScriptOwnerAwareRuntime
    {
        private readonly ContractRuntime _runtime;
        private readonly IReadOnlyList<Assembly> _linkedAssemblies;
        private readonly Dictionary<string, object> _globals = new();
        private IWorldElement? _owner;
        private bool _loaded;

        public ContractScriptRuntime(IEnumerable<Assembly>? linkedAssemblies = null)
        {
            _linkedAssemblies = (linkedAssemblies ?? Enumerable.Empty<Assembly>())
                .Where(a => a != null).Distinct().ToList();
            _runtime = new ContractRuntime();
            _runtime.RegisterBindingAssembly(typeof(V12Log).Assembly);
            foreach (var asm in _linkedAssemblies)
                _runtime.RegisterLinkedAssembly(asm);
        }

        public bool SupportsHotReload => true;

        public string[] SupportedExtensions => new[] { ".ct" };

#pragma warning disable CS0067 // interface contract; not raised (Contract prints via V12.Log)
        public event Action<string>? OnPrint;
#pragma warning restore CS0067

        /// <inheritdoc />
        public void SetOwner(IWorldElement owner) => _owner = owner;

        /// <summary>
        /// Compile and load the script source. <paramref name="scriptName"/> is
        /// the source path when the script came from a file (imports resolve
        /// relative to it); otherwise the source is compiled as self-contained
        /// inline text.
        /// </summary>
        public void Load(string source, string scriptName)
        {
            ORBTModule? module;
            DiagnosticBag diagnostics;
            if (!string.IsNullOrEmpty(scriptName) && File.Exists(scriptName))
                module = ContractCompiler.CompileFileToModule(scriptName, out diagnostics, new[] { typeof(V12Log).Assembly }, _linkedAssemblies);
            else
                module = ContractCompiler.CompileSourceToModule(source, null, out diagnostics, new[] { typeof(V12Log).Assembly }, _linkedAssemblies);

            if (module == null)
            {
                var errors = string.Join("\n", diagnostics.Diagnostics.Select(d => d.ToString()));
                Console.WriteLine($"[Contract] Compile error in {scriptName}:\n{errors}");
                return;
            }

            _runtime.Inner.LoadModule(module);
            _loaded = true;
        }

        /// <summary>
        /// Invoke any top-level method by name (e.g. <c>on_init</c>, <c>on_update</c>,
        /// or a custom event). Missing methods are skipped like MoonSharp skips
        /// non-function globals.
        /// </summary>
        public void Call(string functionName, params object[] args)
        {
            if (!_loaded) return;
            var qualified = FindMethod(functionName);
            if (qualified == null) return;
            V12Script.SetOwner(_owner?.Id ?? 0);
            _runtime.CallMethod<object?>(qualified, args);
        }

        /// <summary>Contract has no global scope; values are held engine-side.</summary>
        public void SetGlobal(string name, object value) => _globals[name] = value;

        /// <summary>Returns a previously set engine-side global, or null.</summary>
        public object? GetGlobal(string name)
            => _globals.TryGetValue(name, out var value) ? value : null;

        public void Dispose()
        {
            _loaded = false;
            _owner = null;
        }

        private string? FindMethod(string methodName)
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
    }
}
