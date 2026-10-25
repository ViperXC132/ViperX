namespace ViperX.App.Services;

/// <summary>Holds the current process-session token only; it never writes credentials to disk or logs.</summary>
public static class ActiveMinecraftSession
{
    private static readonly object Sync = new();
    private static AuthenticatedMinecraftSession? _current;

    public static AuthenticatedMinecraftSession? Current
    {
        get { lock (Sync) return _current; }
    }

    public static void Set(AuthenticatedMinecraftSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        lock (Sync) _current = session;
    }

    public static void Clear()
    {
        lock (Sync) _current = null;
    }
}
