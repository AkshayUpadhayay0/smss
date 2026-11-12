import { AdmissionDashboard, AdmissionDashboardPart } from '../models/admission-dashboard.model';
import { DashStatCard, plural, statCard } from './role-dashboard-format';

const down = (d: AdmissionDashboard, ...parts: AdmissionDashboardPart[]) => parts.some((p) => d.partsUnavailable.includes(p));

/**
 * The three headline cards. Everything goes to /admissions, the one screen Admission Staff work in. A card whose part could not load says "Unavailable", never 0; a real 0 is
 * written out ("None this week", "All documents in").
 */
export function buildAdmissionStats(d: AdmissionDashboard): DashStatCard[] {
  return [
    statCard({ id: 'total', label: 'Applications', value: String(d.totalApplications ?? 0), note: 'In total, every status', link: '/admissions', tone: 'primary' }, down(d, 'applications') || d.totalApplications == null),
    statCard(
      {
        id: 'new',
        label: 'New this week',
        value: d.newThisWeek === 0 ? 'None this week' : String(d.newThisWeek ?? 0),
        note: 'Received in the last 7 days',
        link: '/admissions',
        tone: 'info',
      },
      down(d, 'applications') || d.newThisWeek == null,
    ),
    statCard(
      {
        id: 'documents',
        label: 'Missing documents',
        value: d.missingDocumentsCount === 0 ? 'All documents in' : String(d.missingDocumentsCount ?? 0),
        note: d.missingDocumentsCount === 0 ? 'Every open application is complete' : `${plural(d.missingDocumentsCount ?? 0, 'open application')} missing a required document`,
        link: '/admissions',
        tone: 'danger',
      },
      down(d, 'documents') || d.missingDocumentsCount == null,
    ),
  ];
}
