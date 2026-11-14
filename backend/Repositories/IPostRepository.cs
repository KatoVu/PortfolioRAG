using Backend.Models.Entities;
using Backend.Models.Requests;
using Microsoft.EntityFrameworkCore.Storage;

namespace Backend.Repositories;

public interface IPostRepository
{
    Task<Post?> GetByIdAsync(int id, bool tracked = false, CancellationToken cancellationToken = default);

    Task<Post?> GetBySlugAsync(string slug, bool tracked = false, CancellationToken cancellationToken = default);
    Task<(IReadOnlyList<Post> Items, int TotalItems)> QueryAsync(
        PostQueryRequest request,
        bool? isDraft,
        CancellationToken cancellationToken = default);

    Task<bool> SlugExistsAsync(string slug, int? excludingPostId = null, CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(int id, CancellationToken cancellationToken = default);

    Task AddAsync(Post post, CancellationToken cancellationToken = default);

    Task RemoveAsync(Post post, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stamps <paramref name="now"/> on the post only if it is still a draft in the database,
    /// and holds its row locked until the surrounding transaction ends. False when it is not.
    /// </summary>
    Task<bool> TryLockDraftAsync(int id, DateTimeOffset now, CancellationToken cancellationToken = default);

    Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default);

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
