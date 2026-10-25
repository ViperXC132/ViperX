namespace ViperX.App.Services;

/// <summary>Centralizes local data paths used by ViperX.</summary>
public static class ViperXPaths
{
    public static string RootDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ViperX");
    public static string InstancesDirectory => Path.Combine(RootDirectory, "instances");
    public static string LibrariesDirectory => Path.Combine(RootDirectory, "libraries");
    public static string AssetsDirectory => Path.Combine(RootDirectory, "assets");
    public static string RuntimeDirectory => Path.Combine(RootDirectory, "runtime");
    public static string LogsDirectory => Path.Combine(RootDirectory, "logs");
    public static string SettingsFile => Path.Combine(RootDirectory, "settings.json");

    public static void EnsureBaseDirectories()
    {
        Directory.CreateDirectory(RootDirectory);
        Directory.CreateDirectory(InstancesDirectory);
        Directory.CreateDirectory(LibrariesDirectory);
        Directory.CreateDirectory(AssetsDirectory);
        Directory.CreateDirectory(RuntimeDirectory);
        Directory.CreateDirectory(LogsDirectory);
    }
}
