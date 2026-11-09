import { RenderMode, ServerRoute } from '@angular/ssr';

/**
 * Only the PUBLIC marketing pages are rendered to static HTML at build time (outputMode "static": no Node server needed in production).
 * /login and the whole signed-in app stay client-rendered (served from index.csr.html).
 * Keep the Prerender paths in step with PUBLIC_ROUTES and with public/sitemap.xml.
 */
export const serverRoutes: ServerRoute[] = [
  { path: '', renderMode: RenderMode.Prerender },
  { path: 'contact', renderMode: RenderMode.Prerender },
  { path: 'request-demo', renderMode: RenderMode.Prerender },
  { path: 'privacy-policy', renderMode: RenderMode.Prerender },
  { path: 'terms', renderMode: RenderMode.Prerender },
  { path: '**', renderMode: RenderMode.Client },
];
