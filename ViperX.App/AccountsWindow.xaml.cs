using System.Diagnostics;
using System.Windows;
using ViperX.App.Services;

namespace ViperX.App;

public partial class AccountsWindow : Window
{
    private CancellationTokenSource? _signInCancellation;

    public AccountsWindow()
    {
        InitializeComponent();
        UpdateAccountView();
    }

    private async void SignIn_Click(object sender, RoutedEventArgs e)
    {
        var clientId = ClientIdBox.Text.Trim();
        if (!Guid.TryParse(clientId, out _))
        {
            MessageBox.Show(this, "Enter a valid Microsoft public-client application ID (GUID).", "Application ID required",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _signInCancellation?.Cancel();
        _signInCancellation?.Dispose();
        _signInCancellation = new CancellationTokenSource();
        var token = _signInCancellation.Token;
        SignInButton.IsEnabled = false;
        ClientIdBox.IsEnabled = false;
        DeviceCodeText.Text = "";
        try
        {
            var service = new MicrosoftMinecraftAuthService(clientId);
            var deviceCode = await service.BeginDeviceCodeAsync(token);
            DeviceCodeText.Text = deviceCode.UserCode;
            StatusText.Text = $"{deviceCode.Message}\nExpires at {deviceCode.ExpiresAt.ToLocalTime():t}.";
            try
            {
                Process.Start(new ProcessStartInfo(deviceCode.VerificationUri.AbsoluteUri) { UseShellExecute = true });
            }
            catch (Exception)
            {
                StatusText.Text += $" Open {deviceCode.VerificationUri} manually.";
            }

            var progress = new Progress<string>(message => StatusText.Text = message);
            var session = await service.CompleteSignInAsync(deviceCode, progress, token);
            ActiveMinecraftSession.Set(session);
            UpdateAccountView();
            StatusText.Text = "Signed in. The session is in memory only and will end when ViperX closes.";
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Sign-in cancelled.";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Sign-in failed.";
            MessageBox.Show(this, ex.Message, "Microsoft sign-in failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            SignInButton.IsEnabled = true;
            ClientIdBox.IsEnabled = true;
        }
    }

    private void SignOut_Click(object sender, RoutedEventArgs e)
    {
        _signInCancellation?.Cancel();
        ActiveMinecraftSession.Clear();
        UpdateAccountView();
        StatusText.Text = "Signed out. The in-memory session was cleared.";
    }

    private void UpdateAccountView()
    {
        var session = ActiveMinecraftSession.Current;
        AccountNameText.Text = session?.Account.Username ?? "Not signed in";
        AccountDetailText.Text = session is null
            ? "Your Minecraft session will be held in memory only."
            : $"UUID: {session.Account.Uuid}\nSigned in at {session.Account.SignedInAt.ToLocalTime():g}";
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
