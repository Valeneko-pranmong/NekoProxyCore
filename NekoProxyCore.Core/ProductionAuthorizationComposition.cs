namespace NekoProxyCore.Core;

/// <summary>
/// Pins the accepted launch-authorization contract at the production composition boundary.
/// The current release intentionally has no approved public-key material and therefore remains fail closed.
/// </summary>
public static class ProductionAuthorizationComposition
{
    public const string ContractId = "NEKO-AUTH-S0";
    public const string ContractRevision = "s0-rc1";
    public const string ContractPackageSha256 =
        "6697351b6b280afc566fedaaa1a6cfe207b1ea1d803c2eb613b4c1a891e192df";

    public static IProxyStartAuthorizer CreateStartAuthorizer() =>
        new AuthorizationRequiredStartAuthorizer();
}
