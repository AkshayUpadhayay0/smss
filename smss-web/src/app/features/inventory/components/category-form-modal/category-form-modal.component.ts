import { ChangeDetectionStrategy, Component, OnInit, computed, inject, input, output, signal } from '@angular/core';
import { AbstractControl, FormControl, FormGroup, ReactiveFormsModule, ValidationErrors, Validators } from '@angular/forms';
import { finalize } from 'rxjs';
import { ToastService } from '../../../../core/services/toast.service';
import { ButtonComponent, InputComponent, ModalComponent, TextareaComponent } from '../../../../shared/components';
import { InvCategory } from '../../models/inventory.model';
import { InventoryService } from '../../services/inventory.service';

const notBlank = (c: AbstractControl): ValidationErrors | null => (String(c.value ?? '').trim() === '' ? { blank: true } : null);

/** Add / edit an item category. Status is a row action on the list, never a form field. */
@Component({
  selector: 'app-category-form-modal',
  imports: [ReactiveFormsModule, ModalComponent, ButtonComponent, InputComponent, TextareaComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: '../../styles/inventory-page.scss',
  template: `
    <app-modal [title]="isEdit() ? 'Edit Category' : 'Add Category'" [dismissible]="!saving()" (closed)="closed.emit()">
      <form class="form" [formGroup]="form" (ngSubmit)="submit()" novalidate>
        <app-input label="Category name" [required]="true" placeholder="e.g. Stationery" formControlName="name" [errorText]="error('name')" />
        <app-input label="Category code" [required]="true" placeholder="e.g. STA" helpText="Unique in your school; stored in capitals." formControlName="code" [errorText]="error('code')" />
        <app-textarea label="Description" placeholder="Optional" [maxLength]="1000" formControlName="description" />
        <button type="submit" hidden tabindex="-1"></button>
      </form>
      <ng-container modal-footer>
        <app-button variant="secondary" [disabled]="saving()" (click)="closed.emit()">Cancel</app-button>
        <app-button [disabled]="saving()" (click)="submit()">{{ saving() ? 'Saving…' : isEdit() ? 'Update' : 'Create' }}</app-button>
      </ng-container>
    </app-modal>
  `,
})
export class CategoryFormModalComponent implements OnInit {
  private readonly service = inject(InventoryService);
  private readonly toast = inject(ToastService);

  readonly category = input<InvCategory | null>(null);
  readonly saved = output<InvCategory>();
  readonly closed = output<void>();

  protected readonly isEdit = computed(() => this.category() !== null);
  protected readonly saving = signal(false);
  protected readonly submitted = signal(false);

  protected readonly form = new FormGroup({
    name: new FormControl('', { nonNullable: true, validators: [Validators.required, notBlank, Validators.maxLength(100)] }),
    code: new FormControl('', { nonNullable: true, validators: [Validators.required, notBlank, Validators.maxLength(30), Validators.pattern(/^[A-Za-z0-9_-]+$/)] }),
    description: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(1000)] }),
  });

  ngOnInit(): void {
    const c = this.category();
    if (c) this.form.patchValue({ name: c.categoryName, code: c.categoryCode, description: c.description ?? '' });
  }

  protected error(name: 'name' | 'code'): string | undefined {
    const c = this.form.controls[name];
    if (c.valid || !(c.touched || this.submitted())) return undefined;
    if (c.hasError('required') || c.hasError('blank')) return 'This field is required';
    if (c.hasError('maxlength')) return `Maximum ${c.getError('maxlength').requiredLength} characters`;
    if (c.hasError('pattern')) return 'Use only letters, numbers, hyphen and underscore';
    return 'Invalid value';
  }

  protected submit(): void {
    if (this.saving()) return;
    this.submitted.set(true);
    this.form.markAllAsTouched();
    if (this.form.invalid) return;
    const v = this.form.getRawValue();
    const editing = this.category();
    this.saving.set(true);
    this.service
      .saveCategory(editing?.categoryId ?? null, { categoryName: v.name.trim(), categoryCode: v.code.trim(), description: v.description.trim() || null })
      .pipe(finalize(() => this.saving.set(false)))
      .subscribe({
        next: (saved) => { this.toast.success(`Category ${editing ? 'updated' : 'created'}.`); this.saved.emit(saved); },
        error: () => undefined, // toasted by the interceptor (duplicate code); the modal stays open
      });
  }
}
