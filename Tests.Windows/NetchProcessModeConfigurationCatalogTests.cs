using Microsoft.VisualStudio.TestTools.UnitTesting;
using NekoProxyCore.Core;
using NekoProxyCore.Host.Protocol;
using NekoProxyCore.Legacy;
using Netch;
using Netch.Models;
using Netch.Models.Modes;
using Netch.Models.Modes.ProcessMode;
using Netch.Servers;

namespace Tests.Windows;

[TestClass]
[DoNotParallelize]
public sealed class NetchProcessModeConfigurationCatalogTests
{
    private Setting? _originalSettings;
    private Mode[]? _originalModes;

    [TestInitialize]
    public void SaveGlobalState()
    {
        _originalSettings = Global.Settings;
        _originalModes = Global.Modes.ToArray();
        Global.Settings = new Setting();
        Global.Modes.Clear();
    }

    [TestCleanup]
    public void RestoreGlobalState()
    {
        if (_originalSettings != null)
            Global.Settings = _originalSettings;
        Global.Modes.Clear();
        if (_originalModes != null)
            Global.Modes.AddRange(_originalModes);
    }

    [TestMethod]
    public void OneValidPairIsReturnedAsOneSanitizedCandidate()
    {
        AddServer("SERVER_SECRET_REMARK");
        AddProfile(12, "SERVER_SECRET_REMARK", "MODE_SECRET_REMARK");
        AddMode("MODE_SECRET_REMARK");

        var result = new NetchProcessModeConfigurationCatalog().GetCatalog();

        Assert.IsTrue(result.Succeeded);
        Assert.IsNull(result.FailureReason);
        Assert.AreEqual(1, result.Candidates.Count);
        var candidate = result.Candidates[0];
        Assert.AreEqual("profile-12", candidate.ProfileReference);
        Assert.AreEqual("server-0", candidate.ServerReference);
        Assert.IsTrue(candidate.RelationshipValid);
        Assert.AreEqual(1, candidate.ProcessModeMatchCount);
    }

    [TestMethod]
    public async Task MultipleValidPairsAreSortedAndEveryCandidateResolvesAsync()
    {
        AddServer("SERVER_B");
        AddServer("SERVER_A");
        AddProfile(12, "SERVER_A", "MODE_A");
        AddProfile(2, "SERVER_B", "MODE_B");
        AddMode("MODE_A");
        AddMode("MODE_B");

        var catalog = new NetchProcessModeConfigurationCatalog();
        var result = catalog.GetCatalog();

        Assert.IsTrue(result.Succeeded);
        CollectionAssert.AreEqual(
            new[] { "profile-2/server-0", "profile-12/server-1" },
            result.Candidates
                .Select(candidate => $"{candidate.ProfileReference}/{candidate.ServerReference}")
                .ToArray());
        var resolver = new NetchProcessModeSessionResolver(catalog);
        foreach (var candidate in result.Candidates)
        {
            var session = await resolver.ResolveAsync(
                CreateConfiguration(candidate.ProfileReference, candidate.ServerReference),
                NullStatusSink.Instance,
                CancellationToken.None);
            Assert.IsNotNull(session);
        }
    }

    [DataTestMethod]
    [DataRow("ZERO_PROFILES", false, 0)]
    [DataRow("ZERO_SERVERS", false, 0)]
    [DataRow("PROFILE_SERVER_MISMATCH", false, 1)]
    [DataRow("MODE_MISSING", true, 0)]
    [DataRow("MODE_AMBIGUOUS", true, 2)]
    [DataRow("DUPLICATE_PROFILE_INDEX", false, 0)]
    public async Task InvalidPairsAreNotReturnedAndResolverAlsoRejectsThemAsync(
        string scenario,
        bool expectedRelationship,
        int expectedMatchCount)
    {
        ConfigureInvalidScenario(scenario);
        var catalog = new NetchProcessModeConfigurationCatalog();

        var result = catalog.GetCatalog();
        var validation = catalog.Validate("profile-0", "server-0");

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual(0, result.Candidates.Count);
        Assert.IsFalse(validation.Valid);
        Assert.AreEqual(expectedRelationship, validation.RelationshipValid);
        Assert.AreEqual(expectedMatchCount, validation.ProcessModeMatchCount);
        var resolver = new NetchProcessModeSessionResolver(catalog);
        var exception = await Assert.ThrowsExceptionAsync<ProxyRuntimeException>(() =>
            resolver.ResolveAsync(
                CreateConfiguration("profile-0", "server-0"),
                NullStatusSink.Instance,
                CancellationToken.None));
        Assert.AreEqual(ProxyErrorCode.InvalidConfiguration, exception.Code);
    }

