export type NoticeAudience = 'ALL' | 'STAFF' | 'STUDENTS_GUARDIANS';

export interface Notice {
  noticeId: number;
  title: string;
  body: string;
  audience: NoticeAudience;
  createdBy: number;
  createdByName: string | null;
  publishedAt: string;
  updatedAt: string;
  /** Whether the signed-in user has opened it. */
  isRead: boolean;
  statusName: string;
}

export interface SaveNoticeRequest {
  title: string;
  body: string;
  audience: NoticeAudience;
}

export interface NoticeFilters {
  audience?: NoticeAudience | '';
  unreadOnly?: boolean;
  /** yyyy-MM-dd, the published day in the school's time zone. */
  from?: string;
  to?: string;
}

export const MAX_TITLE = 200;
export const MAX_BODY = 5000;
