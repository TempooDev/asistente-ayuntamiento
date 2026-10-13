namespace AsistenteAyuntamiento.ApiService.Security;

/// <summary>
/// Role values issued by Auth0 in the <c>{CustomClaimsNamespace}/roles</c> claim.
/// Must stay in sync with <c>core/auth/roles.ts</c> in the Angular SPA.
/// </summary>
public static class Roles
{
    /// <summary>Manages the shared knowledge base: ingestion, scraper, cross-tenant metrics and Arena analytics.</summary>
    public const string PlatformAdmin = "admin-plataforma";

    /// <summary>Manages one organization: its AI model settings, its usage metrics and the Arena comparison.</summary>
    public const string OrganizationAdmin = "admin-organizacion";
}

/// <summary>
/// Named authorization policies. Apply them at route-group level so new endpoints inherit them.
/// </summary>
public static class Policies
{
    public const string PlatformAdmin = "PlatformAdmin";
    public const string OrganizationAdmin = "OrganizationAdmin";

    /// <summary>Roles accepted by each policy.</summary>
    public static readonly IReadOnlyList<string> PlatformAdminRoles = [Roles.PlatformAdmin];
    public static readonly IReadOnlyList<string> OrganizationAdminRoles = [Roles.OrganizationAdmin];
}

public static class AuthorizationExtensions
{
    public static IServiceCollection AddAppAuthorization(this IServiceCollection services)
    {
        services.AddAuthorizationBuilder()
            .AddPolicy(Policies.PlatformAdmin, policy => policy
                .RequireAuthenticatedUser()
                .RequireAssertion(context => HasAnyRole(context.User, Policies.PlatformAdminRoles)))
            .AddPolicy(Policies.OrganizationAdmin, policy => policy
                .RequireAuthenticatedUser()
                .RequireAssertion(context => HasAnyRole(context.User, Policies.OrganizationAdminRoles)));

        return services;
    }

    public static bool IsPlatformAdmin(System.Security.Claims.ClaimsPrincipal user) => HasAnyRole(user, Policies.PlatformAdminRoles);

    /// <summary>
    /// Case-insensitive role check, matching the SPA guard (which lowercases roles), so the client
    /// and the server never disagree on who is an administrator.
    /// </summary>
    public static bool HasRole(System.Security.Claims.ClaimsPrincipal user, string role) =>
        user.Identities.Any(identity => identity.FindAll(identity.RoleClaimType)
            .Any(claim => string.Equals(claim.Value, role, StringComparison.OrdinalIgnoreCase)));

    public static bool HasAnyRole(System.Security.Claims.ClaimsPrincipal user, IEnumerable<string> roles) =>
        roles.Any(role => HasRole(user, role));
}
