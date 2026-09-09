import { ChangeDetectionStrategy, Component, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { PageHeaderComponent } from '../../shared/components/page-header/page-header';
import { IconComponent } from '../../shared/components/icon/icon';
import { AvatarComponent } from '../../shared/components/avatar/avatar';

interface Thread { id: string; name: string; role: string; last: string; time: string; unread: number; }
interface Msg { id: string; from: 'me' | 'them'; text: string; time: string; }

@Component({
  selector: 'app-messages',
  standalone: true,
  imports: [CommonModule, FormsModule, PageHeaderComponent, IconComponent, AvatarComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './messages.html',
})
export class MessagesComponent {
  threads: Thread[] = [
    { id: 't1', name: 'Anjali Mehta', role: 'Class Teacher, 9-A', last: 'Sure, I will share the report card by tomorrow.', time: '10:32 AM', unread: 2 },
    { id: 't2', name: 'Rohan Verma', role: 'Parent of Kabir Singh', last: 'Thank you for the update on the fee payment.', time: '9:15 AM', unread: 0 },
    { id: 't3', name: 'Accounts Office', role: 'Administration', last: 'Quarter 2 fee reminders have been sent.', time: 'Yesterday', unread: 0 },
    { id: 't4', name: 'Priya Nair', role: 'Sports Coach', last: 'Practice sessions start from Monday.', time: 'Yesterday', unread: 1 },
    { id: 't5', name: 'Library Desk', role: 'Library', last: 'Your requested book is now available.', time: '2 days ago', unread: 0 },
  ];

  activeThread = signal<Thread>(this.threads[0]);
  draft = signal('');

  messages = signal<Msg[]>([
    { id: 'm1', from: 'them', text: "Hi! Just checking in about Kabir's progress in Mathematics this term.", time: '10:20 AM' },
    { id: 'm2', from: 'me', text: "He's doing well overall — scored 82% in the last unit test.", time: '10:24 AM' },
    { id: 'm3', from: 'them', text: 'That’s great to hear. Could you share the detailed report card?', time: '10:30 AM' },
    { id: 'm4', from: 'them', text: 'Sure, I will share the report card by tomorrow.', time: '10:32 AM' },
  ]);

  selectThread(t: Thread): void {
    this.activeThread.set(t);
  }

  send(): void {
    const text = this.draft().trim();
    if (!text) return;
    this.messages.update(list => [...list, { id: 'm' + Date.now(), from: 'me', text, time: 'Just now' }]);
    this.draft.set('');
  }
}
