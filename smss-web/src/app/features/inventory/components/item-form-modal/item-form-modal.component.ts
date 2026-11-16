import { ChangeDetectionStrategy, Component, OnInit, computed, inject, input, output, signal } from '@angular/core';
import { AbstractControl, FormControl, FormGroup, ReactiveFormsModule, ValidationErrors, Validators } from '@angular/forms';
import { finalize } from 'rxjs';
import { ToastService } from '../../../../core/services/toast.service';
import { ButtonComponent, IconComponent, InputComponent, ModalComponent, SecureImageComponent, SelectComponent, SelectOption, TextareaComponent } from '../../../../shared/components';
import { InvCategory, InvItem, InvUnit, SaveInvItem, moneyError, quantityError } from '../../models/inventory.model';
import { InventoryService } from '../../services/inventory.service';

const notBlank = (c: AbstractControl): ValidationErrors | null => (String(c.value ?? '').trim() === '' ? { blank: true } : null);
const money = (c: AbstractControl): ValidationErrors | null => { const e = moneyError(c.value); return e ? { money: e } : null; };
const gst = (c: AbstractControl): ValidationErrors | null => {
  const raw = String(c.value ?? '').trim();
  if (raw === '') return null;
  const n = Number(raw);
  if (!Number.isFinite(n) || n < 0 || n > 100) return { gst: 'Between 0 and 100' };
  return /^\d+(\.\d{1,2})?$/.test(raw) ? null : { gst: 'Use at most 2 decimal places' };
};
const minLevel = (c: AbstractControl): ValidationErrors | null => {
  const raw = String(c.value ?? '').trim();
  if (raw === '' || Number(raw) === 0) return null;   // 0 = "no minimum"
  const e = quantityError(raw);
  return e ? { minLevel: e } : null;
};

const PHOTO_TYPES = ['image/png', 'image/jpeg', 'image/webp'];
const PHOTO_MAX = 2 * 1024 * 1024;

/**
 * Add / edit an item. There is NO quantity field: stock changes only through Add Stock, Adjustments (opening balance), Issue and Return.
 * The photo (edit only, after the item exists) goes to private storage and is shown through the authenticated image component.
 */
