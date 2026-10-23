import { ChangeDetectionStrategy, Component, OnInit, computed, inject, input, output, signal } from '@angular/core';
import { AbstractControl, FormControl, FormGroup, ReactiveFormsModule, ValidationErrors, Validators } from '@angular/forms';
import { finalize } from 'rxjs';
import { ToastService } from '../../../../core/services/toast.service';
import { ButtonComponent, InputComponent, ModalComponent } from '../../../../shared/components';
import { Period } from '../../models/timetable.model';
import { TimetableService } from '../../services/timetable.service';
import { shortTime } from '../timetable-grid/timetable-grid.component';

/** One bell-schedule period: its number and start / end time. Status is a row action on the list. */
@Component({
  selector: 'app-period-form-modal',
  imports: [ReactiveFormsModule, ModalComponent, ButtonComponent, InputComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <app-modal [title]="isEdit() ? 'Edit Period' : 'Add Period'" [dismissible]="!saving()" (closed)="closed.emit()">
      <form class="form" [formGroup]="form" (ngSubmit)="submit()" novalidate>
        <app-input label="Period number" type="number" [required]="true" placeholder="e.g. 1" formControlName="periodNumber" [errorText]="error('periodNumber')" />
        <app-input label="Start time" type="time" [required]="true" formControlName="startTime" [errorText]="error('startTime')" />
        <app-input label="End time" type="time" [required]="true" formControlName="endTime" [errorText]="error('endTime')" />
        <button type="submit" hidden tabindex="-1"></button>
      </form>

      <ng-container modal-footer>
        <app-button variant="secondary" [disabled]="saving()" (click)="closed.emit()">Cancel</app-button>
        <app-button [disabled]="saving()" (click)="submit()">{{ saving() ? 'Saving…' : isEdit() ? 'Update' : 'Create' }}</app-button>
      </ng-container>
    </app-modal>
  `,
  styles: `
    .form {
      display: flex;
      flex-direction: column;
      gap: var(--space-5);
    }
  `,
})
export class PeriodFormModalComponent implements OnInit {
  private readonly service = inject(TimetableService);
  private readonly toast = inject(ToastService);

  /** The period being edited, or null to create. */
  readonly period = input<Period | null>(null);
  /** Suggested number for a new period. */
  readonly nextNumber = input(1);
  readonly saved = output<Period>();
  readonly closed = output<void>();

  protected readonly isEdit = computed(() => this.period() !== null);
  protected readonly saving = signal(false);
  protected readonly submitted = signal(false);

  protected readonly form = new FormGroup(
    {
      periodNumber: new FormControl('', { nonNullable: true, validators: [Validators.required, periodNumber] }),
      startTime: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
      endTime: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    },
    { validators: endAfterStart },
  );

  ngOnInit(): void {
    const p = this.period();
    if (p) this.form.patchValue({ periodNumber: String(p.periodNumber), startTime: shortTime(p.startTime), endTime: shortTime(p.endTime) });
    else this.form.patchValue({ periodNumber: String(this.nextNumber()) });
  }

  protected error(name: 'periodNumber' | 'startTime' | 'endTime'): string | undefined {
    const c = this.form.controls[name];
    if (!(c.touched || this.submitted())) return undefined;
    if (c.hasError('required')) return 'This field is required';
    if (c.hasError('number')) return 'Enter a whole number from 1 to 30';
    if (name === 'endTime' && this.form.hasError('order')) return 'End time must be after the start time';
    return undefined;
  }

  protected submit(): void {
    if (this.saving()) return;
    this.submitted.set(true);
    this.form.markAllAsTouched();
    if (this.form.invalid) return;

    const v = this.form.getRawValue();
    const body = { periodNumber: Number(v.periodNumber), startTime: v.startTime, endTime: v.endTime };
    const editing = this.period();

    this.saving.set(true);
    (editing ? this.service.updatePeriod(editing.periodId, body) : this.service.createPeriod(body))
      .pipe(finalize(() => this.saving.set(false)))
      .subscribe({
        next: (saved) => {
          this.toast.success(`Period ${editing ? 'updated' : 'created'} successfully.`);
          this.saved.emit(saved);
        },
        // Already toasted by the interceptor (a duplicate number, overlapping times); the modal stays open.
        error: () => undefined,
      });
  }
}

function periodNumber(control: AbstractControl): ValidationErrors | null {
  const raw = String(control.value ?? '').trim();
  if (raw === '') return null;
  const n = Number(raw);
  return Number.isInteger(n) && n >= 1 && n <= 30 ? null : { number: true };
}

function endAfterStart(group: AbstractControl): ValidationErrors | null {
  const start = group.get('startTime')?.value as string;
  const end = group.get('endTime')?.value as string;
  return start && end && end <= start ? { order: true } : null; // "HH:mm" strings compare correctly
}
