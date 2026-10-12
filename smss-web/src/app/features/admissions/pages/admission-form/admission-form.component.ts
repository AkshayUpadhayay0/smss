import { ChangeDetectionStrategy, Component, DestroyRef, ElementRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { AbstractControl, FormControl, FormGroup, ReactiveFormsModule, ValidationErrors, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { Observable, catchError, finalize, of, switchMap, tap } from 'rxjs';
import { MasterDataService } from '../../../../core/services/master-data.service';
import { ToastService } from '../../../../core/services/toast.service';
import {
  Breadcrumb,
  ButtonComponent,
  CardComponent,
  IconComponent,
  InputComponent,
  PageHeaderComponent,
  SelectComponent,
  SelectOption,
} from '../../../../shared/components';
import { AcademicYearService } from '../../../school-setup/services/academic-year.service';
import { ClassService } from '../../../school-setup/services/class.service';
import { SchoolLookupService } from '../../../school-setup/services/school-lookup.service';
import { PINCODE_RE, firstErrorMessage, mobileValidator, pattern, requiredTrimmed } from '../../../schools/utils/school-validators';
import { GUARDIAN_RELATIONSHIPS, SubmitAdmissionRequest } from '../../models/admission.model';
import { AdmissionService } from '../../services/admission.service';

const AADHAAR_RE = /^[0-9]{12}$/;

function pastDate(c: AbstractControl): ValidationErrors | null {
  const v = String(c.value ?? '');
  return v && v >= new Date().toISOString().slice(0, 10) ? { pastDate: 'Date of birth must be in the past' } : null;
}

const str = (v: string): string | null => (v.trim() === '' ? null : v.trim());
const num = (v: string): number | null => (v.trim() === '' ? null : Number(v));

@Component({
  selector: 'app-admission-form',
  imports: [ReactiveFormsModule, ButtonComponent, CardComponent, IconComponent, InputComponent, PageHeaderComponent, SelectComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './admission-form.component.scss',
  templateUrl: './admission-form.component.html',
})
export class AdmissionFormComponent implements OnInit {
  private readonly admissions = inject(AdmissionService);
  private readonly master = inject(MasterDataService);
  private readonly years = inject(AcademicYearService);
  private readonly classes = inject(ClassService);
  private readonly lookups = inject(SchoolLookupService);
  private readonly toast = inject(ToastService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  protected readonly breadcrumbs: Breadcrumb[] = [{ label: 'Admissions', link: '/admissions' }, { label: 'New Application' }];
  protected readonly relationshipOptions: SelectOption[] = GUARDIAN_RELATIONSHIPS.map((r) => ({ label: r.label, value: r.value }));
  protected readonly maxDob = new Date().toISOString().slice(0, 10);

  protected readonly form = new FormGroup({
    // Applicant
    applicantFirstName: new FormControl('', { nonNullable: true, validators: [requiredTrimmed, Validators.maxLength(100)] }),
    applicantMiddleName: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(100)] }),
    applicantLastName: new FormControl('', { nonNullable: true, validators: [requiredTrimmed, Validators.maxLength(100)] }),
    dateOfBirth: new FormControl('', { nonNullable: true, validators: [Validators.required, pastDate] }),
    genderId: new FormControl('', { nonNullable: true }),
    bloodGroupId: new FormControl('', { nonNullable: true }),
    studentCategoryId: new FormControl('', { nonNullable: true }),
    // Identity (optional)
    aadharNumber: new FormControl('', { nonNullable: true, validators: [pattern(AADHAAR_RE, 'Aadhaar number must be exactly 12 digits')] }),
    studentNationId: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(50)] }),
    // Applied for
    academicYearId: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    appliedClassId: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    admissionTypeId: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    // Contact & address
    email: new FormControl('', { nonNullable: true, validators: [Validators.email, Validators.maxLength(150)] }),
    mobileNumber: new FormControl('', { nonNullable: true, validators: [mobileValidator] }),
    addressLine1: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(200)] }),
    addressLine2: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(200)] }),
    countryId: new FormControl('', { nonNullable: true }),
    stateId: new FormControl('', { nonNullable: true }),
    districtId: new FormControl('', { nonNullable: true }),
    cityId: new FormControl('', { nonNullable: true }),
    pincode: new FormControl('', { nonNullable: true, validators: [pattern(PINCODE_RE, 'Enter a valid 6-digit pincode')] }),
    // Guardian
    guardianName: new FormControl('', { nonNullable: true, validators: [requiredTrimmed, Validators.maxLength(150)] }),
    guardianRelationship: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    guardianMobileNumber: new FormControl('', { nonNullable: true, validators: [requiredTrimmed, mobileValidator] }),
    guardianEmail: new FormControl('', { nonNullable: true, validators: [Validators.email, Validators.maxLength(150)] }),
  });

  protected readonly submitted = signal(false);
  protected readonly saving = signal(false);

  // ── Lookup options ──────────────────────────────────────────────────
  protected readonly genderOptions = toSignal(this.safe(this.master.getGenderOptions()), { initialValue: [] });
  protected readonly bloodGroupOptions = toSignal(this.safe(this.master.getBloodGroupOptions()), { initialValue: [] });
  protected readonly studentCategoryOptions = toSignal(this.safe(this.master.getStudentCategoryOptions()), { initialValue: [] });
  protected readonly countryOptions = toSignal(this.safe(this.master.getCountryOptions()), { initialValue: [] });
  protected readonly stateOptions = signal<SelectOption[]>([]);
  protected readonly districtOptions = signal<SelectOption[]>([]);
  protected readonly cityOptions = signal<SelectOption[]>([]);

  protected readonly yearOptions = signal<SelectOption[]>([]);
  protected readonly classOptions = signal<SelectOption[]>([]);
  protected readonly admissionTypeOptions = signal<SelectOption[]>([]);
  protected readonly setupMissing = computed(() => this.yearOptions().length === 0 || this.classOptions().length === 0 || this.admissionTypeOptions().length === 0);
  protected readonly setupLoaded = signal(false);

  ngOnInit(): void {
    this.setupLocationCascade();
    this.setChildrenEnabled({ state: false, district: false, city: false });
    this.loadSchoolOptions();
  }

  /** Active academic years / classes / admission types of this school only (the endpoints scope to the token's school). */
  private loadSchoolOptions(): void {
    const active = <T extends { statusName?: string | null }>(l: T[]) => l.filter((x) => x.statusName === 'Active');

    this.years.list().pipe(catchError(() => of([])), takeUntilDestroyed(this.destroyRef)).subscribe((list) => {
      this.yearOptions.set(active(list).map((y) => ({ label: y.isCurrent ? `${y.yearName} (current)` : y.yearName, value: String(y.academicYearId) })));
      const current = active(list).find((y) => y.isCurrent);
      if (current) this.form.controls.academicYearId.setValue(String(current.academicYearId));
      this.setupLoaded.set(true);
    });
    this.classes.list().pipe(catchError(() => of([])), takeUntilDestroyed(this.destroyRef)).subscribe((list) =>
      this.classOptions.set(active(list).map((c) => ({ label: c.className, value: String(c.classId) }))),
    );
    this.lookups.list('AdmissionTypes').pipe(catchError(() => of([])), takeUntilDestroyed(this.destroyRef)).subscribe((list) =>
      this.admissionTypeOptions.set(active(list).map((t) => ({ label: String(t['typeName']), value: String(t['admissionTypeId']) }))),
    );
  }

  // ── Cascading location (same behaviour as the school form) ─────────
  private setupLocationCascade(): void {
    const c = this.form.controls;
    const reset = (...levels: ('state' | 'district' | 'city')[]) => {
      const map = { state: [c.stateId, this.stateOptions], district: [c.districtId, this.districtOptions], city: [c.cityId, this.cityOptions] } as const;
      for (const l of levels) {
        map[l][0].setValue('', { emitEvent: false });
        map[l][0].markAsUntouched();
        map[l][1].set([]);
      }
    };

    c.countryId.valueChanges
      .pipe(
        tap(() => {
          reset('state', 'district', 'city');
          this.setChildrenEnabled({ state: false, district: false, city: false });
        }),
        switchMap((v) => (v ? this.safe(this.master.getStateOptions(+v)) : of([] as SelectOption[]))),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((o) => {
        this.stateOptions.set(o);
        this.setChildrenEnabled({ state: !!c.countryId.value, district: false, city: false });
      });

    c.stateId.valueChanges
      .pipe(
        tap(() => {
          reset('district', 'city');
          this.setChildrenEnabled({ state: true, district: false, city: false });
        }),
        switchMap((v) => (v ? this.safe(this.master.getDistrictOptions(+c.countryId.value, +v)) : of([] as SelectOption[]))),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((o) => {
        this.districtOptions.set(o);
        this.setChildrenEnabled({ state: true, district: !!c.stateId.value, city: false });
      });

    c.districtId.valueChanges
      .pipe(
        tap(() => {
          reset('city');
          this.setChildrenEnabled({ state: true, district: true, city: false });
        }),
        switchMap((v) => (v ? this.safe(this.master.getCityOptions(+c.countryId.value, +c.stateId.value, +v)) : of([] as SelectOption[]))),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((o) => {
        this.cityOptions.set(o);
        this.setChildrenEnabled({ state: true, district: true, city: !!c.districtId.value });
      });
  }

  private setChildrenEnabled(e: { state: boolean; district: boolean; city: boolean }): void {
    const c = this.form.controls;
    const apply = (ctrl: AbstractControl, on: boolean) => (on ? ctrl.enable({ emitEvent: false }) : ctrl.disable({ emitEvent: false }));
    apply(c.stateId, e.state);
    apply(c.districtId, e.district);
    apply(c.cityId, e.city);
  }

  // ── Template helpers ────────────────────────────────────────────────
  protected err(name: keyof typeof this.form.controls): string | undefined {
    const control = this.form.controls[name];
    const custom = control.errors?.['pastDate'] as string | undefined;
    if (custom && (control.touched || this.submitted())) return custom;
    return firstErrorMessage(control, this.submitted());
  }

  protected cancel(): void {
    void this.router.navigate(['/admissions']);
  }

  // ── Submit ──────────────────────────────────────────────────────────
  protected submit(): void {
    if (this.saving()) return;
    this.submitted.set(true);
    this.form.markAllAsTouched();
    if (this.form.invalid) {
      this.toast.error('Please fix the highlighted fields.');
      requestAnimationFrame(() => this.host.nativeElement.querySelector('.control.invalid')?.scrollIntoView({ block: 'center', behavior: 'smooth' }));
      return;
    }

    const v = this.form.getRawValue();
    const payload: SubmitAdmissionRequest = {
      academicYearId: Number(v.academicYearId),
      appliedClassId: Number(v.appliedClassId),
      admissionTypeId: Number(v.admissionTypeId),
      applicantFirstName: v.applicantFirstName.trim(),
      applicantMiddleName: str(v.applicantMiddleName),
      applicantLastName: v.applicantLastName.trim(),
      dateOfBirth: v.dateOfBirth,
      genderId: num(v.genderId),
      bloodGroupId: num(v.bloodGroupId),
      studentCategoryId: num(v.studentCategoryId),
      aadharNumber: str(v.aadharNumber),
      studentNationId: str(v.studentNationId),
      email: str(v.email),
      mobileNumber: str(v.mobileNumber),
      addressLine1: str(v.addressLine1),
      addressLine2: str(v.addressLine2),
      countryId: num(v.countryId),
      stateId: num(v.stateId),
      districtId: num(v.districtId),
      cityId: num(v.cityId),
      pincode: str(v.pincode),
      guardianName: v.guardianName.trim(),
      guardianRelationship: v.guardianRelationship,
      guardianMobileNumber: v.guardianMobileNumber.trim(),
      guardianEmail: str(v.guardianEmail),
    };

    this.saving.set(true);
    this.admissions
      .submit(payload)
      .pipe(finalize(() => this.saving.set(false)))
      .subscribe({
        next: (created) => {
          this.toast.success(`Application ${created.applicationNumber} submitted.`);
          void this.router.navigate(['/admissions', created.admissionId]);
        },
        error: () => undefined, // already toasted by the interceptor (e.g. the 409 for a duplicate Aadhaar); the form stays
      });
  }

  /** A failed lookup shouldn't break the form: the field just stays empty (the interceptor already toasted). */
  private safe<T>(source: Observable<T[]>): Observable<T[]> {
    return source.pipe(catchError(() => of([] as T[])));
  }
}
