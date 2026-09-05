using NekoProxyCore.Legacy;

namespace NekoProxyCore.SettingsTool;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (args.Length == 4 && string.Equals(args[0], "verify", StringComparison.Ordinal))
            return await VerifyAsync(args[1], args[2], args[3]).ConfigureAwait(false);

        if (args.Length != 5 || !string.Equals(args[0], "seal", StringComparison.Ordinal))
        {
            await Console.Error.WriteLineAsync(
                "Usage: NekoProxyCore.SettingsTool seal <external-settings> <protected-output> <key-output> <trusted-mode-root>");
            return 64;
        }

        try
        {
            var facts = await ProtectedSettingsProvisioner.ProvisionAsync(args[1], args[2], args[3], args[4])
                .ConfigureAwait(false);
            await Console.Out.WriteLineAsync($"PROFILE_COUNT={facts.ProfileCount}");
            await Console.Out.WriteLineAsync($"SERVER_COUNT={facts.ServerCount}");
            await Console.Out.WriteLineAsync(
                $"PSO2_PROFILE_EXISTS={(facts.Pso2ProfileExists ? "YES" : "NO")}");
            await Console.Out.WriteLineAsync(
                $"PROFILE_SERVER_RELATION_VALID={(facts.ProfileServerRelationshipValid ? "YES" : "NO")}");
            return 0;
        }
        catch
        {
            await Console.Error.WriteLineAsync("Protected settings provisioning failed.");
            return 1;
        }
    }

    private static async Task<int> VerifyAsync(
        string protectedPayloadPath,
        string keyPath,
        string trustedModeRoot)
    {
        try
        {
            await ProtectedSettingsProvisioner.VerifyAsync(
                    protectedPayloadPath,
                    keyPath,
                    trustedModeRoot)
                .ConfigureAwait(false);
            return 0;
        }
        catch
        {
            await Console.Error.WriteLineAsync("Protected settings verification failed.");
            return 1;
        }
    }
}
