using System.Security.Cryptography;

namespace ViperX.App.Services;

/// <summary>Downloads files to a temporary path, verifies the supplied SHA-1 when present, then atomically publishes them.</summary>
public sealed class VerifiedDownloadService
{
    private static readonly HttpClient Http = new() { Timeout = Timeout.InfiniteTimeSpan };

    public async Task DownloadAsync(
        Uri source,
        string destinationPath,
        string? expectedSha1 = null,
        long? expectedSize = null,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        if (source.Scheme is not ("https" or "http"))
            throw new ArgumentException("Only HTTP and HTTPS downloads are supported.", nameof(source));
        if (expectedSha1 is not null && (expectedSha1.Length != 40 || !expectedSha1.All(Uri.IsHexDigit)))
            throw new ArgumentException("Expected SHA-1 must contain exactly 40 hexadecimal characters.", nameof(expectedSha1));
        if (expectedSize is < 0)
            throw new ArgumentOutOfRangeException(nameof(expectedSize));

        var fullPath = Path.GetFullPath(destinationPath);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException("Could not determine the destination directory.");
        Directory.CreateDirectory(directory);
        var tempPath = fullPath + "." + Guid.NewGuid().ToString("N") + ".download";

        try
        {
            using var response = await Http.GetAsync(source, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            var total = response.Content.Headers.ContentLength;
            await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var output = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan))
            using (var sha1 = IncrementalHash.CreateHash(HashAlgorithmName.SHA1))
            {
                var buffer = new byte[81920];
                long written = 0;
                while (true)
                {
                    var read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
                    if (read == 0) break;
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    sha1.AppendData(buffer, 0, read);
                    written += read;
                    if (total is > 0)
                        progress?.Report(Math.Clamp((double)written / total.Value, 0, 1));
                }
                await output.FlushAsync(cancellationToken);

                if (expectedSize.HasValue && written != expectedSize.Value)
                    throw new InvalidDataException($"Downloaded size mismatch. Expected {expectedSize.Value} bytes, received {written}.");
                if (!string.IsNullOrWhiteSpace(expectedSha1))
                {
                    var actual = Convert.ToHexString(sha1.GetHashAndReset()).ToLowerInvariant();
                    if (!CryptographicOperations.FixedTimeEquals(
                        System.Text.Encoding.ASCII.GetBytes(actual),
                        System.Text.Encoding.ASCII.GetBytes(expectedSha1.ToLowerInvariant())))
                        throw new InvalidDataException("Downloaded file failed SHA-1 verification.");
                }
            }

            File.Move(tempPath, fullPath, overwrite: true);
            progress?.Report(1);
        }
        finally
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); }
            catch (IOException) { /* Best-effort cleanup; keep the original failure. */ }
            catch (UnauthorizedAccessException) { /* Best-effort cleanup. */ }
        }
    }
}
