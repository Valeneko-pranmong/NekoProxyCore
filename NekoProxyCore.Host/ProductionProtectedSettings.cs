#if WINDOWS
using System.Reflection;
using NekoProxyCore.Legacy;

namespace NekoProxyCore.Host;

internal static class ProductionProtectedSettings
{
    internal const string KeyResourceName = "NekoProxyCore.Host.runtime-settings.key";

    public static byte[] LoadKey()
    {
        using var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream(KeyResourceName) ??
                             throw new ProtectedSettingsException();
        return LoadKey(resource);
    }

    internal static byte[] LoadKey(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var key = new byte[ProtectedSettingsPayload.KeySizeBytes];
        try
        {
            var offset = 0;
            while (offset < key.Length)
            {
                var read = stream.Read(key, offset, key.Length - offset);
                if (read == 0)
                    throw new ProtectedSettingsException();
                offset += read;
            }

            if (stream.ReadByte() != -1)
                throw new ProtectedSettingsException();
            return key;
        }
        catch
        {
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(key);
            throw;
        }
    }
}
#endif