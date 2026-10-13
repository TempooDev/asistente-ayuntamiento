/**
 * User-facing text for a failed chat stream. The hub sends typed codes ("Error [NOT_FOUND]: …") and model
 * failures arrive as "[LLM_QUOTA_EXCEEDED] …"; anything else is treated as a connection problem.
 */
export const STREAM_ERROR_MESSAGES: Record<string, string> = {
  LLM_QUOTA_EXCEEDED: 'Hemos alcanzado el límite de uso del modelo de IA. Inténtalo de nuevo más tarde.',
  LLM_NOT_CONFIGURED: 'El modelo de IA no está configurado. Contacta con el administrador.',
  LLM_UNAVAILABLE: 'El modelo de IA no está disponible en este momento. Inténtalo de nuevo más tarde.',
  UNAUTHORIZED: 'Tu sesión ha caducado. Vuelve a iniciar sesión.',
  NOT_FOUND: 'La conversación no existe o ya no tienes acceso a ella.',
  INVALID_REQUEST: 'No se ha podido enviar el mensaje. Revisa el texto e inténtalo de nuevo.',
  INTERNAL: 'Se ha producido un error interno. Inténtalo de nuevo más tarde.',
};

export const CONNECTION_LOST_MESSAGE = 'Se ha perdido la conexión con el servidor. Inténtalo de nuevo.';

export function streamErrorCode(error: unknown): string | null {
  const text = error instanceof Error ? error.message : String(error ?? '');
  const match = /\[([A-Z_]+)\]/.exec(text);
  return match && match[1] in STREAM_ERROR_MESSAGES ? match[1] : null;
}

/** Markdown shown in place of (or after) the partial answer. */
export function mapStreamError(error: unknown): string {
  const code = streamErrorCode(error);
  const message = code ? STREAM_ERROR_MESSAGES[code] : CONNECTION_LOST_MESSAGE;
  return `\n\n**⚠️ ${message}**`;
}
