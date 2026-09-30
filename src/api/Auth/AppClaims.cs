using System.Security.Claims;
using Expenses.Api.Domain;

namespace Expenses.Api.Auth;

public static class AppClaims
{
    public const string HouseholdId = "household_id";
    public const string Role = "role";
    public const string ParentPolicy = "Parent";

    public static Guid GetMemberId(this ClaimsPrincipal user) =>
        Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);

    public static Guid GetHouseholdId(this ClaimsPrincipal user) =>
        Guid.Parse(user.FindFirstValue(HouseholdId)!);

    public static MemberRole GetRole(this ClaimsPrincipal user) =>
        Enum.Parse<MemberRole>(user.FindFirstValue(Role)!);
}
