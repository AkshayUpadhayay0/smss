import {
  ChangeDetectionStrategy, ChangeDetectorRef, Component, OnDestroy, OnInit, computed, inject, signal,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { toSignal } from '@angular/core/rxjs-interop';
import { AbstractControl, FormArray, FormBuilder, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterModule } from '@angular/router';
import { Observable, catchError, map, of } from 'rxjs';

import { PageHeaderComponent } from '../../../../shared/components/page-header/page-header.component';
import { CardComponent } from '../../../../shared/components/card/card.component';
import { IconComponent } from '../../../../shared/components/icon/icon.component';
import { ButtonComponent } from '../../../../shared/components/button/button.component';
import { InputComponent } from '../../../../shared/components/input/input.component';
import { SelectComponent, SelectOption } from '../../../../shared/components/select/select.component';

import { ToastService } from '../../../../core/services';
import { MasterDataService } from '../../../../core/services/master-data.service';
import { SchoolService } from '../../services/school.service';
import {
  CreateSchoolRequest,
  SchoolContact,
  SchoolRegistrationResponse,
  UpdateSchoolRequest,
} from '../../model/school.model';

const MOBILE_PATTERN = /^[6-9][0-9]{9}$/;
const GSTIN_PATTERN = /^[0-9]{2}[A-Za-z]{5}[0-9]{4}[A-Za-z][1-9A-Za-z]Z[0-9A-Za-z]$/;
const PAN_PATTERN = /^[A-Za-z]{5}[0-9]{4}[A-Za-z]$/;
const PINCODE_PATTERN = /^[1-9][0-9]{5}$/;

const LOGO_MAX_BYTES = 2 * 1024 * 1024;
const LOGO_ALLOWED_TYPES = ['image/png', 'image/jpeg', 'image/webp'];
const LOGO_ALLOWED_EXTENSIONS = ['.png', '.jpg', '.jpeg', '.webp'];

const CONTACT_TYPE_OPTIONS: SelectOption[] = [
  { label: 'School Owner', value: 'School Owner' },
  { label: 'Principal', value: 'Principal' },
  { label: 'Accountant', value: 'Accountant' },
  { label: 'Admin Staff', value: 'Admin Staff' },
];

@Component({
  selector: 'app-add-school',
  standalone: true,
  imports: [
    CommonModule,
    RouterModule,
    ReactiveFormsModule,
    PageHeaderComponent,
    CardComponent,
    IconComponent,
    ButtonComponent,
    InputComponent,
    SelectComponent,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './add-school.component.html',
  styleUrls: ['./add-school.component.scss'],
})
export class AddSchoolComponent implements OnInit, OnDestroy {
  private readonly fb = inject(FormBuilder);
  private readonly cdr = inject(ChangeDetectorRef);
  private readonly schoolService = inject(SchoolService);
  private readonly masterDataService = inject(MasterDataService);
  private readonly toastService = inject(ToastService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  readonly contactTypeOptions = CONTACT_TYPE_OPTIONS;

  readonly countryOptions = toSignal(this.masterDataService.getCountryOptions(), { initialValue: [] as SelectOption[] });
  readonly schoolTypeOptions = toSignal(this.masterDataService.getSchoolTypeOptions(), { initialValue: [] as SelectOption[] });
  readonly schoolLevelOptions = toSignal(this.masterDataService.getSchoolLevelOptions(), { initialValue: [] as SelectOption[] });
  readonly boardTypeOptions = toSignal(this.masterDataService.getBoardTypeOptions(), { initialValue: [] as SelectOption[] });
  readonly subscriptionStatusOptions = toSignal(this.masterDataService.getSubscriptionStatusOptions(), { initialValue: [] as SelectOption[] });

  readonly stateOptions = signal<SelectOption[]>([]);
  readonly districtOptions = signal<SelectOption[]>([]);
  readonly cityOptions = signal<SelectOption[]>([]);

  // ---- Logo state ----
  readonly logoFile = signal<File | null>(null);                  // freshly picked file, uploaded on save
  readonly newLogoPreview = signal<string | null>(null);          // object URL of that file
  readonly existingLogoUrl = signal<string | null>(null);         // logo already stored for this school
  readonly removeExistingLogo = signal(false);                    // removal requested, applied on save
  readonly logoError = signal<string | null>(null);
  readonly currentLogo = computed(
    () => this.newLogoPreview() ?? (this.removeExistingLogo() ? null : this.existingLogoUrl()),
  );

  form!: FormGroup;
  isEditMode = false;
  isViewMode = false;
  schoolId: string | null = null;

  readonly submitting = signal(false);
  readonly registrationResult = signal<SchoolRegistrationResponse | null>(null);

  ngOnInit(): void {
    // Mode must be known BEFORE the form is built (schoolCode validators depend on it)
    this.schoolId = this.route.snapshot.paramMap.get('schoolId');
    this.isEditMode = !!this.schoolId;
    this.isViewMode = this.route.snapshot.data['mode'] === 'view';

    this.buildForm();
    this.setupLocationCascade();

    if (this.schoolId) {
      this.loadSchool(this.schoolId);
    } else {
      this.addContact();
    }
  }

  ngOnDestroy(): void {
    this.revokePreview();
  }

  get pageTitle(): string {
    if (this.isViewMode) return 'School Details';
    return this.isEditMode ? 'Edit School' : 'Register School';
  }

  get pageSubtitle(): string {
    if (this.isViewMode) return 'Read-only view of this school.';
    return this.isEditMode ? "Update this school's details." : 'Onboard a new school onto the platform.';
  }

  get contacts(): FormArray {
    return this.form.get('contacts') as FormArray;
  }

  get contactGroups(): FormGroup[] {
    return this.contacts.controls as FormGroup[];
  }

  private buildForm(): void {
    this.form = this.fb.group({
      // School code is set once at registration and locked afterwards
      schoolCode: [
        { value: '', disabled: this.isEditMode },
        this.isEditMode ? [] : [Validators.required, Validators.maxLength(100), Validators.pattern(/^[A-Za-z0-9_-]+$/)],
      ],
      schoolName: ['', [Validators.required, Validators.maxLength(250)]],
      schoolShortName: ['', Validators.maxLength(100)],
      schoolTypeId: [''],
      schoolLevelId: [''],
      boardTypeId: [''],
      schoolEstablishYear: [null, [Validators.min(1800), Validators.max(new Date().getFullYear())]],
      schoolGstin: ['', Validators.pattern(GSTIN_PATTERN)],
      schoolPan: ['', Validators.pattern(PAN_PATTERN)],

      countryId: [''],
      stateId: [{ value: '', disabled: true }],
      districtId: [{ value: '', disabled: true }],
      cityId: [{ value: '', disabled: true }],

      addressLine1: ['', Validators.maxLength(250)],
      addressLine2: ['', Validators.maxLength(250)],
      pincode: ['', Validators.pattern(PINCODE_PATTERN)],

      email: ['', Validators.email],
      mobileNumber: ['', Validators.pattern(MOBILE_PATTERN)],
      website: [''],

      subscriptionPlanId: [null],
      subscriptionStartDate: [null],
      subscriptionEndDate: [null],
      subscriptionStatusId: [''],

      contacts: this.fb.array([]),
    });
  }

  // ---------------- Logo ----------------

  onLogoSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    input.value = '';                       // lets the same file be picked again
    if (!file) return;

    const error = this.validateLogo(file);
    if (error) {
      this.logoError.set(error);
      return;
    }

    this.revokePreview();
    this.logoError.set(null);
    this.removeExistingLogo.set(false);     // a new file replaces the old one anyway
    this.logoFile.set(file);
    this.newLogoPreview.set(URL.createObjectURL(file));
  }

  clearLogo(): void {
    this.logoError.set(null);
    if (this.newLogoPreview()) {            // first discard a newly picked file
      this.revokePreview();
      this.newLogoPreview.set(null);
      this.logoFile.set(null);
    } else if (this.existingLogoUrl()) {
      this.removeExistingLogo.set(true);    // applied when the form is saved
    }
  }

  undoRemoveLogo(): void {
    this.removeExistingLogo.set(false);
  }

  private validateLogo(file: File): string | null {
    const ext = file.name.substring(file.name.lastIndexOf('.')).toLowerCase();
    if (!LOGO_ALLOWED_EXTENSIONS.includes(ext) || !LOGO_ALLOWED_TYPES.includes(file.type)) {
      return 'Only PNG, JPG or WEBP images are allowed.';
    }
    if (file.size > LOGO_MAX_BYTES) return 'Logo must be 2 MB or smaller.';
    return null;
  }

  private revokePreview(): void {
    const url = this.newLogoPreview();
    if (url) URL.revokeObjectURL(url);
  }

  // Applies the pending logo change. Emits null on success/no-op, or an error message.
  private syncLogo(schoolId: string): Observable<string | null> {
    const file = this.logoFile();
    let call$: Observable<unknown>;

    if (file) {
      call$ = this.schoolService.uploadLogo(schoolId, file);
    } else if (this.removeExistingLogo() && this.existingLogoUrl()) {
      call$ = this.schoolService.removeLogo(schoolId);
    } else {
      return of(null);
    }

    return call$.pipe(
      map((): string | null => null),
      catchError((err) => of<string | null>(err?.error?.message || 'Please try again.')),
    );
  }

  // ---------------- Location cascade (user-driven) ----------------

  private setupLocationCascade(): void {
    const countryCtrl = this.form.get('countryId')!;
    const stateCtrl = this.form.get('stateId')!;
    const districtCtrl = this.form.get('districtId')!;
    const cityCtrl = this.form.get('cityId')!;

    countryCtrl.valueChanges.subscribe((countryId) => {
      stateCtrl.reset('', { emitEvent: false });
      districtCtrl.reset('', { emitEvent: false });
      cityCtrl.reset('', { emitEvent: false });
      this.stateOptions.set([]);
      this.districtOptions.set([]);
      this.cityOptions.set([]);
      districtCtrl.disable({ emitEvent: false });
      cityCtrl.disable({ emitEvent: false });

      if (countryId) {
        stateCtrl.enable({ emitEvent: false });
        this.masterDataService.getStateOptions(Number(countryId)).subscribe((opts) => this.stateOptions.set(opts));
      } else {
        stateCtrl.disable({ emitEvent: false });
      }
    });

    stateCtrl.valueChanges.subscribe((stateId) => {
      districtCtrl.reset('', { emitEvent: false });
      cityCtrl.reset('', { emitEvent: false });
      this.districtOptions.set([]);
      this.cityOptions.set([]);
      cityCtrl.disable({ emitEvent: false });

      const countryId = countryCtrl.value;
      if (countryId && stateId) {
        districtCtrl.enable({ emitEvent: false });
        this.masterDataService
          .getDistrictOptions(Number(countryId), Number(stateId))
          .subscribe((opts) => this.districtOptions.set(opts));
      } else {
        districtCtrl.disable({ emitEvent: false });
      }
    });

    districtCtrl.valueChanges.subscribe((districtId) => {
      cityCtrl.reset('', { emitEvent: false });
      this.cityOptions.set([]);

      const countryId = countryCtrl.value;
      const stateId = stateCtrl.value;
      if (countryId && stateId && districtId) {
        cityCtrl.enable({ emitEvent: false });
        this.masterDataService
          .getCityOptions(Number(countryId), Number(stateId), Number(districtId))
          .subscribe((opts) => this.cityOptions.set(opts));
      } else {
        cityCtrl.disable({ emitEvent: false });
      }
    });
  }

  // ---------------- Location cascade (edit/view: load saved chain) ----------------

  private loadLocationChain(countryId?: number, stateId?: number, districtId?: number, cityId?: number): void {
    if (!countryId) return;

    const stateCtrl = this.form.get('stateId')!;
    const districtCtrl = this.form.get('districtId')!;
    const cityCtrl = this.form.get('cityId')!;
    // In view mode everything stays disabled
    const enable = (c: AbstractControl) => {
      if (!this.isViewMode) c.enable({ emitEvent: false });
    };

    this.masterDataService.getStateOptions(countryId).subscribe((states) => {
      this.stateOptions.set(states);
      enable(stateCtrl);
      stateCtrl.setValue(stateId ? String(stateId) : '', { emitEvent: false });
      if (!stateId) return;

      this.masterDataService.getDistrictOptions(countryId, stateId).subscribe((districts) => {
        this.districtOptions.set(districts);
        enable(districtCtrl);
        districtCtrl.setValue(districtId ? String(districtId) : '', { emitEvent: false });
        if (!districtId) return;

        this.masterDataService.getCityOptions(countryId, stateId, districtId).subscribe((cities) => {
          this.cityOptions.set(cities);
          enable(cityCtrl);
          cityCtrl.setValue(cityId ? String(cityId) : '', { emitEvent: false });
        });
      });
    });
  }

  private loadSchool(schoolId: string): void {
    this.schoolService.getSchoolById(schoolId).subscribe({
      next: (res) => {
        if (!res.status || !res.data) {
          this.toastService.danger('Failed to load school', res.message);
          return;
        }
        const data = res.data;
        const idToString = (v?: number | null) => (v != null ? String(v) : '');

        this.form.patchValue(
          {
            schoolCode: data.schoolCode,
            schoolName: data.schoolName,
            schoolShortName: data.schoolShortName,
            schoolTypeId: idToString(data.schoolTypeId),
            schoolLevelId: idToString(data.schoolLevelId),
            boardTypeId: idToString(data.boardTypeId),
            schoolEstablishYear: data.schoolEstablishYear,
            schoolGstin: data.schoolGstin,
            schoolPan: data.schoolPan,
            countryId: idToString(data.countryId),
            addressLine1: data.addressLine1,
            addressLine2: data.addressLine2,
            pincode: data.pincode,
            email: data.email,
            mobileNumber: data.mobileNumber,
            website: data.website,
            subscriptionPlanId: data.subscriptionPlanId,
            subscriptionStartDate: data.subscriptionStartDate,
            subscriptionEndDate: data.subscriptionEndDate,
            subscriptionStatusId: idToString(data.subscriptionStatusId),
          },
          { emitEvent: false },   // don't trigger the reset-children cascade
        );

        this.existingLogoUrl.set(this.schoolService.toLogoUrl(data.logoUrl));

        this.loadLocationChain(
          data.countryId ?? undefined,
          data.stateId ?? undefined,
          data.districtId ?? undefined,
          data.cityId ?? undefined,
        );

        this.contacts.clear();
        (data.contacts || []).forEach((c) => this.contacts.push(this.buildContactGroup(c)));
        if (this.contacts.length === 0 && !this.isViewMode) this.addContact();

        if (this.isViewMode) this.form.disable({ emitEvent: false });
        this.cdr.markForCheck();
      },
      error: () => this.toastService.danger('Failed to load school', 'Please try again.'),
    });
  }

  private buildContactGroup(c?: SchoolContact): FormGroup {
    return this.fb.group({
      contactId: [c?.contactId ?? null],
      contactType: [c?.contactType ?? '', Validators.required],
      contactName: [c?.contactName ?? '', Validators.required],
      designation: [c?.designation ?? ''],
      email: [c?.email ?? '', Validators.email],
      mobileNumber: [c?.mobileNumber ?? '', Validators.pattern(MOBILE_PATTERN)],
      alternateMobileNumber: [c?.alternateMobileNumber ?? '', Validators.pattern(MOBILE_PATTERN)],
      isPrimary: [c?.isPrimary ?? false],
      statusId: [c?.statusId ?? null],
    });
  }

  addContact(): void {
    const group = this.buildContactGroup();
    if (this.contacts.length === 0) group.patchValue({ isPrimary: true });
    this.contacts.push(group);
  }

  removeContact(index: number): void {
    const wasPrimary = this.contacts.at(index).get('isPrimary')?.value;
    this.contacts.removeAt(index);
    if (wasPrimary && this.contacts.length > 0) {
      this.contacts.at(0).get('isPrimary')?.setValue(true);
    }
  }

  setPrimary(index: number): void {
    this.contacts.controls.forEach((c, i) => c.get('isPrimary')?.setValue(i === index));
  }

  errorFor(control: AbstractControl | null | undefined): string | undefined {
    if (!control || !control.touched || control.valid) return undefined;
    if (control.hasError('required')) return 'This field is required.';
    if (control.hasError('email')) return 'Please enter a valid email address.';
    if (control.hasError('pattern')) return 'Invalid format.';
    if (control.hasError('maxlength')) return `Must be at most ${control.getError('maxlength').requiredLength} characters.`;
    if (control.hasError('min')) return 'Value is too low.';
    if (control.hasError('max')) return 'Value is too high.';
    return 'This field is invalid.';
  }

  // "" -> null so optional fields pass server-side [EmailAddress]/[Url] checks
  private blankToNull<T extends object>(obj: T): T {
    return Object.fromEntries(
      Object.entries(obj as Record<string, unknown>).map(([k, v]) => [k, typeof v === 'string' && v.trim() === '' ? null : v]),
    ) as T;
  }

  submit(): void {
    if (this.isViewMode) return;

    if (this.contacts.length > 0 && !this.contacts.controls.some((c) => c.get('isPrimary')?.value)) {
      this.toastService.danger('Missing primary contact', 'Exactly one contact must be marked as primary.');
      return;
    }
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      this.toastService.danger('Please fix the errors', 'Some fields need your attention before submitting.');
      return;
    }

    this.submitting.set(true);

    // getRawValue() so the disabled cascade controls are included
    const raw = this.blankToNull(this.form.getRawValue());
    const toId = (v: unknown): number | null => (v == null ? null : Number(v));

    const payload = {
      ...raw,
      schoolEstablishYear: toId(raw.schoolEstablishYear),
      schoolTypeId: toId(raw.schoolTypeId),
      schoolLevelId: toId(raw.schoolLevelId),
      boardTypeId: toId(raw.boardTypeId),
      countryId: toId(raw.countryId),
      stateId: toId(raw.stateId),
      districtId: toId(raw.districtId),
      cityId: toId(raw.cityId),
      subscriptionStatusId: toId(raw.subscriptionStatusId),
      contacts: (raw.contacts as SchoolContact[]).map((c) => this.blankToNull(c)),
    };

    if (this.isEditMode && this.schoolId) {
      const schoolId = this.schoolId;
      const { schoolCode, ...updatePayload } = payload;   // code is immutable after registration

      this.schoolService.updateSchool(schoolId, updatePayload as UpdateSchoolRequest).subscribe({
        next: (res) => {
          if (!res.status) {
            this.submitting.set(false);
            this.toastService.danger('Update failed', res.message);
            return;
          }
          this.syncLogo(schoolId).subscribe((logoError) => {
            this.submitting.set(false);
            if (logoError) {
              // stay on the page so the logo can be retried
              this.toastService.danger('Logo not saved', `The details were saved, but the logo was not: ${logoError}`);
              return;
            }
            this.toastService.success('School updated', `${raw.schoolName} was updated successfully.`);
            this.router.navigate(['/schools']);
          });
        },
        error: (err) => this.handleError(err),
      });
    } else {
      this.schoolService.registerSchool(payload as CreateSchoolRequest).subscribe({
        next: (res) => {
          if (!(res.status && res.data)) {
            this.submitting.set(false);
            this.toastService.danger('Registration failed', res.message);
            return;
          }
          const result = res.data;
          this.syncLogo(result.school.schoolId).subscribe((logoError) => {
            this.submitting.set(false);
            if (logoError) {
              this.toastService.danger(
                'Logo not saved',
                `The school was registered, but the logo was not: ${logoError} You can add it later from Edit.`,
              );
            }
            this.registrationResult.set(result);   // credentials panel opens either way
          });
        },
        error: (err) => this.handleError(err),
      });
    }
  }

  private handleError(err: any): void {
    this.submitting.set(false);
    const body = err?.error;
    const message =
      body?.statusCode === 400 && body?.data
        ? body.message || 'Validation failed'
        : body?.message || 'Something went wrong. Please try again.';
    this.toastService.danger('Request failed', message);
  }

  doneAfterRegistration(): void {
    this.router.navigate(['/schools']);
  }
}