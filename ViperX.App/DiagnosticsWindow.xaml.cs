using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using ViperX.App.Models;
using ViperX.App.Services;

namespace ViperX.App;

public partial class DiagnosticsWindow : Window
{
    private readonly InstallationStore _store = new();
    private IReadOnlyList<MinecraftInstallation> _installations = Array.Empty<MinecraftInstallation>();
    private IReadOnlyList<LogFileItem> _files = Array.Empty<LogFileItem>();

    public DiagnosticsWindow()
    {
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            try
            {
                _installations = await _store.LoadAsync();
                InstallationCombo.ItemsSource = _installations;
                if (InstallationCombo.Items.Count > 0) InstallationCombo.SelectedIndex = 0;
                else StatusText.Text = "No installation metadata found. Install Minecraft first.";
            }
            catch (Exception ex) { StatusText.Text = $"Could not load installations: {ex.Message}"; }
        };
    }

    private void InstallationCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) => RefreshFiles();

    private void Refresh_Click(object sender, RoutedEventArgs e) => RefreshFiles();

    private void RefreshFiles()
    {
        if (InstallationCombo.SelectedItem is not MinecraftInstallation installation)
        {
            FilesList.ItemsSource = null;
            ContentBox.Clear();
            return;
        }

        try
        {
            var root = installation.GameDirectory;
            var files = new List<LogFileItem>();
            var latest = Path.Combine(root, "logs", "latest.log");
            if (File.Exists(latest)) files.Add(new LogFileItem("logs/latest.log", latest, File.GetLastWriteTimeUtc(latest)));
            AddFiles(files, Path.Combine(root, "crash-reports"), "crash-reports");
            AddFiles(files, Path.Combine(root, "logs"), "logs", "*.log");
            _files = files.OrderByDescending(item => item.LastWriteTimeUtc).ToArray();
            FilesList.ItemsSource = _files;
            if (FilesList.Items.Count > 0) FilesList.SelectedIndex = 0;
            else ContentBox.Text = "No logs or crash reports found yet. Launch the game once, or select Open game folder to inspect the instance.";
            StatusText.Text = $"Found {_files.Count} readable log/report file(s) in {root}.";
        }
        catch (Exception ex) { StatusText.Text = $"Could not scan logs: {ex.Message}"; }
    }

    private static void AddFiles(List<LogFileItem> results, string directory, string label, string pattern = "*.txt")
    {
        if (!Directory.Exists(directory)) return;
        foreach (var path in Directory.EnumerateFiles(directory, pattern, SearchOption.TopDirectoryOnly))
        {
            var display = $"{label}/{Path.GetFileName(path)}";
            if (results.Any(item => string.Equals(item.FullPath, path, StringComparison.OrdinalIgnoreCase))) continue;
            results.Add(new LogFileItem(display, path, File.GetLastWriteTimeUtc(path)));
        }
    }

    private void FilesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (FilesList.SelectedItem is not LogFileItem item) return;
        try
        {
            var info = new FileInfo(item.FullPath);
            const long maxBytes = 2 * 1024 * 1024;
            using var stream = new FileStream(item.FullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length > maxBytes)
            {
                stream.Seek(-maxBytes, SeekOrigin.End);
                using var reader = new StreamReader(stream);
                ContentBox.Text = "[Showing final 2 MB of this file]\n\n" + reader.ReadToEnd();
            }
            else
            {
                using var reader = new StreamReader(stream);
                ContentBox.Text = reader.ReadToEnd();
            }
            StatusText.Text = $"{item.DisplayName} • {info.Length:N0} bytes • {info.LastWriteTime:g}";
        }
        catch (Exception ex) { ContentBox.Text = $"Could not read this file.\n{ex.Message}"; }
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (InstallationCombo.SelectedItem is not MinecraftInstallation installation) return;
        try { Process.Start(new ProcessStartInfo(installation.GameDirectory) { UseShellExecute = true }); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Could not open game folder", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private sealed record LogFileItem(string DisplayName, string FullPath, DateTime LastWriteTimeUtc);
}
