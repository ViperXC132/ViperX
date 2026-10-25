namespace ViperX.App.Services;

/// <summary>
/// A version entry from Mojang's official version manifest.
/// </summary>
public sealed record MinecraftVersionInfo(
    string Id,
    string Type,
    string Url,
    DateTimeOffset? ReleaseTime,
    DateTimeOffset? Time)
{
    public string DisplayName => Id;
    public bool IsRelease => string.Equals(Type, "release", StringComparison.OrdinalIgnoreCase);
    public bool IsSnapshot => string.Equals(Type, "snapshot", StringComparison.OrdinalIgnoreCase);
}
