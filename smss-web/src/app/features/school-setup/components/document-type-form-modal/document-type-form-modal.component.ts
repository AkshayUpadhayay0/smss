import { ChangeDetectionStrategy, Component, OnInit, computed, inject, input, output, signal } from '@angular/core';
import { AbstractControl, FormControl, FormGroup, ReactiveFormsModule, ValidationErrors, Validators } from '@angular/forms';
import { finalize } from 'rxjs';
import { ToastService } from '../../../../core/services/toast.service';
import { ButtonComponent, InputComponent, ModalComponent, SelectComponent, SelectOption } from '../../../../shared/components';
import { APPLIES_TO_LABELS, AppliesTo, DocumentType } from '../../models/document-type.model';
import { DocumentTypeService } from '../../services/document-type.service';

/** Name, code, who it applies to and whether it is required. Status is a row action on the list, never a form field. */
@Component({
  selector: 'app-document-type-form-modal',
  imports: [ReactiveFormsModule, ModalComponent, ButtonComponent, InputComponent, SelectComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './document-type-form-modal.component.scss',
  template: `
    <app-modal [title]="isEdit() ? 'Edit Document Type' : 'Add Document Type'" [dismissible]="!saving()" (closed)="closed.emit()">
      <form class="form" [formGroup]="form" (ngSubmit)="submit()" novalidate>
        <app-input label="Document name" [required]="true" placeholder="e.g. Aadhar Card" formControlName="documentName" [errorText]="error('documentName')" />
        <!-- focusout bubbles from the inner input: the code is upper-cased when the user leaves the field -->
        <div (focusout)="upperCaseCode()">
          <app-input
            label="Document code"
            [required]="true"
            placeholder="e.g. AADHAR"
            helpText="Saved in upper case. Must be unique within your school."
            formControlName="documentCode"
            [errorText]="error('documentCode')"
          />
        </div>
        <app-select label="Applies to" [required]="true" placeholder="Select…" [options]="appliesToOptions" formControlName="appliesTo" [errorText]="error('appliesTo')" />
        <label class="check">
          <input type="checkbox" formControlName="isRequired" />
          <span>This document is required</span>
        </label>
        <!-- lets Enter submit; the visible button lives in the modal footer -->
        <button type="submit" hidden tabindex="-1"></button>
      </form>

      <ng-container modal-footer>
        <app-button variant="secondary" [disabled]="saving()" (click)="closed.emit()">Cancel</app-button>
        <app-button [disabled]="saving()" (click)="submit()">{{ saving() ? 'Saving…' : isEdit() ? 'Update' : 'Create' }}</app-button>
      </ng-container>
    </app-modal>
  `,
})
export class DocumentTypeFormModalComponent implements OnInit {
  private readonly service = inject(DocumentTypeService);
  private readonly toast = inject(ToastService);

  /** The document type being edited, or null to create. */
  readonly documentType = input<DocumentType | null>(null);
  readonly saved = output<DocumentType>();
  readonly closed = output<void>();

  protected readonly isEdit = computed(() => this.documentType() !== null);
  protected readonly saving = signal(false);
  protected readonly submitted = signal(false);

  protected readonly appliesToOptions: SelectOption[] = (Object.keys(APPLIES_TO_LABELS) as AppliesTo[]).map((k) => ({ label: APPLIES_TO_LABELS[k], value: k }));

  protected readonly form = new FormGroup({
    documentName: new FormControl('', { nonNullable: true, validators: [Validators.required, notBlank, Validators.maxLength(50)] }),
    documentCode: new FormControl('', { nonNullable: true, validators: [Validators.required, notBlank, Validators.maxLength(20)] }),
    appliesTo: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    isRequired: new FormControl(false, { nonNullable: true }),
  });

  ngOnInit(): void {
    const d = this.documentType();
    if (d) this.form.patchValue({ documentName: d.documentName, documentCode: d.documentCode, appliesTo: d.appliesTo, isRequired: d.isRequired });
  }

  protected upperCaseCode(): void {
    const c = this.form.controls.documentCode;
    const upper = c.value.trim().toUpperCase();
    if (upper !== c.value) c.setValue(upper);
  }

  protected error(name: 'documentName' | 'documentCode' | 'appliesTo'): string | undefined {
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

    const v = this.form.getRawValue();
    const body = { documentName: v.documentName.trim(), documentCode: v.documentCode.trim().toUpperCase(), appliesTo: v.appliesTo as AppliesTo, isRequired: v.isRequired };
    const editing = this.documentType();

    this.saving.set(true);
    (editing ? this.service.update(editing.documentTypeId, body) : this.service.create(body))
      .pipe(finalize(() => this.saving.set(false)))
      .subscribe({
        next: (saved) => {
          this.toast.success(`Document type ${editing ? 'updated' : 'created'} successfully.`);
          this.saved.emit(saved);
        },
        // Already toasted by the interceptor (e.g. the 409 for a duplicate code); the modal stays open.
        error: () => undefined,
      });
  }
}

function notBlank(control: AbstractControl): ValidationErrors | null {
  return String(control.value ?? '').trim() === '' ? { blank: true } : null;
}
