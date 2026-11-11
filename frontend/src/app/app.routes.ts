import { Routes } from '@angular/router';

export const routes: Routes = [
  { path: '', pathMatch: 'full', loadComponent: () => import('./home').then((m) => m.Home) },
  {
    path: 'account',
    data: { title: 'Личный кабинет' },
    loadComponent: () => import('./upcoming').then((m) => m.Upcoming),
  },
  {
    path: 'staff',
    data: { title: 'Рабочее пространство' },
    loadComponent: () => import('./upcoming').then((m) => m.Upcoming),
  },
  { path: '**', redirectTo: '' },
];