    [TestMethod]
    public void OutOfRangeReferencesValidateAsInvalidWithoutLeakingCause()
    {
        AddServer("SERVER_A");
        AddProfile(0, "SERVER_A", "MODE_A");
        AddMode("MODE_A");
        var catalog = new NetchProcessModeConfigurationCatalog();

        var missingProfile = catalog.Validate("profile-999999", "server-0");
        var missingServer = catalog.Validate("profile-0", "server-999999");

        Assert.IsFalse(missingProfile.Valid);
        Assert.IsFalse(missingProfile.RelationshipValid);
        Assert.AreEqual(0, missingProfile.ProcessModeMatchCount);
        Assert.IsFalse(missingServer.Valid);
        Assert.IsFalse(missingServer.RelationshipValid);
        Assert.AreEqual(1, missingServer.ProcessModeMatchCount);
    }

    [DataTestMethod]
    [DataRow("profile-x", "server-0")]
    [DataRow("profile-0000000", "server-0")]
    [DataRow("profile-0", "server-x")]
    [DataRow("profile-0", "server-0000000")]
    public void MalformedReferencesFailClosedAtTheCatalogBoundary(
        string profileReference,
        string serverReference)
    {
        var validation = new NetchProcessModeConfigurationCatalog().Validate(
            profileReference,
            serverReference);

        Assert.IsFalse(validation.Valid);
        Assert.IsFalse(validation.RelationshipValid);
        Assert.AreEqual(0, validation.ProcessModeMatchCount);
    }

    [TestMethod]
    public void CatalogOverLimitFailsWithoutTruncationOrFallback()
    {
        AddServer("SHARED_SERVER");
        AddMode("SHARED_MODE");
        for (var index = 0; index <= ProcessModeConfigurationCatalogContract.MaximumCandidates; index++)
            AddProfile(index, "SHARED_SERVER", "SHARED_MODE");

        var result = new NetchProcessModeConfigurationCatalog().GetCatalog();

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(ProcessModeConfigurationCatalogFailureReason.CatalogTooLarge, result.FailureReason);
        Assert.AreEqual(0, result.Candidates.Count);
    }

    [DataTestMethod]
    [DataRow(31)]
    [DataRow(ProcessModeConfigurationCatalogContract.MaximumCandidates)]
    public void CatalogAtOrBelowLimitSucceedsAsOneCompleteBoundedWireResponse(int candidateCount)
    {
        AddServer("SHARED_SERVER");
        AddMode("SHARED_MODE");
        for (var index = 0; index < candidateCount; index++)
            AddProfile(index, "SHARED_SERVER", "SHARED_MODE");

        var result = new NetchProcessModeConfigurationCatalog().GetCatalog();
        var json = ControlProtocol.SerializeRuntimeConfigCatalog(
            "0123456789abcdef0123456789abcdef",
            result);

        Assert.IsTrue(result.Succeeded);
        Assert.IsNull(result.FailureReason);
        Assert.AreEqual(candidateCount, result.Candidates.Count);
        Assert.IsTrue(System.Text.Encoding.UTF8.GetByteCount(json + "\n") <= ControlProtocol.MaxFrameBytes);
        using var document = System.Text.Json.JsonDocument.Parse(json);
        Assert.AreEqual(
            candidateCount,
            document.RootElement.GetProperty("candidates").GetArrayLength());
    }

