using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace ViperX.App.Services;

/// <summary>
/// Installs vanilla Minecraft files referenced by Mojang's version metadata.
/// Authentication and game launching are intentionally separate responsibilities.
/// </summary>
public sealed class VanillaMinecraftInstaller
{
    private readonly MinecraftVersionManifestService _manifest = new();
    private readonly VerifiedDownloadService _downloader = new();
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    public async Task InstallAsync(string versionId, IProgress<(string Stage, double Progress)>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(versionId);
        var entry = await _manifest.FindVersionAsync(versionId, cancellationToken)
            ?? throw new InvalidOperationException($"Minecraft version '{versionId}' is not present in Mojang's official manifest.");

        var instanceRoot = Path.Combine(ViperXPaths.InstancesDirectory, SafeSegment(versionId));
        var versionDirectory = Path.Combine(instanceRoot, "versions", SafeSegment(versionId));
        Directory.CreateDirectory(versionDirectory);

        progress?.Report(("Reading official version metadata", 0));
        using var versionResponse = await Http.GetAsync(entry.Url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        versionResponse.EnsureSuccessStatusCode();
        await using var metadataStream = await versionResponse.Content.ReadAsStreamAsync(cancellationToken);
        using var metadata = await JsonDocument.ParseAsync(metadataStream, cancellationToken: cancellationToken);
        var root = metadata.RootElement;

        var client = root.GetProperty("downloads").GetProperty("client");
        var clientPath = Path.Combine(versionDirectory, versionId + ".jar");
        await _downloader.DownloadAsync(
            new Uri(client.GetProperty("url").GetString()!),
            clientPath,
            client.TryGetProperty("sha1", out var sha) ? sha.GetString() : null,
            client.TryGetProperty("size", out var size) ? size.GetInt64() : null,
            new Progress<double>(p => progress?.Report(("Downloading game client", p * 0.45))),
            cancellationToken);

        if (root.TryGetProperty("logging", out var logging) &&
            logging.TryGetProperty("client", out var loggingClient) &&
            loggingClient.TryGetProperty("file", out var loggingFile) &&
            loggingFile.TryGetProperty("url", out var loggingUrl))
        {
            var logConfigPath = Path.Combine(versionDirectory, loggingFile.TryGetProperty("id", out var logId)
                ? logId.GetString() ?? "logging.xml"
                : "logging.xml");
            await _downloader.DownloadAsync(new Uri(loggingUrl.GetString()!),
                logConfigPath,
                loggingFile.TryGetProperty("sha1", out var logSha) ? logSha.GetString() : null,
                loggingFile.TryGetProperty("size", out var logSize) ? logSize.GetInt64() : null,
                cancellationToken: cancellationToken);
        }

        if (root.TryGetProperty("libraries", out var libraries))
        {
            var libraryEntries = libraries.EnumerateArray().ToArray();
            for (var i = 0; i < libraryEntries.Length; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var library = libraryEntries[i];
                if (!IsAllowedOnWindows(library) || !library.TryGetProperty("downloads", out var downloads))
                    continue;

                if (downloads.TryGetProperty("artifact", out var artifact))
                    await DownloadArtifactAsync(artifact, cancellationToken);

                if (downloads.TryGetProperty("classifiers", out var classifiers) &&
                    library.TryGetProperty("natives", out var natives) &&
                    natives.TryGetProperty("windows", out var nativeName) &&
                    classifiers.TryGetProperty(nativeName.GetString() ?? string.Empty, out var nativeArtifact))
                {
                    await DownloadArtifactAsync(nativeArtifact, cancellationToken);
                    var nativePath = ArtifactPath(nativeArtifact);
                    var nativeDir = Path.Combine(instanceRoot, "natives");
                    Directory.CreateDirectory(nativeDir);
                    using var archive = ZipFile.OpenRead(nativePath);
                    foreach (var item in archive.Entries)
                    {
                        if (string.IsNullOrEmpty(item.Name) || item.FullName.StartsWith("META-INF/", StringComparison.OrdinalIgnoreCase))
                            continue;
                        var destination = Path.GetFullPath(Path.Combine(nativeDir, item.FullName));
                        if (!destination.StartsWith(Path.GetFullPath(nativeDir) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                            throw new InvalidDataException("Native archive contains an unsafe path.");
                        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                        item.ExtractToFile(destination, overwrite: true);
                    }
                }

                progress?.Report(($"Downloading libraries ({i + 1}/{libraryEntries.Length})", 0.45 + 0.25 * (i + 1) / Math.Max(1, libraryEntries.Length)));
            }
        }

        if (root.TryGetProperty("assetIndex", out var assetIndex))
        {
            var indexId = assetIndex.GetProperty("id").GetString()!;
            var indexUrl = assetIndex.GetProperty("url").GetString()!;
            var indexPath = Path.Combine(ViperXPaths.AssetsDirectory, "indexes", indexId + ".json");
            await _downloader.DownloadAsync(new Uri(indexUrl), indexPath,
                assetIndex.TryGetProperty("sha1", out var indexSha) ? indexSha.GetString() : null,
                assetIndex.TryGetProperty("size", out var indexSize) ? indexSize.GetInt64() : null,
                cancellationToken: cancellationToken);

            using var assetDoc = JsonDocument.Parse(await File.ReadAllTextAsync(indexPath, cancellationToken));
            var objects = assetDoc.RootElement.GetProperty("objects").EnumerateObject().ToArray();
            for (var i = 0; i < objects.Length; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var hash = objects[i].Value.GetProperty("hash").GetString()!;
                if (hash.Length != 40 || !hash.All(Uri.IsHexDigit))
                    throw new InvalidDataException("Asset index contains an invalid SHA-1 hash.");
                var objectPath = Path.Combine(ViperXPaths.AssetsDirectory, "objects", hash[..2], hash);
                if (!File.Exists(objectPath) || !await HasSha1Async(objectPath, hash, cancellationToken))
                {
                    var objectUrl = new Uri($"https://resources.download.minecraft.net/{hash[..2]}/{hash}");
                    await _downloader.DownloadAsync(objectUrl, objectPath, hash,
                        objects[i].Value.TryGetProperty("size", out var objectSize) ? objectSize.GetInt64() : null,
                        cancellationToken: cancellationToken);
                }
                if (i % 25 == 0)
                    progress?.Report(($"Downloading assets ({i + 1}/{objects.Length})", 0.70 + 0.29 * (i + 1) / Math.Max(1, objects.Length)));
            }
        }

        await File.WriteAllTextAsync(Path.Combine(versionDirectory, "installed.json"),
            JsonSerializer.Serialize(new { versionId, installedAtUtc = DateTimeOffset.UtcNow, source = "mojang" }, new JsonSerializerOptions { WriteIndented = true }),
            cancellationToken);

        var store = new InstallationStore();
        var existing = (await store.LoadAsync(cancellationToken)).ToList();
        existing.RemoveAll(item => string.Equals(item.GameVersion, versionId, StringComparison.OrdinalIgnoreCase));
        existing.Add(new ViperX.App.Models.MinecraftInstallation
        {
            Name = $"Minecraft {versionId}",
            GameVersion = versionId,
            GameDirectory = instanceRoot,
            MemoryMegabytes = 4096,
            Loader = "Vanilla"
        });
        await store.SaveAsync(existing, cancellationToken);
        progress?.Report(("Installation complete", 1));
    }

    private async Task DownloadArtifactAsync(JsonElement artifact, CancellationToken cancellationToken)
    {
        if (!artifact.TryGetProperty("url", out var url) || string.IsNullOrWhiteSpace(url.GetString()))
            return;
        await _downloader.DownloadAsync(new Uri(url.GetString()!), ArtifactPath(artifact),
            artifact.TryGetProperty("sha1", out var sha) ? sha.GetString() : null,
            artifact.TryGetProperty("size", out var size) ? size.GetInt64() : null,
            cancellationToken: cancellationToken);
    }

    private static string ArtifactPath(JsonElement artifact)
    {
        var relative = artifact.GetProperty("path").GetString()
            ?? throw new InvalidDataException("Library artifact has no path.");
        var root = Path.GetFullPath(ViperXPaths.LibrariesDirectory);
        var path = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Library artifact path escapes the libraries directory.");
        return path;
    }

    private static bool IsAllowedOnWindows(JsonElement library)
    {
        if (!library.TryGetProperty("rules", out var rules))
            return true;

        var allowed = false;
        foreach (var rule in rules.EnumerateArray())
        {
            var action = rule.TryGetProperty("action", out var actionElement) ? actionElement.GetString() : null;
            if (action is not ("allow" or "disallow"))
                continue;
            var matches = true;
            if (rule.TryGetProperty("os", out var os) && os.TryGetProperty("name", out var name))
                matches = string.Equals(name.GetString(), "windows", StringComparison.OrdinalIgnoreCase);
            if (matches)
                allowed = action == "allow";
        }
        return allowed;
    }

    private static async Task<bool> HasSha1Async(string path, string expected, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA1.HashDataAsync(stream, cancellationToken);
        return string.Equals(Convert.ToHexString(hash), expected, StringComparison.OrdinalIgnoreCase);
    }

    private static string SafeSegment(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            value.Contains("..", StringComparison.Ordinal) || value.Contains('/') || value.Contains('\\'))
            throw new ArgumentException("Invalid version identifier.", nameof(value));
        return value;
    }
}
