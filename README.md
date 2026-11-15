# NovaCraft 1.14

Windows Minecraft Java launcher.

Features in the current build:
- Windows 10/11, C#/.NET 8, self-contained win-x64 EXE.
- Minecraft folder can be placed on any drive.
- Official Mojang release manifest through CmlLib.Core.
- Vanilla version installation and launch.
- RAM control; default 3 GB for the game.
- FPS Boost JVM profile using G1GC tuning.
- Modrinth search for mod projects; downloads require explicit confirmation, HTTPS from the official Modrinth CDN, a valid JAR/ZIP signature, and a matching SHA-512 from Modrinth metadata. Files are written only after verification.
- The Fabric/Forge/NeoForge selector currently filters compatible mod versions; it does not yet install the mod loader itself.
- Microsoft/Mojang sign-in is excluded. Ely.by account authentication is the target; offline nickname mode remains a separate fallback.
- Dark Russian UI, no ads, no background service, no automatic startup.
- Local settings under %LOCALAPPDATA%\NovaCraft.
- GitHub Actions build produces a portable EXE ZIP.

Microsoft/Mojang OAuth is intentionally excluded from NovaCraft 1.14. Ely.by authentication must use Ely.by's own authentication/session endpoints and authlib integration; a local offline nickname alone is not equivalent to an Ely.by account. The launcher does not include license-check bypass or game acquisition features.

The EXE is not padded with junk just to hit an arbitrary 100/300 MB number. A large file should contain useful runtime/dependencies. A bundled Java runtime can legitimately make a distribution much larger and will be added only as a real, verified component.


## Download security notes

- Minecraft version metadata and game files are obtained through CmlLib.Core using Mojang's published version metadata. The launcher does not add unofficial Minecraft mirrors.
- Mod downloads are opt-in. The user sees the project, filename, Minecraft version, loader filter, and source before downloading.
- A mod file is accepted only from HTTPS host `cdn.modrinth.com`, must have a JAR/ZIP signature, and must match the SHA-512 value returned by Modrinth's API over HTTPS. A mismatch aborts installation.
- SHA-512 comparison protects against transfer corruption or a CDN file that differs from the API metadata. It does **not** prove that a mod is benign, and it cannot protect against a compromised metadata source or malicious publisher.
- No antivirus scan is claimed. Users should only install mods from authors they trust and can decline every download.
- Modrinth's API and CDN are external dependencies. No third-party mod is bundled into the launcher.
- This security work does not yet constitute a full independent audit of CmlLib.Core, its transitive NuGet dependencies, Java, or Minecraft's complete download chain. Those dependencies still need a pinned-version/SBOM and provenance review.
