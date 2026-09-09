import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { PageHeaderComponent } from '../../shared/components/page-header/page-header';
import { IconComponent } from '../../shared/components/icon/icon';
import { ModalComponent } from '../../shared/components/modal/modal';
import { ClassService } from '../../core/services/data.service';
import { SchoolClass } from '../../core/models/school.models';

@Component({
  selector: 'app-class-list',
  standalone: true,
  imports: [CommonModule, PageHeaderComponent, IconComponent, ModalComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './class-list.html',
})
export class ClassListComponent {
  private classService = inject(ClassService);
  classes = signal<SchoolClass[]>([]);
  modalOpen = signal(false);
  expandedClass = signal<string | null>(null);

  constructor() {
    this.classService.getAll().subscribe(list => this.classes.set(list));
  }

  toggle(id: string): void {
    this.expandedClass.set(this.expandedClass() === id ? null : id);
  }

  totalSections(): number {
    return this.classes().reduce((a, c) => a + c.sections.length, 0);
  }
}
