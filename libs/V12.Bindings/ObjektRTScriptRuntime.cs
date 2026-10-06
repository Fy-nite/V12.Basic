using System.Reflection;
using Contract.Runtime;
using V12.Core.Core.Interfaces;
using V12.Core.Interfaces;

namespace V12.Bindings
{
    /// <summary>
    /// <see cref="IScriptRuntime"/> implementation for precompiled ObjektRT
    /// modules (<c>.orbt</c>/<c>.oil</c>). <see cref="V12.Components.ScriptComponent"/>
    /// dispatches to this runtime when <see cref="V12.Core.ScriptRuntimeRegistry"/>
    /// maps the extension here; Contract (<c>.ct</c>) sources use
    /// <see cref="ContractScriptRuntime"/> instead.
    ///
    /// The module is loaded as-is (no compile step) with the V12.* bindings
    /// registered, so compiled scripts still call <c>V12.World.*</c>,
    /// <c>V12.Log.*</c> and any linked assemblies. Hooks are plain module
    /// methods found by name: <c>on_init()</c> runs at load,
    /// <c>on_update(deltaTime: float)</c> ticks per frame, and any other
    /// method is reachable via <see cref="Call(string, object[])"/>.
    ///
    /// The owning element is exposed to scripts as <c>V12.Script.Owner()</c>;
    /// engine-side globals mirror <see cref="ContractScriptRuntime"/>.
    /// </summary>
    public sealed class ObjektRTScriptRuntime : IScriptRuntime, IScriptOwnerAwareRuntime
    {
        private readonly ContractRuntime _runtime;
        private readonly Dictionary<string, object> _globals = new();
        private IWorldElement? _owner;
        private bool _loaded;

        public ObjektRTScriptRuntime(IEnumerable<Assembly>? linkedAssemblies = null)
        {
            _runtime = new ContractRuntime();
            _runtime.RegisterBindingAssembly(typeof(V12Log).Assembly);
            foreach (var asm in (linkedAssemblies ?? Enumerable.Empty<Assembly>()).Where(a => a != null).Distinct())
                _runtime.RegisterLinkedAssembly(asm);
        }

        public bool SupportsHotReload => true;

        public string[] SupportedExtensions => new[] { ".orbt", ".oil" };

#pragma warning disable CS0067 // interface contract; not raised (prints via V12.Log)
        public event Action<string>? OnPrint;
#pragma warning restore CS0067

        /// <inheritdoc />
        public void SetOwner(IWorldElement owner) => _owner = owner;

        /// <summary>
        /// Loads the compiled module from <paramref name="scriptName"/> (the
        /// resolved on-disk path). <paramref name="source"/> text is ignored —
        /// compiled modules are binary/text IR read by
        /// <see cref="ObjektRTModuleLoader"/>. A missing file or unloadable
        /// module is logged and leaves the runtime unloaded.
        /// </summary>
        public void Load(string source, string scriptName)
        {
            if (string.IsNullOrEmpty(scriptName) || !File.Exists(scriptName))
            {
                Console.WriteLine($"[ObjektRT] Script module not found: {scriptName}");
                return;
            }

            try
            {
                var module = ObjektRTModuleLoader.Load(_runtime, scriptName);
                _runtime.Inner.LoadModule(module);
                _loaded = true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ObjektRT] Failed to load module {scriptName}: {ex.Message}");
                _loaded = false;
            }
        }

        /// <summary>
        /// Invoke any module method by name (e.g. <c>on_init</c>,
        /// <c>on_update</c>, or a custom event). Missing methods are skipped.
        /// </summary>
        public void Call(string functionName, params object[] args)
        {
            if (!_loaded) return;
            var qualified = FindMethod(functionName);
            if (qualified == null) return;
            V12Script.SetOwner(_owner?.Id ?? 0);
            _runtime.CallMethod<object?>(qualified, args);
        }

        /// <summary>Contract/ObjektRT have no global scope; values are held engine-side.</summary>
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
