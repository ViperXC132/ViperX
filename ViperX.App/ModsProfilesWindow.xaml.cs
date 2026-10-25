using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
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
            if (InstallationCombo.Items.Count > 0) InstallationCombo.SelectedIndex = 0;
            _profileItems = (await _profiles.LoadAsync()).ToList();
            ProfilesList.ItemsSource = _profileItems;
            if (ProfilesList.Items.Count > 0) ProfilesList.SelectedIndex = 0;
            StatusText.Text = _installationItems.Count == 0
                ? "Install a vanilla version before creating profiles."
                : $"Loaded {_installationItems.Count} installation(s) and {_profileItems.Count} profile(s).";
            RefreshMods();
        }
        catch (Exception ex) { StatusText.Text = $"Could not load profiles: {ex.Message}"; }
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
        if (ProfilesList.SelectedItem is not LauncherProfile profile) return;
        if (MessageBox.Show(this, $"Remove profile '{profile.Name}'?", "Remove profile",
            MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        _profileItems.RemoveAll(item => item.Id == profile.Id);
        await _profiles.SaveAsync(_profileItems);
        ProfilesList.ItemsSource = null;
        ProfilesList.ItemsSource = _profileItems;
        if (ProfilesList.Items.Count > 0) ProfilesList.SelectedIndex = 0;
        StatusText.Text = $"Removed profile '{profile.Name}'.";
    }

    private void ProfilesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ProfilesList.SelectedItem is not LauncherProfile profile) return;
        SelectedProfileText.Text = profile.Name;
        InstallationCombo.SelectedItem = _installationItems.FirstOrDefault(item => item.Id == profile.InstallationId);
        RefreshMods();
    }

    private void RefreshMods_Click(object sender, RoutedEventArgs e) => RefreshMods();
    private void InstallationCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) => RefreshMods();

    private string? GetModsDirectory()
    {
        if (InstallationCombo.SelectedItem is not MinecraftInstallation installation) return null;
        return Path.Combine(installation.GameDirectory, "mods");
    }

    private void RefreshMods()
    {
        var directory = GetModsDirectory();
        if (directory is null)
        {
            ModFolderText.Text = "No installation available. Install a vanilla version first.";
            ModsList.ItemsSource = null;
            return;
        }
        ModFolderText.Text = directory;
        try
        {
            Directory.CreateDirectory(directory);
            var mods = Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly)
                .Where(path => path.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) ||
                               path.EndsWith(".jar.disabled", StringComparison.OrdinalIgnoreCase))
                .Select(path => new ModFileItem(Path.GetFileName(path),
                    path.EndsWith(".jar.disabled", StringComparison.OrdinalIgnoreCase)))
                .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ToArray();
            ModsList.ItemsSource = mods;
            StatusText.Text = mods.Length == 0 ? "No mod JARs found. Use Install mod to add a local file." :
                $"Found {mods.Length} mod file(s); disabled mods are marked.";
        }
        catch (Exception ex) { StatusText.Text = $"Could not read mods folder: {ex.Message}"; }
    }

    private void InstallMod_Click(object sender, RoutedEventArgs e)
    {
        var directory = GetModsDirectory();
        if (directory is null)
        {
            MessageBox.Show(this, "Install a Minecraft version first.", "No installation", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var dialog = new OpenFileDialog
        {
            Title = "Select mod JAR file(s)",
            Filter = "Minecraft mod JAR (*.jar)|*.jar",
            Multiselect = true,
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            Directory.CreateDirectory(directory);
            var copied = 0;
            foreach (var source in dialog.FileNames)
            {
                var destination = Path.Combine(directory, Path.GetFileName(source));
                if (File.Exists(destination) &&
                    MessageBox.Show(this, $"'{Path.GetFileName(source)}' already exists. Replace it?", "Replace mod",
                        MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                    continue;
                File.Copy(source, destination, overwrite: true);
                copied++;
            }
            RefreshMods();
            StatusText.Text = $"Installed/copied {copied} mod file(s). Confirm loader and Minecraft version compatibility before launching.";
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Mod install failed", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void ToggleMod_Click(object sender, RoutedEventArgs e)
    {
        if (ModsList.SelectedItem is not ModFileItem mod || GetModsDirectory() is not { } directory) return;
        try
        {
            var source = Path.Combine(directory, mod.Name);
            var destination = mod.IsDisabled ? source[..^".disabled".Length] : source + ".disabled";
            if (!File.Exists(source)) throw new FileNotFoundException("The selected mod file no longer exists.", source);
            if (File.Exists(destination)) throw new IOException($"A file named '{Path.GetFileName(destination)}' already exists.");
            File.Move(source, destination);
            RefreshMods();
            StatusText.Text = mod.IsDisabled ? $"Enabled '{Path.GetFileName(destination)}'." : $"Disabled '{mod.Name}'.";
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Could not change mod state", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void RemoveMod_Click(object sender, RoutedEventArgs e)
    {
        if (ModsList.SelectedItem is not ModFileItem mod || GetModsDirectory() is not { } directory) return;
        if (MessageBox.Show(this, $"Permanently delete '{mod.Name}' from this instance?", "Remove mod",
            MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try
        {
            var path = Path.Combine(directory, mod.Name);
            if (File.Exists(path)) File.Delete(path);
            RefreshMods();
            StatusText.Text = $"Removed '{mod.Name}'.";
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Could not remove mod", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void OpenModsFolder_Click(object sender, RoutedEventArgs e)
    {
        var directory = GetModsDirectory();
        if (directory is null) return;
        try
        {
            Directory.CreateDirectory(directory);
            Process.Start(new ProcessStartInfo(directory) { UseShellExecute = true });
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Could not open mods folder", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private sealed record ModFileItem(string Name, bool IsDisabled)
    {
        public string DisplayName => IsDisabled ? $"{Name}  •  DISABLED" : Name;
    }
}
