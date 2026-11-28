# NovaBoost

NovaBoost is an original client-side Fabric optimization project for **Minecraft 1.21.11**.

## Current implementation (0.1.2)

The first feature is conservative entity occlusion culling:

- runs after vanilla frustum checks and skips only ordinary-sized vanilla entities when three sample rays are blocked by occluding blocks;
- caches the result for a short configurable number of client ticks, validating entity UUID and camera/target movement before reusing it;
- evicts the least-recently-used entry when the bounded cache is full instead of flushing every cached result;
- limits raycasts per tick so the optimization cannot generate an unbounded amount of extra work;
- fails open for mod-provided entity types, unknown entity types and unusually large hitboxes; it also leaves players/avatars, glowing entities, named entities, vehicles, passengers and nearby entities alone;
- runs only on the client and does not change remote server files, packets, world state or server TPS.

Settings are written to `config/novaboost.properties` on first launch:

| Setting | Default | Meaning |
| --- | ---: | --- |
| `entity-occlusion` | `true` | Enable or disable this feature |
| `raycasts-per-tick` | `24` | Maximum visibility rays per client tick |
| `cache-ticks` | `2` | How long a visibility result is reused (clamped to 1–3 ticks) |
| `minimum-distance` | `8` | Do not test entities closer than this many blocks |
| `maximum-distance` | `80` | Do not test entities farther than this many blocks |

The culler is deliberately conservative. If the query budget is exhausted, a result is unknown, or any of the sampled points is visible, the entity is rendered normally.

## Project design

See [the NovaBoost technical architecture](docs/PROJECT-DESIGN.md) and [the NovaCraft integration design](../docs/NOVACRAFT-NOVABOOST-INTEGRATION.md) for the modular roadmap, safety rules, benchmark plan and release criteria.

## Build

Build requirement: JDK 25 for the current Fabric Loom plugin. The produced mod bytecode is Java 21 (`options.release = 21`) and targets Minecraft's Java 21 runtime.

From this directory:

```bash
./gradlew clean test build
```

The remapped Fabric mod is produced in `build/libs/`.

## Scope and limitations

NovaBoost 0.1.2 is **not a Sodium replacement** and does not yet replace Minecraft's chunk renderer. It introduces one independently implemented client optimization with bounded work and tests for the query budget. Actual FPS gains depend on the scene; no fixed multiplier is promised. Client-side code cannot increase the TPS of a remote server.

The project targets Fabric 1.21.11 only. Other Minecraft versions and loaders are rejected by the launcher.
