// Mirrors SchoolEventDto / EventCategoryDto / SchoolEventRequest (the academic calendar).

export interface EventCategory {
  eventCategoryId: number;
  categoryName: string;
  categoryCode: string;
  /** #rrggbb */
  colorHex: string;
}

export interface SchoolEvent {
  eventId: number;
  academicYearId: number;
  eventTitle: string;
  /** yyyy-MM-dd */
  startDate: string;
  endDate: string;
  isAllDay: boolean;
  /** HH:mm:ss, null for an all-day event */
  startTime?: string | null;
  endTime?: string | null;
  eventCategoryId: number;
  categoryName: string;
  categoryCode: string;
  colorHex: string;
  description?: string | null;
  createdByName?: string | null;
  /** Only on the upcoming list: started before today and still running. */
  isOngoing?: boolean;
}

export interface SchoolEventRequest {
  eventTitle: string;
  startDate: string;
  endDate?: string | null;
  isAllDay: boolean;
  startTime?: string | null;
  endTime?: string | null;
  eventCategoryId: number;
  description?: string | null;
}

/** Who may add / edit / delete events: the same two roles the API allows (it re-checks; this only decides whether to show the buttons). */
export const EVENT_MANAGER_ROLES = ['School Admin', 'Principal'] as const;
