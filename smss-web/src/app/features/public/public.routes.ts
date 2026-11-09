import { Routes } from '@angular/router';
import { SITE_CONFIG } from '../../core/config/site.config';
import { publicGuard } from '../../core/guards/auth.guard';
import { SeoData } from '../../core/services/seo.service';

const abs = (path: string) => SITE_CONFIG.url + path;

const organization = {
  '@context': 'https://schema.org',
  '@type': 'Organization',
  name: SITE_CONFIG.name,
  legalName: SITE_CONFIG.legalName,
  url: abs('/'),
  logo: abs('/favicon.ico'),
  email: SITE_CONFIG.email,
  telephone: SITE_CONFIG.phone,
  contactPoint: [{ '@type': 'ContactPoint', contactType: 'sales', email: SITE_CONFIG.email, telephone: SITE_CONFIG.phone, areaServed: 'IN', availableLanguage: ['en'] }],
  ...(SITE_CONFIG.social.length ? { sameAs: SITE_CONFIG.social.map((s) => s.url) } : {}),
};

const softwareApplication = {
  '@context': 'https://schema.org',
  '@type': 'SoftwareApplication',
  name: SITE_CONFIG.name,
  description: SITE_CONFIG.description,
  url: abs('/'),
  applicationCategory: 'EducationalApplication',
  operatingSystem: 'Web',
  publisher: { '@type': 'Organization', name: SITE_CONFIG.name, url: abs('/') },
};

const seo = (data: SeoData): { seo: SeoData } => ({ seo: { index: true, ...data } });

/**
 * The public marketing site, inside the public layout. Mounted at the root by app.routes.ts, BEFORE the signed-in app.
 * Keep the paths in step with app.routes.server.ts (prerender list) and public/sitemap.xml.
 */
export const PUBLIC_ROUTES: Routes = [
  {
    path: '',
    pathMatch: 'full',
    // A signed-in user opening "/" goes straight to their dashboard
    canActivate: [publicGuard],
    loadComponent: () => import('./pages/landing/landing.component').then((m) => m.LandingComponent),
    data: seo({
      title: `${SITE_CONFIG.name} | School Management System for Admissions, Attendance, Exams and Fees`,
      description: SITE_CONFIG.description,
      path: '/',
      jsonLd: [organization, softwareApplication],
    }),
  },
  {
    path: 'contact',
    loadComponent: () => import('./pages/form-page.component').then((m) => m.FormPageComponent),
    data: {
      type: 'CONTACT',
      ...seo({ title: `Contact Us | ${SITE_CONFIG.name}`, description: `Questions about ${SITE_CONFIG.name}? Send us a message and we will reply by email.`, path: '/contact' }),
    },
  },
  {
    path: 'request-demo',
    loadComponent: () => import('./pages/form-page.component').then((m) => m.FormPageComponent),
    data: {
      type: 'DEMO',
      ...seo({ title: `Request a Demo | ${SITE_CONFIG.name}`, description: `Book a walkthrough of ${SITE_CONFIG.name} for your school. Tell us about your school and a time that suits you.`, path: '/request-demo' }),
    },
  },
  {
    path: 'privacy-policy',
    loadComponent: () => import('./pages/legal-page.component').then((m) => m.LegalPageComponent),
    data: { kind: 'privacy', ...seo({ title: `Privacy Policy | ${SITE_CONFIG.name}`, description: `How ${SITE_CONFIG.name} handles the details you send us.`, path: '/privacy-policy' }) },
  },
  {
    path: 'terms',
    loadComponent: () => import('./pages/legal-page.component').then((m) => m.LegalPageComponent),
    data: { kind: 'terms', ...seo({ title: `Terms of Use | ${SITE_CONFIG.name}`, description: `The terms for using the ${SITE_CONFIG.name} website.`, path: '/terms' }) },
  },
];
