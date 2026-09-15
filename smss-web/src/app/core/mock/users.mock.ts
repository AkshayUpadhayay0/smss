import { RegisteredAccount, User } from '../models';
import { DEMO_ORGANIZATION } from './organizations.mock';

export const DEMO_ADMIN_USER: User = {
  id: 'user-demo-admin',
  organizationId: DEMO_ORGANIZATION.id,
  fullName: 'Admin User',
  email: 'admin@example.com',
  role: 'Administrator',
  status: 'Active',
  createdAt: new Date().toISOString(),
};

export const DEMO_ACCOUNT: RegisteredAccount = {
  organization: {
    id: DEMO_ORGANIZATION.id,
    name: DEMO_ORGANIZATION.name,
    code: DEMO_ORGANIZATION.code,
    theme: DEMO_ORGANIZATION.theme,
  },
  user: DEMO_ADMIN_USER,
  // Mock/local-only credential store. NEVER do this in a real backend.
  passwordHash: 'demo1234',
};

export const SAMPLE_USERS: User[] = [
  DEMO_ADMIN_USER,
  {
    id: 'user-002',
    organizationId: DEMO_ORGANIZATION.id,
    fullName: 'Priya Sharma',
    email: 'priya.sharma@example.com',
    role: 'Manager',
    status: 'Active',
    createdAt: new Date().toISOString(),
  },
  {
    id: 'user-003',
    organizationId: DEMO_ORGANIZATION.id,
    fullName: 'Akshay Kumar',
    email: 'akshay.kumar@example.com',
    role: 'Staff',
    status: 'Pending',
    createdAt: new Date().toISOString(),
  },
  {
    id: 'user-004',
    organizationId: DEMO_ORGANIZATION.id,
    fullName: 'Meera Nair',
    email: 'meera.nair@example.com',
    role: 'Viewer',
    status: 'Inactive',
    createdAt: new Date().toISOString(),
  },
];
