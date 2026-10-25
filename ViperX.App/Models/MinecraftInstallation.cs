namespace ViperX.App.Models;

/// <summary>
/// User-configurable Minecraft installation metadata. This model does not imply
/// that the referenced version has been downloaded or is launchable yet.
/// </summary>
public sealed record MinecraftInstallation
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = "Vanilla";
    public string GameVersion { get; init; } = string.Empty;
    public string GameDirectory { get; init; } = string.Empty;
    public string? JavaExecutablePath { get; init; }
    public int MemoryMegabytes { get; init; } = 4096;
    public string Loader { get; init; } = "Vanilla";
    public string? LoaderVersion { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastPlayedAt { get; init; }

    /// <summary>Returns validation messages; an empty result means the metadata is valid.</summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (Id == Guid.Empty)
            errors.Add("Installation ID must not be empty.");
        if (string.IsNullOrWhiteSpace(Name))
            errors.Add("Installation name is required.");
        if (string.IsNullOrWhiteSpace(GameVersion))
            errors.Add("A Minecraft version must be selected.");
        if (string.IsNullOrWhiteSpace(GameDirectory))
            errors.Add("A game directory is required.");
        if (MemoryMegabytes < 512)
            errors.Add("Memory allocation must be at least 512 MB.");
        if (MemoryMegabytes > 262144)
            errors.Add("Memory allocation must not exceed 262144 MB.");
        if (string.IsNullOrWhiteSpace(Loader))
            errors.Add("Loader must be specified.");

        return errors;
    }
}
