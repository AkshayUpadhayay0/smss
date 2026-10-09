import { SchoolLookupConfig } from '../models/school-lookup.model';

export const SUBJECT_CONFIG: SchoolLookupConfig = {
  path: 'Subjects',
  title: 'Subject',
  singular: 'subject',
  subtitle: 'Manage the subjects your school teaches',
  icon: 'file-text',
  idKey: 'subjectId',
  nameKey: 'subjectName',
  nameLabel: 'Subject name',
  namePlaceholder: 'e.g. Mathematics',
  codeKey: 'subjectCode',
  codeLabel: 'Subject code',
};

export const EMPLOYEE_DESIGNATION_CONFIG: SchoolLookupConfig = {
  path: 'EmployeeDesignations',
  title: 'Employee Designation',
  singular: 'designation',
  subtitle: "Manage the designations used for your school's employees",
  icon: 'user',
  idKey: 'designationId',
  nameKey: 'designationName',
  nameLabel: 'Designation name',
  namePlaceholder: 'e.g. Principal, Class Teacher',
};

export const EMPLOYEE_DEPARTMENT_CONFIG: SchoolLookupConfig = {
  path: 'EmployeeDepartments',
  title: 'Employee Department',
  singular: 'department',
  subtitle: "Manage the departments your school's employees belong to",
  icon: 'building-2',
  idKey: 'departmentId',
  nameKey: 'departmentName',
  nameLabel: 'Department name',
  namePlaceholder: 'e.g. Administration, Science',
};

export const ADMISSION_TYPE_CONFIG: SchoolLookupConfig = {
  path: 'AdmissionTypes',
  title: 'Admission Type',
  singular: 'admission type',
  subtitle: 'Manage the ways students are admitted to your school',
  icon: 'inbox',
  idKey: 'admissionTypeId',
  nameKey: 'typeName',
  nameLabel: 'Type name',
  namePlaceholder: 'e.g. New Admission, Transfer',
};
