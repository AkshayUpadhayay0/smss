import { Injectable, Injector, computed, effect, inject, signal, untracked } from '@angular/core';
import { AcademicYear } from '../../features/school-setup/models/academic-year.model';
import { AcademicYearService } from '../../features/school-setup/services/academic-year.service';
import { SelectOption } from '../../shared/components';
import { AuthService } from './auth.service';

const STORAGE_KEY = 'smss.viewingYear';
/** What the choice is when nobody picked a specific year: it FOLLOWS the school's current year, so it is stored as this marker and never as an id. */
const CURRENT = 'current';
/** The staff roles that have year-scoped pages. Teacher, Student and Guardian never see the selector. */
const CHOOSERS: readonly string[] = ['School Admin', 'Principal', 'Accountant'];

export type YearContextState = 'idle' | 'loading' | 'none' | 'ready' | 'failed';

/**
 * The ONE "viewing year" of the web app: which academic year the reports, lists and dashboards LOOK at. It never changes the school's current year and never decides where data is
 * entered (attendance, homework, class tests, timetables, admissions always act on the current year: those pages show a banner when the two differ).
 *
 * - Default: the current year, stored as a "current" MARKER, so when an admin moves the current flag everyone who did not pick a year follows it. Only an explicit pick of a
 *   specific year is stored as an id; if that year no longer exists the choice falls back to current. Picking the year that happens to be current also stores the marker.
 * - Kept for the browser session (survives a page reload in the tab), and starts again at "current" at every new sign-in.
 * - Every API call still carries its own explicit academicYearId (the server validates it against the token's school): this service only says WHICH one.
 */
@Injectable({ providedIn: 'root' })
export class YearContextService {
  private readonly auth = inject(AuthService);
  private readonly yearApi = inject(AcademicYearService);
  private readonly injector = inject(Injector);

  private readonly years = signal<AcademicYear[] | null>(null);
  private readonly failed = signal(false);
  private readonly choice = signal<string>(this.readStored());
  private seenEpoch = this.auth.loginEpoch();
  private loading = false;

  /** The signed-in user is a staff role that has year-scoped pages (the selector is for them only). */
  readonly canChoose = computed(() => this.auth.currentUserRoles().some((r) => CHOOSERS.includes(r)) && !!this.auth.user()?.schoolId);

  readonly allYears = computed(() => this.years() ?? []);
  readonly state = computed<YearContextState>(() => {
    if (!this.canChoose()) return 'idle';
    if (this.failed()) return 'failed';
    const y = this.years();
    if (y === null) return 'loading';
    return y.length === 0 ? 'none' : 'ready';
  });
  /** The school's current year (null when it has none, or before the years are known). */
  readonly currentYear = computed(() => this.years()?.find((y) => y.isCurrent) ?? null);
  /** The year being looked at: the explicit pick if it still exists, else the current year. */
  readonly viewingYear = computed<AcademicYear | null>(() => {
    const list = this.years();
    if (!list) return null;
    const c = this.choice();
    const picked = c === CURRENT ? null : (list.find((y) => String(y.academicYearId) === c) ?? null);
    return picked ?? this.currentYear() ?? list[0] ?? null;
  });
  /** What pages send as academicYearId, or null while it is not known yet (pages show their loading state until it is). */
  readonly viewingYearId = computed(() => this.viewingYear()?.academicYearId ?? null);
  readonly viewingYearName = computed(() => this.viewingYear()?.yearName ?? '');
  /** The viewed year is not the school's current one: shown as a chip in the header, and as a banner on pages that write. */
  readonly isNonCurrent = computed(() => {
    const v = this.viewingYear();
    const c = this.currentYear();
    return !!v && !!c && v.academicYearId !== c.academicYearId;
  });
  /** Selector options: every year of the school, labelled Current, Closed, Upcoming (or Past). */
  readonly options = computed<SelectOption[]>(() => this.allYears().map((y) => ({ label: `${y.yearName} (${yearLabel(y)})`, value: String(y.academicYearId) })));
  /** The selector's value: the id of the year on screen. */
  readonly selectedValue = computed(() => String(this.viewingYearId() ?? ''));
  /** Pages with writes disable them while a non-current year is shown. */
  readonly readOnlyView = this.isNonCurrent;
  /** The stored choice ("current" or a year id): exposed for tests and the selector's accessibility text only. */
  readonly storedChoice = this.choice.asReadonly();

  constructor() {
    effect(
      () => {
        const epoch = this.auth.loginEpoch();
        const signedIn = !!this.auth.user();
        untracked(() => {
          if (epoch !== this.seenEpoch) {
            this.seenEpoch = epoch;
            this.reset(); // a new sign-in: back to "current"
          }
          if (!signedIn) {
            this.reset();
            this.years.set(null);
            this.failed.set(false);
            return;
          }
          if (this.canChoose() && this.years() === null) this.load();
        });
      },
      { injector: this.injector },
    );
  }

  /** Pick a year, or the marker. The year that is currently current is stored as the marker, so it keeps following the flag. */
  select(value: string | number | null): void {
    const id = value === null || value === '' ? CURRENT : String(value);
    const year = this.years()?.find((y) => String(y.academicYearId) === id);
    const next = id === CURRENT || !year || year.isCurrent ? CURRENT : id;
    this.choice.set(next);
    this.write(next);
  }

  /** Back to the school's current year (the chip's "Back to current"). */
  useCurrent(): void {
    this.select(null);
  }

  /** Re-reads the school's years (after the current flag moved, or a year was added or removed). A stored pick of a year that is gone falls back to current. */
  refresh(): void {
    if (this.canChoose()) this.load();
  }

  private load(): void {
    if (this.loading) return;
    this.loading = true;
    this.failed.set(false);
    this.yearApi.list().subscribe({
      next: (list) => {
        this.loading = false;
        this.years.set(list);
        const c = this.choice();
        if (c !== CURRENT && !list.some((y) => String(y.academicYearId) === c)) this.select(null); // the stored year no longer exists
      },
      error: () => {
        this.loading = false;
        this.failed.set(true); // pages fall back to their own default (the server's current year)
      },
    });
  }

  private reset(): void {
    this.choice.set(CURRENT);
    this.write(CURRENT);
  }

  private readStored(): string {
    try {
      const raw = sessionStorage.getItem(STORAGE_KEY);
      return raw === CURRENT || (raw !== null && /^\d{1,12}$/.test(raw)) ? raw : CURRENT; // an invalid stored value is ignored
    } catch {
      return CURRENT;
    }
  }

  private write(value: string): void {
    try {
      sessionStorage.setItem(STORAGE_KEY, value);
    } catch {
      /* blocked storage: the choice still holds for this page load */
    }
  }
}

/** Current, Closed, Upcoming; anything else the school has (a year that ended, or one in session that is not flagged) is Past. */
export function yearLabel(y: Pick<AcademicYear, 'isCurrent' | 'isClosed' | 'state'>): string {
  if (y.isCurrent) return 'Current';
  if (y.isClosed || y.state === 'CLOSED') return 'Closed';
  if (y.state === 'UPCOMING') return 'Upcoming';
  return 'Past';
}
