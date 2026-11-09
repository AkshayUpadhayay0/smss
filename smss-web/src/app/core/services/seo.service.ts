import { DOCUMENT } from '@angular/common';
import { Injectable, inject } from '@angular/core';
import { Meta, Title } from '@angular/platform-browser';
import { ActivatedRouteSnapshot, NavigationEnd, Router } from '@angular/router';
import { filter } from 'rxjs';
import { SITE_CONFIG } from '../config/site.config';

/** Put under a route's `data: { seo: {...} }`. A route with no `seo` is treated as private: noindex, nofollow. */
export interface SeoData {
  title: string;
  description: string;
  /** Canonical path, e.g. "/contact". Defaults to the current URL path. */
  path?: string;
  /** Public page that search engines may index. */
  index?: boolean;
  /** Structured data (JSON-LD) objects for this page. */
  jsonLd?: object[];
}

const DEFAULT_TITLE = `${SITE_CONFIG.name} | ${SITE_CONFIG.tagline}`;
const LD_ATTR = 'data-seo-ld';

/**
 * Per-route <title>, description, canonical link, Open Graph, Twitter Card, robots and JSON-LD, driven by route data.
 * Runs in the browser AND during the build-time prerender, so the static HTML of each public page already carries its own tags.
 */
@Injectable({ providedIn: 'root' })
export class SeoService {
  private readonly title = inject(Title);
  private readonly meta = inject(Meta);
  private readonly doc = inject(DOCUMENT);
  private readonly router = inject(Router);

  /** Call once from the root component. */
  init(): void {
    this.router.events.pipe(filter((e): e is NavigationEnd => e instanceof NavigationEnd)).subscribe(() => {
      this.apply(this.leafData(this.router.routerState.snapshot.root)?.['seo'] as SeoData | undefined, this.router.url);
    });
  }

  apply(seo: SeoData | undefined, url: string): void {
    if (!seo?.index) {
      // Everything without explicit SEO data (login, the signed-in app) stays out of search results
      this.title.setTitle(seo?.title ?? DEFAULT_TITLE);
      this.setName('robots', 'noindex, nofollow');
      this.setCanonical(null);
      this.clearJsonLd();
      return;
    }

    const path = seo.path ?? '/' + url.split(/[?#]/)[0].replace(/^\/+|\/+$/g, '');
    const canonical = SITE_CONFIG.url + (path === '/' ? '/' : path);
    const image = SITE_CONFIG.url + SITE_CONFIG.ogImagePath;

    this.title.setTitle(seo.title);
    this.setName('description', seo.description);
    this.setName('robots', 'index, follow');
    this.setCanonical(canonical);

    this.setProperty('og:type', 'website');
    this.setProperty('og:site_name', SITE_CONFIG.name);
    this.setProperty('og:title', seo.title);
    this.setProperty('og:description', seo.description);
    this.setProperty('og:url', canonical);
    this.setProperty('og:image', image);
    this.setProperty('og:locale', 'en_IN');
    this.setName('twitter:card', 'summary_large_image');
    this.setName('twitter:title', seo.title);
    this.setName('twitter:description', seo.description);
    this.setName('twitter:image', image);

    this.clearJsonLd();
    for (const item of seo.jsonLd ?? []) this.addJsonLd(item);
  }

  private leafData(route: ActivatedRouteSnapshot): Record<string, unknown> | undefined {
    let r = route;
    while (r.firstChild) r = r.firstChild;
    return r.data;
  }

  private setName(name: string, content: string): void {
    this.meta.updateTag({ name, content });
  }

  private setProperty(property: string, content: string): void {
    this.meta.updateTag({ property, content });
  }

  private setCanonical(href: string | null): void {
    const head = this.doc.head;
    let link = head.querySelector<HTMLLinkElement>('link[rel="canonical"]');
    if (!href) {
      link?.remove();
      return;
    }
    if (!link) {
      link = this.doc.createElement('link');
      link.setAttribute('rel', 'canonical');
      head.appendChild(link);
    }
    link.setAttribute('href', href);
  }

  private addJsonLd(data: object): void {
    const script = this.doc.createElement('script');
    script.type = 'application/ld+json';
    script.setAttribute(LD_ATTR, '');
    // "<" is escaped so the data can never close the script element early
    script.text = JSON.stringify(data).replace(/</g, '\\u003c');
    this.doc.head.appendChild(script);
  }

  private clearJsonLd(): void {
    this.doc.head.querySelectorAll(`script[${LD_ATTR}]`).forEach((s) => s.remove());
  }
}
