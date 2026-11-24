import { Routes } from '@angular/router';
import { requireInstallation } from './installation-state';

export const routes: Routes = [
  { path: 'setup', loadComponent: () => import('./setup').then((m) => m.Setup) },
  {
    path: '',
    canActivateChild: [requireInstallation],
    children: [
      {
        path: 'news',
        data: { list: true },
        loadComponent: () => import('./news/discovery').then((m) => m.Discovery),
      },
      {
        path: 'account/articles',
        loadComponent: () => import('./editorial/author-workspace').then((m) => m.AuthorWorkspace),
        canDeactivate: [(component: { canLeave(): boolean }) => component.canLeave()],
      },
      {
        path: 'staff/publications',
        loadComponent: () => import('./editorial/publications').then((m) => m.Publications),
      },
      {
        path: 'staff/articles/:id',
        data: { correction: true },
        loadComponent: () => import('./editorial/author-workspace').then((m) => m.AuthorWorkspace),
        canDeactivate: [(component: { canLeave(): boolean }) => component.canLeave()],
      },
      {
        path: 'news/:address',
        loadComponent: () => import('./news/public-article').then((m) => m.PublicArticle),
      },
      {
        path: 'news/**',
        loadComponent: () => import('./news/missing-article').then((m) => m.MissingArticle),
      },
      {
        path: 'staff/taxonomy',
        loadComponent: () => import('./editorial/taxonomy').then((m) => m.Taxonomy),
      },
      {
        path: 'staff/suggestions/moderation',
        data: { moderation: true },
        loadComponent: () => import('./staff/suggestion-queue').then((m) => m.SuggestionQueue),
      },
      {
        path: 'staff/suggestions',
        loadComponent: () => import('./staff/suggestion-queue').then((m) => m.SuggestionQueue),
      },
      {
        path: 'staff/reviews',
        loadComponent: () =>
          import('./editorial/review-workspace/review-workspace').then((m) => m.ReviewWorkspace),
      },
      {
        path: 'account/suggestions',
        loadComponent: () => import('./account/suggestions').then((m) => m.Suggestions),
      },
      {
        path: 'account/classifieds',
        loadComponent: () => import('./account/classifieds').then((m) => m.Classifieds),
        canDeactivate: [(component: { canLeave(): boolean }) => component.canLeave()],
      },
      {
        path: 'account/notifications',
        loadComponent: () => import('./account/notifications').then((m) => m.Notifications),
      },
      {
        path: 'account/media',
        loadComponent: () => import('./media/media-library').then((m) => m.MediaLibrary),
      },
      {
        path: 'staff/media',
        loadComponent: () => import('./media/media-library').then((m) => m.MediaLibrary),
      },
      {
        path: 'account/forgot-password',
        loadComponent: () => import('./account/forgot-password').then((m) => m.ForgotPassword),
      },
      {
        path: 'account/reset-password',
        loadComponent: () => import('./account/reset-password').then((m) => m.ResetPassword),
      },
      {
        path: '',
        pathMatch: 'full',
        loadComponent: () => import('./news/discovery').then((m) => m.Discovery),
      },
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
        pathMatch: 'full',
        redirectTo: 'staff/accounts',
      },
      {
        path: 'staff/accounts',
        loadComponent: () => import('./staff/accounts').then((m) => m.Accounts),
      },
      {
        path: 'staff/email-deliveries',
        loadComponent: () => import('./staff/email-deliveries').then((m) => m.EmailDeliveries),
      },
      { path: '**', redirectTo: '' },
    ],
  },
];
