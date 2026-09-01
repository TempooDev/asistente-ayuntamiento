# Proceso de Autenticación B2B con Auth0

Este documento detalla la arquitectura de autenticación implementada en **Asistente Ayuntamiento Web**, diseñada para un modelo SaaS B2B multi-tenant utilizando **Auth0 Organizations**.

---

## 1. Arquitectura Base (B2B Multi-tenant)

La aplicación utiliza el modelo de **Auth0 Organizations**. Esto significa que:
- Existe un único Tenant global de Auth0.
- Cada cliente (Ayuntamiento) tiene su propia "Organización" dentro de Auth0.
- Cada Organización aísla de forma segura a sus usuarios, configuraciones y conexiones (por ejemplo, algunos ayuntamientos pueden usar Usuario/Contraseña, otros GitHub, y otros Google Workspace).

El Backend (`AsistenteAyuntamiento.ApiService`) lee el `org_id` (ID de la Organización) directamente del token JWT que envía Angular. Con este ID, el backend sabe exactamente a qué base de datos o esquema aislar las consultas, garantizando la seguridad de los datos.

---

## 2. Flujo de Login Inteligente (Sin "Organization Prompt")

Para mejorar la experiencia de usuario y evitar que tengan que escribir el nombre de su ayuntamiento cada vez que inician sesión, se ha implementado un flujo de enrutamiento automático ("Smart Routing"):

### El "Enlace Mágico" (Primera vez)
1. El usuario recibe un correo de invitación a su Ayuntamiento.
2. Al hacer clic, llega a la ruta `/login?invitation=...&organization=org_...` de la aplicación Angular.
3. El `LoginComponent` captura estos parámetros, guarda el `org_id` en el `localStorage` del navegador (bajo la clave `last_org_id`), e inicia el flujo de Auth0.
4. Auth0 permite el registro/login y devuelve al usuario a la web con un token válido que incluye el `org_id`.

### Inicios de sesión posteriores (Acceso directo)
1. El usuario entra directamente a `asistente.antoniobermudez.dev`.
2. El guardián de Angular (`CustomAuthGuard`) detecta que el usuario no está autenticado.
3. El guardián lee el `last_org_id` guardado en el `localStorage` en su primera visita.
4. Angular redirige automáticamente a Auth0 exigiéndole que inicie sesión **exclusivamente en esa organización**.
5. Si el usuario ya tenía sesión iniciada en Auth0 (SSO), entra directamente sin ver ninguna pantalla. Si no, solo ve el botón de su organización.

### Pantalla de Error (Fallback)
Si un usuario entra desde un ordenador nuevo o borra sus cookies, el `localStorage` estará vacío. 
En este caso, Angular lo envía a Auth0 sin organización. Como hemos configurado Auth0 para que lo bloquee si no sabemos quién es, la web atrapará el error y lo enviará a la ruta `/error` con un mensaje claro: *"No perteneces a ninguna organización o has cambiado de equipo. Usa tu enlace de invitación para entrar por primera vez"*.

---

## 3. Flujo de Registro e Invitaciones

El registro público está **completamente cerrado** por seguridad. Nadie que no haya sido invitado puede crearse una cuenta.

### ¿Cómo se invitan a usuarios nuevos?
1. **(Actualmente)** Se invitan mediante la API o mediante el panel de Auth0 (limitado a conexiones de base de datos).
2. **(Futuro)** El panel de Administración del Ayuntamiento en Angular tendrá un formulario para añadir el correo del nuevo empleado. Esto llamará al backend en C#.
3. El backend usará la **Auth0 Management API** (`POST /api/v2/organizations/{id}/invitations`) para generar la invitación sin forzar ninguna conexión.
4. El empleado recibe el correo, hace clic, llega a Auth0 y ve todos los botones habilitados para su Ayuntamiento (GitHub, Email, etc.).
5. Si usa GitHub, se enlaza automáticamente. Si usa Email, se le pedirá que cree una contraseña.

---

## 4. Configuraciones Críticas en el Panel de Auth0

Para que toda esta arquitectura funcione, es estrictamente necesario mantener esta configuración en el Dashboard de Auth0:

| Sección | Ajuste | Valor Necesario | Motivo |
|---------|--------|-----------------|---------|
| **Applications > [Tu App] > Application URIs** | `Application Login URI` | `https://asistente.../login` | Necesario para que Auth0 sepa a qué URL enviar a la gente al pulsar los botones de los correos de invitación. |
| **Applications > [Tu App] > Organizations** | `Types of Users` | **Allow Organization Membership** (o "Both") | Impide que Auth0 lance un error fatal si un desarrollador prueba el login manualmente sin el parámetro `org_id`. |
| **Authentication > Database > Settings** | `Disable Sign Ups` | **Activado** | Evita que cualquier persona de internet se cree una cuenta pública en el sistema. |
| **Organizations > [Tu Org] > Connections** | Múltiples Conexiones | Ej: `GitHub`, `Database`... | En cada ayuntamiento se deben añadir explícitamente qué métodos de login se permiten. |
| **Organizations > [Tu Org] > Connections > Database** | `Enable Sign-up` | **Activado** | (Si se usa contraseña). Permite que los usuarios invitados sí puedan establecer su contraseña la primera vez, saltándose el bloqueo global. |
| **Branding > Universal Login** | Experiencia de Login | **New** | Las invitaciones a organizaciones solo son compatibles con la nueva versión del Universal Login. |
| **Branding > Universal Login > Advanced > Login** | `Customize Login Page` | **Apagado** | Si se enciende el HTML personalizado, Auth0 fuerza el "Classic Login" por detrás, rompiendo las invitaciones. |
