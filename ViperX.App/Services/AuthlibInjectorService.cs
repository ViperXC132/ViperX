using System.Text.Json;

namespace ViperX.App.Services;

/// <summary>Downloads the upstream authlib-injector artifact required by third-party Yggdrasil servers.</summary>
public sealed class AuthlibInjectorService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private const string MetadataUrl = "https://authlib-injector.yushi.moe/artifact/latest.json";

    public async Task<string> EnsureDownloadedAsync(CancellationToken cancellationToken = default)
    {
        var directory = Path.Combine(ViperXPaths.RootDirectory, "authlib-injector");
        Directory.CreateDirectory(directory);
        var existing = Directory.GetFiles(directory, "authlib-injector-*.jar")
            .OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
        if (existing is not null && new FileInfo(existing).Length > 100_000)
            return existing;

        using var response = await Http.GetAsync(MetadataUrl, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var metadata = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        if (!metadata.RootElement.TryGetProperty("download_url", out var urlElement) ||
            !Uri.TryCreate(urlElement.GetString(), UriKind.Absolute, out var downloadUri) ||
            downloadUri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidDataException("authlib-injector release metadata did not contain a trusted HTTPS download URL.");

        using var download = await Http.GetAsync(downloadUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        download.EnsureSuccessStatusCode();
        if (download.Content.Headers.ContentLength is > 50_000_000)
            throw new InvalidDataException("authlib-injector download is unexpectedly large.");
        var bytes = await download.Content.ReadAsByteArrayAsync(cancellationToken);
        if (bytes.Length < 100_000 || bytes.Length > 50_000_000 ||
            !bytes.AsSpan(0, Math.Min(2, bytes.Length)).SequenceEqual(new byte[] { 0x50, 0x4b }))
            throw new InvalidDataException("The authlib-injector download does not look like a valid JAR archive.");
        var version = metadata.RootElement.TryGetProperty("version", out var v) ? v.GetString() : null;
        var safeVersion = string.IsNullOrWhiteSpace(version) ? "latest" : string.Concat(version.Where(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-'));
        var path = Path.Combine(directory, $"authlib-injector-{safeVersion}.jar");
        await File.WriteAllBytesAsync(path, bytes, cancellationToken);
        return path;
    }
}
