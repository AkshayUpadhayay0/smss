import { MasterConfig } from '../models/master.model';

export const ROLE_CONFIG: MasterConfig = {
  title: 'Role',
  singular: 'role',
  subtitle: 'Manage user roles',
  icon: 'shield',
  path: 'role',
  idKey: 'roleId',
  columns: [
    { key: 'roleCode', label: 'Code', sortable: true, width: '190px' },
    { key: 'roleName', label: 'Name', sortable: true },
    { key: 'description', label: 'Description', sortable: true },
    { key: 'roleType', label: 'Type', sortable: true, width: '110px' },
    { key: 'statusName', label: 'Status', sortable: true, type: 'status', width: '120px' },
  ],
  fields: [
    { key: 'roleCode', label: 'Code', type: 'text', required: true, minLength: 2, maxLength: 50, createOnly: true, placeholder: 'e.g. LIBRARIAN', helpText: 'Cannot be changed after creation.' },
    { key: 'roleName', label: 'Name', type: 'text', required: true, minLength: 2, maxLength: 100, placeholder: 'e.g. Librarian' },
    { key: 'description', label: 'Description', type: 'textarea', maxLength: 2000, placeholder: 'Optional' },
  ],
  // `isProtected` comes from the server (it decides which roles are system roles).
  derive: (item) => ({ roleType: item['isProtected'] ? 'System' : 'Custom' }),
  deactivateWarning: (item) =>
    item['isProtected']
      ? `"${item['roleName']}" is a system role that the platform depends on. The server will refuse to deactivate it. Do you want to try anyway?`
      : null, // standard confirm; if users are still assigned, the server refuses and its message is shown
};
