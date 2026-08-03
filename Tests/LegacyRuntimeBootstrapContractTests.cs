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

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Netch.sln")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new AssertFailedException("Unable to locate the repository root.");
    }
}
