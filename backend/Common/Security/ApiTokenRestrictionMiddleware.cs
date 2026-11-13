using Backend.Common.Exceptions;
using Backend.Models.Entities;

namespace Backend.Common.Security;

/// <summary>Middleware to prevent restricted actions to be executed.</summary>
public class ApiTokenRestrictionMiddleware
{
    private static readonly string[] SafeMethods =
        [HttpMethods.Get, HttpMethods.Head, HttpMethods.Options];

    private readonly RequestDelegate _next;

    public ApiTokenRestrictionMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public Task InvokeAsync(HttpContext context, ICurrentUser currentUser)
    {
        // Sessions are unaffected, and so is anything anonymous.
        if (!currentUser.IsApiToken)
            return _next(context);

        if (context.GetEndpoint()?.Metadata.GetMetadata<SessionOnlyAttribute>() is not null)
        {
            throw new ForbiddenException(
                "This action cannot be performed with an API token. Sign in to the studio to " +
                "change your credentials, manage your tokens, or close your account.");
        }

        if (currentUser.ApiTokenScope == ApiTokenScope.Read && !IsSafe(context.Request.Method))
        {
            throw new ForbiddenException(
                $"This API token is read-only, so it cannot {context.Request.Method} " +
                "this resource. Issue a token with the write scope instead.");
        }

        if (currentUser.ApiTokenScope == ApiTokenScope.Author && !IsSafe(context.Request.Method))
        {
            // NOTE: an allowlist, not a denylist. A new writing route stays closed to an
            // assistant until it is marked, and no mark can open a DELETE.
            if (HttpMethods.IsDelete(context.Request.Method))
            {
                throw new ForbiddenException(
                    "This API token can never delete anything. Remove it from the studio instead.");
            }

            if (context.GetEndpoint()?.Metadata.GetMetadata<AllowAuthorScopeAttribute>() is null)
            {
                throw new ForbiddenException(
                    "This API token can only write post drafts and their media. Make this change " +
                    "from the studio instead.");
            }
        }

        return _next(context);
    }

    private static bool IsSafe(string method) =>
        SafeMethods.Contains(method, StringComparer.OrdinalIgnoreCase);
}
