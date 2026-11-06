// Mirrors ReportResultDtos.cs (Examination / Report Card, stage 3: results and publishing).

export type ResultStatus = 'PASS' | 'FAIL' | 'INCOMPLETE';
export type ResultScope = 'TERM' | 'YEAR';

export interface ResultStudent {
  studentId: string; name: string; rollNumber: string; admissionNumber: string; result: ResultStatus;
  obtained?: number | null; outOf?: number | null; percent?: number | null; grade?: string | null; rank?: number | null; failedSubjects: string[];
}

export interface Publication {
  publicationId: number; schemeId: number; schemeName: string; sectionId: number; className: string; sectionName: string; scope: ResultScope; termId?: number | null; termName?: string | null;
  version: number; status: 'PUBLISHED' | 'SUPERSEDED'; publishedAt: string; note?: string | null; studentCount: number; excludedCount: number;
}

export interface ResultPreview {
  schemeId: number; schemeName: string; sectionId: number; className: string; sectionName: string; scope: ResultScope; termId?: number | null; termName?: string | null;
  mode: string; templateKey: string; templateAvailable: boolean; templateMessage?: string | null; showRank: boolean; unlockedTerms: string[]; canPublish: boolean; canDownloadDraft: boolean;
  attendanceSource: string; unmarkedDates: string[]; students: ResultStudent[]; incompleteCount: number; current?: Publication | null;
}

export interface PublishRequest { schemeId: number; sectionId: number; scope: ResultScope; termId?: number | null; note?: string | null; confirmExcluded: boolean; acknowledgeAttendance: boolean }
export interface ExcludedStudent { studentId: string; name: string; rollNumber: string }
export interface PublishResult { published: boolean; needsConfirmation: boolean; reason?: 'EXCLUDED' | 'ATTENDANCE' | null; excluded: ExcludedStudent[]; unmarkedDates: string[]; publication?: Publication | null }

export interface PublicationCard { studentId: string; name: string; admissionNumber: string; rollNumber: string; result: ResultStatus; obtained?: number | null; outOf?: number | null; percent?: number | null; grade?: string | null; rank?: number | null }
export interface PublicationDetail { publication: Publication; cards: PublicationCard[]; excluded: ExcludedStudent[] }

export interface MyReportCard {
  publicationId: number; schemeName: string; className: string; sectionName: string; scope: ResultScope; termName?: string | null; version: number; publishedAt: string;
  result: ResultStatus; percent?: number | null; grade?: string | null; rank?: number | null;
}

/** The part of the stored card the on-screen summary reads. */
export interface StoredCard {
  sessionName: string; mode: string; showRank: boolean;
  terms: { termId: number; name: string }[];
  subjects: { name: string; terms: { termId: number; obtained: number; outOf: number; grade?: string | null; failed: boolean }[] }[];
  termSummaries: { termId: number; obtained: number; outOf: number; percent?: number | null; grade?: string | null; result: ResultStatus; rank?: number | null; workingDays?: number | null; presentDays?: number | null; remark?: string | null }[];
  overall: { obtained: number; outOf: number; percent?: number | null; grade?: string | null; result: ResultStatus; rank?: number | null; rankedCount?: number | null };
  promotion: { decision?: string | null; promotedTo?: string | null; newSessionDate?: string | null };
}
export interface MyReportCardDetail { card: MyReportCard; model: StoredCard }

export const RESULT_LABEL: Record<ResultStatus, string> = { PASS: 'Passed', FAIL: 'Failed', INCOMPLETE: 'Incomplete' };
