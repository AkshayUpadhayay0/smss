import { MasterConfig } from '../models/master.model';

export const RELIGION_CATEGORY_CONFIG: MasterConfig = {
  title: 'Religion/Caste Category',
  singular: 'religion/caste category',
  subtitle: 'Manage religion and caste categories',
  icon: 'shield',
  path: 'religion-categories',
  idKey: 'religionCategoryId',
  columns: [
    { key: 'religionCode', label: 'Code', sortable: true, width: '160px' },
    { key: 'religionName', label: 'Name', sortable: true },
    { key: 'description', label: 'Description', sortable: true },
    { key: 'statusName', label: 'Status', sortable: true, type: 'status', width: '120px' },
  ],
  fields: [
    { key: 'religionCode', label: 'Code', type: 'text', required: true, minLength: 2, maxLength: 30, createOnly: true, placeholder: 'e.g. HINDU', helpText: 'Cannot be changed after creation.' },
    { key: 'religionName', label: 'Name', type: 'text', required: true, minLength: 2, maxLength: 100, placeholder: 'e.g. Hindu' },
    { key: 'description', label: 'Description', type: 'textarea', maxLength: 250, placeholder: 'Optional' },
  ],
};
