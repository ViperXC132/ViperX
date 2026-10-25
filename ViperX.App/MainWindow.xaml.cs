using System.Diagnostics;
using System.Windows;
using ViperX.App.Services;

namespace ViperX.App;

public partial class MainWindow : Window
{
    private Process? _gameProcess;
    public MainWindow()
    {
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            try
            {
                var settings = await new LauncherSettingsService().LoadAsync();
                Width = settings.WindowWidth;
                Height = settings.WindowHeight;
            }
            catch
            {
                // Keep the usable XAML defaults if settings cannot be read.
            }
        };
    }

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

            var launcher = new MinecraftGameLauncher();
            var requiredJavaMajor = await launcher.GetRequiredJavaMajorAsync(installation.GameVersion);
            var launcherSettings = await new LauncherSettingsService().LoadAsync();
            var javaService = new JavaRuntimeService();
            var configuredRuntime = string.IsNullOrWhiteSpace(launcherSettings.JavaExecutablePath)
                ? null
                : await javaService.ProbeAsync(launcherSettings.JavaExecutablePath);
            var runtimes = configuredRuntime is null
                ? await javaService.FindInstalledAsync()
                : new[] { configuredRuntime! };
            var runtime = runtimes
                .Where(item => item.MajorVersion >= requiredJavaMajor)
                .OrderBy(item => item.MajorVersion == requiredJavaMajor ? 0 : 1)
                .ThenBy(item => Math.Abs(item.MajorVersion - requiredJavaMajor))
                .FirstOrDefault();
            if (runtime is null)
                throw new InvalidOperationException($"Minecraft {installation.GameVersion} requires Java {requiredJavaMajor} or later, but no compatible Java runtime was found.");

            PageTitle.Text = $"Launching Minecraft {installation.GameVersion}…";
            var process = await launcher.LaunchAsync(new ViperX.App.Models.MinecraftLaunchRequest
            {
                VersionId = installation.GameVersion,
                Loader = installation.Loader,
                LoaderVersion = installation.LoaderVersion,
                Username = session.Account.Username,
                PlayerUuid = session.Account.Uuid,
                AccessToken = session.MinecraftAccessToken,
                JavaExecutablePath = runtime.ExecutablePath,
                MemoryMegabytes = installation.MemoryMegabytes == 4096
                    ? launcherSettings.DefaultMemoryMegabytes
                    : installation.MemoryMegabytes,
                GameDirectory = launcherSettings.DefaultGameDirectory ?? installation.GameDirectory
            });
            _gameProcess?.Dispose();
            _gameProcess = process;
            process.Exited += (_, _) => Dispatcher.InvokeAsync(() =>
            {
                PageEyebrow.Text = "PLAY";
                PageTitle.Text = $"Minecraft {installation.GameVersion} exited (code {process.ExitCode}).";
                if (!launcherSettings.KeepLauncherOpenAfterGameExit)
                    Close();
            });
            if (process.HasExited)
                PageTitle.Text = $"Minecraft {installation.GameVersion} exited unexpectedly.";

            installation = installation with { LastPlayedAt = DateTimeOffset.UtcNow };
            await store.SaveAsync(installations.Select(item => item.Id == installation.Id ? installation : item));
            PageEyebrow.Text = "PLAY";
            PageTitle.Text = $"Minecraft {installation.GameVersion} launched.";
            MessageBox.Show(this, $"Minecraft {installation.GameVersion} was started with Java {runtime.Version}.", "ViperX",
                MessageBoxButton.OK, MessageBoxImage.Information);
            if (launcherSettings.CloseLauncherOnGameStart)
                Close();
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

    private void Mods_Click(object sender, RoutedEventArgs e)
    {
        var window = new ModsProfilesWindow { Owner = this };
        window.ShowDialog();
    }

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

    private void Diagnostics_Click(object sender, RoutedEventArgs e)
    {
        var window = new DiagnosticsWindow { Owner = this };
        window.ShowDialog();
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        var window = new SettingsWindow { Owner = this };
        if (window.ShowDialog() == true)
        {
            PageEyebrow.Text = "PREFERENCES";
            PageTitle.Text = "Settings saved.";
        }
    }
}
