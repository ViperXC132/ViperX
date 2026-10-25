using System.Windows;

namespace ViperX.App;

public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    private void SetPage(string eyebrow, string title, string message)
    {
        PageEyebrow.Text = eyebrow;
        PageTitle.Text = title;
        MessageBox.Show(message, "ViperX", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void Home_Click(object sender, RoutedEventArgs e)
    {
        PageEyebrow.Text = "YOUR NEXT ADVENTURE";
        PageTitle.Text = "Welcome back.";
    }

    private void Play_Click(object sender, RoutedEventArgs e) => SetPage("PLAY", "Ready to launch?", "The launcher interface is set up, but the Minecraft installation and launch pipeline has not been implemented yet. Next: version metadata, downloads, Java validation, and game process launch.");
    private void Versions_Click(object sender, RoutedEventArgs e) => SetPage("GAME LIBRARY", "Installations", "Version management is the next implementation step. This page will install and configure official Minecraft versions.");
    private void Mods_Click(object sender, RoutedEventArgs e) => SetPage("CUSTOMIZE", "Mods & modpacks", "Mod discovery, loader installation, and modpack import are not connected yet. They will be implemented after the installation engine.");
    private void Accounts_Click(object sender, RoutedEventArgs e) => SetPage("IDENTITY", "Accounts", "Microsoft authentication must use the supported sign-in flow. Account storage and authentication are not implemented yet.");
    private void Settings_Click(object sender, RoutedEventArgs e) => SetPage("PREFERENCES", "Settings", "Java path, memory allocation, game directory, resolution, and theme persistence will be added in the settings stage.");
}
