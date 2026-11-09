import { environment } from '../../../environments/environment';

/**
 * Everything about the company that the public site shows or publishes (header, footer, contact page, SEO, structured data).
 * Change it here and nowhere else. EVERY value below is a PLACEHOLDER until the real details are filled in.
 */
export const SITE_CONFIG = {
  name: 'SMSS',
  legalName: 'SMSS (company name to be confirmed)',
  tagline: 'School Management System',
  description:
    'SMSS is a modern, multi-school management platform: admissions, students, attendance, examinations, fees and communication in one secure place.',
  /** Public origin, from the environment file (siteUrl). No trailing slash. */
  url: environment.siteUrl.replace(/\/+$/, ''),
  /** Path under the site root of the social-sharing picture (1200x630). */
  ogImagePath: '/og-image.png',
  email: 'hello@example-smss.com',
  phone: '+91 00000 00000',
  phoneHref: '+910000000000',
  address: 'Office address to be added, City, State, India',
  /** Add real profiles when they exist; the footer and the structured data pick them up automatically. */
  social: [] as { label: string; url: string }[],
  year: new Date().getFullYear(),
} as const;
