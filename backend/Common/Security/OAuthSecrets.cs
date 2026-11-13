using System.Security.Cryptography;
using Microsoft.AspNetCore.WebUtilities;

namespace Backend.Common.Security;

/// <summary>
/// Mints the opaque secrets of the OAuth flow. Each kind carries its own prefix so one can
/// never be mistaken for another — or for a <c>pfl_</c> API token — and only the hash is
/// stored, as with every other credential here.
/// </summary>
public static class OAuthSecrets
{
    public const string CodePrefix = "pfc_";

    public const string RefreshTokenPrefix = "pfr_";

    public const string UploadTicketPrefix = "pfu_";

    private const int SecretBytes = 32;

    public static string Create(string prefix) =>
        prefix + WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(SecretBytes));

    /// <summary>A client id: unguessable, but not a secret — it is sent in the clear on every request.</summary>
    public static string CreateClientId() =>
        "mcp_" + WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(18));

    public static string Hash(string raw) => ApiTokenGenerator.Hash(raw);

    /// <summary>The S256 PKCE transform: BASE64URL(SHA256(ASCII(verifier))).</summary>
    public static string ChallengeFor(string verifier) =>
        WebEncoders.Base64UrlEncode(SHA256.HashData(System.Text.Encoding.ASCII.GetBytes(verifier)));

    public static bool FixedTimeEquals(string left, string right) =>
        CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(left),
            System.Text.Encoding.UTF8.GetBytes(right));
}
