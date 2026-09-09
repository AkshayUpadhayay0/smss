import { ChangeDetectionStrategy, Component, input } from '@angular/core';

const SUCCESS = new Set(['active', 'present', 'paid', 'approved', 'published', 'available', 'pass', 'graded', 'submitted', 'returned', 'completed']);
const WARNING = new Set(['pending', 'partial', 'late', 'under review', 'waiting list', 'draft', 'upcoming', 'on leave', 'maintenance']);
const DANGER = new Set(['inactive', 'absent', 'overdue', 'rejected', 'suspended', 'fail', 'expired', 'out of stock', 'emergency']);
const INFO = new Set(['leave', 'ongoing', 'issued', 'assigned']);

@Component({
  selector: 'app-status-badge',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<span class="badge" [class]="klass()">{{ status() }}</span>`,
})
export class StatusBadgeComponent {
  status = input.required<string>();

  klass(): string {
    const s = this.status().toLowerCase();
    if (SUCCESS.has(s)) return 'badge-success';
    if (WARNING.has(s)) return 'badge-warning';
    if (DANGER.has(s)) return 'badge-danger';
    if (INFO.has(s)) return 'badge-info';
    return 'badge-neutral';
  }
}
