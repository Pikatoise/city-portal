// Throwaway UI study: public branding read only; no API mutations or persistent state.
import { inject, provideAppInitializer } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { bootstrapApplication } from '@angular/platform-browser';
import { provideRouter, Routes } from '@angular/router';
import { NewsPrototype } from './news-prototype';
import { SiteMetadata } from '../site-metadata';

const routes: Routes = [{ path: '**', component: NewsPrototype }];
bootstrapApplication(NewsPrototype, {
  providers: [
    provideRouter(routes),
    provideHttpClient(),
    provideAppInitializer(() => {
      const site = inject(SiteMetadata);
      site.name.set('Название сайта');
      return site.load();
    }),
  ],
}).catch(console.error);
