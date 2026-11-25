import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { SenderStatusModel } from '../../models/whatsapp-sender.model';
import { WhatsAppSenderService } from '../../services/whatsapp-sender.service';

/**
 * A read-only line for the School Admin and the Principal: this school's WhatsApp sender status and whose number the messages go from. Only the Super Admin can change it. Nothing here sends a message.
 * When the server refuses or the status cannot be read, the line simply does not appear.
 */
@Component({
  selector: 'app-whatsapp-sender-status',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (status(); as s) {
      <p class="line" data-line="whatsapp-sender" [attr.data-status]="s.status">
        <strong>WhatsApp sender:</strong>
        <span class="chip" [class]="'chip ' + s.status.toLowerCase().replace('_', '-')">{{ s.statusLabel }}</span>
        <span class="meta">{{ s.sendsFrom }}.{{ s.whatsAppNumber ? ' School number: ' + s.whatsAppNumber + '.' : '' }} Set up by the platform team.</span>
      </p>
    }
  `,
  styles: `
    .line { display: flex; flex-wrap: wrap; gap: var(--space-2); align-items: center; margin: var(--space-3) 0; font-size: var(--text-sm); }
    .meta { color: var(--color-text-muted); }
    .chip { padding: 2px 10px; border-radius: 999px; font-size: var(--text-xs); font-weight: var(--weight-semibold); background: #f3f4f6; color: #374151; }
    .chip.pending { background: #fef3c7; color: #92400e; }
    .chip.active { background: #dcfce7; color: #166534; }
  `,
})
export class WhatsAppSenderStatusComponent implements OnInit {
  private readonly service = inject(WhatsAppSenderService);
  private readonly destroyRef = inject(DestroyRef);
  protected readonly status = signal<SenderStatusModel | null>(null);

  ngOnInit(): void {
    this.service
      .mine()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({ next: (s) => this.status.set(s ?? null), error: () => undefined });
  }
}
