using System.Text.Json;
using ViperX.App.Models;

namespace ViperX.App.Services;

/// <summary>Loads and saves launcher settings using an atomic replace pattern.</summary>
public sealed class LauncherSettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public async Task<LauncherSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        ViperXPaths.EnsureBaseDirectories();
        if (!File.Exists(ViperXPaths.SettingsFile))
            return new LauncherSettings();

        await using var stream = new FileStream(
            ViperXPaths.SettingsFile, FileMode.Open, FileAccess.Read, FileShare.Read,
            4096, FileOptions.Asynchronous | FileOptions.SequentialScan);

        var settings = await JsonSerializer.DeserializeAsync<LauncherSettings>(
            stream, JsonOptions, cancellationToken);
        if (settings is null || settings.Validate().Count != 0)
            return new LauncherSettings();

        return settings;
    }

    public async Task SaveAsync(LauncherSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var errors = settings.Validate();
        if (errors.Count > 0)
            throw new ArgumentException(string.Join(Environment.NewLine, errors), nameof(settings));

        ViperXPaths.EnsureBaseDirectories();
        var tempPath = ViperXPaths.SettingsFile + ".tmp";
        await using (var stream = new FileStream(
            tempPath, FileMode.Create, FileAccess.Write, FileShare.None,
            4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
        {
            await JsonSerializer.SerializeAsync(stream, settings, JsonOptions, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }

        File.Move(tempPath, ViperXPaths.SettingsFile, overwrite: true);
    }
}
