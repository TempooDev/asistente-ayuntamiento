export const environment = {
  production: true,
  auth0: {
    domain: (window as any).__env?.auth0Domain || '',
    clientId: (window as any).__env?.auth0ClientId || '',
    audience: (window as any).__env?.auth0Audience || ''
  },
  apiBaseUrl: ''
};
