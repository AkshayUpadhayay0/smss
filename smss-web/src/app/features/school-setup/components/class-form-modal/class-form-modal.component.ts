import { ChangeDetectionStrategy, Component, OnInit, computed, inject, input, output, signal } from '@angular/core';
import { AbstractControl, FormControl, FormGroup, ReactiveFormsModule, ValidationErrors, Validators } from '@angular/forms';
import { finalize } from 'rxjs';
import { ToastService } from '../../../../core/services/toast.service';
import { ButtonComponent, InputComponent, ModalComponent, SelectComponent, SelectOption } from '../../../../shared/components';
import { SchoolClass } from '../../models/class.model';
import { ClassService } from '../../services/class.service';

/** Class name, optional code, display order and next class. Status is a row action on the list, never a form field. */
@Component({
  selector: 'app-class-form-modal',
  imports: [ReactiveFormsModule, ModalComponent, ButtonComponent, InputComponent, SelectComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <app-modal [title]="isEdit() ? 'Edit Class' : 'Add Class'" [dismissible]="!saving()" (closed)="closed.emit()">
      <form class="form" [formGroup]="form" (ngSubmit)="submit()" novalidate>
        <app-input label="Class name" [required]="true" placeholder="e.g. Nursery, Class 1" formControlName="className" [errorText]="error('className')" />
        <app-input label="Class code" placeholder="Optional, e.g. NUR" formControlName="classCode" [errorText]="error('classCode')" />
        <app-input
          label="Sequence order"
          type="number"
          [required]="true"
          placeholder="e.g. 1"
          helpText="Controls display order — lower numbers appear first."
          formControlName="sequenceOrder"
          [errorText]="error('sequenceOrder')"
        />
        <app-select
          label="Next class"
          [options]="nextOptions()"
          formControlName="nextClassId"
        />
        <p class="hint">Where students of this class move up to at the start of a new year. It is only the default: it can be changed per student when promoting.</p>
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
    .hint {
      margin: calc(var(--space-3) * -1) 0 0;
      font-size: 0.8125rem;
      color: var(--color-text-muted, #6b7280);
    }
  `,
})
export class ClassFormModalComponent implements OnInit {
  private readonly service = inject(ClassService);
  private readonly toast = inject(ToastService);

  /** The class being edited, or null to create. */
  readonly schoolClass = input<SchoolClass | null>(null);
  /** Every class of the school, to pick the next class from. */
  readonly allClasses = input<SchoolClass[]>([]);
  readonly saved = output<SchoolClass>();
  readonly closed = output<void>();

  protected readonly isEdit = computed(() => this.schoolClass() !== null);
  /** Every OTHER class, in display order; the server refuses a class that another class already moves up to, or a loop. */
  protected readonly nextOptions = computed<SelectOption[]>(() => [
    { label: 'None: the last class (students pass out)', value: '' },
    ...[...this.allClasses()]
      .filter((c) => c.classId !== this.schoolClass()?.classId)
      .sort((a, b) => a.sequenceOrder - b.sequenceOrder || a.className.localeCompare(b.className))
      .map((c) => ({ label: c.className + (c.statusName === 'Active' ? '' : ' (inactive)'), value: String(c.classId) })),
  ]);
  protected readonly saving = signal(false);
  protected readonly submitted = signal(false);

  protected readonly form = new FormGroup({
    className: new FormControl('', { nonNullable: true, validators: [Validators.required, notBlank, Validators.maxLength(50)] }),
    classCode: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(20)] }),
    // smallint in the database
    sequenceOrder: new FormControl('', { nonNullable: true, validators: [Validators.required, positiveWholeNumber] }),
    nextClassId: new FormControl('', { nonNullable: true }),
  });

  ngOnInit(): void {
    const c = this.schoolClass();
    if (c) this.form.patchValue({ className: c.className, classCode: c.classCode ?? '', sequenceOrder: String(c.sequenceOrder), nextClassId: c.nextClassId ? String(c.nextClassId) : '' });
  }

  protected error(name: 'className' | 'classCode' | 'sequenceOrder'): string | undefined {
    const c = this.form.controls[name];
    if (!(c.touched || this.submitted())) return undefined;
    if (c.hasError('required') || c.hasError('blank')) return 'This field is required';
    if (c.hasError('maxlength')) return `Maximum ${c.getError('maxlength').requiredLength} characters`;
    if (c.hasError('whole')) return 'Enter a whole number from 1 to 32767';
    return undefined;
  }

  protected submit(): void {
    if (this.saving()) return;
    this.submitted.set(true);
    this.form.markAllAsTouched();
    if (this.form.invalid) return;

    const v = this.form.getRawValue();
    const body = { className: v.className.trim(), classCode: v.classCode.trim() || null, sequenceOrder: Number(v.sequenceOrder), nextClassId: v.nextClassId ? Number(v.nextClassId) : null };
    const editing = this.schoolClass();

    this.saving.set(true);
    (editing ? this.service.update(editing.classId, body) : this.service.create(body))
      .pipe(finalize(() => this.saving.set(false)))
      .subscribe({
        next: (saved) => {
          this.toast.success(`Class ${editing ? 'updated' : 'created'} successfully.`);
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

function positiveWholeNumber(control: AbstractControl): ValidationErrors | null {
  const raw = String(control.value ?? '').trim();
  if (raw === '') return null; // `required` reports blanks
  const n = Number(raw);
  return Number.isInteger(n) && n >= 1 && n <= 32767 ? null : { whole: true };
}
