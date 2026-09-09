import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { PageHeaderComponent } from '../../shared/components/page-header/page-header';
import { IconComponent } from '../../shared/components/icon/icon';
import { AvatarComponent } from '../../shared/components/avatar/avatar';
import { StatusBadgeComponent } from '../../shared/components/status-badge/status-badge';
import { TabsComponent, TabItem } from '../../shared/components/tabs/tabs';
import { PaginatorComponent } from '../../shared/components/paginator/paginator';
import { ModalComponent } from '../../shared/components/modal/modal';
import { AdmissionService } from '../../core/services/data.service';
import { Admission, AdmissionStatus } from '../../core/models/school.models';

@Component({
  selector: 'app-admission-list',
  standalone: true,
  imports: [CommonModule, FormsModule, PageHeaderComponent, IconComponent, AvatarComponent, StatusBadgeComponent, TabsComponent, PaginatorComponent, ModalComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './admission-list.html',
})
export class AdmissionListComponent {
  private admissionService = inject(AdmissionService);
  admissions = signal<Admission[]>([]);
  activeTab = signal('all');
  query = signal('');
  page = signal(1);
  pageSize = 10;
  modalOpen = signal(false);

  tabs: TabItem[] = [
    { id: 'all', label: 'All Applications', icon: 'list' },
    { id: 'Pending', label: 'Pending', icon: 'clock' },
    { id: 'Under Review', label: 'Under Review', icon: 'eye' },
    { id: 'Approved', label: 'Approved', icon: 'check-circle' },
    { id: 'Rejected', label: 'Rejected', icon: 'x-circle' },
    { id: 'Waiting List', label: 'Waiting List', icon: 'clipboard-list' },
  ];

  constructor() {
    this.admissionService.getAll().subscribe(list => this.admissions.set(list));
  }

  counts = computed(() => {
    const list = this.admissions();
    const byStatus = (s: AdmissionStatus) => list.filter(a => a.status === s).length;
    return {
      total: list.length,
      pending: byStatus('Pending'),
      approved: byStatus('Approved'),
      rejected: byStatus('Rejected'),
    };
  });

  filtered = computed(() => {
    let list = this.admissions();
    if (this.activeTab() !== 'all') list = list.filter(a => a.status === this.activeTab());
    const q = this.query().trim().toLowerCase();
    if (q) list = list.filter(a => a.studentName.toLowerCase().includes(q) || a.applicationId.toLowerCase().includes(q));
    return list;
  });

  paged = computed(() => {
    const start = (this.page() - 1) * this.pageSize;
    return this.filtered().slice(start, start + this.pageSize);
  });

  updateStatus(id: string, status: AdmissionStatus): void {
    this.admissions.update(list => list.map(a => a.id === id ? { ...a, status } : a));
  }
}
