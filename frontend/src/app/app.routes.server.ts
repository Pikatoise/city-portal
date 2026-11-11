import { RenderMode, ServerRoute } from '@angular/ssr';

export const serverRoutes: ServerRoute[] = [
  { path: 'account', renderMode: RenderMode.Client },
  { path: 'staff', renderMode: RenderMode.Client },
  { path: '**', renderMode: RenderMode.Server },
];
