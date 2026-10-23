import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { finalize } from 'rxjs';
import { ButtonComponent, CardComponent, IconComponent, PageHeaderComponent } from '../../../../shared/components';
import { Homework } from '../../models/homework.model';
import { HomeworkService } from '../../services/homework.service';
import { DueState, day, dueLabel, dueState, saveBlob, todayYmd } from '../../utils/homework-format';

interface HomeworkCard extends Homework {
  state: DueState;
  label: string;
}

/** A student's read-only list: their own current section's homework, every subject, soonest due first. Nothing can be submitted from here. */
@Component({
  selector: 'app-my-homework',
  imports: [ButtonComponent, CardComponent, IconComponent, PageHeaderComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './my-homework.component.scss',
  template: `
    <app-page-header title="My Homework" subtitle="Homework for your class, soonest due first" icon="graduation-cap" [breadcrumbs]="breadcrumbs" />

    @if (loading()) {
      <app-card><p class="note" role="status">Loading homework…</p></app-card>
    } @else if (loadFailed()) {
      <app-card><p class="note" role="alert">Your homework could not be loaded.</p></app-card>
    } @else if (cards().length === 0) {
      <app-card>
        <div class="empty">
          <app-icon name="inbox" [size]="36" />
          <p>No homework has been set for your class.</p>
        </div>
      </app-card>
    } @else {
      <p class="summary" role="status">
        {{ overdueCount() }} overdue · {{ cards().length - overdueCount() }} upcoming
      </p>
      <ul class="cards" aria-label="Homework">
        @for (c of cards(); track c.homeworkId) {
          <li class="card" [class.overdue]="c.state === 'overdue'" [class.today]="c.state === 'today'" [class.upcoming]="c.state === 'upcoming'">
            <div class="top">
              <span class="subject">{{ c.subjectName }}</span>
              <span class="chip" [class]="'chip ' + c.state">{{ c.label }}</span>
            </div>
            <h3 class="title">{{ c.title }}</h3>
            @if (c.description) {
              <p class="description">{{ c.description }}</p>
            }
            <div class="meta">
              <span>Due {{ fmt(c.dueDate) }}</span>
              <span>Set {{ fmt(c.assignedDate) }} by {{ c.teacherName }}</span>
              @if (c.hasAttachment) {
                <app-button variant="secondary" size="sm" [disabled]="busyId() === c.homeworkId" (click)="download(c)">Download attachment</app-button>
              }
            </div>
          </li>
        }
      </ul>
    }
  `,
})
export class MyHomeworkComponent implements OnInit {
  private readonly service = inject(HomeworkService);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly breadcrumbs = [{ label: 'My Homework' }];
  protected readonly fmt = day;
  protected readonly items = signal<Homework[]>([]);
  protected readonly loading = signal(true);
  protected readonly loadFailed = signal(false);
  protected readonly busyId = signal<number | null>(null);

  /** Sorted by due date (the server sorts too); overdue / due today / upcoming is computed here against today's date. */
  protected readonly cards = computed<HomeworkCard[]>(() => {
    const today = todayYmd();
    return [...this.items()]
      .sort((a, b) => a.dueDate.localeCompare(b.dueDate) || a.homeworkId - b.homeworkId)
      .map((h) => ({ ...h, state: dueState(h.dueDate, today), label: dueLabel(h.dueDate, today) }));
  });
  protected readonly overdueCount = computed(() => this.cards().filter((c) => c.state === 'overdue').length);

  ngOnInit(): void {
    this.service
      .studentMy()
      .pipe(finalize(() => this.loading.set(false)), takeUntilDestroyed(this.destroyRef))
      .subscribe({ next: (list) => this.items.set(list), error: () => this.loadFailed.set(true) });
  }

  protected download(h: Homework): void {
    if (this.busyId() !== null) return;
    this.busyId.set(h.homeworkId);
    this.service
      .downloadAttachment(h.homeworkId)
      .pipe(finalize(() => this.busyId.set(null)), takeUntilDestroyed(this.destroyRef))
      .subscribe({ next: (blob) => saveBlob(blob, h.attachmentFileName ?? `homework-${h.homeworkId}`), error: () => undefined });
  }
}
