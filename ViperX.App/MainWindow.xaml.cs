using System.Diagnostics;
using System.Windows;
using ViperX.App.Services;

namespace ViperX.App;

public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    private void SetPage(string eyebrow, string title, string message)
    {
        PageEyebrow.Text = eyebrow;
        PageTitle.Text = title;
        MessageBox.Show(this, message, "ViperX", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void Home_Click(object sender, RoutedEventArgs e)
    {
        PageEyebrow.Text = "YOUR NEXT ADVENTURE";
        PageTitle.Text = "Welcome back.";
    }

    private async void Play_Click(object sender, RoutedEventArgs e)
    {
        var session = ActiveMinecraftSession.Current;
        if (session is null)
        {
            MessageBox.Show(this, "Sign in with a Microsoft account before launching Minecraft.", "Sign-in required",
                MessageBoxButton.OK, MessageBoxImage.Information);
            new AccountsWindow { Owner = this }.ShowDialog();
            session = ActiveMinecraftSession.Current;
            if (session is null)
                return;
        }

        try
        {
            PageEyebrow.Text = "PLAY";
            PageTitle.Text = "Preparing your world…";
            var store = new InstallationStore();
            var installations = await store.LoadAsync();
            var installation = installations
                .OrderByDescending(item => item.LastPlayedAt ?? DateTimeOffset.MinValue)
                .FirstOrDefault();
            if (installation is null)
                throw new InvalidOperationException("No installed Minecraft version was found. Open Versions and install a vanilla release first.");

            var javaService = new JavaRuntimeService();
            var runtimes = await javaService.FindInstalledAsync();
            var runtime = runtimes.OrderByDescending(item => item.MajorVersion).FirstOrDefault();
            if (runtime is null)
                throw new InvalidOperationException("Java was not found. Install a compatible Java runtime or configure Java in Settings.");

            PageTitle.Text = $"Launching Minecraft {installation.GameVersion}…";
            var launcher = new MinecraftGameLauncher();
            using var process = await launcher.LaunchAsync(new ViperX.App.Models.MinecraftLaunchRequest
            {
                VersionId = installation.GameVersion,
                Username = session.Account.Username,
                PlayerUuid = session.Account.Uuid,
                AccessToken = session.MinecraftAccessToken,
                JavaExecutablePath = runtime.ExecutablePath,
                MemoryMegabytes = installation.MemoryMegabytes,
                GameDirectory = installation.GameDirectory
            });
            installation = installation with { LastPlayedAt = DateTimeOffset.UtcNow };
            await store.SaveAsync(installations.Select(item => item.Id == installation.Id ? installation : item));
            PageEyebrow.Text = "PLAY";
            PageTitle.Text = $"Minecraft {installation.GameVersion} launched.";
            MessageBox.Show(this, $"Minecraft {installation.GameVersion} was started with Java {runtime.Version}.", "ViperX",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            PageEyebrow.Text = "PLAY";
            PageTitle.Text = "Could not launch Minecraft.";
            MessageBox.Show(this, ex.Message, "Launch failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Versions_Click(object sender, RoutedEventArgs e)
    {
        var window = new VersionsWindow { Owner = this };
        window.ShowDialog();
    }

    private void Mods_Click(object sender, RoutedEventArgs e) =>
        SetPage("CUSTOMIZE", "Mods & modpacks", "Mod discovery, loader installation, and modpack import will be added after the installation engine.");

    private void Accounts_Click(object sender, RoutedEventArgs e)
    {
        var window = new AccountsWindow { Owner = this };
        window.ShowDialog();
        if (ActiveMinecraftSession.Current is { } session)
        {
            PageEyebrow.Text = "IDENTITY";
            PageTitle.Text = $"Signed in as {session.Account.Username}.";
        }
    }

    private void Settings_Click(object sender, RoutedEventArgs e) =>
        SetPage("PREFERENCES", "Settings", "Java path, memory allocation, game directory, resolution, and persistent settings will be added in later phases.");
}
