import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { PageHeaderComponent } from '../../shared/components/page-header/page-header';
import { IconComponent } from '../../shared/components/icon/icon';
import { StatusBadgeComponent } from '../../shared/components/status-badge/status-badge';
import { ModalComponent } from '../../shared/components/modal/modal';
import { NoticeService } from '../../core/services/data.service';
import { Notice } from '../../core/models/school.models';

@Component({
  selector: 'app-notice-list',
  standalone: true,
  imports: [CommonModule, FormsModule, PageHeaderComponent, IconComponent, StatusBadgeComponent, ModalComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './notice-list.html',
})
export class NoticeListComponent {
  private noticeService = inject(NoticeService);
  notices = signal<Notice[]>([]);
  query = signal('');
  modalOpen = signal(false);

  constructor() {
    this.noticeService.getAll().subscribe(list => this.notices.set(list));
  }

  filtered = computed(() => {
    const q = this.query().trim().toLowerCase();
    if (!q) return this.notices();
    return this.notices().filter(n => n.title.toLowerCase().includes(q));
  });
}
