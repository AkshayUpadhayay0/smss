import { CurrencyPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { finalize } from 'rxjs';
import { SecureImageComponent } from '../../../../shared/components';
import { chipLabel, FamilyChildren, initials } from '../../models/family.model';
import { FamilyService } from '../../services/family.service';

/** The screen every parent lands on, even with one child: one card per linked child, the fee tile, and the School Notifications and Academic Calendar buttons. */
@Component({
  selector: 'app-family-home',
  imports: [CurrencyPipe, RouterLink, SecureImageComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './family-home.component.scss',
  template: `
    <h1>Your family</h1>
    @if (loading()) {
      <p role="status" data-state="loading">Loading your children…</p>
    } @else if (failed()) {
      <p class="alert" role="alert" data-state="failed">We could not load your children. <button type="button" (click)="load()" data-action="retry">Try again</button></p>
    } @else if (data(); as d) {
      @if (d.children.length === 0) {
        <p class="empty" data-state="empty">No children are linked to your account yet. If this is not right, please contact the school.</p>
      } @else {
        <section class="fees" data-section="fee-tile" [class.paid]="d.feeTile.allPaid" aria-label="Fees">
          @if (d.feeTile.allPaid) {
            <strong data-field="fee-paid">Fees: Paid</strong><span>Nothing is due right now for any of your children.</span>
          } @else {
            <strong data-field="fee-due">Fees due now: {{ d.feeTile.totalDueNow | currency: 'INR' : 'symbol' : '1.0-2' }}</strong>
            <ul>
              @for (c of d.feeTile.children; track c.studentId) {
                <li [attr.data-fee-child]="c.studentId"><a [routerLink]="['/family/children', c.studentId]" [queryParams]="{ tab: 'fees' }">{{ c.fullName }}</a>: {{ c.dueNow | currency: 'INR' : 'symbol' : '1.0-2' }}</li>
              }
            </ul>
          }
        </section>

        <div class="cards">
          @for (c of d.children; track c.studentId) {
            <a class="card" [routerLink]="['/family/children', c.studentId]" [attr.data-child]="c.studentId" [class.readonly]="c.readOnly">
              <div class="photo"><app-secure-img [src]="c.profilePhotoUrl" [alt]="c.fullName"><span class="initials">{{ initials(c.fullName) }}</span></app-secure-img></div>
              <div class="who">
                <strong>{{ c.fullName }}</strong>
                <span class="meta" data-field="student-id">{{ c.studentId }}</span>
                <span class="meta" data-field="class">{{ c.className ? c.className + (c.sectionName ? ' – ' + c.sectionName : '') : 'Not in a class' }}</span>
                <div class="chips">
                  @if (c.chipState; as chip) { <span class="chip warn" [attr.data-chip]="chip">{{ chipLabel(chip) }}</span> }
                  @else if (c.attendanceToday) { <span class="chip" [class.bad]="c.attendanceToday === 'Absent'" data-chip="attendance">Today: {{ c.attendanceToday }}</span> }
                  @else { <span class="chip muted" data-chip="attendance">Today: not marked yet</span> }
                  @if (c.feeDueNow > 0) { <span class="chip bad" data-chip="fee">Fee due {{ c.feeDueNow | currency: 'INR' : 'symbol' : '1.0-2' }}</span> }
                </div>
              </div>
            </a>
          }
        </div>
      }

      <div class="actions">
        <a class="action" routerLink="/family/notices" data-action="notices">School Notifications @if ((unread() ?? 0) > 0) { <span class="count" data-field="unread">{{ unread() }}</span> }</a>
        <a class="action" routerLink="/family/calendar" data-action="calendar">Academic Calendar</a>
      </div>
    }
  `,
})
export class FamilyHomeComponent implements OnInit {
  private readonly api = inject(FamilyService);

  protected readonly data = signal<FamilyChildren | null>(null);
  protected readonly loading = signal(true);
  protected readonly failed = signal(false);
  protected readonly unread = this.api.unread;
  protected readonly chipLabel = chipLabel;
  protected readonly initials = initials;

  ngOnInit(): void { this.load(); }

  protected load(): void {
    this.loading.set(true); this.failed.set(false);
    this.api.children().pipe(finalize(() => this.loading.set(false))).subscribe({ next: (d) => this.data.set(d), error: () => this.failed.set(true) });
  }
}
