import { ChangeDetectionStrategy, Component, OnInit, computed, inject, input, output, signal } from '@angular/core';
import { AbstractControl, FormControl, FormGroup, ReactiveFormsModule, ValidationErrors, Validators } from '@angular/forms';
import { finalize } from 'rxjs';
import { ToastService } from '../../../../core/services/toast.service';
import { ButtonComponent, InputComponent, ModalComponent, TextareaComponent } from '../../../../shared/components';
import { InvSupplier } from '../../models/inventory.model';
import { InventoryService } from '../../services/inventory.service';

const notBlank = (c: AbstractControl): ValidationErrors | null => (String(c.value ?? '').trim() === '' ? { blank: true } : null);
const optional = (re: RegExp, message: string) => (c: AbstractControl): ValidationErrors | null => {
  const v = String(c.value ?? '').trim();
  return v === '' || re.test(v.toUpperCase()) ? null : { format: message };
};
const GSTIN = /^[0-9]{2}[A-Z]{5}[0-9]{4}[A-Z][1-9A-Z]Z[0-9A-Z]$/;
const PAN = /^[A-Z]{5}[0-9]{4}[A-Z]$/;

/** Add / edit a supplier (also the vendor of Stage 3's billing on behalf). The server validates the same formats and keeps names unique per school. */
@Component({
  selector: 'app-supplier-form-modal',
  imports: [ReactiveFormsModule, ButtonComponent, InputComponent, ModalComponent, TextareaComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: '../../styles/inventory-page.scss',
  template: `
    <app-modal [title]="isEdit() ? 'Edit Supplier' : 'Add Supplier'" [dismissible]="!saving()" (closed)="closed.emit()">
      <form class="form" [formGroup]="form" (ngSubmit)="submit()" novalidate>
        <app-input label="Supplier name" [required]="true" placeholder="e.g. Sharma Stationers" formControlName="name" [errorText]="error('name')" />
        <div class="grid">
          <app-input label="Contact person" placeholder="Optional" formControlName="contact" [errorText]="error('contact')" />
          <app-input label="Mobile" type="tel" placeholder="Optional" formControlName="mobile" [errorText]="error('mobile')" />
          <app-input label="Email" type="email" placeholder="Optional" formControlName="email" [errorText]="error('email')" />
          <app-input label="GSTIN" placeholder="e.g. 22AAAAA0000A1Z5" formControlName="gstin" [errorText]="error('gstin')" />
          <app-input label="PAN" placeholder="e.g. ABCDE1234F" formControlName="pan" [errorText]="error('pan')" />
        </div>
        <app-textarea label="Address" placeholder="Optional" [maxLength]="1000" formControlName="address" />
        <button type="submit" hidden tabindex="-1"></button>
      </form>
      <ng-container modal-footer>
        <app-button variant="secondary" [disabled]="saving()" (click)="closed.emit()">Cancel</app-button>
        <app-button [disabled]="saving()" (click)="submit()">{{ saving() ? 'Saving…' : isEdit() ? 'Update' : 'Create' }}</app-button>
      </ng-container>
    </app-modal>
  `,
})
export class SupplierFormModalComponent implements OnInit {
  private readonly service = inject(InventoryService);
  private readonly toast = inject(ToastService);

  readonly supplier = input<InvSupplier | null>(null);
  readonly saved = output<InvSupplier>();
  readonly closed = output<void>();

  protected readonly isEdit = computed(() => this.supplier() !== null);
  protected readonly saving = signal(false);
  protected readonly submitted = signal(false);

  protected readonly form = new FormGroup({
    name: new FormControl('', { nonNullable: true, validators: [Validators.required, notBlank, Validators.maxLength(150)] }),
    contact: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(100)] }),
    mobile: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(20), optional(/^[0-9+\-\s()]{7,20}$/, 'Enter a valid mobile number')] }),
    email: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(150), Validators.email] }),
    gstin: new FormControl('', { nonNullable: true, validators: [optional(GSTIN, 'GSTIN must be 15 characters, e.g. 22AAAAA0000A1Z5')] }),
    pan: new FormControl('', { nonNullable: true, validators: [optional(PAN, 'PAN must be 10 characters, e.g. ABCDE1234F')] }),
    address: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(1000)] }),
  });

  ngOnInit(): void {
    const s = this.supplier();
    if (s) this.form.patchValue({ name: s.supplierName, contact: s.contactPerson ?? '', mobile: s.mobile ?? '', email: s.email ?? '', gstin: s.gstin ?? '', pan: s.pan ?? '', address: s.address ?? '' });
  }

  protected error(name: 'name' | 'contact' | 'mobile' | 'email' | 'gstin' | 'pan'): string | undefined {
    const c = this.form.controls[name];
    if (c.valid || !(c.touched || this.submitted())) return undefined;
    if (c.hasError('required') || c.hasError('blank')) return 'This field is required';
    if (c.hasError('maxlength')) return `Maximum ${c.getError('maxlength').requiredLength} characters`;
    if (c.hasError('email')) return 'Enter a valid email address';
    return c.getError('format') ?? 'Invalid value';
  }

  protected submit(): void {
    if (this.saving()) return;
    this.submitted.set(true);
    this.form.markAllAsTouched();
    if (this.form.invalid) return;
    const v = this.form.getRawValue();
    const blank = (s: string) => s.trim() || null;   // a blank optional field is sent as null, never ""
    const editing = this.supplier();
    this.saving.set(true);
    this.service
      .saveSupplier(editing?.supplierId ?? null, {
        supplierName: v.name.trim(), contactPerson: blank(v.contact), mobile: blank(v.mobile), email: blank(v.email), address: blank(v.address),
        gstin: blank(v.gstin)?.toUpperCase() ?? null, pan: blank(v.pan)?.toUpperCase() ?? null,
      })
      .pipe(finalize(() => this.saving.set(false)))
      .subscribe({
        next: (saved) => { this.toast.success(`Supplier ${editing ? 'updated' : 'created'}.`); this.saved.emit(saved); },
        error: () => undefined,   // toasted (duplicate name); the modal stays open
      });
  }
}
