using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using ViperX.App.Services;

namespace ViperX.App;

public partial class VersionsWindow : Window
{
    private readonly MinecraftVersionManifestService _manifestService = new();
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
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) =>
        await LoadVersionsAsync(refresh: true);

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
