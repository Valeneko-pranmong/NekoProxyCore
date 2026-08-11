using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tests;

[TestClass]
public sealed class LegacyRuntimeBootstrapContractTests
{
    [TestMethod]
    public void BootstrapExistsAndAvoidsUiAndConsoleBoundaries()
    {
        var repositoryRoot = FindRepositoryRoot();
        var path = Path.Combine(repositoryRoot, "NekoProxyCore.Legacy", "NetchRuntimeBootstrap.cs");
        Assert.IsTrue(File.Exists(path), "The shared non-UI runtime bootstrap is missing.");

        var source = File.ReadAllText(path);
        foreach (var forbidden in new[]
                 {
                     "Global.MainForm",
                     "Application.",
                     "System.Windows.Forms",
                     "MessageBox",
                     "NotifyIcon",
                     "ModeService.Load",
                     "Program.CreateLogger",
                     "Console."
                 })
        {
            Assert.IsFalse(source.Contains(forbidden, StringComparison.Ordinal), $"Bootstrap references forbidden boundary: {forbidden}");
        }

        StringAssert.Contains(source, "Configuration.LoadAsync");
        StringAssert.Contains(source, "ModeHelper.LoadMode");
        StringAssert.Contains(source, "Directory.SetCurrentDirectory");
    }

    [TestMethod]
    public void IntegrationRunnerUsesSharedBootstrapInsteadOfDuplicatingStateLoading()
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(repositoryRoot, "NekoProxyCore.IntegrationRunner", "Program.cs"));

        Assert.IsTrue(source.Contains("NetchRuntimeBootstrap.InitializeAsync(runtimeRoot)", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("private static async Task LoadRuntimeStateAsync", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("ModeHelper.LoadMode", StringComparison.Ordinal));
    }

    [TestMethod]
    public void ProductionHostRequiresProtectedSettingsWithoutPlaintextFallback()
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(repositoryRoot, "NekoProxyCore.Host", "Program.cs"));

        Assert.IsTrue(source.Contains("NetchRuntimeBootstrap.InitializeProtectedAsync", StringComparison.Ordinal));
        Assert.IsTrue(source.Contains("ProductionProtectedSettings.LoadKey", StringComparison.Ordinal));
        Assert.IsTrue(source.Contains("ProtectedSettingsPayload.DefaultFileName", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("NetchRuntimeBootstrap.InitializeAsync", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("settings.json", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void ProductionPublishVerifiesProtectedPayloadAndKeyPair()
    {
        var repositoryRoot = FindRepositoryRoot();
        var project = File.ReadAllText(Path.Combine(repositoryRoot, "NekoProxyCore.Host", "NekoProxyCore.Host.csproj"));
        var tool = File.ReadAllText(Path.Combine(repositoryRoot, "NekoProxyCore.SettingsTool", "Program.cs"));

        Assert.IsTrue(project.Contains("VerifyProtectedSettingsPair", StringComparison.Ordinal));
        Assert.IsTrue(project.Contains("ProtectedSettingsProvisioningTool", StringComparison.Ordinal));
        Assert.IsTrue(tool.Contains("ProtectedSettingsProvisioner.VerifyAsync", StringComparison.Ordinal));
    }

    [TestMethod]
    public void SocksProductionPathStreamsChildConfigurationWithoutPlaintextTempFile()
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(repositoryRoot, "Netch", "Servers", "V2ray", "V2rayController.cs"));

        Assert.IsFalse(source.Contains("Constants.TempConfig", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("FileStream", StringComparison.Ordinal));
        Assert.IsTrue(source.Contains("StartGuardWithStandardInputAsync", StringComparison.Ordinal));
    }

    [TestMethod]
    public void SessionRestoresFrozenRuntimeSettingsAtLegacyStartBoundary()
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "NekoProxyCore.Legacy",
            "NetchProcessModeSessionResolver.cs"));

        var restoreIndex = source.IndexOf("Global.Settings = _runtimeSettings", StringComparison.Ordinal);
        var startIndex = source.IndexOf("MainController.StartAsync(", restoreIndex, StringComparison.Ordinal);
        var releaseIndex = source.IndexOf("Global.Settings = _liveSettings", startIndex, StringComparison.Ordinal);

        Assert.IsTrue(restoreIndex >= 0, "The frozen runtime settings are not restored at the legacy boundary.");
        Assert.IsTrue(startIndex > restoreIndex, "Legacy runtime start does not consume the restored settings snapshot.");
        Assert.IsTrue(releaseIndex > startIndex, "The legacy boundary does not release the frozen global settings after stop.");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Netch.sln")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new AssertFailedException("Unable to locate the repository root.");
    }
}
