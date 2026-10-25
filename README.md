# ViperX Launcher

A Windows Minecraft launcher built with C#, WPF, and .NET 8.

## Requirements

- Windows 10/11
- .NET 8 SDK
- Visual Studio 2022 with the **.NET desktop development** workload, or the .NET CLI

## Build and run

From the repository root:

```powershell
dotnet build .\ViperX.App\ViperX.App.csproj
dotnet run --project .\ViperX.App\ViperX.App.csproj
```

## Current status

The initial branded WPF shell is in place: dark ViperX styling, sidebar navigation, home dashboard, and wired navigation actions. The Minecraft download/installation pipeline, Microsoft authentication, mod loaders, mod management, and persistent settings are not implemented yet.

## Roadmap

1. Minecraft version manifest, downloads, integrity checks, and installation model.
2. Java detection/configuration and safe launch argument generation.
3. Microsoft account authentication and secure token storage.
4. Version profiles, Fabric/Forge/NeoForge integration, and mod management.
5. Persistent settings, diagnostics, crash logs, and tests.
