/**
 * Application-wide constants.
 *
 * Anything that appeared as a magic string or a repeated literal
 * in more than one component belongs here.
 */

/** Institutional identity shown in the header and on the login screen. */
export const APP_IDENTITY = {
  shortName: 'UKIFLMS',
  systemName: 'Uttarakhand Intelligent Forest Land Management System',
  department: 'Uttarakhand Forest Department',
  government: 'Government of Uttarakhand',
  tagline: 'Forest Land Management',
} as const;

export const APP_LOGOS = {
  department: '/images/logos/uttarakhand-forest-dept.png',
  state: '/images/logos/Uttarakhand-Rajya-Color.png',
} as const;

export const FOOTER_TEXT = '© All rights reserved by UKIFLMS 2026';

/** Route the user lands on after a successful login. */
export const DEFAULT_AUTHENTICATED_ROUTE = '/admin-dashboard';

/** Route the user is sent to when unauthenticated. */
export const LOGIN_ROUTE = '/login';

/** Page sizes offered by every paginated table. */
export const PAGE_SIZE_OPTIONS: readonly number[] = [5, 10, 25, 50];

export const DEFAULT_PAGE_SIZE = 10;

/** Master record status values, as stored by the backend. */
export const RECORD_STATUS = {
  active: 'Active',
  inactive: 'Inactive',
} as const;

export type RecordStatus = (typeof RECORD_STATUS)[keyof typeof RECORD_STATUS];

/** File type discriminator sent with chunked upload metadata. */
export const UPLOAD_FILE_TYPE = 'Forest_Data';
