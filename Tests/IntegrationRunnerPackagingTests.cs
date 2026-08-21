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
    public void LegacyProjectDefinesHeadlessBuildWithoutUiResources()
    {
        var repositoryRoot = FindRepositoryRoot();
        var projectPath = Path.Combine(repositoryRoot, "Netch", "Netch.csproj");
        var document = XDocument.Load(projectPath);

        var headlessResources = document
            .Descendants("ItemGroup")
            .Where(item => string.Equals(
                item.Attribute("Condition")?.Value,
                "'$(HeadlessCoreBuild)' == 'true'",
                StringComparison.Ordinal))
            .Descendants("EmbeddedResource")
            .ToArray();

        Assert.IsTrue(
            headlessResources.Any(item => item.Attribute("Remove")?.Value == "Properties\\Resources.resx"),
            "Headless Core builds must exclude legacy UI resources that are not used by ProcessMode.");
    }

    [TestMethod]
    public void ProductionHostContainsBoundedCurrentUserPipeServer()
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "NekoProxyCore.Host",
            "HeadlessControlServer.cs"));

        Assert.IsTrue(source.Contains("PipeOptions.CurrentUserOnly", StringComparison.Ordinal));
        Assert.IsTrue(source.Contains("ControlProtocol.MaxFrameBytes", StringComparison.Ordinal));
        Assert.IsTrue(source.Contains("oneByte[0] == (byte)'\\n'", StringComparison.Ordinal));
        Assert.IsTrue(source.Contains("ControlCommand.Challenge", StringComparison.Ordinal));
        Assert.IsTrue(source.Contains("ControlCommand.Start", StringComparison.Ordinal));
    }

    [TestMethod]
    public void ProductionHostBuildsAsHeadlessWinX64Executable()
    {
        var repositoryRoot = FindRepositoryRoot();
        var projectPath = Path.Combine(repositoryRoot, "NekoProxyCore.Host", "NekoProxyCore.Host.csproj");
        var document = XDocument.Load(projectPath);

        Assert.IsTrue(document.Descendants("OutputType").Any(item => item.Value == "WinExe"));
        Assert.IsTrue(document.Descendants("AssemblyName").Any(item => item.Value == "NekoProxyCore"));
        Assert.IsTrue(document.Descendants("RuntimeIdentifier").Any(item => item.Value == "win-x64"));
        Assert.IsFalse(document.Descendants("UseWindowsForms").Any());
    }

    [TestMethod]
    public void ReleaseBuildDisablesPortableSymbolsAtTheSharedBuildBoundary()
    {
        var repositoryRoot = FindRepositoryRoot();
        var projectPath = Path.Combine(repositoryRoot, "Directory.Build.props");
        Assert.IsTrue(File.Exists(projectPath), "Release symbol policy must apply to every project in the graph.");
        var document = XDocument.Load(projectPath);
        var releaseProperties = document
            .Descendants("PropertyGroup")
            .SingleOrDefault(item => string.Equals(
                item.Attribute("Condition")?.Value,
                "'$(Configuration)' == 'Release'",
                StringComparison.Ordinal));

        Assert.IsNotNull(releaseProperties);
        Assert.AreEqual("none", releaseProperties!.Element("DebugType")?.Value);
        Assert.AreEqual("false", releaseProperties.Element("DebugSymbols")?.Value);
    }

    [TestMethod]
    public void ProductionHostPublishStagesRequiredLegacyRuntimeFiles()
    {
        var repositoryRoot = FindRepositoryRoot();
        var projectPath = Path.Combine(repositoryRoot, "NekoProxyCore.Host", "NekoProxyCore.Host.csproj");
        var document = XDocument.Load(projectPath);
        var stagedFiles = document.Descendants("Content")
            .Select(item => item.Attribute("Include")?.Value?.Replace('/', '\\'))
            .Where(item => item != null)
            .ToArray();
        var stageLegacyBin = document
            .Descendants("Target")
            .SingleOrDefault(item => item.Attribute("Name")?.Value == "StageLegacyRuntimeBin");

        Assert.IsTrue(stagedFiles.Any(item => item!.Contains("Storage\\mode\\**\\*", StringComparison.Ordinal)));
        Assert.IsTrue(stagedFiles.Any(item => item!.Contains("Storage\\i18n\\**\\*", StringComparison.Ordinal)));
        Assert.IsFalse(
            stagedFiles.Any(item => item!.Contains("Netch\\bin\\x64\\Release", StringComparison.Ordinal)),
            "Publish must not capture mutable Netch build output during project evaluation.");
        Assert.IsTrue(document.Descendants("Link").Any(item => item.Value.StartsWith("mode\\", StringComparison.Ordinal)));
        Assert.IsTrue(document.Descendants("CopyToPublishDirectory").Any(item => item.Value == "PreserveNewest"));
        Assert.IsNotNull(stageLegacyBin, "Legacy bin assets must be discovered from the completed publish output.");
        Assert.AreEqual("Publish", stageLegacyBin!.Attribute("AfterTargets")?.Value);
        var legacyRuntimeBinIncludes = stageLegacyBin
            .Descendants("LegacyRuntimeBinFile")
            .Select(item => item.Attribute("Include")?.Value)
            .Where(item => item != null)
            .ToArray();
        Assert.IsTrue(legacyRuntimeBinIncludes.Length > 0);
        Assert.IsTrue(
            legacyRuntimeBinIncludes.All(item => item!.StartsWith("$(PublishDir)", StringComparison.Ordinal)),
            "Legacy bin assets must come from the exact completed publish output, not mutable project bin directories.");
        var rootRuntimeBin = stageLegacyBin
            .Descendants("LegacyRuntimeBinFile")
            .Single(item => item.Attribute("Include")?.Value == "$(PublishDir)*.dll");
        var excludedHostAssemblies = rootRuntimeBin.Attribute("Exclude")?.Value ?? string.Empty;
        StringAssert.Contains(excludedHostAssemblies, "$(PublishDir)NekoProxyCore.dll");
        StringAssert.Contains(excludedHostAssemblies, "$(PublishDir)NekoProxyCore.Legacy.dll");
        StringAssert.Contains(excludedHostAssemblies, "$(PublishDir)NekoProxyCore.Windows.dll");
        Assert.IsTrue(
            stageLegacyBin
                .Descendants("DestinationSubDirectory")
                .Any(item => item.Value == "amd64\\"),
            "Native amd64 assets must retain their bin/amd64 runtime layout.");
        Assert.IsTrue(stageLegacyBin.Descendants("Copy").Any());

        var stageNativeRedirector = document
            .Descendants("Target")
            .SingleOrDefault(item => item.Attribute("Name")?.Value == "StageRequiredNativeRuntime");
        Assert.IsNotNull(
            stageNativeRedirector,
            "Production publish must stage freshly built Redirector native files.");
        Assert.AreEqual("Publish", stageNativeRedirector!.Attribute("BeforeTargets")?.Value);
        var nativeInputs = stageNativeRedirector
            .Descendants("RequiredNativeRuntimeFile")
            .Select(item => item.Attribute("Include")?.Value?.Replace('/', '\\'))
            .ToArray();
        CollectionAssert.Contains(nativeInputs, "$(MSBuildThisFileDirectory)..\\Redirector\\bin\\Release\\Redirector.bin");
        CollectionAssert.Contains(nativeInputs, "$(MSBuildThisFileDirectory)..\\Redirector\\bin\\Release\\nfapi.dll");
        Assert.IsTrue(stageNativeRedirector.Descendants("Error").Any());
        Assert.IsTrue(stageNativeRedirector.Descendants("Copy").Any());

        Assert.IsTrue(
            document.Descendants("Link").Any(item => item.Value == "bin\\%(Filename)%(Extension)"),
            "Storage runtime files must retain the bin/ layout required by current PSO2 Redirector runtime.");
    }

    [TestMethod]
    public void ProductionHostUsesOneAlignedHeadlessNetchPublishGraph()
    {
        var repositoryRoot = FindRepositoryRoot();
        var host = XDocument.Load(Path.Combine(
            repositoryRoot,
            "NekoProxyCore.Host",
            "NekoProxyCore.Host.csproj"));
        var legacy = XDocument.Load(Path.Combine(
            repositoryRoot,
            "NekoProxyCore.Legacy",
            "NekoProxyCore.Legacy.csproj"));

        var hostProperties = FindNetchProjectReferenceProperties(host);
        var legacyProperties = FindNetchProjectReferenceProperties(legacy);

        Assert.AreEqual(
            "HeadlessCoreBuild=true;Platform=x64;GenerateDependencyFile=false",
            hostProperties,
            "The direct Host-to-Netch publish graph must disable unused legacy dependency generation.");
        Assert.AreEqual(
            hostProperties,
            legacyProperties,
            "Every path to Netch must use one aligned set of headless publish properties.");
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

    private static string? FindNetchProjectReferenceProperties(XDocument project) =>
        project
            .Descendants("ProjectReference")
            .Single(item => string.Equals(
                Path.GetFileName(item.Attribute("Include")?.Value),
                "Netch.csproj",
                StringComparison.OrdinalIgnoreCase))
            .Attribute("AdditionalProperties")
            ?.Value;

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Netch.sln")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new AssertFailedException("Unable to locate the repository root.");
    }
}
