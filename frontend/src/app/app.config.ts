import {
  ApplicationConfig,
  inject,
  provideAppInitializer,
  provideBrowserGlobalErrorListeners,
} from '@angular/core';
import { SiteMetadata } from './site-metadata';
import { InstallationState } from './installation-state';
import { provideRouter } from '@angular/router';
import { routes } from './app.routes';
import { provideClientHydration } from '@angular/platform-browser';
import { provideHttpClient, withInterceptors, withNoXsrfProtection } from '@angular/common/http';
import { publicNewsRequests } from './news/public-news-api';

export const appConfig: ApplicationConfig = {
  providers: [
    provideAppInitializer(() => inject(InstallationState).load()),
    provideAppInitializer(() => inject(SiteMetadata).load()),
    provideBrowserGlobalErrorListeners(),
    provideRouter(routes),
    provideClientHydration(),
    provideHttpClient(withInterceptors([publicNewsRequests]), withNoXsrfProtection()),
  ],
};
