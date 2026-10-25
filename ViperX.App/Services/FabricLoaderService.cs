using System.Text.Json;
using ViperX.App.Models;

namespace ViperX.App.Services;

public sealed record FabricLoaderVersion(string Version, bool Stable);

/// <summary>Installs official Fabric loader metadata and its HTTPS-hosted Maven libraries for an existing vanilla version.</summary>
public sealed class FabricLoaderService
{
    private const string MetadataRoot = "https://meta.fabricmc.net";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private readonly VerifiedDownloadService _downloader = new();

    public async Task<string> InstallLatestStableAsync(string gameVersion, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameVersion);
        var instanceRoot = Path.Combine(ViperXPaths.InstancesDirectory, SafeSegment(gameVersion));
        if (!File.Exists(Path.Combine(instanceRoot, "versions", gameVersion, "installed.json")))
            throw new InvalidOperationException($"Install vanilla Minecraft {gameVersion} before installing Fabric.");

        var listUri = new Uri($"{MetadataRoot}/v2/versions/loader/{Uri.EscapeDataString(gameVersion)}");
        using var listResponse = await Http.GetAsync(listUri, cancellationToken);
        listResponse.EnsureSuccessStatusCode();
        using var listDocument = JsonDocument.Parse(await listResponse.Content.ReadAsStringAsync(cancellationToken));
        var available = listDocument.RootElement.EnumerateArray()
            .Select(item => new FabricLoaderVersion(
                item.GetProperty("loader").GetProperty("version").GetString()!,
                item.GetProperty("loader").TryGetProperty("stable", out var stable) && stable.GetBoolean()))
            .ToArray();
        var selected = available.FirstOrDefault(item => item.Stable) ?? available.FirstOrDefault();
        if (selected is null)
            throw new InvalidDataException($"Fabric does not publish a loader for Minecraft {gameVersion}.");

        var profileUri = new Uri($"{MetadataRoot}/v2/versions/loader/{Uri.EscapeDataString(gameVersion)}/{Uri.EscapeDataString(selected.Version)}/profile/json");
        using var profileResponse = await Http.GetAsync(profileUri, cancellationToken);
        profileResponse.EnsureSuccessStatusCode();
        var profileJson = await profileResponse.Content.ReadAsStringAsync(cancellationToken);
        using var profileDocument = JsonDocument.Parse(profileJson);
        var profile = profileDocument.RootElement;
        if (!profile.TryGetProperty("inheritsFrom", out var inherits) ||
            !string.Equals(inherits.GetString(), gameVersion, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Fabric profile does not inherit from the requested Minecraft version.");

        if (profile.TryGetProperty("libraries", out var libraries))
        {
            foreach (var library in libraries.EnumerateArray())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var name = library.TryGetProperty("name", out var nameElement) ? nameElement.GetString() : null;
                var baseUrl = library.TryGetProperty("url", out var urlElement) ? urlElement.GetString() : null;
                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(baseUrl))
                    continue;
                var artifact = ResolveMavenArtifact(name, baseUrl);
                if (!File.Exists(artifact.Path))
                    await _downloader.DownloadAsync(artifact.Uri, artifact.Path, cancellationToken: cancellationToken);
            }
        }

        var profilesDirectory = Path.Combine(instanceRoot, "loader-profiles");
        Directory.CreateDirectory(profilesDirectory);
        var profilePath = Path.Combine(profilesDirectory, $"fabric-{SafeSegment(selected.Version)}.json");
        await File.WriteAllTextAsync(profilePath, profileJson, cancellationToken);

        var store = new InstallationStore();
        var installations = (await store.LoadAsync(cancellationToken)).ToList();
        var index = installations.FindIndex(item => string.Equals(item.GameVersion, gameVersion, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
            throw new InvalidOperationException("The installed version metadata could not be found.");
        installations[index] = installations[index] with { Loader = "Fabric", LoaderVersion = selected.Version };
        await store.SaveAsync(installations, cancellationToken);
        return selected.Version;
    }

    public static string GetProfilePath(string gameVersion, string loaderVersion)
    {
        var root = Path.Combine(ViperXPaths.InstancesDirectory, SafeSegment(gameVersion), "loader-profiles");
        return Path.Combine(root, $"fabric-{SafeSegment(loaderVersion)}.json");
    }

    private static (Uri Uri, string Path) ResolveMavenArtifact(string coordinate, string baseUrl)
    {
        var parts = coordinate.Split(':');
        if (parts.Length < 3 || parts.Any(string.IsNullOrWhiteSpace))
            throw new InvalidDataException($"Unsupported Fabric Maven coordinate: {coordinate}");

        var groupPath = parts[0].Replace('.', '/');
        var artifact = parts[1];
        var version = parts[2];
        var classifier = parts.Length > 3 ? "-" + parts[3] : string.Empty;
        var relative = $"{groupPath}/{artifact}/{version}/{artifact}-{version}{classifier}.jar";
        var root = Path.GetFullPath(ViperXPaths.LibrariesDirectory);
        var path = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Fabric library path escapes the libraries directory.");

        if (!Uri.TryCreate(baseUrl.TrimEnd('/') + "/" + relative, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Fabric library URL must use HTTPS.");
        return (uri, path);
    }

    private static string SafeSegment(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            value.Contains("..", StringComparison.Ordinal) || value.Contains('/') || value.Contains('\\'))
            throw new ArgumentException("Invalid version identifier.", nameof(value));
        return value;
    }
}
