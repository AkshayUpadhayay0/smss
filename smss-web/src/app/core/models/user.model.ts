export type UserRole = 'Administrator' | 'Manager' | 'Staff' | 'Viewer' | (string & {});

export interface User {
  id: string;
  organizationId: string;
  fullName: string;
  email: string;
  role: UserRole;
  avatarUrl?: string;
  status: 'Active' | 'Inactive' | 'Pending';
  createdAt: string;
}

export interface RegisteredAccount {
  organization: {
    id: string;
    name: string;
    code: string;
    theme: string;
  };
  user: User;
  passwordHash: string;
}
