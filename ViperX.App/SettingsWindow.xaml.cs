using Microsoft.Win32;
using System.Windows;
using ViperX.App.Models;
using ViperX.App.Services;

namespace ViperX.App;

public partial class SettingsWindow : Window
{
    private readonly LauncherSettingsService _settingsService = new();
    private LauncherSettings _settings = new();

    public SettingsWindow()
    {
        InitializeComponent();
        Loaded += async (_, _) => await LoadSettingsAsync();
    }

    private async Task LoadSettingsAsync()
    {
        try
        {
            _settings = await _settingsService.LoadAsync();
            JavaPathBox.Text = _settings.JavaExecutablePath ?? "";
            MemoryBox.Text = _settings.DefaultMemoryMegabytes.ToString();
            GameDirectoryBox.Text = _settings.DefaultGameDirectory ?? "";
            WidthBox.Text = _settings.WindowWidth.ToString();
            HeightBox.Text = _settings.WindowHeight.ToString();
            CloseOnLaunchBox.IsChecked = _settings.CloseLauncherOnGameStart;
            KeepOpenBox.IsChecked = _settings.KeepLauncherOpenAfterGameExit;
            StatusText.Text = "Settings loaded.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Could not load settings: {ex.Message}";
        }
    }

    private async void DetectJava_Click(object sender, RoutedEventArgs e)
    {
        StatusText.Text = "Searching PATH and JAVA_HOME…";
        try
        {
            var runtimes = await new JavaRuntimeService().FindInstalledAsync();
            if (runtimes.Count == 0)
            {
                StatusText.Text = "No Java runtime was found in PATH or JAVA_HOME.";
                return;
            }
            JavaPathBox.Text = runtimes[0].ExecutablePath;
            StatusText.Text = $"Found Java {runtimes[0].Version}.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Java detection failed: {ex.Message}";
        }
    }

    private void BrowseJava_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select Java executable",
            Filter = "Java executable (java.exe)|java.exe|All files (*.*)|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) == true)
            JavaPathBox.Text = dialog.FileName;
    }

    private void BrowseGameDirectory_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Choose default game directory", Multiselect = false };
        if (dialog.ShowDialog(this) == true)
            GameDirectoryBox.Text = dialog.FolderName;
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(MemoryBox.Text, out var memory) ||
            !int.TryParse(WidthBox.Text, out var width) ||
            !int.TryParse(HeightBox.Text, out var height))
        {
            MessageBox.Show(this, "Memory, width, and height must be whole numbers.", "Invalid settings",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var settings = new LauncherSettings
        {
            JavaExecutablePath = string.IsNullOrWhiteSpace(JavaPathBox.Text) ? null : JavaPathBox.Text.Trim(),
            DefaultMemoryMegabytes = memory,
            DefaultGameDirectory = string.IsNullOrWhiteSpace(GameDirectoryBox.Text) ? null : GameDirectoryBox.Text.Trim(),
            WindowWidth = width,
            WindowHeight = height,
            CloseLauncherOnGameStart = CloseOnLaunchBox.IsChecked == true,
            KeepLauncherOpenAfterGameExit = KeepOpenBox.IsChecked == true
        };
        var errors = settings.Validate();
        if (errors.Count > 0)
        {
            MessageBox.Show(this, string.Join(Environment.NewLine, errors), "Invalid settings",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            await _settingsService.SaveAsync(settings);
            _settings = settings;
            StatusText.Text = "Settings saved.";
            DialogResult = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Could not save settings", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