    [TestMethod]
    public void SnapshotFailureMakesCatalogUnavailableAndValidationFailClosed()
    {
        Global.Settings.Server.Add(new UnserializableServer());

        var catalog = new NetchProcessModeConfigurationCatalog();
        var result = catalog.GetCatalog();

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(ProcessModeConfigurationCatalogFailureReason.CatalogUnavailable, result.FailureReason);
        Assert.AreEqual(0, result.Candidates.Count);
        Assert.ThrowsException<InvalidOperationException>(() =>
            catalog.Validate("profile-0", "server-0"));
    }

    [TestMethod]
    public async Task CatalogValidationAndResolverUseTheSameFrozenSnapshotAsync()
    {
        AddServer("FROZEN_SERVER");
        AddProfile(0, "FROZEN_SERVER", "FROZEN_MODE");
        AddMode("FROZEN_MODE");
        var catalog = new NetchProcessModeConfigurationCatalog();
        var resolver = new NetchProcessModeSessionResolver(catalog);

        Global.Settings = new Setting();
        Global.Modes.Clear();

        var result = catalog.GetCatalog();
        var validation = catalog.Validate("profile-0", "server-0");
        var session = await resolver.ResolveAsync(
            CreateConfiguration("profile-0", "server-0"),
            NullStatusSink.Instance,
            CancellationToken.None);

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual(1, result.Candidates.Count);
        Assert.IsTrue(validation.Valid);
        Assert.IsNotNull(session);
    }

