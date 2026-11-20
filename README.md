# NovaCraft 1.14

Windows launcher for Minecraft Java Edition. The current implementation is a C# WinForms application targeting .NET 8 (Windows x64). Stage B is being implemented incrementally; its instance layout is covered by regression tests before UI/runtime wiring is enabled.

## Implemented in the current source

### Stage A (2026-11-19)
- Canonical Minecraft offline UUID calculation now matches Java UUID.nameUUIDFromBytes: MD5 with version 3 and RFC 4122 variant bits, without Guid byte-order conversion.
- A single nickname validator enforces 3–16 ASCII characters from A-Z, a-z, 0-9 and underscore for profile creation, renaming and launch validation.
- Nicknames are no longer silently copied from the home field into the active profile; renaming is an explicit profile action.
- Local skin source files are keyed by profile ID under %LOCALAPPDATA%\\NovaCraft\\skins\\<profileId>.png and synchronized into the active game directory before launch.
- Modrinth JAR/ZIP downloads and Skin Bridge downloads use the centralized verified download service with HTTPS/CDN host validation, filename/path checks, streaming SHA-512 verification, archive signatures, size limits, cancellation and atomic finalization.
- settings.json carries SchemaVersion and legacy skin paths are migrated to the profile-ID skin location when possible.
- Stage A xUnit tests cover the UUID control value, UUID version/variant bits, determinism and nickname validation.



- Dark Russian-language interface with Home, Builds, Mods, Resource Packs, Shaders, Datapacks, Modpacks, Music, Skin, Settings and Diagnostics pages.
- Self-contained Windows x64 executable; the installer allows choosing the installation folder, creates a Start Menu shortcut, optionally creates a desktop shortcut, and supports uninstall.
- Minecraft game directory can be selected on any drive.
- Minecraft release list and game installation through CmlLib.Core.
- Offline nickname session. Microsoft/Mojang account sign-in is not implemented.
- Loader installation code paths for Vanilla, Fabric, Forge and Quilt. Successful compilation does not prove runtime compatibility for every Minecraft version/loader combination; these combinations still require testing.
- Modrinth popular-mod feed and search.
- Mod download is limited to the official Modrinth CDN over HTTPS, checks the JAR/ZIP signature and compares SHA-512 against Modrinth metadata. A matching hash does not prove a mod is safe.
- Resource-pack and shader cards support one-click ZIP installation into the game directory. Datapack cards install ZIPs into a user-selected world folder after checking for level.dat. All three use HTTPS from the official Modrinth CDN and SHA-512 verification. Modpack cards still open their Modrinth project pages; modpack import is not implemented.
- Saved build profiles store a name, Minecraft version and loader.
- Custom OGG background-music resource-pack generation. The generated pack uses a fixed pack format value of 34 (Minecraft 1.21.1), so compatibility with other game versions is not guaranteed. The user must enable the pack in Minecraft's Resource Packs menu.
- Local PNG skin selection, validation and preview. The selected PNG is stored under `%LOCALAPPDATA%\NovaCraft\skins\active.png`. This local file is not injected into Minecraft and is not automatically uploaded to a server.
- Optional SkinsRestorer command helper. Other players see the skin only on servers that support the relevant plugin/command and permit the player to use it.
- Settings for game directory, RAM limit, resolution, fullscreen, close-after-launch, auto-repair toggle and extra JVM arguments.
- Basic diagnostics for write access, RAM settings and reinstalling selected Minecraft/loader files. It does not repair every Java, mod or runtime crash.

## Not implemented / not verified

- Stage B instance layout and shared CmlLib storage are implemented in `InstanceStore` and covered by regression tests; UI/runtime wiring to make every launch use the resolved instance is still not complete.
- Stage C interface restructuring into five navigation areas and DPI/layout verification has not been implemented yet.
- Stage D dependency/update manifest management is only partially present; the centralized download path is implemented, but the full dependency graph and /version_files/update workflow are not complete.
- Stage E .mrpack import/export is not implemented.
- Stage F Java installation, worlds/screenshots management and full repair polish are not implemented yet.



- Microsoft/Mojang authentication and online account sessions.
- Universal skin visibility on third-party or offline-mode servers.
- Automatic upload of a local skin PNG to a skin host.
- One-click import/installation of modpacks from their catalog cards.
- Automatic enabling of the generated music resource pack.
- Runtime testing of Vanilla, Fabric, Forge and Quilt across supported Minecraft versions.
- Full game-process crash analysis or guaranteed automatic repair.
- Independent security audit of CmlLib.Core, NuGet dependencies, Java, Minecraft downloads or third-party mods.

## Build

GitHub Actions workflow `.github/workflows/build.yml` restores and publishes the .NET project as a self-contained single-file Windows x64 executable, creates a portable ZIP and builds the Inno Setup installer. A successful workflow confirms compilation and packaging only; it is not a substitute for running the launcher and Minecraft on a Windows test machine.

The executable is not padded with junk to reach an arbitrary file size.