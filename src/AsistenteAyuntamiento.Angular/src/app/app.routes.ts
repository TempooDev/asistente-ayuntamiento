import { Routes } from '@angular/router';
import { authGuard } from './core/auth/auth.guard';
import { roleGuard } from './core/auth/role.guard';
import { ANY_ADMIN_ROLES, ORGANIZATION_ADMIN_ROLES, PLATFORM_ADMIN_ROLES } from './core/auth/roles';

const requireRoles = (roles: readonly string[]) => ({ canActivate: [authGuard, roleGuard], data: { roles } });
const platformAdmin = requireRoles(PLATFORM_ADMIN_ROLES);
const organizationAdmin = requireRoles(ORGANIZATION_ADMIN_ROLES);
/** Pages with sections for each admin scope; each section checks its own role. */
const anyAdmin = requireRoles(ANY_ADMIN_ROLES);

export const routes: Routes = [
  { path: 'login', loadComponent: () => import('./features/auth-pages/login/login.component').then(m => m.LoginComponent) },
  { path: 'callback', loadComponent: () => import('./features/auth-pages/callback/callback.component').then(m => m.CallbackComponent) },
  { path: 'error', loadComponent: () => import('./features/auth-pages/error/error.component').then(m => m.ErrorComponent) },
  {
    path: 'chat',
    canActivate: [authGuard],
    loadComponent: () => import('./features/chat/chat-page/chat-page.component').then(m => m.ChatPageComponent),
  },
  {
    path: 'documentos',
    ...platformAdmin,
    loadComponent: () => import('./features/documents/documents-page/documents-page.component').then(m => m.DocumentsPageComponent),
  },
  {
    path: 'configuracion',
    ...anyAdmin,
    loadComponent: () => import('./features/settings/settings-page/settings-page.component').then(m => m.SettingsPageComponent),
  },
  {
    path: 'arena',
    ...organizationAdmin,
    loadComponent: () => import('./features/arena/arena-page/arena-page.component').then(m => m.ArenaPageComponent),
  },
  {
    path: 'admin/analytics',
    ...anyAdmin,
    loadComponent: () => import('./features/analytics/analytics-dashboard/analytics-dashboard.component').then(m => m.AnalyticsDashboardComponent),
  },
  {
    path: 'perfil',
    canActivate: [authGuard],
    loadComponent: () => import('./features/profile/profile-page/profile-page.component').then(m => m.ProfilePageComponent),
  },
  { path: '', redirectTo: '/chat', pathMatch: 'full' },
  { path: '**', redirectTo: '/chat' },
];
