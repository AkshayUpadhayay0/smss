import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, output, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { AbstractControl, FormControl, FormGroup, ReactiveFormsModule, ValidationErrors, Validators } from '@angular/forms';
import { debounceTime, distinctUntilChanged, finalize } from 'rxjs';
import { ToastService } from '../../../../core/services/toast.service';
import { ButtonComponent, IconComponent, InputComponent, ModalComponent, SelectComponent, SelectOption, TextareaComponent } from '../../../../shared/components';
import { InvIssue, InvItem, InvRecipient, InvRecipientType, RECIPIENT_LABELS, formatQty, quantityError, todayIso } from '../../models/inventory.model';
import { InventoryService } from '../../services/inventory.service';
import { ItemPickerComponent } from '../item-picker/item-picker.component';

const qty = (c: AbstractControl): ValidationErrors | null => { const e = quantityError(c.value); return e ? { qty: e } : null; };

interface IssueLine { id: number; item: InvItem | null; quantity: FormControl<string> }
let nextLineId = 1;

/**
 * Issue items to a staff member, a student or a department. The recipient list is the server's (this school's ACTIVE people only, at most 20 per search).
 * Nothing is written unless EVERY line can be issued: the server answers 409 naming the item that is short and issues none of them.
 */
@Component({
  selector: 'app-issue-form-modal',
  imports: [ReactiveFormsModule, ButtonComponent, IconComponent, InputComponent, ItemPickerComponent, ModalComponent, SelectComponent, TextareaComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrls: ['../../styles/inventory-page.scss', '../../styles/inventory-issues.scss'],
  templateUrl: './issue-form-modal.component.html',
})
export class IssueFormModalComponent implements OnInit {
  private readonly service = inject(InventoryService);
  private readonly toast = inject(ToastService);
  private readonly destroyRef = inject(DestroyRef);

  readonly saved = output<InvIssue>();
  readonly closed = output<void>();

  protected readonly typeOptions: SelectOption[] = (Object.keys(RECIPIENT_LABELS) as InvRecipientType[]).map((t) => ({ value: t, label: RECIPIENT_LABELS[t] }));
  protected readonly qty = formatQty;
  protected readonly saving = signal(false);
  protected readonly submitted = signal(false);
  protected readonly recipients = signal<InvRecipient[]>([]);
  protected readonly searching = signal(false);
  protected readonly lines = signal<IssueLine[]>([this.newLine()]);
  protected readonly recipientOptions = computed<SelectOption[]>(() => this.recipients().map((r) => ({ value: r.id, label: r.detail ? `${r.name} (${r.detail})` : r.name })));
  protected readonly usedItemIds = computed(() => this.lines().map((l) => l.item?.itemId).filter((id): id is number => id != null));

  protected readonly form = new FormGroup({
    recipientType: new FormControl<InvRecipientType>('STAFF', { nonNullable: true, validators: [Validators.required] }),
    recipientSearch: new FormControl('', { nonNullable: true }),
    recipientId: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    issueDate: new FormControl(todayIso(), { nonNullable: true, validators: [Validators.required] }),
    purpose: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(1000)] }),
  });

  private newLine(): IssueLine {
    return { id: nextLineId++, item: null, quantity: new FormControl('', { nonNullable: true, validators: [Validators.required, qty] }) };
  }

  ngOnInit(): void {
    const f = this.form.controls;
    // a new recipient type starts a new search; typing refines it
    f.recipientType.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => { f.recipientId.setValue(''); f.recipientSearch.setValue('', { emitEvent: false }); this.search(); });
    f.recipientSearch.valueChanges.pipe(debounceTime(300), distinctUntilChanged(), takeUntilDestroyed(this.destroyRef)).subscribe(() => this.search());
    this.search();
  }

  private search(): void {
    const f = this.form.getRawValue();
    this.searching.set(true);
    this.service
      .recipients(f.recipientType, f.recipientSearch)
      .pipe(finalize(() => this.searching.set(false)))
      .subscribe({ next: (r) => this.recipients.set(r), error: () => this.recipients.set([]) });
  }

  protected addLine(): void { this.lines.update((l) => [...l, this.newLine()]); }
  protected removeLine(id: number): void { this.lines.update((l) => (l.length > 1 ? l.filter((x) => x.id !== id) : l)); }
  protected pick(line: IssueLine, item: InvItem | null): void { this.lines.update((l) => l.map((x) => (x.id === line.id ? { ...x, item } : x))); }

  protected lineError(line: IssueLine): string | undefined {
    const c = line.quantity;
    if (!(c.touched || this.submitted())) return undefined;
    if (c.hasError('required')) return 'Enter a quantity';
    const e = c.getError('qty');
    if (e) return e;
    if (line.item && Number(c.value) > line.item.currentQuantity) return `Only ${formatQty(line.item.currentQuantity)} ${line.item.unitCode} in stock`;
    return undefined;
  }

  protected recipientLabel(): string { return RECIPIENT_LABELS[this.form.controls.recipientType.value].toLowerCase(); }

  protected dateError(): string | undefined {
    const v = this.form.controls.issueDate.value;
    if (!v) return this.submitted() ? 'This field is required' : undefined;
    return v > todayIso() ? 'The issue date can not be in the future' : undefined;
  }

  protected submit(): void {
    if (this.saving()) return;
    this.submitted.set(true);
    this.form.markAllAsTouched();
    for (const l of this.lines()) l.quantity.markAsTouched();
    const lines = this.lines();
    const v = this.form.getRawValue();
    const bad = this.form.controls.recipientId.invalid || this.form.controls.issueDate.invalid || this.form.controls.purpose.invalid || !!this.dateError()
      || lines.some((l) => !l.item || l.quantity.invalid || !!this.lineError(l));
    if (bad) return;
    this.saving.set(true);
    this.service
      .createIssue({
        recipientType: v.recipientType, recipientId: v.recipientId, issueDate: v.issueDate, purpose: v.purpose.trim() || null,
        lines: lines.map((l) => ({ itemId: l.item!.itemId, quantity: Number(l.quantity.value) })),
      })
      .pipe(finalize(() => this.saving.set(false)))
      .subscribe({
        next: (issue) => { this.toast.success(`Issued as ${issue.issueNumber}.`); this.saved.emit(issue); },
        error: () => undefined,   // 409 "Not enough stock of X: n available" is toasted by the interceptor; nothing was issued and the form stays open
      });
  }
}
