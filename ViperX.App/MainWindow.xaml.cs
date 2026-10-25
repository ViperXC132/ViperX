using System.Windows;

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

    private void Play_Click(object sender, RoutedEventArgs e) =>
        SetPage("PLAY", "Ready to launch?", "The installation and launch pipeline is not implemented yet. Phase 2 will add game downloads, Java validation, launch argument generation, and process launch.");

    private void Versions_Click(object sender, RoutedEventArgs e)
    {
        var window = new VersionsWindow { Owner = this };
        window.ShowDialog();
    }

    private void Mods_Click(object sender, RoutedEventArgs e) =>
        SetPage("CUSTOMIZE", "Mods & modpacks", "Mod discovery, loader installation, and modpack import will be added after the installation engine.");

    private void Accounts_Click(object sender, RoutedEventArgs e) =>
        SetPage("IDENTITY", "Accounts", "Microsoft authentication will use the supported sign-in flow. Account storage and authentication are not implemented yet.");

    private void Settings_Click(object sender, RoutedEventArgs e) =>
        SetPage("PREFERENCES", "Settings", "Java path, memory allocation, game directory, resolution, and persistent settings will be added in later phases.");
}
