using Backend.Models.Entities;

namespace Backend.Repositories;

/// <summary>The clients, codes, grants and refresh tokens behind the OAuth flow.</summary>
public interface IOAuthRepository
{
    Task<OAuthClient?> GetClientAsync(string clientId, CancellationToken cancellationToken = default);

    Task AddClientAsync(OAuthClient client, CancellationToken cancellationToken = default);

    Task AddCodeAsync(OAuthAuthorizationCode code, CancellationToken cancellationToken = default);

    /// <summary>A code by its hash, with its client. Tracked, because spending it writes to it.</summary>
    Task<OAuthAuthorizationCode?> GetCodeByHashAsync(string codeHash, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks a code spent if, and only if, nobody has spent it yet. One conditional UPDATE,
    /// so two requests racing with the same code cannot both win.
    /// </summary>
    Task<bool> TryConsumeCodeAsync(int codeId, DateTimeOffset now, CancellationToken cancellationToken = default);

    Task AddGrantAsync(OAuthGrant grant, CancellationToken cancellationToken = default);

    Task<OAuthGrant?> GetGrantAsync(int id, int authorId, CancellationToken cancellationToken = default);

    /// <summary>An author's grants with their clients, newest first. Revoked ones included.</summary>
    Task<IReadOnlyList<OAuthGrant>> GetGrantsByAuthorAsync(int authorId, CancellationToken cancellationToken = default);

    /// <summary>Revokes a grant and every access token it minted.</summary>
    Task RevokeGrantAsync(int grantId, DateTimeOffset now, CancellationToken cancellationToken = default);

    Task AddRefreshTokenAsync(OAuthRefreshToken token, CancellationToken cancellationToken = default);

    /// <summary>A refresh token by its hash, with its grant and that grant's client.</summary>
    Task<OAuthRefreshToken?> GetRefreshTokenByHashAsync(string tokenHash, CancellationToken cancellationToken = default);

    /// <summary>The same conditional spend as <see cref="TryConsumeCodeAsync"/>, for a refresh token.</summary>
    Task<bool> TryConsumeRefreshTokenAsync(int tokenId, DateTimeOffset now, CancellationToken cancellationToken = default);

    /// <summary>
    /// Drops what a grant no longer needs: access tokens past their expiry and refresh tokens
    /// past theirs. Spent but unexpired refresh tokens stay — they are how replay is caught.
    /// </summary>
    Task PruneGrantAsync(int grantId, DateTimeOffset now, CancellationToken cancellationToken = default);

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
