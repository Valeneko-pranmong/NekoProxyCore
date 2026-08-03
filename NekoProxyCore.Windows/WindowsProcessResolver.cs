using System.Diagnostics;
using System.ComponentModel;
using System.Management;
using System.Runtime.Versioning;
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
        try
        {
            await WaitForExitWithProcessEventAsync(process, cancellationToken).ConfigureAwait(false);
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == 5)
        {
            // Protected processes can be enumerated but deny the process handle required by
            // HasExited/EnableRaisingEvents. WMI deletion events preserve event-based cleanup
            // without turning this expected access boundary into a runtime start failure.
            if (!OperatingSystem.IsWindows())
                throw;

            await WaitForExitWithManagementEventAsync(process.Id, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task WaitForExitWithProcessEventAsync(Process process, CancellationToken cancellationToken)
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

    [SupportedOSPlatform("windows")]
    private static async Task WaitForExitWithManagementEventAsync(int processId, CancellationToken cancellationToken)
    {
        var query = new WqlEventQuery(
            $"SELECT * FROM __InstanceDeletionEvent WITHIN 1 " +
            $"WHERE TargetInstance ISA 'Win32_Process' AND TargetInstance.ProcessId = {processId}");
        using var watcher = new ManagementEventWatcher(query);
        var exited = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        EventArrivedEventHandler onExited = (_, _) => exited.TrySetResult(null);
        watcher.EventArrived += onExited;
        try
        {
            watcher.Start();
            if (!IsProcessPresent(processId))
                exited.TrySetResult(null);

            await exited.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            watcher.EventArrived -= onExited;
            try
            {
                watcher.Stop();
            }
            catch (ManagementException)
            {
                // The watcher can already be stopped during cancellation or WMI shutdown.
            }
        }
    }

    private static bool IsProcessPresent(int processId)
    {
        var processes = Process.GetProcesses();
        try
        {
            return processes.Any(process => process.Id == processId);
        }
        finally
        {
            DisposeAll(processes);
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
