using Backend.Models.DTOs;
using Backend.Models.DTOs.OAuth;

namespace Backend.Services;

/// <summary>One-time upload links. See <see cref="Backend.Models.Entities.UploadTicket"/>.</summary>
public interface IUploadTicketService
{
    /// <summary>Issues a link that will attach one file to one of the caller's posts.</summary>
    Task<UploadTicketDto> CreateAsync(
        int postId,
        CreateUploadTicketDto dto,
        CancellationToken cancellationToken = default);

    /// <summary>Spends a link on the file it was issued for.</summary>
    Task<MediaDto> UploadAsync(string rawTicket, IFormFile file, CancellationToken cancellationToken = default);
}
