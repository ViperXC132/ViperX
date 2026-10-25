using System.Text.Json;
using ViperX.App.Models;

namespace ViperX.App.Services;

/// <summary>Persists installation metadata only; game binaries are managed by the installer.</summary>
public sealed class InstallationStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    private static string StorePath => Path.Combine(ViperXPaths.RootDirectory, "installations.json");

    public async Task<IReadOnlyList<MinecraftInstallation>> LoadAsync(CancellationToken cancellationToken = default)
    {
        ViperXPaths.EnsureBaseDirectories();
        if (!File.Exists(StorePath))
            return Array.Empty<MinecraftInstallation>();

        await using var stream = new FileStream(StorePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
        var entries = await JsonSerializer.DeserializeAsync<List<MinecraftInstallation>>(stream, Options, cancellationToken);
        return (entries ?? new List<MinecraftInstallation>())
            .Where(entry => entry.Validate().Count == 0)
            .GroupBy(entry => entry.Id)
            .Select(group => group.First())
            .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task SaveAsync(IEnumerable<MinecraftInstallation> installations, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(installations);
        var entries = installations.ToArray();
        var errors = entries.SelectMany((entry, index) => entry.Validate().Select(error => $"Installation {index + 1}: {error}")).ToArray();
        if (errors.Length > 0)
            throw new ArgumentException(string.Join(Environment.NewLine, errors), nameof(installations));
        if (entries.Select(entry => entry.Id).Distinct().Count() != entries.Length)
            throw new ArgumentException("Installation IDs must be unique.", nameof(installations));

        ViperXPaths.EnsureBaseDirectories();
        var tempPath = StorePath + ".tmp";
        await using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
        {
            await JsonSerializer.SerializeAsync(stream, entries, Options, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }
        File.Move(tempPath, StorePath, overwrite: true);
    }
}
