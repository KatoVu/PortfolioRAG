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

    /// <summary>Drafts only: read, write drafts, publish. What every grant has.</summary>
    public const string PostsScope = "posts";

    /// <summary>Full editing, on top of <see cref="PostsScope"/>. The author's choice, and theirs to take back.</summary>
    public const string EditScope = "posts:edit";

    /// <summary>
    /// The scope approved: <c>posts</c>, or <c>posts posts:edit</c> for full editing. The
    /// author can switch it from the studio, and it applies to the grant's tokens at once.
    /// </summary>
    public string Scope { get; set; } = string.Empty;

    public bool FullEditing => Scope.Split(' ').Contains(EditScope);

    /// <summary>
    /// What this grant's tokens may do. Read from the grant on every request rather than off
    /// the token, so switching the mode needs no new token and has no window where an old one
    /// still carries the old mode.
    /// </summary>
    public ApiTokenScope TokenScope => FullEditing ? ApiTokenScope.Editor : ApiTokenScope.Author;

    public static string ScopeFor(bool fullEditing) =>
        fullEditing ? $"{PostsScope} {EditScope}" : PostsScope;

    /// <summary>The resource (RFC 8707) the tokens were issued for, if the client named one.</summary>
    public string? Resource { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>When the client last refreshed. Enough to tell a dead connection from a live one.</summary>
    public DateTimeOffset? LastUsedAt { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }

    public List<OAuthRefreshToken> RefreshTokens { get; set; } = new();

    public List<ApiToken> AccessTokens { get; set; } = new();
}
