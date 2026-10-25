using Xunit;
using ViperX.App.Models;
using ViperX.App.Services;

namespace ViperX.Tests;

public sealed class InstallationModelTests
{
    [Fact]
    public void DefaultInstallation_HasExpectedSafeDefaults()
    {
        var installation = new MinecraftInstallation();

        Assert.NotEqual(Guid.Empty, installation.Id);
        Assert.Equal("Vanilla", installation.Loader);
        Assert.Equal(4096, installation.MemoryMegabytes);
        Assert.Contains("A Minecraft version must be selected.", installation.Validate());
        Assert.Contains("A game directory is required.", installation.Validate());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Installation_RequiresName(string name)
    {
        var installation = new MinecraftInstallation
        {
            Name = name,
            GameVersion = "1.21.4",
            GameDirectory = @"C:\Minecraft\ViperX"
        };

        Assert.Contains("Installation name is required.", installation.Validate());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(511)]
    [InlineData(262145)]
    public void Installation_RejectsOutOfRangeMemory(int memoryMegabytes)
    {
        var installation = new MinecraftInstallation
        {
            GameVersion = "1.21.4",
            GameDirectory = @"C:\Minecraft\ViperX",
            MemoryMegabytes = memoryMegabytes
        };

        Assert.Contains(installation.Validate(), error => error.Contains("Memory allocation"));
    }

    [Fact]
    public void Profile_RequiresInstallationReference()
    {
        var profile = new LauncherProfile { Name = "Survival" };

        Assert.Contains("A valid installation must be assigned to this profile.", profile.Validate());
    }

    [Fact]
    public void Profile_WithNameAndInstallationReference_IsValid()
    {
        var profile = new LauncherProfile
        {
            Name = "Survival",
            InstallationId = Guid.NewGuid()
        };

        Assert.Empty(profile.Validate());
    }
}


public sealed class LauncherSettingsTests
{
    [Fact]
    public void DefaultSettings_AreValid()
    {
        var settings = new ViperX.App.Models.LauncherSettings();
        Assert.Empty(settings.Validate());
    }

    [Fact]
    public void Settings_RejectInvalidMemoryAndWindowDimensions()
    {
        var settings = new ViperX.App.Models.LauncherSettings
        {
            DefaultMemoryMegabytes = 128,
            WindowWidth = 100,
            WindowHeight = 100
        };

        Assert.Equal(3, settings.Validate().Count);
    }
}


public sealed class VerifiedDownloadServiceTests
{
    [Fact]
    public async Task Downloader_RejectsNonHttpsSource()
    {
        var service = new VerifiedDownloadService();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.DownloadAsync(new Uri("http://example.invalid/file"), Path.Combine(Path.GetTempPath(), "viperx-test-download")));
    }

    [Fact]
    public async Task Downloader_RejectsMalformedSha1()
    {
        var service = new VerifiedDownloadService();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.DownloadAsync(new Uri("https://example.invalid/file"), Path.Combine(Path.GetTempPath(), "viperx-test-download"), expectedSha1: "not-a-hash"));
    }
}
