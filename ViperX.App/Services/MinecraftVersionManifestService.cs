using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ViperX.App.Services;

/// <summary>
/// Reads the public Mojang version manifest. This service only reads metadata;
/// it does not download game files or authenticate users.
/// </summary>
public sealed class MinecraftVersionManifestService
{
    public const string ManifestUrl = "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json";

    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(20)
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private IReadOnlyList<MinecraftVersionInfo>? _cachedVersions;
    private DateTimeOffset _cacheTime;

    public async Task<IReadOnlyList<MinecraftVersionInfo>> GetVersionsAsync(
        bool refresh = false,
        CancellationToken cancellationToken = default)
    {
        if (!refresh && _cachedVersions is not null &&
            DateTimeOffset.UtcNow - _cacheTime < TimeSpan.FromMinutes(15))
        {
            return _cachedVersions;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!refresh && _cachedVersions is not null &&
                DateTimeOffset.UtcNow - _cacheTime < TimeSpan.FromMinutes(15))
            {
                return _cachedVersions;
            }

            using var response = await Http.GetAsync(
                ManifestUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);
            var manifest = await JsonSerializer.DeserializeAsync<ManifestDocument>(
                stream, JsonOptions, cancellationToken).ConfigureAwait(false);

            if (manifest?.Versions is null || manifest.Versions.Count == 0)
            {
                throw new InvalidDataException("Mojang returned an empty or invalid Minecraft version manifest.");
            }

            _cachedVersions = manifest.Versions
                .Where(v => !string.IsNullOrWhiteSpace(v.Id) &&
                            !string.IsNullOrWhiteSpace(v.Type) &&
                            Uri.TryCreate(v.Url, UriKind.Absolute, out _))
                .Select(v => new MinecraftVersionInfo(
                    v.Id!,
                    v.Type!,
                    v.Url!,
                    ParseDate(v.ReleaseTime),
                    ParseDate(v.Time)))
                .ToArray();

            if (_cachedVersions.Count == 0)
            {
                throw new InvalidDataException("The Minecraft version manifest contained no usable versions.");
            }

            _cacheTime = DateTimeOffset.UtcNow;
            return _cachedVersions;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<MinecraftVersionInfo?> FindVersionAsync(
        string versionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(versionId);
        var versions = await GetVersionsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        return versions.FirstOrDefault(v => string.Equals(v.Id, versionId, StringComparison.OrdinalIgnoreCase));
    }

    private static DateTimeOffset? ParseDate(string? value) =>
        DateTimeOffset.TryParse(value, out var parsed) ? parsed : null;

    private sealed class ManifestDocument
    {
        [JsonPropertyName("versions")]
        public List<ManifestVersion>? Versions { get; set; }
    }

    private sealed class ManifestVersion
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("type")]
        public string? Type { get; set; }

        [JsonPropertyName("url")]
        public string? Url { get; set; }

        [JsonPropertyName("releaseTime")]
        public string? ReleaseTime { get; set; }

        [JsonPropertyName("time")]
        public string? Time { get; set; }
    }
}
