import { MasterConfig } from '../models/master.model';

export const BOARD_TYPE_CONFIG: MasterConfig = {
  title: 'Board Type',
  singular: 'board type',
  subtitle: 'Manage education boards such as CBSE and ICSE',
  icon: 'layers',
  path: 'board-types',
  idKey: 'boardTypeId',
  columns: [
    { key: 'boardCode', label: 'Code', sortable: true, width: '160px' },
    { key: 'boardName', label: 'Name', sortable: true },
    { key: 'description', label: 'Description', sortable: true },
    { key: 'statusName', label: 'Status', sortable: true, type: 'status', width: '120px' },
  ],
  fields: [
    { key: 'boardCode', label: 'Code', type: 'text', required: true, minLength: 2, maxLength: 50, createOnly: true, placeholder: 'e.g. CBSE', helpText: 'Cannot be changed after creation.' },
    { key: 'boardName', label: 'Name', type: 'text', required: true, minLength: 2, maxLength: 150, placeholder: 'e.g. Central Board of Secondary Education' },
    { key: 'description', label: 'Description', type: 'textarea', maxLength: 2000, placeholder: 'Optional' },
  ],
};
