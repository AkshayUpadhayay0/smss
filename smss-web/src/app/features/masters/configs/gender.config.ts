import { MasterConfig } from '../models/master.model';

export const GENDER_CONFIG: MasterConfig = {
  title: 'Gender',
  singular: 'gender',
  subtitle: 'Manage the gender options available on student and employee records',
  icon: 'user',
  path: 'genders',
  idKey: 'genderId',
  columns: [
    { key: 'genderCode', label: 'Code', sortable: true, width: '160px' },
    { key: 'genderName', label: 'Name', sortable: true },
    { key: 'description', label: 'Description', sortable: true },
    { key: 'statusName', label: 'Status', sortable: true, type: 'status', width: '120px' },
  ],
  fields: [
    { key: 'genderCode', label: 'Code', type: 'text', required: true, minLength: 2, maxLength: 20, createOnly: true, placeholder: 'e.g. MALE', helpText: 'Cannot be changed after creation.' },
    { key: 'genderName', label: 'Name', type: 'text', required: true, minLength: 2, maxLength: 50, placeholder: 'e.g. Male' },
    { key: 'description', label: 'Description', type: 'textarea', maxLength: 250, placeholder: 'Optional' },
  ],
};
