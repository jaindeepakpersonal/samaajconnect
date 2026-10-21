import {
  AngularNodeAppEngine,
  createNodeRequestHandler,
  isMainModule,
  writeResponseToNodeResponse,
} from '@angular/ssr/node';
import express from 'express';
import { createProxyMiddleware } from 'http-proxy-middleware';
import { randomBytes } from 'node:crypto';
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
 * only, and images it fetched itself (blob:) or was handed inline (data: - the
 * sign-in CAPTCHA).
 *
 * **No 'unsafe-inline' anywhere, since 2026-10-21: a nonce instead.** Angular
 * injects component styles as <style> tags; each response gets 128 fresh
 * random bits, sent in the policy and stamped on <app-root> as `ngCspNonce`,
 * which Angular reads at bootstrap and puts on every <style> it adds. A <style>
 * an attacker manages to inject carries no nonce and does not apply - the CSS
 * attribute-selector tricks that read a page out character by character need
 * exactly that. Scripts stay 'self' and carry the nonce too, so a
 * server-rendered route with event replay's inline script would already pass.
 * Inline `style="..."` attributes are not covered by a nonce and so are not
 * allowed at all: a template uses a class.
 */
function contentSecurityPolicy(nonce: string): string {
  return [
    "default-src 'self'",
    `script-src 'self' 'nonce-${nonce}'`,
    `style-src 'self' 'nonce-${nonce}'`,
    "img-src 'self' data: blob:",
    "font-src 'self' data:",
    "connect-src 'self'",
    "object-src 'none'",
    "base-uri 'self'",
    "form-action 'self'",
    "frame-ancestors 'none'",
  ].join('; ');
}

app.use((_req, res, next) => {
  const nonce = randomBytes(16).toString('base64');

  res.locals['cspNonce'] = nonce;
  res.setHeader('Content-Security-Policy', contentSecurityPolicy(nonce));
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
    .then(async (response) =>
      response
        ? writeResponseToNodeResponse(await withNonce(response, res.locals['cspNonce'] as string), res)
        : next(),
    )
    .catch(next);
});

/**
 * Stamps this response's nonce on the app's root element, where Angular looks
 * for it (`ngCspNonce`). HTML only; anything else passes through untouched.
 */
async function withNonce(response: Response, nonce: string): Promise<Response> {
  if (!response.headers.get('content-type')?.startsWith('text/html')) {
    return response;
  }

  const html = (await response.text()).replace('<app-root', `<app-root ngCspNonce="${nonce}"`);
  const headers = new Headers(response.headers);

  // The body changed length, and a page carrying a one-time nonce is never
  // one to keep: a cached copy would pair a stale nonce with a fresh policy.
  headers.delete('content-length');
  headers.set('cache-control', 'no-store');

  return new Response(html, { status: response.status, statusText: response.statusText, headers });
}

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
