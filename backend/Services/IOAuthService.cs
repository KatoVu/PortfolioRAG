using Backend.Common.Exceptions;
using Backend.Models.DTOs.OAuth;

namespace Backend.Services;

/// <summary>The authorization server behind the MCP connector. See <see cref="OAuthService"/>.</summary>
public interface IOAuthService
{
    /// <summary>RFC 8414 authorization server metadata.</summary>
    IReadOnlyDictionary<string, object> GetMetadata();

    /// <summary>RFC 7591 dynamic registration. Public clients only.</summary>
    Task<ClientRegistrationResponseDto> RegisterClientAsync(
        ClientRegistrationRequestDto request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates an authorization request and returns the studio consent page to send the
    /// browser to. Throws <see cref="OAuthException"/>; see its <c>Redirectable</c>.
    /// </summary>
    Task<string> BeginAuthorizationAsync(AuthorizeRequestDto request, CancellationToken cancellationToken = default);

    /// <summary>The error redirect for a request whose client and redirect were valid.</summary>
    string BuildErrorRedirect(AuthorizeRequestDto request, OAuthException exception);

    /// <summary>What the consent page shows. Signed-in author only.</summary>
    Task<ConsentDescriptionDto> DescribeConsentAsync(
        AuthorizeRequestDto request,
        CancellationToken cancellationToken = default);

    /// <summary>Records the author's decision and returns where to send the browser.</summary>
    Task<ConsentResultDto> DecideConsentAsync(ConsentDecisionDto decision, CancellationToken cancellationToken = default);

    /// <summary>The token endpoint: authorization_code and refresh_token grants.</summary>
    Task<TokenResponseDto> ExchangeAsync(TokenRequestDto request, CancellationToken cancellationToken = default);

    /// <summary>RFC 7009 revocation. Never fails on an unknown token.</summary>
    Task RevokeTokenAsync(string? token, string? clientId, CancellationToken cancellationToken = default);

    /// <summary>The signed-in author's connected apps.</summary>
    Task<IReadOnlyList<OAuthGrantDto>> GetGrantsAsync(CancellationToken cancellationToken = default);

    /// <summary>Disconnects an app: its grant and every token it holds stop working at once.</summary>
    Task<OAuthGrantDto> RevokeGrantAsync(int id, CancellationToken cancellationToken = default);
}
