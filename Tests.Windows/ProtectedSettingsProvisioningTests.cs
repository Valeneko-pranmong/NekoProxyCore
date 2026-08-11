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
[DoNotParallelize]
public sealed class ProtectedSettingsProvisioningTests
{
    private string? _originalCurrentDirectory;
    private Setting? _originalSettings;
    private Netch.Models.Modes.Mode[]? _originalModes;

    [TestInitialize]
    public void SaveGlobalState()
    {
        _originalCurrentDirectory = Directory.GetCurrentDirectory();
        _originalSettings = Global.Settings;
        _originalModes = Global.Modes.ToArray();
    }

    [TestCleanup]
    public void RestoreGlobalState()
    {
        if (_originalCurrentDirectory != null)
            Directory.SetCurrentDirectory(_originalCurrentDirectory);
        if (_originalSettings != null)
            Global.Settings = _originalSettings;
        Global.Modes.Clear();
        if (_originalModes != null)
            Global.Modes.AddRange(_originalModes);
    }

    [TestMethod]
    public async Task ValidProtectedPayloadLoadsSettingsAndUniquePso2CandidateInMemoryAsync()
    {
        using var fixture = await ProtectedRuntimeFixture.CreateAsync(CreateValidSettings());

        await NetchRuntimeBootstrap.InitializeProtectedAsync(
            fixture.RuntimeRoot,
            fixture.PayloadPath,
            fixture.Key);

        Assert.AreEqual(1, Global.Settings.Profiles.Count);
        Assert.AreEqual(5, Global.Settings.Server.Count);
        var catalog = new NetchProcessModeConfigurationCatalog();
        var result = catalog.GetCatalog();
        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual(1, result.Candidates.Count);
        Assert.AreEqual("profile-0", result.Candidates[0].ProfileReference);
        Assert.AreEqual("server-0", result.Candidates[0].ServerReference);
        Assert.IsTrue(catalog.Validate("profile-0", "server-0").Valid);
        var resolver = new NetchProcessModeSessionResolver(catalog);
        var session = await resolver.ResolveAsync(
            new NekoProxyCore.Core.ProxyConfiguration(
                NekoProxyCore.Core.ProxyModeKind.Process,
                "pso2.exe",
                "profile-0",
                "server-0",
                targetPid: 4242),
            NullStatusSink.Instance,
            CancellationToken.None);
        Assert.IsNotNull(session);
        Assert.IsFalse(File.Exists(Path.Combine(fixture.RuntimeRoot, "data", "settings.json")));
    }

    [DataTestMethod]
    [DataRow("ZERO_PROFILES")]
    [DataRow("ZERO_SERVERS")]
    [DataRow("PROFILE_SERVER_MISMATCH")]
    [DataRow("PSO2_MODE_MISSING")]
    [DataRow("PSO2_MODE_AMBIGUOUS")]
    public async Task StructurallyInvalidProtectedSettingsFailClosedAsync(string scenario)
    {
        var settings = CreateValidSettings();
        var modeCopies = 1;
        switch (scenario)
        {
            case "ZERO_PROFILES":
                settings.Profiles.Clear();
                break;
            case "ZERO_SERVERS":
                settings.Server.Clear();
                break;
            case "PROFILE_SERVER_MISMATCH":
                settings.Profiles[0].ServerRemark = "NO_MATCH";
                break;
            case "PSO2_MODE_MISSING":
                modeCopies = 0;
                break;
            case "PSO2_MODE_AMBIGUOUS":
                modeCopies = 2;
                break;
            default:
                Assert.Fail("Unknown scenario.");
                break;
        }

        using var fixture = await ProtectedRuntimeFixture.CreateAsync(settings, modeCopies);

        await Assert.ThrowsExceptionAsync<ProtectedSettingsException>(() =>
            NetchRuntimeBootstrap.InitializeProtectedAsync(
                fixture.RuntimeRoot,
                fixture.PayloadPath,
                fixture.Key));
    }

