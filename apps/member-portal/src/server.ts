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

// Which server this is is nobody's business.
app.disable('x-powered-by');

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
 * Security headers on every page and asset this server sends - after the /v1
 * proxy, so API responses keep the gateway's own. The same policy
 * apps/admin-portal/security-headers.conf gives the admin panel: this origin
 * only, images it fetched itself (blob:) or was handed inline (data: - the
 * sign-in CAPTCHA), and 'unsafe-inline' for styles alone, because Angular
 * injects component styles as <style> tags. Scripts are held to 'self', which
 * holds while every route renders on the client (app.routes.server.ts); a
 * server-rendered route with event replay would need a CSP nonce first.
 */
const CONTENT_SECURITY_POLICY = [
  "default-src 'self'",
  "script-src 'self'",
  "style-src 'self' 'unsafe-inline'",
  "img-src 'self' data: blob:",
  "font-src 'self' data:",
  "connect-src 'self'",
  "object-src 'none'",
  "base-uri 'self'",
  "form-action 'self'",
  "frame-ancestors 'none'",
].join('; ');

app.use((_req, res, next) => {
  res.setHeader('Content-Security-Policy', CONTENT_SECURITY_POLICY);
  res.setHeader('X-Content-Type-Options', 'nosniff');
  res.setHeader('X-Frame-Options', 'DENY');
  res.setHeader('Referrer-Policy', 'strict-origin-when-cross-origin');
  res.setHeader('Permissions-Policy', 'camera=(), microphone=(), geolocation=(), payment=()');
  res.setHeader('Cross-Origin-Opener-Policy', 'same-origin');
  next();
});

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
