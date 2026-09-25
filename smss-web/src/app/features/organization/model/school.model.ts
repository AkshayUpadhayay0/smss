// Generic API envelope — matches the .NET ApiResponse<T>
export interface ApiResponse<T> {
  status: boolean;
  statusCode: number;
  message: string;
  data: T;
}

export interface SchoolContact {
  contactId?: number;           // present when editing an existing contact
  contactType: string;          // School Owner, Principal, Accountant...
  contactName: string;
  designation?: string;
  email?: string;
  mobileNumber?: string;
  alternateMobileNumber?: string;
  isPrimary: boolean;
  statusId?: number;
}

interface SchoolBase {
  schoolName: string;
  schoolShortName?: string;
  schoolTypeId?: number;
  schoolLevelId?: number;
  boardTypeId?: number;
  schoolEstablishYear?: number;
  schoolGstin?: string;
  schoolPan?: string;
  countryId?: number;
  stateId?: number;
  districtId?: number;
  cityId?: number;
  addressLine1?: string;
  addressLine2?: string;
  pincode?: string;
  email?: string;
  mobileNumber?: string;
  website?: string;
  logoUrl?: string;
  subscriptionPlanId?: number;
  subscriptionStartDate?: string;   // yyyy-MM-dd
  subscriptionEndDate?: string;
  subscriptionStatusId?: number;
  schoolStatusId?: number;
}

export interface CreateSchoolRequest extends SchoolBase {
  schoolCode: string;
  contacts: SchoolContact[];
}

export interface UpdateSchoolRequest extends SchoolBase {
  contacts: SchoolContact[];
}

// One row of the schools table / one full school record
export interface SchoolsListModel extends SchoolBase {
  schoolId: string;
  schoolCode: string;
  createdAt: string;
  updatedAt: string;
  contacts: SchoolContact[];
}

// Returned once, right after a successful registration
export interface SchoolRegistrationResponse {
  school: SchoolsListModel;
  username: string;
  temporaryPassword: string;
}