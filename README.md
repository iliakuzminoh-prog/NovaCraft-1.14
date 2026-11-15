# NovaCraft 1.14

Windows Minecraft Java launcher.

Features in the current build:
- Windows 10/11, C#/.NET 8, self-contained win-x64 EXE.
- Minecraft folder can be placed on any drive.
- Official Mojang release manifest through CmlLib.Core.
- Vanilla version installation and launch.
- RAM control; default 3 GB for the game.
- FPS Boost JVM profile using G1GC tuning.
- Modrinth search and HTTPS download into the selected mods folder.
- Dark Russian UI, no ads, no background service, no automatic startup.
- Local settings under %LOCALAPPDATA%\NovaCraft.
- GitHub Actions build produces a portable EXE ZIP.

Microsoft/Xbox authentication is planned for the next stage. The current launch path uses CmlLib offline sessions and does not bypass Minecraft licensing.

The EXE is not padded with junk just to hit an arbitrary 100/300 MB number. A large file should contain useful runtime/dependencies. A bundled Java runtime can legitimately make a distribution much larger and will be added only as a real, verified component.
