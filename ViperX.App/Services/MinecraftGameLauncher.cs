using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using ViperX.App.Models;

namespace ViperX.App.Services;

/// <summary>Builds arguments from Mojang metadata and starts the Java client for an authenticated account.</summary>
public sealed class MinecraftGameLauncher
{
    private readonly MinecraftVersionManifestService _manifest = new();

    public async Task<int> GetRequiredJavaMajorAsync(string versionId, CancellationToken cancellationToken = default)
    {
        var version = await _manifest.FindVersionAsync(versionId, cancellationToken)
            ?? throw new InvalidOperationException($"Version '{versionId}' was not found in Mojang's manifest.");
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        using var response = await http.GetAsync(version.Url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;
        return root.TryGetProperty("javaVersion", out var javaVersion) &&
               javaVersion.TryGetProperty("majorVersion", out var major) && major.TryGetInt32(out var required)
            ? required
            : 8;
    }

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
        var rootDirectory = Path.GetFullPath(request.InstanceDirectory ?? Path.Combine(ViperXPaths.InstancesDirectory, request.VersionId));
        var versionDirectory = Path.Combine(rootDirectory, "versions", request.VersionId);
        var clientJar = Path.Combine(versionDirectory, request.VersionId + ".jar");
        if (!File.Exists(clientJar) || !File.Exists(Path.Combine(versionDirectory, "installed.json")))
            throw new InvalidOperationException("This version is not installed. Install it before launching.");

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        using var response = await http.GetAsync(version.Url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        var metadataJson = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.Equals(request.Loader, "Fabric", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(request.LoaderVersion))
                throw new InvalidOperationException("The Fabric loader version is missing from installation metadata.");
            var profilePath = Path.Combine(rootDirectory, "loader-profiles", $"fabric-{request.LoaderVersion}.json");
            if (!File.Exists(profilePath))
                throw new FileNotFoundException("The Fabric loader profile is missing. Reinstall Fabric for this version.", profilePath);
            var baseNode = JsonNode.Parse(metadataJson)?.AsObject()
                ?? throw new InvalidDataException("Mojang version metadata is invalid.");
            var profileNode = JsonNode.Parse(await File.ReadAllTextAsync(profilePath, cancellationToken))?.AsObject()
                ?? throw new InvalidDataException("Fabric loader profile is invalid.");
            if (profileNode["mainClass"] is not null)
                baseNode["mainClass"] = profileNode["mainClass"]!.DeepClone();
            if (profileNode["arguments"] is not null)
                baseNode["arguments"] = profileNode["arguments"]!.DeepClone();

            var combinedLibraries = baseNode["libraries"] as JsonArray;
            if (combinedLibraries is null)
            {
                combinedLibraries = new JsonArray();
                baseNode["libraries"] = combinedLibraries;
            }
            if (profileNode["libraries"] is JsonArray fabricLibraries)
            {
                foreach (var library in fabricLibraries)
                {
                    if (library is not JsonObject fabricLibrary ||
                        fabricLibrary["name"] is not JsonValue nameValue ||
                        !nameValue.TryGetValue<string>(out var coordinate) ||
                        string.IsNullOrWhiteSpace(coordinate))
                        continue;
                    if (fabricLibrary["downloads"] is JsonObject)
                    {
                        combinedLibraries.Add(fabricLibrary.DeepClone());
                        continue;
                    }

                    var relative = FabricLibraryRelativePath(coordinate);
                    var baseUrl = fabricLibrary["url"]?.GetValue<string>();
                    if (string.IsNullOrWhiteSpace(baseUrl))
                        throw new InvalidDataException($"Fabric library '{coordinate}' has no repository URL.");
                    var libraryRoot = Path.GetFullPath(ViperXPaths.LibrariesDirectory);
                    var libraryPath = Path.GetFullPath(Path.Combine(libraryRoot, relative.Replace('/', Path.DirectorySeparatorChar)));
                    if (!libraryPath.StartsWith(libraryRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("Fabric library path escapes the libraries directory.");
                    combinedLibraries.Add(new JsonObject
                    {
                        ["name"] = coordinate,
                        ["downloads"] = new JsonObject
                        {
                            ["artifact"] = new JsonObject
                            {
                                ["path"] = relative,
                                ["url"] = baseUrl.TrimEnd('/') + "/" + relative
                            }
                        }
                    });
                }
            }
            metadataJson = baseNode.ToJsonString();
        }
        using var document = JsonDocument.Parse(metadataJson);
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
            ["auth_session"] = request.AccessToken,
            ["user_type"] = "msa",
            ["version_name"] = request.VersionId,
            ["version_type"] = metadata.TryGetProperty("type", out var typeElement) ? typeElement.GetString() ?? "release" : "release",
            ["game_directory"] = gameDirectory,
            ["assets_root"] = assetsDirectory,
            ["game_assets"] = Path.Combine(assetsDirectory, "virtual", "legacy"),
            ["assets_index_name"] = assetIndex,
            ["library_directory"] = Path.GetFullPath(ViperXPaths.LibrariesDirectory),
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

    private static string FabricLibraryRelativePath(string coordinate)
    {
        var parts = coordinate.Split(':');
        if (parts.Length < 3 || parts.Any(string.IsNullOrWhiteSpace))
            throw new InvalidDataException($"Unsupported Fabric Maven coordinate: {coordinate}");
        var group = parts[0].Replace('.', '/');
        var artifact = parts[1];
        var version = parts[2];
        var classifier = parts.Length > 3 ? "-" + parts[3] : string.Empty;
        return $"{group}/{artifact}/{version}/{artifact}-{version}{classifier}.jar";
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
            if (matches && rule.TryGetProperty("features", out var features))
            {
                foreach (var feature in features.EnumerateObject())
                {
                    var supported = feature.Name switch
                    {
                        "is_demo_user" => false,
                        "has_custom_resolution" => false,
                        "has_quick_plays_support" => false,
                        "is_quick_play_singleplayer" => false,
                        "is_quick_play_multiplayer" => false,
                        "is_quick_play_realms" => false,
                        _ => false
                    };
                    if ((feature.Value.ValueKind is JsonValueKind.True or JsonValueKind.False) &&
                        feature.Value.GetBoolean() != supported)
                    {
                        matches = false;
                        break;
                    }
                }
            }
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
            argument = argument.Replace("${" + pair.Key + "}", pair.Value, StringComparison.Ordinal);
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
                var libraryRoot = Path.GetFullPath(ViperXPaths.LibrariesDirectory);
                var fullPath = Path.GetFullPath(Path.Combine(libraryRoot, relative.Replace('/', Path.DirectorySeparatorChar)));
                if (!fullPath.StartsWith(libraryRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Library classpath path escapes the libraries directory.");
                if (!File.Exists(fullPath))
                    throw new FileNotFoundException($"A required Minecraft library is missing: {relative}", fullPath);
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
