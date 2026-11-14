using Backend.Common.Exceptions;
using Backend.Common.Options;
using Backend.Common.Security;
using Backend.Models.DTOs;
using Backend.Models.DTOs.OAuth;
using Backend.Models.Entities;
using Backend.Repositories;
using Microsoft.Extensions.Options;

namespace Backend.Services;

public class UploadTicketService : IUploadTicketService
{
    private readonly IUploadTicketRepository _tickets;
    private readonly IPostRepository _posts;
    private readonly IMediaService _media;
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _timeProvider;
    private readonly OAuthOptions _oauthOptions;
    private readonly MediaOptions _mediaOptions;

    public UploadTicketService(
        IUploadTicketRepository tickets,
        IPostRepository posts,
        IMediaService media,
        ICurrentUser currentUser,
        TimeProvider timeProvider,
        IOptions<OAuthOptions> oauthOptions,
        IOptions<MediaOptions> mediaOptions)
    {
        _tickets = tickets;
        _posts = posts;
        _media = media;
        _currentUser = currentUser;
        _timeProvider = timeProvider;
        _oauthOptions = oauthOptions.Value;
        _mediaOptions = mediaOptions.Value;
    }

    public async Task<UploadTicketDto> CreateAsync(
        int postId,
        CreateUploadTicketDto dto,
        CancellationToken cancellationToken = default)
    {
        // NOTE: the link is built on the public origin, which is the OAuth issuer. Without one
        // there is no address to hand out that is known to reach this API.
        if (string.IsNullOrWhiteSpace(_oauthOptions.Issuer))
        {
            throw new NotConfiguredException(
                $"Upload links need '{OAuthOptions.SectionName}:Issuer' set to this API's public origin.");
        }

        var authorId = _currentUser.RequireAuthorId();
        var authorScoped = _currentUser.ApiTokenScope == ApiTokenScope.Author;

        var post = await _posts.GetByIdAsync(postId, tracked: false, cancellationToken)
            ?? throw NotFoundException.For("Post", postId);

        if (post.AuthorId != authorId)
            throw ForbiddenException.For("post", postId);

        // The full rule is checked again when the file arrives; this only saves a wasted
        // upload when the answer is already known.
        if (authorScoped && !post.IsDraft)
            throw new ForbiddenException("This token can only add media to a draft, and this post is published.");

        var now = _timeProvider.GetUtcNow();
        var raw = OAuthSecrets.Create(OAuthSecrets.UploadTicketPrefix);
        var expiresAt = now.AddMinutes(_oauthOptions.UploadTicketMinutes);

        await _tickets.PruneAsync(authorId, now, cancellationToken);

        await _tickets.AddAsync(new UploadTicket
        {
            TokenHash = OAuthSecrets.Hash(raw),
            AuthorId = authorId,
            PostId = postId,
            Role = dto.Role,
            Caption = string.IsNullOrWhiteSpace(dto.Caption) ? null : dto.Caption.Trim(),
            AuthorScoped = authorScoped,
            CreatedAt = now,
            ExpiresAt = expiresAt
        }, cancellationToken);

        await _tickets.SaveChangesAsync(cancellationToken);

        return new UploadTicketDto
        {
            UploadUrl = $"{_oauthOptions.IssuerBase}/api/uploads/{raw}",
            ExpiresAt = expiresAt,
            MaxImageBytes = _mediaOptions.MaxImageBytes,
            MaxVideoBytes = _mediaOptions.MaxVideoBytes
        };
    }

    public async Task<MediaDto> UploadAsync(string rawTicket, IFormFile file, CancellationToken cancellationToken = default)
    {
        var unknown = new NotFoundException("This upload link is unknown, already used, or expired. Ask for a new one.");

        if (!rawTicket.StartsWith(OAuthSecrets.UploadTicketPrefix, StringComparison.Ordinal) || rawTicket.Length > 128)
            throw unknown;

        var ticket = await _tickets.GetByHashAsync(OAuthSecrets.Hash(rawTicket), cancellationToken);
        var now = _timeProvider.GetUtcNow();

        if (ticket is null || ticket.ConsumedAt is not null || ticket.ExpiresAt <= now)
            throw unknown;

        // NOTE: spent before the upload, not after. A failed upload costs the caller a new
        // link; the other order would let two requests racing on one link both get through.
        if (!await _tickets.TryConsumeAsync(ticket.Id, now, cancellationToken))
            throw unknown;

        return await _media.AttachAsync(
            ticket.AuthorId,
            ticket.PostId,
            file,
            ticket.Role,
            ticket.Caption,
            ticket.AuthorScoped,
            cancellationToken);
    }
}
