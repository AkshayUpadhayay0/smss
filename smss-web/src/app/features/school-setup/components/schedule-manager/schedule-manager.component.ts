import { ChangeDetectionStrategy, Component, OnInit, computed, inject, input, output, signal } from '@angular/core';
import { finalize } from 'rxjs';
import { ConfirmDialogService } from '../../../../core/services/confirm-dialog.service';
import { ToastService } from '../../../../core/services/toast.service';
import { ButtonComponent, IconComponent } from '../../../../shared/components';
import { Schedule, ScheduleSlot, ScheduleSlotRequest } from '../../models/academic-setup.model';
import { PeriodScheduleService } from '../../services/period-schedule.service';

/** "09:00:00" -> "09:00" */
export const hhmm = (t: string): string => t.slice(0, 5);

/** What the add / edit slot form holds. */
export interface SlotForm {
  slotType: 'PERIOD' | 'BREAK';
  periodNumber: string;
  label: string;
  startTime: string;
  endTime: string;
}

export const emptySlotForm = (): SlotForm => ({ slotType: 'PERIOD', periodNumber: '', label: '', startTime: '', endTime: '' });

export function formOf(s: ScheduleSlot): SlotForm {
  return { slotType: s.slotType, periodNumber: s.periodNumber != null ? String(s.periodNumber) : '', label: s.label ?? '', startTime: hhmm(s.startTime), endTime: hhmm(s.endTime) };
}

/** The first thing wrong with the form, or null. The server checks everything again (overlap, duplicate number, ...). */
export function slotFormError(f: SlotForm): string | null {
  if (!f.startTime || !f.endTime) return 'Choose a start and an end time.';
  if (f.endTime <= f.startTime) return 'The end time must be after the start time.';
  if (f.slotType === 'PERIOD') {
    const n = Number(f.periodNumber);
    if (f.periodNumber.trim() === '' || !Number.isInteger(n) || n < 1 || n > 30) return 'A period needs a number from 1 to 30.';
  } else if (f.label.trim() === '') return 'A break needs a label, for example Lunch.';
  return null;
}

export function toSlotRequest(f: SlotForm): ScheduleSlotRequest {
  return {
    slotType: f.slotType,
    periodNumber: f.slotType === 'PERIOD' ? Number(f.periodNumber) : null,
    label: f.label.trim() === '' ? null : f.label.trim(),
    startTime: f.startTime,
    endTime: f.endTime,
  };
}

export const slotName = (s: ScheduleSlot): string => (s.slotType === 'BREAK' ? (s.label ?? 'Break') : `Period ${s.periodNumber}${s.label ? ' (' + s.label + ')' : ''}`);

/**
 * Academic Setup, Schedules (S4), part 1: the school's bell schedules and their periods and breaks. A school has one default schedule; others
 * ("Winter", "Saturday") are assigned to classes and days in the year part below. Slots stay in clock-time order; a slot in use can't be removed.
 */
