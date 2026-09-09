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
  templateUrl: `./header.html`,
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
