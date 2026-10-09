import { ChangeDetectionStrategy, Component, OnInit, computed, inject, input, output, signal } from '@angular/core';
import { AbstractControl, FormControl, FormGroup, ReactiveFormsModule, ValidationErrors, Validators } from '@angular/forms';
import { catchError, finalize, of } from 'rxjs';
import { ToastService } from '../../../../core/services/toast.service';
import { ButtonComponent, InputComponent, ModalComponent, SelectComponent, SelectOption } from '../../../../shared/components';
import { Section } from '../../models/section.model';
import { ClassService } from '../../services/class.service';
import { SectionService } from '../../services/section.service';

/** Class, section name and optional max strength. Status is a row action on the list, never a form field. */
@Component({
  selector: 'app-section-form-modal',
  imports: [ReactiveFormsModule, ModalComponent, ButtonComponent, InputComponent, SelectComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <app-modal [title]="isEdit() ? 'Edit Section' : 'Add Section'" [dismissible]="!saving()" (closed)="closed.emit()">
      <form class="form" [formGroup]="form" (ngSubmit)="submit()" novalidate>
        <app-select
          label="Class"
          [required]="true"
          placeholder="Select class…"
          [options]="classOptions()"
          formControlName="classId"
          [errorText]="error('classId')"
        />
        <app-input label="Section name" [required]="true" placeholder="e.g. A" formControlName="sectionName" [errorText]="error('sectionName')" />
        <app-input
          label="Max strength"
          type="number"
          placeholder="Optional"
          helpText="Maximum number of students in this section."
          formControlName="maxStrength"
          [errorText]="error('maxStrength')"
        />
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
export class SectionFormModalComponent implements OnInit {
  private readonly service = inject(SectionService);
  private readonly classes = inject(ClassService);
  private readonly toast = inject(ToastService);

  /** The section being edited, or null to create. */
  readonly section = input<Section | null>(null);
  readonly saved = output<Section>();
  readonly closed = output<void>();

  protected readonly isEdit = computed(() => this.section() !== null);
  protected readonly saving = signal(false);
  protected readonly submitted = signal(false);
  protected readonly classOptions = signal<SelectOption[]>([]);

  protected readonly form = new FormGroup({
    classId: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    sectionName: new FormControl('', { nonNullable: true, validators: [Validators.required, notBlank, Validators.maxLength(20)] }),
    // smallint in the database; optional
    maxStrength: new FormControl('', { nonNullable: true, validators: [positiveWholeNumber] }),
  });

  ngOnInit(): void {
    const s = this.section();
    if (s) this.form.patchValue({ classId: String(s.classId), sectionName: s.sectionName, maxStrength: s.maxStrength == null ? '' : String(s.maxStrength) });

    // Only active classes (of my own school: the endpoint guarantees that) — plus the section's current class when
    // editing, so an inactive class it already belongs to stays selectable.
    this.classes
      .list()
      .pipe(catchError(() => of([]))) // already toasted by the interceptor
      .subscribe((list) =>
        this.classOptions.set(
          list
            .filter((c) => c.statusName === 'Active' || c.classId === s?.classId)
            .map((c) => ({ label: c.statusName === 'Active' ? c.className : `${c.className} (inactive)`, value: String(c.classId) })),
        ),
      );
  }

  protected error(name: 'classId' | 'sectionName' | 'maxStrength'): string | undefined {
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
    const body = {
      classId: Number(v.classId),
      sectionName: v.sectionName.trim(),
      maxStrength: v.maxStrength.trim() === '' ? null : Number(v.maxStrength),
    };
    const editing = this.section();

    this.saving.set(true);
    (editing ? this.service.update(editing.sectionId, body) : this.service.create(body))
      .pipe(finalize(() => this.saving.set(false)))
      .subscribe({
        next: (saved) => {
          this.toast.success(`Section ${editing ? 'updated' : 'created'} successfully.`);
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
  if (raw === '') return null; // optional
  const n = Number(raw);
  return Number.isInteger(n) && n >= 1 && n <= 32767 ? null : { whole: true };
}
