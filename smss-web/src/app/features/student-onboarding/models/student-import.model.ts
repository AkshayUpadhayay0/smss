// Mirrors StudentImportDtos.cs (student Excel import).

export type ImportRowStatus = 'OK' | 'WARNING' | 'ERROR';
export type ImportFilter = 'all' | 'errors' | 'warnings';

export interface ImportRow {
  /** The row number in the Excel sheet, so the admin can find it. */
  rowNumber: number;
  status: ImportRowStatus;
  messages: string[];
  admissionNumber: string;
  fullName: string;
  gender: string;
  dateOfBirth: string;
  /** dd/mm/yyyy as given in the file; empty when left blank (the start of the academic year is used). */
  admissionDate?: string;
  className: string;
  sectionName: string;
  rollNumber: string;
  guardian: string;
  guardianMobile: string;
  /** The primary guardian's WhatsApp number as it will be stored (with the country code), and email. */
  guardianWhatsApp?: string;
  guardianEmail?: string;
  /** "A guardian with this number already exists in the school (sibling?)": information only, never a warning or an error. Absent or empty when there is none. */
  guardianHint?: string;
}

export interface ImportSummary {
  total: number;
  ok: number;
  warnings: number;
  errors: number;
  /** Rows that can be imported now (no error). A row with an error is skipped; the others still import. */
  importable: number;
  /** Rows (without an error) whose guardian's number already belongs to a guardian of the school: maybe siblings. Information only. */
  guardianHints?: number;
  canImport: boolean;
  maxRows: number;
}

export interface ImportPreview {
  summary: ImportSummary;
  /** Problems with the file as a whole (wrong sheet, missing columns, too many rows, set-up missing). */
  fileErrors: string[];
  academicYearName: string;
  /** The year's first day (yyyy-mm-dd) and whether it has already passed: then "charge from the year's start" can be offered. */
  yearStart?: string;
  yearStarted?: boolean;
  /** Classes of importable rows with NO active fee structure for the year: those students get no fees. */
  classesWithoutFeeStructure?: ImportNoFeeClass[];
  rows: ImportRow[];
}

export interface ImportNoFeeClass { className: string; students: number }

/** What the admin chose for the fees of the imported students. Without chargeFromYearStart nothing is back-dated. */
export interface ImportFeeChoice { chargeFromYearStart: boolean; confirmBackdating: boolean; reason: string | null }

export interface ImportFeeFailure { studentId: string; studentName: string; className: string; reason: string }

export interface ImportClassCount { className: string; sectionName: string; students: number }

export interface ImportResult {
  imported: number;
  academicYearName: string;
  warningRows: number;
  /** Rows with errors that were NOT imported (their sheet row numbers are in skippedRowNumbers). */
  skippedRows?: number;
  skippedRowNumbers?: number[];
  firstStudentId: string;
  lastStudentId: string;
  byClass: ImportClassCount[];
  /** Things the school still has to do (fees, logins). */
  notes: string[];
  /** Fees: students given their class's fee structure, students whose class has none, whether dues were charged from the year's start, and the ones that failed (the import itself stands). */
  feesAssigned?: number;
  feesNoStructure?: number;
  feesBackdated?: boolean;
  feesFailed?: ImportFeeFailure[];
}

export const MAX_IMPORT_FILE_BYTES = 1024 * 1024;

/** A calm, specific reason a chosen file cannot be uploaded, or null when it can. Checked in the browser before anything is sent. */
export function fileProblem(file: { name: string; size: number }): string | null {
  if (!/\.xlsx$/i.test(file.name)) return 'Only .xlsx files can be imported. Download the template, fill it in and save it as an Excel Workbook (.xlsx).';
  if (file.size === 0) return 'That file is empty.';
  if (file.size > MAX_IMPORT_FILE_BYTES) return 'That file is larger than 1 MB. Split it into smaller files.';
  return null;
}

/** The rows a filter shows. */
export function filterRows(rows: ImportRow[], filter: ImportFilter): ImportRow[] {
  if (filter === 'errors') return rows.filter((r) => r.status === 'ERROR');
  if (filter === 'warnings') return rows.filter((r) => r.status === 'WARNING');
  return rows;
}
