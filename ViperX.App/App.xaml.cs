using System.Windows;
using ViperX.App.Services;

namespace ViperX.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        try
        {
            ViperXPaths.EnsureBaseDirectories();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"ViperX could not create its local data folders. Some features may not work until folder access is restored.\n\n{ex.Message}",
                "ViperX — Storage warning",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        base.OnStartup(e);
    }
}
