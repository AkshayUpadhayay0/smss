import { Injectable, signal } from '@angular/core';
import { ActivatedRoute, ActivatedRouteSnapshot, NavigationEnd, Router } from '@angular/router';
import { filter } from 'rxjs';
import { BreadcrumbItem } from '../models';

/**
 * Walks the activated route tree on every navigation and builds a
 * breadcrumb trail from each route's `data.breadcrumb`, so most pages
 * never need to build breadcrumbs by hand — just set route data.
 */
@Injectable({ providedIn: 'root' })
export class BreadcrumbService {
  readonly items = signal<BreadcrumbItem[]>([{ label: 'Home', url: '/dashboard' }]);

  constructor(
    private readonly router: Router,
    private readonly activatedRoute: ActivatedRoute,
  ) {
    this.router.events.pipe(filter((event) => event instanceof NavigationEnd)).subscribe(() => {
      this.items.set(this.buildTrail());
    });
  }

  private buildTrail(): BreadcrumbItem[] {
    const trail: BreadcrumbItem[] = [{ label: 'Home', url: '/dashboard' }];
    let route: ActivatedRouteSnapshot | null = this.activatedRoute.snapshot.root;
    let url = '';

    while (route) {
      const segments = route.url.map((s) => s.path).filter(Boolean);
      if (segments.length) {
        url += '/' + segments.join('/');
      }
      const breadcrumb = route.data?.['breadcrumb'] as string | string[] | undefined;
      if (breadcrumb) {
        const labels = Array.isArray(breadcrumb) ? breadcrumb : [breadcrumb];
        labels.forEach((label) => trail.push({ label, url }));
      }
      route = route.firstChild;
    }

    // The last item should not be clickable.
    if (trail.length > 1) {
      trail[trail.length - 1] = { label: trail[trail.length - 1].label };
    }

    return trail;
  }
}
