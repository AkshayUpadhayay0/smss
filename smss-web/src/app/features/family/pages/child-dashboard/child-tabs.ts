import { CurrencyPipe, DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, OnInit, inject, input, signal } from '@angular/core';
import { Observable, finalize } from 'rxjs';
import { ToastService } from '../../../../core/services/toast.service';
import { TimetableGridComponent } from '../../../timetable/components/timetable-grid/timetable-grid.component';
import { TimetableGrid } from '../../../timetable/models/timetable.model';
import { MyReportCard } from '../../../examinations/models/results.model';
import { StudentLeave } from '../../../leave/models/leave.model';
import { FeeReceiptDialogComponent } from '../../../students/components/fee-receipt-dialog/fee-receipt-dialog.component';
import { FeeReceipt } from '../../../students/models/fee-payment.model';
import { StudentMyFees } from '../../../students/models/student.model';
import { FamilyAttendance, FamilyChildProfile, FamilyExamSchedule, FamilyHomework } from '../../models/family.model';
import { FamilyService } from '../../services/family.service';

/** Loading / failed / data for one tab. Each tab asks the server only when it is opened. */
class Loadable<T> {
  readonly data = signal<T | null>(null);
  readonly loading = signal(true);
  readonly failed = signal(false);
  load(source: Observable<T>): void {
    this.loading.set(true); this.failed.set(false);
    source.pipe(finalize(() => this.loading.set(false))).subscribe({ next: (d) => this.data.set(d), error: () => this.failed.set(true) });
  }
}

const STATE = `
  @if (state.loading()) { <p role="status" data-state="loading">Loading…</p> }
  @else if (state.failed()) { <p class="alert" role="alert" data-state="failed">This could not be loaded. <button type="button" (click)="reload()" data-action="retry">Try again</button></p> }
`;
const STYLES = [`
  :host { display: block; }
  .alert { padding: var(--space-3) var(--space-4); border: 1px solid var(--color-danger); border-radius: var(--radius-md); background: var(--color-danger-soft); color: var(--color-danger); }
  .alert button { margin-left: var(--space-2); text-decoration: underline; background: none; border: 0; color: inherit; cursor: pointer; }
  .empty { color: var(--color-text-muted); padding: var(--space-4); text-align: center; }
  dl { display: grid; grid-template-columns: max-content 1fr; gap: var(--space-2) var(--space-4); margin: 0; }
  dt { color: var(--color-text-muted); } dd { margin: 0; }
  ul.list { list-style: none; margin: 0; padding: 0; display: flex; flex-direction: column; gap: var(--space-2); }
  ul.list li { padding: var(--space-3); border: 1px solid var(--color-border); border-radius: var(--radius-md); background: var(--color-surface, #fff); }
  .meta { color: var(--color-text-muted); font-size: var(--text-sm); }
  table { width: 100%; border-collapse: collapse; font-size: var(--text-sm); } th, td { text-align: left; padding: var(--space-2); border-bottom: 1px solid var(--color-border); } td.num, th.num { text-align: right; }
  .scroll { overflow-x: auto; }
  button.link { background: none; border: 0; color: var(--color-primary-700, var(--color-primary)); text-decoration: underline; cursor: pointer; padding: 0; }
  h3 { margin: var(--space-4) 0 var(--space-2); font-size: var(--text-base); }
`];

