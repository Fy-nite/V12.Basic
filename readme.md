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

## Dependencies

- Project reference: `V12.Core` (from `libs/V12/`)
- Targets .NET 10
