using System.IO.Pipes;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace NekoProxyCore.Host;

internal interface IControlPipeClientProcessIdProvider
{
    bool TryGetClientProcessId(NamedPipeServerStream pipe, out uint processId);
}

internal sealed class WindowsControlPipeClientProcessIdProvider : IControlPipeClientProcessIdProvider
{
    public static readonly WindowsControlPipeClientProcessIdProvider Instance = new();

    private WindowsControlPipeClientProcessIdProvider()
    {
    }

    public bool TryGetClientProcessId(NamedPipeServerStream pipe, out uint processId)
    {
        ArgumentNullException.ThrowIfNull(pipe);
        return GetNamedPipeClientProcessId(pipe.SafePipeHandle, out processId);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeClientProcessId(
        SafePipeHandle pipe,
        out uint clientProcessId);
}
