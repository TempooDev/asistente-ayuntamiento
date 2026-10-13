/**
 * Role values issued by Auth0 in the `{customClaimsNamespace}/roles` claim.
 * Must stay in sync with `Security/AuthorizationPolicies.cs` (Roles) in the API, which enforces them.
 * Client-side guards only improve UX; authorization is decided by the server.
 */
export const ROLES = {
  ADMIN: 'administrador',
  FUNCIONARIO: 'Funcionario',
} as const;
