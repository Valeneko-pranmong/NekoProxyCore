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
public sealed class NetchProcessModeSessionResolverTests
{
    private Setting? _originalSettings;
    private Mode[]? _originalModes;

    [TestInitialize]
    public void SaveGlobalState()
    {
        _originalSettings = Global.Settings;
        _originalModes = Global.Modes.ToArray();
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

    [DataTestMethod]
    [DataRow("PROFILE_MISSING", "SESSION_PROFILE_NOT_FOUND")]
    [DataRow("SERVER_MISSING", "SESSION_SERVER_NOT_FOUND")]
    [DataRow("PROFILE_SERVER_MISMATCH", "SESSION_PROFILE_SERVER_MISMATCH")]
    [DataRow("MODE_MISSING", "SESSION_MODE_NOT_FOUND")]
    [DataRow("MODE_AMBIGUOUS", "SESSION_MODE_AMBIGUOUS")]
    public async Task InvalidRuntimeStatePreservesInternalCauseAndUsesSafeWireMappingAsync(
        string scenario,
        string expectedCategory)
    {
        ConfigureScenario(scenario);
        using var writer = new StringWriter();
        var diagnostics = new SanitizedTextCoreDiagnosticSink(writer);
        var configuration = CreateConfiguration();
        var resolver = new NetchProcessModeSessionResolver(diagnostics);

        var resolverException = await Assert.ThrowsExceptionAsync<ProxyRuntimeException>(
            () => resolver.ResolveAsync(configuration, NullStatusSink.Instance, CancellationToken.None));
        Assert.AreEqual(ProxyErrorCode.InvalidConfiguration, resolverException.Code);

        var directEngine = new NetchProcessModeEngine(resolver, diagnostics: diagnostics);
        var engineException = await Assert.ThrowsExceptionAsync<ProxyRuntimeException>(
            () => directEngine.StartAsync(configuration, CancellationToken.None));
        Assert.AreEqual(ProxyErrorCode.InvalidConfiguration, engineException.Code);

        var engine = new NetchProcessModeEngine(resolver, diagnostics: diagnostics);
        var coordinator = new HeadlessRuntimeCoordinator(
            new ProcessModeController(new ExactProcessResolver(), engine, diagnostics),
            new AllowStartAuthorizer(),
            diagnostics: diagnostics,
            statusSink: null,
            clock: null);
        var result = await coordinator.StartAsync(new ProxyStartRequest(configuration, "runtime-state"));
        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(ProxyErrorCode.InvalidConfiguration, result.Error!.Code);

        var response = ControlResponse.FromResult(result, null, diagnostics);
        Assert.AreEqual(ProxyErrorCode.ConfigurationMismatch, response.ErrorCode);

        var output = writer.ToString();
        StringAssert.Contains(output, "stage=PROCESS_PRECONDITION category=STAGE_COMPLETED");
        StringAssert.Contains(output, $"stage=SESSION_RESOLVE category={expectedCategory}");
        StringAssert.Contains(output, "stage=ENGINE_START category=ENGINE_START_PROXY_ERROR");
        StringAssert.Contains(output, "stage=RUNTIME_START category=RUNTIME_START_PROXY_ERROR");
        StringAssert.Contains(
            output,
            "stage=CONTROL_RESPONSE category=RUNTIME_INVALID_CONFIGURATION_MAPPED_TO_CONFIGURATION_MISMATCH");
        Assert.IsFalse(output.Contains("ENGINE_START_ENTERED", StringComparison.Ordinal));
        Assert.IsFalse(
            output.Contains("CONTROL_ERROR_TRANSLATED_TO_AUTHORIZATION_UNAVAILABLE", StringComparison.Ordinal));
        AssertNoSensitiveTokens(output);
    }

    [TestMethod]
    public async Task ValidRuntimeStateResolvesExactlyOneSessionWithoutStartingNativeRuntimeAsync()
    {
        ConfigureScenario("VALID");
        using var writer = new StringWriter();
        var diagnostics = new SanitizedTextCoreDiagnosticSink(writer);
        var resolver = new NetchProcessModeSessionResolver(diagnostics);

        var session = await resolver.ResolveAsync(
            CreateConfiguration(),
            NullStatusSink.Instance,
            CancellationToken.None);

        Assert.IsNotNull(session);
        Assert.AreEqual(
            "NEKO_CORE_DIAGNOSTIC stage=SESSION_RESOLVE category=STAGE_COMPLETED" +
            Environment.NewLine,
            writer.ToString());
        AssertNoSensitiveTokens(writer.ToString());
    }

    private static void ConfigureScenario(string scenario)
    {
        var settings = new Setting();
        Global.Settings = settings;
        Global.Modes.Clear();

        if (scenario == "PROFILE_MISSING")
            return;

        var profile = new Profile
        {
            Index = 0,
            ServerRemark = "synthetic-server-a",
            ModeRemark = "synthetic-mode"
        };
        settings.Profiles.Add(profile);
        if (scenario == "SERVER_MISSING")
            return;

        settings.Server.Add(new Socks5Server { Remark = "synthetic-server-b" });
        if (scenario == "PROFILE_SERVER_MISMATCH")
            return;

        settings.Server[0].Remark = profile.ServerRemark;
        if (scenario == "MODE_MISSING")
            return;

        Global.Modes.Add(CreateMode(profile.ModeRemark));
        if (scenario == "MODE_AMBIGUOUS")
            Global.Modes.Add(CreateMode(profile.ModeRemark));
    }

    private static Redirector CreateMode(string remark) => new()
    {
        Remark = new Dictionary<string, string> { ["en"] = remark }
    };

    private static ProxyConfiguration CreateConfiguration() => new(
        ProxyModeKind.Process,
        "pso2.exe",
        "profile-0",
        "server-0",
        targetPid: 4242);

    private static void AssertNoSensitiveTokens(string output)
    {
        foreach (var marker in new[]
                 {
                     "synthetic-server-a",
                     "synthetic-server-b",
                     "synthetic-mode",
                     "password",
                     "token",
                     "hostname"
                 })
        {
            Assert.IsFalse(output.Contains(marker, StringComparison.OrdinalIgnoreCase), marker);
        }
    }

    private sealed class AllowStartAuthorizer : IProxyStartAuthorizer
    {
        public Task<ProxyError?> AuthorizeAsync(ProxyStartRequest request) =>
            Task.FromResult<ProxyError?>(null);
    }

    private sealed class ExactProcessResolver : IProcessResolver, IExactProcessResolver
    {
        public Task<bool> IsRunningAsync(string processName, CancellationToken cancellationToken) =>
            Task.FromResult(true);

        public Task WaitForExitAsync(string processName, CancellationToken cancellationToken) =>
            Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);

        public Task<bool> IsExactProcessRunningAsync(
            string processName,
            uint targetPid,
            CancellationToken cancellationToken) => Task.FromResult(true);

        public Task WaitForExactProcessExitAsync(
            string processName,
            uint targetPid,
            CancellationToken cancellationToken) =>
            Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
    }

    private sealed class NullStatusSink : IProxyStatusSink
    {
        public static readonly NullStatusSink Instance = new();

        public void OnStatusChanged(ProxyStatusEvent statusEvent)
        {
        }
    }
}
