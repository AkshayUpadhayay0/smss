import { MasterConfig } from '../models/master.model';

export const BLOOD_GROUP_CONFIG: MasterConfig = {
  title: 'Blood Group',
  singular: 'blood group',
  subtitle: 'Manage blood groups such as A+ and O-',
  icon: 'circle',
  path: 'blood-groups',
  idKey: 'bloodGroupId',
  columns: [
    { key: 'bloodGroupCode', label: 'Code', sortable: true, width: '160px' },
    { key: 'bloodGroupName', label: 'Name', sortable: true },
    { key: 'description', label: 'Description', sortable: true },
    { key: 'statusName', label: 'Status', sortable: true, type: 'status', width: '120px' },
  ],
  fields: [
    { key: 'bloodGroupCode', label: 'Code', type: 'text', required: true, minLength: 2, maxLength: 10, createOnly: true, placeholder: 'e.g. A_POS', helpText: 'Cannot be changed after creation.' },
    { key: 'bloodGroupName', label: 'Name', type: 'text', required: true, minLength: 2, maxLength: 10, placeholder: 'e.g. A+' },
    { key: 'description', label: 'Description', type: 'textarea', maxLength: 250, placeholder: 'Optional' },
  ],
};
