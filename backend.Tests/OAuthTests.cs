using Backend.Common.Exceptions;
using Backend.Common.Options;
using Backend.Common.Security;
using Backend.Models.DTOs.OAuth;
using Backend.Models.Entities;
using Backend.Services;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Backend.Tests;

/// <summary>
/// The authorization server an MCP connector signs in through: registration, consent, the
/// code exchange with PKCE, refresh rotation, and the ways each of them refuses.
/// </summary>
public class OAuthTests
{
    private const string Redirect = "http://127.0.0.1:33418/callback";

    private const string Verifier = "a-verifier-that-is-long-enough-to-satisfy-rfc-7636-forty-three";

    [Theory]
    [InlineData("https://claude.ai/api/mcp/auth_callback")]
    [InlineData("http://localhost:4000/callback")]
    [InlineData("http://127.0.0.1/callback")]
    [InlineData("http://[::1]:9000/cb")]
    [InlineData("cursor://anysphere.cursor-retrieval/oauth/callback")]
    public async Task A_client_may_register_safe_redirects(string redirect)
    {
        await using var harness = await TestHarness.CreateAsync();

        var client = await harness.OAuth.RegisterClientAsync(
            new ClientRegistrationRequestDto { RedirectUris = [redirect], ClientName = "Claude Code" });

        Assert.StartsWith("mcp_", client.ClientId);
        Assert.Equal([redirect], client.RedirectUris);
        Assert.Equal("none", client.TokenEndpointAuthMethod);
    }

    [Theory]
    [InlineData("http://evil.example.com/callback")]
    [InlineData("https://example.com/callback#fragment")]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,hi")]
    [InlineData("not a uri")]
    public async Task A_client_may_not_register_an_unsafe_redirect(string redirect)
    {
        await using var harness = await TestHarness.CreateAsync();

        var error = await Assert.ThrowsAsync<OAuthException>(() => harness.OAuth.RegisterClientAsync(
            new ClientRegistrationRequestDto { RedirectUris = [redirect] }));

        Assert.Equal("invalid_redirect_uri", error.Error);
    }

    [Fact]
    public async Task A_confidential_client_is_turned_away()
    {
        await using var harness = await TestHarness.CreateAsync();

        var error = await Assert.ThrowsAsync<OAuthException>(() => harness.OAuth.RegisterClientAsync(
            new ClientRegistrationRequestDto
            {
                RedirectUris = [Redirect],
                TokenEndpointAuthMethod = "client_secret_basic"
            }));

        Assert.Equal("invalid_client_metadata", error.Error);
    }

    [Fact]
    public async Task The_whole_flow_ends_in_a_short_lived_author_scoped_token()
    {
        await using var harness = await TestHarness.CreateAsync();
        var author = await harness.AddAuthorAsync("author@example.com", "a long enough password");
        var clientId = await RegisterAsync(harness);
        var request = Request(clientId);

        // The authorize endpoint only forwards to the studio, with the request intact.
        var consentUrl = await harness.OAuth.BeginAuthorizationAsync(request);
        Assert.StartsWith(harness.OAuthOptions.ConsentUrl + "?", consentUrl);
        Assert.Equal(clientId, QueryHelpers.ParseQuery(new Uri(consentUrl).Query)["client_id"]);

        harness.SignIn(author);
        var description = await harness.OAuth.DescribeConsentAsync(request);
        Assert.Equal("Test client", description.ClientName);
        Assert.True(description.RedirectsToThisComputer);
        Assert.Equal("https://mcp.example.com", description.Resource);

        var code = await ApproveAsync(harness, request);

        harness.SignOut();
        var tokens = await harness.OAuth.ExchangeAsync(CodeExchange(clientId, code));

        Assert.StartsWith(ApiTokenGenerator.Prefix, tokens.AccessToken);
        Assert.StartsWith(OAuthSecrets.RefreshTokenPrefix, tokens.RefreshToken);
        Assert.Equal(60 * 60, tokens.ExpiresIn);
        Assert.Equal(OAuthService.PostsScope, tokens.Scope);

        var stored = await harness.ApiTokens.GetByHashAsync(ApiTokenGenerator.Hash(tokens.AccessToken), tracked: false);
        Assert.NotNull(stored);
        Assert.Equal(ApiTokenScope.Author, stored.Scope);
        Assert.Equal(author.Id, stored.AuthorId);
        Assert.Equal(harness.TimeProvider.GetUtcNow().AddHours(1), stored.ExpiresAt);
        Assert.NotNull(stored.OAuthGrantId);
    }

