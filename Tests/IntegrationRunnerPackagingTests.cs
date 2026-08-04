using System;
using System.Diagnostics;
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
        Assert.IsTrue(script.Contains("$runnerProcess.WaitForExit()", StringComparison.Ordinal));
        Assert.IsTrue(script.Contains("$runnerProcess.Refresh()", StringComparison.Ordinal));
        Assert.IsTrue(script.Contains("[int]$runnerExitCode = $runnerProcess.ExitCode", StringComparison.Ordinal));
        Assert.IsTrue(script.Contains("[int]$TrafficWindowSeconds = 300", StringComparison.Ordinal));
        Assert.IsTrue(script.Contains("$TrafficWindowSeconds.ToString", StringComparison.Ordinal));
        Assert.IsTrue(script.Contains("$runnerTimeoutMilliseconds = ($TrafficWindowSeconds + 180) * 1000", StringComparison.Ordinal));
        Assert.IsTrue(script.Contains("taskkill.exe /PID $runnerProcess.Id /T /F", StringComparison.Ordinal));

        var runnerSource = File.ReadAllText(Path.Combine(repositoryRoot, "NekoProxyCore.IntegrationRunner", "Program.cs"));
        Assert.IsTrue(runnerSource.Contains("TRAFFIC_WINDOW status=Ready", StringComparison.Ordinal));
        Assert.IsTrue(runnerSource.Contains("TRAFFIC_WINDOW status=Complete", StringComparison.Ordinal));
        Assert.IsTrue(runnerSource.Contains("trafficWindowSeconds", StringComparison.Ordinal));
        Assert.IsTrue(
            runnerSource.IndexOf("try", StringComparison.Ordinal) <
            runnerSource.IndexOf("ParseTrafficWindowSeconds(args.ElementAtOrDefault(3))", StringComparison.Ordinal),
            "Traffic-window validation must execute inside the sanitized runner exception boundary.");
        Assert.IsFalse(script.Contains("-Wait `", StringComparison.Ordinal));
        Assert.IsTrue(script.Contains("RedirectStandardOutput", StringComparison.Ordinal));
        Assert.IsTrue(script.Contains("$allowedOutputPattern", StringComparison.Ordinal));
        Assert.IsTrue(script.Contains("finally", StringComparison.Ordinal));
        Assert.IsTrue(script.Contains("Remove-Item -LiteralPath $temporaryRoot -Recurse -Force", StringComparison.Ordinal));
        Assert.IsFalse(script.Contains("GenerateEvidence", StringComparison.Ordinal));
        Assert.IsFalse(script.Contains("PASS_REPORT", StringComparison.Ordinal));
    }

    [TestMethod]
    public void PowerShellProcessExitCodeProbePropagatesChildFailure()
    {
        var probe = string.Join(
            "; ",
            "$process = Start-Process -FilePath 'cmd.exe' -ArgumentList @('/d', '/c', 'exit 3') -PassThru",
            "if (-not $process.WaitForExit(10000)) { exit 90 }",
            "$process.WaitForExit()",
            "$process.Refresh()",
            "$code = $process.ExitCode",
            "Write-Output \"RUNNER exit=$code\"",
            "exit $code");
        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -NonInteractive -Command \"{probe.Replace("\"", "\\\"")}\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var process = Process.Start(startInfo);
        Assert.IsNotNull(process);
        var output = process!.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        Assert.IsTrue(process.WaitForExit(10000), "PowerShell exit-code probe timed out.");

        Assert.AreEqual(3, process.ExitCode, error);
        StringAssert.Contains(output, "RUNNER exit=3");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Netch.sln")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new AssertFailedException("Unable to locate the repository root.");
    }
}
