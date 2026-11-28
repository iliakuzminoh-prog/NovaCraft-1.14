# NovaCraft 1.14

Windows launcher for Minecraft Java Edition. C# WinForms, .NET 8, self-contained Windows x64.

This README describes the source on the `nova/skin-preview-3d` branch. A green CI build proves compilation, tests and packaging; it does not prove that every modpack launches correctly on every PC.

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
- A searchable in-app CurseForge mod catalog is implemented against the official Core API: it discovers the Minecraft Mods class, filters by active Minecraft version and loader, and uses the API key only in memory. CurseForge's official third-party API access requires an approved key; NovaCraft does not ship a shared key.
- Single-mod installation is allowed only when the API reports `allowModDistribution=true`; the selected JAR is verified with SHA-1 and ZIP/JAR signature and installed only into the active build. Mod dependencies are not installed automatically.

### Mods, packs and downloads

- Modrinth mod feed and search.
- Every Modrinth and CurseForge catalog card has an icon tile. Official artwork replaces a deterministic generated monogram when the source image is available; absent or failed artwork does not leave an empty square.
- Mod downloads use the official Modrinth CDN, SHA-512 and JAR signature checks.
- Resource-pack, shader and datapack catalog cards support verified ZIP installation. The datapack world picker is restricted to the active build's `saves` directory.
- Downloads page shows source, file, target build, progress and task state (downloading, validation, installed, already installed or failed).
- Modrinth transfers and CmlLib Minecraft/Forge installation report byte progress. CmlLib `ByteProgress` is used instead of the high-frequency file event stream; task cards update in place rather than rebuilding the whole list for every callback.
- Queue history is currently in memory only; pause/resume/cancel and persistence across restarts are not implemented.
- Byte-progress wiring is compile-tested in CI, but live download speed and every loader's full network sequence have not been verified on the user's PC.

### Display and settings

- The Home page uses the bundled Minecraft hero artwork instead of a generated gradient. Its visible Play action is an accessible, keyboard-activatable button.

- Launch resolution defaults to the current primary Windows monitor bounds when the game launch options are created.
- Manual width/height mode remains available in Settings.
- RAM limit, fullscreen, close-after-launch, auto-repair toggle and extra JVM arguments are configurable.
- The resolution is detected on the user's Windows machine. No specific monitor resolution is hardcoded as the user's value.

### Skins and music

- Local PNG skin selection, dimension validation and preview.
- The skin preview is a software-rendered 3D Minecraft avatar with slow auto-rotation and mouse-drag rotation. It supports Classic/Slim arm width and both modern 64×64 and legacy 64×32 skin atlases.
- Windows CI includes DrawToBitmap smoke tests for the 3D preview with both atlas formats and both arm models; this is a rendering smoke test, not a screenshot-based proof of visual parity with the concept artwork.
- Skin source files are keyed by offline profile ID under `%LOCALAPPDATA%\\NovaCraft\\skins\\<profileId>.png`.
- Optional CustomSkinLoader / Skin Bridge support. A local PNG is not automatically uploaded to a skin website; other clients see it only when their skin mechanism/server supports it.
- Local MP3/WAV player using the Windows Media Control Interface (MCI): multi-file playlist, add/remove, play/pause/resume/stop, previous/next, automatic track advance and volume where the Windows audio device exposes MCI volume control. Playlist paths are saved in settings; audio files are never copied or uploaded.
- Custom OGG background-music resource-pack generation. The generated pack currently uses pack format 34 (Minecraft 1.21.1); compatibility with other versions is not guaranteed. The user must enable the pack in Minecraft. This replaces in-game background music; it is separate from the launcher player.

### Diagnostics and performance

- Basic diagnostics for write access, Java presence, RAM settings and selected Minecraft/loader files.
- The performance page reads physical/available RAM from Windows and calculates a conservative Java heap recommendation, leaving headroom for Windows. It changes only the Minecraft Java heap setting. No custom FPS mod is bundled or claimed; it links to Modrinth's optimization category and does not promise a frame-rate increase.
- NovaCraft does not change Windows registry settings, disable security software, add itself to startup or run hidden background services.
- Driver repair is not implemented. Diagnostics must not silently download or install drivers.

## Not implemented or not verified

- Microsoft/Mojang online authentication and Ely.by account login/synchronization.
- Real-world testing of the live CurseForge catalog with an approved production API key; the repository CI uses mocked API responses.
- Exact Fabric and Quilt loader-version pinning from imported pack manifests.
- Automatic Java runtime installation.
- A full skin library, cloud upload and universal skin visibility on offline/third-party servers.
- Windows MCI playback has not yet been manually exercised on the user's particular Windows installation; available MP3 codecs and audio-device volume support can vary.
- 3D UI animation and verified visual parity with the eight-screen UI reference shown in the project conversation on 2026-11-27.
- An original Java optimization mod tested on Radeon HD 6900 hardware or low-memory systems.
- Automatic graphics-driver repair or installation.
- Pause/resume/cancel and persistent download queue.
- Runtime launch testing for every Minecraft version, mod loader and imported pack. A successful import means the files passed importer checks; it does not guarantee that the pack launches.

## Tests and CI

The GitHub Actions workflow runs:

1. .NET restore.
2. Build with warnings treated as errors.
3. xUnit tests for offline UUID/nickname rules, ZIP catalog validation, Modrinth/CurseForge manifests, build-profile migration/isolation, 3D skin-preview rendering, local music playlist validation and RAM recommendation rules.
4. Self-contained Windows x64 publish and EXE size check.
5. Portable ZIP and Inno Setup installer packaging.
6. Installer smoke test: install, verify EXE, start the application process, uninstall and verify removal.

The installer smoke test does not launch Minecraft. Real pack compatibility and GPU-specific performance require tests on the target Windows machine.

## Build

Open `NovaCraft.sln` with .NET 8 SDK on Windows, or run the repository's `.github/workflows/build.yml` workflow. The executable is self-contained and is not padded with junk to reach an arbitrary file size.

See [docs/QUALITY_CONTRACT_RU.md](docs/QUALITY_CONTRACT_RU.md) for the acceptance rules and current verified/unverified status.
