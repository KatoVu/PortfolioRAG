using Backend.Data;
using Backend.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace Backend.Repositories;

public class OAuthRepository : IOAuthRepository
{
    private readonly BlogDbContext _context;

    public OAuthRepository(BlogDbContext context)
    {
        _context = context;
    }

    public Task<OAuthClient?> GetClientAsync(string clientId, CancellationToken cancellationToken = default) =>
        _context.OAuthClients.FirstOrDefaultAsync(c => c.ClientId == clientId, cancellationToken);

    public async Task AddClientAsync(OAuthClient client, CancellationToken cancellationToken = default) =>
        await _context.OAuthClients.AddAsync(client, cancellationToken);

    public async Task AddCodeAsync(OAuthAuthorizationCode code, CancellationToken cancellationToken = default) =>
        await _context.OAuthAuthorizationCodes.AddAsync(code, cancellationToken);

    public Task<OAuthAuthorizationCode?> GetCodeByHashAsync(
        string codeHash,
        CancellationToken cancellationToken = default) =>
        _context.OAuthAuthorizationCodes
            .Include(c => c.Client)
            .FirstOrDefaultAsync(c => c.CodeHash == codeHash, cancellationToken);

    public async Task<bool> TryConsumeCodeAsync(
        int codeId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        var won = await _context.OAuthAuthorizationCodes
            .Where(c => c.Id == codeId && c.ConsumedAt == null)
            .ExecuteUpdateAsync(set => set.SetProperty(c => c.ConsumedAt, now), cancellationToken) == 1;

        if (won)
            Sync(_context.OAuthAuthorizationCodes.Local.FirstOrDefault(c => c.Id == codeId), nameof(OAuthAuthorizationCode.ConsumedAt), now);

        return won;
    }

    public async Task AddGrantAsync(OAuthGrant grant, CancellationToken cancellationToken = default) =>
        await _context.OAuthGrants.AddAsync(grant, cancellationToken);

    public Task<OAuthGrant?> GetGrantAsync(int id, int authorId, CancellationToken cancellationToken = default) =>
        _context.OAuthGrants
            .Include(g => g.Client)
            .FirstOrDefaultAsync(g => g.Id == id && g.AuthorId == authorId, cancellationToken);

    public async Task<IReadOnlyList<OAuthGrant>> GetGrantsByAuthorAsync(
        int authorId,
        CancellationToken cancellationToken = default) =>
        await _context.OAuthGrants
            .AsNoTracking()
            .Include(g => g.Client)
            .Where(g => g.AuthorId == authorId)
            .OrderByDescending(g => g.CreatedAt)
            .ThenByDescending(g => g.Id)
            .ToListAsync(cancellationToken);

    public async Task RevokeGrantAsync(int grantId, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        await _context.OAuthGrants
            .Where(g => g.Id == grantId && g.RevokedAt == null)
            .ExecuteUpdateAsync(set => set.SetProperty(g => g.RevokedAt, now), cancellationToken);

        await _context.ApiTokens
            .Where(t => t.OAuthGrantId == grantId && t.RevokedAt == null)
            .ExecuteUpdateAsync(set => set.SetProperty(t => t.RevokedAt, now), cancellationToken);

        Sync(_context.OAuthGrants.Local.FirstOrDefault(g => g.Id == grantId && g.RevokedAt == null), nameof(OAuthGrant.RevokedAt), now);

        foreach (var token in _context.ApiTokens.Local.Where(t => t.OAuthGrantId == grantId && t.RevokedAt == null).ToList())
            Sync(token, nameof(ApiToken.RevokedAt), now);
    }

    public async Task AddRefreshTokenAsync(OAuthRefreshToken token, CancellationToken cancellationToken = default) =>
        await _context.OAuthRefreshTokens.AddAsync(token, cancellationToken);

    public Task<OAuthRefreshToken?> GetRefreshTokenByHashAsync(
        string tokenHash,
        CancellationToken cancellationToken = default) =>
        _context.OAuthRefreshTokens
            .Include(t => t.Grant)
            .ThenInclude(g => g.Client)
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash, cancellationToken);

    public async Task<bool> TryConsumeRefreshTokenAsync(
        int tokenId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        var won = await _context.OAuthRefreshTokens
            .Where(t => t.Id == tokenId && t.ConsumedAt == null)
            .ExecuteUpdateAsync(set => set.SetProperty(t => t.ConsumedAt, now), cancellationToken) == 1;

        if (won)
            Sync(_context.OAuthRefreshTokens.Local.FirstOrDefault(t => t.Id == tokenId), nameof(OAuthRefreshToken.ConsumedAt), now);

        return won;
    }

    public async Task PruneGrantAsync(int grantId, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        await _context.ApiTokens
            .Where(t => t.OAuthGrantId == grantId && t.ExpiresAt < now)
            .ExecuteDeleteAsync(cancellationToken);

        await _context.OAuthRefreshTokens
            .Where(t => t.OAuthGrantId == grantId && t.ExpiresAt < now)
            .ExecuteDeleteAsync(cancellationToken);
    }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        _context.SaveChangesAsync(cancellationToken);

    /// <summary>
    /// Mirrors a bulk update onto an entity this context already tracks. ExecuteUpdate goes
    /// straight to the database, and without this a later read in the same request would be
    /// served the stale copy — a revoked grant that still looks live.
    /// </summary>
    private void Sync<T>(T? entity, string property, object value) where T : class
    {
        if (entity is null)
            return;

        // NOTE: original and current both, so the tracker sees no change to write back. Only
        // this one property: anything else pending on the entity is still the caller's to save.
        var entry = _context.Entry(entity).Property(property);
        entry.CurrentValue = value;
        entry.OriginalValue = value;
        entry.IsModified = false;
    }
}
