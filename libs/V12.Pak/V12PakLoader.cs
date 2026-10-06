using System.Xml;
using V12.Bindings;
using V12.Core;
using V12.Core.Interfaces;
using V12.WorldML;

namespace V12.Pak
{
    /// <summary>Outcome of loading a <c>.v12pak</c> into a <see cref="GameRoot"/>.</summary>
    public sealed class V12PakLoadResult : IDisposable
    {
        public string PakPath { get; }
        public V12PakManifest Manifest { get; }
        public V12PakReader Reader { get; }
        public V12PakAssetResolver Resolver { get; }
        public List<World> LoadedWorlds { get; } = new();
        public int LoadedGamepaks { get; set; }
        public int SkippedAssets { get; set; }
        public int SkippedGamepaks { get; set; }

        public V12PakLoadResult(string pakPath, V12PakManifest manifest, V12PakReader reader, V12PakAssetResolver resolver)
        {
            PakPath = pakPath;
            Manifest = manifest;
            Reader = reader;
            Resolver = resolver;
        }

        /// <summary>Release the reader and its extracted temp directory. Loaded worlds remain.</summary>
        public void Dispose()
        {
            Reader.Dispose();
        }
    }

    /// <summary>
    /// High-level orchestrator for loading <c>.v12pak</c> archives into a running
    /// game: reads the manifest, applies <see cref="V12PakOptions"/> security
    /// filtering, loads declared worlds (world.xml + templates), registers the
    /// pak asset resolver, and optionally loads bundled C# DLL gamepaks and
    /// compiled Contract (<c>.ct</c>) gamepaks.
    ///
    /// The caller keeps the returned <see cref="V12PakLoadResult"/> alive so the
    /// resolver's backing temp directory persists; dispose it to unload.
    /// </summary>
    public static class V12PakLoader
    {
        private static Serilog.ILogger Log => GameRoot.Log;

        public static V12PakLoadResult Load(string pakPath, V12PakOptions? options = null, GameRoot? root = null)
        {
            options ??= new V12PakOptions();
            root ??= GameRoot.Instance
                ?? throw new InvalidOperationException("No active GameRoot. Pass a GameRoot instance to Load().");

            var reader = new V12PakReader(pakPath);
            var manifest = reader.Manifest;
            Log.Information("V12Pak '{PakName}': {Worlds} world(s), {Assets} asset(s), {Paks} pak DLL(s)",
                Path.GetFileName(pakPath), manifest.Worlds.Count, manifest.Assets.Count, manifest.Paks.Count);

            var allowedAssets = FilterAssets(manifest, options, out int skippedAssets);
            var resolver = new V12PakAssetResolver(reader, allowedAssets);
            var result = new V12PakLoadResult(pakPath, manifest, reader, resolver) { SkippedAssets = skippedAssets };

            if (options.AllowWorldLoading)
                LoadWorlds(reader, manifest, resolver, result, root);

            if (options.LoadDlls)
                LoadDlls(reader, manifest, result, root);
            else
            {
                foreach (var dll in manifest.Paks)
                {
                    result.SkippedGamepaks++;
                    Log.Warning("V12Pak '{PakName}': skipping gamepak DLL '{Dll}' (LoadDlls is false)",
                        Path.GetFileName(pakPath), dll);
                }
            }

            if (options.LoadContractScripts)
                LoadScripts(reader, manifest, result, root);
            else
            {
                foreach (var script in manifest.Scripts)
                {
                    result.SkippedGamepaks++;
                    Log.Warning("V12Pak '{PakName}': skipping Contract script '{Script}' (LoadContractScripts is false)",
                        Path.GetFileName(pakPath), script);
                }
            }

            root.Registry.RegisterOrReplace("AssetResolver", resolver);
            return result;
        }

        private static Dictionary<string, string> FilterAssets(V12PakManifest manifest, V12PakOptions options, out int skipped)
        {
            skipped = 0;
            var allowed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (key, path) in manifest.Assets)
            {
                string ext = Path.GetExtension(path);
                if (options.AllowedAssetExtensions != null && !options.AllowedAssetExtensions.Contains(ext))
                {
                    skipped++;
                    Log.Debug("V12Pak: asset '{Key}' blocked (extension '{Ext}' not allowed)", key, ext);
                    continue;
                }
                if (options.BlockedAssetKeys?.Contains(key) == true)
                {
                    skipped++;
                    Log.Debug("V12Pak: asset '{Key}' blocked (on blocklist)", key);
                    continue;
                }
                allowed[key] = path;
            }
            return allowed;
        }

