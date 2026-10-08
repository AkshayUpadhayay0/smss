import { ChangeDetectionStrategy, Component, ElementRef, HostListener, computed, inject, output, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { AuthService } from '../../../core/services/auth.service';
import { IconComponent } from '../../../shared/components/icon/icon.component';

type MenuName = 'org' | 'notifications' | 'user';

// Placeholder until a notifications API exists.
const PLACEHOLDER_NOTIFICATIONS = [
  { title: 'New school registered', time: '5 min ago' },
  { title: 'Password changed successfully', time: '1 hour ago' },
];

@Component({
  selector: 'app-topbar',
  imports: [RouterLink, IconComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './topbar.component.scss',
  templateUrl: './topbar.component.html',
})
export class TopbarComponent {
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  readonly menuToggle = output<void>();

  /** Signed-in user's organization (their school) and identity, from the login session. */
  protected readonly org = computed(() => {
    const u = this.auth.user();
    return { name: u?.schoolName ?? u?.username ?? '', code: u?.schoolCode ?? u?.username ?? '' };
  });
  protected readonly user = computed(() => {
    const u = this.auth.user();
    // `username` in the login response is the person's display name; the login ID is the org user id.
    return { name: u?.username ?? '', role: u?.roles?.[0] ?? u?.userType ?? '' };
  });
  protected readonly notifications = PLACEHOLDER_NOTIFICATIONS;

  protected readonly openMenu = signal<MenuName | null>(null);
  protected readonly searchOpen = signal(false);

  protected logout(): void {
    this.openMenu.set(null);
    this.auth.logout().subscribe(() => void this.router.navigate(['/login']));
  }

  protected initial(name: string): string {
    return name.trim().charAt(0).toUpperCase();
  }

  protected toggle(menu: MenuName): void {
    this.openMenu.update((current) => (current === menu ? null : menu));
  }

  @HostListener('document:click', ['$event'])
  protected onDocumentClick(event: MouseEvent): void {
    if (!this.host.nativeElement.contains(event.target as Node)) this.openMenu.set(null);
  }

  @HostListener('document:keydown.escape')
  protected onEscape(): void {
    this.openMenu.set(null);
    this.searchOpen.set(false);
  }
}
