import { Organization } from '../models';

/**
 * Demo organization pre-seeded into localStorage on first run so the
 * template can be tested immediately without going through registration.
 */
export const DEMO_ORGANIZATION: Organization = {
  id: 'org-demo-001',
  name: 'Demo School',
  code: 'DEMO',
  theme: 'blue',
  createdAt: new Date().toISOString(),
};

export const SAMPLE_ORGANIZATIONS: Organization[] = [
  DEMO_ORGANIZATION,
  {
    id: 'org-002',
    name: 'ABC International School',
    code: 'ABC-INTL',
    theme: 'green',
    createdAt: new Date().toISOString(),
  },
  {
    id: 'org-003',
    name: 'Northgate Academy',
    code: 'NGA',
    theme: 'purple',
    createdAt: new Date().toISOString(),
  },
  {
    id: 'org-004',
    name: 'Sunrise Public School',
    code: 'SPS',
    theme: 'orange',
    createdAt: new Date().toISOString(),
  },
];
