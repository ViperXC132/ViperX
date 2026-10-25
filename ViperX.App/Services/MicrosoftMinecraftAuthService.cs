using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ViperX.App.Models;

namespace ViperX.App.Services;

public sealed record MicrosoftDeviceCode(string UserCode, Uri VerificationUri, string Message, DateTimeOffset ExpiresAt);
public sealed record AuthenticatedMinecraftSession(MinecraftAccount Account, string MinecraftAccessToken);

/// <summary>
/// Official Microsoft -> Xbox Live -> XSTS -> Minecraft Services sign-in flow.
/// Supply a registered public-client application ID; never ship a client secret.
/// Access tokens are returned for the current session and are not persisted by this service.
/// </summary>
public sealed class MicrosoftMinecraftAuthService
{
    private const string DeviceCodeEndpoint = "https://login.microsoftonline.com/consumers/oauth2/v2.0/devicecode";
    private const string TokenEndpoint = "https://login.microsoftonline.com/consumers/oauth2/v2.0/token";
    private const string XboxUserEndpoint = "https://user.auth.xboxlive.com/user/authenticate";
    private const string XboxXstsEndpoint = "https://xsts.auth.xboxlive.com/xsts/authorize";
    private const string MinecraftLoginEndpoint = "https://api.minecraftservices.com/authentication/login_with_xbox";
    private const string EntitlementsEndpoint = "https://api.minecraftservices.com/entitlements/mcstore";
    private const string ProfileEndpoint = "https://api.minecraftservices.com/minecraft/profile";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private readonly string _clientId;

    public MicrosoftMinecraftAuthService(string publicClientId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(publicClientId);
        _clientId = publicClientId;
    }

