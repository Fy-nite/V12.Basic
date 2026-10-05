using System;
using System.Collections.Generic;
using System.IO;
using V12.Components;
using V12.Core;
using V12.Core.Core.Interfaces;
using V12.Core.Input;
using V12.Core.Interfaces;

namespace V12.Core.Systems
{
    public class ScriptSystem : IGameService, IInputHandler
    {
        private readonly GameRoot _gameRoot;
        private readonly List<ScriptComponent> _scripts = new();
        private FileSystemWatcher _watcher;
        private FileSystemWatcher _contractWatcher;

        public ScriptSystem(GameRoot gameRoot)
        {
            _gameRoot = gameRoot;
        }

        public void Initialize(GameRoot g) { }
        public void Initialize()
        {
            var input = _gameRoot.Registry.Get<InputService>();
            input?.RegisterHandler(this);

            // Watch for hot-reload of Lua scripts
            var resolver = _gameRoot.Registry.Get<IAssetResolver>();
            if (resolver is V12AssetResolver varRes)
            {
                foreach (string physicalPath in varRes.GetMountPaths())
                {
                    string scriptsDir = Path.Combine(physicalPath, "scripts");
                    if (Directory.Exists(scriptsDir))
                    {
                        _watcher = new FileSystemWatcher(scriptsDir, "*.lua")
                        {
                            EnableRaisingEvents = true,
                            IncludeSubdirectories = false,
                            NotifyFilter = NotifyFilters.LastWrite
                        };
                        _watcher.Changed += (s, e) => ReloadScripts();

                        // .ct element scripts live alongside the Lua ones. .ct
                        // component *types* are watched by ContractComponentRegistry.
                        _contractWatcher = new FileSystemWatcher(scriptsDir, "*.ct")
                        {
                            EnableRaisingEvents = true,
                            IncludeSubdirectories = true,
                            NotifyFilter = NotifyFilters.LastWrite
                        };
                        _contractWatcher.Changed += (s, e) => ReloadScripts();
                    }
                }
            }
        }

        public void Update(float deltaTime) { }

        public void Update(GameRoot gameRoot)
        {
            _scripts.Clear();

            foreach (var world in gameRoot.ActiveWorlds)
            {
                world.Lock.EnterReadLock();
                try
                {
                    CollectScripts(world.Root);
                }
                finally { world.Lock.ExitReadLock(); }
            }
        }

        private void CollectScripts(IReadOnlyList<IWorldElement> elements)
        {
            foreach (var el in elements)
            {
                foreach (var comp in el.Components)
                {
                    if (comp is ScriptComponent sc && sc.IsInitialized)
                        _scripts.Add(sc);
                }
                CollectScripts(el.Children);
            }
        }

        private void ReloadScripts()
        {
            foreach (var sc in _scripts)
                sc.Reload();
        }

        public void OnInputEvent(InputEvent e)
        {
            foreach (var sc in _scripts)
                sc.CallEvent("on_action", e.Name, (double)e.Value);
        }

        public void HandleInteraction(IWorldElement target)
        {
            var sc = target.GetComponent<ScriptComponent>();
            sc?.CallEvent("on_interact");
        }
    }
}
