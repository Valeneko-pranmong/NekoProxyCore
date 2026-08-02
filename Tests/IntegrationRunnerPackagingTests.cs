using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tests;

[TestClass]
public sealed class IntegrationRunnerPackagingTests
{
    [TestMethod]
    public void IntegrationRunnerPinsWinX64AndStagesWindowsRuntimeAsset()
    {
        var repositoryRoot = FindRepositoryRoot();
        var projectPath = Path.Combine(
            repositoryRoot,
            "NekoProxyCore.IntegrationRunner",
            "NekoProxyCore.IntegrationRunner.csproj");

        Assert.IsTrue(File.Exists(projectPath), "The official integration runner project is missing.");

        var document = XDocument.Load(projectPath);
        var runtimeIdentifier = document.Descendants("RuntimeIdentifier").SingleOrDefault()?.Value;
        var platformTarget = document.Descendants("PlatformTarget").SingleOrDefault()?.Value;
        var useWindowsForms = document.Descendants("UseWindowsForms").SingleOrDefault()?.Value;
        var references = document
            .Descendants("Reference")
            .Select(item => item.Attribute("Include")?.Value)
            .Where(item => item != null)
            .ToArray();
        var runtimeAssetDirectory = document
            .Descendants("WindowsRuntimeAssetDirectory")
            .SingleOrDefault();
        var stageTarget = document
            .Descendants("Target")
            .SingleOrDefault(item => item.Attribute("Name")?.Value == "StageWindowsRuntimeAssets");

        Assert.AreEqual("win-x64", runtimeIdentifier);
        Assert.AreEqual("x64", platformTarget);
        Assert.AreEqual("true", useWindowsForms);
        CollectionAssert.IsSubsetOf(
            new[]
            {
                "NekoProxyCore.Core",
                "NekoProxyCore.Windows",
                "NekoProxyCore.Legacy",
                "Netch"
            },
            references!);
        Assert.IsNotNull(runtimeAssetDirectory);
        StringAssert.Contains(runtimeAssetDirectory.Value.Replace('/', '\\'), "runtimes\\win\\lib\\net6.0");
        Assert.IsTrue(document.Descendants("WindowsRuntimeAsset").Any());
        Assert.IsNotNull(stageTarget);
        Assert.AreEqual("Publish", stageTarget.Attribute("AfterTargets")?.Value);
    }

    [TestMethod]
    public void IntegrationScriptPublishesAndCleansTemporaryRuntimeInFinally()
    {
        var repositoryRoot = FindRepositoryRoot();
        var scriptPath = Path.Combine(repositoryRoot, "tools", "run-processmode-integration.ps1");

        Assert.IsTrue(File.Exists(scriptPath), "The official integration launcher script is missing.");

        var script = File.ReadAllText(scriptPath);
        Assert.IsTrue(script.Contains("dotnet publish", StringComparison.Ordinal));
        Assert.IsTrue(script.Contains("Get-FileHash", StringComparison.Ordinal));
        Assert.IsTrue(script.Contains("$windowsRuntimeAssets", StringComparison.Ordinal));
        Assert.IsFalse(script.Contains("[string]$RuntimeRoot", StringComparison.Ordinal));
        Assert.IsTrue(script.Contains("$RuntimeRoot = Join-Path $repositoryRoot 'Original setting'", StringComparison.Ordinal));
        Assert.IsTrue(script.Contains("WaitForExit($runnerTimeoutMilliseconds)", StringComparison.Ordinal));
        Assert.IsTrue(script.Contains("taskkill.exe /PID $runnerProcess.Id /T /F", StringComparison.Ordinal));
        Assert.IsFalse(script.Contains("-Wait `", StringComparison.Ordinal));
        Assert.IsTrue(script.Contains("RedirectStandardOutput", StringComparison.Ordinal));
        Assert.IsTrue(script.Contains("$allowedOutputPattern", StringComparison.Ordinal));
        Assert.IsTrue(script.Contains("finally", StringComparison.Ordinal));
        Assert.IsTrue(script.Contains("Remove-Item -LiteralPath $temporaryRoot -Recurse -Force", StringComparison.Ordinal));
        Assert.IsFalse(script.Contains("GenerateEvidence", StringComparison.Ordinal));
        Assert.IsFalse(script.Contains("PASS_REPORT", StringComparison.Ordinal));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Netch.sln")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new AssertFailedException("Unable to locate the repository root.");
    }
}
