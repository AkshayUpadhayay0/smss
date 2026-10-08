import { IconName, SelectOption, TableColumn } from '../../../shared/components';

/** Any master record as returned by the API. All five masters expose a boolean `isActive`. */
export interface MasterItem {
  isActive: boolean;
  [key: string]: unknown;
}

/** A list row: the API item plus derived display fields. */
export type MasterRow = MasterItem & { statusName: string };

export interface MasterField {
  /** Property name in the API request body (and the form control name). */
  key: string;
  label: string;
  type: 'text' | 'textarea' | 'select';
  required?: boolean;
  minLength?: number;
  maxLength?: number;
  placeholder?: string;
  helpText?: string;
  /** For type 'select'. Values are strings. */
  options?: SelectOption[];
  /** Sent on create only (e.g. immutable codes). Shown read-only when editing, omitted from PUT bodies. */
  createOnly?: boolean;
}

/** Everything the generic list + form-modal need to know about one master. */
export interface MasterConfig {
  title: string; // "Board Type"
  singular: string; // lower-case noun used in messages, e.g. "board type"
  subtitle: string;
  icon: IconName;
  /** Path under /api/MasterData, e.g. "board-types". */
  path: string;
  /** Property holding the record id (used in PUT / toggle URLs). */
  idKey: string;
  columns: TableColumn<MasterRow>[];
  fields: MasterField[];
  /** Optional dropdown above the table that narrows rows to one value of `key` (e.g. Status by type). */
  filter?: { key: string; label: string; allLabel: string; labelOf?: (value: unknown) => string };
  /** Extra display-only fields computed from an item (e.g. role "System"/"Custom"). */
  derive?: (item: MasterItem) => Record<string, unknown>;
  /** Return text to show a stronger warning in the deactivate confirmation, or null for the standard one. */
  deactivateWarning?: (item: MasterItem) => string | null;
  /** Notice shown at the top of the add/edit modal. */
  formNotice?: string;
}
