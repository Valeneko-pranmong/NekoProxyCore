using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NekoProxyCore.Legacy;
using Netch;
using Netch.JsonConverter;
using Netch.Models;
using Netch.Servers;

namespace Tests.Windows;

[TestClass]
public sealed class ProtectedSettingsProvisionerTests
{
    [TestMethod]
    public async Task ProvisionerAcceptsExternalInputAndCreatesAuthenticatedPayloadAndSeparateKeyAsync()
    {
        using var fixture = new Fixture();
        await fixture.WriteSettingsAsync(validRelationship: true);

        var facts = await ProtectedSettingsProvisioner.ProvisionAsync(
            fixture.InputPath,
            fixture.PayloadPath,
            fixture.KeyPath,
            fixture.ModeRoot);

        Assert.AreEqual(1, facts.ProfileCount);
        Assert.AreEqual(5, facts.ServerCount);
        Assert.IsTrue(facts.Pso2ProfileExists);
        Assert.IsTrue(facts.ProfileServerRelationshipValid);
        Assert.AreEqual(ProtectedSettingsPayload.KeySizeBytes, new FileInfo(fixture.KeyPath).Length);
        Assert.IsTrue(new FileInfo(fixture.PayloadPath).Length > 0);
        Assert.AreNotEqual(
            Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(fixture.InputPath))),
            Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(fixture.PayloadPath))));
    }

    [TestMethod]
    public async Task ProvisionerAcceptsCanonicalSettingsAgainstThePackagedModeBundleAsync()
    {
        using var fixture = new Fixture();
        await fixture.WriteSettingsAsync(validRelationship: true);

        var facts = await ProtectedSettingsProvisioner.ProvisionAsync(
            fixture.InputPath,
            fixture.PayloadPath,
            fixture.KeyPath,
            Path.Combine(FindRepositoryRoot(), "Storage", "mode"));

        Assert.AreEqual(1, facts.ProfileCount);
        Assert.AreEqual(5, facts.ServerCount);
    }

    [TestMethod]
    public async Task ProvisionerRejectsInvalidRelationshipWithoutLeavingOutputsAsync()
    {
        using var fixture = new Fixture();
        await fixture.WriteSettingsAsync(validRelationship: false);

        await Assert.ThrowsExceptionAsync<ProtectedSettingsException>(() =>
            ProtectedSettingsProvisioner.ProvisionAsync(
                fixture.InputPath,
                fixture.PayloadPath,
                fixture.KeyPath,
                fixture.ModeRoot));

        Assert.IsFalse(File.Exists(fixture.PayloadPath));
        Assert.IsFalse(File.Exists(fixture.KeyPath));
    }

    [TestMethod]
    public async Task VerifyAsyncAuthenticatesAndValidatesFrozenPairAsync()
    {
        using var fixture = new Fixture();
        await fixture.WriteSettingsAsync(validRelationship: true);

        await ProtectedSettingsProvisioner.ProvisionAsync(
            fixture.InputPath,
            fixture.PayloadPath,
            fixture.KeyPath,
            fixture.ModeRoot);
        var facts = await ProtectedSettingsProvisioner.VerifyAsync(
                fixture.PayloadPath,
                fixture.KeyPath,
                fixture.ModeRoot);

        Assert.AreEqual(1, facts.ProfileCount);
        Assert.AreEqual(5, facts.ServerCount);
        Assert.IsTrue(facts.Pso2ProfileExists);
        Assert.IsTrue(facts.ProfileServerRelationshipValid);
    }

    [TestMethod]
    public async Task VerifyAsyncRejectsWrongKeyAsync()
    {
        using var fixture = new Fixture();
        await fixture.WriteSettingsAsync(validRelationship: true);
        await ProtectedSettingsProvisioner.ProvisionAsync(
            fixture.InputPath,
            fixture.PayloadPath,
            fixture.KeyPath,
            fixture.ModeRoot);
        await File.WriteAllBytesAsync(
            fixture.KeyPath,
            RandomNumberGenerator.GetBytes(ProtectedSettingsPayload.KeySizeBytes));

        await Assert.ThrowsExceptionAsync<ProtectedSettingsException>(
            () => ProtectedSettingsProvisioner.VerifyAsync(
                fixture.PayloadPath,
                fixture.KeyPath,
                fixture.ModeRoot));
    }

    [TestMethod]
    public async Task VerifyAsyncRejectsAuthenticatedMalformedStructureAsync()
    {
        using var fixture = new Fixture();
        await fixture.WriteSettingsAsync(validRelationship: true);
        await ProtectedSettingsProvisioner.ProvisionAsync(
            fixture.InputPath,
            fixture.PayloadPath,
            fixture.KeyPath,
            fixture.ModeRoot);

        var malformedInput = Path.Combine(Path.GetDirectoryName(fixture.InputPath)!, "malformed.json");
        await File.WriteAllTextAsync(malformedInput, "{}");
        File.Delete(fixture.PayloadPath);
        var key = await File.ReadAllBytesAsync(fixture.KeyPath);
        try
        {
            await using var input = File.OpenRead(malformedInput);
            await using var output = File.Create(fixture.PayloadPath);
            await ProtectedSettingsPayload.SealAsync(input, output, key);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }

        await Assert.ThrowsExceptionAsync<ProtectedSettingsException>(
            () => ProtectedSettingsProvisioner.VerifyAsync(
                fixture.PayloadPath,
                fixture.KeyPath,
                fixture.ModeRoot));
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _root = Path.Combine(
            Path.GetTempPath(),
            "neko-settings-provisioner-test-" + Guid.NewGuid().ToString("N"));

        public Fixture()
        {
            var modeDirectory = Path.Combine(ModeRoot, "Custom");
            Directory.CreateDirectory(modeDirectory);
            File.Copy(
                Path.Combine(FindRepositoryRoot(), "Storage", "mode", "Custom", "PSO2.json"),
                Path.Combine(modeDirectory, "PSO2.json"));
        }

        public string InputPath => Path.Combine(_root, "external-input.json");
        public string PayloadPath => Path.Combine(_root, "runtime-settings.nkps");
        public string KeyPath => Path.Combine(_root, "runtime-settings.key");
        public string ModeRoot => Path.Combine(_root, "mode");

        public async Task WriteSettingsAsync(bool validRelationship)
        {
            var settings = new Setting();
            for (var index = 0; index < 5; index++)
            {
                settings.Server.Add(new Socks5Server
                {
                    Remark = "SECRET_SERVER_MARKER_" + index,
                    Hostname = "SECRET_HOST_MARKER_" + index + ".example.invalid",
                    Port = checked((ushort)(22000 + index)),
                    Username = "SECRET_USER_MARKER_" + index,
                    Password = "SECRET_PASSWORD_MARKER_" + index
                });
            }
            settings.Profiles.Add(new Profile
            {
                Index = 0,
                ProfileName = "SYNTHETIC_PROFILE",
                ServerRemark = validRelationship ? settings.Server[0].Remark : "NO_MATCH",
                ModeRemark = "PSO2"
            });

            var options = Global.NewCustomJsonSerializerOptions();
            options.Converters.Add(new ServerConverterWithTypeDiscriminator());
            options.Converters.Add(new JsonStringEnumConverter());
            await File.WriteAllBytesAsync(InputPath, JsonSerializer.SerializeToUtf8Bytes(settings, options));
        }

        public void Dispose() => Directory.Delete(_root, recursive: true);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Netch.sln")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new AssertFailedException("Unable to locate the repository root.");
    }
}
