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
- [ ] Exercise full install/launch across representative Minecraft versions in Windows CI and fix compatibility gaps.

### Accounts
- [x] Microsoft device-code sign-in flow implementation.
- [x] Xbox Live and XSTS exchange, Minecraft Services sign-in, entitlement and profile checks.
- [x] Session token kept separate from the persistable account identity model.
- [ ] Register/configure a Microsoft public-client application ID and integrate account flow into the WPF UI.
- [ ] Add OS-protected refresh-token storage and account removal controls.

### Mods & Profiles
- [x] Installation and profile metadata foundations.
- [ ] Profile UI and isolated per-profile configuration.
- [ ] Fabric/Forge/NeoForge installer integrations and mod management.

### Polish & Release
- [x] Atomic JSON persistence for settings and installation metadata.
- [x] Automated unit-test project and Windows GitHub Actions workflow.
- [ ] Run the Windows CI build/tests and resolve any remaining failures.
- [ ] Diagnostics, crash log viewer, accessibility review, packaging, and release validation.

## Important limitations
This repository is still under active development. The installer downloads vanilla client, libraries, and asset objects from URLs in Mojang's version metadata; it is not yet a fully verified production launcher. Launching requires a valid authenticated Minecraft session. The Microsoft authentication service needs a registered public-client application ID and is not yet wired to the UI. Do not commit client IDs that belong to someone else, client secrets, access tokens, refresh tokens, or other credentials.

Mods and mod loaders are not implemented yet. A successful compile is not claimed until the Windows Actions build and tests report success.
