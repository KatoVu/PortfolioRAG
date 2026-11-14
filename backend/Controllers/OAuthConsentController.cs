using Backend.Common.Security;
using Backend.Models.DTOs.OAuth;
using Backend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Controllers;

/// <summary>
/// The studio's side of OAuth: the consent page, and the list of connected apps. Session
/// only — an app may not approve itself, nor list or revoke the others.
/// </summary>
[ApiController]
[Route("api/oauth")]
[Authorize]
[SessionOnly]
public class OAuthConsentController : ControllerBase
{
    private readonly IOAuthService _oauth;

    public OAuthConsentController(IOAuthService oauth)
    {
        _oauth = oauth;
    }

    /// <summary>
    /// What an authorization request is asking for, for the consent page to show. POST, so
    /// the request parameters travel in a body the generated client can type.
    /// </summary>
    [HttpPost("consent/describe")]
    [ProducesResponseType(typeof(ConsentDescriptionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<ConsentDescriptionDto>> Describe(
        [FromBody] AuthorizeRequestDto request,
        CancellationToken cancellationToken) =>
        Ok(await _oauth.DescribeConsentAsync(request, cancellationToken));

    /// <summary>
    /// Approves or declines. Either way the answer is a URL back to the app — with a code, or
    /// with access_denied — for the page to send the browser to.
    /// </summary>
    [HttpPost("consent")]
    [ProducesResponseType(typeof(ConsentResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<ConsentResultDto>> Decide(
        [FromBody] ConsentDecisionDto decision,
        CancellationToken cancellationToken) =>
        Ok(await _oauth.DecideConsentAsync(decision, cancellationToken));

    /// <summary>Apps connected to your account, including disconnected ones.</summary>
    [HttpGet("grants")]
    [ProducesResponseType(typeof(IReadOnlyList<OAuthGrantDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IReadOnlyList<OAuthGrantDto>>> ListGrants(CancellationToken cancellationToken) =>
        Ok(await _oauth.GetGrantsAsync(cancellationToken));

    /// <summary>Disconnects an app. Idempotent; its tokens stop working immediately.</summary>
    [HttpPost("grants/{id:int}/revoke")]
    [ProducesResponseType(typeof(OAuthGrantDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<OAuthGrantDto>> RevokeGrant(int id, CancellationToken cancellationToken) =>
        Ok(await _oauth.RevokeGrantAsync(id, cancellationToken));

    /// <summary>
    /// Switches an app between drafts only and full editing. Applies to its very next
    /// request, with no reconnecting. Session only, like everything here: an app may never
    /// widen its own access.
    /// </summary>
    [HttpPut("grants/{id:int}/mode")]
    [ProducesResponseType(typeof(OAuthGrantDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<OAuthGrantDto>> SetGrantMode(
        int id,
        [FromBody] GrantModeDto mode,
        CancellationToken cancellationToken) =>
        Ok(await _oauth.SetGrantModeAsync(id, mode, cancellationToken));
}
