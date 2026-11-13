namespace Backend.Common.Options;

/// <summary>
/// The authorization server that lets an AI assistant act for an author. Optional: with no
/// issuer configured the OAuth endpoints answer 503 and nothing else is affected.
/// </summary>
public class OAuthOptions
{
    public const string SectionName = "OAuth";

    /// <summary>This API's public origin, e.g. https://api.blograg.pbh-dev.tech. Also the base of upload URLs.</summary>
    public string Issuer { get; set; } = string.Empty;

    /// <summary>The studio page that asks the author to approve, e.g. https://blograg.pbh-dev.tech/oauth/consent.</summary>
    public string ConsentUrl { get; set; } = string.Empty;

    /// <summary>
    /// Origins a client may ask for a token for (RFC 8707). The MCP server's, in practice.
    /// A request naming any other resource is refused; one naming none is allowed.
    /// </summary>
    public List<string> AllowedResources { get; set; } = new();

    public int AccessTokenMinutes { get; set; } = 60;

    public int RefreshTokenDays { get; set; } = 30;

    public int AuthorizationCodeSeconds { get; set; } = 300;

    public int UploadTicketMinutes { get; set; } = 15;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Issuer) && !string.IsNullOrWhiteSpace(ConsentUrl);

    public string IssuerBase => Issuer.TrimEnd('/');
}