@Component({
  selector: 'app-schedule-manager',
  imports: [ButtonComponent, IconComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './schedule-manager.component.scss',
  templateUrl: './schedule-manager.component.html',
})
export class ScheduleManagerComponent implements OnInit {
  private readonly api = inject(PeriodScheduleService);
  private readonly toast = inject(ToastService);
  private readonly confirm = inject(ConfirmDialogService);

  readonly readOnly = input(false);
  /** Something about the schedules changed (so the year assignments below should reload their choices). */
  readonly changed = output<void>();

  protected readonly schedules = signal<Schedule[]>([]);
  protected readonly loading = signal(true);
  protected readonly failed = signal(false);
  protected readonly busy = signal(false);

  // new schedule
  protected readonly newName = signal('');
  protected readonly copyFrom = signal<string>('');
  // rename
  protected readonly renaming = signal<number | null>(null);
  protected readonly renameText = signal('');
  // slot form: one open at a time. editing = periodId being edited (null = adding)
  protected readonly formFor = signal<number | null>(null);
  protected readonly editing = signal<number | null>(null);
  protected readonly form = signal<SlotForm>(emptySlotForm());

  protected readonly formError = computed(() => slotFormError(this.form()));
  protected readonly hasSchedules = computed(() => this.schedules().length > 0);

  ngOnInit(): void {
    this.load();
  }

  protected load(): void {
    this.loading.set(true);
    this.failed.set(false);
    this.api.list().pipe(finalize(() => this.loading.set(false))).subscribe({
      next: (list) => { this.schedules.set(list); this.changed.emit(); },
      error: () => this.failed.set(true),   // already toasted by the interceptor
    });
  }

  private replace(updated: Schedule): void {
    this.schedules.update((list) => list.map((s) => (s.scheduleId === updated.scheduleId ? updated : s)));
    this.changed.emit();
  }

  // ---------------- schedules
  protected create(): void {
    const name = this.newName().trim();
    if (!name || this.busy()) return;
    this.busy.set(true);
    this.api.create(name, this.copyFrom() ? Number(this.copyFrom()) : null).pipe(finalize(() => this.busy.set(false))).subscribe({
      next: (s) => { this.toast.success(`Schedule "${s.scheduleName}" created.`); this.newName.set(''); this.copyFrom.set(''); this.load(); },
      error: () => undefined,
    });
  }

  protected startRename(s: Schedule): void {
    this.renaming.set(s.scheduleId);
    this.renameText.set(s.scheduleName);
  }

  protected saveRename(s: Schedule): void {
    const name = this.renameText().trim();
    if (!name || this.busy()) return;
    this.busy.set(true);
    this.api.rename(s.scheduleId, name).pipe(finalize(() => this.busy.set(false))).subscribe({
      next: () => { this.renaming.set(null); this.load(); },
      error: () => undefined,
    });
  }

  protected makeDefault(s: Schedule): void {
    this.busy.set(true);
    this.api.setDefault(s.scheduleId).pipe(finalize(() => this.busy.set(false))).subscribe({
      next: () => { this.toast.success(`"${s.scheduleName}" is now the default schedule.`); this.load(); },
      error: () => undefined,   // e.g. "would leave timetable slots on periods of another schedule"
    });
  }

  protected async remove(s: Schedule): Promise<void> {
    if (s.deleteBlockedReason) return;
    const ok = await this.confirm.confirm({ title: 'Delete schedule', message: `Delete the schedule "${s.scheduleName}" and its ${s.slots.length} periods and breaks? This can't be undone.`, confirmText: 'Delete', variant: 'danger' });
    if (!ok) return;
    this.busy.set(true);
    this.api.remove(s.scheduleId).pipe(finalize(() => this.busy.set(false))).subscribe({
      next: () => { this.toast.success('Schedule deleted.'); this.load(); },
      error: () => undefined,
    });
  }

  // ---------------- slots
  protected openAdd(s: Schedule): void {
    this.formFor.set(s.scheduleId);
    this.editing.set(null);
    this.form.set(emptySlotForm());
  }

  protected openEdit(s: Schedule, slot: ScheduleSlot): void {
    this.formFor.set(s.scheduleId);
    this.editing.set(slot.periodId);
    this.form.set(formOf(slot));
  }

  protected closeForm(): void {
    this.formFor.set(null);
    this.editing.set(null);
  }

  protected patch(p: Partial<SlotForm>): void {
    this.form.update((f) => ({ ...f, ...p }));
  }

  protected saveSlot(s: Schedule): void {
    if (this.formError() || this.busy()) return;
    this.busy.set(true);
    const body = toSlotRequest(this.form());
    const call = this.editing() != null ? this.api.updateSlot(s.scheduleId, this.editing()!, body) : this.api.addSlot(s.scheduleId, body);
    call.pipe(finalize(() => this.busy.set(false))).subscribe({
      next: (updated) => { this.replace(updated); this.closeForm(); this.toast.success(this.editing() != null ? 'Slot updated.' : 'Slot added.'); },
      error: () => undefined,   // overlap / duplicate number: the server's reason is already toasted
    });
  }

  protected toggle(s: Schedule, slot: ScheduleSlot): void {
    this.api.toggleSlot(s.scheduleId, slot.periodId).subscribe({ next: (updated) => this.replace(updated), error: () => undefined });
  }

  protected async removeSlot(s: Schedule, slot: ScheduleSlot): Promise<void> {
    const ok = await this.confirm.confirm({ title: 'Delete slot', message: `Delete ${slotName(slot)} from "${s.scheduleName}"?`, confirmText: 'Delete', variant: 'danger' });
    if (!ok) return;
    this.api.removeSlot(s.scheduleId, slot.periodId).subscribe({ next: (updated) => this.replace(updated), error: () => undefined });
  }

  protected readonly name = slotName;
  protected readonly time = hhmm;
}
