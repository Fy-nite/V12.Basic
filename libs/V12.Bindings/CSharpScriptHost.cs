using System.Reflection;
using V12.Core;

namespace V12.Bindings
{
    /// <summary>
    /// Owns one compiled-from-source C# script set: it compiles the
    /// <c>.cs</c> files under a directory, loads the resulting assembly into a
    /// collectible <see cref="CSharpScriptLoadContext"/>, registers every
    /// component type with <see cref="CSharpComponentRegistry"/>, and links the
    /// assembly into Contract scripts (<see cref="V12LinkedAssemblies"/>).
    /// <para>
    /// <see cref="Watch"/> makes the set hot-reloadable. Changes are only
    /// enqueued on the watcher thread; <see cref="Pump"/> performs the actual
    /// compile/swap on the game thread (call it from an
    /// <c>IGameService.Update</c> or your own loop), so the same debounce and
    /// single-thread discipline as <see cref="ContractHotReloader"/> applies.
    /// A failed compile keeps the previously loaded assembly running.
    /// </para>
    /// </summary>
    public sealed class CSharpScriptHost : IDisposable
    {
        private readonly object _gate = new();
        private readonly string _assemblyName;
        private CSharpScriptLoadContext? _context;
        private FileSystemWatcher? _watcher;
        private DateTime _lastChangeUtc = DateTime.MinValue;
        private bool _reloadRequested;
        private int _generation;
        private bool _disposed;

        /// <summary>Absolute directory the sources are compiled from.</summary>
        public string SourceDirectory { get; }

        /// <summary>Assembly name used for the emitted DLL.</summary>
        public string AssemblyName => _assemblyName;

        /// <summary>Stable registry source key for this script set (used to unregister on reload).</summary>
        public string SourceKey => "cs://" + _assemblyName;

        /// <summary>Absolute path the DLL is emitted to.</summary>
        public string OutputPath { get; }

        /// <summary>The currently loaded assembly, or null before the first successful compile.</summary>
        public Assembly? Assembly { get; private set; }

        /// <summary>Result of the most recent compile attempt.</summary>
        public CSharpCompileResult? LastResult { get; private set; }

        /// <summary>True when a file change is waiting for the next <see cref="Pump"/>.</summary>
        public bool HasPendingReload
        {
            get { lock (_gate) return _reloadRequested; }
        }

        public CSharpScriptHost(string sourceDirectory, string? assemblyName = null, string? outputPath = null)
        {
            if (string.IsNullOrWhiteSpace(sourceDirectory))
                throw new ArgumentException("sourceDirectory is required.", nameof(sourceDirectory));

            SourceDirectory = Path.GetFullPath(sourceDirectory);
            _assemblyName = SanitizeAssemblyName(
                string.IsNullOrWhiteSpace(assemblyName) ? "V12CSharp_" + Path.GetFileName(SourceDirectory.TrimEnd(Path.DirectorySeparatorChar)) : assemblyName);
            OutputPath = string.IsNullOrWhiteSpace(outputPath)
                ? CSharpScriptCompiler.DefaultOutputPath(_assemblyName)
                : Path.GetFullPath(outputPath);
        }

        /// <summary>True when the directory exists and contains at least one <c>.cs</c> file.</summary>
        public bool HasSources()
            => Directory.Exists(SourceDirectory) &&
               Directory.EnumerateFiles(SourceDirectory, "*" + CSharpScriptCompiler.Extension, SearchOption.AllDirectories).Any();

        /// <summary>
        /// Compiles the source directory and swaps in the new assembly. On a
        /// compile error the previously loaded assembly is left in place and the
        /// error is reported through <see cref="CSharpCompileResult"/>.
        /// </summary>
        public CSharpCompileResult Reload()
        {
            var result = CSharpScriptCompiler.CompileDirectory(SourceDirectory, _assemblyName, OutputPath);
            LastResult = result;

            if (!result.Success || string.IsNullOrEmpty(result.AssemblyPath))
            {
                GameRoot.Log.Warning("[CSharpScripts] Compile failed for {Directory}:\n{Errors}", SourceDirectory, result.ErrorText);
                return result;
            }

            var context = new CSharpScriptLoadContext($"{_assemblyName}#{++_generation}");
            Assembly assembly;
            try
            {
                assembly = context.LoadFrom(result.AssemblyPath);
            }
            catch (Exception ex)
            {
                GameRoot.Log.Error(ex, "[CSharpScripts] Failed to load compiled assembly {Path}", result.AssemblyPath);
                try { context.Unload(); } catch { /* best effort */ }
                return new CSharpCompileResult(false, null, new[] { ex.Message }, Array.Empty<string>());
            }

            var registry = CSharpComponentRegistry.GetOrCreate();
            registry.RemoveSource(SourceKey);
            int registered = registry.RegisterAssembly(assembly, SourceKey);

            V12LinkedAssemblies.Add(assembly);
            CSharpScriptImages.Add(assembly, result.Image);

            CSharpScriptLoadContext? previous;
            Assembly? previousAssembly;
            lock (_gate)
            {
                previous = _context;
                previousAssembly = Assembly;
                _context = context;
            }
            Assembly = assembly;
            CSharpScriptImages.Remove(previousAssembly);

            // Live instances keep the old assembly; new instances use this one.
            try { previous?.Unload(); } catch { /* best effort */ }

            GameRoot.Log.Information(
                "[CSharpScripts] Loaded {Assembly}: {Count} component type(s), {Warnings} warning(s) from {Directory}",
                assembly.GetName().Name, registered, result.Warnings.Count, SourceDirectory);

            return result;
        }

        /// <summary>Starts watching <see cref="SourceDirectory"/> for <c>.cs</c> edits.</summary>
        public void Watch()
        {
            if (_watcher != null || _disposed || !Directory.Exists(SourceDirectory)) return;

            var watcher = new FileSystemWatcher(SourceDirectory, "*" + CSharpScriptCompiler.Extension)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.CreationTime,
                EnableRaisingEvents = true
            };
            watcher.Changed += OnFileChanged;
            watcher.Created += OnFileChanged;
            watcher.Deleted += OnFileChanged;
            watcher.Renamed += OnFileChanged;
            _watcher = watcher;
        }

        /// <summary>
        /// Recompiles when a watched file changed at least 150 ms ago. Call from
        /// the game thread (the watcher only enqueues).
        /// </summary>
        public void Pump()
        {
            bool due;
            lock (_gate)
            {
                due = _reloadRequested && (DateTime.UtcNow - _lastChangeUtc).TotalMilliseconds >= 150;
                if (due) _reloadRequested = false;
            }
            if (due) Reload();
        }

        private void OnFileChanged(object sender, FileSystemEventArgs e)
        {
            lock (_gate)
            {
                _reloadRequested = true;
                _lastChangeUtc = DateTime.UtcNow;
            }
        }

        private static string SanitizeAssemblyName(string name)
        {
            var builder = new System.Text.StringBuilder(name.Length);
            foreach (char c in name)
                builder.Append(char.IsLetterOrDigit(c) || c == '.' || c == '_' ? c : '_');

            string result = builder.ToString().Trim('.', '_');
            return result.Length == 0 ? "V12CSharp" : result;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _watcher?.Dispose();
            _watcher = null;

            CSharpComponentRegistry? registry = CSharpComponentRegistry.FromGameRoot();
            registry?.RemoveSource(SourceKey);

            CSharpScriptLoadContext? context;
            lock (_gate)
            {
                context = _context;
                _context = null;
            }
            try { context?.Unload(); } catch { /* best effort */ }
            CSharpScriptImages.Remove(Assembly);
            Assembly = null;
        }
    }
}
