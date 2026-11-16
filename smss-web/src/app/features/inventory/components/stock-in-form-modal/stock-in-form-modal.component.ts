import { ChangeDetectionStrategy, Component, OnInit, computed, inject, output, signal } from '@angular/core';
import { AbstractControl, FormControl, FormGroup, ReactiveFormsModule, ValidationErrors, Validators } from '@angular/forms';
import { finalize } from 'rxjs';
import { ToastService } from '../../../../core/services/toast.service';
import { ButtonComponent, InputComponent, ModalComponent, SelectComponent, SelectOption, TextareaComponent } from '../../../../shared/components';
import { InvItem, InvStockIn, formatQty, moneyError, quantityError, todayIso } from '../../models/inventory.model';
import { InventoryService } from '../../services/inventory.service';
import { ItemPickerComponent } from '../item-picker/item-picker.component';

const qty = (c: AbstractControl): ValidationErrors | null => { const e = quantityError(c.value); return e ? { qty: e } : null; };
const cost = (c: AbstractControl): ValidationErrors | null => { const e = moneyError(c.value); return e ? { cost: e } : null; };

/** Record stock received (no purchase order needed). The server raises the item's quantity through the ledger and remembers the cost as its last purchase price. */
@Component({
  selector: 'app-stock-in-form-modal',
  imports: [ReactiveFormsModule, ButtonComponent, InputComponent, ItemPickerComponent, ModalComponent, SelectComponent, TextareaComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: '../../styles/inventory-page.scss',
  template: `
    <app-modal title="Add Stock" [dismissible]="!saving()" (closed)="closed.emit()">
      <form class="form" [formGroup]="form" (ngSubmit)="submit()" novalidate>
        <app-item-picker (picked)="onPicked($event)" [errorText]="submitted() && !item() ? 'Choose an item' : undefined" />
        @if (item(); as i) {
          <p class="note">On hand now: <strong>{{ qty(i.currentQuantity) }} {{ i.unitCode }}</strong>. After this: <strong>{{ after() }}</strong>.</p>
        }
        <div class="grid">
          <app-input label="Quantity received" type="number" [required]="true" placeholder="0" formControlName="quantity" [errorText]="error('quantity')" />
          <app-input label="Unit cost (₹)" type="number" [required]="true" placeholder="0.00" formControlName="unitCost" [errorText]="error('unitCost')" />
          <app-input label="Received on" type="date" [required]="true" formControlName="receivedDate" [errorText]="dateError()" />
          <app-select label="Supplier" [options]="supplierOptions()" placeholder="No supplier" formControlName="supplierId" />
          <app-input label="Bill / invoice number" placeholder="Optional" formControlName="billNumber" [errorText]="error('billNumber')" />
          <app-input label="Expiry date" type="date" formControlName="expiryDate" helpText="Optional. Replaces the item's expiry." [errorText]="expiryError()" />
        </div>
        <app-textarea label="Remarks" placeholder="Optional" [maxLength]="500" formControlName="remarks" />
        <button type="submit" hidden tabindex="-1"></button>
      </form>
      <ng-container modal-footer>
        <app-button variant="secondary" [disabled]="saving()" (click)="closed.emit()">Cancel</app-button>
        <app-button [disabled]="saving()" (click)="submit()">{{ saving() ? 'Saving…' : 'Add Stock' }}</app-button>
      </ng-container>
    </app-modal>
  `,
})
export class StockInFormModalComponent implements OnInit {
  private readonly service = inject(InventoryService);
  private readonly toast = inject(ToastService);

  readonly saved = output<InvStockIn>();
  readonly closed = output<void>();

  protected readonly qty = formatQty;
  protected readonly item = signal<InvItem | null>(null);
  protected readonly saving = signal(false);
  protected readonly submitted = signal(false);
  protected readonly suppliers = signal<SelectOption[]>([]);
  protected readonly supplierOptions = computed<SelectOption[]>(() => [{ value: '', label: 'No supplier' }, ...this.suppliers()]);
  protected readonly entered = signal(0);
  protected readonly after = computed(() => {
    const i = this.item();
    return i ? `${formatQty(i.currentQuantity + this.entered())} ${i.unitCode}` : '';
  });

  protected readonly form = new FormGroup({
    quantity: new FormControl('', { nonNullable: true, validators: [Validators.required, qty] }),
    unitCost: new FormControl('', { nonNullable: true, validators: [Validators.required, cost] }),
    receivedDate: new FormControl(todayIso(), { nonNullable: true, validators: [Validators.required] }),
    supplierId: new FormControl('', { nonNullable: true }),
    billNumber: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(50)] }),
    expiryDate: new FormControl('', { nonNullable: true }),
    remarks: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(500)] }),
  });

  ngOnInit(): void {
    this.service.suppliers(false).subscribe({ next: (r) => this.suppliers.set(r.items.filter((s) => s.isActive).map((s) => ({ value: String(s.supplierId), label: s.supplierName }))), error: () => undefined });
    this.form.controls.quantity.valueChanges.subscribe((v) => this.entered.set(quantityError(v) || !v ? 0 : Number(v)));
  }

  protected onPicked(i: InvItem | null): void {
    this.item.set(i);
    if (i && !this.form.controls.unitCost.value) this.form.controls.unitCost.setValue(String(i.purchasePrice));   // a starting point: the last cost
  }

  protected error(name: 'quantity' | 'unitCost' | 'billNumber'): string | undefined {
    const c = this.form.controls[name];
    if (c.valid || !(c.touched || this.submitted())) return undefined;
    if (c.hasError('required')) return 'This field is required';
    if (c.hasError('maxlength')) return `Maximum ${c.getError('maxlength').requiredLength} characters`;
    return c.getError('qty') ?? c.getError('cost') ?? 'Invalid value';
  }

  protected dateError(): string | undefined {
    const v = this.form.controls.receivedDate.value;
    if (!v) return this.submitted() ? 'This field is required' : undefined;
    return v > todayIso() ? 'The received date can not be in the future' : undefined;
  }

  protected expiryError(): string | undefined {
    const { expiryDate, receivedDate } = this.form.getRawValue();
    return expiryDate && receivedDate && expiryDate <= receivedDate ? 'The expiry date must be after the received date' : undefined;
  }

  protected submit(): void {
    if (this.saving()) return;
    this.submitted.set(true);
    this.form.markAllAsTouched();
    const v = this.form.getRawValue();
    const picked = this.item();
    if (!picked || this.form.invalid || this.dateError() || this.expiryError()) return;
    this.saving.set(true);
    this.service
      .createStockIn({
        itemId: picked.itemId, supplierId: v.supplierId ? Number(v.supplierId) : null, quantity: Number(v.quantity), unitCost: Number(v.unitCost), receivedDate: v.receivedDate,
        billNumber: v.billNumber.trim() || null, expiryDate: v.expiryDate || null, remarks: v.remarks.trim() || null,
      })
      .pipe(finalize(() => this.saving.set(false)))
      .subscribe({
        next: (saved) => { this.toast.success(`Added ${formatQty(saved.quantity)} ${saved.unitCode} to ${saved.itemName}.`); this.saved.emit(saved); },
        error: () => undefined,
      });
  }
}
