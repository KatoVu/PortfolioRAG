using System.Net;
using System.Text.RegularExpressions;
using Backend.Common.Exceptions;
using Backend.Common.Options;
using Backend.Common.Security;
using Backend.Models.DTOs.OAuth;
using Backend.Models.Entities;
using Backend.Repositories;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace Backend.Services;

/// <summary>
/// A small OAuth 2.1 authorization server for one purpose: letting an AI assistant's MCP
/// connector write drafts for an author.
///
/// <para>
/// Authorization code with PKCE (S256 only), refresh tokens that rotate, open client
/// registration (RFC 7591), resource indicators (RFC 8707) and revocation (RFC 7009). Public
/// clients only; nothing here ever holds a client secret.
/// </para>
/// <para>
/// The access token it issues is an ordinary <c>pfl_</c> API token with the
/// <see cref="ApiTokenScope.Author"/> scope and an hour to live, so every rule the API
/// already enforces on tokens applies to it unchanged, and revoking it is the same write.
/// </para>
/// <para>
/// The author approves on a page in the studio rather than one served here: the studio holds
/// the session, and this API has no cookie of its own to recognise a browser by.
/// </para>
/// </summary>
public partial class OAuthService : IOAuthService
{
    /// <summary>The one scope there is. Anything else a client asks for is dropped, not refused.</summary>
    public const string PostsScope = "posts";

    private const int MaxRedirectUris = 5;

    private const int MaxRedirectUriLength = 500;

    private const int MaxClientNameLength = 100;

    private const int MaxStateLength = 500;

    private const int MaxTokenNameLength = 60;

    // NOTE: schemes a browser would run or read locally instead of handing to an app. An
    // app's private scheme (cursor://, vscode://) is fine; these never are.
    private static readonly string[] ForbiddenSchemes =
        ["javascript", "data", "file", "vbscript", "about", "blob", "ftp", "ws", "wss"];

    private readonly IOAuthRepository _oauth;
    private readonly IApiTokenRepository _apiTokens;
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _timeProvider;
    private readonly OAuthOptions _options;
    private readonly ILogger<OAuthService> _logger;

    public OAuthService(
        IOAuthRepository oauth,
        IApiTokenRepository apiTokens,
        ICurrentUser currentUser,
        TimeProvider timeProvider,
        IOptions<OAuthOptions> options,
        ILogger<OAuthService> logger)
    {
        _oauth = oauth;
        _apiTokens = apiTokens;
        _currentUser = currentUser;
        _timeProvider = timeProvider;
        _options = options.Value;
        _logger = logger;
    }

    // ---------------------------------------------------------------------------
    // Discovery
    // ---------------------------------------------------------------------------

    public IReadOnlyDictionary<string, object> GetMetadata()
    {
        EnsureConfigured();
        var issuer = _options.IssuerBase;

        return new Dictionary<string, object>
        {
            ["issuer"] = issuer,
            ["authorization_endpoint"] = $"{issuer}/oauth/authorize",
            ["token_endpoint"] = $"{issuer}/oauth/token",
            ["registration_endpoint"] = $"{issuer}/oauth/register",
            ["revocation_endpoint"] = $"{issuer}/oauth/revoke",
            ["response_types_supported"] = new[] { "code" },
            ["grant_types_supported"] = new[] { "authorization_code", "refresh_token" },
            ["code_challenge_methods_supported"] = new[] { "S256" },
            ["token_endpoint_auth_methods_supported"] = new[] { "none" },
            ["revocation_endpoint_auth_methods_supported"] = new[] { "none" },
            ["scopes_supported"] = new[] { PostsScope },
            ["authorization_response_iss_parameter_supported"] = true
        };
    }

    // ---------------------------------------------------------------------------
    // Registration
    // ---------------------------------------------------------------------------

