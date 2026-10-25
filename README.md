# ViperX Launcher

A Windows Minecraft launcher built with C#, WPF, and .NET 8.

**Brand promise:** Your Minecraft. Your Rules. Your World.

## Requirements
- Windows 10/11
- .NET 8 SDK
- Visual Studio 2022 with the .NET desktop development workload, or the .NET CLI

## Build and run
From the repository root:

```powershell
dotnet build .\ViperX.sln
dotnet run --project .\ViperX.App\ViperX.App.csproj
```

## Five-phase roadmap
1. **Foundation:** branded WPF shell, official version catalog, launcher data paths, installation models, CI build.
2. **Install & Play:** game downloads, integrity checks, Java discovery, launch arguments, process launch.
3. **Accounts:** supported Microsoft sign-in, account management, secure token handling.
4. **Mods & Profiles:** isolated profiles, Fabric/Forge/NeoForge, mods and modpacks.
5. **Polish & Release:** persistent settings, memory and resolution controls, diagnostics, crash logs, tests, release packaging.

## Current status — Phase 1
- [x] Dark ViperX WPF shell and navigation.
- [x] Minecraft version metadata model and official Mojang manifest client.
- [x] Manifest cache, cancellation support, and basic validation.
- [x] Centralized local data paths.
- [x] Version catalog UI with search, refresh, version type, release date, and manifest URL.
- [x] Windows CI workflow to restore and build the .NET 8 solution.
- [ ] Run the CI build and fix any reported errors.
- [ ] Add installation/profile models and tests.

The version catalog reads Mojang's public metadata only. It does not yet install game files, authenticate players, or launch Minecraft. Those features are planned for later phases.
