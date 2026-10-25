namespace ViperX.App.Models;

/// <summary>Persistable user preferences. Paths may be null when automatic detection is preferred.</summary>
public sealed record LauncherSettings
{
    public string? JavaExecutablePath { get; init; }
    public int DefaultMemoryMegabytes { get; init; } = 4096;
    public string? DefaultGameDirectory { get; init; }
    public int WindowWidth { get; init; } = 1280;
    public int WindowHeight { get; init; } = 800;
    public bool CloseLauncherOnGameStart { get; init; }
    public bool KeepLauncherOpenAfterGameExit { get; init; } = true;

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (DefaultMemoryMegabytes < 512 || DefaultMemoryMegabytes > 262144)
            errors.Add("Default memory must be between 512 MB and 262144 MB.");
        if (WindowWidth < 640 || WindowWidth > 7680)
            errors.Add("Window width must be between 640 and 7680 pixels.");
        if (WindowHeight < 480 || WindowHeight > 4320)
            errors.Add("Window height must be between 480 and 4320 pixels.");
        return errors;
    }
}
