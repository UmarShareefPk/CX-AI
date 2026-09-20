using System.Security.Claims;
using Cx.Ai.Agent;
using Cx.Core.Domain;
using Cx.Core.Security;

namespace Cx.Api.Infrastructure;

public static class ClaimNames
{
    public const string Subject = "sub";
    public const string Name = "name";
    public const string Role = "role";
    public const string DealerId = "dealer_id";
}

public static class ClaimsExtensions
{
    /// <summary>
    /// The data scope comes only from the validated token. No endpoint takes a dealer id from a dealer's request:
    /// <see cref="DataScope.ResolveDealer"/> rejects it unless it equals the caller's own dealer.
    /// </summary>
    public static DataScope ToScope(this ClaimsPrincipal user) =>
        user.IsInRole(nameof(UserRole.Admin))
            ? DataScope.Admin
            : DataScope.ForDealer(user.FindFirstValue(ClaimNames.DealerId) ?? throw new ForbiddenException("Your account is not linked to a dealership."));

    public static string UserId(this ClaimsPrincipal user) =>
        user.FindFirstValue(ClaimNames.Subject) ?? throw new ForbiddenException("Invalid token.");

    public static AgentIdentity ToAgentIdentity(this ClaimsPrincipal user)
    {
        var name = user.FindFirstValue(ClaimNames.Name) ?? "User";
        var scope = user.ToScope();
        return new AgentIdentity(user.UserId(), name, scope.IsAdmin ? UserRole.Admin : UserRole.Dealer, scope.DealerId, scope.IsAdmin ? null : name);
    }
}
