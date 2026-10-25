namespace ViperX.App.Models;

/// <summary>Account identity metadata. Tokens are deliberately excluded from this persistable model.</summary>
public sealed record MinecraftAccount
{
    public required string Id { get; init; }
    public required string Username { get; init; }
    public required string Uuid { get; init; }
    public DateTimeOffset SignedInAt { get; init; } = DateTimeOffset.UtcNow;
}