    public async Task<ClientRegistrationResponseDto> RegisterClientAsync(
        ClientRegistrationRequestDto request,
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        if (request.TokenEndpointAuthMethod is { } method && method != "none")
        {
            throw new OAuthException(
                "invalid_client_metadata",
                "Only public clients are supported: token_endpoint_auth_method must be \"none\".");
        }

        if (request.GrantTypes is { Count: > 0 } grants &&
            grants.Any(g => g is not ("authorization_code" or "refresh_token")))
        {
            throw new OAuthException(
                "invalid_client_metadata",
                "Only the authorization_code and refresh_token grant types are supported.");
        }

        if (request.ResponseTypes is { Count: > 0 } responses && responses.Any(r => r != "code"))
            throw new OAuthException("invalid_client_metadata", "Only the \"code\" response type is supported.");

        var redirectUris = (request.RedirectUris ?? [])
            .Where(uri => !string.IsNullOrWhiteSpace(uri))
            .Select(uri => uri.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (redirectUris.Count is 0 or > MaxRedirectUris)
        {
            throw new OAuthException(
                "invalid_redirect_uri",
                $"Register between 1 and {MaxRedirectUris} redirect_uris.");
        }

        foreach (var uri in redirectUris)
        {
            if (!IsAcceptableRedirectUri(uri))
            {
                throw new OAuthException(
                    "invalid_redirect_uri",
                    $"'{uri}' cannot be a redirect URI. Use https, an http loopback address " +
                    "(localhost, 127.0.0.1, [::1]), or an app's private scheme; no fragments.");
            }
        }

        var name = string.IsNullOrWhiteSpace(request.ClientName) ? "Unnamed client" : request.ClientName.Trim();
        if (name.Length > MaxClientNameLength)
            name = name[..MaxClientNameLength];

        var now = _timeProvider.GetUtcNow();

        var client = new OAuthClient
        {
            ClientId = OAuthSecrets.CreateClientId(),
            ClientName = name,
            RedirectUris = redirectUris,
            CreatedAt = now
        };

        await _oauth.AddClientAsync(client, cancellationToken);
        await _oauth.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Registered OAuth client {ClientId} ({ClientName}).", client.ClientId, client.ClientName);

        return new ClientRegistrationResponseDto
        {
            ClientId = client.ClientId,
            ClientIdIssuedAt = now.ToUnixTimeSeconds(),
            ClientName = client.ClientName,
            RedirectUris = client.RedirectUris,
            GrantTypes = ["authorization_code", "refresh_token"],
            ResponseTypes = ["code"],
            TokenEndpointAuthMethod = "none"
        };
    }

    // ---------------------------------------------------------------------------
    // Authorization
    // ---------------------------------------------------------------------------

    public async Task<string> BeginAuthorizationAsync(
        AuthorizeRequestDto request,
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        // Validated here so a broken request fails before the author is asked anything, and
        // again on the consent page's two calls, since nothing stops those being forged.
        await ValidateAsync(request, cancellationToken);

        var query = new Dictionary<string, string?>
        {
            ["client_id"] = request.ClientId,
            ["redirect_uri"] = request.RedirectUri,
            ["response_type"] = request.ResponseType,
            ["code_challenge"] = request.CodeChallenge,
            ["code_challenge_method"] = request.CodeChallengeMethod,
            ["state"] = request.State,
            ["scope"] = request.Scope,
            ["resource"] = request.Resource
        };

        return QueryHelpers.AddQueryString(
            _options.ConsentUrl,
            query.Where(pair => pair.Value is not null)!);
    }

    public async Task<ConsentDescriptionDto> DescribeConsentAsync(
        AuthorizeRequestDto request,
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        _currentUser.RequireAuthorId();

        var validated = await ValidateForStudioAsync(request, cancellationToken);
        var redirect = new Uri(validated.RedirectUri);
        var loopback = IsLoopback(redirect);

        return new ConsentDescriptionDto
        {
            ClientId = validated.Client.ClientId,
            ClientName = validated.Client.ClientName,
            RedirectTarget = loopback ? "an app on this computer" : redirect.GetLeftPart(UriPartial.Authority),
            RedirectsToThisComputer = loopback,
            Scope = validated.Scope,
            Resource = validated.Resource,
            Allows =
            [
                "Read your posts, drafts and their media",
                "Create new posts as drafts",
                "Edit drafts and add pictures, a thumbnail and a trailer to them",
                "Publish a draft when you ask it to"
            ],
            NeverAllows =
            [
                "Delete anything",
                "Edit or unpublish a post that is already live",
                "Replace an existing thumbnail or trailer",
                "Touch your profile, timeline, credentials or account"
            ]
        };
    }

    public async Task<ConsentResultDto> DecideConsentAsync(
        ConsentDecisionDto decision,
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        var authorId = _currentUser.RequireAuthorId();

        var validated = await ValidateForStudioAsync(decision, cancellationToken);

        if (!decision.Approve)
        {
            _logger.LogInformation(
                "Author {AuthorId} declined OAuth client {ClientId}.",
                authorId,
                validated.Client.ClientId);

            return new ConsentResultDto
            {
                RedirectUrl = ErrorRedirect(validated.RedirectUri, "access_denied", "The author declined.", decision.State)
            };
        }

        var now = _timeProvider.GetUtcNow();
        var rawCode = OAuthSecrets.Create(OAuthSecrets.CodePrefix);

        await _oauth.AddCodeAsync(new OAuthAuthorizationCode
        {
            CodeHash = OAuthSecrets.Hash(rawCode),
            OAuthClientId = validated.Client.Id,
            AuthorId = authorId,
            RedirectUri = validated.RedirectUri,
            CodeChallenge = validated.CodeChallenge,
            Scope = validated.Scope,
            Resource = validated.Resource,
            CreatedAt = now,
            ExpiresAt = now.AddSeconds(_options.AuthorizationCodeSeconds)
        }, cancellationToken);

        await _oauth.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Author {AuthorId} approved OAuth client {ClientId}.",
            authorId,
            validated.Client.ClientId);

        var query = new Dictionary<string, string?>
        {
            ["code"] = rawCode,
            ["state"] = decision.State,
            // RFC 9207: tells the client which server answered, against mix-up attacks.
            ["iss"] = _options.IssuerBase
        };

        return new ConsentResultDto
        {
            RedirectUrl = QueryHelpers.AddQueryString(
                validated.RedirectUri,
                query.Where(pair => pair.Value is not null)!)
        };
    }