    [Fact]
    public async Task The_redirect_carries_state_and_issuer_back()
    {
        await using var harness = await TestHarness.CreateAsync();
        var author = await harness.AddAuthorAsync("author@example.com", "a long enough password");
        var clientId = await RegisterAsync(harness);
        harness.SignIn(author);

        var result = await harness.OAuth.DecideConsentAsync(Decision(Request(clientId), approve: true));
        var query = QueryHelpers.ParseQuery(new Uri(result.RedirectUrl).Query);

        Assert.StartsWith(Redirect + "?", result.RedirectUrl);
        Assert.Equal("the-state", query["state"]);
        Assert.Equal("https://api.example.com", query["iss"]);
        Assert.StartsWith(OAuthSecrets.CodePrefix, query["code"].ToString());
    }

    [Fact]
    public async Task Declining_sends_access_denied_back_and_issues_nothing()
    {
        await using var harness = await TestHarness.CreateAsync();
        var author = await harness.AddAuthorAsync("author@example.com", "a long enough password");
        var clientId = await RegisterAsync(harness);
        harness.SignIn(author);

        var result = await harness.OAuth.DecideConsentAsync(Decision(Request(clientId), approve: false));
        var query = QueryHelpers.ParseQuery(new Uri(result.RedirectUrl).Query);

        Assert.Equal("access_denied", query["error"]);
        Assert.False(query.ContainsKey("code"));
        Assert.Equal(0, await harness.Context.OAuthAuthorizationCodes.CountAsync());
    }

    [Fact]
    public async Task Consent_needs_a_signed_in_author()
    {
        await using var harness = await TestHarness.CreateAsync();
        var clientId = await RegisterAsync(harness);

        await Assert.ThrowsAsync<UnauthorizedException>(
            () => harness.OAuth.DecideConsentAsync(Decision(Request(clientId), approve: true)));
    }

    [Fact]
    public async Task An_unregistered_redirect_is_never_redirected_to()
    {
        await using var harness = await TestHarness.CreateAsync();
        var clientId = await RegisterAsync(harness);

        var error = await Assert.ThrowsAsync<OAuthException>(() => harness.OAuth.BeginAuthorizationAsync(
            Request(clientId, redirectUri: "https://attacker.example.com/callback")));

        Assert.False(error.Redirectable);
    }

    [Fact]
    public async Task An_unknown_client_is_never_redirected_to()
    {
        await using var harness = await TestHarness.CreateAsync();

        var error = await Assert.ThrowsAsync<OAuthException>(
            () => harness.OAuth.BeginAuthorizationAsync(Request("mcp_nobody")));

        Assert.False(error.Redirectable);
        Assert.Equal("invalid_client", error.Error);
    }

    [Fact]
    public async Task A_loopback_redirect_may_come_back_on_another_port()
    {
        await using var harness = await TestHarness.CreateAsync();
        var clientId = await RegisterAsync(harness);

        var consentUrl = await harness.OAuth.BeginAuthorizationAsync(
            Request(clientId, redirectUri: "http://127.0.0.1:51234/callback"));

        Assert.StartsWith(harness.OAuthOptions.ConsentUrl, consentUrl);
    }

    [Fact]
    public async Task A_loopback_redirect_may_not_change_its_path()
    {
        await using var harness = await TestHarness.CreateAsync();
        var clientId = await RegisterAsync(harness);

        var error = await Assert.ThrowsAsync<OAuthException>(() => harness.OAuth.BeginAuthorizationAsync(
            Request(clientId, redirectUri: "http://127.0.0.1:33418/elsewhere")));

        Assert.False(error.Redirectable);
    }

    [Fact]
    public async Task Pkce_is_required_and_only_s256()
    {
        await using var harness = await TestHarness.CreateAsync();
        var clientId = await RegisterAsync(harness);

        var missing = await Assert.ThrowsAsync<OAuthException>(() => harness.OAuth.BeginAuthorizationAsync(
            Request(clientId) with { CodeChallenge = null }));
        var plain = await Assert.ThrowsAsync<OAuthException>(() => harness.OAuth.BeginAuthorizationAsync(
            Request(clientId) with { CodeChallengeMethod = "plain" }));

        Assert.True(missing.Redirectable);
        Assert.Equal("invalid_request", missing.Error);
        Assert.Equal("invalid_request", plain.Error);
    }

