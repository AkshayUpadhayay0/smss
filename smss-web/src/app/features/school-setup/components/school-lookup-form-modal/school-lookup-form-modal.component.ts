import { ChangeDetectionStrategy, Component, OnInit, computed, inject, input, output, signal } from '@angular/core';
import { AbstractControl, FormControl, FormGroup, ReactiveFormsModule, ValidationErrors, Validators } from '@angular/forms';
import { finalize } from 'rxjs';
import { ToastService } from '../../../../core/services/toast.service';
import { ButtonComponent, InputComponent, ModalComponent, TextareaComponent } from '../../../../shared/components';
import { SchoolLookupConfig, SchoolLookupItem } from '../../models/school-lookup.model';
import { SchoolLookupService } from '../../services/school-lookup.service';

/** Add / edit modal for a school-owned lookup: name, optional code (when the config has one) and description. */
@Component({
  selector: 'app-school-lookup-form-modal',
  imports: [ReactiveFormsModule, ModalComponent, ButtonComponent, InputComponent, TextareaComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <app-modal [title]="title()" [dismissible]="!saving()" (closed)="closed.emit()">
      <form class="form" [formGroup]="form" (ngSubmit)="submit()" novalidate>
        <app-input
          [label]="config().nameLabel"
          [required]="true"
          [placeholder]="config().namePlaceholder"
          formControlName="name"
          [errorText]="error('name')"
        />
        @if (config().codeKey) {
          <app-input [label]="config().codeLabel ?? 'Code'" placeholder="Optional" formControlName="code" [errorText]="error('code')" />
        }
        <app-textarea label="Description" placeholder="Optional" [maxLength]="250" formControlName="description" [errorText]="error('description')" />
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
export class SchoolLookupFormModalComponent implements OnInit {
  private readonly service = inject(SchoolLookupService);
  private readonly toast = inject(ToastService);

  readonly config = input.required<SchoolLookupConfig>();
  /** The record being edited, or null to create. */
  readonly item = input<SchoolLookupItem | null>(null);
  readonly saved = output<SchoolLookupItem>();
  readonly closed = output<void>();

  protected readonly isEdit = computed(() => this.item() !== null);
  protected readonly title = computed(() => `${this.isEdit() ? 'Edit' : 'Add'} ${this.config().title}`);
  protected readonly saving = signal(false);
  protected readonly submitted = signal(false);

  protected readonly form = new FormGroup({
    name: new FormControl('', { nonNullable: true, validators: [Validators.required, notBlank, Validators.maxLength(100)] }),
    code: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(20)] }),
    description: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(250)] }),
  });

  ngOnInit(): void {
    const item = this.item();
    if (!item) return;
    const cfg = this.config();
    this.form.patchValue({
      name: String(item[cfg.nameKey] ?? ''),
      code: cfg.codeKey ? String(item[cfg.codeKey] ?? '') : '',
      description: item.description ?? '',
    });
  }

  protected error(name: 'name' | 'code' | 'description'): string | undefined {
    const c = this.form.controls[name];
    if (!(c.touched || this.submitted())) return undefined;
    if (c.hasError('required') || c.hasError('blank')) return 'This field is required';
    if (c.hasError('maxlength')) return `Maximum ${c.getError('maxlength').requiredLength} characters`;
    return undefined;
  }

  protected submit(): void {
    if (this.saving()) return;
    this.submitted.set(true);
    this.form.markAllAsTouched();
    if (this.form.invalid) return;

    const cfg = this.config();
    const v = this.form.getRawValue();
    const body: Record<string, unknown> = { [cfg.nameKey]: v.name.trim(), description: v.description.trim() || null };
    if (cfg.codeKey) body[cfg.codeKey] = v.code.trim() || null;
    const editing = this.item();

    this.saving.set(true);
    (editing ? this.service.update(cfg.path, editing[cfg.idKey] as number, body) : this.service.create(cfg.path, body))
      .pipe(finalize(() => this.saving.set(false)))
      .subscribe({
        next: (saved) => {
          this.toast.success(`${cfg.title} ${editing ? 'updated' : 'created'} successfully.`);
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
