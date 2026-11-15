# NovaCraft 1.14

Windows launcher for Minecraft Java Edition. The current implementation is a C# WinForms application targeting .NET 8 (Windows x64).

## Implemented in the current source

- Dark Russian-language interface with Home, Builds, Mods, Resource Packs, Shaders, Datapacks, Modpacks, Music, Skin, Settings and Diagnostics pages.
- Self-contained Windows x64 executable; the installer allows choosing the installation folder, creates a Start Menu shortcut, optionally creates a desktop shortcut, and supports uninstall.
- Minecraft game directory can be selected on any drive.
- Minecraft release list and game installation through CmlLib.Core.
- Offline nickname session. Microsoft/Mojang account sign-in is not implemented.
- Loader installation code paths for Vanilla, Fabric, Forge and Quilt. Successful compilation does not prove runtime compatibility for every Minecraft version/loader combination; these combinations still require testing.
- Modrinth popular-mod feed and search.
- Mod download is limited to the official Modrinth CDN over HTTPS, checks the JAR/ZIP signature and compares SHA-512 against Modrinth metadata. A matching hash does not prove a mod is safe.
- Resource-pack and shader cards support one-click ZIP installation into the selected game directory after HTTPS CDN and SHA-512 verification. Datapack and modpack cards still open their Modrinth project pages; datapack installation needs a selected world, and modpack import is not implemented.
- Saved build profiles store a name, Minecraft version and loader.
- Custom OGG background-music resource-pack generation. The generated pack uses a fixed pack format value of 34 (Minecraft 1.21.1), so compatibility with other game versions is not guaranteed. The user must enable the pack in Minecraft's Resource Packs menu.
- Local PNG skin selection, validation and preview. The selected PNG is stored under `%LOCALAPPDATA%\NovaCraft\skins\active.png`. This local file is not injected into Minecraft and is not automatically uploaded to a server.
- Optional SkinsRestorer command helper. Other players see the skin only on servers that support the relevant plugin/command and permit the player to use it.
- Settings for game directory, RAM limit, resolution, fullscreen, close-after-launch, auto-repair toggle and extra JVM arguments.
- Basic diagnostics for write access, RAM settings and reinstalling selected Minecraft/loader files. It does not repair every Java, mod or runtime crash.

## Not implemented / not verified

- Microsoft/Mojang authentication and online account sessions.
- Universal skin visibility on third-party or offline-mode servers.
- Automatic upload of a local skin PNG to a skin host.
- One-click installation of datapacks and modpacks from their catalog cards.
- Automatic enabling of the generated music resource pack.
- Runtime testing of Vanilla, Fabric, Forge and Quilt across supported Minecraft versions.
- Full game-process crash analysis or guaranteed automatic repair.
- Independent security audit of CmlLib.Core, NuGet dependencies, Java, Minecraft downloads or third-party mods.

## Build

GitHub Actions workflow `.github/workflows/build.yml` restores and publishes the .NET project as a self-contained single-file Windows x64 executable, creates a portable ZIP and builds the Inno Setup installer. A successful workflow confirms compilation and packaging only; it is not a substitute for running the launcher and Minecraft on a Windows test machine.

The executable is not padded with junk to reach an arbitrary file size.