    [TestMethod]
    public async Task SnapshotResolutionFactsCannotDriftAfterCatalogConstructionAsync()
    {
        var server = new Socks5Server
        {
            Remark = "FROZEN_SERVER",
            Hostname = "frozen.example.invalid"
        };
        var profile = new Profile
        {
            Index = 0,
            ServerRemark = "FROZEN_SERVER",
            ModeRemark = "FROZEN_MODE"
        };
        var mode = new Redirector
        {
            Remark = new Dictionary<string, string> { ["en"] = "FROZEN_MODE" },
            FilterTCP = true,
            FullName = "C:\\frozen-mode.txt"
        };
        Global.Settings.Server.Add(server);
        Global.Settings.Profiles.Add(profile);
        Global.Settings.Redirector.DNSHost = "203.0.113.10:53";
        Global.Settings.Redirector.FilterTCP = true;
        Global.Settings.Socks5LocalPort = 38123;
        Global.Settings.LocalAddress = "127.0.0.7";
        Global.Settings.STUN_Server = "frozen-stun.example.invalid";
        Global.Settings.AioDNS.ListenPort = 15353;
        Global.Settings.V2RayConfig.AllowInsecure = false;
        Global.Settings.V2RayConfig.KcpConfig.mtu = 1234;
        Global.Modes.Add(mode);
        var catalog = new NetchProcessModeConfigurationCatalog();
        var resolver = new NetchProcessModeSessionResolver(catalog);

        server.Remark = "MUTATED_SERVER";
        server.Hostname = "mutated.example.invalid";
        mode.Remark["en"] = "MUTATED_MODE";
        mode.FilterTCP = false;
        mode.FullName = "C:\\mutated-mode.txt";
        Global.Settings.Redirector.DNSHost = "198.51.100.20:53";
        Global.Settings.Redirector.FilterTCP = false;
        Global.Settings.Socks5LocalPort = 48123;
        Global.Settings.LocalAddress = "0.0.0.0";
        Global.Settings.STUN_Server = "mutated-stun.example.invalid";
        Global.Settings.AioDNS.ListenPort = 25353;
        Global.Settings.V2RayConfig.AllowInsecure = true;
        Global.Settings.V2RayConfig.KcpConfig.mtu = 4321;

        var validation = catalog.Validate("profile-0", "server-0");
        var session = await resolver.ResolveAsync(
            CreateConfiguration("profile-0", "server-0"),
            NullStatusSink.Instance,
            CancellationToken.None);

        Assert.IsTrue(validation.Valid);
        Assert.IsNotNull(session);
        var frozenServer = (Socks5Server)session.GetType()
            .GetField("_server", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(session)!;
        var frozenMode = (Redirector)session.GetType()
            .GetField("_mode", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(session)!;
        Assert.AreNotSame(server, frozenServer);
        Assert.AreNotSame(mode, frozenMode);
        Assert.AreEqual("frozen.example.invalid", frozenServer.Hostname);
        Assert.AreEqual(true, frozenMode.FilterTCP);
        Assert.AreEqual("C:\\frozen-mode.txt", frozenMode.FullName);

        var frozenRuntimeSettings = session.GetType()
            .GetField("_runtimeSettings", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(session)!;
        var frozenRedirector = frozenRuntimeSettings.GetType().GetProperty("Redirector")!
            .GetValue(frozenRuntimeSettings)!;
        Assert.AreEqual(
            "203.0.113.10:53",
            frozenRedirector.GetType().GetProperty("DNSHost")!.GetValue(frozenRedirector));
        Assert.AreEqual(
            38123,
            Convert.ToInt32(frozenRuntimeSettings.GetType().GetProperty("Socks5LocalPort")!
                .GetValue(frozenRuntimeSettings)));
        Assert.AreEqual(
            "127.0.0.7",
            frozenRuntimeSettings.GetType().GetProperty("LocalAddress")!
                .GetValue(frozenRuntimeSettings));
        Assert.AreEqual(
            "frozen-stun.example.invalid",
            frozenRuntimeSettings.GetType().GetProperty("STUN_Server")!
                .GetValue(frozenRuntimeSettings));
        var frozenAioDns = frozenRuntimeSettings.GetType().GetProperty("AioDNS")!
            .GetValue(frozenRuntimeSettings)!;
        Assert.AreEqual(
            15353,
            Convert.ToInt32(frozenAioDns.GetType().GetProperty("ListenPort")!.GetValue(frozenAioDns)));
        var frozenV2Ray = frozenRuntimeSettings.GetType().GetProperty("V2RayConfig")!
            .GetValue(frozenRuntimeSettings)!;
        Assert.AreEqual(false, frozenV2Ray.GetType().GetProperty("AllowInsecure")!.GetValue(frozenV2Ray));
        var frozenKcp = frozenV2Ray.GetType().GetProperty("KcpConfig")!.GetValue(frozenV2Ray)!;
        Assert.AreEqual(
            1234,
            frozenKcp.GetType().GetProperty("mtu")!.GetValue(frozenKcp));
    }

    [TestMethod]
    public async Task ResolverRejectsEveryPairThatValidationRejectsAsync()
    {
        AddServer("SERVER_A");
        AddProfile(0, "SERVER_A", "MODE_A");
        AddMode("MODE_A");
        AddMode("MODE_A");
        var catalog = new NetchProcessModeConfigurationCatalog();
        var resolver = new NetchProcessModeSessionResolver(catalog);

        Assert.IsFalse(catalog.Validate("profile-0", "server-0").Valid);
        var exception = await Assert.ThrowsExceptionAsync<ProxyRuntimeException>(() =>
            resolver.ResolveAsync(
                CreateConfiguration("profile-0", "server-0"),
                NullStatusSink.Instance,
                CancellationToken.None));
        Assert.AreEqual(ProxyErrorCode.InvalidConfiguration, exception.Code);
    }

    [TestMethod]
    public async Task SerializedCatalogValidationAndDiagnosticsNeverContainHostileConfigurationSecretsAsync()
    {
        var originalEnvironmentMarker = Environment.GetEnvironmentVariable("NEKO_TEST_SECRET_MARKER");
        Environment.SetEnvironmentVariable("NEKO_TEST_SECRET_MARKER", "ENVIRONMENT_SECRET");
        AddServer("SERVER_REMARK_SECRET");
        AddProfile(0, "SERVER_REMARK_SECRET", "MODE_REMARK_SECRET");
        AddMode("MODE_REMARK_SECRET");
        var catalog = new NetchProcessModeConfigurationCatalog();
        var catalogJson = ControlProtocol.SerializeRuntimeConfigCatalog(
            "0123456789abcdef0123456789abcdef",
            catalog.GetCatalog());
        var validationJson = ControlProtocol.SerializeRuntimeConfigValidation(
            "fedcba9876543210fedcba9876543210",
            catalog.Validate("profile-0", "server-0"),
            succeeded: true);
        using var diagnosticWriter = new StringWriter();
        var resolver = new NetchProcessModeSessionResolver(
            catalog,
            new SanitizedTextCoreDiagnosticSink(diagnosticWriter));
        _ = await resolver.ResolveAsync(
            CreateConfiguration("profile-0", "server-0"),
            NullStatusSink.Instance,
            CancellationToken.None);
        var output = catalogJson + validationJson + diagnosticWriter;
        Environment.SetEnvironmentVariable("NEKO_TEST_SECRET_MARKER", originalEnvironmentMarker);

        foreach (var marker in new[]
                 {
                     "SECRET_HOST.example.invalid",
                     "203.0.113.77",
                     "48137",
                     "SECRET_USERNAME",
                     "SECRET_PASSWORD",
                     "SERVER_REMARK_SECRET",
                     "MODE_REMARK_SECRET",
                     "PROFILE_SECRET_LABEL",
                     "SERVER_JSON_SECRET",
                     "PERMIT_SECRET",
                     "JWT_SECRET",
                     "CHALLENGE_SECRET",
                     "CLAIMS_SECRET",
                     "PRIVATE_KEY_SECRET",
                     "ENVIRONMENT_SECRET",
                     "C:\\SECRET_CONFIGURATION_PATH"
                 })
        {
            Assert.IsFalse(output.Contains(marker, StringComparison.OrdinalIgnoreCase), marker);
        }
    }

    private static void ConfigureInvalidScenario(string scenario)
    {
        if (scenario == "ZERO_PROFILES")
        {
            AddServer("SERVER_A");
            AddMode("MODE_A");
            return;
        }

        AddProfile(0, "SERVER_A", "MODE_A");
        if (scenario == "ZERO_SERVERS")
            return;

        AddServer(scenario == "PROFILE_SERVER_MISMATCH" ? "SERVER_B" : "SERVER_A");
        if (scenario == "DUPLICATE_PROFILE_INDEX")
        {
            AddProfile(0, "SERVER_A", "MODE_A");
            AddMode("MODE_A");
            return;
        }

        if (scenario == "MODE_MISSING" || scenario == "PROFILE_SERVER_MISMATCH")
        {
            if (scenario == "PROFILE_SERVER_MISMATCH")
                AddMode("MODE_A");
            return;
        }

        AddMode("MODE_A");
        AddMode("MODE_A");
    }

    private static ProxyConfiguration CreateConfiguration(
        string profileReference,
        string serverReference) => new(
        ProxyModeKind.Process,
        "pso2.exe",
        profileReference,
        serverReference,
        targetPid: 4242);

    private static void AddProfile(int index, string serverRemark, string modeRemark) =>
        Global.Settings.Profiles.Add(new Profile
        {
            Index = index,
            ServerRemark = serverRemark,
            ModeRemark = modeRemark,
            ProfileName = "PROFILE_SECRET_LABEL"
        });

    private static void AddServer(string remark) =>
        Global.Settings.Server.Add(new Socks5Server
        {
            Remark = remark,
            Hostname = "SECRET_HOST.example.invalid",
            Port = 48137,
            Username = "SECRET_USERNAME",
            Password = "SECRET_PASSWORD",
            Group = "SERVER_JSON_SECRET PERMIT_SECRET JWT_SECRET CHALLENGE_SECRET CLAIMS_SECRET PRIVATE_KEY_SECRET",
            RemoteHostname = "203.0.113.77 C:\\SECRET_CONFIGURATION_PATH"
        });

    private static void AddMode(string remark) =>
        Global.Modes.Add(new Redirector
        {
            Remark = new Dictionary<string, string> { ["en"] = remark }
        });

    private sealed class NullStatusSink : IProxyStatusSink
    {
        public static readonly NullStatusSink Instance = new();

        public void OnStatusChanged(ProxyStatusEvent statusEvent)
        {
        }
    }

    private sealed class UnserializableServer : Server
    {
        public override string Type => "TEST";

        public string ExplosiveValue => throw new InvalidOperationException("SECRET_EXCEPTION_TEXT");

        public override string MaskedData() => string.Empty;
    }
}
