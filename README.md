# ViperX Launcher

A Windows Minecraft launcher built with C#, WPF, and .NET 8.

**Brand promise:** Your Minecraft. Your Rules. Your World.

## Requirements
- Windows 10/11
- .NET 8 SDK
- Visual Studio 2022 with the .NET desktop development workload, or the .NET CLI

## Build, test, and run
From the repository root in PowerShell:

```powershell
dotnet restore .\ViperX.sln
dotnet build .\ViperX.sln --configuration Release --no-restore
dotnet test .\ViperX.sln --configuration Release --no-build
dotnet run --project .\ViperX.App\ViperX.App.csproj
```

## Implementation status

### Foundation
- [x] Dark ViperX WPF shell and navigation.
- [x] Official Mojang version manifest with in-memory caching and refresh.
- [x] Centralized local data paths, created at startup.
- [x] Installation, profile, account-identity, and settings models with validation.
- [x] JSON persistence for launcher settings and installation metadata.
- [x] Windows CI workflow and focused unit tests.

### Install & Play
- [x] HTTP downloader with temporary files, expected-size checks, and optional SHA-1 verification.
- [x] Vanilla client and library download pipeline from official version metadata.
- [x] Windows native library extraction with archive path checks.
- [x] Asset index/object download with SHA-1 verification.
- [x] Java discovery and version probing.
- [x] Process launch argument construction from Mojang version metadata.
- [x] Connect version catalog, installation metadata, Java selection, and authenticated launch to the main UI.
- [ ] Exercise full install/launch across representative Minecraft versions in Windows CI and fix compatibility gaps.

### Accounts
- [x] Microsoft device-code sign-in flow implementation.
- [x] Xbox Live and XSTS exchange, Minecraft Services sign-in, entitlement and profile checks.
- [x] Session token kept separate from the persistable account identity model.
- [x] Integrate device-code sign-in into the Accounts window (requires the user to provide a registered public-client application ID).
- [ ] Add OS-protected refresh-token storage and account removal controls.

### Mods & Profiles
- [x] Installation and profile metadata foundations.
- [x] Profile CRUD UI and persistent profile metadata.
- [x] Inspect and open each installation’s local mods folder.
- [x] Fabric loader integration (needs real-game verification); Forge/NeoForge are not yet integrated.\n- [x] Local mod JAR install/copy, enable/disable by safe filename extension, remove, list, and open mods folder.

### Polish & Release
- [x] Atomic JSON persistence for settings and installation metadata.
- [x] Automated unit-test project and Windows GitHub Actions workflow.
- [ ] Run the Windows CI build/tests and resolve any remaining failures (this environment cannot run .NET locally).
- [x] Local diagnostics window for latest.log and crash reports, with bounded file reads.\n- [ ] Accessibility review, packaging, and release validation.

## Important limitations
This repository is still under active development. The installer downloads vanilla client, libraries, and asset objects from URLs in Mojang's version metadata; it is not yet a fully verified production launcher. Launching requires a valid authenticated Minecraft session. The Microsoft authentication service needs a registered public-client application ID and is not yet wired to the UI. Do not commit client IDs that belong to someone else, client secrets, access tokens, refresh tokens, or other credentials.

ViperX has basic local mod-file management and Fabric profile support; Forge/NeoForge, modpack importing, remote mod catalog integration, and real-world launch validation remain unfinished. The diagnostics window reads local logs only and does not upload them. A successful compile is not claimed until the latest Windows Actions build and tests report success.