    public async Task<MicrosoftDeviceCode> BeginDeviceCodeAsync(CancellationToken cancellationToken = default)
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = _clientId,
            ["scope"] = "XboxLive.signin offline_access"
        });
        using var response = await Http.PostAsync(DeviceCodeEndpoint, content, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        var verification = root.GetProperty("verification_uri").GetString()
            ?? throw new InvalidDataException("Microsoft did not provide a verification URL.");
        return new MicrosoftDeviceCode(
            root.GetProperty("user_code").GetString()!,
            new Uri(verification),
            root.TryGetProperty("message", out var message) ? message.GetString() ?? "Complete sign-in in your browser." : "Complete sign-in in your browser.",
            DateTimeOffset.UtcNow.AddSeconds(root.GetProperty("expires_in").GetInt32()));
    }

    public async Task<AuthenticatedMinecraftSession> CompleteSignInAsync(
        MicrosoftDeviceCode deviceCode,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(deviceCode);
        progress?.Report("Waiting for Microsoft sign-in…");
        var microsoftToken = await PollMicrosoftTokenAsync(deviceCode, cancellationToken);
        progress?.Report("Validating Xbox Live identity…");

        var xbox = await PostJsonAsync(XboxUserEndpoint, new
        {
            Properties = new { AuthMethod = "RPS", SiteName = "user.auth.xboxlive.com", RpsTicket = "d=" + microsoftToken.AccessToken },
            RelyingParty = "http://auth.xboxlive.com",
            TokenType = "JWT"
        }, cancellationToken);
        var xboxToken = RequiredString(xbox, "Token");
        var userHash = xbox.GetProperty("DisplayClaims").GetProperty("xui").EnumerateArray().First().GetProperty("uhs").GetString()
            ?? throw new InvalidDataException("Xbox Live did not return a user hash.");

        progress?.Report("Validating Xbox security token…");
        var xsts = await PostJsonAsync(XboxXstsEndpoint, new
        {
            Properties = new { SandboxId = "RETAIL", UserTokens = new[] { xboxToken } },
            RelyingParty = "rp://api.minecraftservices.com/",
            TokenType = "JWT"
        }, cancellationToken);
        var xstsToken = RequiredString(xsts, "Token");
        var xstsHash = xsts.GetProperty("DisplayClaims").GetProperty("xui").EnumerateArray().First().GetProperty("uhs").GetString() ?? userHash;

        progress?.Report("Signing in to Minecraft Services…");
        var minecraftLogin = await PostJsonAsync(MinecraftLoginEndpoint, new
        {
            identityToken = $"XBL3.0 x={xstsHash};{xstsToken}"
        }, cancellationToken);
        var minecraftToken = RequiredString(minecraftLogin, "access_token");

        progress?.Report("Checking Minecraft ownership…");
        using (var entitlementRequest = new HttpRequestMessage(HttpMethod.Get, EntitlementsEndpoint))
        {
            entitlementRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", minecraftToken);
            using var entitlementResponse = await Http.SendAsync(entitlementRequest, cancellationToken);
            var entitlementBody = await entitlementResponse.Content.ReadAsStringAsync(cancellationToken);
            if (!entitlementResponse.IsSuccessStatusCode)
                throw new HttpRequestException($"Minecraft ownership check failed ({(int)entitlementResponse.StatusCode}): {SafeError(entitlementBody)}");
            using var entitlementDocument = JsonDocument.Parse(entitlementBody);
            if (!entitlementDocument.RootElement.TryGetProperty("items", out var items) ||
                items.ValueKind != JsonValueKind.Array || items.GetArrayLength() == 0)
                throw new UnauthorizedAccessException("This Microsoft account does not appear to own Minecraft Java Edition.");
        }

        progress?.Report("Loading Minecraft profile…");
        using var profileRequest = new HttpRequestMessage(HttpMethod.Get, ProfileEndpoint);
        profileRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", minecraftToken);
        using var profileResponse = await Http.SendAsync(profileRequest, cancellationToken);
        var profileBody = await profileResponse.Content.ReadAsStringAsync(cancellationToken);
        if (profileResponse.StatusCode == HttpStatusCode.NoContent)
            throw new InvalidOperationException("This account has no Minecraft Java profile yet.");
        if (!profileResponse.IsSuccessStatusCode)
            throw new HttpRequestException($"Minecraft profile lookup failed ({(int)profileResponse.StatusCode}): {SafeError(profileBody)}");

        using var profileDocument = JsonDocument.Parse(profileBody);
        var profile = profileDocument.RootElement;
        var uuid = RequiredString(profile, "id");
        var username = RequiredString(profile, "name");
        return new AuthenticatedMinecraftSession(
            new MinecraftAccount { Id = uuid, Uuid = uuid, Username = username },
            minecraftToken);
    }

    private async Task<MicrosoftTokenResponse> PollMicrosoftTokenAsync(MicrosoftDeviceCode deviceCode, CancellationToken cancellationToken)
    {
        var interval = TimeSpan.FromSeconds(5);
        while (DateTimeOffset.UtcNow < deviceCode.ExpiresAt)
        {
            await Task.Delay(interval, cancellationToken);
            using var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = _clientId,
                ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code",
                ["device_code"] = deviceCode.UserCode
            });
            using var response = await Http.PostAsync(TokenEndpoint, content, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (response.IsSuccessStatusCode)
                return new MicrosoftTokenResponse(RequiredString(root, "access_token"));

            var error = root.TryGetProperty("error", out var errorElement) ? errorElement.GetString() : null;
            if (error == "authorization_pending")
                continue;
            if (error == "slow_down")
            {
                interval += TimeSpan.FromSeconds(5);
                continue;
            }
            if (error == "authorization_declined")
                throw new UnauthorizedAccessException("Microsoft sign-in was declined.");
            if (error == "expired_token")
                break;
            throw new HttpRequestException($"Microsoft sign-in failed: {SafeError(body)}");
        }
        throw new TimeoutException("The Microsoft device code expired before sign-in was completed.");
    }

    private static async Task<JsonElement> PostJsonAsync(string url, object payload, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(payload);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await Http.PostAsync(url, content, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Authentication request failed ({(int)response.StatusCode}): {SafeError(body)}");
        using var document = JsonDocument.Parse(body);
        return document.RootElement.Clone();
    }

    private static string RequiredString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String &&
        !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()!
            : throw new InvalidDataException($"Authentication response is missing '{name}'.");

    private static string SafeError(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("errorMessage", out var message))
                return message.GetString() ?? "Unknown service error.";
            if (document.RootElement.TryGetProperty("error_description", out var description))
                return description.GetString() ?? "Unknown service error.";
            if (document.RootElement.TryGetProperty("error", out var error))
                return error.GetString() ?? "Unknown service error.";
        }
        catch (JsonException) { }
        return "The service returned an error response.";
    }

    private sealed record MicrosoftTokenResponse([property: JsonPropertyName("access_token")] string AccessToken);
}
