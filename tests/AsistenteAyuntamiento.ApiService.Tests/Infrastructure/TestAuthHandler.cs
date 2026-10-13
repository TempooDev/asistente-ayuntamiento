using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AsistenteAyuntamiento.ApiService.Tests.Infrastructure;

/// <summary>
/// Authenticates requests from test headers instead of an Auth0 JWT:
/// <c>X-Test-User</c> (sub), <c>X-Test-Org</c> (org_id) and <c>X-Test-Roles</c> (comma-separated).
/// Requests without <c>X-Test-User</c> stay anonymous.
/// </summary>
public class TestAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Test";
    public const string UserHeader = "X-Test-User";
    public const string OrgHeader = "X-Test-Org";
    public const string RolesHeader = "X-Test-Roles";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(UserHeader, out var user) || string.IsNullOrEmpty(user))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, user!) };

        if (Request.Headers.TryGetValue(OrgHeader, out var org) && !string.IsNullOrEmpty(org))
        {
            claims.Add(new Claim("org_id", org!));
        }

        if (Request.Headers.TryGetValue(RolesHeader, out var roles))
        {
            claims.AddRange(roles.ToString()
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(role => new Claim(ApiFactory.RoleClaimType, role)));
        }

        var identity = new ClaimsIdentity(claims, SchemeName, ClaimTypes.Name, ApiFactory.RoleClaimType);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
