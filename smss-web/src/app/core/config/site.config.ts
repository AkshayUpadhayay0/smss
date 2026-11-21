import { environment } from '../../../environments/environment';

/**
 * The developer name EXACTLY as it appears on the Google Play Console. The ONE place to set it: `npm run build` refuses to run while it still holds the
 * placeholder (scripts/seo-files.mjs), listing every place that shows it.
 */
export const DEVELOPER_NAME = 'Akshay Upadhayay';

/** "Last updated" date of the Privacy Policy (yyyy-MM-dd). Change it whenever the policy text changes. */
export const PRIVACY_LAST_UPDATED = '2026-11-21';

/**
 * Everything about the company that the public site shows or publishes (header, footer, contact page, SEO, structured data).
 * Change it here and nowhere else. EVERY value below is a PLACEHOLDER until the real details are filled in.
 */
export const SITE_CONFIG = {
  name: 'SMSS',
  /** The developer / publisher name shown in the footer, the Privacy Policy and the structured data. */
  legalName: DEVELOPER_NAME,
  tagline: 'School Management System',
  description:
    'SMSS is a modern, multi-school management platform: admissions, students, attendance, examinations, fees and communication in one secure place.',
  /** Public origin, from the environment file (siteUrl). No trailing slash. */
  url: environment.siteUrl.replace(/\/+$/, ''),
  /** Path under the site root of the social-sharing picture (1200x630). */
  ogImagePath: '/og-image.png',
  email: 'smsserp.info@gmail.com',
  phone: '+91 9149241421',
  phoneHref: '+919149241421',
  address: 'Dehradun, Uttarakhand, India',
  /** Add real profiles when they exist; the footer and the structured data pick them up automatically. */
  social: [] as { label: string; url: string }[],
  year: new Date().getFullYear(),
} as const;
