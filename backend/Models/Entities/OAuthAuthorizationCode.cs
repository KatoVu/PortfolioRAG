namespace Backend.Models.Entities;

/// <summary>
/// The code an approved consent hands back to the client, exchanged once for tokens. It
/// remembers everything the token request has to repeat — the client, the redirect and the
/// PKCE challenge — so a code lifted from a redirect is useless to anyone else.
/// </summary>
public class OAuthAuthorizationCode
{
    public int Id { get; set; }

    public string CodeHash { get; set; } = string.Empty;

    public int OAuthClientId { get; set; }

    public OAuthClient Client { get; set; } = null!;

    public int AuthorId { get; set; }

    public string RedirectUri { get; set; } = string.Empty;

    /// <summary>BASE64URL(SHA256(code_verifier)). Only S256 is accepted.</summary>
    public string CodeChallenge { get; set; } = string.Empty;

    public string Scope { get; set; } = string.Empty;

    public string? Resource { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset? ConsumedAt { get; set; }

    /// <summary>
    /// The grant this code was exchanged for. Kept so that a second attempt to spend the
    /// code can revoke what the first one produced (RFC 6749 §4.1.2).
    /// </summary>
    public int? OAuthGrantId { get; set; }
}
