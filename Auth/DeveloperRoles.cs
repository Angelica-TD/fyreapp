using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;

namespace FyreApp.Auth;

// A Developer can do everything an Admin can, plus the Developer-only pages (Uptick migration, the catalogue,
// Data Reset). Rather than every [Authorize(Roles = "Admin")] and IsInRole("Admin") also naming
// Developer, a Developer's sign-in carries the Admin role too, so any Admin check (including new ones) lets them in.
// DevAuthHandler signs in with both for the same reason.
public sealed class DeveloperRoles : IClaimsTransformation
{
    public const string Admin = "Admin";
    public const string Developer = "Developer";

    public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        // Runs on every authenticated request, possibly more than once: add the role only if it's missing
        if (principal.Identity is ClaimsIdentity { IsAuthenticated: true } identity
            && principal.IsInRole(Developer) && !principal.IsInRole(Admin))
        {
            identity.AddClaim(new Claim(identity.RoleClaimType, Admin));
        }

        return Task.FromResult(principal);
    }
}
