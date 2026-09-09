import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { PageHeaderComponent } from '../../shared/components/page-header/page-header';
import { IconComponent } from '../../shared/components/icon/icon';
import { StatusBadgeComponent } from '../../shared/components/status-badge/status-badge';
import { ProgressBarComponent } from '../../shared/components/progress-bar/progress-bar';
import { TabsComponent, TabItem } from '../../shared/components/tabs/tabs';
import { PaginatorComponent } from '../../shared/components/paginator/paginator';
import { ModalComponent } from '../../shared/components/modal/modal';
import { FeeService } from '../../core/services/data.service';
import { StudentFee, Payment, FeeStructureItem } from '../../core/models/school.models';

@Component({
  selector: 'app-fees-dashboard',
  standalone: true,
  imports: [CommonModule, FormsModule, PageHeaderComponent, IconComponent, StatusBadgeComponent, ProgressBarComponent, TabsComponent, PaginatorComponent, ModalComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './fees-dashboard.html',
})
export class FeesDashboardComponent {
  private feeService = inject(FeeService);
  fees = signal<StudentFee[]>([]);
  payments = signal<Payment[]>([]);
  structure = signal<FeeStructureItem[]>([]);
  activeTab = signal('students');
  query = signal('');
  statusFilter = signal('all');
  page = signal(1);
  pageSize = 10;
  modalOpen = signal(false);

  tabs: TabItem[] = [
    { id: 'students', label: 'Student Fees', icon: 'users' },
    { id: 'payments', label: 'Payments / Receipts', icon: 'file-text' },
    { id: 'structure', label: 'Fee Structure', icon: 'sliders' },
  ];

  constructor() {
    this.feeService.getStudentFees().subscribe(list => this.fees.set(list));
    this.feeService.getPayments().subscribe(list => this.payments.set(list));
    this.feeService.getStructure().subscribe(list => this.structure.set(list as FeeStructureItem[]));
  }

  totals = computed(() => {
    const list = this.fees();
    const total = list.reduce((a, f) => a + f.totalFee, 0);
    const collected = list.reduce((a, f) => a + f.paid, 0);
    const pending = total - collected;
    const overdue = list.filter(f => f.status === 'Overdue').reduce((a, f) => a + f.pending, 0);
    return { total, collected, pending, overdue, pct: total ? Math.round((collected / total) * 100) : 0 };
  });

  filteredFees = computed(() => {
    let list = this.fees();
    const q = this.query().trim().toLowerCase();
    if (q) list = list.filter(f => f.studentName.toLowerCase().includes(q));
    if (this.statusFilter() !== 'all') list = list.filter(f => f.status === this.statusFilter());
    return list;
  });

  pagedFees = computed(() => {
    const start = (this.page() - 1) * this.pageSize;
    return this.filteredFees().slice(start, start + this.pageSize);
  });
}
