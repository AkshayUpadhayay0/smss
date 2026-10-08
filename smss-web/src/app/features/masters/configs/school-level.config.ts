import { MasterConfig } from '../models/master.model';

export const SCHOOL_LEVEL_CONFIG: MasterConfig = {
  title: 'School Level',
  singular: 'school level',
  subtitle: 'Manage school levels such as Primary and Secondary',
  icon: 'layers',
  path: 'school-levels',
  idKey: 'schoolLevelId',
  columns: [
    { key: 'schoolLevelCode', label: 'Code', sortable: true, width: '180px' },
    { key: 'schoolLevelName', label: 'Name', sortable: true },
    { key: 'description', label: 'Description', sortable: true },
    { key: 'statusName', label: 'Status', sortable: true, type: 'status', width: '120px' },
  ],
  fields: [
    { key: 'schoolLevelCode', label: 'Code', type: 'text', required: true, minLength: 2, maxLength: 50, createOnly: true, placeholder: 'e.g. MIDDLE', helpText: 'Cannot be changed after creation.' },
    { key: 'schoolLevelName', label: 'Name', type: 'text', required: true, minLength: 2, maxLength: 150, placeholder: 'e.g. Middle School' },
    { key: 'description', label: 'Description', type: 'textarea', maxLength: 2000, placeholder: 'Optional' },
  ],
};
