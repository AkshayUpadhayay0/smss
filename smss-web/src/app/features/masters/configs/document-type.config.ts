import { MasterConfig } from '../models/master.model';

export const DOCUMENT_TYPE_CONFIG: MasterConfig = {
  title: 'Document Type',
  singular: 'document type',
  subtitle: 'Manage the document types a school can collect, such as Aadhar Card',
  icon: 'file-text',
  path: 'document-types',
  idKey: 'documentTypeId',
  columns: [
    { key: 'documentTypeCode', label: 'Code', sortable: true, width: '160px' },
    { key: 'documentTypeName', label: 'Name', sortable: true },
    { key: 'description', label: 'Description', sortable: true },
    { key: 'statusName', label: 'Status', sortable: true, type: 'status', width: '120px' },
  ],
  fields: [
    { key: 'documentTypeCode', label: 'Code', type: 'text', required: true, minLength: 2, maxLength: 30, createOnly: true, placeholder: 'e.g. AADHAR', helpText: 'Cannot be changed after creation.' },
    { key: 'documentTypeName', label: 'Name', type: 'text', required: true, minLength: 2, maxLength: 100, placeholder: 'e.g. Aadhar Card' },
    { key: 'description', label: 'Description', type: 'textarea', maxLength: 250, placeholder: 'Optional' },
  ],
};