@Component({
  selector: 'app-child-profile-tab', imports: [DatePipe], changeDetection: ChangeDetectionStrategy.OnPush, styles: STYLES,
  template: `${STATE} @if (state.data(); as p) {
    <dl data-tab="profile">
      <dt>Name</dt><dd>{{ p.fullName }}</dd><dt>Student ID</dt><dd>{{ p.studentId }}</dd><dt>Admission number</dt><dd>{{ p.admissionNumber }}</dd>
      <dt>Class</dt><dd>{{ p.className ? p.className + (p.sectionName ? ' – ' + p.sectionName : '') : '—' }}</dd><dt>Roll number</dt><dd>{{ p.rollNumber || '—' }}</dd>
      <dt>Academic year</dt><dd>{{ p.academicYearName || '—' }}</dd><dt>Date of birth</dt><dd>{{ p.dateOfBirth | date: 'd MMM yyyy' }}</dd>
      <dt>Gender</dt><dd>{{ p.genderName || '—' }}</dd><dt>Blood group</dt><dd>{{ p.bloodGroupName || '—' }}</dd><dt>Address</dt><dd>{{ p.address || '—' }}</dd>
    </dl> } `,
})
export class ChildProfileTabComponent implements OnInit {
  private readonly api = inject(FamilyService);
  readonly studentId = input.required<string>();
  protected readonly state = new Loadable<FamilyChildProfile>();
  ngOnInit(): void { this.reload(); }
  protected reload(): void { this.state.load(this.api.profile(this.studentId())); }
}

