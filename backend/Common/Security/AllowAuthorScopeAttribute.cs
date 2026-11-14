namespace Backend.Common.Security;

/// <summary>
/// Marks a writing endpoint that an <see cref="Backend.Models.Entities.ApiTokenScope.Author"/>
/// token may call. Everything else refuses that scope anything but a read, so a route added
/// later is closed to it until someone decides otherwise. A DELETE is refused regardless.
/// Enforced by <see cref="ApiTokenRestrictionMiddleware"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class AllowAuthorScopeAttribute : Attribute
{
}