        private static void LoadWorlds(V12PakReader reader, V12PakManifest manifest, V12PakAssetResolver resolver, V12PakLoadResult result, GameRoot root)
        {
            var templates = new WorldTemplateProvider();
            LoadTemplatesFromPak(reader, templates);

            foreach (var entry in manifest.Worlds)
            {
                if (string.IsNullOrEmpty(entry.Entry))
                {
                    Log.Warning("V12Pak: world '{Name}' has no entry path, skipping", entry.Name);
                    continue;
                }
                if (!reader.HasEntry(entry.Entry))
                {
                    Log.Warning("V12Pak: world '{Name}' entry '{Entry}' not found, skipping", entry.Name, entry.Entry);
                    continue;
                }

                string xml = reader.ReadEntryText(entry.Entry)!;
                var parser = new WorldMLParser { TemplateProvider = templates };
                var worldContents = parser.Parse(xml);

                var world = new World(worldContents.Name ?? entry.Name)
                {
                    MountPoint = entry.Name,
                    ExtractPath = reader.TempDirectory
                };
                world.AddElement(worldContents);

                root.Worlds.Add(world);
                result.LoadedWorlds.Add(world);
                resolver.Mount(entry.Name, reader.TempDirectory);

                Log.Information("V12Pak: loaded world '{World}' from '{Entry}'", world.WorldName, entry.Entry);
            }

            if (templates.TemplateNames.Any())
            {
                root.Registry.RegisterOrReplace("TemplateProvider", templates);
                Log.Information("V12Pak: registered {Count} template(s)", templates.TemplateNames.Count());
            }
        }

        private static void LoadTemplatesFromPak(V12PakReader reader, WorldTemplateProvider templates)
        {
            foreach (var name in reader.EntryNames)
            {
                if (!name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)) continue;
                if (!name.Contains("/templates/", StringComparison.OrdinalIgnoreCase)
                    && !name.StartsWith("templates/", StringComparison.OrdinalIgnoreCase)) continue;

                var doc = new XmlDocument();
                using (var stream = reader.OpenEntry(name))
                {
                    if (stream == null) continue;
                    doc.Load(stream);
                }
                templates.AddDocument(doc, Path.GetFileNameWithoutExtension(name));
            }
        }

        private static void LoadDlls(V12PakReader reader, V12PakManifest manifest, V12PakLoadResult result, GameRoot root)
        {
            string paksDir = Path.Combine(reader.TempDirectory, "paks");
            if (!Directory.Exists(paksDir))
            {
                if (manifest.Paks.Count > 0)
                    Log.Warning("V12Pak: {Count} gamepak DLL(s) declared but no paks/ directory found", manifest.Paks.Count);
                return;
            }

            int before = root.Gamepaks.Gamepaks.Count;
            root.Gamepaks.LoadFromDirectory(paksDir);
            result.LoadedGamepaks = root.Gamepaks.Gamepaks.Count - before;
            Log.Information("V12Pak: loaded {Count} gamepak(s)", result.LoadedGamepaks);
        }

        /// <summary>
        /// Wrap each declared script entrypoint in the gamepack that serves its
        /// format — Contract (<c>.ct</c>) sources via <see cref="ContractGamepack"/>
        /// (compiled on start), precompiled ObjektRT modules
        /// (<c>.orbt</c>/<c>.oil</c>) via <see cref="ObjektRTGamepak"/> (loaded
        /// as-is) — and register it with the engine's
        /// <see cref="GamePak.GamepackLoader"/>. Scripts that fail to load are
        /// skipped with a warning (mirroring DLL loading behaviour).
        /// </summary>
        private static void LoadScripts(V12PakReader reader, V12PakManifest manifest, V12PakLoadResult result, GameRoot root)
        {
            foreach (var script in manifest.Scripts)
            {
                string? path = reader.ResolveEntryPath(script);
                if (path == null)
                {
                    result.SkippedGamepaks++;
                    Log.Warning("V12Pak: script '{Script}' not found in archive, skipping", script);
                    continue;
                }

                try
                {
                    var gamepack = CreateGamepack(path, Path.GetFileNameWithoutExtension(script));
                    root.Gamepaks.Add(gamepack);
                    result.LoadedGamepaks++;
                    Log.Information("V12Pak: registered {Kind} gamepak '{Name}' from '{Script}'",
                        gamepack is ObjektRTGamepak ? "ObjektRT" : "Contract", gamepack.Name, script);
                }
                catch (Exception ex)
                {
                    result.SkippedGamepaks++;
                    Log.Warning(ex, "V12Pak: script '{Script}' failed to load, skipping", script);
                }
            }
        }

        /// <summary>Creates the gamepack for one script entrypoint by format:
        /// compiled ObjektRT modules (<c>.orbt</c>/<c>.oil</c>/<c>.oir</c>) vs
        /// Contract <c>.ct</c> sources.</summary>
        private static IV12Gamepack CreateGamepack(string path, string name)
            => ObjektRTModuleLoader.IsCompiledScript(path)
                ? new ObjektRTGamepak(path, name)
                : new ContractGamepack(path, name);
    }
}
