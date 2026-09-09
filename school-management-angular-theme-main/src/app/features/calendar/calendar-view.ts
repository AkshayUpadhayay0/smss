import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { PageHeaderComponent } from '../../shared/components/page-header/page-header';
import { IconComponent } from '../../shared/components/icon/icon';
import { ModalComponent } from '../../shared/components/modal/modal';
import { TabsComponent, TabItem } from '../../shared/components/tabs/tabs';
import { EventService } from '../../core/services/data.service';
import { SchoolEvent } from '../../core/models/school.models';

const TYPE_COLORS: Record<string, string> = {
  Holiday: 'var(--danger)', Exam: '#7c3aed', Meeting: 'var(--info)', Function: 'var(--warning)',
  Sports: 'var(--success)', Workshop: 'var(--primary)', Birthday: '#ec4899', Other: 'var(--text-muted)',
};

@Component({
  selector: 'app-calendar-view',
  standalone: true,
  imports: [CommonModule, PageHeaderComponent, IconComponent, ModalComponent, TabsComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './calendar-view.html',
})
export class CalendarViewComponent {
  private eventService = inject(EventService);
  events = signal<SchoolEvent[]>([]);
  view = signal<'month' | 'list'>('month');
  modalOpen = signal(false);
  selectedEvent = signal<SchoolEvent | null>(null);

  tabs: TabItem[] = [
    { id: 'month', label: 'Monthly View', icon: 'calendar' },
    { id: 'list', label: 'Event List', icon: 'list' },
  ];

  today = new Date();
  monthLabel = this.today.toLocaleString('en-US', { month: 'long', year: 'numeric' });

  constructor() {
    this.eventService.getAll().subscribe(list => this.events.set(list));
  }

  calendarDays = computed(() => {
    const year = this.today.getFullYear();
    const month = this.today.getMonth();
    const firstDay = new Date(year, month, 1);
    const startOffset = firstDay.getDay();
    const daysInMonth = new Date(year, month + 1, 0).getDate();
    const days: { date: number; iso: string; isToday: boolean }[] = [];
    for (let i = 0; i < startOffset; i++) days.push({ date: 0, iso: '', isToday: false });
    for (let d = 1; d <= daysInMonth; d++) {
      const iso = `${year}-${String(month + 1).padStart(2, '0')}-${String(d).padStart(2, '0')}`;
      days.push({ date: d, iso, isToday: d === this.today.getDate() });
    }
    return days;
  });

  eventsFor(iso: string): SchoolEvent[] {
    return this.events().filter(e => e.date === iso || (e.endDate && iso >= e.date && iso <= e.endDate));
  }

  colorFor(type: string): string {
    return TYPE_COLORS[type] ?? 'var(--text-muted)';
  }

  openEvent(e: SchoolEvent): void {
    this.selectedEvent.set(e);
    this.modalOpen.set(true);
  }

  setView(value: string): void {
    this.view.set(value === 'list' ? 'list' : 'month');
  }
}
