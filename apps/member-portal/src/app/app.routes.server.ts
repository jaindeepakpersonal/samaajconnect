import { RenderMode, ServerRoute } from '@angular/ssr';

/**
 * The portal keeps its SSR setup for the public, content-heavy screens still to
 * come, but every screen built so far is personalised: Login and
 * Register are forms whose server-rendered output would be an empty shell, and
 * every signed-in screen, Timeline (the landing page) included, sits behind
 * the auth guard with a token that exists only in the browser.
 * Prerendering them at build time would also mean calling the gateway from the
 * build, which is why the generated default of `Prerender` fails here.
 */
export const serverRoutes: ServerRoute[] = [
  { path: '**', renderMode: RenderMode.Client },
];