    [DataTestMethod]
    [DataRow("MISSING")]
    [DataRow("TRUNCATED")]
    [DataRow("BIT_FLIPPED")]
    [DataRow("TAG_FAILURE")]
    [DataRow("WRONG_KEY")]
    public async Task InvalidProtectedMaterialFailsClosedAsync(string scenario)
    {
        using var fixture = await ProtectedRuntimeFixture.CreateAsync(CreateValidSettings());
        var key = fixture.Key;
        switch (scenario)
        {
            case "MISSING":
                File.Delete(fixture.PayloadPath);
                break;
            case "TRUNCATED":
                File.WriteAllBytes(fixture.PayloadPath, File.ReadAllBytes(fixture.PayloadPath)[..12]);
                break;
            case "BIT_FLIPPED":
                FlipPayloadByte(fixture.PayloadPath, -1);
                break;
            case "TAG_FAILURE":
                FlipPayloadByte(fixture.PayloadPath, 8 + 1 + 12);
                break;
            case "WRONG_KEY":
                key = RandomNumberGenerator.GetBytes(ProtectedSettingsPayload.KeySizeBytes);
                break;
            default:
                Assert.Fail("Unknown scenario.");
                break;
        }

        try
        {
            var exception = await Assert.ThrowsExceptionAsync<ProtectedSettingsException>(() =>
                NetchRuntimeBootstrap.InitializeProtectedAsync(
                    fixture.RuntimeRoot,
                    fixture.PayloadPath,
                    key));
            Assert.AreEqual("Protected runtime settings are unavailable or invalid.", exception.Message);
        }
        finally
        {
            if (!ReferenceEquals(key, fixture.Key))
                CryptographicOperations.ZeroMemory(key);
        }
    }

    [TestMethod]
    public async Task MalformedAuthenticatedPlaintextFailsClosedWithoutPersistenceAsync()
    {
        var malformed = System.Text.Encoding.UTF8.GetBytes(
            "{\"marker\":\"SECRET_HOST_MARKER SECRET_PASSWORD_MARKER SECRET_SERVER_MARKER\"");
        using var fixture = await ProtectedRuntimeFixture.CreateRawAsync(malformed);
        CryptographicOperations.ZeroMemory(malformed);

        var exception = await Assert.ThrowsExceptionAsync<ProtectedSettingsException>(() =>
            NetchRuntimeBootstrap.InitializeProtectedAsync(
                fixture.RuntimeRoot,
                fixture.PayloadPath,
                fixture.Key));

        Assert.AreEqual("Protected runtime settings are unavailable or invalid.", exception.Message);
        Assert.IsFalse(File.Exists(Path.Combine(fixture.RuntimeRoot, "data", "settings.json")));
    }

    [TestMethod]
    public async Task InvalidProtectedPayloadNeverFallsBackToPlaintextSettingsAsync()
    {
        using var fixture = await ProtectedRuntimeFixture.CreateAsync(CreateValidSettings());
        var dataDirectory = Path.Combine(fixture.RuntimeRoot, "data");
        Directory.CreateDirectory(dataDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(dataDirectory, "settings.json"),
            "{\"marker\":\"SECRET_HOST_MARKER SECRET_PASSWORD_MARKER SECRET_SERVER_MARKER\"}");
        FlipPayloadByte(fixture.PayloadPath, -1);
        Global.Settings = new Setting();

        await Assert.ThrowsExceptionAsync<ProtectedSettingsException>(() =>
            NetchRuntimeBootstrap.InitializeProtectedAsync(
                fixture.RuntimeRoot,
                fixture.PayloadPath,
                fixture.Key));

        Assert.AreEqual(0, Global.Settings.Profiles.Count);
        Assert.AreEqual(0, Global.Settings.Server.Count);
    }

    private static void FlipPayloadByte(string path, int index)
    {
        var payload = File.ReadAllBytes(path);
        var resolvedIndex = index < 0 ? payload.Length + index : index;
        payload[resolvedIndex] ^= 0x01;
        File.WriteAllBytes(path, payload);
        CryptographicOperations.ZeroMemory(payload);
    }

