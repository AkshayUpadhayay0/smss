import { FormArray, FormControl, FormGroup, Validators } from '@angular/forms';
import { CreateSchoolRequest, SchoolBase, SchoolContact, SchoolsListModel, UpdateSchoolRequest } from '../models/school.model';
import {
  GSTIN_RE,
  PAN_RE,
  PINCODE_RE,
  UDISE_RE,
  endDateValidator,
  establishYearValidator,
  mobileValidator,
  pattern,
  positiveIntValidator,
  requiredTrimmed,
  primaryContactValidator,
  urlValidator,
} from './school-validators';

/** All controls are strings (selects are string-typed); numbers are converted in the mappers below. */

export function createContactGroup(contact?: Partial<SchoolContact>) {
  return new FormGroup({
    contactId: new FormControl<number | null>(contact?.contactId ?? null),
    statusId: new FormControl<number | null>(contact?.statusId ?? null),
    contactType: new FormControl(contact?.contactType ?? '', { nonNullable: true, validators: [Validators.required] }),
    contactName: new FormControl(contact?.contactName ?? '', { nonNullable: true, validators: [Validators.required, Validators.maxLength(200)] }),
    designation: new FormControl(contact?.designation ?? '', { nonNullable: true, validators: [Validators.maxLength(150)] }),
    // Email and mobile are mandatory for every school contact (alternate mobile stays optional).
    email: new FormControl(contact?.email ?? '', { nonNullable: true, validators: [requiredTrimmed, Validators.email, Validators.maxLength(150)] }),
    mobileNumber: new FormControl(contact?.mobileNumber ?? '', { nonNullable: true, validators: [requiredTrimmed, mobileValidator] }),
    alternateMobileNumber: new FormControl(contact?.alternateMobileNumber ?? '', { nonNullable: true, validators: [mobileValidator] }),
    isPrimary: new FormControl(contact?.isPrimary ?? false, { nonNullable: true }),
  });
}

export type ContactGroup = ReturnType<typeof createContactGroup>;

export function createSchoolForm() {
  return new FormGroup({
    schoolCode: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, pattern(UDISE_RE, 'UDISE code must be exactly 11 digits')],
    }),
    schoolName: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.maxLength(250)] }),
    schoolShortName: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(100)] }),
    schoolTypeId: new FormControl('', { nonNullable: true }),
    schoolLevelId: new FormControl('', { nonNullable: true }),
    boardTypeId: new FormControl('', { nonNullable: true }),
    schoolEstablishYear: new FormControl('', { nonNullable: true, validators: [establishYearValidator] }),

    schoolGstin: new FormControl('', { nonNullable: true, validators: [pattern(GSTIN_RE, 'Enter a valid 15-character GSTIN')] }),
    schoolPan: new FormControl('', { nonNullable: true, validators: [pattern(PAN_RE, 'Enter a valid 10-character PAN')] }),

    countryId: new FormControl('', { nonNullable: true }),
    stateId: new FormControl('', { nonNullable: true }),
    districtId: new FormControl('', { nonNullable: true }),
    cityId: new FormControl('', { nonNullable: true }),
    addressLine1: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(250)] }),
    addressLine2: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(250)] }),
    pincode: new FormControl('', { nonNullable: true, validators: [pattern(PINCODE_RE, 'Enter a valid 6-digit pincode')] }),

    email: new FormControl('', { nonNullable: true, validators: [Validators.email, Validators.maxLength(150)] }),
    mobileNumber: new FormControl('', { nonNullable: true, validators: [mobileValidator] }),
    website: new FormControl('', { nonNullable: true, validators: [urlValidator, Validators.maxLength(200)] }),

    subscriptionPlanId: new FormControl('', { nonNullable: true, validators: [positiveIntValidator] }),
    subscriptionStartDate: new FormControl('', { nonNullable: true }),
    subscriptionEndDate: new FormControl('', { nonNullable: true, validators: [endDateValidator] }),
    subscriptionStatusId: new FormControl('', { nonNullable: true }),

    contacts: new FormArray<ContactGroup>([], { validators: [primaryContactValidator] }),
  });
}

