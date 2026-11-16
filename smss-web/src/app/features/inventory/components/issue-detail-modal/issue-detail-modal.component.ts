import { ChangeDetectionStrategy, Component, OnInit, computed, inject, input, output, signal } from '@angular/core';
import { AbstractControl, FormControl, ReactiveFormsModule, ValidationErrors, Validators } from '@angular/forms';
import { finalize } from 'rxjs';
import { ConfirmDialogService } from '../../../../core/services/confirm-dialog.service';
import { ToastService } from '../../../../core/services/toast.service';
import { ButtonComponent, IconComponent, InputComponent, ModalComponent, SelectComponent, SelectOption } from '../../../../shared/components';
import { InvIssue, InvIssueLine, RECIPIENT_LABELS, formatDate, formatDateTime, formatQty, issueTone, quantityError, todayIso } from '../../models/inventory.model';
import { InventoryService } from '../../services/inventory.service';
import { VoidReasonModalComponent } from '../void-reason-modal/void-reason-modal.component';

const qty = (c: AbstractControl): ValidationErrors | null => (String(c.value ?? '').trim() === '' ? null : ((e) => (e ? { qty: e } : null))(quantityError(c.value)));

interface ReturnEntry { line: InvIssueLine; quantity: FormControl<string>; condition: FormControl<string>; remarks: FormControl<string> }

/**
 * One issue in full: who got what, what is back, and (for those who can manage) recording a return or voiding the issue.
 * A GOOD return puts the stock back; a DAMAGED one is recorded but adds no stock. Voiding is only possible while nothing has been returned.
 */
@Component({
  selector: 'app-issue-detail-modal',
  imports: [ReactiveFormsModule, ButtonComponent, IconComponent, InputComponent, ModalComponent, SelectComponent, VoidReasonModalComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrls: ['../../styles/inventory-page.scss', '../../styles/inventory-issues.scss'],
  templateUrl: './issue-detail-modal.component.html',
})
export class IssueDetailModalComponent implements OnInit {
  private readonly service = inject(InventoryService);
  private readonly toast = inject(ToastService);
  private readonly confirmDialog = inject(ConfirmDialogService);

  readonly issueId = input.required<number>();
  /** From the server's canManage flag of the list the modal was opened from. */
  readonly canManage = input(false);
  /** Something changed (a return or a void): the list behind should reload. */
  readonly changed = output<void>();
  readonly closed = output<void>();

  protected readonly qty = formatQty;
  protected readonly date = formatDate;
  protected readonly dateTime = formatDateTime;
  protected readonly tone = issueTone;
  protected readonly recipientLabels = RECIPIENT_LABELS;
  protected readonly conditionOptions: SelectOption[] = [{ value: 'GOOD', label: 'Good: back in stock' }, { value: 'DAMAGED', label: 'Damaged: not added to stock' }];

  protected readonly issue = signal<InvIssue | null>(null);
  protected readonly entries = signal<ReturnEntry[]>([]);
  protected readonly loading = signal(true);
  protected readonly failed = signal(false);
  protected readonly saving = signal(false);
  protected readonly voiding = signal(false);
  protected readonly voidBusy = signal(false);
  protected readonly submitted = signal(false);
  protected readonly returnDate = new FormControl(todayIso(), { nonNullable: true, validators: [Validators.required] });

  protected readonly canReturn = computed(() => this.canManage() && !!this.issue() && this.issue()!.status !== 'Voided' && this.issue()!.status !== 'Returned');
  protected readonly canVoid = computed(() => this.canManage() && this.issue()?.status === 'Outstanding');

  ngOnInit(): void {
    this.load();
  }

  protected load(): void {
    this.loading.set(true);
    this.failed.set(false);
    this.service
      .issue(this.issueId())
      .pipe(finalize(() => this.loading.set(false)))
      .subscribe({
        next: (i) => {
          this.issue.set(i);
          this.entries.set(
            i.lines.filter((l) => l.outstandingQuantity > 0).map((line) => ({
              line, quantity: new FormControl('', { nonNullable: true, validators: [qty] }), condition: new FormControl('GOOD', { nonNullable: true }), remarks: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(500)] }),
            })),
          );
          this.submitted.set(false);
        },
        error: () => this.failed.set(true),
      });
  }

  protected entryError(e: ReturnEntry): string | undefined {
    if (!(e.quantity.touched || this.submitted())) return undefined;
    const err = e.quantity.getError('qty');
    if (err) return err;
    if (Number(e.quantity.value) > e.line.outstandingQuantity) return `Only ${formatQty(e.line.outstandingQuantity)} is outstanding`;
    return undefined;
  }

  protected dateError(): string | undefined {
    const v = this.returnDate.value;
    const i = this.issue();
    if (!v) return this.submitted() ? 'This field is required' : undefined;
    if (v > todayIso()) return 'The return date can not be in the future';
    if (i && v < i.issueDate) return 'The return date can not be before the issue date';
    return undefined;
  }

  protected submitReturn(): void {
    if (this.saving()) return;
    this.submitted.set(true);
    const filled = this.entries().filter((e) => String(e.quantity.value).trim() !== '');
    for (const e of this.entries()) e.quantity.markAsTouched();
    if (filled.length === 0) { this.toast.danger('Enter the quantity that came back on at least one line.'); return; }
    if (this.dateError() || filled.some((e) => e.quantity.invalid || !!this.entryError(e) || e.remarks.invalid)) return;
    this.saving.set(true);
    this.service
      .returnIssue(this.issueId(), {
        returnDate: this.returnDate.value,
        lines: filled.map((e) => ({ issueLineId: e.line.issueLineId, quantity: Number(e.quantity.value), condition: e.condition.value, remarks: e.remarks.value.trim() || null })),
      })
      .pipe(finalize(() => this.saving.set(false)))
      .subscribe({
        next: () => { this.toast.success('Return recorded.'); this.changed.emit(); this.load(); },
        error: () => undefined,
      });
  }

  /** Every void is confirmed first, then explained. */
  protected async startVoid(): Promise<void> {
    const i = this.issue();
    if (!i) return;
    const ok = await this.confirmDialog.confirm({
      title: `Void ${i.issueNumber}?`,
      message: `All ${formatQty(i.totalQuantity)} issued item(s) go back into stock and the issue stays in the list, marked Voided. This can only be done while nothing has been returned, and it can not be undone.`,
      confirmText: 'Continue',
      variant: 'danger',
    });
    if (ok) this.voiding.set(true);
  }

  protected confirmVoid(reason: string): void {
    if (this.voidBusy()) return;
    this.voidBusy.set(true);
    this.service
      .voidIssue(this.issueId(), reason)
      .pipe(finalize(() => this.voidBusy.set(false)))
      .subscribe({
        next: () => { this.toast.success('Issue voided and the stock restored.'); this.voiding.set(false); this.changed.emit(); this.load(); },
        error: () => undefined,
      });
  }
}
