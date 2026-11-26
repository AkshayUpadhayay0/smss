import { ChangeDetectionStrategy, Component, OnInit, computed, inject, input, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { finalize } from 'rxjs';
import { SecureImageComponent } from '../../../../shared/components';
import { FamilyChildCard, chipLabel, initials, readOnlyBanner } from '../../models/family.model';
import { FamilyService } from '../../services/family.service';
import {
  ChildAttendanceTabComponent, ChildExamsTabComponent, ChildFeesTabComponent, ChildHomeworkTabComponent, ChildLeaveTabComponent, ChildProfileTabComponent, ChildReportsTabComponent, ChildTimetableTabComponent,
} from './child-tabs';

export const CHILD_TABS = [
  { key: 'profile', label: 'Profile' }, { key: 'attendance', label: 'Attendance' }, { key: 'homework', label: 'Homework' }, { key: 'timetable', label: 'Timetable' },
  { key: 'exams', label: 'Exams' }, { key: 'reports', label: 'Report cards' }, { key: 'fees', label: 'Fees' }, { key: 'leave', label: 'Leave' },
] as const;
export type ChildTab = (typeof CHILD_TABS)[number]['key'];

/** One child's read-only dashboard, with a header switcher to move between children. A tab asks the server only when it is opened. The route carries the child id and the tab; the id is never put anywhere else. */
@Component({
  selector: 'app-child-dashboard',
  imports: [RouterLink, SecureImageComponent, ChildProfileTabComponent, ChildAttendanceTabComponent, ChildHomeworkTabComponent, ChildTimetableTabComponent, ChildExamsTabComponent, ChildReportsTabComponent, ChildFeesTabComponent, ChildLeaveTabComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './child-dashboard.component.scss',
  template: `
    <a class="back" routerLink="/family" data-link="back">← All children</a>
    @if (loading()) {
      <p role="status" data-state="loading">Loading…</p>
    } @else if (failed()) {
      <p class="alert" role="alert" data-state="failed">We could not load this child. <button type="button" (click)="load()" data-action="retry">Try again</button></p>
    } @else if (child(); as c) {
      <header class="head">
        <div class="photo"><app-secure-img [src]="c.profilePhotoUrl" [alt]="c.fullName"><span class="initials">{{ initials(c.fullName) }}</span></app-secure-img></div>
        <div class="who">
          <strong data-field="name">{{ c.fullName }}</strong>
          <span class="meta">{{ c.studentId }} · {{ c.className ? c.className + (c.sectionName ? ' – ' + c.sectionName : '') : 'Not in a class' }}</span>
        </div>
        @if (siblings().length > 1) {
          <label class="switch">Child
            <select [value]="c.studentId" (change)="switchTo($any($event.target).value)" data-field="switcher" aria-label="Switch child">
              @for (s of siblings(); track s.studentId) { <option [value]="s.studentId" [selected]="s.studentId === c.studentId">{{ s.fullName }}</option> }
            </select>
          </label>
        }
      </header>
      @if (banner(); as b) { <p class="banner" role="status" data-banner="read-only">{{ b }}</p> }

      <div class="tabs" role="tablist">
        @for (t of tabs; track t.key) {
          <button type="button" role="tab" [attr.aria-selected]="tab() === t.key" [class.active]="tab() === t.key" (click)="open(t.key)" [attr.data-tab-button]="t.key">{{ t.label }}</button>
        }
      </div>

      @switch (tab()) {
        @case ('profile') { <app-child-profile-tab [studentId]="c.studentId" /> }
        @case ('attendance') { <app-child-attendance-tab [studentId]="c.studentId" /> }
        @case ('homework') { <app-child-homework-tab [studentId]="c.studentId" /> }
        @case ('timetable') { <app-child-timetable-tab [studentId]="c.studentId" /> }
        @case ('exams') { <app-child-exams-tab [studentId]="c.studentId" /> }
        @case ('reports') { <app-child-reports-tab [studentId]="c.studentId" /> }
        @case ('fees') { <app-child-fees-tab [studentId]="c.studentId" /> }
        @case ('leave') { <app-child-leave-tab [studentId]="c.studentId" /> }
      }
    } @else {
      <p class="alert" role="alert" data-state="not-found">This child could not be found on your account.</p>
    }
  `,
})
export class ChildDashboardComponent implements OnInit {
  private readonly api = inject(FamilyService);
  private readonly router = inject(Router);

  /** Bound from the :studentId route param and the ?tab= query (withComponentInputBinding). */
  readonly studentId = input.required<string>();
  readonly tabParam = input<string | undefined>(undefined, { alias: 'tab' });

  protected readonly tabs = CHILD_TABS;
  protected readonly siblings = signal<FamilyChildCard[]>([]);
  protected readonly loading = signal(true);
  protected readonly failed = signal(false);
  protected readonly initials = initials;
  protected readonly chipLabel = chipLabel;
  protected readonly child = computed(() => this.siblings().find((s) => s.studentId === this.studentId()) ?? null);
  protected readonly banner = computed(() => readOnlyBanner(this.child()?.chipState));
  protected readonly tab = computed<ChildTab>(() => (CHILD_TABS.find((t) => t.key === this.tabParam())?.key ?? 'profile'));

  ngOnInit(): void { this.load(); }

  /** The switcher and the card come from the one children list: the server decides which children are this parent's. */
  protected load(): void {
    this.loading.set(true); this.failed.set(false);
    this.api.children().pipe(finalize(() => this.loading.set(false))).subscribe({ next: (d) => this.siblings.set(d.children), error: () => this.failed.set(true) });
  }

  protected open(key: ChildTab): void { void this.router.navigate([], { queryParams: { tab: key }, queryParamsHandling: 'merge', replaceUrl: true }); }
  protected switchTo(id: string): void { void this.router.navigate(['/family/children', id], { queryParams: { tab: this.tab() } }); }
}
