using Backend.Data;
using Backend.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace Backend.Repositories;

public class UploadTicketRepository : IUploadTicketRepository
{
    private readonly BlogDbContext _context;

    public UploadTicketRepository(BlogDbContext context)
    {
        _context = context;
    }

    public async Task AddAsync(UploadTicket ticket, CancellationToken cancellationToken = default) =>
        await _context.UploadTickets.AddAsync(ticket, cancellationToken);

    public Task<UploadTicket?> GetByHashAsync(string tokenHash, CancellationToken cancellationToken = default) =>
        _context.UploadTickets
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash, cancellationToken);

    public async Task<bool> TryConsumeAsync(
        int ticketId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default) =>
        await _context.UploadTickets
            .Where(t => t.Id == ticketId && t.ConsumedAt == null)
            .ExecuteUpdateAsync(set => set.SetProperty(t => t.ConsumedAt, now), cancellationToken) == 1;

    public Task PruneAsync(int authorId, DateTimeOffset now, CancellationToken cancellationToken = default) =>
        _context.UploadTickets
            .Where(t => t.AuthorId == authorId && (t.ConsumedAt != null || t.ExpiresAt < now))
            .ExecuteDeleteAsync(cancellationToken);

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        _context.SaveChangesAsync(cancellationToken);
}
