namespace ViperX.App.Models;

/// <summary>Transient launch input. Access tokens must never be persisted or written to logs.</summary>
public sealed record MinecraftLaunchRequest
{
    public required string VersionId { get; init; }
    public string Loader { get; init; } = "Vanilla";
    public string? LoaderVersion { get; init; }
    public required string Username { get; init; }
    public required string PlayerUuid { get; init; }
    public required string AccessToken { get; init; }
    public required string JavaExecutablePath { get; init; }
    public int MemoryMegabytes { get; init; } = 4096;
    public string? GameDirectory { get; init; }
}
