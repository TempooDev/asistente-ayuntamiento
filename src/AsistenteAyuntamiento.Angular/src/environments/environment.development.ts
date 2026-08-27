export const environment = {
  production: false,
  auth0: {
    domain: process.env.NG_APP_AUTH0_DOMAIN || '',
    clientId: process.env.NG_APP_AUTH0_CLIENT_ID || '',
    audience: process.env.NG_APP_AUTH0_AUDIENCE || ''
  },
  apiBaseUrl: process.env.NG_APP_API_BASE_URL || ''
};