    /// <summary>
    /// What the studio sees of a bad request. An error the client should hear about goes back
    /// to it as a redirect; one about the client or the redirect itself is shown to the author.
    /// </summary>
    private async Task<ValidatedRequest> ValidateForStudioAsync(
        AuthorizeRequestDto request,
        CancellationToken cancellationToken)
    {
        try
        {
            return await ValidateAsync(request, cancellationToken);
        }
        catch (OAuthException exception) when (!exception.Redirectable)
        {
            throw new ValidationException(exception.Message);
        }
    }

    /// <summary>
    /// Checks an authorization request. Problems with the client or its redirect are thrown
    /// as non-redirectable; anything after that is the client's to hear about.
    /// </summary>
    private async Task<ValidatedRequest> ValidateAsync(
        AuthorizeRequestDto request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ClientId))
            throw NotRedirectable("invalid_request", "client_id is required.");

        var client = await _oauth.GetClientAsync(request.ClientId, cancellationToken)
            ?? throw NotRedirectable("invalid_client", "This client is not registered. Register it first.");

        if (string.IsNullOrWhiteSpace(request.RedirectUri))
            throw NotRedirectable("invalid_request", "redirect_uri is required.");

        if (!client.RedirectUris.Any(registered => RedirectMatches(registered, request.RedirectUri)))
            throw NotRedirectable("invalid_request", "redirect_uri does not match any URI this client registered.");

        if (request.ResponseType != "code")
            throw new OAuthException("unsupported_response_type", "Only response_type=code is supported.");

        if (string.IsNullOrWhiteSpace(request.CodeChallenge) || request.CodeChallengeMethod != "S256")
            throw new OAuthException("invalid_request", "PKCE is required: send code_challenge with code_challenge_method=S256.");

        if (!CodeChallengePattern().IsMatch(request.CodeChallenge))
            throw new OAuthException("invalid_request", "code_challenge is not a valid S256 challenge.");

        if (request.State is { Length: > MaxStateLength })
            throw new OAuthException("invalid_request", $"state is longer than {MaxStateLength} characters.");

        var resource = NormalizeResource(request.Resource);

        return new ValidatedRequest(client, request.RedirectUri, request.CodeChallenge, PostsScope, resource);
    }

    // ---------------------------------------------------------------------------
    // Tokens
    // ---------------------------------------------------------------------------

    public Task<TokenResponseDto> ExchangeAsync(TokenRequestDto request, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        return request.GrantType switch
        {
            "authorization_code" => RedeemCodeAsync(request, cancellationToken),
            "refresh_token" => RefreshAsync(request, cancellationToken),
            null or "" => throw new OAuthException("invalid_request", "grant_type is required."),
            _ => throw new OAuthException("unsupported_grant_type", $"grant_type '{request.GrantType}' is not supported.")
        };
    }

    private async Task<TokenResponseDto> RedeemCodeAsync(TokenRequestDto request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Code) ||
            string.IsNullOrWhiteSpace(request.ClientId) ||
            string.IsNullOrWhiteSpace(request.RedirectUri) ||
            string.IsNullOrWhiteSpace(request.CodeVerifier))
        {
            throw new OAuthException(
                "invalid_request",
                "code, client_id, redirect_uri and code_verifier are all required.");
        }

        var client = await RequireClientAsync(request.ClientId, cancellationToken);
        var code = await _oauth.GetCodeByHashAsync(OAuthSecrets.Hash(request.Code), cancellationToken);
        var now = _timeProvider.GetUtcNow();

        if (code is null || code.OAuthClientId != client.Id)
            throw InvalidGrant("The authorization code is not valid.");

        if (code.ConsumedAt is not null)
            await RevokeReplayedCodeAsync(code, client, now, cancellationToken);

        if (code.ExpiresAt <= now)
            throw InvalidGrant("The authorization code has expired. Start the sign-in again.");

        if (!string.Equals(code.RedirectUri, request.RedirectUri, StringComparison.Ordinal))
            throw InvalidGrant("redirect_uri does not match the one the code was issued to.");

        if (!CodeVerifierPattern().IsMatch(request.CodeVerifier) ||
            !OAuthSecrets.FixedTimeEquals(OAuthSecrets.ChallengeFor(request.CodeVerifier), code.CodeChallenge))
        {
            throw InvalidGrant("code_verifier does not match the code_challenge.");
        }

        if (request.Resource is not null &&
            !string.Equals(NormalizeResource(request.Resource), code.Resource, StringComparison.Ordinal))
        {
            throw new OAuthException("invalid_target", "resource does not match the one that was authorized.");
        }

        if (!await _oauth.TryConsumeCodeAsync(code.Id, now, cancellationToken))
            await RevokeReplayedCodeAsync(code, client, now, cancellationToken);

        var grant = new OAuthGrant
        {
            AuthorId = code.AuthorId,
            OAuthClientId = client.Id,
            Scope = code.Scope,
            Resource = code.Resource,
            CreatedAt = now,
            LastUsedAt = now
        };

        await _oauth.AddGrantAsync(grant, cancellationToken);
        var response = await IssueAsync(grant, client, now, cancellationToken);

        code.OAuthGrantId = grant.Id;
        await _oauth.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Issued grant {GrantId} to client {ClientId} for author {AuthorId}.",
            grant.Id,
            client.ClientId,
            grant.AuthorId);

        return response;
    }

    /// <summary>
    /// A code presented twice means someone else has it too, so whatever the first use
    /// produced is cut off (RFC 6749 §4.1.2). Always throws.
    /// </summary>
    private async Task RevokeReplayedCodeAsync(
        OAuthAuthorizationCode code,
        OAuthClient client,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (code.OAuthGrantId is { } spentGrant)
        {
            await _oauth.RevokeGrantAsync(spentGrant, now, cancellationToken);
            _logger.LogWarning(
                "Authorization code replayed for client {ClientId}; revoked grant {GrantId}.",
                client.ClientId,
                spentGrant);
        }

        throw InvalidGrant("The authorization code is not valid.");
    }

    private async Task<TokenResponseDto> RefreshAsync(TokenRequestDto request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken) || string.IsNullOrWhiteSpace(request.ClientId))
            throw new OAuthException("invalid_request", "refresh_token and client_id are both required.");

        var client = await RequireClientAsync(request.ClientId, cancellationToken);
        var stored = await _oauth.GetRefreshTokenByHashAsync(OAuthSecrets.Hash(request.RefreshToken), cancellationToken);
        var now = _timeProvider.GetUtcNow();

        if (stored is null || stored.Grant.OAuthClientId != client.Id)
            throw InvalidGrant("The refresh token is not valid.");

        var grant = stored.Grant;

        if (grant.RevokedAt is not null || stored.ExpiresAt <= now)
            throw InvalidGrant("The refresh token is not valid. Connect again.");

        if (!await _oauth.TryConsumeRefreshTokenAsync(stored.Id, now, cancellationToken))
        {
            // NOTE: a spent refresh token coming back means it was copied. Rotation exists so
            // this can be noticed; the only safe answer is to end the whole grant.
            await _oauth.RevokeGrantAsync(grant.Id, now, cancellationToken);
            _logger.LogWarning(
                "Refresh token replayed on grant {GrantId} (client {ClientId}); grant revoked.",
                grant.Id,
                client.ClientId);

            throw InvalidGrant("The refresh token is not valid. Connect again.");
        }

        grant.LastUsedAt = now;
        await _oauth.PruneGrantAsync(grant.Id, now, cancellationToken);

        var response = await IssueAsync(grant, client, now, cancellationToken);
        await _oauth.SaveChangesAsync(cancellationToken);
        return response;
    }

    /// <summary>A fresh access token and refresh token on a grant. Saved by the caller.</summary>
    private async Task<TokenResponseDto> IssueAsync(
        OAuthGrant grant,
        OAuthClient client,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var (rawAccess, accessHash, preview) = ApiTokenGenerator.Create();
        var rawRefresh = OAuthSecrets.Create(OAuthSecrets.RefreshTokenPrefix);
        var name = client.ClientName.Length > MaxTokenNameLength
            ? client.ClientName[..MaxTokenNameLength]
            : client.ClientName;

        grant.AccessTokens.Add(new ApiToken
        {
            AuthorId = grant.AuthorId,
            Name = name,
            TokenHash = accessHash,
            Prefix = preview,
            Scope = ApiTokenScope.Author,
            CreatedAt = now,
            ExpiresAt = now.AddMinutes(_options.AccessTokenMinutes)
        });

        grant.RefreshTokens.Add(new OAuthRefreshToken
        {
            TokenHash = OAuthSecrets.Hash(rawRefresh),
            CreatedAt = now,
            ExpiresAt = now.AddDays(_options.RefreshTokenDays)
        });

        // NOTE: saved here so the grant has its id before the caller records it on the code.
        await _oauth.SaveChangesAsync(cancellationToken);

        return new TokenResponseDto
        {
            AccessToken = rawAccess,
            ExpiresIn = _options.AccessTokenMinutes * 60,
            RefreshToken = rawRefresh,
            Scope = grant.Scope
        };
    }

    public async Task RevokeTokenAsync(string? token, string? clientId, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        // RFC 7009 §2.2: an unknown or malformed token is not an error, so a client can always
        // treat "revoked" as done.
        if (string.IsNullOrWhiteSpace(token))
            return;

        var now = _timeProvider.GetUtcNow();

        if (token.StartsWith(OAuthSecrets.RefreshTokenPrefix, StringComparison.Ordinal))
        {
            var stored = await _oauth.GetRefreshTokenByHashAsync(OAuthSecrets.Hash(token), cancellationToken);

            if (stored is not null && (clientId is null || stored.Grant.Client.ClientId == clientId))
                await _oauth.RevokeGrantAsync(stored.OAuthGrantId, now, cancellationToken);

            return;
        }

        if (ApiTokenGenerator.LooksLikeApiToken(token))
        {
            var stored = await _apiTokens.GetByHashAsync(ApiTokenGenerator.Hash(token), tracked: true, cancellationToken);

            // Only a token this flow minted. A hand-issued token is revoked from the studio.
            if (stored is { OAuthGrantId: not null, RevokedAt: null })
            {
                stored.RevokedAt = now;
                await _apiTokens.SaveChangesAsync(cancellationToken);
            }
        }
    }

    // ---------------------------------------------------------------------------
    // Connected apps
    // ---------------------------------------------------------------------------

    public async Task<IReadOnlyList<OAuthGrantDto>> GetGrantsAsync(CancellationToken cancellationToken = default)
    {
        var grants = await _oauth.GetGrantsByAuthorAsync(_currentUser.RequireAuthorId(), cancellationToken);
        return grants.Select(ToDto).ToList();
    }

    public async Task<OAuthGrantDto> RevokeGrantAsync(int id, CancellationToken cancellationToken = default)
    {
        var authorId = _currentUser.RequireAuthorId();

        var grant = await _oauth.GetGrantAsync(id, authorId, cancellationToken)
            ?? throw NotFoundException.For("Connected app", id);

        if (grant.RevokedAt is null)
        {
            var now = _timeProvider.GetUtcNow();
            await _oauth.RevokeGrantAsync(grant.Id, now, cancellationToken);
            grant.RevokedAt = now;
        }

        return ToDto(grant);
    }

    private static OAuthGrantDto ToDto(OAuthGrant grant) => new()
    {
        Id = grant.Id,
        ClientName = grant.Client.ClientName,
        Scope = grant.Scope,
        CreatedAt = grant.CreatedAt,
        LastUsedAt = grant.LastUsedAt,
        RevokedAt = grant.RevokedAt,
        IsActive = grant.RevokedAt is null
    };

    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    private void EnsureConfigured()
    {
        if (!_options.IsConfigured)
        {
            throw new NotConfiguredException(
                $"OAuth is not configured on this server. Set '{OAuthOptions.SectionName}:Issuer' " +
                $"and '{OAuthOptions.SectionName}:ConsentUrl' to enable it.");
        }
    }

    private async Task<OAuthClient> RequireClientAsync(string clientId, CancellationToken cancellationToken) =>
        await _oauth.GetClientAsync(clientId, cancellationToken)
            ?? throw new OAuthException("invalid_client", "This client is not registered.", StatusCodes.Status401Unauthorized);

    /// <summary>
    /// The resource a token is for, reduced to its origin and checked against the allowed
    /// list. An MCP client sends the server's full URL (…/mcp); the origin is what matters.
    /// </summary>
    private string? NormalizeResource(string? resource)
    {
        if (string.IsNullOrWhiteSpace(resource))
            return null;

        if (!Uri.TryCreate(resource, UriKind.Absolute, out var uri) || !string.IsNullOrEmpty(uri.Fragment))
            throw new OAuthException("invalid_target", "resource must be an absolute URI with no fragment.");

        var origin = uri.GetLeftPart(UriPartial.Authority);

        // NOTE: no list means a development setup where the MCP server moves around; a real
        // deployment names its MCP server and nothing else is accepted.
        if (_options.AllowedResources.Count > 0 &&
            !_options.AllowedResources.Any(allowed =>
                string.Equals(allowed.TrimEnd('/'), origin, StringComparison.OrdinalIgnoreCase)))
        {
            throw new OAuthException("invalid_target", $"Tokens cannot be issued for '{resource}'.");
        }

        return origin;
    }

    internal static bool IsAcceptableRedirectUri(string value)
    {
        if (value.Length > MaxRedirectUriLength ||
            !Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            !string.IsNullOrEmpty(uri.Fragment) ||
            value.Contains('#'))
        {
            return false;
        }

        if (uri.Scheme == Uri.UriSchemeHttps)
            return true;

        if (uri.Scheme == Uri.UriSchemeHttp)
            return IsLoopback(uri);

        return !ForbiddenSchemes.Contains(uri.Scheme, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Exact match, except that a loopback redirect may come back on any port: a native app
    /// takes whichever port is free when it starts listening (RFC 8252 §7.3).
    /// </summary>
    internal static bool RedirectMatches(string registered, string requested)
    {
        if (string.Equals(registered, requested, StringComparison.Ordinal))
            return true;

        if (!Uri.TryCreate(registered, UriKind.Absolute, out var a) ||
            !Uri.TryCreate(requested, UriKind.Absolute, out var b))
        {
            return false;
        }

        return a.Scheme == Uri.UriSchemeHttp &&
               b.Scheme == Uri.UriSchemeHttp &&
               IsLoopback(a) &&
               IsLoopback(b) &&
               string.Equals(a.Host, b.Host, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(a.PathAndQuery, b.PathAndQuery, StringComparison.Ordinal);
    }

    private static bool IsLoopback(Uri uri) =>
        uri.IsLoopback ||
        (IPAddress.TryParse(uri.Host.Trim('[', ']'), out var address) && IPAddress.IsLoopback(address));

    private static string ErrorRedirect(string redirectUri, string error, string description, string? state)
    {
        var query = new Dictionary<string, string?>
        {
            ["error"] = error,
            ["error_description"] = description,
            ["state"] = state
        };

        return QueryHelpers.AddQueryString(redirectUri, query.Where(pair => pair.Value is not null)!);
    }

    /// <summary>The error redirect for a request whose client and redirect have already checked out.</summary>
    public string BuildErrorRedirect(AuthorizeRequestDto request, OAuthException exception) =>
        ErrorRedirect(request.RedirectUri!, exception.Error, exception.Message, request.State);

    private static OAuthException NotRedirectable(string error, string description) =>
        new(error, description) { Redirectable = false };

    private static OAuthException InvalidGrant(string description) => new("invalid_grant", description);

    // RFC 7636 §4.1–4.2: 43–128 unreserved characters; an S256 challenge is exactly 43.
    [GeneratedRegex("^[A-Za-z0-9._~-]{43,128}$")]
    private static partial Regex CodeVerifierPattern();

    [GeneratedRegex("^[A-Za-z0-9_-]{43}$")]
    private static partial Regex CodeChallengePattern();

    private sealed record ValidatedRequest(
        OAuthClient Client,
        string RedirectUri,
        string CodeChallenge,
        string Scope,
        string? Resource);
}
