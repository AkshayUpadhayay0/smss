import { MasterConfig } from '../models/master.model';

export const STUDENT_CATEGORY_CONFIG: MasterConfig = {
  title: 'Student Category',
  singular: 'student category',
  subtitle: 'Manage the categories students can be grouped under, such as General and Scholarship',
  icon: 'layers',
  path: 'student-categories',
  idKey: 'studentCategoryId',
  columns: [
    { key: 'categoryCode', label: 'Code', sortable: true, width: '160px' },
    { key: 'categoryName', label: 'Name', sortable: true },
    { key: 'description', label: 'Description', sortable: true },
    { key: 'statusName', label: 'Status', sortable: true, type: 'status', width: '120px' },
  ],
  fields: [
    { key: 'categoryCode', label: 'Code', type: 'text', required: true, minLength: 2, maxLength: 30, createOnly: true, placeholder: 'e.g. GENERAL', helpText: 'Cannot be changed after creation.' },
    { key: 'categoryName', label: 'Name', type: 'text', required: true, minLength: 2, maxLength: 100, placeholder: 'e.g. General' },
    { key: 'description', label: 'Description', type: 'textarea', maxLength: 250, placeholder: 'Optional' },
  ],
};
