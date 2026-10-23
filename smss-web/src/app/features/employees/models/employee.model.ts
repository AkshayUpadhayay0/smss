// Mirrors EmployeeDto / EmployeeListItemDto / EmployeeLookupsDto ... (the Employee module).
// employeeId is the business id string (e.g. DPS001EM001), never a number.

export const EMPLOYEE_TYPES = ['Teaching', 'Non-Teaching'] as const;
export type EmployeeType = (typeof EMPLOYEE_TYPES)[number];

export interface EmployeeListItem {
  employeeId: string;
  fullName: string;
  employeeType: EmployeeType;
  designationId: number;
  designationName: string;
  departmentId: number;
  departmentName: string;
  employmentStatusName: string;
  mobileNumber: string;
  email?: string | null;
  dateOfJoining: string;
  /** Active / Inactive: whether the row is shown (the soft-delete flag). */
  statusName: string;
  photoUrl?: string | null;
  hasLogin: boolean;
}

export interface EmployeePage {
  items: EmployeeListItem[];
  total: number;
  page: number;
  pageSize: number;
}

export interface Employee {
  employeeId: string;
  fullName: string;
  firstName: string;
  middleName?: string | null;
  lastName: string;
  employeeType: EmployeeType;
  designationId: number;
  designationName: string;
  departmentId: number;
  departmentName: string;
  employmentStatusId: number;
  employmentStatusName: string;
  genderId?: number | null;
  genderName?: string | null;
  bloodGroupId?: number | null;
  bloodGroupName?: string | null;
  dateOfBirth: string;
  email?: string | null;
  mobileNumber: string;
  alternateMobile?: string | null;
  address: string;
  dateOfJoining: string;
  dateOfLeaving?: string | null;
  qualification?: string | null;
  photoUrl?: string | null;
  statusName: string;
  /** Set once a login exists; the password is never returned again. */
  loginId?: string | null;
  loginRoles: string[];
  createdAt: string;
  updatedAt: string;
}

export interface EmployeeRequest {
  designationId: number;
  departmentId: number;
  employmentStatusId?: number | null;
  employeeType: EmployeeType;
  firstName: string;
  middleName?: string | null;
  lastName: string;
  genderId?: number | null;
  bloodGroupId?: number | null;
  dateOfBirth: string;
  email?: string | null;
  mobileNumber: string;
  alternateMobile?: string | null;
  address: string;
  dateOfJoining: string;
  dateOfLeaving?: string | null;
  qualification?: string | null;
}

export interface EmployeePhoto {
  employeeId: string;
  photoUrl?: string | null;
}

export interface EmployeeDocument {
  employeeDocumentId: number;
  documentTypeId: number;
  documentTypeName: string;
  fileName: string;
  contentType: string;
  fileSize: number;
  remarks?: string | null;
  uploadedByName?: string | null;
  uploadedAt: string;
  statusName: string;
}

export interface LookupItem {
  id: number;
  name: string;
}

export interface EmployeeDocumentType extends LookupItem {
  code: string;
  isRequired: boolean;
}

export interface AssignableRole {
  roleId: number;
  roleName: string;
  description?: string | null;
}

export interface EmployeeLookups {
  designations: LookupItem[];
  departments: LookupItem[];
  employmentStatuses: LookupItem[];
  genders: LookupItem[];
  bloodGroups: LookupItem[];
  documentTypes: EmployeeDocumentType[];
  assignableRoles: AssignableRole[];
}

export interface EmployeeLoginCreated {
  employeeId: string;
  displayName: string;
  orgUserId: string;
  /** Shown ONCE; only its hash is stored. */
  temporaryPassword: string;
  roles: string[];
  emailSent: boolean;
  emailSentTo?: string | null;
}

export interface EmployeeFilters {
  search?: string;
  designationId?: string;
  departmentId?: string;
  employmentStatusId?: string;
  employeeType?: string;
  status?: string;
}

/** One row of the list table (display strings; the avatar column wants an absolute url or ''). */
export interface EmployeeRow extends Record<string, unknown> {
  employeeId: string;
  photoUrl: string;
  fullName: string;
  designationName: string;
  departmentName: string;
  employeeType: string;
  mobileNumber: string;
  employmentStatusName: string;
  statusName: string;
  login: string;
}

/** Pill colour of an employment status. */
export function employmentStatusTone(status: unknown): string {
  switch (status) {
    case 'Active':
      return 'green';
    case 'On Leave':
      return 'yellow';
    case 'Terminated':
      return 'red';
    case 'Retired':
      return 'blue';
    default:
      return 'gray'; // Resigned
  }
}

export function formatFileSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}
