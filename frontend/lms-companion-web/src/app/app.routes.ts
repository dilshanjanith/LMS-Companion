import { Routes } from '@angular/router';
import { authGuard } from './core/auth.guard';

export const routes: Routes = [
  {
    path: 'auth/callback',
    loadComponent: () => import('./pages/auth-callback.page').then(module => module.AuthCallbackPage)
  },
  {
    path: 'dashboard',
    canActivate: [authGuard],
    loadComponent: () => import('./pages/dashboard.page').then(module => module.DashboardPage)
  },
  {
    path: '',
    loadComponent: () => import('./pages/sign-in.page').then(module => module.SignInPage)
  },
  { path: '**', redirectTo: '' }
];
