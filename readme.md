# V12.Basic

A **batteries-included companion library** for the V12 engine that provides standard gameplay components and systems. This is the layer most game code uses, sitting on top of `V12.Core`.

**Source:** Git submodule from `https://forgejo.finite.ovh/Finite/V12.Basic`

## Components

### Player Components

| File | Purpose |
|---|---|
| `PlayerComponent.cs` | Core player element -- manages input consumption, yaw/pitch rotation, mouse look, teleportation |
| `DesktopPlayerComponent.cs` | Desktop-specific player variant |
| `VRPlayerComponent.cs` | VR-specific player variant |

### Gameplay Components

| File | Purpose |
|---|---|
| `CameraComponent.cs` | Camera component -- FOV, near/far clip, current-camera flag |
| `LocomotionComponent.cs` | Movement controller -- walk/sprint/jump speeds, grounded state, velocity, look sensitivity |
| `HealthComponent.cs` | Health pool with max HP and invincibility |

### Physics Components

| File | Purpose |
|---|---|
| `PhysicsBodyComponent.cs` | Wraps a physics body (rigid/kinematic), links V12 elements to physics simulation |
| `RigidBodyComponent.cs` | Dynamic rigid body -- mass, gravity scale, isKinematic |

### Interaction Components

| File | Purpose |
|---|---|
| `LaserInteraction.cs` | VR laser pointer interaction support |

## Systems

| File | Purpose |
|---|---|
| `LocomotionSystem.cs` | Processes `LocomotionComponent` -- applies movement, gravity, jumping, sprinting |
| `PhysicsLocomotionSystem.cs` | Physics-based movement (integrates with BepuPhysics via the physics backend) |
| `PhysicsService.cs` | Physics simulation service |
| `CameraControlSystem.cs` | Camera look/orbit logic |
| `PickupSystem.cs` | VR/desktop grab/interact -- laser pointing + selection |
| `ButtonSystem.cs` | UI button interaction detection |
| `ScriptSystem.cs` | Lua script execution via MoonSharp |

## Registry

`BasicRegistry.cs` is a convenience static class that bulk-registers all Basic systems into a `GameRoot` in one call.

## Building & Scene API

Two convenience layers for building scenes quickly: `V12.Basic.Building` (programmatic) and `V12.Basic.Scene` (source-driven).

### Programmatic building (`V12.Basic.Building`)

| Helper | Purpose |
|---|---|
| `element.AddMesh/AddCollider/AddPhysics/AddMaterial/AddTags` | Attach standard components; each returns the component for chaining |
| `element.AddPointLight/AddSpotLight/AddDirectionalLight` | Lights |
| `element.SetTransform/SetPosition/SetRotation`, `GetOrAdd<T>()` | Transform + generic component access |
| `world.SpawnBox/SpawnSphere/SpawnCapsule/SpawnCylinder/SpawnPlane/SpawnPrimitive` | Mesh + collider + physics in one call |
| `world.AddGround/AddBoundaryWalls/SpawnBoxStack/SpawnGrid/AddSpawnPoint/AddSun` | Level scaffolding |
| `world.SpawnPlayer3D(...)` | Player + locomotion + camera (camera auto-created) |
| `new ElementBuilder("Crate").Box().Dynamic().Material(...).At(...).Build(world)` | Fluent builder (composes the above) |
| `world.SyncLocalTransforms()` | Reconcile `TransformComponent` → `LocalTransform` after loading XML |

```csharp
world.AddGround(size: 40f, topY: 0f);
world.SpawnBoxStack(new Vector3(0, 0.5f, 0), 3, 1f);
new ElementBuilder("Crate").Box(1, 1, 1).Dynamic().At(4, 0.5f, 0).Build(world);
world.PersistentWorld.SpawnPlayer3D(at: new Vector3(0, 1.5f, 0));
```

### Source-driven scenes (`V12.Basic.Scene`)

Load a WorldML scene, then find and bind code to its elements — the editor-authoring → code loop:

| Helper | Purpose |
|---|---|
| `root.LoadSceneFromXml/File/Resource(...)` | Scene XML → `World` |
| `world.Find(name)`, `FindAll`, `FindByPath`, `FindByTag`, `Require`, `Enumerate()`, `FindComponent<T>(name)` | Element lookup (case-insensitive) |
| `world.Bind().On(name, ...).OnTag(tag, ...).On<TC>(name, ...).OnPath(path, ...).Auto()` | Bind handlers by name/tag/path/component; runs now and as content is added |

```csharp
world.Bind()
     .On("SpawnBoxButton", e => e.GetComponent<ButtonComponent>().OnPressed = SpawnBox)
     .On<HealthComponent>("Boss", h => h.MaxHealth = 500)
     .OnTag("enemy", e => e.AddComponent(new HealthComponent(50)))
     .Auto();
```

It is world-agnostic, so it binds to worlds loaded from a `.v12pak` (via V12.Pak's `GameRoot.LoadPak`) the same way.

## Dependencies

- Project reference: `V12.Core` (from `libs/V12/`)
- Targets .NET 10
