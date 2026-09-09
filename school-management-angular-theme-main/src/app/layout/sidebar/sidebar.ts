import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { RouterLink, RouterLinkActive } from '@angular/router';
import { IconComponent } from '../../shared/components/icon/icon';
import { ThemeService } from '../../core/services/theme.service';
import { BrandingService } from '../../core/services/branding.service';
import { NAV_SECTIONS, NavItem } from './nav-config';

@Component({
  selector: 'app-sidebar',
  standalone: true,
  imports: [RouterLink, RouterLinkActive, IconComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <aside class="sidebar" [class.icon-mode]="isIcon()" [class.mobile-open]="theme.mobileSidebarOpen()">
      <div class="brand">
        <div class="brand-logo">{{ branding.branding().logoInitials }}</div>
        @if (!isIcon()) {
          <div class="brand-text">
            <div class="brand-name">{{ branding.branding().shortName }}</div>
            <div class="brand-sub text-muted">{{ branding.branding().name }}</div>
          </div>
        }
        <button class="mobile-close btn btn-ghost btn-icon btn-sm" (click)="theme.closeMobileSidebar()" aria-label="Close menu">
          <app-icon name="x" [size]="18" />
        </button>
      </div>

      <nav class="nav">
        @for (section of sections; track section.title) {
          <div class="nav-section">
            @if (!isIcon()) { <div class="section-label text-muted">{{ section.title }}</div> }
            @for (item of section.items; track item.label) {
              @if (item.children) {
                <div class="nav-parent">
                  <button
                    class="nav-link parent-toggle"
                    [class.expanded]="isExpanded(item.label)"
                    (click)="toggle(item.label)"
                    type="button"
                  >
                    <app-icon [name]="item.icon" [size]="19" />
                    @if (!isIcon()) {
                      <span class="label">{{ item.label }}</span>
                      <app-icon name="chevron-down" [size]="14" class="chevron" />
                    }
                    @if (isIcon()) { <span class="tooltip">{{ item.label }}</span> }
                  </button>
                  @if (isExpanded(item.label) && !isIcon()) {
                    <div class="submenu">
                      @for (child of item.children; track child.label) {
                        <a
                          class="nav-link sub"
                          [routerLink]="child.link"
                          routerLinkActive="active"
                        >
                          <app-icon [name]="child.icon" [size]="16" />
                          <span class="label">{{ child.label }}</span>
                        </a>
                      }
                    </div>
                  }
                </div>
              } @else {
                <a
                  class="nav-link"
                  [routerLink]="item.link"
                  routerLinkActive="active"
                  [routerLinkActiveOptions]="{ exact: item.link === '/dashboard' }"
                  (click)="theme.closeMobileSidebar()"
                >
                  <app-icon [name]="item.icon" [size]="19" />
                  @if (!isIcon()) { <span class="label">{{ item.label }}</span> }
                  @if (isIcon()) { <span class="tooltip">{{ item.label }}</span> }
                </a>
              }
            }
          </div>
        }
      </nav>

      <div class="sidebar-footer">
        <button class="collapse-btn btn btn-outline btn-sm" (click)="cycleSidebarStyle()">
          <app-icon name="chevrons-left" [size]="15" [style.transform]="isIcon() ? 'rotate(180deg)' : 'none'" />
          @if (!isIcon()) { <span>Collapse</span> }
        </button>
      </div>
    </aside>
    @if (theme.mobileSidebarOpen()) {
      <div class="mobile-backdrop" (click)="theme.closeMobileSidebar()"></div>
    }
  `,
  styles: [`
    .sidebar {
      position: fixed; top: 0; left: 0; bottom: 0; width: var(--sidebar-width);
      background: var(--surface); border-right: 1px solid var(--border);
      display: flex; flex-direction: column; z-index: 40;
      transition: width var(--transition-base), transform var(--transition-base);
    }
    .icon-mode { width: 80px; }

    .brand { display: flex; align-items: center; gap: 12px; padding: var(--space-5) var(--space-5); height: var(--header-height); border-bottom: 1px solid var(--border); position: relative; }
    .brand-logo {
      width: 38px; height: 38px; border-radius: var(--radius-md); background: var(--primary); color: #fff;
      display: flex; align-items: center; justify-content: center; font-weight: 800; font-family: var(--font-display); flex-shrink: 0;
    }
    .brand-text { min-width: 0; }
    .brand-name { font-weight: 800; font-family: var(--font-display); font-size: 15px; white-space: nowrap; }
    .brand-sub { font-size: 10.5px; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; max-width: 160px; }
    .mobile-close { display: none; position: absolute; right: 12px; }

    .nav { flex: 1; overflow-y: auto; padding: var(--space-3) var(--space-3) var(--space-5); }
    .nav-section { margin-bottom: var(--space-2); }
    .section-label { font-size: 10.5px; font-weight: 700; text-transform: uppercase; letter-spacing: 0.06em; padding: var(--space-4) var(--space-3) var(--space-1); }

    .nav-link {
      display: flex; align-items: center; gap: 12px; padding: 9px 12px; border-radius: var(--radius-md);
      color: var(--text-secondary); font-size: 13.5px; font-weight: 500; margin-bottom: 2px; position: relative;
      cursor: pointer; border: none; background: transparent; width: 100%; text-align: left; font-family: inherit;
      transition: background var(--transition-fast), color var(--transition-fast);
    }
    .nav-link:hover { background: var(--surface-alt); color: var(--text-primary); }
    .nav-link.active { background: var(--primary-light); color: var(--primary); font-weight: 700; }
    .nav-link .label { flex: 1; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
    .chevron { transition: transform var(--transition-fast); }
    .parent-toggle.expanded .chevron { transform: rotate(180deg); }
    .submenu { padding-left: 14px; margin: 2px 0 6px; border-left: 2px solid var(--border); margin-left: 22px; }
    .sub { font-size: 13px; padding: 7px 12px; }

    .icon-mode .nav-link { justify-content: center; }
    .icon-mode .nav-link .tooltip {
      position: absolute; left: 100%; margin-left: 12px; background: var(--text-primary); color: var(--surface);
      padding: 6px 10px; border-radius: var(--radius-sm); font-size: 12px; white-space: nowrap; opacity: 0;
      pointer-events: none; transition: opacity var(--transition-fast); z-index: 60;
    }
    .icon-mode .nav-link:hover .tooltip { opacity: 1; }

    .sidebar-footer { padding: var(--space-4); border-top: 1px solid var(--border); }
    .collapse-btn { width: 100%; justify-content: center; }

    .mobile-backdrop { display: none; }

    @media (max-width: 1024px) {
      .sidebar { transform: translateX(-100%); width: 272px !important; box-shadow: var(--shadow-lg); }
      .sidebar.mobile-open { transform: translateX(0); }
      .mobile-close { display: inline-flex; }
      .icon-mode.mobile-open .nav-link { justify-content: flex-start; }
      .sidebar-footer { display: none; }
      .mobile-backdrop {
        display: block; position: fixed; inset: 0; background: rgba(10,14,24,0.5); z-index: 39;
      }
    }
  `],
})
export class SidebarComponent {
  theme = inject(ThemeService);
  branding = inject(BrandingService);
  sections = NAV_SECTIONS;

  private expandedItems = signal<Set<string>>(new Set(['Examinations']));

  isIcon(): boolean {
    return this.theme.sidebarStyle() === 'icon';
  }

  isExpanded(label: string): boolean {
    return this.expandedItems().has(label);
  }

  toggle(label: string): void {
    const next = new Set(this.expandedItems());
    if (next.has(label)) next.delete(label); else next.add(label);
    this.expandedItems.set(next);
  }

  cycleSidebarStyle(): void {
    this.theme.setSidebarStyle(this.theme.sidebarStyle() === 'icon' ? 'expanded' : 'icon');
  }
}
