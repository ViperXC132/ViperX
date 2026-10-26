using System.Windows;
using ViperX.App.Models;
using ViperX.App.Services;

namespace ViperX.App;

public partial class JavaStoreWindow : Window
{
    private readonly JavaRuntimeStoreService _store = new();
    private readonly LauncherSettingsService _settingsService = new();

    public JavaStoreWindow()
    {
        InitializeComponent();
        JavaVersionSelector.ItemsSource = _store.AvailablePackages;
        JavaVersionSelector.SelectedValue = 21;
        Loaded += async (_, _) => await RefreshRuntimesAsync();
    }

    private async Task RefreshRuntimesAsync()
    {
        try
        {
            StatusText.Text = "Detecting installed Java runtimes…";
            var system = await new JavaRuntimeService().FindInstalledAsync();
            var managed = await _store.FindManagedAsync();
            RuntimeList.ItemsSource = managed.Concat(system)
                .GroupBy(x => x.ExecutablePath, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .OrderByDescending(x => x.MajorVersion)
                .ToArray();
            StatusText.Text = $"{RuntimeList.Items.Count} compatible Java installation(s) detected. Managed runtimes stay inside the ViperX data folder.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Could not scan Java installations: {ex.Message}";
        }
    }

    private async void Install_Click(object sender, RoutedEventArgs e)
    {
        if (JavaVersionSelector.SelectedValue is not int major)
        {
            StatusText.Text = "Choose a Java version first.";
            return;
        }

        InstallButton.IsEnabled = false;
        JavaVersionSelector.IsEnabled = false;
        InstallProgress.Value = 0;
        InstallProgress.Visibility = Visibility.Visible;
        StatusText.Text = $"Preparing Java {major} download…";
        var progress = new Progress<double>(value =>
        {
            InstallProgress.Value = value;
            StatusText.Text = value < 85
                ? $"Downloading Java {major}… {value:0}%"
                : $"Verifying and installing Java {major}…";
        });

        try
        {
            var executable = await _store.InstallAsync(major, progress);
            var settings = await _settingsService.LoadAsync();
            await _settingsService.SaveAsync(settings with { JavaExecutablePath = executable });
            StatusText.Text = $"Java {major} installed and selected as the preferred runtime.";
            await RefreshRuntimesAsync();
            MessageBox.Show(this, $"Java {major} is installed and saved as ViperX's preferred Java runtime. The launcher will still choose a compatible version for each Minecraft installation.",
                "Java installed", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Java installation failed: {ex.Message}";
            MessageBox.Show(this, ex.Message, "Java installation failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            InstallButton.IsEnabled = true;
            JavaVersionSelector.IsEnabled = true;
            InstallProgress.Visibility = Visibility.Collapsed;
        }
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshRuntimesAsync();

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
