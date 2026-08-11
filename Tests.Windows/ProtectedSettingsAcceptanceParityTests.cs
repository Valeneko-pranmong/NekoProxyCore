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
public sealed class ProtectedSettingsAcceptanceParityTests
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

    [DataTestMethod]
    [DataRow("VALID_CANONICAL", true)]
    [DataRow("PROFILE_INDEX_NONZERO", false)]
    [DataRow("MATCHING_SERVER_AT_NONZERO_POSITION", false)]
    [DataRow("PROFILE_SERVER_MISMATCH", false)]
    [DataRow("DUPLICATE_RELATIONSHIP", false)]
    [DataRow("WRONG_MODE_REMARK", false)]
    [DataRow("ZERO_PROFILES", false)]
    [DataRow("ZERO_SERVERS", false)]
    [DataRow("PSO2_MODE_MISSING", false)]
    [DataRow("PSO2_MODE_AMBIGUOUS", false)]
    public async Task ProvisionVerifyAndProductionStartupHaveIdenticalAcceptanceAsync(
        string scenario,
        bool expectedAcceptance)
    {
        var modeCopies = scenario switch
        {
            "PSO2_MODE_MISSING" => 0,
            "PSO2_MODE_AMBIGUOUS" => 2,
            _ => 1
        };
        using var fixture = await Fixture.CreateAsync(CreateSettings(scenario), modeCopies);

        var provisionAccepted = await IsAcceptedAsync(() =>
            ProtectedSettingsProvisioner.ProvisionAsync(
                fixture.InputPath,
                fixture.ProvisionedPayloadPath,
                fixture.ProvisionedKeyPath,
                fixture.ModeRoot));
        var verifyAccepted = await IsAcceptedAsync(() =>
            ProtectedSettingsProvisioner.VerifyAsync(fixture.PayloadPath, fixture.KeyPath, fixture.ModeRoot));
        var startupAccepted = await IsAcceptedAsync(() =>
            NetchRuntimeBootstrap.InitializeProtectedAsync(
                fixture.RuntimeRoot,
                fixture.PayloadPath,
                fixture.Key));

        Assert.AreEqual(expectedAcceptance, provisionAccepted, $"Provision acceptance differed for {scenario}.");
        Assert.AreEqual(expectedAcceptance, verifyAccepted, $"Verify acceptance differed for {scenario}.");
        Assert.AreEqual(expectedAcceptance, startupAccepted, $"Startup acceptance differed for {scenario}.");
        Assert.AreEqual(provisionAccepted, verifyAccepted, $"Provision/Verify parity failed for {scenario}.");
        Assert.AreEqual(verifyAccepted, startupAccepted, $"Verify/Startup parity failed for {scenario}.");
    }

    private static async Task<bool> IsAcceptedAsync(Func<Task> operation)
    {
        try
        {
            await operation();
            return true;
        }
        catch (ProtectedSettingsException)
        {
            return false;
        }
    }

    private static Setting CreateSettings(string scenario)
    {
        var settings = new Setting();
        for (var index = 0; index < 5; index++)
        {
            settings.Server.Add(new Socks5Server
            {
                Remark = "SYNTHETIC_SERVER_" + index,
                Hostname = "synthetic-" + index + ".example.invalid",
                Port = checked((ushort)(21000 + index)),
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

        switch (scenario)
        {
            case "VALID_CANONICAL":
                break;
            case "PROFILE_INDEX_NONZERO":
                settings.Profiles[0].Index = 1;
                break;
            case "MATCHING_SERVER_AT_NONZERO_POSITION":
                settings.Profiles[0].ServerRemark = settings.Server[1].Remark;
                break;
            case "PROFILE_SERVER_MISMATCH":
                settings.Profiles[0].ServerRemark = "NO_MATCH";
                break;
            case "DUPLICATE_RELATIONSHIP":
                settings.Server[1].Remark = settings.Server[0].Remark;
                break;
            case "WRONG_MODE_REMARK":
                settings.Profiles[0].ModeRemark = "WRONG_MODE";
                break;
            case "ZERO_PROFILES":
                settings.Profiles.Clear();
                break;
            case "ZERO_SERVERS":
                settings.Server.Clear();
                break;
            case "PSO2_MODE_MISSING":
            case "PSO2_MODE_AMBIGUOUS":
                break;
            default:
                Assert.Fail("Unknown scenario.");
                break;
        }

        return settings;
    }

    private sealed class Fixture : IDisposable
    {
        private Fixture(string runtimeRoot, byte[] key)
        {
            RuntimeRoot = runtimeRoot;
            Key = key;
        }

        public string RuntimeRoot { get; }
        public byte[] Key { get; }
        public string InputPath => Path.Combine(RuntimeRoot, "external-input.json");
        public string PayloadPath => Path.Combine(RuntimeRoot, ProtectedSettingsPayload.DefaultFileName);
        public string KeyPath => Path.Combine(RuntimeRoot, "frozen.key");
        public string ProvisionedPayloadPath => Path.Combine(RuntimeRoot, "provisioned.nkps");
        public string ProvisionedKeyPath => Path.Combine(RuntimeRoot, "provisioned.key");
        public string ModeRoot => Path.Combine(RuntimeRoot, "mode");

        public static async Task<Fixture> CreateAsync(Setting settings, int modeCopies)
        {
            var root = Path.Combine(
                Path.GetTempPath(),
                "neko-settings-parity-test-" + Guid.NewGuid().ToString("N"));
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
            var fixture = new Fixture(root, key);
            await File.WriteAllBytesAsync(fixture.InputPath, plaintext);
            await File.WriteAllBytesAsync(fixture.KeyPath, key);
            await using (var plaintextStream = new MemoryStream(plaintext, writable: false))
            await using (var payloadStream = new FileStream(
                             fixture.PayloadPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None))
            {
                await ProtectedSettingsPayload.SealAsync(plaintextStream, payloadStream, key);
            }
            CryptographicOperations.ZeroMemory(plaintext);
            return fixture;
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
}
