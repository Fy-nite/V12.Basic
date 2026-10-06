using System.Threading;
using V12.Bindings;
using V12.Components;
using V12.Core;

int failures = 0;

void Check(bool cond, string label)
{
    if (cond) Console.WriteLine($"PASS  {label}");
    else { Console.WriteLine($"FAIL  {label}"); failures++; }
}

try
{
    var root = new GameRoot();
    var world = root.CreateWorld("TestWorld", "empty");
    Check(world != null && root.SelectedWorld == world, "world created and selected");

    string scriptPath = Path.Combine(Path.GetTempPath(), $"v12_bindings_smoke_{Guid.NewGuid():N}.ct");
    string v1 = """
        Contract Program {
            static fn Main() {
                var crate = V12.World.SpawnElementAt("crate", 1.0, 2.0, 3.0);
                V12.Components.AddMesh(crate, 0, 2.0, 1.0, 1.0);
                V12.Components.SetColor(crate, 0.8, 0.1, 0.1, 1.0);
                V12.Components.AddCollider(crate, 0, 2.0, 1.0, 1.0);
                V12.Registry.RegisterString("game.title", "V12 From Contract");
                V12.Registry.Register("crate.count", 1);
                V12.Log.Info("crate spawned from Contract");
            }
            static fn OnUpdate(deltaTime: float) {
                V12.Log.Info("OnUpdate tick");
            }
        }
        """;
    File.WriteAllText(scriptPath, v1);

    var host = ContractV12Host.Create(scriptPath);
    Check(host != null, "host created (compile + Main ran)");

    var crate = root.FindElement(e => e.Name == "crate");
    Check(crate != null, "V12.World.SpawnElementAt spawned 'crate'");
    if (crate == null) throw new Exception("crate missing");
    Check(crate.GetComponent("Transform") is TransformComponent tr
        && Math.Abs(tr.X - 1.0f) < 1e-4f
        && Math.Abs(tr.Y - 2.0f) < 1e-4f
        && Math.Abs(tr.Z - 3.0f) < 1e-4f, "SpawnElementAt position (1,2,3)");
    Check(crate.GetComponent("Mesh") != null, "V12.Components.AddMesh added Mesh");
    Check(crate.GetComponent("Material") is MaterialComponent mat && Math.Abs(mat.R - 0.8f) < 1e-3f, "SetColor set R=0.8");
    Check(crate.GetComponent("Collider") != null, "V12.Components.AddCollider added Collider");

    var title = root.Registry.Get("game.title")?.ServiceInstance as string;
    Check(title == "V12 From Contract", $"V12.Registry.RegisterString stored '{title}'");

    var count = root.Registry.Get("crate.count")?.ServiceInstance;
    Check(count is long l && l == 1, $"V12.Registry.Register stored long {count}");

    Check(host.InvokeUpdate(0.016f), "OnUpdate hook invoked");

    string v2 = """
        Contract Program {
            static fn Main() {
                var crate = V12.World.SpawnElementAt("crate", 1.0, 2.0, 3.0);
                var beacon = V12.World.SpawnElement("beacon");
                V12.Components.AddTransform(beacon, 5.0, 0.5, 5.0);
                V12.Registry.RegisterString("game.title", "V12 Reloaded");
            }
            static fn OnUpdate(deltaTime: float) {
                V12.Log.Info("reloaded tick");
            }
        }
        """;
    File.WriteAllText(scriptPath, v2);
    host.Reload();

    int crateCount = root.FindElements(e => e.Name == "crate").Count;
    Check(crateCount == 1, $"reload rebuilt world without duplicate 'crate' (found {crateCount})");
    var beacon = root.FindElement(e => e.Name == "beacon");
    Check(beacon != null, "reload ran new Main (spawned 'beacon')");
    var title2 = root.Registry.Get("game.title")?.ServiceInstance as string;
    Check(title2 == "V12 Reloaded", $"reload updated registry ('{title2}')");

    string bad = "Contract Program { static fn Main() { V12.World.Nope(); } }";
    File.WriteAllText(scriptPath, bad);
    try
    {
        host.Reload();
        Check(false, "compile error surfaced as ContractCompileException");
    }
    catch (ContractCompileException)
    {
        Check(true, "compile error surfaced as ContractCompileException");
    }

    // ── Hot reload ────────────────────────────────────────────────────────
    // host still holds the LAST GOOD module (v2): beacon exists, title "V12 Reloaded".
    Check(root.FindElement(e => e.Name == "beacon") != null, "host kept last good module after failed compile");

    var watcher = ContractHotReloader.Watch(scriptPath, host);
    using (watcher)
    {
        string v3 = """
            Contract Program {
                static fn Main() {
                    var beacon = V12.World.SpawnElement("hot_beacon");
                    V12.Components.AddTransform(beacon, 9.0, 1.0, 1.0);
                    V12.Registry.RegisterString("game.title", "V12 Hot");
                }
            }
            """;

        // The watcher must exist BEFORE the write — FileSystemWatcher does not
        // replay changes that happened before it subscribed.
        File.WriteAllText(scriptPath, v3);
        bool enqueued = false;
        for (int i = 0; i < 40 && !enqueued; i++)
        {
            Thread.Sleep(100);
            enqueued = watcher.PendingReloads > 0;
        }
        watcher.Pump();
        Check(enqueued, "file change enqueued a reload request");
        Check(root.FindElement(e => e.Name == "hot_beacon") != null, "hot reload ran new Main (spawned 'hot_beacon')");
        var title3 = root.Registry.Get("game.title")?.ServiceInstance as string;
        Check(title3 == "V12 Hot", $"hot reload swapped module ('{title3}')");

        // Broken save keeps the old module and does not throw.
        Thread.Sleep(300); // respect the reloader's debounce window
        File.WriteAllText(scriptPath, "Contract Program { static fn Main() { V12.World.Nope(); } }");
        bool enqueued2 = false;
        for (int i = 0; i < 40 && !enqueued2; i++)
        {
            Thread.Sleep(100);
            enqueued2 = watcher.PendingReloads > 0;
        }
        watcher.Pump();
        Check(true, "broken save pumped without throwing");
        Check(root.FindElement(e => e.Name == "hot_beacon") != null, "old module still running after broken save");

        // Good save recovers.
        Thread.Sleep(300);
        File.WriteAllText(scriptPath, "Contract Program { static fn Main() { V12.World.SpawnElement(\"recovered\"); } }");
        bool enqueued3 = false;
        for (int i = 0; i < 40 && !enqueued3; i++)
        {
            Thread.Sleep(100);
            enqueued3 = watcher.PendingReloads > 0;
        }
        watcher.Pump();
        Check(root.FindElement(e => e.Name == "recovered") != null, "hot reload recovered after broken save");
    }

    // ── Element scripts (ScriptRuntimeRegistry dispatch) ──────────────────
    var runtimeRegistry = V12ScriptRuntimeRegistration.RegisterAll(root);
    Check(runtimeRegistry.CreateForExtension(".lua") is MoonSharpScriptRuntime, ".lua resolves to MoonSharp runtime");
    Check(runtimeRegistry.CreateForExtension(".ct") is ContractScriptRuntime, ".ct resolves to Contract runtime");
    Check(runtimeRegistry.CreateForScript("scripts/mob.ct") is ContractScriptRuntime, "CreateForScript dispatches .ct by extension");
    Check(runtimeRegistry.CreateForScript("scripts/mob.lua") is MoonSharpScriptRuntime, "CreateForScript dispatches .lua by extension");

    // Inline Contract element script. Source must carry the ".ct" extension so
    // the registry picks the Contract runtime; ScriptText supplies the code.
    // The element is added to the world BEFORE the script component attaches,
    // so on_init can address it via V12.World.* lookups.
    var scriptedElement = new Element("ScriptedCube");
    scriptedElement.AddComponent(new TransformComponent(4f, 4f, 4f));
    world!.AddElement(scriptedElement);
    scriptedElement.AddComponent(new ScriptComponent
    {
        Source = "inline.ct",
        ScriptText = """
            Contract ScriptedCube {
                static fn on_init() {
                    var me = V12.Script.Owner();
                    V12.World.SetName(me, "RenamedByScript");
                    V12.Registry.Register("script.init.calls", 1);
                }
                static fn on_update(deltaTime: float) {
                    V12.Registry.RegisterDouble("script.last.dt", deltaTime);
                }
            }
            """
    });
    Check(scriptedElement.Name == "RenamedByScript", "on_init ran and V12.Script.Owner() resolved the element");
    var initCalls = root.Registry.Get("script.init.calls")?.ServiceInstance;
    Check(initCalls is long il && il == 1, "on_init ran (registry write)");

    var scriptComp = scriptedElement.GetComponent("Script") as ScriptComponent;
    Check(scriptComp != null, "ScriptComponent attached");
    scriptComp?.Update(0.5f);
    var lastDt = root.Registry.Get("script.last.dt")?.ServiceInstance;
    Check(lastDt is double d && Math.Abs(d - 0.5) < 1e-9, "on_update ran with deltaTime");

    // Broken .ct inline source logs the compile errors and never throws.
    var brokenElement = new Element("BrokenScriptCube");
    brokenElement.AddComponent(new ScriptComponent
    {
        Source = "broken.ct",
        ScriptText = "Contract Broken { static fn on_init() { V12.World.Nope(); } }"
    });
    world.AddElement(brokenElement);
    Check(true, "broken .ct compile logged without throwing");

    // ── Assembly-link: call the real V12 engine API directly by CLR name ──
    string linkPath = Path.Combine(Path.GetTempPath(), $"v12_link_{Guid.NewGuid():N}.ct");
    File.WriteAllText(linkPath, """
        Contract Program {
            static fn Main() {
                var t = new V12.Components.TransformComponent();
                t.X = 1.5;
                t.Y = 2.5;
                t.Rotation = 90.0;
                V12.Registry.RegisterDouble("link.transform.x", t.X);
                V12.Log.Info("assembly-link set a real TransformComponent");
            }
        }
        """);
    var linkHost = ContractV12Host.Create(linkPath, V12LinkedAssemblies.All);
    Check(linkHost != null, "assembly-link host created against the V12 engine");
    var linkX = root.Registry.Get("link.transform.x")?.ServiceInstance;
    Check(linkX is double lx && Math.Abs(lx - 1.5) < 1e-6, $"assembly-link called real V12 API (X={linkX})");
    File.Delete(linkPath);

    File.Delete(scriptPath);
}
catch (Exception ex)
{
    Console.WriteLine($"FAIL  harness threw: {ex.GetType().Name}: {ex.Message}");
    Console.WriteLine(ex.StackTrace);
    failures++;
}

Console.WriteLine(failures == 0 ? "\nALL TESTS PASSED" : $"\n{failures} TEST(S) FAILED");
return failures == 0 ? 0 : 1;
