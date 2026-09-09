import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { PageHeaderComponent } from '../../shared/components/page-header/page-header';
import { IconComponent } from '../../shared/components/icon/icon';
import { StatusBadgeComponent } from '../../shared/components/status-badge/status-badge';
import { TabsComponent, TabItem } from '../../shared/components/tabs/tabs';
import { ModalComponent } from '../../shared/components/modal/modal';
import { LibraryService } from '../../core/services/data.service';
import { Book, LibraryIssue } from '../../core/models/school.models';

@Component({
  selector: 'app-library-dashboard',
  standalone: true,
  imports: [CommonModule, FormsModule, PageHeaderComponent, IconComponent, StatusBadgeComponent, TabsComponent, ModalComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './library-dashboard.html',
})
export class LibraryDashboardComponent {
  private libraryService = inject(LibraryService);
  books = signal<Book[]>([]);
  issues = signal<LibraryIssue[]>([]);
  activeTab = signal('books');
  query = signal('');
  modalOpen = signal(false);

  tabs: TabItem[] = [
    { id: 'books', label: 'Books', icon: 'book' },
    { id: 'issued', label: 'Issued / Overdue', icon: 'clock' },
    { id: 'members', label: 'Members', icon: 'users' },
  ];

  constructor() {
    this.libraryService.getBooks().subscribe(list => this.books.set(list));
    this.libraryService.getIssues().subscribe(list => this.issues.set(list));
  }

  stats = computed(() => {
    const books = this.books();
    const totalBooks = books.reduce((a, b) => a + b.quantity, 0);
    const available = books.reduce((a, b) => a + b.available, 0);
    const issued = totalBooks - available;
    const overdue = this.issues().filter(i => i.status === 'Overdue').length;
    return { totalBooks, available, issued, overdue };
  });

  filteredBooks = computed(() => {
    const q = this.query().trim().toLowerCase();
    if (!q) return this.books();
    return this.books().filter(b => b.title.toLowerCase().includes(q) || b.author.toLowerCase().includes(q));
  });
}
