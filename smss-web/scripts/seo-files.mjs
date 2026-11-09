// Writes public/robots.txt and public/sitemap.xml from the site URL in src/environments/environment.ts (the production build's environment),
// so the domain is set in ONE place. Runs before `npm run build`. The public paths here must match src/app/app.routes.server.ts.
import { readFileSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = join(dirname(fileURLToPath(import.meta.url)), '..');
const env = readFileSync(join(root, 'src/environments/environment.ts'), 'utf8');
const match = /siteUrl:\s*'([^']+)'/.exec(env);
if (!match) throw new Error('siteUrl not found in src/environments/environment.ts');
const site = match[1].replace(/\/+$/, '');

const PUBLIC_PATHS = ['/', '/contact', '/request-demo', '/privacy-policy', '/terms'];
const today = new Date().toISOString().slice(0, 10);

const sitemap =
  '<?xml version="1.0" encoding="UTF-8"?>\n' +
  '<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">\n' +
  PUBLIC_PATHS.map((p) => `  <url>\n    <loc>${site}${p}</loc>\n    <lastmod>${today}</lastmod>\n  </url>\n`).join('') +
  '</urlset>\n';

const robots =
  'User-agent: *\n' +
  'Allow: /\n' +
  'Disallow: /login\n' +
  'Disallow: /dashboard\n' +
  `\nSitemap: ${site}/sitemap.xml\n`;

writeFileSync(join(root, 'public/sitemap.xml'), sitemap);
writeFileSync(join(root, 'public/robots.txt'), robots);
console.log(`SEO files written for ${site}`);
