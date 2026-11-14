namespace Backend.Models.Entities;

/// <summary>
/// An application that registered itself to ask authors for access — an AI assistant's MCP
/// connector, typically. Registration is open (RFC 7591), because every MCP client expects
/// to do it on its own, so a client is worth nothing until an author approves it: holding a
/// client id grants no access to anything.
///
/// Public clients only. There is no client secret: an assistant running on someone's laptop
/// cannot keep one, and PKCE is what binds a code to the client that asked for it.
/// </summary>
public class OAuthClient
{
    public int Id { get; set; }

    /// <summary>The opaque identifier handed back at registration and sent on every request.</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>What the client called itself. Shown on the consent page, so never trusted as proof of anything.</summary>
    public string ClientName { get; set; } = string.Empty;

    /// <summary>Where a code may be sent. Matched exactly, apart from the port of a loopback address.</summary>
    public List<string> RedirectUris { get; set; } = new();

    public DateTimeOffset CreatedAt { get; set; }
}
