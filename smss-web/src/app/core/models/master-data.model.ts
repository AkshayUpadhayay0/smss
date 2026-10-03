export interface Country {
  cid: number;
  cname: string;
}

export interface State {
  cid: number;
  sid: number;
  sname: string;
}

export interface District {
  cid: number;
  sid: number;
  did: number;
  dname: string;
}

export interface City {
  cid: number;
  sid: number;
  did: number;
  cityId: number;
  cityName: string;
}

export type StatusType = 'general status' | 'school plan status' | 'payment status';

export interface StatusLookup {
  sid: number;
  sname: string;
  stype: StatusType;
}

export interface BoardType {
  boardTypeId: number;
  boardCode: string;
  boardName: string;
  description?: string;
  isActive: boolean;
  createdAt: string;
  updatedAt: string;
}

export interface SchoolType {
  schoolTypeId: number;
  schoolTypeCode: string;
  schoolTypeName: string;
  description?: string;
  isActive: boolean;
  createdAt: string;
  updatedAt: string;
}

export interface SchoolLevel {
  schoolLevelId: number;
  schoolLevelCode: string;
  schoolLevelName: string;
  description?: string;
  isActive: boolean;
  createdAt: string;
  updatedAt: string;
}

export interface RoleLookup {
  roleId: number;
  roleName: string;
  roleCode: string;
  description?: string;
  statusId?: number;
  createdAt: string;
  updatedAt: string;
}








export interface ApiResponse<T> {
  status: boolean;
  statusCode: number;
  message: string;
  data: T | null;
}

export type MasterItem = {
  isActive: boolean;
  isProtected?: boolean; 
  description?: string | null;
  createdAt?: string;
  updatedAt?: string;
} & Record<string, any>;

export interface MasterConfig {
  title: string;
  singular: string;
  subtitle: string;
  apiPath: string;
  keys: { id: string; code?: string; name: string };   // code is optional (Status has none)
  codeLabel?: string;
  nameLabel: string;
  codeMaxLength?: number;
  nameMaxLength: number;
  hasDescription?: boolean;                             // default true
  typeField?: { key: string; label: string; maxLength: number; placeholder?: string };
}