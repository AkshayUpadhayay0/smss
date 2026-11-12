import { DashboardAdmission } from './school-dashboard.model';

/** GET /api/AdmissionDashboard/summary (Admission Staff only). Mirrors AdmissionDashboardDtos.cs. */

export type AdmissionDashboardPart = 'applications' | 'documents';

export interface AdmissionStatusCount {
  /** APPLICATION_SUBMITTED / UNDER_REVIEW / APPROVED / ADMITTED / REJECTED / CANCELLED */
  statusName: string;
  count: number;
}

export interface AdmissionMissingDocuments {
  admissionId: number;
  applicationNumber: string;
  applicantFullName: string;
  statusName: string;
  applicationDate: string;
  missingDocuments: string[];
}

export interface AdmissionDashboard {
  today: string;
  /** Every status in pipeline order, zero-filled. Empty when applications are unavailable. */
  byStatus: AdmissionStatusCount[];
  totalApplications: number | null;
  /** Dated in the last 7 days, today included. */
  newThisWeek: number | null;
  /** Open applications missing at least one required document. */
  missingDocumentsCount: number | null;
  missingDocuments: AdmissionMissingDocuments[];
  recentApplications: DashboardAdmission[];
  partsUnavailable: AdmissionDashboardPart[];
}
