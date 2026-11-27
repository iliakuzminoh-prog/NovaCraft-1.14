# NovaCraft 1.14

Windows launcher for Minecraft Java Edition. C# WinForms, .NET 8, self-contained Windows x64.

This README describes the source on the `nova/refined-interface` branch. A green CI build proves compilation, tests and packaging; it does not prove that every modpack launches correctly on every PC.

## Implemented

### Launcher and profiles

- Dark Russian-language WinForms UI with Home, Versions, Builds, Mods, Resource Packs, Shaders, Datapacks, Modpack catalog links, Music, Skins, Performance, Servers, Settings, Diagnostics and Downloads pages.
- A build selector in the top bar switches the active Minecraft instance.
- Each build profile stores its own Minecraft directory, version, loader and optional exact loader version.
- Existing Minecraft folder is preserved as the primary profile. Legacy saved build names are migrated into separate instance folders.
- Removing a profile removes only its profile record. It does not delete the instance directory, worlds, mods or configuration files.
- Vanilla, Fabric, Forge and Quilt installation paths are present. The exact Forge version from an imported pack is passed to the Forge installer. Fabric and Quilt currently use their installer defaults; exact loader-version pinning for those loaders is not claimed.
- Offline nickname sessions use the Java-compatible offline UUID calculation and a shared nickname validator. Microsoft/Mojang authentication is not implemented.

### Modrinth and CurseForge packs

- Modrinth `.mrpack` import reads `modrinth.index.json`, Minecraft version, loader, required files, hashes and client overrides.
- CurseForge exported ZIP import reads `manifest.json`, Minecraft version, loader version, project/file IDs and the declared overrides folder.
- Modrinth downloads are restricted to HTTPS `cdn.modrinth.com` and SHA-512 verification.
- CurseForge uses its official API with a key entered by the user for that import. The key is kept in process memory and is not saved in `settings.json` or logs. Required files are verified against CurseForge SHA-1 metadata and JAR signature.
- CurseForge files without a permitted download URL are reported as unavailable; the launcher does not bypass author restrictions.
- Import rejects unsafe paths, ZIP path traversal, duplicate override paths, symlinks, oversized files and excessive total extraction size.
- Import creates a new isolated build folder and records the source platform.
- The current CurseForge integration imports an exported archive. A searchable live CurseForge catalog inside NovaCraft is not implemented.

### Mods, packs and downloads

- Modrinth mod feed and search.
- Mod downloads use the official Modrinth CDN, SHA-512 and JAR signature checks.
- Resource-pack, shader and datapack catalog cards support verified ZIP installation. The datapack world picker is restricted to the active build's `saves` directory.
- Downloads page shows source, file, target build, progress and task state (downloading, validation, installed, already installed or failed).
- Modrinth transfers and CmlLib Minecraft/Forge installation report byte progress. CmlLib `ByteProgress` is used instead of the high-frequency file event stream; task cards update in place rather than rebuilding the whole list for every callback.
- Queue history is currently in memory only; pause/resume/cancel and persistence across restarts are not implemented.
- Byte-progress wiring is compile-tested in CI, but live download speed and every loader's full network sequence have not been verified on the user's PC.

### Display and settings

- Launch resolution defaults to the current primary Windows monitor bounds when the game launch options are created.
- Manual width/height mode remains available in Settings.
- RAM limit, fullscreen, close-after-launch, auto-repair toggle and extra JVM arguments are configurable.
- The resolution is detected on the user's Windows machine. No specific monitor resolution is hardcoded as the user's value.

### Skins and music

- Local PNG skin selection, dimension validation and preview.
- Skin source files are keyed by offline profile ID under `%LOCALAPPDATA%\\NovaCraft\\skins\\<profileId>.png`.
- Optional CustomSkinLoader / Skin Bridge support. A local PNG is not automatically uploaded to a skin website; other clients see it only when their skin mechanism/server supports it.
- Custom OGG background-music resource-pack generation. The generated pack currently uses pack format 34 (Minecraft 1.21.1); compatibility with other versions is not guaranteed. The user must enable the pack in Minecraft.

### Diagnostics and performance

- Basic diagnostics for write access, Java presence, RAM settings and selected Minecraft/loader files.
- The performance page changes Minecraft/JVM launch settings only. No custom FPS mod is included or claimed.
- NovaCraft does not change Windows registry settings, disable security software, add itself to startup or run hidden background services.
- Driver repair is not implemented. Diagnostics must not silently download or install drivers.

## Not implemented or not verified

- Microsoft/Mojang online authentication and Ely.by account login/synchronization.
- Live CurseForge catalog search and one-click import from a CurseForge project page.
- Exact Fabric and Quilt loader-version pinning from imported pack manifests.
- Automatic Java runtime installation.
- A full skin library, cloud upload and universal skin visibility on offline/third-party servers.
- A full music player with a persistent playlist and playback engine.
- 3D UI animation and verified visual parity with the eight-screen UI reference shown in the project conversation on 2026-11-27.
- An original Java optimization mod tested on Radeon HD 6900 hardware or low-memory systems.
- Automatic graphics-driver repair or installation.
- Pause/resume/cancel and persistent download queue.
- Runtime launch testing for every Minecraft version, mod loader and imported pack. A successful import means the files passed importer checks; it does not guarantee that the pack launches.

## Tests and CI

The GitHub Actions workflow runs:

1. .NET restore.
2. Build with warnings treated as errors.
3. xUnit tests for offline UUID/nickname rules, ZIP catalog validation, Modrinth/CurseForge manifests and build-profile migration/isolation.
4. Self-contained Windows x64 publish and EXE size check.
5. Portable ZIP and Inno Setup installer packaging.
6. Installer smoke test: install, verify EXE, start the application process, uninstall and verify removal.

The installer smoke test does not launch Minecraft. Real pack compatibility and GPU-specific performance require tests on the target Windows machine.

## Build

Open `NovaCraft.sln` with .NET 8 SDK on Windows, or run the repository's `.github/workflows/build.yml` workflow. The executable is self-contained and is not padded with junk to reach an arbitrary file size.

See [docs/QUALITY_CONTRACT_RU.md](docs/QUALITY_CONTRACT_RU.md) for the acceptance rules and current verified/unverified status.
