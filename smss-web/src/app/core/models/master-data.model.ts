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
  description?: string | null;
  isActive: boolean;
}

export interface SchoolType {
  schoolTypeId: number;
  schoolTypeCode: string;
  schoolTypeName: string;
  description?: string | null;
  isActive: boolean;
}

export interface SchoolLevel {
  schoolLevelId: number;
  schoolLevelCode: string;
  schoolLevelName: string;
  description?: string | null;
  isActive: boolean;
}

export interface RoleLookup {
  roleId: number;
  roleName: string;
  roleCode: string;
  description?: string | null;
  statusId?: number | null;
}
