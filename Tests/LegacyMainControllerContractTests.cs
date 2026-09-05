using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tests;

[TestClass]
public sealed class LegacyMainControllerContractTests
{
    [TestMethod]
    public void HttpProbeAwaitsInsideTrySoAsynchronousFailuresAreClassifiedAsProbeFailures()
    {
        var source = ReadMainControllerSource();
        var method = ExtractMethod(
            source,
            "HttpConnectAsync(CancellationToken ctx = default)",
            "private static void PublishStatus");

        Assert.IsTrue(
            method.Contains("return await Socks5ServerTestUtils.HttpConnectAsync", StringComparison.Ordinal),
            "HttpConnectAsync must await the probe inside its try block.");
        Assert.IsTrue(
            method.Contains("catch (OperationCanceledException)", StringComparison.Ordinal),
            "HttpConnectAsync must classify asynchronous cancellation as a failed probe.");
        Assert.IsTrue(
            method.Contains("return null;", StringComparison.Ordinal),
            "HttpConnectAsync must return null for a failed probe.");
    }

    [TestMethod]
    public void StopFailurePropagatesBeforeControllerReferencesAreCleared()
    {
        var source = ReadMainControllerSource();
        var method = ExtractMethod(
            source,
            "public static async Task StopAsync()",
            "public static void PortCheck");
        var catchIndex = method.IndexOf("catch (Exception e)", StringComparison.Ordinal);
        var throwIndex = method.IndexOf("throw;", catchIndex, StringComparison.Ordinal);
        var clearIndex = method.IndexOf("ServerController = null;", StringComparison.Ordinal);

        Assert.IsTrue(catchIndex >= 0, "StopAsync must retain explicit stop-error logging.");
        Assert.IsTrue(throwIndex > catchIndex, "StopAsync must propagate controller stop failures.");
        Assert.IsTrue(clearIndex > throwIndex, "Controller references must only be cleared after successful stop completion.");
    }

    private static string ReadMainControllerSource()
    {
        var repositoryRoot = FindRepositoryRoot();
        var path = Path.Combine(repositoryRoot, "Netch", "Controllers", "MainController.cs");
        Assert.IsTrue(File.Exists(path), "MainController source is missing.");
        return File.ReadAllText(path);
    }

    private static string ExtractMethod(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        var end = source.IndexOf(endMarker, start, StringComparison.Ordinal);
        Assert.IsTrue(start >= 0, $"Method marker is missing: {startMarker}");
        Assert.IsTrue(end > start, $"Method end marker is missing: {endMarker}");
        return source.Substring(start, end - start);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Netch.sln")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new AssertFailedException("Unable to locate the repository root.");
    }
}
