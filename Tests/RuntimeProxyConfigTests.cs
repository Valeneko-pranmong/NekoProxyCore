using System;
using System.Security.Cryptography;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NekoProxyCore.Core;

namespace Tests;

[TestClass]
public sealed class RuntimeProxyConfigTests
{
    private const string Sentinel = "SENTINEL_PROXY_SECRET_42";
    private const long MaxSafeInteger = 9007199254740991L;

    [TestMethod]
    public void SharedFixtureProducesExactCanonicalBytesAndDigest()
    {
        var config = Valid();
        const string expected =
            "schema_version=1\nconfig_version=18\nendpoint_id=japan-vps-1\nhost=127.0.0.1\n" +
            "port=8389\nprotocol=shadowsocks\ncipher=aes-256-gcm\n" +
            "credential=SENTINEL_PROXY_SECRET_42\nissued_at=1000\nexpires_at=1120\n";

        CollectionAssert.AreEqual(Encoding.ASCII.GetBytes(expected), config.CanonicalBytes());
        Assert.AreEqual(
            "02060535a1e3c4db74edffc8d0b1f5bfd6feee948980669ff06acab9afdecf4d",
            Convert.ToHexString(SHA256.HashData(config.CanonicalBytes())).ToLowerInvariant());
    }

    [TestMethod]
    public void CredentialAndConfigFormattingAreRedacted()
    {
        var config = Valid();
        Assert.AreEqual("[REDACTED]", config.Credential.ToString());
        Assert.IsFalse(config.ToString().Contains(Sentinel, StringComparison.Ordinal));
        Assert.AreEqual(Sentinel, config.Credential.RevealForTransport());
    }

    [DataTestMethod]
    [DataRow(0L)]
    [DataRow(-1L)]
    [DataRow(9007199254740992L)]
    public void InvalidConfigVersionsAreRejected(long value) =>
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => Valid(configVersion: value));

    [TestMethod]
    public void ExactPositiveMaxSafeBoundaryIsAccepted()
    {
        var config = Valid(configVersion: MaxSafeInteger, issuedAt: MaxSafeInteger - 120, expiresAt: MaxSafeInteger);
        Assert.AreEqual(MaxSafeInteger, config.ConfigVersion);
        Assert.AreEqual(MaxSafeInteger, config.ExpiresAt);
    }

    [DataTestMethod]
    [DataRow(-1L, 119L)]
    [DataRow(9007199254740872L, 9007199254740992L)]
    [DataRow(9007199254740992L, 9007199254741112L)]
    public void UnsafeTimestampBoundsAreRejected(long issuedAt, long expiresAt) =>
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => Valid(issuedAt: issuedAt, expiresAt: expiresAt));

    [DataTestMethod]
    [DataRow("", "host", "cipher", "credential")]
    [DataRow("endpoint", "", "cipher", "credential")]
    [DataRow("endpoint", "host", "", "credential")]
    [DataRow("endpoint", "host", "cipher", "")]
    [DataRow("bad\nendpoint", "host", "cipher", "credential")]
    [DataRow("endpoint", "bad\rhost", "cipher", "credential")]
    [DataRow("endpoint", "host", "bad\u0001cipher", "credential")]
    [DataRow("endpoint", "host", "cipher", "bad\ncredential")]
    [DataRow("endpoiné", "host", "cipher", "credential")]
    [DataRow("endpoint", "höst", "cipher", "credential")]
    [DataRow("endpoint", "host", "ciphér", "credential")]
    [DataRow("endpoint", "host", "cipher", "sëcret")]
    public void InvalidBoundedStringsAreRejected(string endpoint, string host, string cipher, string credential) =>
        Assert.ThrowsException<ArgumentException>(() => Valid(endpointId: endpoint, host: host, cipher: cipher, credential: credential));

    [TestMethod]
    public void TooLongBoundedStringsAreRejected()
    {
        Assert.ThrowsException<ArgumentException>(() => Valid(endpointId: new string('e', 65)));
        Assert.ThrowsException<ArgumentException>(() => Valid(host: new string('h', 254)));
        Assert.ThrowsException<ArgumentException>(() => Valid(cipher: new string('c', 65)));
        Assert.ThrowsException<ArgumentException>(() => Valid(credential: new string('s', 257)));
    }

    [DataTestMethod]
    [DataRow(0)]
    [DataRow(65536)]
    public void InvalidPortsAreRejected(int port) =>
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => Valid(port: port));

    [DataTestMethod]
    [DataRow("Shadowsocks")]
    [DataRow("other")]
    public void UnsupportedProtocolsAreRejected(string protocol) =>
        Assert.ThrowsException<ArgumentException>(() => Valid(protocol: protocol));

    [DataTestMethod]
    [DataRow(1000L, 1119L)]
    [DataRow(1000L, 1121L)]
    [DataRow(1000L, 1000L)]
    [DataRow(1000L, 999L)]
    public void NonExactLifetimeIsRejected(long issuedAt, long expiresAt) =>
        Assert.ThrowsException<ArgumentException>(() => Valid(issuedAt: issuedAt, expiresAt: expiresAt));

    private static RuntimeProxyConfig Valid(
        int schemaVersion = 1,
        long configVersion = 18,
        string endpointId = "japan-vps-1",
        string host = "127.0.0.1",
        int port = 8389,
        string protocol = "shadowsocks",
        string cipher = "aes-256-gcm",
        string credential = Sentinel,
        long issuedAt = 1000,
        long expiresAt = 1120) =>
        new(schemaVersion, configVersion, endpointId, host, port, protocol, cipher,
            new SensitiveRuntimeCredential(credential), issuedAt, expiresAt);
}
