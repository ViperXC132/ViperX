using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using ViperX.App.Models;
using ViperX.App.Services;

namespace ViperX.App;

public partial class ModsProfilesWindow : Window
{
    private readonly InstallationStore _installations = new();
    private readonly ProfileStore _profiles = new();
    private IReadOnlyList<MinecraftInstallation> _installationItems = Array.Empty<MinecraftInstallation>();
    private List<LauncherProfile> _profileItems = new();

    public ModsProfilesWindow()
    {
        InitializeComponent();
        Loaded += async (_, _) => await LoadAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            _installationItems = await _installations.LoadAsync();
            InstallationCombo.ItemsSource = _installationItems;
            if (InstallationCombo.Items.Count > 0)
                InstallationCombo.SelectedIndex = 0;
            _profileItems = (await _profiles.LoadAsync()).ToList();
            ProfilesList.ItemsSource = null;
            ProfilesList.ItemsSource = _profileItems;
            if (ProfilesList.Items.Count > 0)
                ProfilesList.SelectedIndex = 0;
            StatusText.Text = _installationItems.Count == 0
                ? "Install a vanilla version before creating profiles."
                : $"Loaded {_installationItems.Count} installation(s) and {_profileItems.Count} profile(s).";
            RefreshMods();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Could not load profiles: {ex.Message}";
        }
    }

    private async void CreateProfile_Click(object sender, RoutedEventArgs e)
    {
        var name = ProfileNameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name) || InstallationCombo.SelectedItem is not MinecraftInstallation installation)
        {
            MessageBox.Show(this, "Enter a profile name and install a Minecraft version first.", "Profile details required",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (_profileItems.Any(profile => string.Equals(profile.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show(this, "A profile with that name already exists.", "Duplicate profile",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _profileItems.Add(new LauncherProfile { Name = name, InstallationId = installation.Id });
        await _profiles.SaveAsync(_profileItems);
        ProfilesList.ItemsSource = null;
        ProfilesList.ItemsSource = _profileItems;
        ProfilesList.SelectedItem = _profileItems.Last();
        ProfileNameBox.Clear();
        StatusText.Text = $"Created profile '{name}'.";
    }

    private async void RemoveProfile_Click(object sender, RoutedEventArgs e)
    {
        if (ProfilesList.SelectedItem is not LauncherProfile profile)
            return;
        var answer = MessageBox.Show(this, $"Remove profile '{profile.Name}'?", "Remove profile",
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes)
            return;
        _profileItems.RemoveAll(item => item.Id == profile.Id);
        await _profiles.SaveAsync(_profileItems);
        ProfilesList.ItemsSource = null;
        ProfilesList.ItemsSource = _profileItems;
        if (ProfilesList.Items.Count > 0)
            ProfilesList.SelectedIndex = 0;
        StatusText.Text = $"Removed profile '{profile.Name}'.";
    }

    private void ProfilesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ProfilesList.SelectedItem is not LauncherProfile profile)
            return;
        SelectedProfileText.Text = profile.Name;
        InstallationCombo.SelectedItem = _installationItems.FirstOrDefault(item => item.Id == profile.InstallationId);
        RefreshMods();
    }

    private void RefreshMods_Click(object sender, RoutedEventArgs e) => RefreshMods();

    private void RefreshMods()
    {
        if (InstallationCombo.SelectedItem is not MinecraftInstallation installation)
        {
            ModFolderText.Text = "No installation available. Install a vanilla version first.";
            ModsList.ItemsSource = null;
            return;
        }
        var modsDirectory = Path.Combine(installation.GameDirectory, "mods");
        ModFolderText.Text = modsDirectory;
        var mods = Directory.Exists(modsDirectory)
            ? Directory.EnumerateFiles(modsDirectory, "*.jar", SearchOption.TopDirectoryOnly)
                .Select(Path.GetFileName).Where(name => name is not null).OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray()
            : Array.Empty<string>();
        ModsList.ItemsSource = mods;
        StatusText.Text = mods.Length == 0 ? "No .jar mods found in this installation's mods folder." : $"Found {mods.Length} mod file(s).";
    }

    private void OpenModsFolder_Click(object sender, RoutedEventArgs e)
    {
        if (InstallationCombo.SelectedItem is not MinecraftInstallation installation)
            return;
        var directory = Path.Combine(installation.GameDirectory, "mods");
        Directory.CreateDirectory(directory);
        Process.Start(new ProcessStartInfo(directory) { UseShellExecute = true });
        StatusText.Text = "Opened mods folder. Only use mods compatible with the installed loader and game version.";
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
