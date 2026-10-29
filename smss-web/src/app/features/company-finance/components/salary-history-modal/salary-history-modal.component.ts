import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, inject, input, output, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule, Validators } from '@angular/forms';
import { finalize } from 'rxjs';
import { ToastService } from '../../../../core/services/toast.service';
import { formatInr } from '../../../../core/utils/currency';
import { ButtonComponent, ModalComponent, TextareaComponent } from '../../../../shared/components';
import { formatDate } from '../../../admissions/models/admission.model';
import { CompanyEmployee, CompanySalaryPayment, periodLabel } from '../../models/company-finance.model';
import { CompanyFinanceService } from '../../services/company-finance.service';

/** Every salary payment of one employee, newest month first (voided ones struck through). An Active payment can be voided with a reason. */
@Component({
  selector: 'app-salary-history-modal',
  imports: [ReactiveFormsModule, ButtonComponent, ModalComponent, TextareaComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styles: `
    .state { margin: 0; padding: var(--space-6); text-align: center; color: var(--color-text-muted); }
    .muted { color: var(--color-text-muted); font-size: var(--text-sm); }
    ul { margin: 0; padding: 0; list-style: none; }
    li { padding: var(--space-3) 0; border-bottom: 1px solid var(--color-border); font-size: var(--text-sm); }
    li:last-child { border-bottom: 0; }
    .line { display: flex; flex-wrap: wrap; align-items: center; gap: var(--space-3); }
    .period { font-weight: var(--weight-semibold); min-width: 80px; }
    .amt { font-variant-numeric: tabular-nums; font-weight: var(--weight-semibold); }
    .when { margin-left: auto; color: var(--color-text-muted); }
    .voided .period, .voided .amt { text-decoration: line-through; color: var(--color-text-muted); }
    .tag { padding: 2px var(--space-2); border-radius: 999px; background: var(--color-status-inactive-bg); color: var(--color-status-inactive-fg); font-size: var(--text-xs); }
    small { display: block; margin-top: var(--space-1); color: var(--color-text-muted); }
    .void { margin-top: var(--space-3); }
    .void-actions { display: flex; justify-content: flex-end; gap: var(--space-2); margin-top: var(--space-2); }
    .total { margin: var(--space-3) 0 0; text-align: right; font-size: var(--text-sm); }
  `,
  template: `
    <app-modal [title]="'Salary payments: ' + employee().fullName" [dismissible]="!voidBusy()" (closed)="closed.emit()">
      @if (failed()) {
        <p class="state">The payments could not be loaded.</p>
      } @else if (loading()) {
        <p class="state">Loading…</p>
      } @else if (payments().length === 0) {
        <p class="state">No salary payments recorded yet.</p>
      } @else {
        <ul>
          @for (p of payments(); track p.salaryPaymentId) {
            <li [class.voided]="p.status === 'Voided'">
              <div class="line">
                <span class="period">{{ period(p) }}</span>
                <span class="amt">{{ inr(p.amount) }}</span>
                <span>{{ p.paymentMode }}</span>
                <time class="when">paid {{ date(p.paymentDate) }}</time>
                @if (p.status === 'Active') {
                  <app-button variant="danger-ghost" size="sm" [disabled]="voidBusy()" (click)="startVoid(p)">Void</app-button>
                } @else {
                  <span class="tag">Voided</span>
                }
              </div>
              @if (p.remarks) { <small>{{ p.remarks }}</small> }
              @if (p.status === 'Voided') { <small>Voided: {{ p.voidReason }}</small> }
              @if (voidingId() === p.salaryPaymentId) {
                <form class="void" (ngSubmit)="confirmVoid()" novalidate>
                  <app-textarea label="Reason for voiding" [required]="true" [rows]="2" [maxLength]="500" [formControl]="voidReason"
                    [errorText]="voidSubmitted() && voidReason.invalid ? 'A reason is required' : undefined" />
                  <div class="void-actions">
                    <app-button variant="secondary" size="sm" [disabled]="voidBusy()" (click)="voidingId.set(null)">Cancel</app-button>
                    <app-button variant="danger" size="sm" [disabled]="voidBusy()" (click)="confirmVoid()">{{ voidBusy() ? 'Voiding…' : 'Void payment' }}</app-button>
                  </div>
                </form>
              }
            </li>
          }
        </ul>
        <p class="total">Paid in total (Active payments): <strong>{{ inr(activeTotal()) }}</strong></p>
      }
      <ng-container modal-footer>
        <app-button variant="secondary" (click)="closed.emit()">Close</app-button>
      </ng-container>
    </app-modal>
  `,
})
export class SalaryHistoryModalComponent implements OnInit {
  private readonly service = inject(CompanyFinanceService);
  private readonly toast = inject(ToastService);
  private readonly destroyRef = inject(DestroyRef);

  readonly employee = input.required<CompanyEmployee>();
  /** A payment was voided, so the overview numbers changed. */
  readonly changed = output<void>();
  readonly closed = output<void>();

  protected readonly inr = formatInr;
  protected readonly date = formatDate;
  protected readonly period = (p: CompanySalaryPayment): string => periodLabel(p.periodMonth, p.periodYear);

  protected readonly payments = signal<CompanySalaryPayment[]>([]);
  protected readonly activeTotal = signal(0);
  protected readonly loading = signal(true);
  protected readonly failed = signal(false);

  protected readonly voidingId = signal<number | null>(null);
  protected readonly voidReason = new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.maxLength(500)] });
  protected readonly voidSubmitted = signal(false);
  protected readonly voidBusy = signal(false);

  ngOnInit(): void {
    this.load();
  }

  private load(): void {
    this.loading.set(true);
    this.service
      .salaryPaymentsOf(this.employee().companyEmployeeId)
      .pipe(finalize(() => this.loading.set(false)), takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (p) => { this.payments.set(p.items); this.activeTotal.set(p.activeTotal); this.failed.set(false); },
        error: () => this.failed.set(true),
      });
  }

  protected startVoid(p: CompanySalaryPayment): void {
    this.voidingId.set(p.salaryPaymentId);
    this.voidReason.reset('');
    this.voidSubmitted.set(false);
  }

  protected confirmVoid(): void {
    const id = this.voidingId();
    if (id === null || this.voidBusy()) return;
    this.voidSubmitted.set(true);
    const reason = this.voidReason.value.trim();
    if (!reason) return;
    this.voidBusy.set(true);
    this.service
      .voidSalary(id, reason)
      .pipe(finalize(() => this.voidBusy.set(false)))
      .subscribe({
        next: () => {
          this.toast.success('Salary payment voided. That month can be paid again.');
          this.voidingId.set(null);
          this.changed.emit();
          this.load();
        },
        error: () => undefined,
      });
  }
}
