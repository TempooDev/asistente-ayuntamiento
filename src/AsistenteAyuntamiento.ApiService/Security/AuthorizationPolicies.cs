namespace AsistenteAyuntamiento.ApiService.Security;

/// <summary>
/// Role values issued by Auth0 in the <c>{CustomClaimsNamespace}/roles</c> claim.
/// Must stay in sync with <c>core/auth/roles.ts</c> in the Angular SPA.
/// </summary>
public static class Roles
{
    public const string Admin = "administrador";
}

/// <summary>
/// Named authorization policies. Apply them at route-group level so new endpoints inherit them.
/// </summary>
public static class Policies
{
    public const string Admin = "Admin";
}

public static class AuthorizationExtensions
{
    public static IServiceCollection AddAppAuthorization(this IServiceCollection services)
    {
        services.AddAuthorizationBuilder()
            .AddPolicy(Policies.Admin, policy => policy
                .RequireAuthenticatedUser()
                .RequireAssertion(context => HasRole(context.User, Roles.Admin)));

        return services;
    }

    /// <summary>
    /// Case-insensitive role check, matching the SPA guard (which lowercases roles), so the client
    /// and the server never disagree on who is an administrator.
    /// </summary>
    public static bool HasRole(System.Security.Claims.ClaimsPrincipal user, string role) =>
        user.Identities.Any(identity => identity.FindAll(identity.RoleClaimType)
            .Any(claim => string.Equals(claim.Value, role, StringComparison.OrdinalIgnoreCase)));
}
