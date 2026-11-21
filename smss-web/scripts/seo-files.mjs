// Writes public/robots.txt and public/sitemap.xml. Runs before `npm run build`.
//  - The site URL comes from src/environments/environment.ts (the production build's environment): the domain is set in ONE place.
//  - robots.txt disallows every signed-in route, derived from ROUTE_ROLES (src/app/core/config/route-roles.ts), so a new
//    authenticated screen is covered automatically. The public paths below must match src/app/app.routes.server.ts.
import { readFileSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = join(dirname(fileURLToPath(import.meta.url)), '..');
const read = (p) => readFileSync(join(root, p), 'utf8');

// The developer name must be set (core/config/site.config.ts DEVELOPER_NAME) before any build: it is shown in the footer and the Privacy Policy that Google Play reviews.
const dev = /export const DEVELOPER_NAME\s*=\s*'([^']*)'/.exec(read('src/app/core/config/site.config.ts'));
if (!dev || dev[1].trim() === '' || dev[1].includes('<<') || dev[1].includes('PUT EXACT')) {
  console.error(
    [
      '',
      'BUILD STOPPED: the developer name is not set.',
      "Set DEVELOPER_NAME in src/app/core/config/site.config.ts to the name exactly as it appears on the Google Play Console.",
      'That one constant feeds every place below (nothing else needs editing):',
      '  - src/app/features/public/layout/public-layout.component.ts   footer: "(c) <name>. All rights reserved."',
      '  - src/app/features/public/pages/privacy-policy.component.ts   "Who we are" and "Contact" sections',
      '  - src/app/features/public/public.routes.ts                    Organization structured data (legalName)',
      '  - src/app/core/config/site.config.ts                          SITE_CONFIG.legalName',
      '',
    ].join('\n'),
  );
  process.exit(1);
}

const match = /siteUrl:\s*'([^']+)'/.exec(read('src/environments/environment.ts'));
if (!match) throw new Error('siteUrl not found in src/environments/environment.ts');
const site = match[1].replace(/\/+$/, '');

const PUBLIC_PATHS = ['/', '/contact', '/request-demo', '/privacy-policy', '/terms'];
// Authenticated root segments that are not (or not only) keys of ROUTE_ROLES
const EXTRA_PRIVATE = ['/login'];

// Keys of the ROUTE_ROLES object: lines like   '/school-setup/classes': [ROLES.SCHOOL_ADMIN],
const routeRoles = /export const ROUTE_ROLES[^=]*=\s*\{([\s\S]*?)\n\};/.exec(read('src/app/core/config/route-roles.ts'));
if (!routeRoles) throw new Error('ROUTE_ROLES not found in src/app/core/config/route-roles.ts');
const segments = new Set();
for (const m of routeRoles[1].matchAll(/^\s*'(\/[^']*)'\s*:/gm)) {
  const first = m[1].split('/')[1];
  if (first) segments.add('/' + first);
}
for (const p of EXTRA_PRIVATE) segments.add(p);
const overlap = PUBLIC_PATHS.filter((p) => p !== '/' && segments.has(p));
if (overlap.length) throw new Error(`Public paths also listed as authenticated routes: ${overlap.join(', ')}`);
const disallowed = [...segments].sort();

const today = new Date().toISOString().slice(0, 10);
const sitemap =
  '<?xml version="1.0" encoding="UTF-8"?>\n' +
  '<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">\n' +
  PUBLIC_PATHS.map((p) => `  <url>\n    <loc>${site}${p}</loc>\n    <lastmod>${today}</lastmod>\n  </url>\n`).join('') +
  '</urlset>\n';

const robots =
  'User-agent: *\n' +
  'Allow: /\n' +
  disallowed.map((p) => `Disallow: ${p}\n`).join('') +
  `\nSitemap: ${site}/sitemap.xml\n`;

writeFileSync(join(root, 'public/sitemap.xml'), sitemap);
writeFileSync(join(root, 'public/robots.txt'), robots);
console.log(`SEO files written for ${site} (${disallowed.length} private paths disallowed)`);
