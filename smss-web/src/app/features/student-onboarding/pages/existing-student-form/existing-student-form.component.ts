import { ChangeDetectionStrategy, Component, DestroyRef, ElementRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { AbstractControl, FormControl, FormGroup, ReactiveFormsModule, ValidationErrors, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { Observable, catchError, finalize, map, of, switchMap } from 'rxjs';
import { MasterDataService } from '../../../../core/services/master-data.service';
import { ToastService } from '../../../../core/services/toast.service';
import { Breadcrumb, ButtonComponent, CardComponent, IconComponent, InputComponent, PageHeaderComponent, SelectComponent, SelectOption } from '../../../../shared/components';
import { GUARDIAN_RELATIONSHIPS } from '../../../admissions/models/admission.model';
import { StudentService } from '../../../students/services/student.service';
import { LogoChange, LogoUploadComponent } from '../../../schools/components/logo-upload/logo-upload.component';
import { AcademicYearService } from '../../../school-setup/services/academic-year.service';
import { SchoolLookupService } from '../../../school-setup/services/school-lookup.service';
import { SectionService } from '../../../school-setup/services/section.service';
import { firstErrorMessage, mobileValidator, pattern, requiredTrimmed } from '../../../schools/utils/school-validators';
import { DirectStudentEntryRequest, StudentOnboardingResult, StudentOnboardingService } from '../../services/student-onboarding.service';

/** The school's own Admission Type used for these students (must match the API's StudentOnboardingService). */
const EXISTING_STUDENT_TYPE = 'existing student';
const AADHAAR_RE = /^[0-9]{12}$/;

const today = () => new Date().toISOString().slice(0, 10);
const str = (v: string): string | null => (v.trim() === '' ? null : v.trim());
const num = (v: string): number | null => (v.trim() === '' ? null : Number(v));

function notFuture(c: AbstractControl): ValidationErrors | null {
  const v = String(c.value ?? '');
  return v && v > today() ? { custom: 'Admission date cannot be in the future' } : null;
}

function pastDate(c: AbstractControl): ValidationErrors | null {
  const v = String(c.value ?? '');
  return v && v >= today() ? { custom: 'Date of birth must be in the past' } : null;
}

/** Direct entry of a student the school already has: no application, review or approval stage. */
@Component({
  selector: 'app-existing-student-form',
  imports: [ReactiveFormsModule, ButtonComponent, CardComponent, IconComponent, InputComponent, LogoUploadComponent, PageHeaderComponent, SelectComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './existing-student-form.component.scss',
  templateUrl: './existing-student-form.component.html',
})
export class ExistingStudentFormComponent implements OnInit {
  private readonly onboarding = inject(StudentOnboardingService);
  private readonly studentApi = inject(StudentService);
  private readonly master = inject(MasterDataService);
  private readonly years = inject(AcademicYearService);
  private readonly sections = inject(SectionService);
  private readonly lookups = inject(SchoolLookupService);
  private readonly toast = inject(ToastService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  protected readonly breadcrumbs: Breadcrumb[] = [{ label: 'Add Existing Student' }];
  protected readonly relationshipOptions: SelectOption[] = GUARDIAN_RELATIONSHIPS.map((r) => ({ label: r.label, value: r.value }));
  protected readonly maxDate = today();

  protected readonly form = new FormGroup({
    // Admission record
    admissionNumber: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(50)] }),
    admissionDate: new FormControl('', { nonNullable: true, validators: [Validators.required, notFuture] }),
    // Academic placement
    academicYearId: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    classSectionId: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    rollNumber: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(30)] }),
    // Student
    firstName: new FormControl('', { nonNullable: true, validators: [requiredTrimmed, Validators.maxLength(100)] }),
    middleName: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(100)] }),
    lastName: new FormControl('', { nonNullable: true, validators: [requiredTrimmed, Validators.maxLength(100)] }),
    dateOfBirth: new FormControl('', { nonNullable: true, validators: [Validators.required, pastDate] }),
    genderId: new FormControl('', { nonNullable: true }),
    bloodGroupId: new FormControl('', { nonNullable: true }),
    studentCategoryId: new FormControl('', { nonNullable: true }),
    aadharNumber: new FormControl('', { nonNullable: true, validators: [pattern(AADHAAR_RE, 'Aadhaar number must be exactly 12 digits')] }),
    // Contact
    email: new FormControl('', { nonNullable: true, validators: [Validators.email, Validators.maxLength(150)] }),
    mobileNumber: new FormControl('', { nonNullable: true, validators: [mobileValidator] }),
    // Guardian
    guardianName: new FormControl('', { nonNullable: true, validators: [requiredTrimmed, Validators.maxLength(150)] }),
    guardianRelationship: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    guardianMobileNumber: new FormControl('', { nonNullable: true, validators: [requiredTrimmed, mobileValidator] }),
    guardianEmail: new FormControl('', { nonNullable: true, validators: [Validators.email, Validators.maxLength(150)] }),
  });

  protected readonly submitted = signal(false);
  protected readonly saving = signal(false);
  protected readonly created = signal<StudentOnboardingResult | null>(null);
  /** Optional photo, uploaded right after the student is created. */
  private photo: File | null = null;
  /** Bumped after each save so the picker is re-created (clears its pending file). */
  protected readonly photoVersion = signal(0);

  // ── Lookup options ──────────────────────────────────────────────────
  protected readonly genderOptions = toSignal(this.safe(this.master.getGenderOptions()), { initialValue: [] });
  protected readonly bloodGroupOptions = toSignal(this.safe(this.master.getBloodGroupOptions()), { initialValue: [] });
  protected readonly studentCategoryOptions = toSignal(this.safe(this.master.getStudentCategoryOptions()), { initialValue: [] });
  protected readonly yearOptions = signal<SelectOption[]>([]);
  protected readonly sectionOptions = signal<SelectOption[]>([]);

  /** null = still checking; false = the school has no active "Existing Student" admission type yet. */
  protected readonly typeReady = signal<boolean | null>(null);
  protected readonly typeMissing = computed(() => this.typeReady() === false);
  /** Set when the server itself reports the missing type (e.g. it was deactivated after this page loaded). */
  protected readonly serverSaysTypeMissing = signal(false);
  protected readonly showTypeBanner = computed(() => this.typeMissing() || this.serverSaysTypeMissing());

  ngOnInit(): void {
    this.loadSchoolOptions();
  }

  private loadSchoolOptions(): void {
    const active = <T extends { statusName?: string | null }>(l: T[]) => l.filter((x) => x.statusName === 'Active');

    this.years.list().pipe(catchError(() => of([])), takeUntilDestroyed(this.destroyRef)).subscribe((list) => {
      this.yearOptions.set(active(list).map((y) => ({ label: y.isCurrent ? `${y.yearName} (current)` : y.yearName, value: String(y.academicYearId) })));
      const current = active(list).find((y) => y.isCurrent);
      if (current && !this.form.controls.academicYearId.value) this.form.controls.academicYearId.setValue(String(current.academicYearId));
    });

    // The API returns sections ordered by class, then name
    this.sections.list().pipe(catchError(() => of([])), takeUntilDestroyed(this.destroyRef)).subscribe((list) =>
      this.sectionOptions.set(list.filter((s) => s.statusName === 'Active').map((s) => ({ label: `${s.className} – ${s.sectionName}`, value: String(s.sectionId) }))),
    );

    this.lookups.list('AdmissionTypes').pipe(catchError(() => of(null)), takeUntilDestroyed(this.destroyRef)).subscribe((list) => {
      if (list === null) return; // couldn't check: let the server decide on submit
      this.typeReady.set(list.some((t) => String(t['typeName'] ?? '').trim().toLowerCase() === EXISTING_STUDENT_TYPE && t.statusName === 'Active'));
    });
  }

  // ── Template helpers ────────────────────────────────────────────────
  protected err(name: keyof typeof this.form.controls): string | undefined {
    const control = this.form.controls[name];
    const custom = control.errors?.['custom'] as string | undefined;
    if (custom && (control.touched || this.submitted())) return custom;
    return firstErrorMessage(control, this.submitted());
  }

  protected onPhotoChanged(change: LogoChange): void {
    this.photo = change.file;
  }

  protected goToAdmissionTypes(): void {
    void this.router.navigate(['/school-setup/admission-types']);
  }

  protected addAnother(): void {
    const keepYear = this.form.controls.academicYearId.value;
    const keepSection = this.form.controls.classSectionId.value;
    const keepDate = this.form.controls.admissionDate.value;
    this.form.reset();
    // a school usually enters a whole class in a row: keep the placement and date they just used
    this.form.patchValue({ academicYearId: keepYear, classSectionId: keepSection, admissionDate: keepDate });
    this.submitted.set(false);
    this.created.set(null);
    this.photo = null;
    this.photoVersion.update((v) => v + 1);
    window.scrollTo({ top: 0 });
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
    const payload: DirectStudentEntryRequest = {
      admissionNumber: str(v.admissionNumber),
      admissionDate: v.admissionDate,
      academicYearId: Number(v.academicYearId),
      classSectionId: Number(v.classSectionId),
      rollNumber: str(v.rollNumber),
      firstName: v.firstName.trim(),
      middleName: str(v.middleName),
      lastName: v.lastName.trim(),
      dateOfBirth: v.dateOfBirth,
      genderId: num(v.genderId),
      bloodGroupId: num(v.bloodGroupId),
      studentCategoryId: num(v.studentCategoryId),
      aadharNumber: str(v.aadharNumber),
      email: str(v.email),
      mobileNumber: str(v.mobileNumber),
      guardianName: v.guardianName.trim(),
      guardianRelationship: v.guardianRelationship,
      guardianMobileNumber: v.guardianMobileNumber.trim(),
      guardianEmail: str(v.guardianEmail),
    };

    this.saving.set(true);
    const photo = this.photo;
    this.onboarding
      .createExistingStudent(payload)
      .pipe(
        // the student is saved first; the photo is a best-effort follow-up that must never undo or fail that
        switchMap((result) =>
          photo
            ? this.studentApi.uploadPhoto(result.studentId, photo).pipe(
                map(() => ({ result, photoError: null as string | null })),
                catchError((e) => of({ result, photoError: this.messageOf(e) })),
              )
            : of({ result, photoError: null as string | null }),
        ),
        finalize(() => this.saving.set(false)),
      )
      .subscribe({
        next: ({ result, photoError }) => {
          this.created.set(result);
          this.toast.success(`${result.fullName} added with admission number ${result.admissionNumber}.`);
          if (photoError) this.toast.warning(`${photoError} You can add the photo later from the student's profile.`, 'Student added, but the photo was not uploaded');
          window.scrollTo({ top: 0 });
        },
        error: (e) => {
          const message = this.messageOf(e);
          // The API's "add an 'Existing Student' admission type" refusal gets a one-click way to fix it
          if (/existing student/i.test(message) && /admission type/i.test(message)) this.serverSaysTypeMissing.set(true);
          this.toast.error(message);
        },
      });
  }

  private messageOf(e: unknown): string {
    const err = e as { error?: { message?: string }; message?: string };
    return err?.error?.message ?? err?.message ?? 'Something went wrong. Please try again.';
  }

  /** A failed lookup shouldn't break the form: the field just stays empty (the interceptor already toasted). */
  private safe<T>(source: Observable<T[]>): Observable<T[]> {
    return source.pipe(catchError(() => of([] as T[])));
  }
}
