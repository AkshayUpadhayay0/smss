import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, inject } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { filter } from 'rxjs';
import { AuthService } from '../../../core/services/auth.service';
import { IconComponent, IconName } from '../../../shared/components';
import { FamilyService } from '../services/family.service';

interface ParentNavItem { label: string; link: string; icon: IconName; exact: boolean; badge?: boolean }

/** The parent's own shell: a header and four links (Family, Notices, Calendar, Account). No staff sidebar, no topbar menus. Mobile first: the links sit at the bottom of a narrow screen and move up beside the title on a wide one. */
@Component({
  selector: 'app-parent-layout',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, IconComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styles: [`
    :host { display: flex; flex-direction: column; min-height: 100vh; background: var(--color-bg, #f8f9fb); }
    header { display: flex; align-items: center; gap: var(--space-3); padding: var(--space-3) var(--space-4); background: var(--color-surface, #fff); border-bottom: 1px solid var(--color-border); position: sticky; top: 0; z-index: 5; }
    .brand { display: flex; flex-direction: column; min-width: 0; flex: 1; }
    .brand strong { font-size: var(--text-base); overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .brand span { font-size: var(--text-xs); color: var(--color-text-muted); overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    nav { display: flex; gap: var(--space-1); }
    nav a { display: flex; flex-direction: column; align-items: center; gap: 2px; padding: var(--space-2) var(--space-3); border-radius: var(--radius-md); color: var(--color-text-muted); text-decoration: none; font-size: var(--text-xs); position: relative; min-width: 60px; }
    nav a.active { color: var(--color-primary-700, var(--color-primary)); background: var(--color-primary-soft, #f1edff); font-weight: var(--weight-semibold); }
    .badge { position: absolute; top: 2px; right: 10px; min-width: 16px; height: 16px; padding: 0 4px; border-radius: 8px; background: var(--color-danger); color: #fff; font-size: 10px; line-height: 16px; text-align: center; }
    main { flex: 1; width: 100%; max-width: 960px; margin: 0 auto; padding: var(--space-4); padding-bottom: 88px; }
    @media (max-width: 719px) {
      header nav { position: fixed; left: 0; right: 0; bottom: 0; justify-content: space-around; background: var(--color-surface, #fff); border-top: 1px solid var(--color-border); padding: var(--space-1); z-index: 6; }
      header nav a { flex: 1; }
    }
    @media (min-width: 720px) { main { padding-bottom: var(--space-6); } }
  `],
  template: `
    <header>
      <div class="brand">
        <strong data-field="school">{{ user()?.schoolName || 'SMSS' }}</strong>
        <span data-field="parent">{{ user()?.displayName }}</span>
      </div>
      <nav aria-label="Parent navigation">
        @for (n of items; track n.link) {
          <a [routerLink]="n.link" routerLinkActive="active" [routerLinkActiveOptions]="{ exact: n.exact }" [attr.data-nav]="n.label">
            <app-icon [name]="n.icon" [size]="20" />{{ n.label }}
            @if (n.badge && (unread() ?? 0) > 0) { <span class="badge" data-field="unread" aria-label="Unread notices">{{ unread() }}</span> }
          </a>
        }
      </nav>
    </header>
    <main><router-outlet /></main>
  `,
})
export class ParentLayoutComponent implements OnInit {
  private readonly auth = inject(AuthService);
  private readonly family = inject(FamilyService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly user = this.auth.user;
  protected readonly unread = this.family.unread;
  protected readonly items: ParentNavItem[] = [
    { label: 'Family', link: '/family', icon: 'users', exact: true },
    { label: 'Notices', link: '/family/notices', icon: 'bell', exact: false, badge: true },
    { label: 'Calendar', link: '/family/calendar', icon: 'calendar', exact: false },
    { label: 'Account', link: '/family/account', icon: 'user', exact: false },
  ];

  ngOnInit(): void {
    this.family.refreshUnread();
    this.router.events.pipe(filter((e) => e instanceof NavigationEnd), takeUntilDestroyed(this.destroyRef)).subscribe(() => this.family.refreshUnread());
  }
}
