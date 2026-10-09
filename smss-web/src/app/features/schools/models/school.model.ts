// Mirrors the C# DTOs verbatim (property names are case-sensitive on the wire).
// Optional fields are `| null` because the server serialises absent values as null and
// blank form fields are sent as null (server [EmailAddress]/[Url] validators reject "").

export interface SchoolContact {
  contactId?: number | null;
  contactType: string; // "School Owner" | "Principal" | "Accountant" | "Admin Staff"
  contactName: string;
  designation?: string | null;
  email?: string | null;
  mobileNumber?: string | null;
  alternateMobileNumber?: string | null;
  isPrimary: boolean;
  statusId?: number | null;
}

// schoolStatusId and logoUrl are deliberately NOT here — server-controlled, never sent on create/update.
export interface SchoolBase {
  schoolName: string;
  schoolShortName?: string | null;
  schoolTypeId?: number | null;
  schoolLevelId?: number | null;
  boardTypeId?: number | null;
  schoolEstablishYear?: number | null;
  schoolGstin?: string | null;
  schoolPan?: string | null;
  countryId?: number | null;
  stateId?: number | null;
  districtId?: number | null;
  cityId?: number | null;
  addressLine1?: string | null;
  addressLine2?: string | null;
  pincode?: string | null;
  email?: string | null;
  mobileNumber?: string | null;
  website?: string | null;
  subscriptionPlanId?: number | null;
  subscriptionStartDate?: string | null; // yyyy-MM-dd
  subscriptionEndDate?: string | null;
  /**
   * Not in the original spec, but the API has it on the request DTO and OVERWRITES it on every update,
   * so omitting it from edits would wipe the stored value.
   */
  subscriptionStatusId?: number | null;
}

export interface CreateSchoolRequest extends SchoolBase {
  schoolCode: string; // required on create only, immutable after
  contacts: SchoolContact[];
}

export interface UpdateSchoolRequest extends SchoolBase {
  contacts: SchoolContact[]; // no schoolCode field
}

/** A School Admin editing their own school: no subscription, status, code, logo or contacts. */
export type UpdateMySchoolProfileRequest = Omit<
  SchoolBase,
  'subscriptionPlanId' | 'subscriptionStartDate' | 'subscriptionEndDate' | 'subscriptionStatusId'
>;

export interface SchoolsListModel extends SchoolBase {
  schoolId: string; // e.g. "sch2026001"
  schoolCode: string;
  schoolStatusId?: number | null; // read-only, changed only via toggle-status
  logoUrl?: string | null; // relative path, prepend environment.apiUrl
  createdAt: string;
  updatedAt: string;
  contacts: SchoolContact[];
}

export interface SchoolRegistrationResponse {
  school: SchoolsListModel;
  username: string;
  temporaryPassword: string; // shown once, never fetchable again
  emailSent: boolean;
  emailSentTo?: string | null;
}

export const CONTACT_TYPES = ['School Owner', 'Principal', 'Accountant', 'Admin Staff'] as const;
