import { ChangeDetectionStrategy, Component, DestroyRef, ElementRef, HostListener, OnInit, computed, inject, output, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { filter } from 'rxjs';
import { NavigationEnd, Router, RouterLink } from '@angular/router';
import { AuthService } from '../../../core/services/auth.service';
import { Notice } from '../../../features/notices/models/notice.model';
import { NoticeService } from '../../../features/notices/services/notice.service';
import { publishedLabel, snippet } from '../../../features/notices/utils/notice-format';
import { IconComponent } from '../../../shared/components/icon/icon.component';

type MenuName = 'org' | 'notifications' | 'user';

/** How many unread notices the bell lists; the rest are one click away on My Notices. */
const BELL_LIMIT = 5;

@Component({
  selector: 'app-topbar',
  imports: [RouterLink, IconComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './topbar.component.scss',
  templateUrl: './topbar.component.html',
})
export class TopbarComponent implements OnInit {
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly notices = inject(NoticeService);
  private readonly destroyRef = inject(DestroyRef);

  readonly menuToggle = output<void>();

  /** Signed-in user's organization (their school) and identity, from the login session. */
  protected readonly org = computed(() => {
    const u = this.auth.user();
    return { name: u?.schoolName ?? u?.displayName ?? '', code: u?.schoolCode ?? u?.orgUserId ?? '' };
  });
  protected readonly user = computed(() => {
    const u = this.auth.user();
    // displayName is the person's name; the login ID is orgUserId.
    return { name: u?.displayName ?? '', role: u?.roles?.[0] ?? u?.userType ?? '' };
  });
  /** The bell: the unread count (shared with My Notices) and the latest unread notices, loaded when the dropdown opens. Only users of a school have notices. */
  protected readonly unreadCount = this.notices.unreadCount;
  protected readonly latestUnread = signal<Notice[]>([]);
  protected readonly bellLoaded = signal(false);
  protected readonly hasSchool = computed(() => !!this.auth.user()?.schoolId);
  protected readonly published = publishedLabel;
  protected readonly preview = (body: string) => snippet(body, 70);

  protected readonly openMenu = signal<MenuName | null>(null);
  protected readonly searchOpen = signal(false);

  ngOnInit(): void {
    if (!this.auth.user()?.schoolId) return;
    this.notices.refreshUnread();
    this.router.events.pipe(filter((e) => e instanceof NavigationEnd), takeUntilDestroyed(this.destroyRef)).subscribe(() => this.notices.refreshUnread());
  }

  protected logout(): void {
    this.openMenu.set(null);
    this.auth.logout().subscribe(() => void this.router.navigate(['/login']));
  }

  protected initial(name: string): string {
    return name.trim().charAt(0).toUpperCase();
  }

  protected toggle(menu: MenuName): void {
    this.openMenu.update((current) => (current === menu ? null : menu));
    if (menu === 'notifications' && this.openMenu() === 'notifications' && this.auth.user()?.schoolId) this.loadLatest();
  }

  private loadLatest(): void {
    this.bellLoaded.set(false);
    this.notices.my({ unreadOnly: true }).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (list) => {
        this.latestUnread.set(list.slice(0, BELL_LIMIT));
        this.bellLoaded.set(true);
      },
      error: () => this.bellLoaded.set(true),
    });
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
