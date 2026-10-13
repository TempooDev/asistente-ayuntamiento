/** Codes the app passes to /error?code=…; the error page maps them to messages. */
export const AUTH_ERROR_CODES = {
  noOrganization: 'NO_ORGANIZATION',
  configUnavailable: 'AUTH_CONFIG_UNAVAILABLE',
} as const;

export type AuthErrorCode = (typeof AUTH_ERROR_CODES)[keyof typeof AUTH_ERROR_CODES];

export const AUTH_ERROR_MESSAGES: Record<AuthErrorCode, string> = {
  NO_ORGANIZATION: 'No perteneces a ninguna organización. Solicita una invitación al administrador.',
  AUTH_CONFIG_UNAVAILABLE: 'No se ha podido cargar la configuración de inicio de sesión. Inténtalo de nuevo en unos minutos.',
};
