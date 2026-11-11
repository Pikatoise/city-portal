import {
  AngularNodeAppEngine,
  createNodeRequestHandler,
  isMainModule,
  writeResponseToNodeResponse,
} from '@angular/ssr/node';
import express from 'express';
import { join } from 'node:path';

const app = express();
const angularApp = new AngularNodeAppEngine({
  // Enable only behind a proxy that replaces these headers; Compose does not publish this service.
  trustProxyHeaders:
    process.env['TRUST_PROXY_HEADERS'] === 'true'
      ? ['x-forwarded-for', 'x-forwarded-host', 'x-forwarded-proto']
      : false,
});
app.disable('x-powered-by');
app.use(['/account', '/staff'], (_request, response, next) => {
  response.setHeader('Cache-Control', 'no-store');
  next();
});
app.use(
  express.static(join(import.meta.dirname, '../browser'), {
    maxAge: '1y',
    index: false,
    redirect: false,
  }),
);
app.use((request, response, next) => {
  angularApp
    .handle(request)
    .then((result) => (result ? writeResponseToNodeResponse(result, response) : next()))
    .catch(next);
});

if (isMainModule(import.meta.url) || process.env['pm_id']) {
  const port = Number(process.env['PORT'] || 4000);
  app.listen(port, '0.0.0.0', () => {
    console.log(`CityPortal frontend listening on port ${port}`);
  });
}

export const reqHandler = createNodeRequestHandler(app);
