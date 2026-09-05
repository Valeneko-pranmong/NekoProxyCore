using System.Globalization;

namespace NekoProxyCore.Host;

internal static class LauncherProcessBinding
{
    private const string LauncherPidOption = "--launcher-pid";

    public static bool TryParseArguments(string[] arguments, out uint launcherProcessId)
    {
        launcherProcessId = 0;
        return arguments.Length == 2 &&
               string.Equals(arguments[0], LauncherPidOption, StringComparison.Ordinal) &&
               uint.TryParse(arguments[1], NumberStyles.None, CultureInfo.InvariantCulture, out launcherProcessId) &&
               launcherProcessId != 0;
    }
}
