# NovaCraft 1.14

Windows launcher for Minecraft Java Edition. C# WinForms, .NET 8, self-contained Windows x64.

This README describes the integrated launcher and NovaBoost work on the `nova/novaboost-mod-0.1.0` development branch. A green CI build proves compilation, tests and packaging; it does not prove that every modpack launches correctly on every PC.

## Implemented

### Launcher and profiles

- Dark Russian-language WinForms UI with 16 feature pages grouped under seven expandable left-navigation sections: Главная, Каталог, Профили и скины, Настройки, Журнал и диагностика. The existing page factories and original page indices are preserved.
- The global app bar shows the active build, current page, offline profile chip and direct Install / Play actions; the profile chip opens profile and skin management.
- A build selector in the top bar switches the active Minecraft instance.
- The shared `NovaCraft.UI.AppTheme` applies a consistent dark palette, card outlines, input styling and button states to lazily-created pages.
- Each build profile stores its own Minecraft directory, version, loader and optional exact loader version.
- Build profiles can be copied into a new isolated folder. The user chooses whether to copy mods, configs, worlds, game options/server list, resource packs, shader packs and screenshots. The source profile is never overwritten; large Minecraft libraries, assets and version binaries are not duplicated and are installed into the new profile at first launch.
- World saves can be archived to a timestamped ZIP inside the profile's `backups` folder. Backups never delete or move the original worlds. Copy and backup operations reject reparse points/symbolic links to avoid following redirected paths.
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
- The Mods page has an in-place switch between the Modrinth catalogue and the active build's installed JAR files. Installed mods can be temporarily disabled and re-enabled by renaming `.jar` to `.jar.disabled` and back; files are never deleted by this switch. The list only touches JARs directly inside the active build's `mods` folder and refuses paths that resolve through reparse points.
- The Home dashboard uses an original bundled voxel-landscape illustration as its background; the compressed pixel-art image is embedded into the assembly, so it loads locally and needs no network connection or loose asset file. Software-rendered 3D accents animate over it: perspective-projected rotating cubes, a pulsing portal aura, dashed energy rings, orbiting sparks and camera parallax. If the artwork resource cannot be decoded, the earlier procedural voxel terrain, river, trees, idle-animated explorer, crystal and portal scene remains as a fallback. The Play control has a separate hover/press animation and keyboard activation. The 30 FPS animation timer runs only while the control is visible and pauses when the launcher is minimized. The profile/version chip updates when the active offline profile or Minecraft version changes.
- The Home dashboard also includes clickable build tiles and a real popular-Modrinth feed with project artwork, authors, descriptions, download counts and install actions.
- Pressing Enter in the Home search opens the Modrinth mod catalogue with the typed query.
- Every Modrinth and CurseForge catalog card has an icon tile. Modrinth prefers the official project icon, then its raw icon, featured image and gallery. Only HTTPS images from the approved Modrinth CDN hosts are accepted; downloads are size-limited, decoded images are capped at 2048×2048 and 4 megapixels, and small images are cached locally (up to 256 files of 256 KiB each). Missing artwork receives a shaded, project-specific voxel emblem instead of a plain monogram or empty square. CurseForge cards use official artwork when available and the same voxel fallback otherwise.
- Mod downloads use the official Modrinth CDN, SHA-512 and JAR signature checks.
- Resource-pack, shader and datapack catalog cards support verified ZIP installation. The datapack world picker is restricted to the active build's `saves` directory.
- Downloads page shows source, file, target build, progress and task state (downloading, validation, installed, already installed or failed).
- Modrinth transfers and CmlLib Minecraft/Forge installation report byte progress. CmlLib `ByteProgress` is used instead of the high-frequency file event stream; task cards update in place rather than rebuilding the whole list for every callback.
- Queue history is currently in memory only; pause/resume/cancel and persistence across restarts are not implemented.
- Byte-progress wiring is compile-tested in CI, but live download speed and every loader's full network sequence have not been verified on the user's PC.

### Display and settings

- The Home hero background is bundled inside the assembly and never fetched from a remote server. The header uses a rotating software-rendered 3D cube mark. Primary action buttons animate their depth on hover/press and retain keyboard activation; sidebar page buttons animate their selected indicator. Navigation between pages uses a short depth/slide transition; shared cards gain a restrained hover-depth and border-glow state.

- Launch resolution defaults to the current primary Windows monitor bounds when the game launch options are created.
- Manual width/height mode remains available in Settings.
- RAM limit, fullscreen, close-after-launch, auto-repair toggle and extra JVM arguments are configurable.
- The resolution is detected on the user's Windows machine. No specific monitor resolution is hardcoded as the user's value.

### Skins and music

