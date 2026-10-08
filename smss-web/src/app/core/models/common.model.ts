export interface BreadcrumbItem {
  label: string;
  url?: string;
}

export interface NavChild {
  label: string;
  route: string;
  icon?: string;
}

export interface NavItem {
  label: string;
  route?: string;
  icon: string;
  children?: NavChild[];
}

export type SortDirection = 'asc' | 'desc' | null;

export interface TableColumn<T = any> {
  key: keyof T & string;
  label: string;
  sortable?: boolean;
  width?: string;
  /** Extra class on the column's th/td, e.g. 'hide-mobile'. */
  cellClass?: string;
}

export interface UserTableRow {
  id: string;
  name: string;
  email: string;
  organization: string;
  role: string;
  status: 'Active' | 'Inactive' | 'Pending';
  createdDate: string;
}

export type ToastVariant = 'success' | 'info' | 'warning' | 'danger';

export interface ToastMessage {
  id: number;
  variant: ToastVariant;
  title: string;
  message?: string;
  duration?: number;
}
