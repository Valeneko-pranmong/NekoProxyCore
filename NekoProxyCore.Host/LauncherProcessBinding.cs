using System.Globalization;

namespace NekoProxyCore.Host;

internal static class LauncherProcessBinding
{
    private const string LauncherPidOption = "--launcher-pid";
    private const string MutableRootOption = "--mutable-root";

    public static bool TryParseArguments(string[] arguments, out uint launcherProcessId)
    {
        return TryParseArguments(arguments, out launcherProcessId, out _);
    }

    public static bool TryParseArguments(string[] arguments, out uint launcherProcessId, out string? mutableRoot)
    {
        launcherProcessId = 0;
        mutableRoot = null;

        if (arguments.Length != 2 && arguments.Length != 4)
            return false;

        if (!string.Equals(arguments[0], LauncherPidOption, StringComparison.Ordinal))
            return false;

        if (!uint.TryParse(arguments[1], NumberStyles.None, CultureInfo.InvariantCulture, out launcherProcessId) || launcherProcessId == 0)
            return false;

        if (arguments.Length == 4)
        {
            if (!string.Equals(arguments[2], MutableRootOption, StringComparison.Ordinal))
                return false;

            if (string.IsNullOrWhiteSpace(arguments[3]))
                return false;

            mutableRoot = arguments[3];
        }

        return true;
    }
}
