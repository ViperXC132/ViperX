using System.Diagnostics;
using System.Text.RegularExpressions;

namespace ViperX.App.Services;

public sealed record JavaRuntimeInfo(string ExecutablePath, string Version, int MajorVersion);

/// <summary>Finds and validates a Java executable without downloading or running untrusted installers.</summary>
public sealed class JavaRuntimeService
{
    private static readonly Regex VersionPattern = new(@"version ""(?<version>[^""]+)""", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public async Task<IReadOnlyList<JavaRuntimeInfo>> FindInstalledAsync(CancellationToken cancellationToken = default)
    {
        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pathValue = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var folder in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var candidate = Path.Combine(folder.Trim('"'), OperatingSystem.IsWindows() ? "java.exe" : "java");
            if (File.Exists(candidate))
                candidates.Add(candidate);
        }

        var javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
        if (!string.IsNullOrWhiteSpace(javaHome))
        {
            var candidate = Path.Combine(javaHome, "bin", OperatingSystem.IsWindows() ? "java.exe" : "java");
            if (File.Exists(candidate))
                candidates.Add(candidate);
        }

        var results = new List<JavaRuntimeInfo>();
        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var info = await ProbeAsync(candidate, cancellationToken);
            if (info is not null && results.All(existing => !string.Equals(existing.ExecutablePath, info.ExecutablePath, StringComparison.OrdinalIgnoreCase)))
                results.Add(info);
        }

        return results.OrderByDescending(item => item.MajorVersion).ToArray();
    }

    public async Task<JavaRuntimeInfo?> ProbeAsync(string executablePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
            return null;

        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = executablePath,
                    Arguments = "-version",
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                }
            };
            if (!process.Start())
                return null;

            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
            var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            await process.WaitForExitAsync(timeout.Token);
            var output = (await stderrTask) + Environment.NewLine + (await stdoutTask);
            var match = VersionPattern.Match(output);
            if (!match.Success)
                return null;

            var version = match.Groups["version"].Value;
            var majorToken = version.StartsWith("1.", StringComparison.Ordinal) ? version[2..].Split('.')[0] : version.Split('.')[0];
            return int.TryParse(majorToken, out var major)
                ? new JavaRuntimeInfo(Path.GetFullPath(executablePath), version, major)
                : null;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }
}
