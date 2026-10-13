export const environment = {
  production: false,
  auth0: {
    // domain, clientId and audience are loaded at runtime from /api/config/auth0 (AUTH_RUNTIME_CONFIG)
    customClaimsNamespace: 'https://asistente.antoniobermudez.dev'
  },
  apiBaseUrl: '',
  // Minutes without interaction before the session is closed (InactivityService).
  sessionTimeoutMinutes: 30,
  enableArenaModeByDefault: true
};
