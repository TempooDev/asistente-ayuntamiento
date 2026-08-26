import { Routes } from '@angular/router';
import { ChatPanelComponent } from './pages/chat-panel/chat-panel';
import { authGuardFn } from '@auth0/auth0-angular';

export const routes: Routes = [
    { path: 'chat', component: ChatPanelComponent, canActivate: [authGuardFn] },
    { path: '', redirectTo: '/chat', pathMatch: 'full' }
];
