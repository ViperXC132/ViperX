namespace ViperX.App.Models;

/// <summary>
/// A named launcher profile that points to an installation configuration.
/// Profiles are metadata only until the installation and launch services are implemented.
/// </summary>
public sealed record LauncherProfile
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = "Default";
    public Guid InstallationId { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (Id == Guid.Empty)
            errors.Add("Profile ID must not be empty.");
        if (string.IsNullOrWhiteSpace(Name))
            errors.Add("Profile name is required.");
        if (InstallationId == Guid.Empty)
            errors.Add("A valid installation must be assigned to this profile.");

        return errors;
    }
}
