namespace Backend.Common.Exceptions;

/// <summary>
/// An OAuth error, which has its own wire format (RFC 6749 §5.2) rather than problem+json:
/// the clients reading it are OAuth libraries, and they look for <c>error</c>.
/// </summary>
public class OAuthException : Exception
{
    public OAuthException(string error, string description, int statusCode = StatusCodes.Status400BadRequest)
        : base(description)
    {
        Error = error;
        StatusCode = statusCode;
    }

    /// <summary>The registered error code — invalid_request, invalid_grant and so on.</summary>
    public string Error { get; }

    public int StatusCode { get; }

    /// <summary>
    /// Whether an authorization request with this problem may be answered by redirecting
    /// back to the client. False when the client or the redirect itself is what is wrong —
    /// sending the browser there would make this server an open redirector.
    /// </summary>
    public bool Redirectable { get; init; } = true;
}
