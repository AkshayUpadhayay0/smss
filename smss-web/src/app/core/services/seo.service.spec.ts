import { DOCUMENT } from '@angular/common';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { SITE_CONFIG } from '../config/site.config';
import { SeoService } from './seo.service';

describe('SeoService', () => {
  let seo: SeoService;
  let doc: Document;
  const meta = (selector: string) => doc.head.querySelector(selector)?.getAttribute('content');

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideRouter([])] });
    seo = TestBed.inject(SeoService);
    doc = TestBed.inject(DOCUMENT);
    doc.head.querySelectorAll('meta[name="robots"], link[rel="canonical"], script[data-seo-ld]').forEach((e) => e.remove());
  });

  it('sets title, description, canonical, Open Graph, Twitter and robots for an indexable page', () => {
    seo.apply({ title: 'Contact Us | SMSS', description: 'Write to us.', index: true, path: '/contact' }, '/contact?x=1');
    expect(doc.title).toBe('Contact Us | SMSS');
    expect(meta('meta[name="description"]')).toBe('Write to us.');
    expect(meta('meta[name="robots"]')).toBe('index, follow');
    expect(doc.head.querySelector('link[rel="canonical"]')?.getAttribute('href')).toBe(`${SITE_CONFIG.url}/contact`);
    expect(meta('meta[property="og:title"]')).toBe('Contact Us | SMSS');
    expect(meta('meta[property="og:url"]')).toBe(`${SITE_CONFIG.url}/contact`);
    expect(meta('meta[name="twitter:card"]')).toBe('summary_large_image');
  });

  it('keeps a page without SEO data (or with index: false) out of search results and drops the canonical link', () => {
    seo.apply({ title: 'T', description: 'D', index: true, path: '/contact' }, '/contact');
    seo.apply(undefined, '/dashboard');
    expect(meta('meta[name="robots"]')).toBe('noindex, nofollow');
    expect(doc.head.querySelector('link[rel="canonical"]')).toBeNull();
    seo.apply({ title: 'Sign in', description: '', index: false }, '/login');
    expect(meta('meta[name="robots"]')).toBe('noindex, nofollow');
  });

  it('writes JSON-LD once per navigation and escapes "<" so the data cannot close the script', () => {
    seo.apply({ title: 'T', description: 'D', index: true, path: '/', jsonLd: [{ '@type': 'Organization', name: 'A</script><b>' }] }, '/');
    seo.apply({ title: 'T', description: 'D', index: true, path: '/', jsonLd: [{ '@type': 'Organization', name: 'A</script><b>' }] }, '/');
    const scripts = doc.head.querySelectorAll('script[data-seo-ld]');
    expect(scripts).toHaveLength(1);
    expect(scripts[0].textContent).not.toContain('</script>');
    expect(JSON.parse(scripts[0].textContent!).name).toBe('A</script><b>');
    seo.apply(undefined, '/dashboard');
    expect(doc.head.querySelectorAll('script[data-seo-ld]')).toHaveLength(0);
  });
});
