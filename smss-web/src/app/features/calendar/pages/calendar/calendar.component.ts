import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { finalize } from 'rxjs';
import { AuthService } from '../../../../core/services/auth.service';
import { ConfirmDialogService } from '../../../../core/services/confirm-dialog.service';
import { ToastService } from '../../../../core/services/toast.service';
import { ButtonComponent, CardComponent, IconComponent, ModalComponent, PageHeaderComponent } from '../../../../shared/components';
import { EventFormModalComponent } from '../../components/event-form-modal/event-form-modal.component';
import { EVENT_MANAGER_ROLES, EventCategory, SchoolEvent } from '../../models/calendar.model';
import { SchoolEventService } from '../../services/school-event.service';
import { CalendarDay, MONTH_NAMES, WEEKDAYS, chipLabel, describeWhen, eventsOn, fromYmd, monthGrid, ymd } from '../../utils/calendar-dates';

const MAX_CHIPS = 3;

/** The school calendar: a month view with an upcoming list beside it. Everyone reads; School Admin / Principal add, edit and delete. */
@Component({
  selector: 'app-calendar',
  imports: [ButtonComponent, CardComponent, EventFormModalComponent, IconComponent, ModalComponent, PageHeaderComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './calendar.component.scss',
  templateUrl: './calendar.component.html',
})
export class CalendarComponent implements OnInit {
  private readonly service = inject(SchoolEventService);
  private readonly auth = inject(AuthService);
  private readonly toast = inject(ToastService);
  private readonly confirmDialog = inject(ConfirmDialogService);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly breadcrumbs = [{ label: 'Calendar' }];
  protected readonly weekdays = WEEKDAYS;
  protected readonly maxChips = MAX_CHIPS;
  protected readonly describeWhen = describeWhen;
  protected readonly chipLabel = chipLabel;

  protected readonly canManage = computed(() => this.auth.currentUserRoles().some((r) => (EVENT_MANAGER_ROLES as readonly string[]).includes(r)));

  /** yyyy-MM-dd of today (the browser's local date). */
  protected readonly today = ymd(new Date());
  private readonly now = new Date();
  protected readonly year = signal(this.now.getFullYear());
  protected readonly month = signal(this.now.getMonth());
  protected readonly selected = signal<string>(this.today);

  protected readonly title = computed(() => `${MONTH_NAMES[this.month()]} ${this.year()}`);
  protected readonly weeks = computed(() => monthGrid(this.year(), this.month(), this.today));
  protected readonly events = signal<SchoolEvent[]>([]);
  protected readonly upcoming = signal<SchoolEvent[]>([]);
  protected readonly categories = signal<EventCategory[]>([]);
  protected readonly loading = signal(true);
  protected readonly loadFailed = signal(false);
  protected readonly upcomingFailed = signal(false);

  protected readonly selectedEvents = computed(() => eventsOn(this.selected(), this.events()));
  protected readonly selectedText = computed(() => fromYmd(this.selected()).toLocaleDateString('en-GB', { weekday: 'long', day: 'numeric', month: 'long', year: 'numeric' }));

  /** The event whose details are open, and the add / edit form. */
  protected readonly detail = signal<SchoolEvent | null>(null);
  protected readonly form = signal<{ event: SchoolEvent | null; date: string } | null>(null);
  protected readonly busy = signal(false);

  ngOnInit(): void {
    this.service.categories().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({ next: (c) => this.categories.set(c), error: () => undefined });
    this.loadMonth();
    this.loadUpcoming();
  }

  protected dayEvents(day: CalendarDay): SchoolEvent[] {
    return eventsOn(day.date, this.events());
  }

  /** The whole 6-week grid is fetched, so the days shown from the neighbouring months have their events too. */
  protected loadMonth(): void {
    const weeks = this.weeks();
    const from = weeks[0][0].date;
    const to = weeks[5][6].date;
    this.loading.set(true);
    this.loadFailed.set(false);
    this.service
      .range(from, to)
      .pipe(finalize(() => this.loading.set(false)), takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (list) => this.events.set(list),
        error: () => this.loadFailed.set(true), // already toasted by the interceptor
      });
  }

  protected loadUpcoming(): void {
    this.upcomingFailed.set(false);
    this.service
      .upcoming(5)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({ next: (l) => this.upcoming.set(l), error: () => this.upcomingFailed.set(true) });
  }

  protected step(delta: number): void {
    const d = new Date(this.year(), this.month() + delta, 1);
    this.year.set(d.getFullYear());
    this.month.set(d.getMonth());
    this.loadMonth();
  }

  protected goToday(): void {
    const d = new Date();
    this.year.set(d.getFullYear());
    this.month.set(d.getMonth());
    this.selected.set(this.today);
    this.loadMonth();
  }

  protected select(day: CalendarDay): void {
    this.selected.set(day.date);
    if (!day.inMonth) {
      const d = fromYmd(day.date);
      this.year.set(d.getFullYear());
      this.month.set(d.getMonth());
      this.loadMonth();
    }
  }

  protected open(event: SchoolEvent, e?: Event): void {
    e?.stopPropagation();
    this.detail.set(event);
  }

  protected add(date?: string): void {
    this.form.set({ event: null, date: date ?? this.selected() });
  }

  protected edit(event: SchoolEvent): void {
    this.detail.set(null);
    this.form.set({ event, date: event.startDate });
  }

  protected onSaved(): void {
    this.form.set(null);
    this.loadMonth();
    this.loadUpcoming();
  }

  protected async remove(event: SchoolEvent): Promise<void> {
    const ok = await this.confirmDialog.confirm({
      title: 'Delete event?',
      message: `"${event.eventTitle}" will be removed from the calendar for everyone.`,
      confirmText: 'Delete',
      variant: 'danger',
    });
    if (!ok || this.busy()) return;
    this.busy.set(true);
    this.service
      .delete(event.eventId)
      .pipe(finalize(() => this.busy.set(false)), takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.detail.set(null);
          this.toast.success('Event deleted.');
          this.loadMonth();
          this.loadUpcoming();
        },
        error: () => undefined,
      });
  }
}
