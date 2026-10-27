import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute } from '@angular/router';
import { finalize } from 'rxjs';
import { CardComponent, IconComponent, PageHeaderComponent } from '../../../../shared/components';
import { Notice } from '../../models/notice.model';
import { NoticeService } from '../../services/notice.service';
import { audienceLabel, publishedLabel, snippet } from '../../utils/notice-format';

/**
 * Every school role: the notices addressed to the signed-in user, newest first. Opening one shows it in full and marks it read
 * (the server records it); `?open=<id>` from the bell opens that notice straight away.
 */
@Component({
  selector: 'app-my-notices',
  imports: [CardComponent, IconComponent, PageHeaderComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './my-notices.component.scss',
  template: `
    <app-page-header title="My Notices" subtitle="Announcements addressed to you" icon="bell" [breadcrumbs]="breadcrumbs" />

    @if (loading()) {
      <app-card><p class="note" role="status">Loading notices…</p></app-card>
    } @else if (loadFailed()) {
      <app-card><p class="note" role="alert">Your notices could not be loaded.</p></app-card>
    } @else {
      <div class="toolbar">
        <span role="status">{{ unreadTotal() }} unread</span>
        <label><input type="checkbox" [checked]="unreadOnly()" (change)="setUnreadOnly($any($event.target).checked)" /> Unread only</label>
      </div>
      @if (items().length === 0) {
        <app-card>
          <div class="empty">
            <app-icon name="inbox" [size]="36" />
            <p>{{ unreadOnly() ? 'You have no unread notices.' : 'There are no notices for you.' }}</p>
          </div>
        </app-card>
      } @else {
        <ul class="cards" aria-label="Notices">
          @for (n of items(); track n.noticeId) {
            <li class="card" [class.unread]="!n.isRead">
              <button type="button" class="head" [attr.aria-expanded]="openId() === n.noticeId" (click)="toggle(n)">
                <h3 class="title">{{ n.title }} @if (!n.isRead) { <span class="new">New</span> }</h3>
                <span class="when">{{ published(n.publishedAt) }}</span>
              </button>
              @if (openId() === n.noticeId) {
                <p class="body">{{ n.body }}</p>
              } @else {
                <p class="preview">{{ preview(n.body) }}</p>
              }
              <div class="meta">{{ n.createdByName ?? 'School' }} · {{ audience(n.audience) }}</div>
            </li>
          }
        </ul>
      }
    }
  `,
})
export class MyNoticesComponent implements OnInit {
  private readonly service = inject(NoticeService);
  private readonly route = inject(ActivatedRoute);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly breadcrumbs = [{ label: 'My Notices' }];
  protected readonly published = publishedLabel;
  protected readonly preview = snippet;
  protected readonly audience = audienceLabel;
  protected readonly items = signal<Notice[]>([]);
  protected readonly loading = signal(true);
  protected readonly loadFailed = signal(false);
  protected readonly unreadOnly = signal(false);
  protected readonly openId = signal<number | null>(null);
  protected readonly unreadTotal = this.service.unreadCount;

  ngOnInit(): void {
    this.load(Number(this.route.snapshot.queryParamMap.get('open')) || null);
  }

  protected setUnreadOnly(on: boolean): void {
    this.unreadOnly.set(on);
    this.load(null);
  }

  private load(autoOpen: number | null): void {
    this.loading.set(true);
    this.loadFailed.set(false);
    this.service
      .my({ unreadOnly: this.unreadOnly() })
      .pipe(finalize(() => this.loading.set(false)), takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (list) => {
          this.items.set(list);
          this.service.refreshUnread();
          const target = autoOpen ? list.find((n) => n.noticeId === autoOpen) : undefined;
          if (target) this.toggle(target);
        },
        error: () => this.loadFailed.set(true),
      });
  }

  /** Opening a notice (not closing it) asks the server for it, which records the read. */
  protected toggle(n: Notice): void {
    if (this.openId() === n.noticeId) {
      this.openId.set(null);
      return;
    }
    this.openId.set(n.noticeId);
    if (n.isRead) return;
    this.service.open(n.noticeId).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      // it stays in the list (even in the unread-only view) until the next load, so it does not vanish under the cursor
      next: (opened) => this.items.update((l) => l.map((x) => (x.noticeId === opened.noticeId ? { ...x, isRead: true } : x))),
      error: () => this.openId.set(null),
    });
  }
}