@Component({
  selector: 'app-item-form-modal',
  imports: [ReactiveFormsModule, ButtonComponent, IconComponent, InputComponent, ModalComponent, SecureImageComponent, SelectComponent, TextareaComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrls: ['../../styles/inventory-page.scss', '../../styles/inventory-items.scss'],
  templateUrl: './item-form-modal.component.html',
})
export class ItemFormModalComponent implements OnInit {
  private readonly service = inject(InventoryService);
  private readonly toast = inject(ToastService);

  /** The item being edited, or null to create. */
  readonly item = input<InvItem | null>(null);
  readonly categories = input<InvCategory[]>([]);
  readonly units = input<InvUnit[]>([]);
  readonly saved = output<InvItem>();
  /** The photo changed (the form stays open). */
  readonly photoChanged = output<InvItem>();
  readonly closed = output<void>();

  protected readonly current = signal<InvItem | null>(null);
  protected readonly isEdit = computed(() => this.item() !== null);
  protected readonly saving = signal(false);
  protected readonly photoBusy = signal(false);
  protected readonly submitted = signal(false);
  protected readonly photoUrl = computed(() => {
    const i = this.current();
    return i?.hasPhoto ? this.service.photoPath(i.itemId) : null;
  });

  protected readonly categoryOptions = computed<SelectOption[]>(() => {
    const own = this.item()?.categoryId;
    return this.categories().filter((c) => c.isActive || c.categoryId === own).map((c) => ({ value: String(c.categoryId), label: c.categoryName + (c.isActive ? '' : ' (inactive)') }));
  });
  protected readonly unitOptions = computed<SelectOption[]>(() => this.units().map((u) => ({ value: String(u.unitId), label: `${u.unitName} (${u.unitCode})` })));
  protected readonly typeOptions: SelectOption[] = [{ value: 'CONSUMABLE', label: 'Consumable (used up)' }, { value: 'ASSET', label: 'Asset (kept and returned)' }];

  protected readonly form = new FormGroup({
    itemName: new FormControl('', { nonNullable: true, validators: [Validators.required, notBlank, Validators.maxLength(150)] }),
    categoryId: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    unitId: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    itemType: new FormControl('CONSUMABLE', { nonNullable: true, validators: [Validators.required] }),
    barcode: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(100)] }),
    purchasePrice: new FormControl('', { nonNullable: true, validators: [money] }),
    isSaleable: new FormControl(false, { nonNullable: true }),
    sellingPrice: new FormControl('', { nonNullable: true, validators: [money] }),
    gstPercent: new FormControl('', { nonNullable: true, validators: [gst] }),
    minStockLevel: new FormControl('', { nonNullable: true, validators: [minLevel] }),
    expiryDate: new FormControl('', { nonNullable: true }),
    description: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(2000)] }),
  });

  ngOnInit(): void {
    const i = this.item();
    this.current.set(i);
    if (i) {
      this.form.patchValue({
        itemName: i.itemName, categoryId: String(i.categoryId), unitId: String(i.unitId), itemType: i.itemType, barcode: i.barcode ?? '', purchasePrice: String(i.purchasePrice),
        isSaleable: i.isSaleable, sellingPrice: i.sellingPrice == null ? '' : String(i.sellingPrice), gstPercent: String(i.gstPercent), minStockLevel: String(i.minStockLevel),
        expiryDate: i.expiryDate ?? '', description: i.description ?? '',
      });
    }
  }

  protected error(name: string): string | undefined {
    const c = this.form.get(name);
    if (!c || c.valid || !(c.touched || this.submitted())) return undefined;
    if (c.hasError('required') || c.hasError('blank')) return 'This field is required';
    if (c.hasError('maxlength')) return `Maximum ${c.getError('maxlength').requiredLength} characters`;
    return c.getError('money') ?? c.getError('gst') ?? c.getError('minLevel') ?? 'Invalid value';
  }

  protected sellingError(): string | undefined {
    const v = this.form.getRawValue();
    if (v.isSaleable && v.sellingPrice.trim() === '' && this.submitted()) return 'A saleable item needs a selling price';
    return this.error('sellingPrice');
  }

  protected submit(): void {
    if (this.saving()) return;
    this.submitted.set(true);
    this.form.markAllAsTouched();
    const v = this.form.getRawValue();
    if (this.form.invalid || (v.isSaleable && v.sellingPrice.trim() === '')) return;

    const body: SaveInvItem = {
      itemName: v.itemName.trim(), barcode: v.barcode.trim() || null, description: v.description.trim() || null, categoryId: Number(v.categoryId), unitId: Number(v.unitId),
      itemType: v.itemType, purchasePrice: Number(v.purchasePrice || 0), sellingPrice: v.sellingPrice.trim() === '' ? null : Number(v.sellingPrice), isSaleable: v.isSaleable,
      gstPercent: Number(v.gstPercent || 0), minStockLevel: Number(v.minStockLevel || 0), expiryDate: v.expiryDate || null,
    };
    const editing = this.item();
    this.saving.set(true);
    this.service
      .saveItem(editing?.itemId ?? null, body)
      .pipe(finalize(() => this.saving.set(false)))
      .subscribe({
        next: (saved) => { this.toast.success(editing ? 'Item updated.' : 'Item created. Put stock on it with Adjustments (opening balance) or Add Stock.'); this.saved.emit(saved); },
        error: () => undefined, // toasted by the interceptor (duplicate barcode, missing category...); the form stays open
      });
  }

  // ---------------------------------------------------------------- photo
  protected onPhotoPicked(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    input.value = '';
    const item = this.current();
    if (!file || !item || this.photoBusy()) return;
    if (!PHOTO_TYPES.includes(file.type)) { this.toast.danger('Only PNG, JPG or WEBP images are allowed.'); return; }
    if (file.size > PHOTO_MAX) { this.toast.danger('The photo must be 2 MB or smaller.'); return; }
    this.photoBusy.set(true);
    this.service
      .uploadPhoto(item.itemId, file)
      .pipe(finalize(() => this.photoBusy.set(false)))
      .subscribe({
        next: (updated) => { this.service.bumpPhoto(updated.itemId); this.current.set(updated); this.photoChanged.emit(updated); this.toast.success('Photo saved.'); },
        error: (e) => this.toast.danger(e?.message ?? 'The photo could not be saved.'),
      });
  }

  protected removePhoto(): void {
    const item = this.current();
    if (!item || this.photoBusy()) return;
    this.photoBusy.set(true);
    this.service
      .removePhoto(item.itemId)
      .pipe(finalize(() => this.photoBusy.set(false)))
      .subscribe({
        next: (updated) => { this.service.bumpPhoto(updated.itemId); this.current.set(updated); this.photoChanged.emit(updated); this.toast.success('Photo removed.'); },
        error: () => undefined,
      });
  }
}
