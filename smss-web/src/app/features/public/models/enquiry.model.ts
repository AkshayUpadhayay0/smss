export type EnquiryType = 'CONTACT' | 'DEMO';

/** Body of POST /api/public/enquiries. The API validates the same rules again. */
export interface CreateEnquiryRequest {
  enquiryType: EnquiryType;
  fullName: string;
  designation?: string | null;
  schoolName?: string | null;
  email: string;
  phone?: string | null;
  city?: string | null;
  studentCountRange?: string | null;
  subject?: string | null;
  message?: string | null;
  /** yyyy-MM-dd */
  preferredDemoDate?: string | null;
  preferredTimeSlot?: string | null;
  consentGiven: boolean;
  /** Honeypot: stays empty for real visitors. */
  website: string;
}

/** The choices below must match EnquiryChoices on the API (the server rejects anything else). */
export const STUDENT_COUNT_RANGES = ['Up to 200', '201 - 500', '501 - 1,000', '1,001 - 2,500', 'More than 2,500'] as const;
export const TIME_SLOTS = ['Morning (9 AM - 12 PM)', 'Afternoon (12 PM - 4 PM)', 'Evening (4 PM - 7 PM)'] as const;

/** Same patterns as EnquiryRules on the API. */
export const EMAIL_PATTERN = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;
export const PHONE_PATTERN = /^\+?[0-9][0-9 ()-]{5,28}[0-9]$/;
