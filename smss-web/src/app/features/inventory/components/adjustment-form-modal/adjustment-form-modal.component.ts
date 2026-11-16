import { ChangeDetectionStrategy, Component, OnInit, computed, inject, output, signal } from '@angular/core';
import { AbstractControl, FormControl, FormGroup, ReactiveFormsModule, ValidationErrors, Validators } from '@angular/forms';
import { toSignal } from '@angular/core/rxjs-interop';
import { finalize } from 'rxjs';
import { ToastService } from '../../../../core/services/toast.service';
import { ButtonComponent, InputComponent, ModalComponent, SelectComponent, SelectOption, TextareaComponent } from '../../../../shared/components';
import { InvItem, InvMovement, InvReason, REASON_LABELS, formatQty, quantityError, signedAdjustment } from '../../models/inventory.model';
import { InventoryService } from '../../services/inventory.service';
import { ItemPickerComponent } from '../item-picker/item-picker.component';

const amount = (c: AbstractControl): ValidationErrors | null => { const e = quantityError(c.value); return e ? { amount: e } : null; };
const notBlank = (c: AbstractControl): ValidationErrors | null => (String(c.value ?? '').trim() === '' ? { blank: true } : null);

/**
 * A stock adjustment is a ledger row: a reason, an amount and written remarks. The person types a POSITIVE amount; the direction comes from the reason
 * (damaged / lost / expired take stock out, opening balance puts it in, a count correction asks which way). The server enforces the same rules.
 */
@Component({
  selector: 'app-adjustment-form-modal',
  imports: [ReactiveFormsModule, ButtonComponent, InputComponent, ItemPickerComponent, ModalComponent, SelectComponent, TextareaComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: '../../styles/inventory-page.scss',
  template: `
    <app-modal title="Stock Adjustment" [dismissible]="!saving()" (closed)="closed.emit()">
      <form class="form" [formGroup]="form" (ngSubmit)="submit()" novalidate>
        <app-item-picker [required]="true" (picked)="item.set($event)" [errorText]="submitted() && !item() ? 'Choose an item' : undefined" />
        <div class="grid">
          <app-select label="Reason" [required]="true" [options]="reasonOptions()" placeholder="Choose a reason" formControlName="reason" [errorText]="error('reason')" />
          @if (reason() === 'CORRECTION') {
            <app-select label="The count is" [options]="directionOptions" formControlName="direction" />
          }
          <app-input [label]="amountLabel()" type="number" [required]="true" placeholder="0" formControlName="amount" [errorText]="error('amount')" />
        </div>
        @if (preview(); as p) {
          <p class="note" [class.bad-note]="p.negative">{{ p.text }}</p>
        }
        <app-textarea label="Remarks" [required]="true" placeholder="What happened? e.g. 3 packs water-damaged in the store room" [maxLength]="500" formControlName="remarks" [errorText]="error('remarks')" />
        <p class="note">An adjustment can not be edited or deleted later. If it was wrong, add a correcting adjustment.</p>
        <button type="submit" hidden tabindex="-1"></button>
      </form>
      <ng-container modal-footer>
        <app-button variant="secondary" [disabled]="saving()" (click)="closed.emit()">Cancel</app-button>
        <app-button [disabled]="saving()" (click)="submit()">{{ saving() ? 'Saving…' : 'Record adjustment' }}</app-button>
      </ng-container>
    </app-modal>
  `,
})
export class AdjustmentFormModalComponent implements OnInit {
  private readonly service = inject(InventoryService);
  private readonly toast = inject(ToastService);

  readonly saved = output<InvMovement>();
  readonly closed = output<void>();

  protected readonly item = signal<InvItem | null>(null);
  protected readonly reasons = signal<InvReason[]>([]);
  protected readonly saving = signal(false);
  protected readonly submitted = signal(false);
  protected readonly directionOptions: SelectOption[] = [{ value: 'IN', label: 'higher than the system: add' }, { value: 'OUT', label: 'lower than the system: remove' }];

  protected readonly form = new FormGroup({
    reason: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    direction: new FormControl<'IN' | 'OUT'>('IN', { nonNullable: true }),
    amount: new FormControl('', { nonNullable: true, validators: [Validators.required, amount] }),
    remarks: new FormControl('', { nonNullable: true, validators: [Validators.required, notBlank, Validators.maxLength(500)] }),
  });

  private readonly reasonValue = toSignal(this.form.controls.reason.valueChanges, { initialValue: '' });
  private readonly amountValue = toSignal(this.form.controls.amount.valueChanges, { initialValue: '' });
  private readonly directionValue = toSignal(this.form.controls.direction.valueChanges, { initialValue: 'IN' as 'IN' | 'OUT' });
  protected readonly reason = computed(() => this.reasonValue());
  protected readonly reasonOptions = computed<SelectOption[]>(() => this.reasons().map((r) => ({ value: r.code, label: r.label })));
  protected readonly amountLabel = computed(() => {
    const r = this.reasonValue();
    return r === 'OPENING_BALANCE' ? 'Opening quantity' : r === 'CORRECTION' ? 'Difference' : r ? 'Quantity to remove' : 'Quantity';
  });

  /** What the stock will be after this, so a typo (100 for 10) is seen before it is saved. */
  protected readonly preview = computed(() => {
    const i = this.item(); const r = this.reasonValue(); const raw = this.amountValue();
    if (!i || !r || quantityError(raw) || !raw) return null;
    const change = signedAdjustment(r, Number(raw), this.directionValue());
    const after = i.currentQuantity + change;
    return { negative: after < 0, text: `${formatQty(i.currentQuantity)} ${i.unitCode} now, ${change > 0 ? '+' : ''}${formatQty(change)}, so ${after < 0 ? 'NOT ENOUGH STOCK: it would be ' : ''}${formatQty(after)} ${i.unitCode} after.` };
  });

  ngOnInit(): void {
    this.service.reasons().subscribe({ next: (r) => this.reasons.set(r), error: () => this.reasons.set(Object.entries(REASON_LABELS).map(([code, label]) => ({ code, label, direction: 'EITHER' as const }))) });
  }

  protected error(name: 'reason' | 'amount' | 'remarks'): string | undefined {
    const c = this.form.controls[name];
    if (c.valid || !(c.touched || this.submitted())) return undefined;
    if (c.hasError('required') || c.hasError('blank')) return 'This field is required';
    if (c.hasError('maxlength')) return `Maximum ${c.getError('maxlength').requiredLength} characters`;
    return c.getError('amount') ?? 'Invalid value';
  }

  protected submit(): void {
    if (this.saving()) return;
    this.submitted.set(true);
    this.form.markAllAsTouched();
    const i = this.item();
    if (!i || this.form.invalid) return;
    const v = this.form.getRawValue();
    this.saving.set(true);
    this.service
      .createAdjustment({ itemId: i.itemId, reasonCode: v.reason, quantityChange: signedAdjustment(v.reason, Number(v.amount), v.direction), remarks: v.remarks.trim() })
      .pipe(finalize(() => this.saving.set(false)))
      .subscribe({
        next: (m) => { this.toast.success(`Adjustment recorded. ${m.itemName} is now ${formatQty(m.quantityAfter)} ${m.unitCode}.`); this.saved.emit(m); },
        error: () => undefined,   // e.g. 409 "only N on hand" or "already has an opening balance": toasted, form stays open
      });
  }
}