- Local PNG skin selection, dimension validation and preview.
- Profile and skin cards reflow their list, details and action controls as the launcher window width changes; other older fixed-coordinate layouts still need a full DPI audit.
- The skin preview is a software-rendered 3D Minecraft avatar with slow auto-rotation and mouse-drag rotation. It supports Classic/Slim arm width and both modern 64×64 and legacy 64×32 skin atlases.
- Windows CI includes DrawToBitmap smoke tests for the 3D preview with both atlas formats and both arm models; this is a rendering smoke test, not a screenshot-based proof of visual parity with the concept artwork.
- Skin source files are keyed by offline profile ID under `%LOCALAPPDATA%\\NovaCraft\\skins\\<profileId>.png`.
- Optional CustomSkinLoader / Skin Bridge support. A local PNG is not automatically uploaded to a skin website; other clients see it only when their skin mechanism/server supports it.
- Local MP3/WAV player using the Windows Media Control Interface (MCI): multi-file playlist, add/remove, play/pause/resume/stop, previous/next, automatic track advance and volume where the Windows audio device exposes MCI volume control. Playlist paths are saved in settings; audio files are never copied or uploaded.
- Custom OGG background-music resource-pack generation. The generated pack currently uses pack format 34 (Minecraft 1.21.1); compatibility with other versions is not guaranteed. The user must enable the pack in Minecraft. This replaces in-game background music; it is separate from the launcher player.

### Diagnostics and performance

- Basic diagnostics for write access, Java presence, RAM settings and selected Minecraft/loader files.
- The performance page reads physical/available RAM from Windows and calculates a conservative Java heap recommendation, leaving headroom for Windows. It changes only the Minecraft Java heap setting.
- NovaBoost 0.1.1 is an original client-only Fabric mod for Minecraft 1.21.11. The launcher downloads its own JAR from the project's preview release, verifies the published SHA-256 sidecar and Fabric manifest, stages the file, and installs it only into the active profile's mods directory. It does not silently replace another mod JAR.
- Sodium is not included in the automatic NovaBoost installer. Radeon HD 6900 predates Sodium's stated AMD HD 7000 / GCN support baseline, so automatic installation would be an unsafe default for that GPU family. This does not prove Sodium can never launch on any specific HD 6900 driver setup.
- NovaBoost does not modify remote servers and cannot improve a remote server's TPS. Its culling feature can reduce some client rendering work when many entities are hidden behind blocks; scenes without such entities may show no gain or even a small CPU cost. No fixed FPS multiplier is promised.
- NovaCraft does not change Windows registry settings, disable security software, add itself to startup or run hidden background services.
- Driver repair is not implemented. Diagnostics must not silently download or install drivers.

## Not implemented or not verified

- Microsoft/Mojang online authentication and Ely.by account login/synchronization.
- Real-world testing of the live CurseForge catalog with an approved production API key; the repository CI uses mocked API responses.
- Exact Fabric and Quilt loader-version pinning from imported pack manifests.
- Automatic Java runtime installation.
- A full skin library, cloud upload and universal skin visibility on offline/third-party servers.
- Windows MCI playback has not yet been manually exercised on the user's particular Windows installation; available MP3 codecs and audio-device volume support can vary.
- GPU-accelerated 3D rendering is not used: the Home scene is software-rendered with GDI+ to keep it self-contained. Its exact frame rate depends on the Windows machine.
- Pixel-perfect parity with the eight-screen UI reference shown in the project conversation on 2026-11-27 has not been verified. CI includes rendering smoke tests but does not compare screenshots against those reference screens.
- A complete 100/125/150% DPI and small-window layout audit of every page; the current UI pass improves shared styling and selected responsive layouts but does not claim that every legacy fixed-coordinate screen is fully responsive.
- Automatic graphics-driver repair or installation.
- Pause/resume/cancel and persistent download queue.
- Runtime launch testing for every Minecraft version, mod loader and imported pack. A successful import means the files passed importer checks; it does not guarantee that the pack launches.

## Tests and CI

The GitHub Actions workflow runs:

1. .NET restore.
2. Build with warnings treated as errors.
3. xUnit tests for offline UUID/nickname rules, ZIP catalog validation, Modrinth/CurseForge manifests, build-profile migration/isolation, 3D skin-preview, Home hero, rotating brand cube, animated primary-button and sidebar-selection rendering, local music playlist validation and RAM recommendation rules.
4. Self-contained Windows x64 publish and EXE size check.
5. Portable ZIP and Inno Setup installer packaging.
6. Installer smoke test: install, verify EXE, start the application process, uninstall and verify removal.

The installer smoke test does not launch Minecraft. Real pack compatibility and GPU-specific performance require tests on the target Windows machine.

## Build

Open `NovaCraft.sln` with .NET 8 SDK on Windows, or run the repository's `.github/workflows/build.yml` workflow. The executable is self-contained and is not padded with junk to reach an arbitrary file size.

See [docs/QUALITY_CONTRACT_RU.md](docs/QUALITY_CONTRACT_RU.md) for the acceptance rules and current verified/unverified status.
