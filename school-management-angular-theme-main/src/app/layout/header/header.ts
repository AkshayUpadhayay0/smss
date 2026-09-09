import { ChangeDetectionStrategy, Component, HostListener, inject, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { IconComponent } from '../../shared/components/icon/icon';
import { AvatarComponent } from '../../shared/components/avatar/avatar';
import { ThemeService } from '../../core/services/theme.service';
import { AuthService } from '../../core/services/auth.service';
import { NotificationService } from '../../core/services/data.service';
import { SESSIONS } from '../../core/services/mock-data';
import { AppNotification } from '../../core/models/school.models';

type MenuKey = 'session' | 'notifications' | 'messages' | 'theme' | 'user' | null;

@Component({
  selector: 'app-header',
  standalone: true,
  imports: [RouterLink, FormsModule, IconComponent, AvatarComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <header class="app-header">
      <div class="left">
        <button class="btn btn-ghost btn-icon" (click)="theme.toggleMobileSidebar()" aria-label="Toggle menu">
          <app-icon name="menu" [size]="20" />
        </button>
        <button class="btn btn-ghost btn-icon desktop-only" (click)="theme.setSidebarStyle(theme.sidebarStyle() === 'icon' ? 'expanded' : 'icon')" aria-label="Collapse sidebar">
          <app-icon name="menu" [size]="20" />
        </button>
        <div class="search-box">
          <app-icon name="search" [size]="16" class="search-icon" />
          <input class="search-input" type="text" placeholder="Search students, teachers, classes…" [(ngModel)]="searchQuery" name="search" />
        </div>
      </div>

      <div class="right">
        <div class="dropdown-wrap">
          <button class="session-chip" (click)="toggleMenu('session')">
            <app-icon name="calendar" [size]="15" />
            <span class="desktop-only">{{ activeSession }}</span>
          </button>
          @if (openMenu() === 'session') {
            <div class="dropdown">
              <div class="dropdown-title">Academic Session</div>
              @for (s of sessions; track s.id) {
                <button class="dropdown-item" (click)="activeSession = s.label; openMenu.set(null)">
                  <span>{{ s.label }}</span>
                  <span class="badge" [class]="s.status === 'Active' ? 'badge-success' : 'badge-neutral'">{{ s.status }}</span>
                </button>
              }
            </div>
          }
        </div>

        <div class="dropdown-wrap">
          <button class="btn btn-ghost btn-icon icon-btn" (click)="toggleMenu('notifications')" aria-label="Notifications">
            <app-icon name="bell" [size]="19" />
            @if (unreadCount() > 0) { <span class="dot-badge">{{ unreadCount() }}</span> }
          </button>
          @if (openMenu() === 'notifications') {
            <div class="dropdown wide">
              <div class="dropdown-title flex justify-between items-center">
                <span>Notifications</span>
                <a routerLink="/notifications" class="link-sm" (click)="openMenu.set(null)">View all</a>
              </div>
              @for (n of notifications().slice(0, 5); track n.id) {
                <div class="notif-item" [class.unread]="!n.read">
                  <div class="notif-dot" [class]="dotClass(n.type)"></div>
                  <div class="notif-body">
                    <div class="notif-title">{{ n.title }}</div>
                    <div class="notif-msg text-secondary">{{ n.message }}</div>
                    <div class="notif-time text-muted">{{ n.date }}</div>
                  </div>
                </div>
              }
            </div>
          }
        </div>

        <button class="btn btn-ghost btn-icon icon-btn desktop-only" routerLink="/messages" aria-label="Messages">
          <app-icon name="message-square" [size]="19" />
        </button>

        <div class="dropdown-wrap">
          <button class="btn btn-ghost btn-icon" (click)="toggleMenu('theme')" aria-label="Theme switcher">
            <app-icon [name]="theme.resolvedDark() ? 'moon' : 'sun'" [size]="19" />
          </button>
          @if (openMenu() === 'theme') {
            <div class="dropdown">
              <div class="dropdown-title">Appearance</div>
              <button class="dropdown-item" (click)="theme.setMode('light')">
                <app-icon name="sun" [size]="16" /> Light @if (theme.mode() === 'light') { <app-icon name="check" [size]="14" class="ml-auto" /> }
              </button>
              <button class="dropdown-item" (click)="theme.setMode('dark')">
                <app-icon name="moon" [size]="16" /> Dark @if (theme.mode() === 'dark') { <app-icon name="check" [size]="14" class="ml-auto" /> }
              </button>
              <button class="dropdown-item" (click)="theme.setMode('system')">
                <app-icon name="monitor" [size]="16" /> System @if (theme.mode() === 'system') { <app-icon name="check" [size]="14" class="ml-auto" /> }
              </button>
              <div class="dropdown-divider"></div>
              <a routerLink="/settings" class="dropdown-item" (click)="openMenu.set(null)">
                <app-icon name="palette" [size]="16" /> Theme customizer
              </a>
            </div>
          }
        </div>

        <div class="dropdown-wrap">
          <button class="user-chip" (click)="toggleMenu('user')">
            <app-avatar [src]="auth.currentUser()?.avatar ?? ''" [name]="auth.currentUser()?.name ?? ''" [size]="34" />
            <div class="user-info desktop-only">
              <div class="user-name">{{ auth.currentUser()?.name }}</div>
              <div class="user-role text-muted">{{ auth.currentUser()?.role }}</div>
            </div>
            <app-icon name="chevron-down" [size]="14" class="desktop-only" />
          </button>
          @if (openMenu() === 'user') {
            <div class="dropdown">
              <a routerLink="/profile" class="dropdown-item" (click)="openMenu.set(null)"><app-icon name="user" [size]="16" /> My Profile</a>
              <a routerLink="/settings" class="dropdown-item" (click)="openMenu.set(null)"><app-icon name="settings" [size]="16" /> Account Settings</a>
              <a routerLink="/settings" class="dropdown-item" (click)="openMenu.set(null)"><app-icon name="lock" [size]="16" /> Change Password</a>
              <a routerLink="/notifications" class="dropdown-item" (click)="openMenu.set(null)"><app-icon name="bell" [size]="16" /> Notifications</a>
              <div class="dropdown-divider"></div>
              <button class="dropdown-item danger" (click)="logout()"><app-icon name="log-out" [size]="16" /> Logout</button>
            </div>
          }
        </div>
      </div>
    </header>
  `,
  styles: [`
    .app-header {
      position: sticky; top: 0; z-index: 30; height: var(--header-height);
      background: var(--surface); border-bottom: 1px solid var(--border);
      display: flex; align-items: center; justify-content: space-between; padding: 0 var(--space-5); gap: var(--space-4);
    }
    .left { display: flex; align-items: center; gap: var(--space-3); flex: 1; min-width: 0; }
    .right { display: flex; align-items: center; gap: var(--space-2); }

    .search-box { position: relative; max-width: 380px; width: 100%; }
    .search-icon { position: absolute; left: 12px; top: 50%; transform: translateY(-50%); color: var(--text-muted); }
    .search-input {
      width: 100%; padding: 9px 12px 9px 36px; border-radius: var(--radius-full); border: 1px solid var(--border);
      background: var(--surface-alt); font-size: 13px; color: var(--text-primary); font-family: inherit;
    }
    .search-input:focus { outline: none; border-color: var(--primary); background: var(--surface); }

    .session-chip, .user-chip {
      display: flex; align-items: center; gap: 8px; padding: 7px 12px; border-radius: var(--radius-full);
      background: var(--surface-alt); border: 1px solid var(--border); cursor: pointer; color: var(--text-primary); font-size: 12.5px; font-weight: 600;
    }
    .user-chip { padding: 5px 10px 5px 5px; }
    .user-info { text-align: left; line-height: 1.25; }
    .user-name { font-size: 12.5px; font-weight: 700; }
    .user-role { font-size: 10.5px; }

    .icon-btn { position: relative; }
    .dot-badge {
      position: absolute; top: 2px; right: 2px; background: var(--danger); color: #fff; font-size: 9.5px; font-weight: 700;
      min-width: 15px; height: 15px; border-radius: 999px; display: flex; align-items: center; justify-content: center; padding: 0 3px;
    }

    .dropdown-wrap { position: relative; }
    .dropdown {
      position: absolute; right: 0; top: calc(100% + 10px); background: var(--surface); border: 1px solid var(--border);
      border-radius: var(--radius-lg); box-shadow: var(--shadow-lg); min-width: 220px; padding: 6px; z-index: 50;
      animation: fadeSlideIn 140ms ease;
    }
    .dropdown.wide { min-width: 320px; max-height: 400px; overflow-y: auto; }
    .dropdown-title { font-size: 11px; font-weight: 700; text-transform: uppercase; color: var(--text-muted); padding: 8px 10px 6px; letter-spacing: 0.04em; }
    .dropdown-item {
      display: flex; align-items: center; gap: 10px; width: 100%; padding: 8px 10px; border-radius: var(--radius-md);
      background: transparent; border: none; color: var(--text-primary); font-size: 13px; cursor: pointer; text-align: left; font-family: inherit;
    }
    .dropdown-item:hover { background: var(--surface-alt); }
    .dropdown-item.danger { color: var(--danger); }
    .dropdown-divider { height: 1px; background: var(--border); margin: 6px 4px; }
    .ml-auto { margin-left: auto; }
    .link-sm { font-size: 12px; color: var(--primary); font-weight: 600; }

    .notif-item { display: flex; gap: 10px; padding: 9px 10px; border-radius: var(--radius-md); }
    .notif-item.unread { background: var(--surface-alt); }
    .notif-dot { width: 8px; height: 8px; border-radius: 50%; margin-top: 5px; flex-shrink: 0; }
    .notif-dot.general { background: var(--info); } .notif-dot.attendance { background: var(--warning); }
    .notif-dot.fees { background: var(--success); } .notif-dot.homework { background: var(--primary); }
    .notif-dot.examination { background: #7c3aed; } .notif-dot.emergency { background: var(--danger); }
    .notif-title { font-size: 12.5px; font-weight: 700; }
    .notif-msg { font-size: 12px; margin-top: 2px; }
    .notif-time { font-size: 10.5px; margin-top: 3px; }

    @media (max-width: 860px) { .desktop-only { display: none !important; } .search-box { max-width: 200px; } }
    @media (max-width: 560px) { .search-box { display: none; } }
  `],
})
export class HeaderComponent {
  theme = inject(ThemeService);
  auth = inject(AuthService);
  private notificationService = inject(NotificationService);
  private router = inject(Router);

  sessions = SESSIONS;
  activeSession = SESSIONS.find(s => s.status === 'Active')?.label ?? SESSIONS[0].label;
  searchQuery = '';
  openMenu = signal<MenuKey>(null);
  notifications = signal<AppNotification[]>([]);

  constructor() {
    this.notificationService.getAll().subscribe(list => this.notifications.set(list));
  }

  unreadCount() {
    return this.notifications().filter(n => !n.read).length;
  }

  dotClass(type: string): string {
    return type.toLowerCase();
  }

  toggleMenu(key: Exclude<MenuKey, null>): void {
    this.openMenu.set(this.openMenu() === key ? null : key);
  }

  logout(): void {
    this.openMenu.set(null);
    this.auth.logout();
    this.router.navigate(['/auth/login']);
  }

  @HostListener('document:click', ['$event'])
  onDocClick(event: MouseEvent): void {
    const target = event.target as HTMLElement;
    if (!target.closest('.dropdown-wrap')) {
      this.openMenu.set(null);
    }
  }
}
