using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
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

        SetBusy(true);
        _signInCancellation?.Cancel();
        _signInCancellation?.Dispose();
        _signInCancellation = new CancellationTokenSource();
        var token = _signInCancellation.Token;
        DeviceCodeText.Text = "";
        try
        {
            var service = new MicrosoftMinecraftAuthService(clientId);
            var deviceCode = await service.BeginDeviceCodeAsync(token);
            DeviceCodeText.Text = deviceCode.UserCode;
            StatusText.Text = $"{deviceCode.Message}\nExpires at {deviceCode.ExpiresAt.ToLocalTime():t}.";
            try { Process.Start(new ProcessStartInfo(deviceCode.VerificationUri.AbsoluteUri) { UseShellExecute = true }); }
            catch (Exception) { StatusText.Text += $" Open {deviceCode.VerificationUri} manually."; }
            var session = await service.CompleteSignInAsync(deviceCode, new Progress<string>(message => StatusText.Text = message), token);
            ActiveMinecraftSession.Set(session);
            UpdateAccountView();
            StatusText.Text = "Microsoft sign-in successful. Session token remains in memory.";
        }
        catch (OperationCanceledException) { StatusText.Text = "Sign-in cancelled."; }
        catch (Exception ex)
        {
            StatusText.Text = "Microsoft sign-in failed.";
            MessageBox.Show(this, ex.Message, "Sign-in failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally { SetBusy(false); }
    }

    private void Offline_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ActiveMinecraftSession.Set(AlternateAccountService.CreateOffline(OfflineNameBox.Text));
            UpdateAccountView();
            StatusText.Text = "Offline identity selected. Online-mode servers will reject this account.";
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Invalid offline username", MessageBoxButton.OK, MessageBoxImage.Information); }
    }

    private async void YggdrasilSignIn_Click(object sender, RoutedEventArgs e)
    {
        SetBusy(true);
        try
        {
            var provider = (ProviderBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Custom";
            var session = await AlternateAccountService.SignInYggdrasilAsync(
                ServerUrlBox.Text, YggdrasilUsernameBox.Text, YggdrasilPasswordBox.Password, provider);
            ActiveMinecraftSession.Set(session);
            UpdateAccountView();
            StatusText.Text = $"Signed in with {provider}. Authlib-injector will be configured when launching.";
            YggdrasilPasswordBox.Clear();
        }
        catch (Exception ex)
        {
            StatusText.Text = "Yggdrasil sign-in failed.";
            MessageBox.Show(this, ex.Message, "Yggdrasil sign-in failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally { SetBusy(false); }
    }

    private void Provider_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ServerUrlBox is null || ProviderBox.SelectedItem is not ComboBoxItem item) return;
        ServerUrlBox.Text = item.Tag?.ToString() switch
        {
            "LittleSkin" => "https://littleskin.cn/api/yggdrasil",
            "Ely.by" => "https://authserver.ely.by",
            _ => "http://localhost:25585"
        };
    }

    private void SetBusy(bool busy)
    {
        SignInButton.IsEnabled = !busy;
        YggdrasilSignInButton.IsEnabled = !busy;
    }

    private void SignOut_Click(object sender, RoutedEventArgs e)
    {
        _signInCancellation?.Cancel();
        ActiveMinecraftSession.Clear();
        UpdateAccountView();
        StatusText.Text = "Signed out. The active session was cleared.";
    }

    private void UpdateAccountView()
    {
        var session = ActiveMinecraftSession.Current;
        AccountNameText.Text = session?.Account.Username ?? "Not signed in";
        AccountDetailText.Text = session is null
            ? "Choose Microsoft, offline, LittleSkin, Ely.by or a custom Yggdrasil account."
            : $"{session.Account.AccountType} • UUID: {session.Account.Uuid}\nSigned in at {session.Account.SignedInAt.ToLocalTime():g}";
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
