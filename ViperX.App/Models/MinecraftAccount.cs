namespace ViperX.App.Models;

/// <summary>Account identity metadata; authentication tokens are never included here.</summary>
public sealed record MinecraftAccount
{
    public required string Id { get; init; }
    public required string Username { get; init; }
    public required string Uuid { get; init; }
    public string AccountType { get; init; } = "Microsoft";
    public DateTimeOffset SignedInAt { get; init; } = DateTimeOffset.UtcNow;
}
