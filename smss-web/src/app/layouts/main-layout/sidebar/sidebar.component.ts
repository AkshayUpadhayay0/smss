import { ChangeDetectionStrategy, Component, computed, inject, input, output, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router, RouterLink } from '@angular/router';
import { filter, map, startWith } from 'rxjs';
import { canAccess } from '../../../core/config/route-roles';
import { AuthService } from '../../../core/services/auth.service';
import { IconComponent } from '../../../shared/components/icon/icon.component';
import { LogoComponent } from '../../../shared/components/logo/logo.component';
import { NAV_ITEMS, NavItem } from '../nav.config';

@Component({
  selector: 'app-sidebar',
  imports: [RouterLink, IconComponent, LogoComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './sidebar.component.scss',
  host: { '[class.is-collapsed]': 'collapsed()' },
  templateUrl: './sidebar.component.html',
})
export class SidebarComponent {
  private readonly router = inject(Router);
  private readonly auth = inject(AuthService);

  /** Current URL path (no query / fragment), kept in a signal so the active highlight follows every navigation. */
  private readonly path = toSignal(
    this.router.events.pipe(
      filter((e): e is NavigationEnd => e instanceof NavigationEnd),
      startWith(null),
      map(() => this.router.url.split(/[?#]/)[0]),
    ),
    { initialValue: this.router.url.split(/[?#]/)[0] },
  );

  /** Every route in the menu, leaves and group children. */
  private static readonly ALL_ROUTES: string[] = NAV_ITEMS.flatMap((i) => (i.children ? i.children.map((c) => c.route) : i.route ? [i.route] : []));

  readonly collapsed = input(false);
  /** Emitted when a link is followed (lets the mobile drawer close itself). */
  readonly navigated = output<void>();
  /** Emitted when a group is clicked while the rail is collapsed, so the layout can expand it. */
  readonly expandRequested = output<void>();

  /**
   * Only what the signed-in user may open, decided by the same ROUTE_ROLES the route guard uses. A group
   * (e.g. Masters) disappears entirely when none of its children are allowed.
   */
  protected readonly items = computed<NavItem[]>(() => {
    const roles = this.auth.currentUserRoles();
    return NAV_ITEMS.flatMap((item) => {
      if (item.children) {
        const children = item.children.filter((c) => canAccess(c.route, roles));
        return children.length ? [{ ...item, children }] : [];
      }
      return item.route && canAccess(item.route, roles) ? [item] : [];
    });
  });
  private readonly openGroups = signal<Set<string>>(
    new Set(NAV_ITEMS.filter((i) => this.containsUrl(i, this.router.url)).map((i) => i.label)),
  );

  /**
   * A link is active for its own URL and anything below it (/students/DPS001ST001 keeps "Students" lit), unless a MORE
   * specific menu item also matches: /students/onboard-existing belongs to "Add Existing Student", not to "Students".
   */
  protected isActive(route: string | undefined): boolean {
    if (!route) return false;
    const path = this.path();
    const matches = (r: string) => path === r || path.startsWith(r + '/');
    if (!matches(route)) return false;
    return !SidebarComponent.ALL_ROUTES.some((r) => r.length > route.length && matches(r));
  }

  protected isOpen(item: NavItem): boolean {
    return this.openGroups().has(item.label);
  }

  protected toggleGroup(item: NavItem): void {
    if (this.collapsed()) {
      this.expandRequested.emit();
      this.openGroups.update((s) => new Set(s).add(item.label));
      return;
    }
    this.openGroups.update((s) => {
      const next = new Set(s);
      if (!next.delete(item.label)) next.add(item.label);
      return next;
    });
  }

  private containsUrl(item: NavItem, url: string): boolean {
    return !!item.children?.some((c) => url.startsWith(c.route));
  }
}
