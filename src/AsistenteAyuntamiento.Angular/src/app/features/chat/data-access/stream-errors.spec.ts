import { CONNECTION_LOST_MESSAGE, STREAM_ERROR_MESSAGES, mapStreamError, streamErrorCode } from './stream-errors';

describe('mapStreamError', () => {
  it('reads hub error codes from SignalR errors', () => {
    const error = new Error("An unexpected error occurred invoking 'StreamMessage' on the server. HubException: Error [NOT_FOUND]: la conversación no existe o no tienes acceso.");

    expect(streamErrorCode(error)).toBe('NOT_FOUND');
    expect(mapStreamError(error)).toContain(STREAM_ERROR_MESSAGES['NOT_FOUND']);
  });

  it('reads model error codes', () => {
    expect(mapStreamError('[LLM_QUOTA_EXCEEDED] Hemos alcanzado el límite')).toContain(STREAM_ERROR_MESSAGES['LLM_QUOTA_EXCEEDED']);
  });

  it('treats unknown errors as a lost connection without echoing them', () => {
    const text = mapStreamError(new Error('WebSocket closed with status code: 1006 (secret detail)'));

    expect(text).toContain(CONNECTION_LOST_MESSAGE);
    expect(text).not.toContain('secret detail');
  });
});
