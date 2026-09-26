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