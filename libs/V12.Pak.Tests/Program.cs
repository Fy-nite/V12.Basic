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
        reader.Dispose();
        Check(!Directory.Exists(tempDir), "reader Dispose removed temp dir");
    }

    var root = new GameRoot();
    using (var result = root.LoadPak(pakPath, new V12PakOptions { LoadDlls = true, LoadContractScripts = true }))
    {
        Check(result.LoadedWorlds.Count == 1, "LoadPak loaded 1 world");
        var world = result.LoadedWorlds[0];
        Check(root.Worlds.Contains(world), "world added to GameRoot.Worlds");
        Check(world.WorldName == "DemoWorld", "world name resolved from XML");

        var worldRoot = world.Root.FirstOrDefault();
        var crate = worldRoot?.FindChildByName("crate");
        Check(crate != null, "crate element parsed from pak world.xml");
        if (crate != null)
        {
            Check(crate.GetComponent("Transform") is TransformComponent tr && Math.Abs(tr.X - 1.0f) < 1e-4f, "crate Transform x=1");
            Check(crate.GetComponent("mesh_data") is MeshComponent, "crate has Mesh component (name from XML attribute)");
            Check(crate.GetComponent("Collider") != null, "crate has Collider component");
        }
        var crate2 = worldRoot?.FindChildByName("crate2");
        var boxItem = worldRoot?.FindChildByName("boxItem");
        Check(crate2 != null && boxItem != null && boxItem.GetComponent("Mesh") != null, "template 'box' expanded from pak templates (boxItem present with component)");

        var resolver = root.Registry.Get<IAssetResolver>();
        Check(resolver is V12PakAssetResolver, "pak resolver registered as AssetResolver");
        string resolvedPng = resolver!.Resolve("v12://textures/crate.png");
        Check(File.Exists(resolvedPng), "v12://textures/crate.png resolves to a real file");
        using (var s = resolver.Open("v12://textures/crate.png"))
            Check(s != null && s.Length == png.Length, "v12://textures/crate.png opens with expected length");
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
        Check(res2.LoadedWorlds.Count == 1, "restricted load still loads worlds");
        Check(res2.SkippedAssets == 3, $"restricted load skipped 3 assets (got {res2.SkippedAssets})");
        var r2resolver = restricted.Registry.Get<IAssetResolver>();
        string resolved2 = r2resolver!.Resolve("v12://textures/crate.png");
        Check(resolved2 == "v12://textures/crate.png", "blocked asset key does not resolve");
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
}

Console.WriteLine(failures == 0 ? "\nALL TESTS PASSED" : $"\n{failures} TEST(S) FAILED");
return failures == 0 ? 0 : 1;
