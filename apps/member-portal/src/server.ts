import {
  AngularNodeAppEngine,
  createNodeRequestHandler,
  isMainModule,
  writeResponseToNodeResponse,
} from '@angular/ssr/node';
import express from 'express';
import { createProxyMiddleware } from 'http-proxy-middleware';
import { join } from 'node:path';

const browserDistFolder = join(import.meta.dirname, '../browser');

const app = express();
const angularApp = new AngularNodeAppEngine();

/**
 * Sends `/v1` to the gateway, so the app and its API share an origin.
 *
 * The portal's production `ApiConfig` is `gatewayUrl: ''` - every request is
 * relative - so whatever serves the app has to serve the API too. In a
 * container that is this; `apps/admin-portal/nginx.conf` does the same job for
 * the admin panel.
 *
 * **Mounted only when `GATEWAY_URL` is set**, and that is what keeps
 * development unaffected. Under `ng serve` the dev server's own
 * `proxy.conf.json` already forwards `/v1` before a request reaches this
 * handler, so mounting a second proxy there would be one too many. The variable
 * is set in docker-compose.yml and nowhere else.
 */
const gatewayUrl = process.env['GATEWAY_URL'];

if (gatewayUrl) {
  app.use(
    createProxyMiddleware({
      // `pathFilter`, not `app.use('/v1', ...)`. Mounting on a path strips it
      // before forwarding, so /v1/identity/me would reach the gateway as
      // /identity/me and 404. Filtering keeps the whole path.
      pathFilter: '/v1',
      target: gatewayUrl,

      // The gateway is addressed by its service name on the compose network,
      // and it reads the tenant off the token rather than the Host header, so
      // nothing here needs the original host preserved.
      changeOrigin: true,
    }),
  );
}

/**
 * Example Express Rest API endpoints can be defined here.
 * Uncomment and define endpoints as necessary.
 *
 * Example:
 * ```ts
 * app.get('/api/{*splat}', (req, res) => {
 *   // Handle API request
 * });
 * ```
 */

/**
 * Serve static files from /browser
 */
app.use(
  express.static(browserDistFolder, {
    maxAge: '1y',
    index: false,
    redirect: false,
  }),
);

/**
 * Handle all other requests by rendering the Angular application.
 */
app.use((req, res, next) => {
  angularApp
    .handle(req)
    .then((response) =>
      response ? writeResponseToNodeResponse(response, res) : next(),
    )
    .catch(next);
});

/**
 * Start the server if this module is the main entry point, or it is ran via PM2.
 * The server listens on the port defined by the `PORT` environment variable, or defaults to 4000.
 */
if (isMainModule(import.meta.url) || process.env['pm_id']) {
  const port = process.env['PORT'] || 4000;
  app.listen(port, (error) => {
    if (error) {
      throw error;
    }

    console.log(`Node Express server listening on http://localhost:${port}`);
  });
}

/**
 * Request handler used by the Angular CLI (for dev-server and during build) or Firebase Cloud Functions.
 */
export const reqHandler = createNodeRequestHandler(app);
