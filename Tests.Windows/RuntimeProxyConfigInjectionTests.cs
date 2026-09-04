using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NekoProxyCore.Core;
using NekoProxyCore.Legacy;
using Netch;
using Netch.Models;
using Netch.Models.Modes;
using Netch.Models.Modes.ProcessMode;
using Netch.Servers;

namespace Tests.Windows;

[TestClass]
[DoNotParallelize]
public sealed class RuntimeProxyConfigInjectionTests
{
    private const string Sentinel = "SENTINEL_PROXY_SECRET_42";
    private Setting? _originalSettings;
    private Mode[]? _originalModes;

    [TestInitialize]
    public void Initialize()
    {
        _originalSettings = Global.Settings;
        _originalModes = Global.Modes.ToArray();
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (_originalSettings != null) Global.Settings = _originalSettings;
        Global.Modes.Clear();
        if (_originalModes != null) Global.Modes.AddRange(_originalModes);
        ResetControllerSeams();
    }

    [DataTestMethod]
    [DataRow(1)]
    [DataRow(65535)]
    public async Task RuntimeConfigMutatesOnlyFreshResolvedClone(int port)
    {
        var template = (ShadowsocksServer)Configure(new ShadowsocksServer
        {
            Remark = "server-template", Hostname = "template.invalid", Port = 9999,
            EncryptMethod = "aes-128-gcm", Password = "template-password", Plugin = "plugin", PluginOption = "option"
        });
        var settings = Global.Settings;
        var mode = Global.Modes[0];
        var catalog = new NetchProcessModeConfigurationCatalog();
        var resolver = new NetchProcessModeSessionResolver(catalog);
        var runtime = Runtime(port: port);

        var first = await resolver.ResolveAsync(Configuration(), runtime, Sink.Instance, CancellationToken.None);
        var injected = Server(first);
        Assert.AreNotSame(template, injected);
        Assert.AreEqual("template.invalid", injected.Hostname);
        Assert.AreEqual((ushort)9999, injected.Port);
        Assert.AreEqual("aes-128-gcm", injected.EncryptMethod);
        Assert.AreEqual("template-password", injected.Password);
        SetControllerSeams((_, _, _) => Task.CompletedTask, () => Task.CompletedTask);
        await first.StartAsync(CancellationToken.None);
        Assert.AreEqual("runtime.example.invalid", injected.Hostname);
        Assert.AreEqual((ushort)port, injected.Port);
        Assert.AreEqual("aes-256-gcm", injected.EncryptMethod);
        Assert.AreEqual(Sentinel, injected.Password);
        Assert.AreEqual("plugin", injected.Plugin);
        Assert.AreEqual("option", injected.PluginOption);
        Assert.AreNotSame(settings, Global.Settings);
        Assert.AreSame(mode, Global.Modes[0]);
        Assert.AreEqual("template.invalid", template.Hostname);
        Assert.AreEqual((ushort)9999, template.Port);
        Assert.AreEqual("aes-128-gcm", template.EncryptMethod);
        Assert.AreEqual("template-password", template.Password);
        await first.StopAsync(CancellationToken.None);
        Assert.AreSame(settings, Global.Settings);

        var second = await resolver.ResolveAsync(Configuration(), Sink.Instance, CancellationToken.None);
        var clean = Server(second);
        Assert.AreNotSame(injected, clean);
        Assert.AreEqual("template.invalid", clean.Hostname);
        Assert.AreEqual((ushort)9999, clean.Port);
        Assert.AreEqual("aes-128-gcm", clean.EncryptMethod);
        Assert.AreEqual("template-password", clean.Password);
    }

