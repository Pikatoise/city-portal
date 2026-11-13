import {
  AngularNodeAppEngine,
  createNodeRequestHandler,
  isMainModule,
  writeResponseToNodeResponse,
} from '@angular/ssr/node';
import express from 'express';
import { join } from 'node:path';

const app = express();
const allowedHosts = ['localhost', '127.0.0.1'];
const siteDomain = process.env['SITE_DOMAIN'];
if (siteDomain) {
  const siteUrl = new URL(`https://${siteDomain}`);
  if (
    siteUrl.hostname.includes('*') ||
    siteUrl.port ||
    siteUrl.username ||
    siteUrl.password ||
    siteUrl.pathname !== '/' ||
    siteUrl.search ||
    siteUrl.hash
  )
    throw new Error(
      'SITE_DOMAIN must contain one hostname without a scheme, port, path or wildcard.',
    );
  allowedHosts.push(siteUrl.hostname);
}
const angularApp = new AngularNodeAppEngine({
  allowedHosts,
  // Enable only behind a proxy that replaces these headers; Compose does not publish this service.
  trustProxyHeaders:
    process.env['TRUST_PROXY_HEADERS'] === 'true'
      ? ['x-forwarded-for', 'x-forwarded-host', 'x-forwarded-proto']
      : false,
});
app.disable('x-powered-by');
app.use(['/account', '/staff', '/setup'], (_request, response, next) => {
  response.setHeader('Cache-Control', 'no-store');
  response.setHeader('Referrer-Policy', 'no-referrer');
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
  if (request.path === '/setup') {
    next();
    return;
  }
  // Check fixed internal API state before serving any application page, including CSR shells.
  // Assets were handled above so the wizard can load while the application stays closed.
  const statusUrl = new URL(
    '/api/installation/status',
    process.env['API_INTERNAL_ORIGIN'] ?? 'http://localhost:5080',
  );
  fetch(statusUrl, { signal: AbortSignal.timeout(3000) })
    .then(async (status) => {
      if (!status.ok) throw new Error('Installation status unavailable.');
      if ((await status.json()).installed !== true) {
        response.setHeader('Cache-Control', 'no-store');
        response.redirect(302, '/setup');
        return;
      }
      next();
    })
    .catch(() => {
      response.setHeader('Cache-Control', 'no-store');
      response
        .status(503)
        .send('Не удалось проверить состояние установки. Повторите запрос позже.');
    });
});
app.use((request, response, next) => {
  angularApp
    .handle(request)
    .then((result) => (result ? writeResponseToNodeResponse(result, response) : next()))
    .catch(next);
});

if (isMainModule(import.meta.url) || process.env['pm_id']) {
  const port = Number(process.env['PORT'] || 4000);
  app.listen(port, process.env['HOST'] ?? '127.0.0.1', () => {
    console.log(`CityPortal frontend listening on port ${port}`);
  });
}

export const reqHandler = createNodeRequestHandler(app);
