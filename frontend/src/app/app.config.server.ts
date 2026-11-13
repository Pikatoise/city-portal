import { mergeApplicationConfig, ApplicationConfig } from '@angular/core';
import { provideServerRendering, withRoutes } from '@angular/ssr';
import { appConfig } from './app.config';
import { serverRoutes } from './app.routes.server';
import { provideApiConfiguration } from './api/api-configuration';
import { env } from 'node:process';

const serverConfig: ApplicationConfig = {
  providers: [
    provideServerRendering(withRoutes(serverRoutes)),
    provideApiConfiguration(env['API_INTERNAL_ORIGIN'] ?? 'http://localhost:5080'),
  ],
};

export const config = mergeApplicationConfig(appConfig, serverConfig);
