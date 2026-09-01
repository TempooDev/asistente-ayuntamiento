import { bootstrapApplication } from '@angular/platform-browser';
import { appConfig } from './app/app.config';
import { App } from './app/app';

fetch('/api/config/auth0')
  .then(res => res.json())
  .then(config => {
    (window as any).__env = (window as any).__env || {};
    (window as any).__env.auth0Domain = config.domain;
    (window as any).__env.auth0ClientId = config.clientId;
    (window as any).__env.auth0Audience = config.audience;

    bootstrapApplication(App, appConfig)
      .catch((err) => console.error(err));
  })
  .catch(err => {
    console.error('Failed to load Auth0 config:', err);
    // Podemos arrancar igual para que no se quede la pantalla en blanco,
    // o mostrar un error amigable. Arrancaremos por defecto.
    bootstrapApplication(App, appConfig)
      .catch((e) => console.error(e));
  });
