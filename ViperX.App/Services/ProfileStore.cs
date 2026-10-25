using System.Text.Json;
using ViperX.App.Models;

namespace ViperX.App.Services;

public sealed class ProfileStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    private static string StorePath => Path.Combine(ViperXPaths.RootDirectory, "profiles.json");

    public async Task<IReadOnlyList<LauncherProfile>> LoadAsync(CancellationToken cancellationToken = default)
    {
        ViperXPaths.EnsureBaseDirectories();
        if (!File.Exists(StorePath))
            return Array.Empty<LauncherProfile>();
        await using var stream = new FileStream(StorePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
        var profiles = await JsonSerializer.DeserializeAsync<List<LauncherProfile>>(stream, Options, cancellationToken);
        return (profiles ?? new List<LauncherProfile>())
            .Where(profile => profile.Validate().Count == 0)
            .GroupBy(profile => profile.Id)
            .Select(group => group.First())
            .OrderBy(profile => profile.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task SaveAsync(IEnumerable<LauncherProfile> profiles, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        var items = profiles.ToArray();
        if (items.Any(profile => profile.Validate().Count != 0))
            throw new ArgumentException("Every profile must have a name and a valid installation reference.", nameof(profiles));
        if (items.Select(profile => profile.Id).Distinct().Count() != items.Length)
            throw new ArgumentException("Profile IDs must be unique.", nameof(profiles));

        ViperXPaths.EnsureBaseDirectories();
        var tempPath = StorePath + ".tmp";
        await using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
        {
            await JsonSerializer.SerializeAsync(stream, items, Options, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }
        File.Move(tempPath, StorePath, overwrite: true);
    }
}
