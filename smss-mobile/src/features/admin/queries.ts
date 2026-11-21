import { useQuery } from '@tanstack/react-query';
import * as admin from '../../api/admin';

// The cache is cleared on sign-in / sign-out, so the key needs no user id.
export const adminKeys = { dashboard: ['admin', 'dashboard'] as const };

export const useSchoolAdminDashboard = () => useQuery({ queryKey: adminKeys.dashboard, queryFn: admin.getSchoolAdminDashboard });