    [Fact]
    public async Task A_token_cannot_be_asked_for_some_other_resource()
    {
        await using var harness = await TestHarness.CreateAsync();
        var clientId = await RegisterAsync(harness);

        var error = await Assert.ThrowsAsync<OAuthException>(() => harness.OAuth.BeginAuthorizationAsync(
            Request(clientId) with { Resource = "https://other.example.com/mcp" }));

        Assert.Equal("invalid_target", error.Error);
    }

    [Fact]
    public async Task The_wrong_verifier_gets_nothing()
    {
        await using var harness = await TestHarness.CreateAsync();
        var (clientId, code) = await AuthorizeAsync(harness);

        var error = await Assert.ThrowsAsync<OAuthException>(() => harness.OAuth.ExchangeAsync(
            CodeExchange(clientId, code) with { CodeVerifier = new string('x', 64) }));

        Assert.Equal("invalid_grant", error.Error);
    }

    [Fact]
    public async Task A_code_is_bound_to_its_client_and_redirect()
    {
        await using var harness = await TestHarness.CreateAsync();
        var (clientId, code) = await AuthorizeAsync(harness);
        var otherClient = await RegisterAsync(harness);

        var wrongClient = await Assert.ThrowsAsync<OAuthException>(() => harness.OAuth.ExchangeAsync(
            CodeExchange(otherClient, code)));
        var wrongRedirect = await Assert.ThrowsAsync<OAuthException>(() => harness.OAuth.ExchangeAsync(
            CodeExchange(clientId, code) with { RedirectUri = "http://127.0.0.1:1/callback" }));

        Assert.Equal("invalid_grant", wrongClient.Error);
        Assert.Equal("invalid_grant", wrongRedirect.Error);
    }

    [Fact]
    public async Task An_expired_code_is_refused()
    {
        await using var harness = await TestHarness.CreateAsync();
        var (clientId, code) = await AuthorizeAsync(harness);

        harness.TimeProvider.Advance(TimeSpan.FromMinutes(6));

        var error = await Assert.ThrowsAsync<OAuthException>(
            () => harness.OAuth.ExchangeAsync(CodeExchange(clientId, code)));

        Assert.Equal("invalid_grant", error.Error);
    }

    [Fact]
    public async Task Spending_a_code_twice_revokes_what_the_first_spend_produced()
    {
        await using var harness = await TestHarness.CreateAsync();
        var (clientId, code) = await AuthorizeAsync(harness);

        var first = await harness.OAuth.ExchangeAsync(CodeExchange(clientId, code));

        await Assert.ThrowsAsync<OAuthException>(() => harness.OAuth.ExchangeAsync(CodeExchange(clientId, code)));

        harness.Context.ChangeTracker.Clear();
        var access = await harness.ApiTokens.GetByHashAsync(ApiTokenGenerator.Hash(first.AccessToken), tracked: false);
        Assert.NotNull(access!.RevokedAt);

        var refresh = await Assert.ThrowsAsync<OAuthException>(() => harness.OAuth.ExchangeAsync(
            new TokenRequestDto { GrantType = "refresh_token", ClientId = clientId, RefreshToken = first.RefreshToken }));
        Assert.Equal("invalid_grant", refresh.Error);
    }

    [Fact]
    public async Task A_refresh_rotates_and_the_old_token_stops_working()
    {
        await using var harness = await TestHarness.CreateAsync();
        var (clientId, code) = await AuthorizeAsync(harness);
        var first = await harness.OAuth.ExchangeAsync(CodeExchange(clientId, code));

        harness.TimeProvider.Advance(TimeSpan.FromMinutes(61));

        var second = await harness.OAuth.ExchangeAsync(
            new TokenRequestDto { GrantType = "refresh_token", ClientId = clientId, RefreshToken = first.RefreshToken });

        Assert.NotEqual(first.RefreshToken, second.RefreshToken);
        Assert.NotEqual(first.AccessToken, second.AccessToken);

        // NOTE: the expired access token was pruned by the refresh rather than left to pile up.
        harness.Context.ChangeTracker.Clear();
        Assert.Null(await harness.ApiTokens.GetByHashAsync(ApiTokenGenerator.Hash(first.AccessToken), tracked: false));
    }

