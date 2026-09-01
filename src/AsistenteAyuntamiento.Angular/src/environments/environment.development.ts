export const environment = {
  production: false,
  auth0: {
    domain: (window as any).__env?.auth0Domain || '',
    clientId: (window as any).__env?.auth0ClientId || '',
    audience: (window as any).__env?.auth0Audience || ''
  },
  apiBaseUrl: ''
};
