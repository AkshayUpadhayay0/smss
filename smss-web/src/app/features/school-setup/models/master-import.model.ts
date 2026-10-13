// Mirrors MasterImportCatalogDto / ImportMastersRequest / ImportMastersResultDto.

export interface MasterImportItem {
  /** What is sent back to select it: the name (for document types, the code). */
  key: string;
  name: string;
  code?: string | null;
  /** Document types only. */
  appliesTo?: 'STUDENT' | 'EMPLOYEE' | 'BOTH' | null;
  isRequired?: boolean | null;
  /** The school already has it; importing would skip it. */
  alreadyAdded: boolean;
}

export type MasterImportGroupKey = 'classes' | 'subjects' | 'designations' | 'departments' | 'admissionTypes' | 'documentTypes';

export interface MasterImportCatalog extends Record<MasterImportGroupKey, MasterImportItem[]> {
  defaultSectionName: string;
}

/** The selected keys per group. */
export type MasterImportRequest = Record<MasterImportGroupKey, string[]>;

export interface ImportCount {
  created: number;
  skipped: number;
}

export interface MasterImportResult {
  classes: ImportCount;
  sections: ImportCount;
  subjects: ImportCount;
  designations: ImportCount;
  departments: ImportCount;
  admissionTypes: ImportCount;
  documentTypes: ImportCount;
  totalCreated: number;
}

export interface MasterImportGroup {
  key: MasterImportGroupKey;
  title: string;
  hint: string;
}

/** Display order of the groups on the page. */
export const MASTER_IMPORT_GROUPS: MasterImportGroup[] = [
  { key: 'classes', title: 'Classes', hint: 'Each class you pick also gets Section A.' },
  { key: 'subjects', title: 'Subjects', hint: 'Subjects taught in the school.' },
  { key: 'designations', title: 'Employee Designations', hint: 'Roles your staff hold.' },
  { key: 'departments', title: 'Employee Departments', hint: 'Departments your staff belong to.' },
  { key: 'admissionTypes', title: 'Admission Types', hint: '"Existing Student" is needed to add students who are already enrolled.' },
  { key: 'documentTypes', title: 'Document Types', hint: 'Documents collected for students and staff.' },
];
