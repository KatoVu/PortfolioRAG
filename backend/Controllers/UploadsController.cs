using Backend.Common;
using Backend.Models.DTOs;
using Backend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Controllers;

/// <summary>
/// Where a one-time upload link lands. Anonymous because the link is the credential: it was
/// issued to an authenticated caller for one file on one post, and it works once.
/// </summary>
[ApiController]
[Route("api/uploads")]
[AllowAnonymous]
public class UploadsController : ControllerBase
{
    private readonly IUploadTicketService _tickets;

    public UploadsController(IUploadTicketService tickets)
    {
        _tickets = tickets;
    }

    /// <summary>Uploads the file a link was issued for, as multipart field <c>file</c>.</summary>
    [HttpPost("{ticket}")]
    [Consumes("multipart/form-data")]
    [UploadSizeLimit]
    [ProducesResponseType(typeof(MediaDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(StatusCodes.Status415UnsupportedMediaType)]
    public async Task<ActionResult<MediaDto>> Upload(
        string ticket,
        IFormFile file,
        CancellationToken cancellationToken)
    {
        var media = await _tickets.UploadAsync(ticket, file, cancellationToken);

        return CreatedAtAction(
            nameof(MediaController.GetContent),
            "Media",
            new { id = media.Id },
            media);
    }
}
