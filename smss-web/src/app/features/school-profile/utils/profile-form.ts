import { FormControl, FormGroup, Validators } from '@angular/forms';
import { SchoolsListModel, UpdateMySchoolProfileRequest } from '../../schools/models/school.model';
import { num, str } from '../../schools/utils/school-form';
import {
  GSTIN_RE,
  PAN_RE,
  PINCODE_RE,
  establishYearValidator,
  mobileValidator,
  pattern,
  urlValidator,
} from '../../schools/utils/school-validators';

/**
 * The School Admin's own-school form. Deliberately has no schoolCode, subscription or contacts controls,
 * so those can never be shown or sent from this page. Like the registration form, all controls are strings.
 */
export function createProfileForm() {
  return new FormGroup({
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
  });
}

export type ProfileForm = ReturnType<typeof createProfileForm>;

const toText = (v: string | number | null | undefined): string => (v == null ? '' : String(v));

export function profileToFormValue(s: SchoolsListModel) {
  return {
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
  };
}

export function toProfileRequest(form: ProfileForm): UpdateMySchoolProfileRequest {
  const v = form.getRawValue();
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
  };
}
