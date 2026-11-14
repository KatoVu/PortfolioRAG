using Backend.Common.Exceptions;
using Backend.Models.DTOs.OAuth;
using Backend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Backend.Controllers;

/// <summary>
/// The OAuth protocol endpoints an MCP client talks to on its own. Kept out of the OpenAPI
/// document: their callers are OAuth libraries following RFCs, not the generated client, and
/// they speak form posts and RFC 6749 errors rather than JSON and problem+json.
/// </summary>
[ApiController]
[ApiExplorerSettings(IgnoreApi = true)]
[AllowAnonymous]
[OAuthErrors]
public class OAuthController : ControllerBase
{
    private readonly IOAuthService _oauth;

    public OAuthController(IOAuthService oauth)
    {
        _oauth = oauth;
    }

    /// <summary>RFC 8414 metadata — where an MCP client learns everything below.</summary>
    [HttpGet("/.well-known/oauth-authorization-server")]
    public IActionResult Metadata()
    {
        Response.Headers.CacheControl = "public, max-age=3600";
        return Ok(_oauth.GetMetadata());
    }

    [HttpPost("/oauth/register")]
    public async Task<IActionResult> Register(
        [FromBody] ClientRegistrationRequestDto request,
        CancellationToken cancellationToken)
    {
        var client = await _oauth.RegisterClientAsync(request, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, client);
    }

    /// <summary>
    /// Checks the request and hands the browser to the studio's consent page, which holds the
    /// author's session. Errors about the client or its redirect are shown here instead of
    /// redirected, so this endpoint can never bounce a browser to an unregistered address.
    /// </summary>
    [HttpGet("/oauth/authorize")]
    public async Task<IActionResult> Authorize(CancellationToken cancellationToken)
    {
        var request = new AuthorizeRequestDto
        {
            ClientId = Query("client_id"),
            RedirectUri = Query("redirect_uri"),
            ResponseType = Query("response_type"),
            CodeChallenge = Query("code_challenge"),
            CodeChallengeMethod = Query("code_challenge_method"),
            State = Query("state"),
            Scope = Query("scope"),
            Resource = Query("resource")
        };

        try
        {
            return Redirect(await _oauth.BeginAuthorizationAsync(request, cancellationToken));
        }
        catch (OAuthException exception) when (exception.Redirectable)
        {
            return Redirect(_oauth.BuildErrorRedirect(request, exception));
        }
        catch (OAuthException exception)
        {
            return new ContentResult
            {
                StatusCode = StatusCodes.Status400BadRequest,
                ContentType = "text/plain; charset=utf-8",
                Content = $"This sign-in link is not valid: {exception.Message}\n\n" +
                          "Go back to the app you were connecting and try again."
            };
        }
    }

    [HttpPost("/oauth/token")]
    [Consumes("application/x-www-form-urlencoded")]
    public async Task<IActionResult> Token(CancellationToken cancellationToken)
    {
        var form = await Request.ReadFormAsync(cancellationToken);

        var tokens = await _oauth.ExchangeAsync(new TokenRequestDto
        {
            GrantType = form["grant_type"].FirstOrDefault(),
            Code = form["code"].FirstOrDefault(),
            RedirectUri = form["redirect_uri"].FirstOrDefault(),
            ClientId = form["client_id"].FirstOrDefault(),
            CodeVerifier = form["code_verifier"].FirstOrDefault(),
            RefreshToken = form["refresh_token"].FirstOrDefault(),
            Resource = form["resource"].FirstOrDefault()
        }, cancellationToken);

        NoStore();
        return Ok(tokens);
    }

    [HttpPost("/oauth/revoke")]
    [Consumes("application/x-www-form-urlencoded")]
    public async Task<IActionResult> Revoke(CancellationToken cancellationToken)
    {
        var form = await Request.ReadFormAsync(cancellationToken);
        await _oauth.RevokeTokenAsync(
            form["token"].FirstOrDefault(),
            form["client_id"].FirstOrDefault(),
            cancellationToken);

        return Ok();
    }

    private string? Query(string name) => Request.Query[name].FirstOrDefault();

    private void NoStore()
    {
        Response.Headers.CacheControl = "no-store";
        Response.Headers.Pragma = "no-cache";
    }
}

/// <summary>Writes an <see cref="OAuthException"/> in RFC 6749 §5.2 shape instead of problem+json.</summary>
public sealed class OAuthErrorsAttribute : ExceptionFilterAttribute
{
    public override void OnException(ExceptionContext context)
    {
        if (context.Exception is not OAuthException exception)
            return;

        context.HttpContext.Response.Headers.CacheControl = "no-store";

        context.Result = new ObjectResult(new Dictionary<string, string>
        {
            ["error"] = exception.Error,
            ["error_description"] = exception.Message
        })
        {
            StatusCode = exception.StatusCode
        };

        context.ExceptionHandled = true;
    }
}
