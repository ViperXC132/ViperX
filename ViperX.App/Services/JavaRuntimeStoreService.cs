using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;

namespace ViperX.App.Services;

public sealed record JavaRuntimePackage(int MajorVersion, string DisplayName, string Description);

/// <summary>Downloads portable Eclipse Temurin JRE archives into ViperX's private runtime directory.</summary>
public sealed class JavaRuntimeStoreService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(10) };

    public IReadOnlyList<JavaRuntimePackage> AvailablePackages { get; } = new[]
    {
        new JavaRuntimePackage(8, "Java 8", "For legacy Minecraft versions, including many 1.16.5 and older setups."),
        new JavaRuntimePackage(17, "Java 17", "Recommended for Minecraft 1.18 through 1.20.4."),
        new JavaRuntimePackage(21, "Java 21", "Recommended for Minecraft 1.20.5 and newer releases."),
        new JavaRuntimePackage(25, "Java 25", "For versions that explicitly support the latest Java runtime.")
    };

    public async Task<string> InstallAsync(int majorVersion, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        if (!AvailablePackages.Any(p => p.MajorVersion == majorVersion))
            throw new ArgumentOutOfRangeException(nameof(majorVersion), "Unsupported Java version.");

        var root = Path.GetFullPath(Path.Combine(ViperXPaths.RuntimeDirectory, "managed-java"));
        Directory.CreateDirectory(root);
        var destination = Path.Combine(root, $"temurin-{majorVersion}");
        var javaExe = Path.Combine(destination, "bin", "java.exe");
        if (File.Exists(javaExe))
        {
            var existing = await new JavaRuntimeService().ProbeAsync(javaExe, cancellationToken);
            if (existing?.MajorVersion == majorVersion) return javaExe;
        }

        var tempArchive = Path.Combine(root, $"temurin-{majorVersion}-{Guid.NewGuid():N}.zip");
        var tempExtract = Path.Combine(root, $".install-{majorVersion}-{Guid.NewGuid():N}");
        try
        {
            var url = $"https://api.adoptium.net/v3/binary/latest/{majorVersion}/ga/windows/x64/jre/hotspot/normal/eclipse";
            using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength;
            await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var output = new FileStream(tempArchive, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                var buffer = new byte[81920];
                long received = 0;
                int read;
                while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    received += read;
                    if (total is > 0) progress?.Report(Math.Clamp((double)received / total.Value * 85, 0, 85));
                }
            }

            Directory.CreateDirectory(tempExtract);
            ZipFile.ExtractToDirectory(tempArchive, tempExtract, overwriteFiles: true);
            var extractedJava = Directory.GetFiles(tempExtract, "java.exe", SearchOption.AllDirectories)
                .FirstOrDefault(p => string.Equals(Path.GetFileName(Path.GetDirectoryName(p)), "bin", StringComparison.OrdinalIgnoreCase));
            if (extractedJava is null)
                throw new InvalidDataException("The downloaded Temurin archive did not contain bin\\java.exe.");

            var sourceRoot = Directory.GetParent(Path.GetDirectoryName(extractedJava)!)!.FullName;
            var stagedDestination = destination + ".new";
            if (Directory.Exists(stagedDestination)) Directory.Delete(stagedDestination, true);
            Directory.CreateDirectory(stagedDestination);
            CopyDirectory(sourceRoot, stagedDestination);
            var stagedJava = Path.Combine(stagedDestination, "bin", "java.exe");
            var verified = await new JavaRuntimeService().ProbeAsync(stagedJava, cancellationToken);
            if (verified is null || verified.MajorVersion != majorVersion)
                throw new InvalidDataException($"Downloaded Java did not verify as Java {majorVersion}.");

            if (Directory.Exists(destination)) Directory.Delete(destination, true);
            Directory.Move(stagedDestination, destination);
            progress?.Report(100);
            return javaExe;
        }
        finally
        {
            if (File.Exists(tempArchive)) File.Delete(tempArchive);
            if (Directory.Exists(tempExtract)) Directory.Delete(tempExtract, true);
            var staged = destination + ".new";
            if (Directory.Exists(staged)) Directory.Delete(staged, true);
        }
    }

    public async Task<IReadOnlyList<JavaRuntimeInfo>> FindManagedAsync(CancellationToken cancellationToken = default)
    {
        var root = Path.Combine(ViperXPaths.RuntimeDirectory, "managed-java");
        if (!Directory.Exists(root)) return Array.Empty<JavaRuntimeInfo>();
        var results = new List<JavaRuntimeInfo>();
        foreach (var executable in Directory.GetFiles(root, "java.exe", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!string.Equals(Path.GetFileName(Path.GetDirectoryName(executable)), "bin", StringComparison.OrdinalIgnoreCase))
                continue;
            var info = await new JavaRuntimeService().ProbeAsync(executable, cancellationToken);
            if (info is not null) results.Add(info);
        }
        return results.OrderBy(x => x.MajorVersion).ToArray();
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);
        foreach (var directory in Directory.GetDirectories(source))
            CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
    }
}
