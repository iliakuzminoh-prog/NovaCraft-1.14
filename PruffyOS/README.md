# Pruﬀy OS

A temporary Windows desktop interface. It is an ordinary application, not a replacement Windows shell.

## Safety model

- No administrator elevation.
- No registry edits.
- No startup registration.
- No shell replacement.
- No background service.
- No telemetry.
- No bundled third-party runtime.
- No downloads or remote code execution.
- Closing the EXE returns to the normal Windows desktop.

## Build

Requires .NET 8 SDK on Windows.

```powershell
dotnet publish PruffyOS.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
```
