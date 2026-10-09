import { ChangeDetectionStrategy, Component, OnInit, computed, inject, input, output, signal } from '@angular/core';
import { AbstractControl, FormControl, FormGroup, ReactiveFormsModule, ValidationErrors, Validators } from '@angular/forms';
import { finalize } from 'rxjs';
import { ToastService } from '../../../../core/services/toast.service';
import { ButtonComponent, InputComponent, ModalComponent } from '../../../../shared/components';
import { AcademicYear } from '../../models/academic-year.model';
import { AcademicYearService } from '../../services/academic-year.service';

/** Year name + dates only. Status and "current" are actions on the list, never form fields. */
@Component({
  selector: 'app-academic-year-form-modal',
  imports: [ReactiveFormsModule, ModalComponent, ButtonComponent, InputComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <app-modal [title]="isEdit() ? 'Edit Academic Year' : 'Add Academic Year'" [dismissible]="!saving()" (closed)="closed.emit()">
      <form class="form" [formGroup]="form" (ngSubmit)="submit()" novalidate>
        <app-input label="Year name" [required]="true" placeholder="e.g. 2026-2027" formControlName="yearName" [errorText]="error('yearName')" />
        <app-input label="Start date" type="date" [required]="true" formControlName="startDate" [errorText]="error('startDate')" />
        <app-input label="End date" type="date" [required]="true" formControlName="endDate" [errorText]="error('endDate')" />
        <!-- lets Enter submit; the visible button lives in the modal footer -->
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
export class AcademicYearFormModalComponent implements OnInit {
  private readonly service = inject(AcademicYearService);
  private readonly toast = inject(ToastService);

  /** The year being edited, or null to create. */
  readonly year = input<AcademicYear | null>(null);
  readonly saved = output<AcademicYear>();
  readonly closed = output<void>();

  protected readonly isEdit = computed(() => this.year() !== null);
  protected readonly saving = signal(false);
  protected readonly submitted = signal(false);

  protected readonly form = new FormGroup(
    {
      yearName: new FormControl('', { nonNullable: true, validators: [Validators.required, notBlank, Validators.maxLength(20)] }),
      startDate: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
      endDate: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    },
    { validators: [endAfterStart] },
  );

  ngOnInit(): void {
    const y = this.year();
    if (y) this.form.patchValue({ yearName: y.yearName, startDate: y.startDate.slice(0, 10), endDate: y.endDate.slice(0, 10) });
  }

  protected error(name: 'yearName' | 'startDate' | 'endDate'): string | undefined {
    const c = this.form.controls[name];
    if (!(c.touched || this.submitted())) return undefined;
    if (c.hasError('required') || c.hasError('blank')) return 'This field is required';
    if (c.hasError('maxlength')) return `Maximum ${c.getError('maxlength').requiredLength} characters`;
    if (name === 'endDate' && this.form.hasError('endBeforeStart')) return 'End date must be after start date';
    return undefined;
  }

  protected submit(): void {
    if (this.saving()) return;
    this.submitted.set(true);
    this.form.markAllAsTouched();
    if (this.form.invalid) return;

    const v = this.form.getRawValue();
    const body = { yearName: v.yearName.trim(), startDate: v.startDate, endDate: v.endDate };
    const editing = this.year();

    this.saving.set(true);
    (editing ? this.service.update(editing.academicYearId, body) : this.service.create(body))
      .pipe(finalize(() => this.saving.set(false)))
      .subscribe({
        next: (saved) => {
          this.toast.success(`Academic year ${editing ? 'updated' : 'created'} successfully.`);
          this.saved.emit(saved);
        },
        // Already toasted by the interceptor (e.g. the 409 for a duplicate name); the modal stays open.
        error: () => undefined,
      });
  }
}

function notBlank(control: AbstractControl): ValidationErrors | null {
  return String(control.value ?? '').trim() === '' ? { blank: true } : null;
}

function endAfterStart(group: AbstractControl): ValidationErrors | null {
  const start = group.get('startDate')?.value as string;
  const end = group.get('endDate')?.value as string;
  return start && end && end <= start ? { endBeforeStart: true } : null; // yyyy-MM-dd compares correctly as text
}
