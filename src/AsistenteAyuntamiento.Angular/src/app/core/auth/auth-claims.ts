import { environment } from '../../../environments/environment';

/** Claims that Auth0 Actions add under the custom namespace. */
export const CLAIMS = {
  roles: `${environment.auth0.customClaimsNamespace}/roles`,
  organizations: `${environment.auth0.customClaimsNamespace}/orgs`,
} as const;

type Claims = Record<string, unknown> | null | undefined;

/** Roles of the user, lower-cased (the claim may be a string or an array). */
export function rolesFromClaims(claims: Claims): string[] {
  const value = claims?.[CLAIMS.roles];
  const roles = Array.isArray(value) ? value : value ? [value] : [];
  return roles.filter((r): r is string => typeof r === 'string').map(r => r.toLowerCase());
}

/** Ids of the organizations the user belongs to (the claim holds ids or { id } objects). */
export function organizationsFromClaims(claims: Claims): string[] {
  const value = claims?.[CLAIMS.organizations];
  if (!Array.isArray(value)) return [];
  return value
    .map(org => (typeof org === 'string' ? org : (org as { id?: unknown })?.id))
    .filter((id): id is string => typeof id === 'string' && id.length > 0);
}
