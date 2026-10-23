import { ChangeDetectionStrategy, Component, DestroyRef, ElementRef, OnInit, computed, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { AbstractControl, FormControl, FormGroup, ReactiveFormsModule, ValidationErrors, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { Observable, catchError, finalize, forkJoin, of, switchMap, tap } from 'rxjs';
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
import { AdmissionDetail, EDITABLE_STATUSES, GUARDIAN_RELATIONSHIPS, SubmitAdmissionRequest } from '../../models/admission.model';
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

  /** Bound from the :admissionId route param when editing (…/admissions/:admissionId/edit); absent for a new application. */
  readonly admissionId = input<string>();
  protected readonly editing = computed(() => this.admissionId() !== undefined);
  protected readonly breadcrumbs = computed<Breadcrumb[]>(() => [{ label: 'Admissions', link: '/admissions' }, { label: this.editing() ? 'Edit Application' : 'New Application' }]);
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
    previousSchool: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(200)] }),
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
    if (this.editing()) this.loadForEdit();
  }

  /** The application being edited. Only a Submitted or Under Review one can change: any other is sent back to its page. */
  private loadForEdit(): void {
    const id = Number(this.admissionId());
    this.admissions
      .getById(id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (a) => {
          if (!EDITABLE_STATUSES.includes(a.statusName)) {
            this.toast.error('This application has been decided or closed, so it can no longer be edited.');
            void this.router.navigate(['/admissions', id]);
            return;
          }
          this.patchFrom(a);
        },
        error: () => void this.router.navigate(['/admissions']), // already toasted by the interceptor
      });
  }

  private patchFrom(a: AdmissionDetail): void {
    const s = (v?: string | number | null) => (v === null || v === undefined ? '' : String(v));
    this.form.patchValue(
      {
        applicantFirstName: a.applicantFirstName, applicantMiddleName: s(a.applicantMiddleName), applicantLastName: a.applicantLastName, dateOfBirth: a.dateOfBirth.slice(0, 10),
        genderId: s(a.genderId), bloodGroupId: s(a.bloodGroupId), studentCategoryId: s(a.studentCategoryId), studentNationId: s(a.studentNationId),
        academicYearId: s(a.academicYearId), appliedClassId: s(a.appliedClassId), admissionTypeId: s(a.admissionTypeId), previousSchool: s(a.previousSchool),
        email: s(a.email), mobileNumber: s(a.mobileNumber), addressLine1: s(a.addressLine1), addressLine2: s(a.addressLine2), pincode: s(a.pincode),
        guardianName: a.guardianName, guardianRelationship: a.guardianRelationship, guardianMobileNumber: a.guardianMobileNumber, guardianEmail: s(a.guardianEmail),
      },
      { emitEvent: false },
    );
    // The Aadhaar number only comes back masked: the box stays empty and an empty box keeps the stored number (the server never clears it on an edit)
    this.storedAadhaarMasked.set(a.aadharNumberMasked ?? null);
    this.restoreLocation(a);
  }

  /** The masked Aadhaar of the application being edited (null when it has none). */
  protected readonly storedAadhaarMasked = signal<string | null>(null);

  /** The country / state / district / city selects fill in one after another; here they are filled in one go for the saved ids. */
  private restoreLocation(a: AdmissionDetail): void {
    const c = this.form.controls;
    const country = a.countryId ?? null;
    if (!country) return;
    forkJoin({
      states: this.safe(this.master.getStateOptions(country)),
      districts: a.stateId ? this.safe(this.master.getDistrictOptions(country, a.stateId)) : of([] as SelectOption[]),
      cities: a.stateId && a.districtId ? this.safe(this.master.getCityOptions(country, a.stateId, a.districtId)) : of([] as SelectOption[]),
    })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(({ states, districts, cities }) => {
        this.stateOptions.set(states);
        this.districtOptions.set(districts);
        this.cityOptions.set(cities);
        this.setChildrenEnabled({ state: true, district: !!a.stateId, city: !!a.districtId });
        c.countryId.setValue(String(country), { emitEvent: false });
        c.stateId.setValue(a.stateId ? String(a.stateId) : '', { emitEvent: false });
        c.districtId.setValue(a.districtId ? String(a.districtId) : '', { emitEvent: false });
        c.cityId.setValue(a.cityId ? String(a.cityId) : '', { emitEvent: false });
      });
  }

  /** Active academic years / classes / admission types of this school only (the endpoints scope to the token's school). */
  private loadSchoolOptions(): void {
    const active = <T extends { statusName?: string | null }>(l: T[]) => l.filter((x) => x.statusName === 'Active');

    this.years.list().pipe(catchError(() => of([])), takeUntilDestroyed(this.destroyRef)).subscribe((list) => {
      this.yearOptions.set(active(list).map((y) => ({ label: y.isCurrent ? `${y.yearName} (current)` : y.yearName, value: String(y.academicYearId) })));
      const current = active(list).find((y) => y.isCurrent);
      if (current && !this.editing()) this.form.controls.academicYearId.setValue(String(current.academicYearId));
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
    void this.router.navigate(this.editing() ? ['/admissions', this.admissionId()] : ['/admissions']);
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
      previousSchool: str(v.previousSchool),
    };

    this.saving.set(true);
    if (this.editing()) {
      const id = Number(this.admissionId());
      this.admissions
        .update(id, payload)
        .pipe(finalize(() => this.saving.set(false)))
        .subscribe({
          next: (saved) => {
            this.toast.success(`Application ${saved.applicationNumber} updated.`);
            void this.router.navigate(['/admissions', saved.admissionId]);
          },
          error: () => undefined, // already toasted by the interceptor (e.g. the 409 once a decision has been made); the form stays
        });
      return;
    }
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