    private static Setting CreateValidSettings()
    {
        var settings = new Setting();
        for (var index = 0; index < 5; index++)
        {
            settings.Server.Add(new Socks5Server
            {
                Remark = "SYNTHETIC_SERVER_" + index,
                Hostname = "synthetic-" + index + ".example.invalid",
                Port = checked((ushort)(20000 + index)),
                Username = "SYNTHETIC_USER_" + index,
                Password = "SYNTHETIC_PASSWORD_" + index
            });
        }

        settings.Profiles.Add(new Profile
        {
            Index = 0,
            ProfileName = "SYNTHETIC_PROFILE",
            ServerRemark = settings.Server[0].Remark,
            ModeRemark = "PSO2"
        });
        return settings;
    }

    private sealed class ProtectedRuntimeFixture : IDisposable
    {
        private ProtectedRuntimeFixture(string runtimeRoot, string payloadPath, byte[] key)
        {
            RuntimeRoot = runtimeRoot;
            PayloadPath = payloadPath;
            Key = key;
        }

        public string RuntimeRoot { get; }
        public string PayloadPath { get; }
        public byte[] Key { get; }

        public static async Task<ProtectedRuntimeFixture> CreateAsync(Setting settings, int modeCopies = 1)
        {
            var root = Path.Combine(Path.GetTempPath(), "neko-protected-settings-test-" + Guid.NewGuid().ToString("N"));
            var modeDirectory = Path.Combine(root, "mode", "Custom");
            Directory.CreateDirectory(modeDirectory);
            for (var index = 0; index < modeCopies; index++)
            {
                File.Copy(
                    Path.Combine(FindRepositoryRoot(), "Storage", "mode", "Custom", "PSO2.json"),
                    Path.Combine(modeDirectory, "PSO2-" + index + ".json"));
            }

            var plaintext = JsonSerializer.SerializeToUtf8Bytes(settings, SerializerOptions());
            var key = RandomNumberGenerator.GetBytes(ProtectedSettingsPayload.KeySizeBytes);
            var payloadPath = Path.Combine(root, ProtectedSettingsPayload.DefaultFileName);
            await using (var plaintextStream = new MemoryStream(plaintext, writable: false))
            await using (var payloadStream = new FileStream(payloadPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await ProtectedSettingsPayload.SealAsync(plaintextStream, payloadStream, key);
            }
            CryptographicOperations.ZeroMemory(plaintext);
            return new ProtectedRuntimeFixture(root, payloadPath, key);
        }

        public static async Task<ProtectedRuntimeFixture> CreateRawAsync(byte[] plaintext)
        {
            var root = Path.Combine(Path.GetTempPath(), "neko-protected-settings-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(root, "mode"));
            var key = RandomNumberGenerator.GetBytes(ProtectedSettingsPayload.KeySizeBytes);
            var payloadPath = Path.Combine(root, ProtectedSettingsPayload.DefaultFileName);
            await using (var plaintextStream = new MemoryStream(plaintext, writable: false))
            await using (var payloadStream = new FileStream(payloadPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await ProtectedSettingsPayload.SealAsync(plaintextStream, payloadStream, key);
            }
            return new ProtectedRuntimeFixture(root, payloadPath, key);
        }

        public void Dispose()
        {
            CryptographicOperations.ZeroMemory(Key);
            if (Path.GetFullPath(Directory.GetCurrentDirectory())
                .StartsWith(Path.GetFullPath(RuntimeRoot), StringComparison.OrdinalIgnoreCase))
                Directory.SetCurrentDirectory(FindRepositoryRoot());
            Directory.Delete(RuntimeRoot, recursive: true);
        }

        private static JsonSerializerOptions SerializerOptions()
        {
            var options = Global.NewCustomJsonSerializerOptions();
            options.Converters.Add(new ServerConverterWithTypeDiscriminator());
            options.Converters.Add(new JsonStringEnumConverter());
            return options;
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Netch.sln")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new AssertFailedException("Unable to locate the repository root.");
    }

    private sealed class NullStatusSink : NekoProxyCore.Core.IProxyStatusSink
    {
        public static NullStatusSink Instance { get; } = new();

        public void OnStatusChanged(NekoProxyCore.Core.ProxyStatusEvent statusEvent)
        {
        }
    }
}
