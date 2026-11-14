namespace Backend.Models.Entities;

/// <summary>
/// A single-use refresh token for an <see cref="OAuthGrant"/>. Spending one issues the next,
/// and presenting one that was already spent is read as theft: the whole grant is revoked,
/// as OAuth 2.1 asks of a public client's refresh tokens.
/// </summary>
public class OAuthRefreshToken
{
    public int Id { get; set; }

    public int OAuthGrantId { get; set; }

    public OAuthGrant Grant { get; set; } = null!;

    public string TokenHash { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset? ConsumedAt { get; set; }
}
