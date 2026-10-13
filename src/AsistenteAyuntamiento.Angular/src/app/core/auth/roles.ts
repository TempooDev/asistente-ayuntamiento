/**
 * Role values issued by Auth0 in the `{customClaimsNamespace}/roles` claim.
 * Must stay in sync with `Security/AuthorizationPolicies.cs` (Roles) in the API, which enforces them.
 * Client-side guards only improve UX; authorization is decided by the server.
 */
export const ROLES = {
  /** Shared knowledge base: documents, scraper, Arena analytics. */
  PLATFORM_ADMIN: 'admin-plataforma',
  /** One organization: its AI settings, its usage metrics, the Arena comparison. */
  ORGANIZATION_ADMIN: 'admin-organizacion',
  FUNCIONARIO: 'Funcionario',
} as const;

export const PLATFORM_ADMIN_ROLES: readonly string[] = [ROLES.PLATFORM_ADMIN];
export const ORGANIZATION_ADMIN_ROLES: readonly string[] = [ROLES.ORGANIZATION_ADMIN];
export const ANY_ADMIN_ROLES: readonly string[] = [ROLES.PLATFORM_ADMIN, ROLES.ORGANIZATION_ADMIN];
