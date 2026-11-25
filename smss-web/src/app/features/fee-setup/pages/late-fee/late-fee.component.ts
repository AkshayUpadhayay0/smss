import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { finalize } from 'rxjs';
import { ToastService } from '../../../../core/services/toast.service';
import { ButtonComponent, CardComponent, PageHeaderComponent } from '../../../../shared/components';
import { LATE_FEE_RULE_LABELS, LateFeeRule, LateFeeRuleType } from '../../models/late-fee.model';
import { LateFeeService } from '../../services/late-fee.service';

/**
 * Fee Setup, Late fee (School Admin): whether the school charges a late fee at payment, how it is worked out, the grace days, a cap per installment, whether tax also applies to it, and which fee types it applies
 * to (all off until chosen). A new rule applies from the next payment; receipts already issued never change. This screen only saves settings: it charges nothing by itself.
 */
@Component({
  selector: 'app-late-fee',
  imports: [ReactiveFormsModule, ButtonComponent, CardComponent, PageHeaderComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './late-fee.component.scss',
  templateUrl: './late-fee.component.html',
})
export class LateFeeComponent implements OnInit {
  private readonly api = inject(LateFeeService);
  private readonly toast = inject(ToastService);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly ruleTypes = (Object.keys(LATE_FEE_RULE_LABELS) as LateFeeRuleType[]).map((v) => ({ value: v, label: LATE_FEE_RULE_LABELS[v] }));
  protected readonly loading = signal(true);
  protected readonly loadFailed = signal(false);
  protected readonly saving = signal(false);
  protected readonly saved = signal<LateFeeRule | null>(null);
  protected readonly flagged = signal<ReadonlySet<number>>(new Set());
  protected readonly errors = signal<string[]>([]);

  protected readonly form = new FormGroup({
    enabled: new FormControl(false, { nonNullable: true }),
    ruleType: new FormControl<LateFeeRuleType | ''>('', { nonNullable: true }),
    amount: new FormControl<number | null>(null),
    percent: new FormControl<number | null>(null),
    graceDays: new FormControl(0, { nonNullable: true }),
    capPerDue: new FormControl<number | null>(null),
    taxApplies: new FormControl(false, { nonNullable: true }),
  });
  private readonly typeSignal = signal<LateFeeRuleType | ''>('');
  protected readonly isAmount = computed(() => this.typeSignal() === 'FLAT' || this.typeSignal() === 'PER_DAY');
  protected readonly isPercent = computed(() => this.typeSignal() === 'PERCENT');

  ngOnInit(): void {
    this.form.controls.ruleType.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((v) => this.typeSignal.set(v));
    this.api
      .getRule()
      .pipe(finalize(() => this.loading.set(false)), takeUntilDestroyed(this.destroyRef))
      .subscribe({ next: (r) => this.apply(r), error: () => this.loadFailed.set(true) });
  }

  private apply(r: LateFeeRule): void {
    this.saved.set(r);
    this.form.reset({ enabled: r.enabled, ruleType: r.ruleType ?? '', amount: r.amount, percent: r.percent, graceDays: r.graceDays, capPerDue: r.capPerDue, taxApplies: r.taxApplies });
    this.typeSignal.set(r.ruleType ?? '');
    this.flagged.set(new Set(r.feeTypes.filter((f) => f.appliesLateFee).map((f) => f.feeTypeId)));
  }

  protected toggleType(id: number, on: boolean): void {
    this.flagged.update((s) => { const n = new Set(s); if (on) n.add(id); else n.delete(id); return n; });
  }

  /** The same checks the server makes, so a mistake is told here before anything is sent. */
  private validate(): string[] {
    const v = this.form.getRawValue();
    const e: string[] = [];
    if (v.graceDays == null || v.graceDays < 0 || v.graceDays > 365 || !Number.isInteger(v.graceDays)) e.push('Grace days must be a whole number from 0 to 365.');
    if (v.capPerDue != null && v.capPerDue <= 0) e.push('The cap must be above 0 (leave it empty for no cap).');
    if (v.enabled) {
      if (!v.ruleType) e.push('Choose how the late fee is worked out.');
      if ((v.ruleType === 'FLAT' || v.ruleType === 'PER_DAY') && !(v.amount != null && v.amount > 0)) e.push(v.ruleType === 'FLAT' ? 'Enter the flat late fee amount.' : 'Enter the late fee amount per day.');
      if (v.ruleType === 'PERCENT' && !(v.percent != null && v.percent > 0 && v.percent <= 100)) e.push('Enter a percentage above 0 and up to 100.');
      if (this.flagged().size === 0) e.push('Choose at least one fee type the late fee applies to.');
    }
    return e;
  }

  protected save(): void {
    if (this.saving()) return;
    const errors = this.validate();
    this.errors.set(errors);
    if (errors.length > 0) return;
    const v = this.form.getRawValue();
    this.saving.set(true);
    this.api
      .saveRule({
        enabled: v.enabled, ruleType: v.ruleType || null, amount: v.ruleType === 'PERCENT' ? null : v.amount, percent: v.ruleType === 'PERCENT' ? v.percent : null,
        graceDays: v.graceDays, capPerDue: v.capPerDue, taxApplies: v.taxApplies, feeTypeIds: [...this.flagged()],
      })
      .pipe(finalize(() => this.saving.set(false)), takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (r) => { this.apply(r); this.toast.success('Late fee settings saved. They apply from the next payment.'); },
        error: () => undefined,   // the server's reason is toasted; the form keeps what was typed
      });
  }
}
