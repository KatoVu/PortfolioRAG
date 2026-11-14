using Backend.Models.Entities;

namespace Backend.Repositories;

public interface IUploadTicketRepository
{
    Task AddAsync(UploadTicket ticket, CancellationToken cancellationToken = default);

    Task<UploadTicket?> GetByHashAsync(string tokenHash, CancellationToken cancellationToken = default);

    /// <summary>Spends a ticket if nobody has yet. One conditional UPDATE, so a race has one winner.</summary>
    Task<bool> TryConsumeAsync(int ticketId, DateTimeOffset now, CancellationToken cancellationToken = default);

    /// <summary>Drops an author's tickets that expired or were spent, so the table does not only grow.</summary>
    Task PruneAsync(int authorId, DateTimeOffset now, CancellationToken cancellationToken = default);

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