    [Fact]
    public async Task Replaying_a_spent_refresh_token_ends_the_grant()
    {
        await using var harness = await TestHarness.CreateAsync();
        var (clientId, code) = await AuthorizeAsync(harness);
        var first = await harness.OAuth.ExchangeAsync(CodeExchange(clientId, code));

        var second = await harness.OAuth.ExchangeAsync(
            new TokenRequestDto { GrantType = "refresh_token", ClientId = clientId, RefreshToken = first.RefreshToken });

        // Somebody else replays the first one.
        await Assert.ThrowsAsync<OAuthException>(() => harness.OAuth.ExchangeAsync(
            new TokenRequestDto { GrantType = "refresh_token", ClientId = clientId, RefreshToken = first.RefreshToken }));

        // ...and the legitimate holder is cut off too, because there is no telling them apart.
        await Assert.ThrowsAsync<OAuthException>(() => harness.OAuth.ExchangeAsync(
            new TokenRequestDto { GrantType = "refresh_token", ClientId = clientId, RefreshToken = second.RefreshToken }));

        harness.Context.ChangeTracker.Clear();
        var access = await harness.ApiTokens.GetByHashAsync(ApiTokenGenerator.Hash(second.AccessToken), tracked: false);
        Assert.NotNull(access!.RevokedAt);
    }

    [Fact]
    public async Task Disconnecting_an_app_in_the_studio_stops_its_tokens_at_once()
    {
        await using var harness = await TestHarness.CreateAsync();
        var author = await harness.AddAuthorAsync("author@example.com", "a long enough password");
        var (clientId, code) = await AuthorizeAsync(harness, author);
        var tokens = await harness.OAuth.ExchangeAsync(CodeExchange(clientId, code));

        harness.SignIn(author);
        var grants = await harness.OAuth.GetGrantsAsync();
        var grant = Assert.Single(grants);
        Assert.True(grant.IsActive);

        var revoked = await harness.OAuth.RevokeGrantAsync(grant.Id);
        Assert.False(revoked.IsActive);

        harness.Context.ChangeTracker.Clear();
        var access = await harness.ApiTokens.GetByHashAsync(ApiTokenGenerator.Hash(tokens.AccessToken), tracked: false);
        Assert.NotNull(access!.RevokedAt);

        await Assert.ThrowsAsync<OAuthException>(() => harness.OAuth.ExchangeAsync(
            new TokenRequestDto { GrantType = "refresh_token", ClientId = clientId, RefreshToken = tokens.RefreshToken }));
    }

    [Fact]
    public async Task Another_authors_app_cannot_be_disconnected()
    {
        await using var harness = await TestHarness.CreateAsync();
        var owner = await harness.AddAuthorAsync("owner@example.com", "a long enough password");
        var other = await harness.AddAuthorAsync("other@example.com", "a long enough password");
        var (clientId, code) = await AuthorizeAsync(harness, owner);
        await harness.OAuth.ExchangeAsync(CodeExchange(clientId, code));

        harness.SignIn(owner);
        var grant = Assert.Single(await harness.OAuth.GetGrantsAsync());

        harness.SignIn(other);
        Assert.Empty(await harness.OAuth.GetGrantsAsync());
        await Assert.ThrowsAsync<NotFoundException>(() => harness.OAuth.RevokeGrantAsync(grant.Id));
    }

    [Fact]
    public async Task Minted_tokens_stay_off_the_studios_token_list()
    {
        await using var harness = await TestHarness.CreateAsync();
        var author = await harness.AddAuthorAsync("author@example.com", "a long enough password");
        var (clientId, code) = await AuthorizeAsync(harness, author);
        await harness.OAuth.ExchangeAsync(CodeExchange(clientId, code));

        harness.SignIn(author);

        Assert.Empty(await harness.Auth.GetApiTokensAsync());
    }

    [Fact]
    public async Task Revocation_by_refresh_token_ends_the_grant_and_never_errors()
    {
        await using var harness = await TestHarness.CreateAsync();
        var (clientId, code) = await AuthorizeAsync(harness);
        var tokens = await harness.OAuth.ExchangeAsync(CodeExchange(clientId, code));

        await harness.OAuth.RevokeTokenAsync(tokens.RefreshToken, clientId);
        await harness.OAuth.RevokeTokenAsync("pfr_nothing-like-this-exists", clientId);
        await harness.OAuth.RevokeTokenAsync(null, null);

        harness.Context.ChangeTracker.Clear();
        var access = await harness.ApiTokens.GetByHashAsync(ApiTokenGenerator.Hash(tokens.AccessToken), tracked: false);
        Assert.NotNull(access!.RevokedAt);
    }

