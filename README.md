# NovaCraft 1.14

Windows Minecraft Java launcher.

Features in the current build:
- Windows 10/11, C#/.NET 8, self-contained win-x64 EXE.
- Minecraft folder can be placed on any drive.
- Official Mojang release manifest through CmlLib.Core.
- Vanilla version installation and launch.
- RAM control; default 3 GB for the game.
- FPS Boost JVM profile using G1GC tuning.
- Modrinth popular-project feed on the home page, sorted by downloads, with project cards, descriptions, author names, download counts, and icons loaded from Modrinth project metadata.
- Modrinth mod search with responsive project cards and bounded parallel icon loading; missing icons remain neutral placeholders rather than invented artwork.
- Mod downloads require explicit confirmation, HTTPS from the official Modrinth CDN, a valid JAR/ZIP signature, and a matching SHA-512 from Modrinth metadata. Files are written only after verification.
- The launcher contains install paths for Vanilla, Fabric, Forge, and Quilt. Runtime verification of each loader is still required.
- Microsoft/Mojang sign-in is excluded. Ely.by account authentication is the target; offline nickname mode remains a separate fallback.
- Custom left navigation for Home, saved build profiles, mods, resource packs, shaders, datapacks, modpacks, news, account, settings, and diagnostics.
- Original NovaCraft hero artwork embedded in the executable; no third-party watermark.
- Flexible settings for RAM, Java executable path, game resolution, fullscreen, custom JVM arguments, launcher close behavior, and automatic repair.
- Diagnostics for writable game folder, invalid Java path, RAM allocation, and repair/reinstall of selected version files. Common launch errors are translated into readable messages; this is not a guarantee against every possible Minecraft or mod error.
- Dark Russian UI, no ads, no background service, no automatic startup.
- Local settings under %LOCALAPPDATA%\NovaCraft.
- GitHub Actions build produces a portable EXE ZIP.

Microsoft/Mojang OAuth is intentionally excluded from NovaCraft 1.14. Ely.by authentication must use Ely.by's own authentication/session endpoints and authlib integration; a local offline nickname alone is not equivalent to an Ely.by account. The launcher does not include license-check bypass or game acquisition features.

The UI is implemented independently in C# WinForms; it does not copy SKlauncher source code. The Modrinth catalog uses project metadata and icons from Modrinth's API. Non-mod catalog cards currently open their Modrinth project pages; one-click installation for resource packs, shaders, and datapacks is not yet implemented.

The EXE is not padded with junk just to hit an arbitrary 100/300 MB number. A large file should contain useful runtime/dependencies. A bundled Java runtime can legitimately make a distribution much larger and will be added only as a real, verified component.


## Download security notes

- Minecraft version metadata and game files are obtained through CmlLib.Core using Mojang's published version metadata. The launcher does not add unofficial Minecraft mirrors.
- Mod downloads are opt-in. The user sees the project, filename, Minecraft version, loader filter, and source before downloading.
- A mod file is accepted only from HTTPS host `cdn.modrinth.com`, must have a JAR/ZIP signature, and must match the SHA-512 value returned by Modrinth's API over HTTPS. A mismatch aborts installation.
- SHA-512 comparison protects against transfer corruption or a CDN file that differs from the API metadata. It does **not** prove that a mod is benign, and it cannot protect against a compromised metadata source or malicious publisher.
- No antivirus scan is claimed. Users should only install mods from authors they trust and can decline every download.
- Modrinth's API and CDN are external dependencies. No third-party mod is bundled into the launcher.
- This security work does not yet constitute a full independent audit of CmlLib.Core, its transitive NuGet dependencies, Java, or Minecraft's complete download chain. Those dependencies still need a pinned-version/SBOM and provenance review.
