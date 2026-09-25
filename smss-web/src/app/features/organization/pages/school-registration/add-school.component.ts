import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { AbstractControl, FormArray, FormBuilder, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterModule } from '@angular/router';

import { PageHeaderComponent } from '../../../../shared/components/page-header/page-header.component';
import { CardComponent } from '../../../../shared/components/card/card.component';
import { IconComponent } from '../../../../shared/components/icon/icon.component';
import { ButtonComponent } from '../../../../shared/components/button/button.component';
import { InputComponent } from '../../../../shared/components/input/input.component';
import { SelectComponent, SelectOption } from '../../../../shared/components/select/select.component';

import { ToastService } from '../../../../core/services';
import { SchoolService } from '../../services/school.service';
import {
  CreateSchoolRequest,
  SchoolContact,
  SchoolRegistrationResponse,
  UpdateSchoolRequest
} from '../../model/school.model';

const MOBILE_PATTERN = /^[6-9][0-9]{9}$/;
const GSTIN_PATTERN = /^[0-9]{2}[A-Za-z]{5}[0-9]{4}[A-Za-z][1-9A-Za-z]Z[0-9A-Za-z]$/;
const PAN_PATTERN = /^[A-Za-z]{5}[0-9]{4}[A-Za-z]$/;
const PINCODE_PATTERN = /^[1-9][0-9]{5}$/;

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
export class AddSchoolComponent implements OnInit {
  private readonly fb = inject(FormBuilder);
  private readonly schoolService = inject(SchoolService);
  private readonly toastService = inject(ToastService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  readonly contactTypeOptions = CONTACT_TYPE_OPTIONS;

  form!: FormGroup;
  isEditMode = false;
  schoolId: string | null = null;

  readonly submitting = signal(false);
  readonly registrationResult = signal<SchoolRegistrationResponse | null>(null);

  ngOnInit(): void {
    this.buildForm();
    this.schoolId = this.route.snapshot.paramMap.get('schoolId');
    this.isEditMode = !!this.schoolId;

    if (this.isEditMode && this.schoolId) {
      this.loadSchool(this.schoolId);
    } else {
      this.addContact();
    }
  }

  get contacts(): FormArray {
    return this.form.get('contacts') as FormArray;
  }

  get contactGroups(): FormGroup[] {
    return this.contacts.controls as FormGroup[];
  }

  private buildForm(): void {
    this.form = this.fb.group({
      schoolCode: ['', this.isEditMode ? [] : [Validators.required, Validators.maxLength(100)]],
      schoolName: ['', [Validators.required, Validators.maxLength(250)]],
      schoolShortName: ['', Validators.maxLength(100)],
      schoolTypeId: [null],
      schoolLevelId: [null],
      boardTypeId: [null],
      schoolEstablishYear: [null, [Validators.min(1800), Validators.max(new Date().getFullYear())]],
      schoolGstin: ['', Validators.pattern(GSTIN_PATTERN)],
      schoolPan: ['', Validators.pattern(PAN_PATTERN)],
      countryId: [null],
      stateId: [null],
      districtId: [null],
      cityId: [null],
      addressLine1: ['', Validators.maxLength(250)],
      addressLine2: ['', Validators.maxLength(250)],
      pincode: ['', Validators.pattern(PINCODE_PATTERN)],
      email: ['', Validators.email],
      mobileNumber: ['', Validators.pattern(MOBILE_PATTERN)],
      website: [''],
      logoUrl: [''],
      subscriptionPlanId: [null],
      subscriptionStartDate: [null],
      subscriptionEndDate: [null],
      subscriptionStatusId: [null],
      schoolStatusId: [null],
      contacts: this.fb.array([]),
    });
  }

  private loadSchool(schoolId: string): void {
    this.schoolService.getSchoolById(schoolId).subscribe({
      next: (res) => {
        if (!res.status || !res.data) {
          this.toastService.danger('Failed to load school', res.message);
          return;
        }
        this.form.patchValue(res.data);
        this.contacts.clear();
        (res.data.contacts || []).forEach((c) => this.contacts.push(this.buildContactGroup(c)));
        if (this.contacts.length === 0) this.addContact();
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

  submit(): void {
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
    const raw = this.form.getRawValue();

    if (this.isEditMode && this.schoolId) {
      const { schoolCode, ...payload } = raw as UpdateSchoolRequest & { schoolCode: string };
      this.schoolService.updateSchool(this.schoolId, payload).subscribe({
        next: (res) => {
          this.submitting.set(false);
          if (res.status) {
            this.toastService.success('School updated', `${raw.schoolName} was updated successfully.`);
            this.router.navigate(['/schools']);
          } else {
            this.toastService.danger('Update failed', res.message);
          }
        },
        error: (err) => this.handleError(err),
      });
    } else {
      this.schoolService.registerSchool(raw as CreateSchoolRequest).subscribe({
        next: (res) => {
          this.submitting.set(false);
          if (res.status && res.data) {
            this.registrationResult.set(res.data);
          } else {
            this.toastService.danger('Registration failed', res.message);
          }
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