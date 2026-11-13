namespace Backend.Models.Entities;

/// <summary>
/// One author's standing approval of one client: what the studio lists as a connected app.
/// The refresh tokens and the short-lived access tokens hang off it, so revoking the grant
/// cuts the client off at once rather than whenever its last token happens to expire.
/// </summary>
public class OAuthGrant
{
    public int Id { get; set; }

    public int AuthorId { get; set; }

    public Author Author { get; set; } = null!;

    public int OAuthClientId { get; set; }

    public OAuthClient Client { get; set; } = null!;

    /// <summary>The scope string approved, as the client asked for it.</summary>
    public string Scope { get; set; } = string.Empty;

    /// <summary>The resource (RFC 8707) the tokens were issued for, if the client named one.</summary>
    public string? Resource { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>When the client last refreshed. Enough to tell a dead connection from a live one.</summary>
    public DateTimeOffset? LastUsedAt { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }

    public List<OAuthRefreshToken> RefreshTokens { get; set; } = new();

    public List<ApiToken> AccessTokens { get; set; } = new();
}
