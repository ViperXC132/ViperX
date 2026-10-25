using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using ViperX.App.Services;

namespace ViperX.App;

public partial class VersionsWindow : Window
{
    private readonly MinecraftVersionManifestService _manifestService = new();
    private readonly VanillaMinecraftInstaller _installer = new();
    private CancellationTokenSource? _installCancellation;
    private IReadOnlyList<MinecraftVersionInfo> _allVersions = Array.Empty<MinecraftVersionInfo>();

    public VersionsWindow()
    {
        InitializeComponent();
        Loaded += async (_, _) => await LoadVersionsAsync();
    }

    private async Task LoadVersionsAsync(bool refresh = false)
    {
        RefreshButton.IsEnabled = false;
        SearchBox.IsEnabled = false;
        StatusText.Text = refresh ? "Refreshing official version manifest…" : "Loading official version manifest…";
        CountText.Text = "Fetching version data…";

        try
        {
            _allVersions = await _manifestService.GetVersionsAsync(refresh);
            ApplyFilter();
            StatusText.Text = $"Loaded {_allVersions.Count:N0} official version entries.";
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Version lookup was cancelled.";
        }
        catch (HttpRequestException ex)
        {
            StatusText.Text = "Could not reach Mojang. Check your internet connection and retry.";
            CountText.Text = "Network request failed.";
            MessageBox.Show(this, $"ViperX could not download the official version manifest.\n\n{ex.Message}",
                "Version catalog unavailable", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            StatusText.Text = "The version manifest could not be read.";
            CountText.Text = "Invalid or unavailable manifest.";
            MessageBox.Show(this, ex.Message, "Version catalog error", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            RefreshButton.IsEnabled = true;
            SearchBox.IsEnabled = true;
        }
    }

    private void ApplyFilter()
    {
        var query = SearchBox.Text.Trim();
        var filtered = string.IsNullOrEmpty(query)
            ? _allVersions
            : _allVersions.Where(v => v.Id.Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();

        VersionsList.ItemsSource = filtered;
        CountText.Text = $"{filtered.Count:N0} versions shown";
        if (VersionsList.Items.Count > 0 && VersionsList.SelectedIndex < 0)
            VersionsList.SelectedIndex = 0;
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (IsInitialized)
            ApplyFilter();
    }

    private void VersionsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (VersionsList.SelectedItem is not MinecraftVersionInfo version)
            return;

        SelectedVersionText.Text = version.Id;
        SelectedTypeText.Text = version.Type.ToUpperInvariant();
        ReleaseDateText.Text = version.ReleaseTime?.ToLocalTime().ToString("dd MMM yyyy") ?? "Not provided";
        ManifestUrlText.Text = version.Url;
        InstallButton.IsEnabled = version.IsRelease || version.IsSnapshot;
        InstallFabricButton.IsEnabled = File.Exists(Path.Combine(ViperXPaths.InstancesDirectory, version.Id, "versions", version.Id, "installed.json"));
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) =>
        await LoadVersionsAsync(refresh: true);

    private async void Install_Click(object sender, RoutedEventArgs e)
    {
        if (VersionsList.SelectedItem is not MinecraftVersionInfo version || InstallButton.IsEnabled == false)
            return;

        _installCancellation?.Cancel();
        _installCancellation?.Dispose();
        _installCancellation = new CancellationTokenSource();
        var token = _installCancellation.Token;
        InstallButton.IsEnabled = false;
        RefreshButton.IsEnabled = false;
        SearchBox.IsEnabled = false;
        try
        {
            var progress = new Progress<(string Stage, double Progress)>(item =>
            {
                if (!token.IsCancellationRequested)
                    StatusText.Text = item.Progress > 0
                        ? $"{item.Stage} — {item.Progress:P0}"
                        : item.Stage;
            });
            await _installer.InstallAsync(version.Id, progress, token);
            InstallFabricButton.IsEnabled = true;
            StatusText.Text = $"Minecraft {version.Id} installed. Java and sign-in are still required to launch it.";
            MessageBox.Show(this, $"Minecraft {version.Id} files were downloaded and verified where Mojang supplied checksums.\n\nYou can now configure Java and sign in before launching.", "Installation complete", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Installation cancelled.";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Installation failed. Review the error and retry.";
            MessageBox.Show(this, ex.Message, "Minecraft installation failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            InstallButton.IsEnabled = VersionsList.SelectedItem is MinecraftVersionInfo selected && (selected.IsRelease || selected.IsSnapshot);
            RefreshButton.IsEnabled = true;
            SearchBox.IsEnabled = true;
        }
    }

    private async void InstallFabric_Click(object sender, RoutedEventArgs e)
    {
        if (VersionsList.SelectedItem is not MinecraftVersionInfo version || !InstallFabricButton.IsEnabled)
            return;
        InstallFabricButton.IsEnabled = false;
        InstallButton.IsEnabled = false;
        RefreshButton.IsEnabled = false;
        SearchBox.IsEnabled = false;
        StatusText.Text = "Finding the latest stable Fabric loader…";
        try
        {
            var loaderVersion = await new FabricLoaderService().InstallLatestStableAsync(version.Id);
            StatusText.Text = $"Fabric {loaderVersion} installed for Minecraft {version.Id}.";
            MessageBox.Show(this, $"Fabric {loaderVersion} is ready for Minecraft {version.Id}. Select Play to launch it after signing in and configuring Java.", "Fabric installed",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            StatusText.Text = "Fabric installation failed. Review the error and retry.";
            MessageBox.Show(this, ex.Message, "Fabric installation failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            InstallButton.IsEnabled = VersionsList.SelectedItem is MinecraftVersionInfo selected && (selected.IsRelease || selected.IsSnapshot);
            InstallFabricButton.IsEnabled = VersionsList.SelectedItem is MinecraftVersionInfo current &&
                File.Exists(Path.Combine(ViperXPaths.InstancesDirectory, current.Id, "versions", current.Id, "installed.json"));
            RefreshButton.IsEnabled = true;
            SearchBox.IsEnabled = true;
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        _installCancellation?.Cancel();
        Close();
    }
}
