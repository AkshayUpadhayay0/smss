import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { PageHeaderComponent } from '../../shared/components/page-header/page-header';
import { IconComponent } from '../../shared/components/icon/icon';
import { StatusBadgeComponent } from '../../shared/components/status-badge/status-badge';
import { SessionService } from '../../core/services/data.service';
import { AcademicSession } from '../../core/models/school.models';

@Component({
  selector: 'app-session-list',
  standalone: true,
  imports: [CommonModule, PageHeaderComponent, IconComponent, StatusBadgeComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './session-list.html',
})
export class SessionListComponent {
  private sessionService = inject(SessionService);
  sessions = signal<AcademicSession[]>([]);

  constructor() {
    this.sessionService.getAll().subscribe(list => this.sessions.set(list));
  }
}
