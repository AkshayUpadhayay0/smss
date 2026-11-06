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
  className: string;
  sectionName: string;
  rollNumber: string;
  guardian: string;
  guardianMobile: string;
}

export interface ImportSummary {
  total: number;
  ok: number;
  warnings: number;
  errors: number;
  canImport: boolean;
  maxRows: number;
}

export interface ImportPreview {
  summary: ImportSummary;
  /** Problems with the file as a whole (wrong sheet, missing columns, too many rows, set-up missing). */
  fileErrors: string[];
  academicYearName: string;
  rows: ImportRow[];
}

export interface ImportClassCount { className: string; sectionName: string; students: number }

export interface ImportResult {
  imported: number;
  academicYearName: string;
  warningRows: number;
  firstStudentId: string;
  lastStudentId: string;
  byClass: ImportClassCount[];
  /** Things the school still has to do (fees, logins). */
  notes: string[];
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
