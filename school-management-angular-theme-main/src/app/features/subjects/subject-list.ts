import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { PageHeaderComponent } from '../../shared/components/page-header/page-header';
import { IconComponent } from '../../shared/components/icon/icon';
import { StatusBadgeComponent } from '../../shared/components/status-badge/status-badge';
import { SubjectService } from '../../core/services/data.service';
import { Subject } from '../../core/models/school.models';

@Component({
  selector: 'app-subject-list',
  standalone: true,
  imports: [CommonModule, PageHeaderComponent, IconComponent, StatusBadgeComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './subject-list.html',
})
export class SubjectListComponent {
  private subjectService = inject(SubjectService);
  subjects = signal<Subject[]>([]);

  constructor() {
    this.subjectService.getAll().subscribe(list => this.subjects.set(list));
  }
}
