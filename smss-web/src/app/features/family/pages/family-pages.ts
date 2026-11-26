import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { finalize } from 'rxjs';
import { AuthService } from '../../../core/services/auth.service';
import { FamilyEvent, FamilyNotice } from '../models/family.model';
import { FamilyService } from '../services/family.service';

const STYLES = [`
  :host { display: flex; flex-direction: column; gap: var(--space-4); }
  h1 { margin: 0; font-size: var(--text-xl); }
  .alert { padding: var(--space-3) var(--space-4); border: 1px solid var(--color-danger); border-radius: var(--radius-md); background: var(--color-danger-soft); color: var(--color-danger); }
  .alert button { margin-left: var(--space-2); text-decoration: underline; background: none; border: 0; color: inherit; cursor: pointer; }
  .empty { color: var(--color-text-muted); padding: var(--space-5); text-align: center; background: var(--color-surface, #fff); border-radius: var(--radius-lg); }
  ul { list-style: none; margin: 0; padding: 0; display: flex; flex-direction: column; gap: var(--space-2); }
  li { padding: var(--space-3) var(--space-4); border: 1px solid var(--color-border); border-radius: var(--radius-lg); background: var(--color-surface, #fff); }
  li.unread { border-left: 4px solid var(--color-primary-700, var(--color-primary)); }
  li button.head { width: 100%; text-align: left; background: none; border: 0; cursor: pointer; padding: 0; font: inherit; }
  .meta { color: var(--color-text-muted); font-size: var(--text-sm); }
  .body { white-space: pre-wrap; margin-top: var(--space-2); }
  .dot { display: inline-block; width: 10px; height: 10px; border-radius: 50%; margin-right: var(--space-2); }
  .bar { display: flex; align-items: center; justify-content: space-between; gap: var(--space-2); }
  .bar button, .btn { padding: var(--space-2) var(--space-3); border-radius: var(--radius-md); border: 1px solid var(--color-border); background: var(--color-surface, #fff); cursor: pointer; text-decoration: none; color: inherit; }
`];