    [DataTestMethod]
    [DataRow("shadowsocks", "AES-256-GCM", 8388)]
    [DataRow("shadowsocks", " aes-256-gcm", 8388)]
    [DataRow("shadowsocks", "aes-256-gcm ", 8388)]
    [DataRow("shadowsocks", "sentinel-cipher-value", 8388)]
    public async Task UnsupportedDynamicValuesAreRejectedWithoutDisclosure(string protocol, string cipher, int port)
    {
        Configure(new ShadowsocksServer { Remark = "server-template" });
        using var writer = new StringWriter();
        var resolver = new NetchProcessModeSessionResolver(new SanitizedTextCoreDiagnosticSink(writer));
        var exception = await Assert.ThrowsExceptionAsync<ProxyRuntimeException>(() =>
            resolver.ResolveAsync(Configuration(), Runtime(protocol, cipher, port), Sink.Instance, CancellationToken.None));
        Assert.AreEqual(ProxyErrorCode.InvalidConfiguration, exception.Code);
        var text = exception + writer.ToString();
        Assert.IsFalse(text.Contains(Sentinel, StringComparison.Ordinal));
        Assert.IsFalse(text.Contains("sentinel-cipher-value", StringComparison.Ordinal));
        Assert.IsFalse(text.Contains("runtime.example.invalid", StringComparison.Ordinal));
    }

    [DataTestMethod]
    [DataRow("SHADOWSOCKS", 8388)]
    [DataRow(" shadowsocks", 8388)]
    [DataRow("shadowsocks ", 8388)]
    [DataRow("shadowsocks", 0)]
    [DataRow("shadowsocks", 65536)]
    public void RuntimeConfigRejectsProtocolAndPortBoundsWithoutDynamicDisclosure(string protocol, int port)
    {
        Exception? exception = null;
        try { _ = Runtime(protocol: protocol, port: port); }
        catch (ArgumentException caught) { exception = caught; }
        Assert.IsNotNull(exception);
        var text = exception.ToString();
        Assert.IsFalse(text.Contains(Sentinel, StringComparison.Ordinal));
        Assert.IsFalse(text.Contains("runtime.example.invalid", StringComparison.Ordinal));
        Assert.IsFalse(text.Contains(protocol, StringComparison.Ordinal));
    }

