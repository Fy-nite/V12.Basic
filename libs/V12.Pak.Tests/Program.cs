using System.Runtime.InteropServices;
using V12.Bindings;
using V12.Components;
using V12.Core;
using V12.Core.Core.Interfaces;
using V12.Core.Interfaces;
using V12.Pak;
using V12.Pak.Tests;

int failures = 0;

void Check(bool cond, string label)
{
    if (cond) Console.WriteLine($"PASS  {label}");
    else { Console.WriteLine($"FAIL  {label}"); failures++; }
}

string work = Path.Combine(Path.GetTempPath(), $"v12pak_tests_{Guid.NewGuid():N}");
var cacheDirectories = new List<string>();
try
{
    string sampleDir = Path.Combine(work, "sample");
    string worldDir = Path.Combine(sampleDir, "worlds", "DemoWorld");
    string templatesDir = Path.Combine(worldDir, "templates");
    Directory.CreateDirectory(templatesDir);
    Directory.CreateDirectory(Path.Combine(sampleDir, "assets", "textures"));
    Directory.CreateDirectory(Path.Combine(sampleDir, "assets", "meshes"));
    Directory.CreateDirectory(Path.Combine(sampleDir, "assets", "scripts"));
    Directory.CreateDirectory(Path.Combine(sampleDir, "paks"));
    Directory.CreateDirectory(Path.Combine(sampleDir, "scripts"));

    string worldXml = """
        <?xml version="1.0" encoding="utf-8"?>
        <World name="DemoWorld">
            <Element name="crate">
                <TransformComponent x="1.000" y="2.000" z="3.000" />
                <Component type="MeshComponent" name="mesh_data" Shape="Box" Width="1.000" Height="1.000" Depth="1.000" />
                <Component type="MeshRenderer" Mesh="mesh_data" />
                <ColliderComponent Shape="Box" Width="1.000" Height="1.000" Depth="1.000" isTrigger="false" />
            </Element>
            <Element name="crate2" template="box" />
        </World>
        """;
    string templateXml = """
        <Template name="box">
            <Element name="boxItem" description="from pak template">
                <MeshComponent shape="1" />
            </Element>
        </Template>
        """;
    File.WriteAllText(Path.Combine(worldDir, "world.xml"), worldXml);
    File.WriteAllText(Path.Combine(templatesDir, "box.xml"), templateXml);
    byte[] png = { 0x89, 0x50, 0x4E, 0x47, 1, 2, 3 };
    File.WriteAllBytes(Path.Combine(sampleDir, "assets", "textures", "crate.png"), png);
    File.WriteAllBytes(Path.Combine(sampleDir, "assets", "meshes", "hero.glb"), new byte[] { 1, 2, 3, 4 });
    File.WriteAllText(Path.Combine(sampleDir, "assets", "scripts", "player.lua"), "print('hi')");
    string mainCt = """
        Contract Game {
            static fn Main() {
                var crate = V12.World.SpawnElementAt("ct_crate", 0.0, 1.0, 0.0);
                V12.Registry.RegisterString("ct.status", "started");
            }

            static fn OnUpdate(deltaTime: float) {
                V12.Registry.RegisterDouble("ct.tick_time", deltaTime);
            }
        }
        """;
    File.WriteAllText(Path.Combine(sampleDir, "scripts", "main.ct"), mainCt);
    File.Copy(typeof(TestGamePak).Assembly.Location, Path.Combine(sampleDir, "paks", "TestGamePak.dll"), true);

    // Native libraries, one flattened next to the managed DLLs (a RID-specific
    // publish) and one under runtimes/<rid>/native (a portable publish). The
    // loader has to find both for the platform it runs on.
    string nativeExtension = OperatingSystem.IsWindows() ? ".dll" : OperatingSystem.IsMacOS() ? ".dylib" : ".so";
    string nativePrefix = OperatingSystem.IsWindows() ? "" : "lib";
    string hostRid = RuntimeInformation.RuntimeIdentifier;

    Directory.CreateDirectory(Path.Combine(sampleDir, "bin", "runtimes", hostRid, "native"));
    Directory.CreateDirectory(Path.Combine(sampleDir, "bin", "runtimes", "plan9-x64", "native"));
    File.WriteAllBytes(Path.Combine(sampleDir, "bin", nativePrefix + "flatnativetest" + nativeExtension), new byte[] { 1 });
    File.WriteAllBytes(
        Path.Combine(sampleDir, "bin", "runtimes", hostRid, "native", nativePrefix + "ridnativetest" + nativeExtension),
        new byte[] { 2 });
    File.WriteAllBytes(
        Path.Combine(sampleDir, "bin", "runtimes", "plan9-x64", "native", "libwrongplatform.so"), new byte[] { 3 });

    string pakPath = Path.Combine(work, "game.v12pak");

    new GameRoot().BuildPak(sampleDir, pakPath);
    Check(File.Exists(pakPath), "BuildPak produced game.v12pak");

    using (var reader = new V12PakReader(pakPath))
    {
        Check(reader.Manifest.Version == 1, "reader parsed manifest");
        Check(reader.Manifest.Worlds.Count == 1, "generated manifest declares 1 world");
        Check(reader.Manifest.Assets.Count == 3, "generated manifest declares 3 assets");
        Check(reader.Manifest.Paks.Count == 1, "generated manifest declares 1 pak DLL");
        Check(reader.Manifest.Scripts.Count == 1, "generated manifest declares 1 Contract script");
        Check(reader.HasEntry("manifest.json"), "reader indexed manifest.json");
        Check(reader.HasEntry("worlds/DemoWorld/world.xml"), "reader indexed world.xml");
        Check(reader.HasEntry("worlds/DemoWorld/templates/box.xml"), "reader indexed template");
        Check(reader.HasEntry("assets/textures/crate.png"), "reader indexed crate.png");
        Check(reader.HasEntry("scripts/main.ct"), "reader indexed main.ct");
        string? xml = reader.ReadEntryText("worlds/DemoWorld/world.xml");
        Check(xml != null && xml.Contains("<World"), "ReadEntryText reads world.xml");

        string tempDir = reader.TempDirectory;
        cacheDirectories.Add(tempDir);
        Check(reader.ReusedCache == false, "first open extracted the pak");
        Check(Directory.Exists(tempDir), "extraction directory exists while open");

        reader.Dispose();
        Check(Directory.Exists(tempDir), "reader Dispose keeps the cache directory");

        // Opening the same pak again must reuse that exact directory instead of
        // extracting a second full copy (the old behaviour grew /tmp per launch).
        using var again = new V12PakReader(pakPath);
        Check(again.ReusedCache, "second open reused the cached extraction");
        Check(string.Equals(again.TempDirectory, tempDir, StringComparison.Ordinal),
            "cache directory is content-addressed, not GUID-random");
        Check(again.HasEntry("assets/textures/crate.png"), "cached extraction still indexes entries");

        string? cachedXml = again.ReadEntryText("worlds/DemoWorld/world.xml");
        Check(cachedXml != null && cachedXml.Contains("<World"), "cached extraction serves entry text");
    }

    var root = new GameRoot();
    using (var result = root.LoadPak(pakPath, new V12PakOptions { LoadDlls = true, LoadContractScripts = true }))
    {
        cacheDirectories.Add(result.Reader.TempDirectory);
        Check(result.LoadedWorlds.Count == 1, "LoadPak loaded 1 world");
        var world = result.LoadedWorlds[0];
        Check(root.Worlds.Contains(world), "world added to GameRoot.Worlds");
        Check(world.WorldName == "DemoWorld", "world name resolved from XML");

        var worldRoot = world.Root.FirstOrDefault();
        var crate = worldRoot?.FindChildByName("crate");
        Check(crate != null, "crate element parsed from pak world.xml");
        if (crate != null)
        {
            Check(Math.Abs(crate.LocalTransform.Position.X - 1.0f) < 1e-4f, "crate transform x=1");
            Check(crate.GetComponent("mesh_data") is MeshComponent, "crate has Mesh component (name from XML attribute)");
            Check(crate.GetComponent("Collider") != null, "crate has Collider component");
        }
        var crate2 = worldRoot?.FindChildByName("crate2");
        var boxItem = worldRoot?.FindChildByNameRecursive("boxItem");
        Check(crate2 != null && boxItem != null && boxItem.GetComponent("Mesh") != null, "template 'box' expanded from pak templates (boxItem present with component)");

        var resolver = root.Registry.Get<IAssetResolver>();
        Check(resolver is V12PakAssetResolver, "pak resolver registered as AssetResolver");
        string resolvedPng = resolver!.Resolve("v12://textures/crate.png");
        Check(File.Exists(resolvedPng), "v12://textures/crate.png resolves to a real file");

        // Worlds authored for --game mode address assets by archive path
        // ("v12://Assets/..."), not by the bare manifest key. That spelling used
        // to fail inside a pak with "did not resolve to a file".
        string resolvedByPath = resolver.Resolve("v12://assets/textures/crate.png");
        Check(File.Exists(resolvedByPath), "v12://assets/textures/crate.png (archive path) resolves");

        string resolvedMixedCase = resolver.Resolve("v12://Assets/Textures/Crate.PNG");
        Check(File.Exists(resolvedMixedCase), "archive-path URIs resolve case-insensitively on Linux");
        using (var s = resolver.Open("v12://textures/crate.png"))
            Check(s != null && s.Length == png.Length, "v12://textures/crate.png opens with expected length");

        using (var s = resolver.Open("v12://assets/textures/crate.png"))
            Check(s != null && s.Length == png.Length, "archive-path URI opens with expected length");

        using (var s = resolver.Open("v12://scripts/player.lua"))
        {
            bool ok = s != null;
            if (ok)
            {
                using var r = new StreamReader(s);
                ok = r.ReadToEnd() == "print('hi')";
            }
            Check(ok, "v12://scripts/player.lua opens with expected text");
        }

        // Native probing: a pak's own libraries must be findable on the platform
        // it is running on, in both the flattened and the per-RID layout.
        Check(V12PakLoader.TryFindNativeLibrary("flatnativetest", out string flatNative), "flattened native library found");
        Check(File.Exists(flatNative) && flatNative.Contains("flatnativetest"), "flattened native points at the pak copy");

        Check(V12PakLoader.TryFindNativeLibrary("ridnativetest", out string ridNative), "runtimes/<rid>/native library found");
        Check(File.Exists(ridNative) && ridNative.Replace('\\', '/').Contains("/runtimes/" + hostRid + "/native/"),
            "per-RID native points at the matching platform folder");

        Check(!V12PakLoader.TryFindNativeLibrary("wrongplatform", out _), "another platform's RID folder is not probed");
        Check(!V12PakLoader.TryFindNativeLibrary("definitelynotapaknative", out _), "unknown native library is not invented");

        Check(result.LoadedGamepaks == 2, "LoadDlls+LoadContractScripts loaded 2 gamepaks (1 DLL + 1 .ct)");
        var gp = root.Gamepaks.FindByName("TestGamePak");
        Check(gp != null, "TestGamePak gamepak discoverable");
        if (gp != null)
        {
            gp.Initialize();
            gp.OnStart();
            Check(true, "gamepak Initialize/OnStart ran without throwing");
        }

        var ctGamepack = root.Gamepaks.FindByName("main");
        Check(ctGamepack is ContractGamepack, "Contract gamepak discoverable from scripts/main.ct");
        if (ctGamepack is ContractGamepack contractGp)
        {
            contractGp.Initialize();
            contractGp.OnStart();
            Check(true, "Contract gamepak Initialize/OnStart ran without throwing");
            Check(root.FindElement(e => e.Name == "ct_crate") != null, "Contract Main spawned 'ct_crate'");
            Check(root.Registry.Get("ct.status")?.ServiceInstance as string == "started", "Contract Main registered ct.status='started'");

            var tick = root.Registry.Get("ContractTick:main")?.ServiceInstance as IGameService;
            Check(tick != null, "Contract OnUpdate tick service registered");
            if (tick != null)
            {
                tick.Update(0.25f);
                var tickTime = root.Registry.Get("ct.tick_time")?.ServiceInstance;
                Check(tickTime is double d && Math.Abs(d - 0.25) < 1e-4, $"Contract OnUpdate hook ran per frame (tick_time={tickTime})");
            }
        }
    }

    var restricted = new GameRoot();
    using (var res2 = restricted.LoadPak(pakPath, new V12PakOptions
           {
               LoadDlls = false,
               AllowedAssetExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".xml" },
               BlockedAssetKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "textures/crate.png" }
           }))
    {
        cacheDirectories.Add(res2.Reader.TempDirectory);
        Check(res2.LoadedWorlds.Count == 1, "restricted load still loads worlds");
        Check(res2.SkippedAssets == 3, $"restricted load skipped 3 assets (got {res2.SkippedAssets})");
        var r2resolver = restricted.Registry.Get<IAssetResolver>();
        string resolved2 = r2resolver!.Resolve("v12://textures/crate.png");
        Check(resolved2 == "v12://textures/crate.png", "blocked asset key does not resolve");

        // The archive-path lookup must not become a way around the blocklist.
        string resolvedBlockedPath = r2resolver.Resolve("v12://assets/textures/crate.png");
        Check(resolvedBlockedPath == "v12://assets/textures/crate.png",
            "blocked asset does not resolve by archive path either");
        using var blockedStream = r2resolver.Open("v12://textures/crate.png");
        Check(blockedStream == null, "blocked asset does not open");
        Check(res2.SkippedGamepaks == 2, $"LoadDlls=false skipped gamepak DLL + Contract script (got {res2.SkippedGamepaks})");
        Check(restricted.Gamepaks.Gamepaks.Count == 0, "no gamepaks loaded when LoadDlls=false");
    }

    var badDir = Path.Combine(work, "bad");
    Directory.CreateDirectory(Path.Combine(badDir, "scripts"));
    File.WriteAllText(Path.Combine(badDir, "scripts", "broken.ct"),
        "Contract Game { static fn Main() { V12.World.Nope(); } }");
    string badPakPath = Path.Combine(work, "broken.v12pak");
    new GameRoot().BuildPak(badDir, badPakPath);
    var badRoot = new GameRoot();
    using (var resBad = badRoot.LoadPak(badPakPath, new V12PakOptions { LoadContractScripts = true }))
    {
        cacheDirectories.Add(resBad.Reader.TempDirectory);
        Check(resBad.LoadedGamepaks == 1, "bad script pak still registers gamepack at load");
        var broken = badRoot.Gamepaks.FindByName("broken");
        Check(broken is ContractGamepack, "broken.ct registered as Contract gamepack");
        if (broken is ContractGamepack brokenGp)
        {
            brokenGp.Initialize();
            bool threw = false;
            try { brokenGp.OnStart(); }
            catch (ContractCompileException) { threw = true; }
            catch (Exception ex) { Console.WriteLine($"DIAG wrong exception: {ex.GetType().Name}: {ex.Message}"); }
            Check(threw, "compile error surfaces as ContractCompileException from OnStart");
        }
    }

    var noWorlds = new GameRoot();
    using (var res3 = noWorlds.LoadPak(pakPath, new V12PakOptions { AllowWorldLoading = false }))
    {
        cacheDirectories.Add(res3.Reader.TempDirectory);
        Check(res3.LoadedWorlds.Count == 0, "AllowWorldLoading=false loads no worlds");
        Check(noWorlds.Worlds.Count == 0, "GameRoot.Worlds untouched");
    }
}
catch (Exception ex)
{
    Console.WriteLine($"FAIL  harness threw: {ex.GetType().Name}: {ex.Message}");
    Console.WriteLine(ex.StackTrace);
    failures++;
}
finally
{
    try { if (Directory.Exists(work)) Directory.Delete(work, true); } catch { }

    // The extraction cache is deliberately persistent, so the test removes the
    // directories it created rather than leaving them behind.
    foreach (string directory in cacheDirectories.Distinct(StringComparer.OrdinalIgnoreCase))
        V12PakCache.Discard(directory);
}

Console.WriteLine(failures == 0 ? "\nALL TESTS PASSED" : $"\n{failures} TEST(S) FAILED");
return failures == 0 ? 0 : 1;
