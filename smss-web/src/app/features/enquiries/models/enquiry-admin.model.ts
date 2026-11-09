export type EnquiryKind = 'CONTACT' | 'DEMO';
export type EnquiryStatus = 'NEW' | 'CONTACTED' | 'DEMO_SCHEDULED' | 'CLOSED' | 'SPAM';

export const ENQUIRY_STATUSES: readonly EnquiryStatus[] = ['NEW', 'CONTACTED', 'DEMO_SCHEDULED', 'CLOSED', 'SPAM'];

export const STATUS_LABEL: Record<EnquiryStatus, string> = {
  NEW: 'New',
  CONTACTED: 'Contacted',
  DEMO_SCHEDULED: 'Demo scheduled',
  CLOSED: 'Closed',
  SPAM: 'Spam',
};

export const TYPE_LABEL: Record<EnquiryKind, string> = { DEMO: 'Demo request', CONTACT: 'Contact message' };

/** Pill colour per status (see TableColumn.tone). */
export const STATUS_TONE: Record<EnquiryStatus, string> = {
  NEW: 'blue',
  CONTACTED: 'yellow',
  DEMO_SCHEDULED: 'violet',
  CLOSED: 'green',
  SPAM: 'red',
};

export interface EnquiryListItem {
  enquiryId: number;
  enquiryType: EnquiryKind;
  fullName: string;
  schoolName: string | null;
  email: string;
  phone: string | null;
  city: string | null;
  subject: string | null;
  status: EnquiryStatus;
  createdAt: string;
}

export interface EnquiryListResult {
  items: EnquiryListItem[];
  total: number;
  page: number;
  pageSize: number;
}

export interface EnquiryDetail extends EnquiryListItem {
  designation: string | null;
  studentCountRange: string | null;
  message: string | null;
  preferredDemoDate: string | null;
  preferredTimeSlot: string | null;
  consentGiven: boolean;
  adminNotes: string | null;
  handledByName: string | null;
  updatedAt: string | null;
}

export interface EnquirySummary {
  new: number;
  contacted: number;
  demoScheduled: number;
  newDemo: number;
  newContact: number;
  totalThisMonth: number;
}

export interface EnquiryQuery {
  type?: EnquiryKind | '';
  status?: EnquiryStatus | '';
  from?: string;
  to?: string;
  search?: string;
  page: number;
  pageSize: number;
}
