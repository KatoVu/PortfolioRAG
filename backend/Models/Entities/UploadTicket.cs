namespace Backend.Models.Entities;

/// <summary>
/// Permission to upload one file to one post, carried in a URL instead of a header.
///
/// An assistant reaches the API through an MCP server that is not on the author's machine,
/// so it cannot hand that server a local file, and pushing a video through the model as
/// base64 is out of the question. Instead it asks for a ticket and sends the bytes itself
/// with a plain HTTP client. The ticket fixes the post, the slot and the caption in advance,
/// and spends itself on first use.
/// </summary>
public class UploadTicket
{
    public int Id { get; set; }

    public string TokenHash { get; set; } = string.Empty;

    public int AuthorId { get; set; }

    public int PostId { get; set; }

    public Post Post { get; set; } = null!;

    public MediaRole Role { get; set; }

    public string? Caption { get; set; }

    /// <summary>
    /// Whether the ticket was issued to an <see cref="ApiTokenScope.Author"/> token, whose
    /// limits — drafts only, never over existing artwork — are checked again when it is spent.
    /// </summary>
    public bool AuthorScoped { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset? ConsumedAt { get; set; }
}
