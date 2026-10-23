import { ChangeDetectionStrategy, Component, OnInit, computed, inject, input, output, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { finalize } from 'rxjs';
import { ToastService } from '../../../../core/services/toast.service';
import { ButtonComponent, InputComponent, ModalComponent, SelectComponent, SelectOption } from '../../../../shared/components';
import { TimetableEntry, TimetableLookups } from '../../models/timetable.model';
import { TimetableService } from '../../services/timetable.service';

export interface SlotTarget {
  academicYearId: number;
  sectionId: number;
  sectionLabel: string;
  dayOfWeek: number;
  dayName: string;
  periodId: number;
  periodNumber: number;
  current: TimetableEntry | null;
}

/** Assign (or change, or clear) the subject, teacher and room of one cell of a section's timetable. */
@Component({
  selector: 'app-slot-modal',
  imports: [ReactiveFormsModule, ModalComponent, ButtonComponent, InputComponent, SelectComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <app-modal [title]="title()" [dismissible]="!busy()" (closed)="closed.emit()">
      <form class="form" [formGroup]="form" (ngSubmit)="save()" novalidate>
        <app-select label="Subject" [required]="true" placeholder="Select subject…" [options]="subjectOptions()" formControlName="subjectId" [errorText]="error('subjectId')" />
        <app-select label="Teacher" [required]="true" placeholder="Select teacher…" [options]="teacherOptions()" formControlName="employeeId" [errorText]="error('employeeId')" />
        <app-input label="Room" placeholder="Optional, e.g. 101" formControlName="roomNo" [errorText]="error('roomNo')" />
        @if (lookups() && lookups()!.subjects.length === 0) {
          <p class="hint">This class has no subjects yet. Add them under School Setup → Class-Subject Mapping.</p>
        }
        <button type="submit" hidden tabindex="-1"></button>
      </form>

      <ng-container modal-footer>
        @if (target().current) {
          <app-button variant="danger-ghost" [disabled]="busy()" (click)="clear()">Clear slot</app-button>
        }
        <app-button variant="secondary" [disabled]="busy()" (click)="closed.emit()">Cancel</app-button>
        <app-button [disabled]="busy() || !lookups()" (click)="save()">{{ busy() ? 'Saving…' : 'Save' }}</app-button>
      </ng-container>
    </app-modal>
  `,
  styles: `
    .form {
      display: flex;
      flex-direction: column;
      gap: var(--space-5);
    }

    .hint {
      margin: 0;
      font-size: var(--text-sm);
      color: var(--color-text-muted);
    }
  `,
})
export class SlotModalComponent implements OnInit {
  private readonly service = inject(TimetableService);
  private readonly toast = inject(ToastService);

  readonly target = input.required<SlotTarget>();
  readonly saved = output<void>();
  readonly closed = output<void>();

  protected readonly lookups = signal<TimetableLookups | null>(null);
  protected readonly busy = signal(false);
  protected readonly submitted = signal(false);

  protected readonly title = computed(() => {
    const t = this.target();
    return `${t.sectionLabel} · ${t.dayName}, Period ${t.periodNumber}`;
  });
  protected readonly subjectOptions = computed<SelectOption[]>(() => (this.lookups()?.subjects ?? []).map((s) => ({ label: s.subjectName, value: String(s.subjectId) })));
  protected readonly teacherOptions = computed<SelectOption[]>(() => (this.lookups()?.teachers ?? []).map((t) => ({ label: `${t.fullName} (${t.employeeId})`, value: t.employeeId })));

  protected readonly form = new FormGroup({
    subjectId: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    employeeId: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    roomNo: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(50)] }),
  });

  ngOnInit(): void {
    const t = this.target();
    const cur = t.current;
    if (cur) this.form.patchValue({ subjectId: String(cur.subjectId), employeeId: cur.employeeId, roomNo: cur.roomNo ?? '' });
    this.service.lookups(t.sectionId).subscribe({
      next: (l) => this.lookups.set(l),
      error: () => this.closed.emit(), // already toasted by the interceptor
    });
  }

  protected error(name: 'subjectId' | 'employeeId' | 'roomNo'): string | undefined {
    const c = this.form.controls[name];
    if (!(c.touched || this.submitted())) return undefined;
    if (c.hasError('required')) return 'This field is required';
    if (c.hasError('maxlength')) return 'Maximum 50 characters';
    return undefined;
  }

  protected save(): void {
    if (this.busy()) return;
    this.submitted.set(true);
    this.form.markAllAsTouched();
    if (this.form.invalid) return;

    const t = this.target();
    const v = this.form.getRawValue();
    this.busy.set(true);
    this.service
      .assign({
        academicYearId: t.academicYearId,
        sectionId: t.sectionId,
        dayOfWeek: t.dayOfWeek,
        periodId: t.periodId,
        subjectId: Number(v.subjectId),
        employeeId: v.employeeId,
        roomNo: v.roomNo.trim() === '' ? null : v.roomNo.trim(),
      })
      .pipe(finalize(() => this.busy.set(false)))
      .subscribe({
        next: () => {
          this.toast.success('Timetable slot saved.');
          this.saved.emit();
        },
        // Already toasted by the interceptor (e.g. the 409 naming the class the teacher is already teaching); the modal stays open.
        error: () => undefined,
      });
  }

  protected clear(): void {
    if (this.busy()) return;
    const t = this.target();
    this.busy.set(true);
    this.service
      .unassign({ academicYearId: t.academicYearId, sectionId: t.sectionId, dayOfWeek: t.dayOfWeek, periodId: t.periodId })
      .pipe(finalize(() => this.busy.set(false)))
      .subscribe({
        next: () => {
          this.toast.success('Timetable slot cleared.');
          this.saved.emit();
        },
        error: () => undefined,
      });
  }
}
