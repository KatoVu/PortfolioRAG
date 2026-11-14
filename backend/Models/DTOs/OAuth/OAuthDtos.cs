using System.Text.Json.Serialization;

namespace Backend.Models.DTOs.OAuth;

/// <summary>
/// The parameters of an authorization request, as /oauth/authorize received them and the
/// studio's consent page passes them back. Nothing in here is trusted: every field is
/// validated again on each step.
/// </summary>
public class AuthorizeRequestDto
{
    public string? ClientId { get; init; }

    public string? RedirectUri { get; init; }

    public string? ResponseType { get; init; }

    public string? CodeChallenge { get; init; }

    public string? CodeChallengeMethod { get; init; }

    public string? State { get; init; }

    public string? Scope { get; init; }

    public string? Resource { get; init; }
}

/// <summary>What the consent page shows the author before they decide.</summary>
public class ConsentDescriptionDto
{
    public string ClientId { get; init; } = string.Empty;

    /// <summary>What the client calls itself. Unverified — the page says so.</summary>
    public string ClientName { get; init; } = string.Empty;

    /// <summary>Where the author's browser goes next: an origin, or "this computer" for a loopback address.</summary>
    public string RedirectTarget { get; init; } = string.Empty;

    public bool RedirectsToThisComputer { get; init; }

    public string Scope { get; init; } = string.Empty;

    public string? Resource { get; init; }

    /// <summary>The plain-language list of what approving allows, and what it never will.</summary>
    public IReadOnlyList<string> Allows { get; init; } = [];

    public IReadOnlyList<string> NeverAllows { get; init; } = [];

    /// <summary>What choosing full editing adds. The page lists it under that option, as a warning.</summary>
    public IReadOnlyList<string> FullEditingAllows { get; init; } = [];
}

public class ConsentDecisionDto : AuthorizeRequestDto
{
    public bool Approve { get; init; }

    /// <summary>Full editing instead of drafts only. Switchable later from the studio.</summary>
    public bool FullEditing { get; init; }
}

/// <summary>Switches a connected app between drafts only and full editing.</summary>
public class GrantModeDto
{
    public bool FullEditing { get; init; }
}

/// <summary>Where the consent page sends the browser: back to the client, with a code or an error.</summary>
public class ConsentResultDto
{
    public string RedirectUrl { get; init; } = string.Empty;
}

/// <summary>One connected app, as the studio lists it.</summary>
public class OAuthGrantDto
{
    public int Id { get; init; }

    public string ClientName { get; init; } = string.Empty;

    public string Scope { get; init; } = string.Empty;

    /// <summary>True when the app may also edit live posts, replace artwork, unpublish and delete.</summary>
    public bool FullEditing { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? LastUsedAt { get; init; }

    public DateTimeOffset? RevokedAt { get; init; }

    public bool IsActive { get; init; }
}

/// <summary>RFC 7591 client metadata, as a client sends it. Only the fields this server acts on.</summary>
public class ClientRegistrationRequestDto
{
    [JsonPropertyName("redirect_uris")]
    public List<string>? RedirectUris { get; init; }

    [JsonPropertyName("client_name")]
    public string? ClientName { get; init; }

    [JsonPropertyName("token_endpoint_auth_method")]
    public string? TokenEndpointAuthMethod { get; init; }

    [JsonPropertyName("grant_types")]
    public List<string>? GrantTypes { get; init; }

    [JsonPropertyName("response_types")]
    public List<string>? ResponseTypes { get; init; }
}

public class ClientRegistrationResponseDto
{
    [JsonPropertyName("client_id")]
    public string ClientId { get; init; } = string.Empty;

    [JsonPropertyName("client_id_issued_at")]
    public long ClientIdIssuedAt { get; init; }

    [JsonPropertyName("client_name")]
    public string ClientName { get; init; } = string.Empty;

    [JsonPropertyName("redirect_uris")]
    public IReadOnlyList<string> RedirectUris { get; init; } = [];

    [JsonPropertyName("grant_types")]
    public IReadOnlyList<string> GrantTypes { get; init; } = [];

    [JsonPropertyName("response_types")]
    public IReadOnlyList<string> ResponseTypes { get; init; } = [];

    [JsonPropertyName("token_endpoint_auth_method")]
    public string TokenEndpointAuthMethod { get; init; } = "none";
}

/// <summary>The form fields of a token request (RFC 6749 §4.1.3, §6).</summary>
public class TokenRequestDto
{
    public string? GrantType { get; init; }

    public string? Code { get; init; }

    public string? RedirectUri { get; init; }

    public string? ClientId { get; init; }

    public string? CodeVerifier { get; init; }

    public string? RefreshToken { get; init; }

    public string? Resource { get; init; }
}

public class TokenResponseDto
{
    [JsonPropertyName("access_token")]
    public string AccessToken { get; init; } = string.Empty;

    [JsonPropertyName("token_type")]
    public string TokenType { get; init; } = "Bearer";

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; init; }

    [JsonPropertyName("refresh_token")]
    public string RefreshToken { get; init; } = string.Empty;

    [JsonPropertyName("scope")]
    public string Scope { get; init; } = string.Empty;
}

/// <summary>A one-time upload link. POST the file to it as multipart field <c>file</c>.</summary>
public class UploadTicketDto
{
    public string UploadUrl { get; init; } = string.Empty;

    public string Method { get; init; } = "POST";

    /// <summary>The multipart field the file goes in.</summary>
    public string Field { get; init; } = "file";

    public DateTimeOffset ExpiresAt { get; init; }

    public long MaxImageBytes { get; init; }

    public long MaxVideoBytes { get; init; }
}

public class CreateUploadTicketDto
{
    public Backend.Models.Entities.MediaRole Role { get; init; } = Backend.Models.Entities.MediaRole.Attachment;

    [System.ComponentModel.DataAnnotations.StringLength(200)]
    public string? Caption { get; init; }
}
