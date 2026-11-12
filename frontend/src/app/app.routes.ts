import { Routes } from '@angular/router';

export const routes: Routes = [
  {
    path: 'account/forgot-password',
    loadComponent: () => import('./account/forgot-password').then((m) => m.ForgotPassword),
  },
  {
    path: 'account/reset-password',
    loadComponent: () => import('./account/reset-password').then((m) => m.ResetPassword),
  },
  { path: '', pathMatch: 'full', loadComponent: () => import('./home').then((m) => m.Home) },
  {
    path: 'account/register',
    loadComponent: () => import('./account/register').then((m) => m.Register),
  },
  {
    path: 'account/login',
    loadComponent: () => import('./account/login').then((m) => m.Login),
  },
  {
    path: 'account/confirm-email',
    loadComponent: () => import('./account/confirm-email').then((m) => m.ConfirmEmail),
  },
  {
    path: 'account',
    loadComponent: () => import('./account/profile').then((m) => m.Profile),
  },
  {
    path: 'staff',
    data: { title: 'Рабочее пространство' },
    loadComponent: () => import('./upcoming').then((m) => m.Upcoming),
  },
  { path: '**', redirectTo: '' },
];
