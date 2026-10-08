import { MasterConfig } from '../models/master.model';

export const SCHOOL_TYPE_CONFIG: MasterConfig = {
  title: 'School Type',
  singular: 'school type',
  subtitle: 'Manage school types such as Private and Government',
  icon: 'layers',
  path: 'school-types',
  idKey: 'schoolTypeId',
  columns: [
    { key: 'schoolTypeCode', label: 'Code', sortable: true, width: '180px' },
    { key: 'schoolTypeName', label: 'Name', sortable: true },
    { key: 'description', label: 'Description', sortable: true },
    { key: 'statusName', label: 'Status', sortable: true, type: 'status', width: '120px' },
  ],
  fields: [
    { key: 'schoolTypeCode', label: 'Code', type: 'text', required: true, minLength: 2, maxLength: 50, createOnly: true, placeholder: 'e.g. GOVT_AIDED', helpText: 'Cannot be changed after creation.' },
    { key: 'schoolTypeName', label: 'Name', type: 'text', required: true, minLength: 2, maxLength: 150, placeholder: 'e.g. Government Aided School' },
    { key: 'description', label: 'Description', type: 'textarea', maxLength: 2000, placeholder: 'Optional' },
  ],
};
