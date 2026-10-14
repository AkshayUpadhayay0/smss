import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { AbstractControl, FormArray, FormControl, FormGroup, ReactiveFormsModule, ValidationErrors, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { finalize, forkJoin, map, startWith } from 'rxjs';
import { ToastService } from '../../../../core/services/toast.service';
import { ButtonComponent, CardComponent, IconComponent, InputComponent, PageHeaderComponent, SelectComponent, SelectOption } from '../../../../shared/components';
import { AcademicYear } from '../../../school-setup/models/academic-year.model';
import { SchoolClass } from '../../../school-setup/models/class.model';
import { AcademicYearService } from '../../../school-setup/services/academic-year.service';
import { ClassService } from '../../../school-setup/services/class.service';
import { FeeFrequency, FeeStructure, FeeStructureLineRequest, FeeTypeOption } from '../../models/fee-structure.model';
import { FeeStructureService } from '../../services/fee-structure.service';

const MAX_AMOUNT = 9_999_999_999.99;

interface LineControls {
  feeTypeId: FormControl<string>;
  frequencyId: FormControl<string>;
  amount: FormControl<string>;
}
type LineGroup = FormGroup<LineControls>;

const notBlank = (c: AbstractControl): ValidationErrors | null => (String(c.value ?? '').trim() === '' ? { blank: true } : null);

/** A non-negative amount with at most two decimals (numeric(12,2) in the database). */
function amountValidator(c: AbstractControl): ValidationErrors | null {
  const raw = String(c.value ?? '').trim();
  if (raw === '') return null; // `required` reports the empty case
  const n = Number(raw);
  if (!Number.isFinite(n) || n < 0) return { amount: 'Enter an amount of 0 or more' };
  if (n > MAX_AMOUNT) return { amount: 'Amount is too large' };
  if (!/^\d+(\.\d{1,2})?$/.test(raw)) return { amount: 'Use at most 2 decimal places' };
  return null;
}

const inr = (n: number) => '₹' + n.toLocaleString('en-IN', { minimumFractionDigits: 2, maximumFractionDigits: 2 });

/**
 * Builds (or edits) one fee structure: academic year + class on top, then one row per fee head
 * (fee type, frequency, amount). Everything is saved in a single request.
 */
@Component({
  selector: 'app-fee-structure-builder',
  imports: [ReactiveFormsModule, ButtonComponent, CardComponent, IconComponent, InputComponent, PageHeaderComponent, SelectComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './fee-structure-builder.component.scss',
  templateUrl: './fee-structure-builder.component.html',
})
export class FeeStructureBuilderComponent implements OnInit {
  private readonly service = inject(FeeStructureService);
  private readonly years = inject(AcademicYearService);
  private readonly classes = inject(ClassService);
  private readonly router = inject(Router);
  private readonly toast = inject(ToastService);
  private readonly destroyRef = inject(DestroyRef);

  /** Bound from the :feeStructureId route param; absent when adding. */
  readonly feeStructureId = input<string>();
  protected readonly isEdit = computed(() => !!this.feeStructureId());

  protected readonly form = new FormGroup({
    academicYearId: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    classId: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    structureName: new FormControl('', { nonNullable: true, validators: [Validators.required, notBlank, Validators.maxLength(150)] }),
    lines: new FormArray<LineGroup>([]),
  });
  protected get lines(): FormArray<LineGroup> {
    return this.form.controls.lines;
  }

  protected readonly loading = signal(true);
  protected readonly loadFailed = signal(false);
  protected readonly saving = signal(false);
  protected readonly submitted = signal(false);

  private readonly allYears = signal<AcademicYear[]>([]);
  private readonly allClasses = signal<SchoolClass[]>([]);
  private readonly feeTypes = signal<FeeTypeOption[]>([]);
  protected readonly frequencies = signal<FeeFrequency[]>([]);
  /** The structure being edited (its year / class may be inactive, but must stay visible). */
  private readonly loaded = signal<FeeStructure | null>(null);

  /** The form's current values, re-read on every change (rows added/removed/edited). */
  private readonly value = toSignal(this.form.valueChanges.pipe(map(() => this.form.getRawValue()), startWith(this.form.getRawValue())), {
    requireSync: true,
  });

  protected readonly breadcrumbs = computed(() => [
    { label: 'Fee Setup' },
    { label: 'Fee Structures', link: '/fee-setup/fee-structures' },
    { label: this.isEdit() ? 'Edit' : 'Add' },
  ]);

  protected readonly yearOptions = computed<SelectOption[]>(() => {
    const own = this.loaded()?.academicYearId;
    return this.allYears()
      .filter((y) => y.statusName === 'Active' || y.academicYearId === own)
      .map((y) => ({ label: y.yearName + (y.statusName === 'Active' ? '' : ' (inactive)'), value: String(y.academicYearId) }));
  });

  protected readonly classOptions = computed<SelectOption[]>(() => {
    const own = this.loaded()?.classId;
    return [...this.allClasses()]
      .sort((a, b) => a.sequenceOrder - b.sequenceOrder || a.className.localeCompare(b.className))
      .filter((c) => c.statusName === 'Active' || c.classId === own)
      .map((c) => ({ label: c.className + (c.statusName === 'Active' ? '' : ' (inactive)'), value: String(c.classId) }));
  });

  /** Per row: the active fee types, with the ones another row already uses disabled. */
  protected readonly feeTypeOptions = computed<SelectOption[][]>(() => {
    const picked = this.value().lines.map((l) => l.feeTypeId);
    return picked.map((own, i) =>
      this.feeTypes()
        .filter((t) => t.isActive || String(t.feeTypeId) === own)
        .map((t) => ({
          label: t.feeTypeName + (t.isActive ? '' : ' (inactive)'),
          value: String(t.feeTypeId),
          disabled: picked.some((p, j) => j !== i && p === String(t.feeTypeId)),
        })),
    );
  });

  protected readonly frequencyOptions = computed<SelectOption[]>(() =>
    this.frequencies().map((f) => ({ label: f.frequencyName, value: String(f.frequencyId) })),
  );

  /** Subtotals per frequency: a monthly fee and an annual one are never added together. */
  protected readonly totals = computed(() => {
    const sums = new Map<string, number>();
    for (const l of this.value().lines) {
      const amount = Number(l.amount);
      if (!l.frequencyId || l.amount.trim() === '' || !Number.isFinite(amount)) continue;
      sums.set(l.frequencyId, Math.round(((sums.get(l.frequencyId) ?? 0) + amount) * 100) / 100);
    }
    const names = new Map(this.frequencies().map((f) => [String(f.frequencyId), f.frequencyName]));
    return this.frequencies()
      .map((f) => String(f.frequencyId))
      .filter((id) => sums.has(id))
      .map((id) => ({ frequency: names.get(id) ?? '', amount: inr(sums.get(id)!) }));
  });

  protected readonly hasNoFeeTypes = computed(() => !this.loading() && this.feeTypes().filter((t) => t.isActive).length === 0);

  constructor() {
    // Suggest a name once year and class are chosen — until the user types their own
    const maybeSuggest = () => {
      const name = this.form.controls.structureName;
      if (this.isEdit() || name.dirty) return;
      const cls = this.allClasses().find((c) => String(c.classId) === this.form.controls.classId.value);
      const year = this.allYears().find((y) => String(y.academicYearId) === this.form.controls.academicYearId.value);
      if (cls && year) name.setValue(`${cls.className} Fees ${year.yearName}`);
    };
    this.form.controls.classId.valueChanges.pipe(takeUntilDestroyed()).subscribe(maybeSuggest);
    this.form.controls.academicYearId.valueChanges.pipe(takeUntilDestroyed()).subscribe(maybeSuggest);
  }

  ngOnInit(): void {
    this.load();
  }

  protected load(): void {
    this.loading.set(true);
    this.loadFailed.set(false);
    const id = this.feeStructureId();

    forkJoin({
      years: this.years.list(),
      classes: this.classes.list(),
      feeTypes: this.service.feeTypes(),
      frequencies: this.service.frequencies(),
      structure: id ? this.service.get(Number(id)) : [null],
    })
      .pipe(finalize(() => this.loading.set(false)), takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (r) => {
          this.allYears.set(r.years);
          this.allClasses.set(r.classes);
          this.feeTypes.set(r.feeTypes);
          this.frequencies.set(r.frequencies);

          const s = r.structure as FeeStructure | null;
          if (s) {
            this.loaded.set(s);
            this.lines.clear();
            this.form.patchValue({ academicYearId: String(s.academicYearId), classId: String(s.classId), structureName: s.structureName });
            for (const d of s.details) this.lines.push(this.newLine(String(d.feeTypeId), String(d.frequencyId), String(d.amount)));
            // A structure belongs to ONE class and year; changing them would be a different structure
            this.form.controls.academicYearId.disable();
            this.form.controls.classId.disable();
          } else if (this.lines.length === 0) {
            this.addLine();
          }
        },
        error: () => this.loadFailed.set(true), // already toasted by the interceptor
      });
  }

  private newLine(feeTypeId = '', frequencyId = '', amount = ''): LineGroup {
    return new FormGroup<LineControls>({
      feeTypeId: new FormControl(feeTypeId, { nonNullable: true, validators: [Validators.required] }),
      frequencyId: new FormControl(frequencyId, { nonNullable: true, validators: [Validators.required] }),
      amount: new FormControl(amount, { nonNullable: true, validators: [Validators.required, amountValidator] }),
    });
  }

  protected addLine(): void {
    this.lines.push(this.newLine());
  }

  protected removeLine(index: number): void {
    if (this.lines.length > 1) this.lines.removeAt(index);
  }

  protected error(control: AbstractControl | null): string | undefined {
    if (!control || control.valid || !(control.touched || this.submitted())) return undefined;
    if (control.hasError('required') || control.hasError('blank')) return 'This field is required';
    if (control.hasError('maxlength')) return `Maximum ${control.getError('maxlength').requiredLength} characters`;
    if (control.hasError('amount')) return control.getError('amount');
    return 'Invalid value';
  }

  protected lineError(index: number, name: keyof LineControls): string | undefined {
    return this.error(this.lines.at(index).controls[name]);
  }

  protected cancel(): void {
    void this.router.navigate(['/fee-setup/fee-structures']);
  }

  protected save(): void {
    if (this.saving()) return;
    this.submitted.set(true);
    this.form.markAllAsTouched();
    if (this.form.invalid) return;

    const v = this.form.getRawValue();
    const details: FeeStructureLineRequest[] = v.lines.map((l) => ({
      feeTypeId: Number(l.feeTypeId),
      frequencyId: Number(l.frequencyId),
      amount: Number(l.amount),
    }));
    const structureName = v.structureName.trim();

    this.saving.set(true);
    const request$ = this.isEdit()
      ? this.service.update(Number(this.feeStructureId()), { structureName, details })
      : this.service.create({ academicYearId: Number(v.academicYearId), classId: Number(v.classId), structureName, details });

    request$.pipe(finalize(() => this.saving.set(false))).subscribe({
      next: () => {
        this.toast.success(`Fee structure ${this.isEdit() ? 'updated' : 'created'} successfully.`);
        void this.router.navigate(['/fee-setup/fee-structures']);
      },
      error: () => undefined, // the server's reason (e.g. 409 duplicate class + year) is toasted; the form stays as it is
    });
  }
}
