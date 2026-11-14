namespace Backend.Common.Security;

/// <summary>
/// Marks a post or media endpoint that only a full-editing assistant
/// (<see cref="Backend.Models.Entities.ApiTokenScope.Editor"/>) may call: unpublishing and
/// deleting. An Editor token is refused every write that carries neither this nor
/// <see cref="AllowAuthorScopeAttribute"/>. Enforced by <see cref="ApiTokenRestrictionMiddleware"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class AllowEditorScopeAttribute : Attribute
{
}
