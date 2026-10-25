using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using ViperX.App.Models;

namespace ViperX.App.Services;

public sealed record ModrinthProject(string ProjectId, string Title, string Description, string Slug, string ProjectType, string? IconUrl);
public sealed record ModrinthVersionFile(string Url, string Filename, long Size, string? Sha1);
public sealed record ModrinthVersion(string Id, string Name, string VersionNumber, string GameVersion, string[] Loaders, ModrinthVersionFile File);

/// <summary>Modrinth API client for discovering compatible mods and importing standard .mrpack archives.</summary>
public sealed class ModrinthContentService
{
    private static readonly HttpClient Http = CreateClient();
    private readonly VerifiedDownloadService _downloader = new();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(45) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("ViperX/1.0 (Minecraft launcher)");
        return client;
    }

    public async Task<IReadOnlyList<ModrinthProject>> SearchModsAsync(string query, string gameVersion, string loader, CancellationToken cancellationToken = default)
    {
        var facets = $"[[\"project_type:mod\"],[\"versions:{EscapeFacet(gameVersion)}\"],[\"categories:{EscapeFacet(loader.ToLowerInvariant())}\"]]";
        var uri = new Uri("https://api.modrinth.com/v2/search?query=" + Uri.EscapeDataString(query.Trim()) +
            "&limit=30&facets=" + Uri.EscapeDataString(facets));
        using var response = await Http.GetAsync(uri, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        return doc.RootElement.GetProperty("hits").EnumerateArray().Select(hit => new ModrinthProject(
            hit.GetProperty("project_id").GetString()!,
            hit.GetProperty("title").GetString() ?? hit.GetProperty("slug").GetString()!,
            hit.GetProperty("description").GetString() ?? string.Empty,
            hit.GetProperty("slug").GetString()!,
            hit.GetProperty("project_type").GetString() ?? "mod",
            hit.TryGetProperty("icon_url", out var icon) && icon.ValueKind == JsonValueKind.String ? icon.GetString() : null)).ToArray();
    }

    public async Task<ModrinthVersion> GetCompatibleVersionAsync(string projectId, string gameVersion, string loader, CancellationToken cancellationToken = default)
    {
        var uri = new Uri($"https://api.modrinth.com/v2/project/{Uri.EscapeDataString(projectId)}/version?game_versions=" +
            Uri.EscapeDataString(JsonSerializer.Serialize(new[] { gameVersion })) + "&loaders=" +
            Uri.EscapeDataString(JsonSerializer.Serialize(new[] { loader.ToLowerInvariant() })));
        using var response = await Http.GetAsync(uri, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        foreach (var version in doc.RootElement.EnumerateArray())
        {
            var file = version.GetProperty("files").EnumerateArray()
                .FirstOrDefault(item => item.TryGetProperty("primary", out var primary) && primary.GetBoolean());
            if (file.ValueKind == JsonValueKind.Undefined)
                file = version.GetProperty("files").EnumerateArray().FirstOrDefault();
            if (file.ValueKind == JsonValueKind.Undefined) continue;
            var hashes = file.GetProperty("hashes");
            return new ModrinthVersion(
                version.GetProperty("id").GetString()!,
                version.GetProperty("name").GetString() ?? version.GetProperty("version_number").GetString()!,
                version.GetProperty("version_number").GetString()!,
                gameVersion,
                version.GetProperty("loaders").EnumerateArray().Select(x => x.GetString() ?? "").ToArray(),
                new ModrinthVersionFile(file.GetProperty("url").GetString()!, file.GetProperty("filename").GetString()!,
                    file.TryGetProperty("size", out var size) ? size.GetInt64() : 0,
                    hashes.TryGetProperty("sha1", out var sha) ? sha.GetString() : null));
        }
        throw new InvalidOperationException($"No {loader} build compatible with Minecraft {gameVersion} was found for this project.");
    }

    public async Task InstallModAsync(ModrinthProject project, MinecraftInstallation installation, CancellationToken cancellationToken = default)
    {
        var loader = installation.Loader.ToLowerInvariant();
        if (loader == "vanilla")
        {
            var loaderVersion = await new FabricLoaderService().InstallLatestStableAsync(installation.GameVersion, cancellationToken);
            installation = installation with { Loader = "Fabric", LoaderVersion = loaderVersion };
            var store = new InstallationStore();
            var installations = (await store.LoadAsync(cancellationToken)).Select(item => item.Id == installation.Id ? installation : item).ToArray();
            await store.SaveAsync(installations, cancellationToken);
            loader = "fabric";
        }
        if (loader is not ("fabric" or "forge" or "neoforge" or "quilt"))
            throw new InvalidOperationException($"Unsupported mod loader '{installation.Loader}'.");
        var version = await GetCompatibleVersionAsync(project.ProjectId, installation.GameVersion, loader, cancellationToken);
        var modsDirectory = Path.Combine(installation.GameDirectory, "mods");
        Directory.CreateDirectory(modsDirectory);
        var fileName = SafeFileName(version.File.Filename);
        if (!fileName.EndsWith(".jar", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Modrinth's selected primary file is not a JAR.");
        var destination = Path.Combine(modsDirectory, fileName);
        await _downloader.DownloadAsync(new Uri(version.File.Url), destination, version.File.Sha1, version.File.Size, cancellationToken: cancellationToken);
    }

    public async Task<IReadOnlyList<ModrinthProject>> SearchModpacksAsync(string query, CancellationToken cancellationToken = default)
    {
        var uri = new Uri("https://api.modrinth.com/v2/search?query=" + Uri.EscapeDataString(query.Trim()) +
            "&limit=30&facets=" + Uri.EscapeDataString("[[\"project_type:modpack\"]]"));
        using var response = await Http.GetAsync(uri, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        return doc.RootElement.GetProperty("hits").EnumerateArray().Select(hit => new ModrinthProject(
            hit.GetProperty("project_id").GetString()!,
            hit.GetProperty("title").GetString() ?? hit.GetProperty("slug").GetString()!,
            hit.GetProperty("description").GetString() ?? string.Empty,
            hit.GetProperty("slug").GetString()!,
            hit.GetProperty("project_type").GetString() ?? "modpack",
            hit.TryGetProperty("icon_url", out var icon) && icon.ValueKind == JsonValueKind.String ? icon.GetString() : null)).ToArray();
    }

    public async Task<MinecraftInstallation> DownloadAndImportModpackAsync(ModrinthProject project, CancellationToken cancellationToken = default)
    {
        using var response = await Http.GetAsync($"https://api.modrinth.com/v2/project/{Uri.EscapeDataString(project.ProjectId)}/version", cancellationToken);
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        foreach (var version in doc.RootElement.EnumerateArray())
        {
            foreach (var file in version.GetProperty("files").EnumerateArray())
            {
                var filename = file.GetProperty("filename").GetString() ?? string.Empty;
                if (!filename.EndsWith(".mrpack", StringComparison.OrdinalIgnoreCase)) continue;
                var url = file.GetProperty("url").GetString()!;
                if (!Uri.TryCreate(url, UriKind.Absolute, out var downloadUri) || downloadUri.Scheme != Uri.UriSchemeHttps)
                    throw new InvalidDataException("Modrinth returned a non-HTTPS modpack URL.");
                var temp = Path.Combine(ViperXPaths.RootDirectory, "downloads", Guid.NewGuid().ToString("N") + ".mrpack");
                try
                {
                    await _downloader.DownloadAsync(downloadUri, temp,
                        file.TryGetProperty("hashes", out var hashes) && hashes.TryGetProperty("sha1", out var sha) ? sha.GetString() : null,
                        file.TryGetProperty("size", out var size) ? size.GetInt64() : null,
                        cancellationToken: cancellationToken);
                    return await ImportMrpackAsync(temp, cancellationToken);
                }
                finally { try { if (File.Exists(temp)) File.Delete(temp); } catch (IOException) { } }
            }
        }
        throw new InvalidDataException("This Modrinth project has no downloadable .mrpack release.");
    }

    public async Task<MinecraftInstallation> ImportMrpackAsync(string archivePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(archivePath)) throw new FileNotFoundException("Modpack archive not found.", archivePath);
        var tempRoot = Path.Combine(ViperXPaths.RootDirectory, "imports", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            using var archive = ZipFile.OpenRead(archivePath);
            var indexEntry = archive.GetEntry("modrinth.index.json")
                ?? throw new InvalidDataException("This is not a valid Modrinth .mrpack: modrinth.index.json is missing.");
            if (indexEntry.Length > 4 * 1024 * 1024) throw new InvalidDataException("Modpack index is too large.");
            using var indexStream = indexEntry.Open();
            using var index = await JsonDocument.ParseAsync(indexStream, cancellationToken: cancellationToken);
            var root = index.RootElement;
            if (root.GetProperty("formatVersion").GetInt32() != 1) throw new InvalidDataException("Unsupported Modrinth modpack format version.");
            var name = root.GetProperty("name").GetString() ?? Path.GetFileNameWithoutExtension(archivePath);
            var dependencies = root.GetProperty("dependencies");
            var gameVersion = dependencies.GetProperty("minecraft").GetString()!;
            var loader = dependencies.EnumerateObject().FirstOrDefault(p => p.Name is "fabric-loader" or "forge" or "neoforge" or "quilt-loader");
            var loaderName = loader.Name switch { "fabric-loader" => "Fabric", "forge" => "Forge", "neoforge" => "NeoForge", "quilt-loader" => "Quilt", _ => "Vanilla" };
            if (loaderName is "Forge" or "NeoForge" or "Quilt")
                throw new NotSupportedException($"This modpack requires {loaderName}. The pack was not installed because ViperX cannot yet install/launch that loader reliably. Fabric and vanilla packs are supported by this importer.");
            if (loaderName == "Vanilla" && root.TryGetProperty("files", out var packFiles) && packFiles.EnumerateArray().Any())
            {
                // Vanilla modpacks can still contain resource/config overrides; mod JARs require a loader.
            }

            var safeName = string.Concat(name.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch)).Trim();
            if (string.IsNullOrWhiteSpace(safeName)) safeName = "Imported Modpack";
            var instanceRoot = Path.Combine(ViperXPaths.InstancesDirectory, safeName + "-" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(instanceRoot);

            var vanilla = new VanillaMinecraftInstaller();
            await vanilla.InstallAsync(gameVersion, cancellationToken: cancellationToken);
            var sharedRoot = Path.Combine(ViperXPaths.InstancesDirectory, gameVersion);
            CopyDirectory(Path.Combine(sharedRoot, "versions", gameVersion), Path.Combine(instanceRoot, "versions", gameVersion));
            var natives = Path.Combine(sharedRoot, "natives");
            if (Directory.Exists(natives)) CopyDirectory(natives, Path.Combine(instanceRoot, "natives"));
            var installedMarker = Path.Combine(instanceRoot, "versions", gameVersion, "installed.json");
            if (!File.Exists(installedMarker)) throw new InvalidDataException("Vanilla base installation did not complete.");

            if (loaderName == "Fabric")
            {
                await new FabricLoaderService().InstallLatestStableAsync(gameVersion, cancellationToken);
                var sharedProfile = FabricLoaderService.GetProfilePath(gameVersion,
                    (await new InstallationStore().LoadAsync(cancellationToken)).FirstOrDefault(x => x.GameVersion == gameVersion && string.Equals(Path.GetFullPath(x.GameDirectory), Path.GetFullPath(sharedRoot), StringComparison.OrdinalIgnoreCase))?.LoaderVersion
                    ?? throw new InvalidOperationException("Fabric loader installation did not return a loader version."));
                var profileDirectory = Path.Combine(instanceRoot, "loader-profiles");
                Directory.CreateDirectory(profileDirectory);
                File.Copy(sharedProfile, Path.Combine(profileDirectory, Path.GetFileName(sharedProfile)), true);
            }

            var files = root.GetProperty("files").EnumerateArray().ToArray();
            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (file.TryGetProperty("env", out var env) && env.TryGetProperty("client", out var clientEnv) &&
                    string.Equals(clientEnv.GetString(), "unsupported", StringComparison.OrdinalIgnoreCase)) continue;
                var path = SafeArchivePath(file.GetProperty("path").GetString()!);
                if (!file.TryGetProperty("downloads", out var downloads) || downloads.GetArrayLength() == 0) continue;
                var url = downloads[0].GetString()!;
                if (!Uri.TryCreate(url, UriKind.Absolute, out var downloadUri) || downloadUri.Scheme != Uri.UriSchemeHttps)
                    throw new InvalidDataException("Modpack file contains a non-HTTPS download URL.");
                var hashes = file.GetProperty("hashes");
                var sha1 = hashes.TryGetProperty("sha1", out var hash) ? hash.GetString() : null;
                var size = file.TryGetProperty("fileSize", out var fileSize) ? fileSize.GetInt64() : (long?)null;
                var destination = Path.GetFullPath(Path.Combine(instanceRoot, path.Replace('/', Path.DirectorySeparatorChar)));
                EnsureUnderRoot(instanceRoot, destination);
                await _downloader.DownloadAsync(downloadUri, destination, sha1, size, cancellationToken: cancellationToken);
            }

            foreach (var entry in archive.Entries.Where(e => e.FullName.StartsWith("overrides/", StringComparison.Ordinal)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var rawRelative = entry.FullName["overrides/".Length..].TrimEnd('/');
                if (rawRelative.Length == 0) continue;
                var relative = SafeArchivePath(rawRelative);
                var destination = Path.GetFullPath(Path.Combine(instanceRoot, relative.Replace('/', Path.DirectorySeparatorChar)));
                EnsureUnderRoot(instanceRoot, destination);
                if (string.IsNullOrEmpty(entry.Name)) { Directory.CreateDirectory(destination); continue; }
                if (entry.Length > 256L * 1024 * 1024) throw new InvalidDataException("Modpack override file is too large.");
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                entry.ExtractToFile(destination, overwrite: true);
            }

            var installation = new MinecraftInstallation
            {
                Name = safeName,
                GameVersion = gameVersion,
                GameDirectory = instanceRoot,
                Loader = loaderName,
                LoaderVersion = loaderName == "Fabric"
                    ? (await new InstallationStore().LoadAsync(cancellationToken)).FirstOrDefault(x => x.GameVersion == gameVersion && string.Equals(Path.GetFullPath(x.GameDirectory), Path.GetFullPath(sharedRoot), StringComparison.OrdinalIgnoreCase))?.LoaderVersion
                    : null,
                MemoryMegabytes = 4096
            };
            var store = new InstallationStore();
            var installations = (await store.LoadAsync(cancellationToken)).ToList();
            installations.Add(installation);
            await store.SaveAsync(installations, cancellationToken);
            return installation;
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    private static string EscapeFacet(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    private static string SafeFileName(string name)
    {
        var file = Path.GetFileName(name);
        if (string.IsNullOrWhiteSpace(file) || file.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) throw new InvalidDataException("Unsafe download filename.");
        return file;
    }
    private static string SafeArchivePath(string path)
    {
        path = path.Replace('\\', '/');
        if (string.IsNullOrWhiteSpace(path) || path.StartsWith('/') || path.Split('/').Any(part => part is "" or "." or "..") || path.Contains(':'))
            throw new InvalidDataException($"Unsafe modpack path: {path}");
        return path;
    }
    private static void EnsureUnderRoot(string root, string path)
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!Path.GetFullPath(path).StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Modpack path escapes its installation directory.");
    }
    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source)) File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), true);
        foreach (var dir in Directory.EnumerateDirectories(source)) CopyDirectory(dir, Path.Combine(destination, Path.GetFileName(dir)));
    }
}
