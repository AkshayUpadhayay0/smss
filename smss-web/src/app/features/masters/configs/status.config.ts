import { MasterConfig } from '../models/master.model';

/** The three status groups the API knows about (stype is a free string server-side). */
export const STATUS_TYPES = [
  { label: 'General status', value: 'general status' },
  { label: 'School plan status', value: 'school plan status' },
  { label: 'Payment status', value: 'payment status' },
];

/** The server looks Active / Inactive up BY NAME (login, school toggle, roles…), so these two are load-bearing. */
const isCoreStatus = (name: unknown) => ['active', 'inactive'].includes(String(name ?? '').trim().toLowerCase());

export const STATUS_CONFIG: MasterConfig = {
  title: 'Status',
  singular: 'status',
  subtitle: 'Manage status values used across the platform',
  icon: 'layers',
  path: 'status',
  idKey: 'sid',
  columns: [
    { key: 'sid', label: 'ID', sortable: true, width: '90px' },
    { key: 'sname', label: 'Status Name', sortable: true },
    { key: 'stypeLabel', label: 'Type', sortable: true },
    { key: 'statusName', label: 'State', sortable: true, type: 'status', width: '120px' },
  ],
  fields: [
    { key: 'sname', label: 'Status name', type: 'text', required: true, minLength: 2, maxLength: 250, placeholder: 'e.g. Suspended' },
    { key: 'stype', label: 'Type', type: 'select', required: true, options: STATUS_TYPES },
  ],
  filter: { key: 'stype', label: 'Type', allLabel: 'All types', labelOf: (v) => STATUS_TYPES.find((t) => t.value === v)?.label ?? String(v) },
  derive: (item) => ({ stypeLabel: STATUS_TYPES.find((t) => t.value === item['stype'])?.label ?? String(item['stype'] ?? '') }),
  deactivateWarning: (item) =>
    isCoreStatus(item['sname'])
      ? `"${item['sname']}" is a core status. The platform looks it up by name for sign-in, school activation and roles, so deactivating it can break those features. Only continue if you are sure.`
      : null,
  formNotice: 'Do not rename "Active" or "Inactive": the platform finds them by name, and renaming them can break sign-in and school activation.',
};
