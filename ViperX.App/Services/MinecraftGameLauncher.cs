using System.Diagnostics;
using System.Text.Json;
using ViperX.App.Models;

namespace ViperX.App.Services;

/// <summary>Builds arguments from Mojang metadata and starts the Java client for an authenticated account.</summary>
public sealed class MinecraftGameLauncher
{
    private readonly MinecraftVersionManifestService _manifest = new();

    public async Task<Process> LaunchAsync(MinecraftLaunchRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.VersionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Username);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.PlayerUuid);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.AccessToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.JavaExecutablePath);

        if (request.MemoryMegabytes < 512 || request.MemoryMegabytes > 262144)
            throw new ArgumentOutOfRangeException(nameof(request.MemoryMegabytes), "Memory must be between 512 MB and 262144 MB.");
        if (!File.Exists(request.JavaExecutablePath))
            throw new FileNotFoundException("The selected Java executable was not found.", request.JavaExecutablePath);

        var version = await _manifest.FindVersionAsync(request.VersionId, cancellationToken)
            ?? throw new InvalidOperationException($"Version '{request.VersionId}' was not found in Mojang's manifest.");
        var rootDirectory = Path.Combine(ViperXPaths.InstancesDirectory, request.VersionId);
        var versionDirectory = Path.Combine(rootDirectory, "versions", request.VersionId);
        var clientJar = Path.Combine(versionDirectory, request.VersionId + ".jar");
        if (!File.Exists(clientJar) || !File.Exists(Path.Combine(versionDirectory, "installed.json")))
            throw new InvalidOperationException("This version is not installed. Install it before launching.");

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        using var response = await http.GetAsync(version.Url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var metadata = document.RootElement;

        var mainClass = metadata.GetProperty("mainClass").GetString()
            ?? throw new InvalidDataException("Version metadata has no main class.");
        var assetIndex = metadata.TryGetProperty("assetIndex", out var assetElement) &&
                         assetElement.TryGetProperty("id", out var idElement)
            ? idElement.GetString() ?? string.Empty
            : string.Empty;
        var loggingPath = string.Empty;
        if (metadata.TryGetProperty("logging", out var logging) &&
            logging.TryGetProperty("client", out var loggingClient) &&
            loggingClient.TryGetProperty("file", out var loggingFile) &&
            loggingFile.TryGetProperty("id", out var loggingId))
        {
            loggingPath = Path.Combine(versionDirectory, loggingId.GetString() ?? string.Empty);
        }
        var gameDirectory = Path.GetFullPath(request.GameDirectory ?? rootDirectory);
        Directory.CreateDirectory(gameDirectory);
        var assetsDirectory = Path.GetFullPath(ViperXPaths.AssetsDirectory);
        var nativesDirectory = Path.Combine(rootDirectory, "natives");
        var classpath = BuildClasspath(metadata, clientJar);
        var classpathSeparator = OperatingSystem.IsWindows() ? ";" : Path.PathSeparator.ToString();

        var placeholders = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["auth_player_name"] = request.Username,
            ["auth_uuid"] = request.PlayerUuid.Replace("-", string.Empty, StringComparison.Ordinal),
            ["auth_access_token"] = request.AccessToken,
            ["user_type"] = "msa",
            ["version_name"] = request.VersionId,
            ["game_directory"] = gameDirectory,
            ["assets_root"] = assetsDirectory,
            ["assets_index_name"] = assetIndex,
            ["natives_directory"] = nativesDirectory,
            ["classpath"] = classpath,
            ["classpath_separator"] = classpathSeparator,
            ["launcher_name"] = "ViperX",
            ["launcher_version"] = "1.0",
            ["clientid"] = string.Empty,
            ["auth_xuid"] = string.Empty,
            ["user_properties"] = "{}",
            ["path"] = loggingPath
        };

        var startInfo = new ProcessStartInfo
        {
            FileName = Path.GetFullPath(request.JavaExecutablePath),
            WorkingDirectory = gameDirectory,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add($"-Xms512M");
        startInfo.ArgumentList.Add($"-Xmx{request.MemoryMegabytes}M");
        startInfo.ArgumentList.Add($"-Djava.library.path={nativesDirectory}");

        var jvmArguments = GetArguments(metadata, "jvm");
        if (jvmArguments.Count > 0)
        {
            foreach (var argument in jvmArguments)
                AddArgument(startInfo, argument, placeholders);
        }
        else
        {
            startInfo.ArgumentList.Add("-cp");
            startInfo.ArgumentList.Add(classpath);
        }

        startInfo.ArgumentList.Add(mainClass);
        var gameArguments = GetArguments(metadata, "game");
        if (gameArguments.Count > 0)
        {
            foreach (var argument in gameArguments)
                AddArgument(startInfo, argument, placeholders);
        }
        else if (metadata.TryGetProperty("minecraftArguments", out var legacyArguments) &&
                 legacyArguments.ValueKind == JsonValueKind.String)
        {
            foreach (var argument in SplitLegacyArguments(legacyArguments.GetString()!))
                AddArgument(startInfo, argument, placeholders);
        }

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        if (!process.Start())
        {
            process.Dispose();
            throw new InvalidOperationException("Java did not start the Minecraft process.");
        }
        return process;
    }

    private static List<string> GetArguments(JsonElement metadata, string kind)
    {
        if (!metadata.TryGetProperty("arguments", out var arguments) ||
            !arguments.TryGetProperty(kind, out var values) || values.ValueKind != JsonValueKind.Array)
            return new List<string>();

        var result = new List<string>();
        foreach (var item in values.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                result.Add(item.GetString()!);
                continue;
            }

            if (item.ValueKind != JsonValueKind.Object ||
                !item.TryGetProperty("value", out var value))
                continue;
            if (!RulesAllow(item))
                continue;

            if (value.ValueKind == JsonValueKind.String)
                result.Add(value.GetString()!);
            else if (value.ValueKind == JsonValueKind.Array)
                result.AddRange(value.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.String).Select(v => v.GetString()!));
        }
        return result;
    }

    private static bool RulesAllow(JsonElement item)
    {
        if (!item.TryGetProperty("rules", out var rules))
            return true;
        var allowed = false;
        foreach (var rule in rules.EnumerateArray())
        {
            var matches = true;
            if (rule.TryGetProperty("os", out var os) && os.TryGetProperty("name", out var name))
                matches = string.Equals(name.GetString(), "windows", StringComparison.OrdinalIgnoreCase) == OperatingSystem.IsWindows();
            if (matches && rule.TryGetProperty("action", out var action))
                allowed = action.GetString() == "allow";
        }
        return allowed;
    }

    private static IEnumerable<string> SplitLegacyArguments(string arguments)
    {
        // Mojang's legacy argument format is space-delimited and does not define shell quoting.
        return arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static void AddArgument(ProcessStartInfo startInfo, string argument, IReadOnlyDictionary<string, string> placeholders)
    {
        foreach (var pair in placeholders)
            argument = argument.Replace(" + pair.Key + ", pair.Value, StringComparison.Ordinal);
        startInfo.ArgumentList.Add(argument);
    }

    private static string BuildClasspath(JsonElement metadata, string clientJar)
    {
        var paths = new List<string>();
        if (metadata.TryGetProperty("libraries", out var libraries))
        {
            foreach (var library in libraries.EnumerateArray())
            {
                if (!LibraryAllowed(library) || !library.TryGetProperty("downloads", out var downloads) ||
                    !downloads.TryGetProperty("artifact", out var artifact) ||
                    !artifact.TryGetProperty("path", out var pathElement))
                    continue;
                var relative = pathElement.GetString();
                if (string.IsNullOrWhiteSpace(relative))
                    continue;
                var fullPath = Path.GetFullPath(Path.Combine(ViperXPaths.LibrariesDirectory, relative.Replace('/', Path.DirectorySeparatorChar)));
                if (File.Exists(fullPath))
                    paths.Add(fullPath);
            }
        }
        paths.Add(Path.GetFullPath(clientJar));
        return string.Join(OperatingSystem.IsWindows() ? ";" : Path.PathSeparator.ToString(), paths.Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private static bool LibraryAllowed(JsonElement library)
    {
        if (!library.TryGetProperty("rules", out var rules))
            return true;
        var allowed = false;
        foreach (var rule in rules.EnumerateArray())
        {
            var matches = true;
            if (rule.TryGetProperty("os", out var os) && os.TryGetProperty("name", out var name))
                matches = string.Equals(name.GetString(), "windows", StringComparison.OrdinalIgnoreCase) == OperatingSystem.IsWindows();
            if (matches && rule.TryGetProperty("action", out var action))
                allowed = action.GetString() == "allow";
        }
        return allowed;
    }
}
