# ViperX Launcher

A Windows Minecraft launcher built with C#, WPF, and .NET 8.

**Brand promise:** Your Minecraft. Your Rules. Your World.

## Requirements

- Windows 10 or 11 (x64)
- A valid Minecraft: Java Edition account for authenticated online play
- A registered Microsoft Entra/Azure public-client application ID for Microsoft sign-in
- .NET 8 SDK to build from source; the CI artifact is self-contained

## Build, test, and run

From the repository root in PowerShell:

```powershell
dotnet restore .\ViperX.sln
dotnet build .\ViperX.sln --configuration Release --no-restore
dotnet test .\ViperX.sln --configuration Release --no-build
dotnet run --project .\ViperX.App\ViperX.App.csproj
```

## Current implementation

### Launcher foundation

- Dark WPF interface and navigation for versions, accounts, installations, mods/profiles, and settings.
- Mojang version manifest discovery and version metadata retrieval.
- Vanilla client, library, native, and asset download pipeline with size/hash verification where metadata provides it.
- Java discovery and version probing, launch argument assembly, and process startup.
- Persistent launcher settings, profiles, and installation metadata.
- Local diagnostics viewer for Minecraft logs and crash reports.

### Accounts

- Microsoft device-code sign-in flow, Xbox Live/XSTS exchange, Minecraft Services authentication, entitlement checks, and profile lookup.
- The user must configure a public-client application ID. No third-party client ID or secret is bundled.
- Refresh-token protection and complete account lifecycle polish still need additional work.

### Mods and modpacks

- Local mod JAR management: add, list, enable/disable, remove, and open the mods folder.
- Fabric loader installation and launch-profile integration (compiled and CI-tested, but not yet verified by a full live-game test matrix).
- Modrinth mod search and compatible-file download with size/SHA-1 verification.
- Modrinth modpack discovery and download, plus local `.mrpack` import.
- Importer protects against unsafe archive paths, checks remote file hashes/sizes when provided, and extracts overrides into a separate installation directory.
- Vanilla and Fabric modpacks are supported by the importer. Forge, NeoForge, and Quilt modpack installation/launch are not implemented and are explicitly rejected rather than falsely marked installed.
- Automatic mod dependency resolution is not implemented.

## CI build

GitHub Actions restores, builds, tests, publishes a self-contained Windows x64 launcher, and uploads it as a workflow artifact. The latest checked run completed successfully:

- [Latest successful build and downloadable artifact](https://github.com/ViperXC132/ViperX/actions/runs/40229656105)
- [Source repository](https://github.com/ViperXC132/ViperX)

Open the workflow run and download the `ViperX-win-x64` artifact from its Artifacts section. Artifacts are retained for a limited period by GitHub Actions.

## Known limitations

ViperX is still under active development and should not yet be described as a fully production-validated launcher. The successful CI build verifies compilation, tests, and packaging—not successful launches across all Minecraft releases or every modpack. Real Windows game-launch testing is still required. Legacy versions, Forge/NeoForge/Quilt, automatic mod dependency resolution, protected refresh-token storage, accessibility review, and release packaging need further work.

Never commit access tokens, refresh tokens, client secrets, or other credentials. The diagnostics window reads local logs and does not upload them.