export type SchoolForm = ReturnType<typeof createSchoolForm>;

// ── Mapping: API model -> form ───────────────────────────────────────────────

const toText = (v: string | number | null | undefined): string => (v == null ? '' : String(v));

export function schoolToFormValue(s: SchoolsListModel) {
  return {
    schoolCode: s.schoolCode,
    schoolName: s.schoolName,
    schoolShortName: toText(s.schoolShortName),
    schoolTypeId: toText(s.schoolTypeId),
    schoolLevelId: toText(s.schoolLevelId),
    boardTypeId: toText(s.boardTypeId),
    schoolEstablishYear: toText(s.schoolEstablishYear),
    schoolGstin: toText(s.schoolGstin),
    schoolPan: toText(s.schoolPan),
    countryId: toText(s.countryId),
    stateId: toText(s.stateId),
    districtId: toText(s.districtId),
    cityId: toText(s.cityId),
    addressLine1: toText(s.addressLine1),
    addressLine2: toText(s.addressLine2),
    pincode: toText(s.pincode),
    email: toText(s.email),
    mobileNumber: toText(s.mobileNumber),
    website: toText(s.website),
    subscriptionPlanId: toText(s.subscriptionPlanId),
    subscriptionStartDate: toText(s.subscriptionStartDate)?.slice(0, 10),
    subscriptionEndDate: toText(s.subscriptionEndDate)?.slice(0, 10),
    subscriptionStatusId: toText(s.subscriptionStatusId),
  };
}

// ── Mapping: form -> API request ─────────────────────────────────────────────

/** Trim; blank -> null (server [EmailAddress]/[Url] reject ""). */
export const str = (v: string | null | undefined): string | null => {
  const t = (v ?? '').trim();
  return t === '' ? null : t;
};
export const num = (v: string | null | undefined): number | null => {
  const t = (v ?? '').trim();
  return t === '' ? null : Number(t);
};

type FormValue = ReturnType<SchoolForm['getRawValue']>;

function toBase(v: FormValue): SchoolBase {
  return {
    schoolName: v.schoolName.trim(),
    schoolShortName: str(v.schoolShortName),
    schoolTypeId: num(v.schoolTypeId),
    schoolLevelId: num(v.schoolLevelId),
    boardTypeId: num(v.boardTypeId),
    schoolEstablishYear: num(v.schoolEstablishYear),
    schoolGstin: str(v.schoolGstin)?.toUpperCase() ?? null,
    schoolPan: str(v.schoolPan)?.toUpperCase() ?? null,
    countryId: num(v.countryId),
    stateId: num(v.stateId),
    districtId: num(v.districtId),
    cityId: num(v.cityId),
    addressLine1: str(v.addressLine1),
    addressLine2: str(v.addressLine2),
    pincode: str(v.pincode),
    email: str(v.email),
    mobileNumber: str(v.mobileNumber),
    website: str(v.website),
    subscriptionPlanId: num(v.subscriptionPlanId),
    subscriptionStartDate: str(v.subscriptionStartDate),
    subscriptionEndDate: str(v.subscriptionEndDate),
    subscriptionStatusId: num(v.subscriptionStatusId),
  };
}

function toContacts(v: FormValue, includeIds: boolean): SchoolContact[] {
  return v.contacts.map((c) => ({
    ...(includeIds && c.contactId != null ? { contactId: c.contactId } : {}),
    contactType: c.contactType,
    contactName: c.contactName.trim(),
    designation: str(c.designation),
    email: str(c.email),
    mobileNumber: str(c.mobileNumber),
    alternateMobileNumber: str(c.alternateMobileNumber),
    isPrimary: c.isPrimary,
    statusId: c.statusId,
  }));
}

export function toCreateRequest(form: SchoolForm): CreateSchoolRequest {
  const v = form.getRawValue();
  return { ...toBase(v), schoolCode: v.schoolCode.trim(), contacts: toContacts(v, false) };
}

/** No schoolCode: it is immutable after creation. */
export function toUpdateRequest(form: SchoolForm): UpdateSchoolRequest {
  const v = form.getRawValue();
  return { ...toBase(v), contacts: toContacts(v, true) };
}
