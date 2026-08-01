using System.Diagnostics;
using System.ComponentModel;
using NekoProxyCore.Core;

namespace NekoProxyCore.Windows;

/// <summary>
/// Resolves a local Windows process and waits for its exit without a permanent polling loop.
/// The process name is an executable name only; paths and command-line material are rejected.
/// </summary>
public sealed class WindowsProcessResolver : IProcessResolver
{
    public Task<bool> IsRunningAsync(string processName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var normalizedName = NormalizeProcessName(processName);
        var processes = GetProcesses(normalizedName);
        try
        {
            foreach (var process in processes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!process.HasExited)
                    return Task.FromResult(true);
            }

            return Task.FromResult(false);
        }
        finally
        {
            DisposeAll(processes);
        }
    }

    public async Task WaitForExitAsync(string processName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var normalizedName = NormalizeProcessName(processName);
        var processes = GetProcesses(normalizedName);
        try
        {
            if (processes.Length == 0)
                return;

            // Snapshot the matching processes once. Each Process object owns an OS handle and
            // signals Exited; this avoids a background polling watcher and is deterministic for
            // the process set that was observed by the caller.
            await Task.WhenAll(processes.Select(process => WaitForExitAsync(process, cancellationToken)))
                .ConfigureAwait(false);
        }
        finally
        {
            DisposeAll(processes);
        }
    }

    private static async Task WaitForExitAsync(Process process, CancellationToken cancellationToken)
    {
        if (process.HasExited)
            return;

        var exited = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler onExited = (_, _) => exited.TrySetResult(null);
        process.EnableRaisingEvents = true;
        process.Exited += onExited;
        try
        {
            // The process can exit between the first HasExited check and event subscription.
            // Re-checking after subscription closes that race without polling.
            if (process.HasExited)
                exited.TrySetResult(null);

            await exited.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            process.Exited -= onExited;
        }
    }

    private static Process[] GetProcesses(string normalizedName)
    {
        try
        {
            return Process.GetProcessesByName(normalizedName);
        }
        catch (Win32Exception)
        {
            throw new ProxyRuntimeException(ProxyErrorCode.StartFailed, "Unable to inspect the target process.");
        }
        catch (InvalidOperationException)
        {
            throw new ProxyRuntimeException(ProxyErrorCode.StartFailed, "Unable to inspect the target process.");
        }
    }

    private static string NormalizeProcessName(string processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
            throw new ArgumentException("A process executable name is required.", nameof(processName));

        var trimmed = processName.Trim();
        if (trimmed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            trimmed = trimmed[..^4];

        if (trimmed.Length == 0 || trimmed.Length > 260 || trimmed.IndexOfAny(new[] { '\\', '/', ':', '*', '?', '"', '<', '>', '|' }) >= 0)
            throw new ArgumentException("A process executable name is required.", nameof(processName));

        return trimmed;
    }

    private static void DisposeAll(IEnumerable<Process> processes)
    {
        foreach (var process in processes)
            process.Dispose();
    }
}