@Component({
  selector: 'app-child-attendance-tab', imports: [DatePipe], changeDetection: ChangeDetectionStrategy.OnPush, styles: STYLES,
  template: `${STATE} @if (state.data(); as a) {
    <div data-tab="attendance">
      <p data-field="summary"><strong>Last 30 days:</strong> {{ a.summary.byStatus['PRESENT'] ?? 0 }} present, {{ a.summary.byStatus['ABSENT'] ?? 0 }} absent
        @if (a.summary.presentPercentage != null) { ({{ a.summary.presentPercentage }}% present) }</p>
      @if (a.days.length === 0) { <p class="empty" data-state="empty">No attendance has been marked in this period.</p> }
      @else { <ul class="list">@for (d of a.days; track d.date) { <li>{{ d.date | date: 'EEE d MMM' }}: <strong>{{ d.statusName }}</strong></li> }</ul> }
    </div> } `,
})
export class ChildAttendanceTabComponent implements OnInit {
  private readonly api = inject(FamilyService);
  readonly studentId = input.required<string>();
  protected readonly state = new Loadable<FamilyAttendance>();
  ngOnInit(): void { this.reload(); }
  protected reload(): void {
    const to = new Date(); const from = new Date(); from.setDate(to.getDate() - 29);
    const iso = (d: Date) => `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
    this.state.load(this.api.attendance(this.studentId(), iso(from), iso(to)));
  }
}

@Component({
  selector: 'app-child-homework-tab', imports: [DatePipe], changeDetection: ChangeDetectionStrategy.OnPush, styles: STYLES,
  template: `${STATE} @if (state.data(); as list) {
    <div data-tab="homework">
      @if (list.length === 0) { <p class="empty" data-state="empty">No homework has been given.</p> }
      @else { <ul class="list">@for (h of list; track h.homeworkId) { <li><strong>{{ h.subjectName }}: {{ h.title }}</strong><div class="meta">Due {{ h.dueDate | date: 'EEE d MMM' }} · {{ h.teacherName }}</div>@if (h.description) { <p>{{ h.description }}</p> }</li> }</ul> }
    </div> } `,
})
export class ChildHomeworkTabComponent implements OnInit {
  private readonly api = inject(FamilyService);
  readonly studentId = input.required<string>();
  protected readonly state = new Loadable<FamilyHomework[]>();
  ngOnInit(): void { this.reload(); }
  protected reload(): void { this.state.load(this.api.homework(this.studentId())); }
}

@Component({
  selector: 'app-child-timetable-tab', imports: [TimetableGridComponent], changeDetection: ChangeDetectionStrategy.OnPush, styles: STYLES,
  template: `${STATE} @if (state.data(); as g) { <div class="scroll" data-tab="timetable">@if (g.entryCount === 0) { <p class="empty" data-state="empty">No timetable has been published for this class yet.</p> } @else { <app-timetable-grid [grid]="g" /> }</div> } `,
})
export class ChildTimetableTabComponent implements OnInit {
  private readonly api = inject(FamilyService);
  readonly studentId = input.required<string>();
  protected readonly state = new Loadable<TimetableGrid>();
  ngOnInit(): void { this.reload(); }
  protected reload(): void { this.state.load(this.api.timetable(this.studentId())); }
}

@Component({
  selector: 'app-child-exams-tab', imports: [DatePipe], changeDetection: ChangeDetectionStrategy.OnPush, styles: STYLES,
  template: `${STATE} @if (state.data(); as list) {
    <div data-tab="exams">
      @if (list.length === 0) { <p class="empty" data-state="empty">No exam dates have been announced for this class.</p> }
      @for (s of list; track s.schemeName) { <h3>{{ s.schemeName }} <span class="meta">{{ s.academicYearName }}</span></h3>
        <ul class="list">@for (e of s.exams; track e.examName + e.termName) { <li>{{ e.termName }} · <strong>{{ e.examName }}</strong>: {{ e.examDate ? (e.examDate | date: 'EEE d MMM yyyy') : 'date to be announced' }}</li> }</ul> }
    </div> } `,
})
export class ChildExamsTabComponent implements OnInit {
  private readonly api = inject(FamilyService);
  readonly studentId = input.required<string>();
  protected readonly state = new Loadable<FamilyExamSchedule[]>();
  ngOnInit(): void { this.reload(); }
  protected reload(): void { this.state.load(this.api.examSchedule(this.studentId())); }
}

@Component({
  selector: 'app-child-reports-tab', imports: [DatePipe], changeDetection: ChangeDetectionStrategy.OnPush, styles: STYLES,
  template: `${STATE} @if (state.data(); as list) {
    <div data-tab="reports">
      @if (list.length === 0) { <p class="empty" data-state="empty">No report card has been published yet.</p> }
      @else { <ul class="list">@for (c of list; track c.publicationId) { <li [attr.data-report]="c.publicationId"><strong>{{ c.schemeName }}</strong> · {{ c.scope === 'TERM' ? (c.termName || 'Term') : 'Whole year' }}
        <div class="meta">Published {{ c.publishedAt | date: 'd MMM yyyy' }}@if (c.percent != null) { · {{ c.percent }}% }@if (c.grade) { · Grade {{ c.grade }} }</div>
        <button type="button" class="link" (click)="pdf(c)" [disabled]="busy()" data-action="pdf">Download PDF</button></li> }</ul> }
    </div> } `,
})
export class ChildReportsTabComponent implements OnInit {
  private readonly api = inject(FamilyService);
  private readonly toast = inject(ToastService);
  readonly studentId = input.required<string>();
  protected readonly state = new Loadable<MyReportCard[]>();
  protected readonly busy = signal(false);
  ngOnInit(): void { this.reload(); }
  protected reload(): void { this.state.load(this.api.reportCards(this.studentId())); }
  protected pdf(c: MyReportCard): void {
    if (this.busy()) return;
    this.busy.set(true);
    this.api.reportCardPdf(this.studentId(), c.publicationId).pipe(finalize(() => this.busy.set(false))).subscribe({
      next: (blob) => {
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a'); a.href = url; a.download = `report-card-${c.publicationId}.pdf`; a.click();
        setTimeout(() => URL.revokeObjectURL(url), 10_000);
      },
      error: () => this.toast.warning('The report card could not be downloaded. Please try again.'),
    });
  }
}

@Component({
  selector: 'app-child-fees-tab', imports: [CurrencyPipe, DatePipe, FeeReceiptDialogComponent], changeDetection: ChangeDetectionStrategy.OnPush, styles: STYLES,
  template: `${STATE} @if (state.data(); as f) {
    <div data-tab="fees">
      @if (!f.hasFees) { <p class="empty" data-state="empty">No fees have been set for this child yet.</p> }
      @else {
        <p data-field="totals"><strong>{{ f.overallStatus }}</strong> · Total {{ f.totalDue | currency: 'INR' : 'symbol' : '1.0-2' }} · Paid {{ f.totalPaid | currency: 'INR' : 'symbol' : '1.0-2' }} · Balance {{ f.balance | currency: 'INR' : 'symbol' : '1.0-2' }}</p>
        <h3>Instalments</h3>
        <div class="scroll"><table data-table="dues"><thead><tr><th>Fee</th><th>Due</th><th class="num">Amount</th><th class="num">Paid</th><th>Status</th></tr></thead><tbody>
          @for (d of f.dues; track d.dueDate + d.feeTypeName) { <tr><td>{{ d.feeTypeName }}</td><td>{{ d.dueDate | date: 'd MMM yyyy' }}</td><td class="num">{{ d.amountDue | currency: 'INR' : 'symbol' : '1.0-2' }}</td><td class="num">{{ d.amountPaid | currency: 'INR' : 'symbol' : '1.0-2' }}</td><td>{{ d.status }}</td></tr> }
        </tbody></table></div>
        <h3>Payments and receipts</h3>
        @if (f.payments.length === 0) { <p class="empty">No payment has been made yet.</p> }
        @else { <ul class="list" data-list="payments">@for (p of f.payments; track p.receiptNumber) { <li>{{ p.paymentDate | date: 'd MMM yyyy' }} · {{ p.amount | currency: 'INR' : 'symbol' : '1.0-2' }} · {{ p.paymentMode }}
          @if (p.lateFee; as lf) { <div class="meta" data-field="late-fee">Includes late fee {{ lf.amount | currency: 'INR' : 'symbol' : '1.0-2' }}@if (lf.ruleText) { ({{ lf.ruleText }}) }</div> }
          @if (p.paymentId) { <button type="button" class="link" (click)="open(p.paymentId)" data-action="receipt">Receipt {{ p.receiptNumber }}</button> } @else { <span class="meta">Receipt {{ p.receiptNumber }}</span> }</li> }</ul> }
      }
      @if (receipt(); as r) { <app-fee-receipt-dialog [receipt]="r" (closed)="receipt.set(null)" /> }
    </div> } `,
})
export class ChildFeesTabComponent implements OnInit {
  private readonly api = inject(FamilyService);
  private readonly toast = inject(ToastService);
  readonly studentId = input.required<string>();
  protected readonly state = new Loadable<StudentMyFees>();
  protected readonly receipt = signal<FeeReceipt | null>(null);
  ngOnInit(): void { this.reload(); }
  protected reload(): void { this.state.load(this.api.fees(this.studentId())); }
  protected open(paymentId: number): void {
    this.api.receipt(this.studentId(), paymentId).subscribe({ next: (r) => this.receipt.set(r), error: () => this.toast.warning('The receipt could not be opened. Please try again.') });
  }
}

@Component({
  selector: 'app-child-leave-tab', imports: [DatePipe], changeDetection: ChangeDetectionStrategy.OnPush, styles: STYLES,
  template: `${STATE} @if (state.data(); as list) {
    <div data-tab="leave">
      @if (list.length === 0) { <p class="empty" data-state="empty">No leave has been requested for this child.</p> }
      @else { <ul class="list">@for (l of list; track l.leaveRequestId) { <li><strong>{{ l.leaveTypeName }}</strong> · {{ l.fromDate | date: 'd MMM' }} to {{ l.toDate | date: 'd MMM yyyy' }} ({{ l.days }} {{ l.days === 1 ? 'day' : 'days' }})
        <div class="meta">{{ l.status }}@if (l.reason) { · {{ l.reason }} }</div></li> }</ul> }
    </div> } `,
})
export class ChildLeaveTabComponent implements OnInit {
  private readonly api = inject(FamilyService);
  readonly studentId = input.required<string>();
  protected readonly state = new Loadable<StudentLeave[]>();
  ngOnInit(): void { this.reload(); }
  protected reload(): void { this.state.load(this.api.leave(this.studentId())); }
}
