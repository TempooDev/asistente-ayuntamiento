const fs = require('fs');
const path = require('path');

const dir = './src/environments';
if (!fs.existsSync(dir)) {
  fs.mkdirSync(dir, { recursive: true });
}

const domain = process.env.Auth0__Domain || 'TU_DOMINIO.auth0.com';
const clientId = process.env.Auth0__ClientId || 'TU_CLIENT_ID';
const audience = process.env.Auth0__Audience || 'TU_AUDIENCE';

const envConfigFile = `export const environment = {
  production: false,
  auth0: {
    domain: '${domain}',
    clientId: '${clientId}',
    audience: '${audience}'
  },
  apiBaseUrl: ''
};`;

fs.writeFileSync(path.join(dir, 'environment.ts'), envConfigFile);
fs.writeFileSync(path.join(dir, 'environment.development.ts'), envConfigFile);

console.log('[Aspire Integration] Environment configuration generated successfully.');
