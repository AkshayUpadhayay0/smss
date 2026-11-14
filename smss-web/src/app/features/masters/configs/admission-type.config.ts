import { MasterConfig } from '../models/master.model';

/** Admission Type is a GLOBAL master (shared by every school); only the Super Admin edits it. Deactivating never hides it on existing applications. */
export const ADMISSION_TYPE_CONFIG: MasterConfig = {
  title: 'Admission Type',
  singular: 'admission type',
  subtitle: 'Manage the ways students can be admitted, shared by every school',
  icon: 'inbox',
  path: 'admission-types',
  idKey: 'admissionTypeId',
  columns: [
    { key: 'typeCode', label: 'Code', sortable: true, width: '180px' },
    { key: 'typeName', label: 'Name', sortable: true },
    { key: 'description', label: 'Description', sortable: true },
    { key: 'statusName', label: 'Status', sortable: true, type: 'status', width: '120px' },
  ],
  fields: [
    { key: 'typeCode', label: 'Code', type: 'text', required: true, minLength: 2, maxLength: 30, createOnly: true, placeholder: 'e.g. NEW_ADMISSION', helpText: 'Cannot be changed after creation.' },
    { key: 'typeName', label: 'Name', type: 'text', required: true, minLength: 2, maxLength: 100, placeholder: 'e.g. New Admission' },
    { key: 'description', label: 'Description', type: 'textarea', maxLength: 250, placeholder: 'Optional' },
  ],
};
