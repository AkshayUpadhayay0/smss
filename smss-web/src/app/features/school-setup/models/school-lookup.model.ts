import { IconName } from '../../../shared/components/icon/icons';

/**
 * The simple school-owned masters (Subject, Designation, Department, Admission Type) are all
 * "name (+ optional code) + description + status" records scoped to the caller's school. One config per master
 * drives the shared list screen and form modal. This is deliberately separate from the Super Admin `MasterConfig`
 * pattern: those are global lookups, these are tenant-owned.
 */
export interface SchoolLookupConfig {
  /** API path under /api, e.g. 'Subjects'. */
  path: string;
  /** Page title, e.g. 'Subject'. */
  title: string;
  /** Lower-case singular for messages, e.g. 'subject'. */
  singular: string;
  subtitle: string;
  icon: IconName;
  /** JSON property names in the API response/request. */
  idKey: string;
  nameKey: string;
  nameLabel: string;
  namePlaceholder: string;
  /** Present only for masters that have a code (Subject). */
  codeKey?: string;
  codeLabel?: string;
}

/** One record as the API returns it: the master-specific id/name/code keys plus the shared fields. */
export type SchoolLookupItem = Record<string, unknown> & {
  schoolId: string;
  description?: string | null;
  statusName?: string | null;
};

/** A list row: the item with a display-safe status and flag. */
export type SchoolLookupRow = Record<string, unknown> & { statusName: string; isActive: boolean };