@Component({
  selector: 'app-family-notices',
  imports: [DatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styles: STYLES,
  template: `
    <h1>School Notifications</h1>
    @if (loading()) { <p role="status" data-state="loading">Loading…</p> }
    @else if (failed()) { <p class="alert" role="alert" data-state="failed">The notices could not be loaded. <button type="button" (click)="load()" data-action="retry">Try again</button></p> }
    @else if (list().length === 0) { <p class="empty" data-state="empty">There are no notices.</p> }
    @else {
      <ul data-list="notices">
        @for (n of list(); track n.noticeId) {
          <li [class.unread]="!n.isRead" [attr.data-notice]="n.noticeId">
            <button type="button" class="head" (click)="toggle(n)" [attr.aria-expanded]="openId() === n.noticeId">
              <strong>{{ n.title }}</strong><div class="meta">{{ n.publishedAt | date: 'd MMM yyyy' }}@if (n.postedBy) { · {{ n.postedBy }} }@if (!n.isRead) { · <strong>New</strong> }</div>
            </button>
            @if (openId() === n.noticeId) { <div class="body" data-field="body">{{ n.body }}</div> }
          </li>
        }
      </ul>
    }
  `,
})
export class FamilyNoticesComponent implements OnInit {
  private readonly api = inject(FamilyService);
  protected readonly list = signal<FamilyNotice[]>([]);
  protected readonly loading = signal(true);
  protected readonly failed = signal(false);
  protected readonly openId = signal<number | null>(null);

  ngOnInit(): void { this.load(); }

  protected load(): void {
    this.loading.set(true); this.failed.set(false);
    this.api.notices().pipe(finalize(() => this.loading.set(false))).subscribe({ next: (l) => this.list.set(l), error: () => this.failed.set(true) });
  }

  /** Opening a notice marks it read (the server does that); the unread badge is refreshed. */
  protected toggle(n: FamilyNotice): void {
    if (this.openId() === n.noticeId) { this.openId.set(null); return; }
    this.openId.set(n.noticeId);
    if (!n.isRead) this.api.notice(n.noticeId).subscribe({
      next: () => { this.list.update((l) => l.map((x) => (x.noticeId === n.noticeId ? { ...x, isRead: true } : x))); this.api.refreshUnread(); },
      error: () => undefined,
    });
  }
}

@Component({
  selector: 'app-family-calendar',
  imports: [DatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styles: STYLES,
  template: `
    <h1>Academic Calendar</h1>
    <div class="bar">
      <button type="button" (click)="move(-1)" data-action="prev" aria-label="Previous month">‹</button>
      <strong data-field="month">{{ monthStart() | date: 'MMMM yyyy' }}</strong>
      <button type="button" (click)="move(1)" data-action="next" aria-label="Next month">›</button>
    </div>
    @if (loading()) { <p role="status" data-state="loading">Loading…</p> }
    @else if (failed()) { <p class="alert" role="alert" data-state="failed">The calendar could not be loaded. <button type="button" (click)="load()" data-action="retry">Try again</button></p> }
    @else if (events().length === 0) { <p class="empty" data-state="empty">Nothing is planned this month.</p> }
    @else {
      <ul data-list="events">
        @for (e of events(); track e.eventId) {
          <li [attr.data-event]="e.eventId">
            <span class="dot" [style.background]="e.colorHex"></span><strong>{{ e.title }}</strong>
            <div class="meta">{{ e.startDate | date: 'EEE d MMM' }}@if (e.endDate !== e.startDate) { to {{ e.endDate | date: 'EEE d MMM' }} } · {{ e.categoryName }}@if (!e.isAllDay && e.startTime) { · {{ e.startTime.slice(0, 5) }}@if (e.endTime) { – {{ e.endTime.slice(0, 5) }} } }</div>
            @if (e.description) { <div>{{ e.description }}</div> }
          </li>
        }
      </ul>
    }
  `,
})
export class FamilyCalendarComponent implements OnInit {
  private readonly api = inject(FamilyService);
  protected readonly events = signal<FamilyEvent[]>([]);
  protected readonly loading = signal(true);
  protected readonly failed = signal(false);
  protected readonly monthStart = signal(new Date(new Date().getFullYear(), new Date().getMonth(), 1));

  ngOnInit(): void { this.load(); }

  protected move(by: number): void {
    const d = this.monthStart();
    this.monthStart.set(new Date(d.getFullYear(), d.getMonth() + by, 1));
    this.load();
  }

  protected load(): void {
    const d = this.monthStart();
    const iso = (x: Date) => `${x.getFullYear()}-${String(x.getMonth() + 1).padStart(2, '0')}-${String(x.getDate()).padStart(2, '0')}`;
    this.loading.set(true); this.failed.set(false);
    this.api.calendar(iso(d), iso(new Date(d.getFullYear(), d.getMonth() + 1, 0))).pipe(finalize(() => this.loading.set(false)))
      .subscribe({ next: (l) => this.events.set(l), error: () => this.failed.set(true) });
  }
}

@Component({
  selector: 'app-family-account',
  imports: [RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styles: STYLES,
  template: `
    <h1>Your account</h1>
    <ul>
      <li><div class="meta">Name</div><strong data-field="name">{{ user()?.displayName }}</strong></li>
      <li><div class="meta">Your login ID</div><strong data-field="login-id">{{ user()?.orgUserId }}</strong></li>
      <li><div class="meta">School</div><strong>{{ user()?.schoolName }}</strong></li>
    </ul>
    <a class="btn" routerLink="/family/account/change-password" data-link="change-password">Change password</a>
    <button type="button" class="btn" (click)="signOut()" data-action="sign-out">Sign out</button>
  `,
})
export class FamilyAccountComponent {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  protected readonly user = this.auth.user;

  protected signOut(): void { this.auth.logout().subscribe(() => void this.router.navigate(['/login'])); }
}
