import { ChangeDetectionStrategy, Component, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { finalize } from 'rxjs';
import { ToastService } from '../../../../core/services/toast.service';
import { ButtonComponent, IconComponent } from '../../../../shared/components';
import { SaveYearSchedulesRequest, YearSchedules } from '../../models/academic-setup.model';
import { AcademicSetupService } from '../../services/academic-setup.service';

export const DAY_NAMES = ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'];

/** One line of the screen: select values are strings, '' = every class / every day. */
export interface AssignmentDraft {
  classId: string;
  day: string;
  scheduleId: string;
}

export const toAssignmentDrafts = (y: YearSchedules): AssignmentDraft[] =>
  y.rows.map((r) => ({ classId: r.classId != null ? String(r.classId) : '', day: r.dayOfWeek != null ? String(r.dayOfWeek) : '', scheduleId: String(r.scheduleId) }));

export const toAssignmentRequest = (rows: AssignmentDraft[]): SaveYearSchedulesRequest => ({
  rows: rows.filter((r) => r.scheduleId !== '').map((r) => ({ classId: r.classId === '' ? null : Number(r.classId), dayOfWeek: r.day === '' ? null : Number(r.day), scheduleId: Number(r.scheduleId) })),
});

/** The same class + day twice (the server refuses it too). */
export function duplicateKeys(rows: AssignmentDraft[]): Set<string> {
  const seen = new Set<string>();
  const dup = new Set<string>();
  for (const r of rows) {
    const k = `${r.classId}|${r.day}`;
    if (seen.has(k)) dup.add(k);
    seen.add(k);
  }
  return dup;
}

/**
 * Academic Setup, Schedules (S4), part 2: which bell schedule each class follows on each day of the chosen year. A class and day with no line follows the
 * default schedule. Lines are most specific first: class + day, then class, then day, then everything.
 */
@Component({
  selector: 'app-schedule-assignments',
  imports: [ButtonComponent, IconComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './schedule-assignments.component.scss',
  templateUrl: './schedule-assignments.component.html',
})
export class ScheduleAssignmentsComponent {
  private readonly setup = inject(AcademicSetupService);
  private readonly toast = inject(ToastService);

  readonly yearId = input.required<number>();
  readonly readOnly = input(false);

  protected readonly days = DAY_NAMES;
  protected readonly loading = signal(false);
  protected readonly failed = signal(false);
  protected readonly saving = signal(false);
  protected readonly data = signal<YearSchedules | null>(null);
  protected readonly rows = signal<AssignmentDraft[]>([]);
  private readonly savedKey = signal('');

  protected readonly locked = computed(() => this.readOnly() || this.data()?.isClosed === true);
  protected readonly dirty = computed(() => JSON.stringify(this.rows()) !== this.savedKey());
  protected readonly duplicates = computed(() => duplicateKeys(this.rows()));
  protected readonly defaultName = computed(() => this.data()?.schedules.find((s) => s.isDefault)?.scheduleName ?? 'the default schedule');
  protected readonly onlyOneSchedule = computed(() => (this.data()?.schedules.length ?? 0) <= 1);

  constructor() {
    effect(() => {
      const id = this.yearId();
      untracked(() => this.load(id));
    });
  }

  protected load(id: number): void {
    this.loading.set(true);
    this.failed.set(false);
    this.setup.getYearSchedules(id).pipe(finalize(() => this.loading.set(false))).subscribe({
      next: (y) => this.apply(y),
      error: () => { this.data.set(null); this.failed.set(true); },
    });
  }

  private apply(y: YearSchedules): void {
    this.data.set(y);
    const drafts = toAssignmentDrafts(y);
    this.rows.set(drafts);
    this.savedKey.set(JSON.stringify(drafts));
  }

  protected isDuplicate(r: AssignmentDraft): boolean {
    return this.duplicates().has(`${r.classId}|${r.day}`);
  }

  protected add(): void {
    const first = this.data()?.schedules.find((s) => !s.isDefault) ?? this.data()?.schedules[0];
    this.rows.update((list) => [...list, { classId: '', day: '', scheduleId: first ? String(first.scheduleId) : '' }]);
  }

  protected patch(index: number, p: Partial<AssignmentDraft>): void {
    this.rows.update((list) => list.map((r, i) => (i === index ? { ...r, ...p } : r)));
  }

  protected remove(index: number): void {
    this.rows.update((list) => list.filter((_, i) => i !== index));
  }

  protected reset(): void {
    const y = this.data();
    if (y) this.apply(y);
  }

  protected save(): void {
    const y = this.data();
    if (!y || this.duplicates().size > 0) return;
    this.saving.set(true);
    this.setup.saveYearSchedules(y.academicYearId, toAssignmentRequest(this.rows())).pipe(finalize(() => this.saving.set(false))).subscribe({
      next: (saved) => { this.apply(saved); this.toast.success(`Saved the schedules of ${saved.yearName}.`); },
      error: () => undefined,   // e.g. "would leave timetable slots on a schedule the class no longer follows": already toasted
    });
  }
}