    [DataTestMethod]
    [DataRow("SOCKS5")]
    [DataRow("SSR")]
    public async Task NonShadowsocksTemplateIsRejected(string type)
    {
        Server server = type == "SSR"
            ? new ShadowsocksRServer { Remark = "server-template" }
            : new Socks5Server { Remark = "server-template" };
        Configure(server);
        var exception = await Assert.ThrowsExceptionAsync<ProxyRuntimeException>(() =>
            new NetchProcessModeSessionResolver().ResolveAsync(
                Configuration(), Runtime(), Sink.Instance, CancellationToken.None));
        Assert.AreEqual(ProxyErrorCode.InvalidConfiguration, exception.Code);
        Assert.IsFalse(exception.ToString().Contains(Sentinel, StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task FailedDirectStartCleansAndConcurrentRepeatedStopIsExactlyOnce()
    {
        Configure(new ShadowsocksServer { Remark = "server-template" });
        var original = Global.Settings;
        var session = await new NetchProcessModeSessionResolver().ResolveAsync(
            Configuration(), Runtime(), Sink.Instance, CancellationToken.None);
        var stopCalls = 0;
        SetControllerSeams(
            (_, _, _) => Task.FromException(new InvalidOperationException("start failure")),
            () => { Interlocked.Increment(ref stopCalls); return Task.CompletedTask; });

        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => session.StartAsync(CancellationToken.None));
        Assert.AreSame(original, Global.Settings);
        Assert.AreEqual(string.Empty, Server(session).Password);
        Assert.AreEqual(1, stopCalls);
        await Task.WhenAll(session.StopAsync(CancellationToken.None), session.StopAsync(CancellationToken.None));
        Assert.AreEqual(1, stopCalls);
    }

    [TestMethod]
    public async Task CancelledLeaseWaiterNeverStopsControllerAndLeaseRemainsReusable()
    {
        Configure(new ShadowsocksServer { Remark = "server-template" });
        var resolver = new NetchProcessModeSessionResolver();
        var first = await resolver.ResolveAsync(Configuration(), Runtime(), Sink.Instance, CancellationToken.None);
        var second = await resolver.ResolveAsync(Configuration(), Runtime(), Sink.Instance, CancellationToken.None);
        var stopCalls = 0;
        SetControllerSeams((_, _, _) => Task.CompletedTask,
            () => { Interlocked.Increment(ref stopCalls); return Task.CompletedTask; });
        await first.StartAsync(CancellationToken.None);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsExceptionAsync<TaskCanceledException>(() => second.StartAsync(cancelled.Token));
        await second.StopAsync(CancellationToken.None);
        Assert.AreEqual(0, stopCalls);
        await Task.WhenAll(first.StopAsync(CancellationToken.None), first.StopAsync(CancellationToken.None));
        Assert.AreEqual(1, stopCalls);
        await second.StartAsync(CancellationToken.None);
        await second.StopAsync(CancellationToken.None);
        Assert.AreEqual(2, stopCalls);
    }

    [TestMethod]
    public async Task CancelledStopWaitRetainsSettingsAndLeaseUntilUnderlyingStopCompletes()
    {
        Configure(new ShadowsocksServer { Remark = "server-template" });
        var original = Global.Settings;
        var resolver = new NetchProcessModeSessionResolver();
        var first = await resolver.ResolveAsync(Configuration(), Runtime(), Sink.Instance, CancellationToken.None);
        var second = await resolver.ResolveAsync(Configuration(), Runtime(), Sink.Instance, CancellationToken.None);
        var stopCompletion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var stopCalls = 0;
        SetControllerSeams((_, _, _) => Task.CompletedTask, () =>
        {
            Interlocked.Increment(ref stopCalls);
            return stopCompletion.Task;
        });

        await first.StartAsync(CancellationToken.None);
        var runtimeSettings = Global.Settings;
        using var cancelled = new CancellationTokenSource();
        var stopping = first.StopAsync(cancelled.Token);
        cancelled.Cancel();
        await Assert.ThrowsExceptionAsync<TaskCanceledException>(() => stopping);
        Assert.AreEqual(1, stopCalls);

        var secondStart = second.StartAsync(CancellationToken.None);
        Assert.IsFalse(secondStart.IsCompleted);
        Assert.AreSame(runtimeSettings, Global.Settings);

        stopCompletion.SetResult(null);
        await secondStart;
        Assert.AreEqual(1, stopCalls);
        await second.StopAsync(CancellationToken.None);
        Assert.AreSame(original, Global.Settings);
        Assert.AreEqual(2, stopCalls);
    }

    [TestMethod]
    public async Task EngineRetainsCancelledStopOwnershipAndLaterFinalizesWithoutStoppingNextSession()
    {
        Configure(new ShadowsocksServer { Remark = "server-template" });
        var resolver = new NetchProcessModeSessionResolver();
        var firstEngine = new NetchProcessModeEngine(resolver);
        var secondEngine = new NetchProcessModeEngine(resolver);
        var stopCompletion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var stopCalls = 0;
        SetControllerSeams((_, _, _) => Task.CompletedTask, () =>
        {
            var attempt = Interlocked.Increment(ref stopCalls);
            return attempt == 1 ? stopCompletion.Task : Task.CompletedTask;
        });

        await ((IRuntimeConfiguredProcessModeEngine)firstEngine).StartAsync(
            Configuration(), Runtime(), CancellationToken.None);
        using var cancelled = new CancellationTokenSource();
        var stopping = firstEngine.StopAsync(cancelled.Token);
        cancelled.Cancel();
        await Assert.ThrowsExceptionAsync<TaskCanceledException>(() => stopping);

        stopCompletion.SetResult(null);
        await firstEngine.StopAsync(CancellationToken.None);
        Assert.AreEqual(1, stopCalls);

        await ((IRuntimeConfiguredProcessModeEngine)secondEngine).StartAsync(
            Configuration(), Runtime(), CancellationToken.None);
        await firstEngine.StopAsync(CancellationToken.None);
        Assert.AreEqual(1, stopCalls);
        await secondEngine.StopAsync(CancellationToken.None);
        Assert.AreEqual(2, stopCalls);
    }

    [TestMethod]
    public async Task FaultedUnderlyingStopRetainsLeaseClearsPasswordAndCanRetryExactlyOnce()
    {
        Configure(new ShadowsocksServer { Remark = "server-template" });
        var original = Global.Settings;
        var resolver = new NetchProcessModeSessionResolver();
        var first = await resolver.ResolveAsync(Configuration(), Runtime(), Sink.Instance, CancellationToken.None);
        var second = await resolver.ResolveAsync(Configuration(), Runtime(), Sink.Instance, CancellationToken.None);
        var stopCalls = 0;
        SetControllerSeams((_, _, _) => Task.CompletedTask, () =>
        {
            var attempt = Interlocked.Increment(ref stopCalls);
            return attempt == 1
                ? Task.FromException(new InvalidOperationException("stop failure"))
                : Task.CompletedTask;
        });

        await first.StartAsync(CancellationToken.None);
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => first.StopAsync(CancellationToken.None));
        Assert.AreEqual(string.Empty, Server(first).Password);

        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsExceptionAsync<TaskCanceledException>(() => second.StartAsync(cancelled.Token));
        await first.StopAsync(CancellationToken.None);
        Assert.AreSame(original, Global.Settings);
        Assert.AreEqual(2, stopCalls);
        await first.StopAsync(CancellationToken.None);
        Assert.AreEqual(2, stopCalls);

        await second.StartAsync(CancellationToken.None);
        await second.StopAsync(CancellationToken.None);
        Assert.AreEqual(3, stopCalls);
    }

    private static Server Configure(Server server)
    {
        var settings = new Setting();
        settings.Server.Add(server);
        settings.Profiles.Add(new Profile { Index = 0, ServerRemark = "server-template", ModeRemark = "mode-template" });
        Global.Settings = settings;
        Global.Modes.Clear();
        Global.Modes.Add(new Redirector { Remark = new Dictionary<string, string> { ["en"] = "mode-template" } });
        return server;
    }

    private static ProxyConfiguration Configuration() =>
        new(ProxyModeKind.Process, "pso2.exe", "profile-0", "server-0", targetPid: 42);

    private static RuntimeProxyConfig Runtime(string protocol = "shadowsocks", string cipher = "aes-256-gcm", int port = 8388) =>
        new(1, 18, "server-id", "runtime.example.invalid", port, protocol, cipher,
            new SensitiveRuntimeCredential(Sentinel), 2_000_000_000, 2_000_000_120);

    private static ShadowsocksServer Server(ILegacyProcessModeSession session) =>
        (ShadowsocksServer)(session.GetType().GetField("_server", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(session)!);

    private static Type SessionType => typeof(NetchProcessModeSessionResolver)
        .GetNestedType("NetchProcessModeSession", BindingFlags.NonPublic)!;

    private static void SetControllerSeams(
        Func<Server, Redirector, IProxyStatusSink, Task> start,
        Func<Task> stop)
    {
        SessionType.GetField("StartControllerAsync", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, start);
        SessionType.GetField("StopControllerAsync", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, stop);
    }

    private static void ResetControllerSeams() => SetControllerSeams(
        (server, mode, sink) => Netch.Controllers.MainController.StartAsync(
            server, mode, sink, openLogOnUnhandledException: false),
        Netch.Controllers.MainController.StopAsync);

    private sealed class Sink : IProxyStatusSink
    {
        public static readonly Sink Instance = new();
        public void OnStatusChanged(ProxyStatusEvent statusEvent) { }
    }
}
