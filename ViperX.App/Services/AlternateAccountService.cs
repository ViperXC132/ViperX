using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ViperX.App.Models;

namespace ViperX.App.Services;

public static class AlternateAccountService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(25) };

    public static AuthenticatedMinecraftSession CreateOffline(string username)
    {
        username = username.Trim();
        if (username.Length is < 3 or > 16 || username.Any(c => !(char.IsAsciiLetterOrDigit(c) || c == '_')))
            throw new ArgumentException("Offline usernames must be 3–16 characters and contain only A–Z, 0–9, or underscore.");
        var bytes = MD5.HashData(Encoding.UTF8.GetBytes("OfflinePlayer:" + username));
        var uuid = new Guid(bytes).ToString("N");
        return new AuthenticatedMinecraftSession(new MinecraftAccount
        {
            Id = "offline:" + uuid, Username = username, Uuid = uuid, AccountType = "Offline"
        }, "0", "legacy", null);
    }

    public static async Task<AuthenticatedMinecraftSession> SignInYggdrasilAsync(
        string serverUrl, string username, string password, string provider,
        CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(serverUrl.Trim(), UriKind.Absolute, out var baseUri) ||
            (baseUri.Scheme != Uri.UriSchemeHttps && !(baseUri.IsLoopback && baseUri.Scheme == Uri.UriSchemeHttp)))
            throw new ArgumentException("Use an HTTPS Yggdrasil server URL. HTTP is allowed only for localhost.");
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
            throw new ArgumentException("Enter your account username/email and password.");
        var root = baseUri.AbsoluteUri.TrimEnd('/') + "/";
        var endpoint = new Uri(new Uri(root), "authserver/authenticate");
        var payload = JsonSerializer.Serialize(new
        {
            agent = new { name = "Minecraft", version = 1 },
            username, password, requestUser = true
        });
        using var response = await Http.PostAsync(endpoint,
            new StringContent(payload, Encoding.UTF8, "application/json"), cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new UnauthorizedAccessException($"Sign-in to {provider} failed ({(int)response.StatusCode}). Check credentials and the server's documented Yggdrasil API base URL.");
        using var doc = JsonDocument.Parse(body);
        var rootJson = doc.RootElement;
        var token = Get(rootJson, "accessToken");
        var selected = rootJson.GetProperty("selectedProfile");
        var uuid = Get(selected, "id");
        var name = Get(selected, "name");
        return new AuthenticatedMinecraftSession(new MinecraftAccount
        {
            Id = provider.ToLowerInvariant() + ":" + uuid, Username = name, Uuid = uuid, AccountType = provider
        }, token, "legacy", baseUri.AbsoluteUri.TrimEnd('/'));
    }

    private static string Get(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String &&
        !string.IsNullOrWhiteSpace(value.GetString()) ? value.GetString()! :
        throw new InvalidDataException($"Yggdrasil response is missing '{property}'.");
}
