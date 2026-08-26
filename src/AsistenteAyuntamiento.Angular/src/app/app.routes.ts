import { Routes } from '@angular/router';
import { ChatPanelComponent } from './pages/chat-panel/chat-panel';
import { DocumentosComponent } from './pages/documentos/documentos';
import { ConfiguracionComponent } from './pages/configuracion/configuracion';
import { authGuardFn } from '@auth0/auth0-angular';

export const routes: Routes = [
    { path: 'chat', component: ChatPanelComponent, canActivate: [authGuardFn] },
    { path: 'documentos', component: DocumentosComponent, canActivate: [authGuardFn] },
    { path: 'configuracion', component: ConfiguracionComponent, canActivate: [authGuardFn] },
    { path: '', redirectTo: '/chat', pathMatch: 'full' }
];