    [Fact]
    public async Task An_unconfigured_server_says_so()
    {
        await using var harness = await TestHarness.CreateAsync();

        var service = new OAuthService(
            harness.OAuthRepository,
            harness.ApiTokens,
            harness.CurrentUser,
            harness.TimeProvider,
            Options.Create(new OAuthOptions()),
            NullLogger<OAuthService>.Instance);

        Assert.Throws<NotConfiguredException>(() => service.GetMetadata());
    }

    [Fact]
    public async Task Metadata_advertises_only_what_is_supported()
    {
        await using var harness = await TestHarness.CreateAsync();

        var metadata = harness.OAuth.GetMetadata();

        Assert.Equal("https://api.example.com", metadata["issuer"]);
        Assert.Equal("https://api.example.com/oauth/token", metadata["token_endpoint"]);
        Assert.Equal(new[] { "S256" }, metadata["code_challenge_methods_supported"]);
        Assert.Equal(new[] { "none" }, metadata["token_endpoint_auth_methods_supported"]);
    }

    // ------------------------------------------------------------------

    private static async Task<string> RegisterAsync(TestHarness harness)
    {
        var client = await harness.OAuth.RegisterClientAsync(
            new ClientRegistrationRequestDto { RedirectUris = [Redirect], ClientName = "Test client" });
        return client.ClientId;
    }

    private static AuthorizeRequest Request(string clientId, string redirectUri = Redirect) => new()
    {
        ClientId = clientId,
        RedirectUri = redirectUri,
        ResponseType = "code",
        CodeChallenge = OAuthSecrets.ChallengeFor(Verifier),
        CodeChallengeMethod = "S256",
        State = "the-state",
        Scope = "posts offline_access",
        Resource = "https://mcp.example.com/mcp"
    };

    private static ConsentDecisionDto Decision(AuthorizeRequestDto request, bool approve) => new()
    {
        ClientId = request.ClientId,
        RedirectUri = request.RedirectUri,
        ResponseType = request.ResponseType,
        CodeChallenge = request.CodeChallenge,
        CodeChallengeMethod = request.CodeChallengeMethod,
        State = request.State,
        Scope = request.Scope,
        Resource = request.Resource,
        Approve = approve
    };

    private static async Task<string> ApproveAsync(TestHarness harness, AuthorizeRequestDto request)
    {
        var result = await harness.OAuth.DecideConsentAsync(Decision(request, approve: true));
        return QueryHelpers.ParseQuery(new Uri(result.RedirectUrl).Query)["code"].ToString();
    }

    private static async Task<(string ClientId, string Code)> AuthorizeAsync(TestHarness harness, Author? author = null)
    {
        author ??= await harness.AddAuthorAsync($"{Guid.NewGuid():N}@example.com", "a long enough password");
        var clientId = await RegisterAsync(harness);

        harness.SignIn(author);
        var code = await ApproveAsync(harness, Request(clientId));
        harness.SignOut();

        return (clientId, code);
    }

    private static TokenExchange CodeExchange(string clientId, string code) => new()
    {
        GrantType = "authorization_code",
        ClientId = clientId,
        Code = code,
        RedirectUri = Redirect,
        CodeVerifier = Verifier,
        Resource = "https://mcp.example.com/mcp"
    };

    /// <summary>The DTOs with record-style copying, so a test can vary one field.</summary>
    private sealed record AuthorizeRequest
    {
        public string? ClientId { get; init; }
        public string? RedirectUri { get; init; }
        public string? ResponseType { get; init; }
        public string? CodeChallenge { get; init; }
        public string? CodeChallengeMethod { get; init; }
        public string? State { get; init; }
        public string? Scope { get; init; }
        public string? Resource { get; init; }

        public static implicit operator AuthorizeRequestDto(AuthorizeRequest r) => new()
        {
            ClientId = r.ClientId,
            RedirectUri = r.RedirectUri,
            ResponseType = r.ResponseType,
            CodeChallenge = r.CodeChallenge,
            CodeChallengeMethod = r.CodeChallengeMethod,
            State = r.State,
            Scope = r.Scope,
            Resource = r.Resource
        };
    }

    private sealed record TokenExchange
    {
        public string? GrantType { get; init; }
        public string? Code { get; init; }
        public string? RedirectUri { get; init; }
        public string? ClientId { get; init; }
        public string? CodeVerifier { get; init; }
        public string? Resource { get; init; }

        public static implicit operator TokenRequestDto(TokenExchange r) => new()
        {
            GrantType = r.GrantType,
            Code = r.Code,
            RedirectUri = r.RedirectUri,
            ClientId = r.ClientId,
            CodeVerifier = r.CodeVerifier,
            Resource = r.Resource
        };
    }
}
