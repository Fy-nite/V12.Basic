using System.Collections.Concurrent;
using V12.Core;
using V12.Core.Core.Interfaces;

namespace V12.Bindings
{
    /// <summary>
    /// Hot reload for a Contract (<c>.ct</c>) script. Watches the script's source
    /// file and recompiles it on save, so an editor + running game loop become a
    /// live-coding environment for V12 hosts.
    ///
    /// Threading: the <see cref="FileSystemWatcher"/> fires on a threadpool thread
    /// and only enqueues a debounced reload request. The actual
    /// <see cref="ContractV12Host.Reload"/> happens on the game thread, driven by
    /// <see cref="Update(float)"/> (register this as an <see cref="IGameService"/>
    /// to tick automatically) or by calling <see cref="Pump"/> from your own loop.
    /// This keeps the Contract runtime and V12 mutations on one thread.
    ///
    /// A script that fails to compile keeps the previously loaded module running:
    /// <see cref="ContractV12Host.Reload"/> compiles before it swaps the module,
    /// so a broken save is logged and ignored rather than taking the game down.
    /// </summary>
    public sealed class ContractHotReloader : IGameService, IDisposable
    {
        private readonly ContractV12Host _host;
        private readonly string _watchPath;
        private readonly FileSystemWatcher? _watcher;
        private readonly ConcurrentQueue<string> _queue = new();
        private readonly System.Threading.Lock _debounceLock = new();
        private DateTime _lastEnqueue = DateTime.MinValue;
        private bool _disposed;

        /// <summary>Number of pending (not yet pumped) reload requests.</summary>
        public int PendingReloads => _queue.Count;

        /// <summary>Create a hot reloader and start watching <paramref name="scriptPath"/>.</summary>
        public static ContractHotReloader Watch(string scriptPath, ContractV12Host host)
            => new(scriptPath, host);

        public ContractHotReloader(string scriptPath, ContractV12Host host)
        {
            _host = host;
            _watchPath = Path.GetFullPath(scriptPath);

            string? dir = Path.GetDirectoryName(_watchPath);
            string file = Path.GetFileName(_watchPath);
            if (string.IsNullOrEmpty(dir) || !File.Exists(_watchPath))
                return;

            _watcher = new FileSystemWatcher(dir, file)
            {
                NotifyFilter = NotifyFilters.LastWrite,
                EnableRaisingEvents = true
            };
            _watcher.Changed += (_, _) => EnqueueReload();
        }

        private void EnqueueReload()
        {
            lock (_debounceLock)
            {
                if ((DateTime.UtcNow - _lastEnqueue).TotalMilliseconds < 200)
                    return;
                _lastEnqueue = DateTime.UtcNow;
            }
            _queue.Enqueue(_watchPath);
        }

        /// <summary>
        /// Process any pending reload requests. Must be called on the thread that
        /// owns the Contract runtime (the game thread). Registered as an
        /// <see cref="IGameService"/>, <see cref="Update(float)"/> calls this every frame.
        /// </summary>
        public void Pump()
        {
            while (_queue.TryDequeue(out _))
            {
                try
                {
                    GameRoot.Log.Information("Contract hot reload triggered: {Path}", _watchPath);
                    _host.Reload();
                    GameRoot.Log.Information("Contract hot reload succeeded: {Path}", _watchPath);
                }
                catch (ContractCompileException ex)
                {
                    GameRoot.Log.Warning("Contract hot reload failed, keeping previous module:\n{Errors}", ex.Message);
                }
                catch (Exception ex)
                {
                    GameRoot.Log.Error(ex, "Contract hot reload failed, keeping previous module: {Path}", _watchPath);
                }
            }
        }

        public void Initialize(GameRoot g)
        {
        }

        public void Update(GameRoot gameRoot)
        {
        }

        public void Update(float deltaTime) => Pump();

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _watcher?.Dispose();
        }
    }
}
