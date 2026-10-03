import { MasterConfig } from '../../core/models/master-data.model';

export const BOARD_TYPE_CONFIG: MasterConfig = {
  title: 'Board Types',
  singular: 'Board Type',
  subtitle: 'Education boards available when registering a school.',
  apiPath: 'board-types',
  keys: { id: 'boardTypeId', code: 'boardCode', name: 'boardName' },
  codeLabel: 'Board Code',
  nameLabel: 'Board Name',
  codeMaxLength: 50,
  nameMaxLength: 150
};

export const SCHOOL_TYPE_CONFIG: MasterConfig = {
  title: 'School Types',
  singular: 'School Type',
  subtitle: 'School categories available when registering a school.',
  apiPath: 'school-types',
  keys: { id: 'schoolTypeId', code: 'schoolTypeCode', name: 'schoolTypeName' },
  codeLabel: 'School Type Code',
  nameLabel: 'School Type Name',
  codeMaxLength: 50,
  nameMaxLength: 150
};

export const SCHOOL_LEVEL_CONFIG: MasterConfig = {
  title: 'School Levels',
  singular: 'School Level',
  subtitle: 'Education levels (for example primary, secondary) available when registering a school.',
  apiPath: 'school-levels',
  keys: { id: 'schoolLevelId', code: 'schoolLevelCode', name: 'schoolLevelName' },
  codeLabel: 'School Level Code',
  nameLabel: 'School Level Name',
  codeMaxLength: 50,
  nameMaxLength: 150
};

export const STATUS_CONFIG: MasterConfig = {
  title: 'Status',
  singular: 'Status',
  subtitle: 'Status values used across the system (schools, users, contacts).',
  apiPath: 'status',
  keys: { id: 'sid', name: 'sname' },
  nameLabel: 'Status Name',
  nameMaxLength: 250,
  hasDescription: false,
  typeField: { key: 'stype', label: 'Status Type', maxLength: 250, placeholder: 'e.g. School' }
};

export const ROLE_CONFIG: MasterConfig = {
  title: 'Roles',
  singular: 'Role',
  subtitle: 'Roles that can be assigned to users. System roles cannot be deactivated.',
  apiPath: 'role',
  keys: { id: 'roleId', code: 'roleCode', name: 'roleName' },
  codeLabel: 'Role Code',
  nameLabel: 'Role Name',
  codeMaxLength: 50,
  nameMaxLength: 100
};