import { Routes } from '@angular/router';
import { ChatPanelComponent } from './pages/chat-panel/chat-panel';
import { DocumentosComponent } from './pages/documentos/documentos';
import { ConfiguracionComponent } from './pages/configuracion/configuracion';
import { CallbackComponent } from './pages/callback/callback';
import { PerfilComponent } from './pages/perfil/perfil';
import { ErrorComponent } from './pages/error/error';
import { LoginComponent } from './pages/login/login';
import { customAuthGuardFn } from './guards/custom-auth.guard';

export const routes: Routes = [
    { path: 'login', component: LoginComponent },
    { path: 'callback', component: CallbackComponent },
    { path: 'error', component: ErrorComponent },
    { path: 'chat', component: ChatPanelComponent, canActivate: [customAuthGuardFn] },
    { path: 'documentos', component: DocumentosComponent, canActivate: [customAuthGuardFn] },
    { path: 'configuracion', component: ConfiguracionComponent, canActivate: [customAuthGuardFn] },
    { path: 'perfil', component: PerfilComponent, canActivate: [customAuthGuardFn] },
    { path: '', redirectTo: '/chat', pathMatch: 'full' }
];
