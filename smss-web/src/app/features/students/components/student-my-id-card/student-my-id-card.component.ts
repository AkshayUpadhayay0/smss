import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { finalize } from 'rxjs';
import { ToastService } from '../../../../core/services/toast.service';
import { ButtonComponent, CardComponent, IconComponent } from '../../../../shared/components';
import { saveBlob } from '../../../homework/utils/homework-format';
import { MyIdCardPreview } from '../../models/student.model';
import { StudentService } from '../../services/student.service';

const UNAVAILABLE = 'Your ID card is not available right now. Please ask the school office.';

/**
 * A Student's own ID card: the front and back as the school's saved template draws them (GET /api/Student/me/id-card/preview), a Front / Back switch,
 * and a Download (PDF). The server answers a calm 404 when no card can be issued (left, inactive, not enrolled): shown here in place, never as an error toast.
 */
@Component({
  selector: 'app-student-my-id-card',
  imports: [ButtonComponent, CardComponent, IconComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './student-my-id-card.component.scss',
  template: `
    <app-card title="My ID Card">
      @if (loading()) {
        <p class="note" role="status">Loading your ID card…</p>
      } @else if (unavailable(); as why) {
        <p class="note" role="status">{{ why }}</p>
      } @else if (preview()) {
        <div class="switch" role="group" aria-label="Side of the card">
          <button type="button" [class.on]="side() === 'front'" [attr.aria-pressed]="side() === 'front'" (click)="side.set('front')">Front</button>
          <button type="button" [class.on]="side() === 'back'" [attr.aria-pressed]="side() === 'back'" (click)="side.set('back')">Back</button>
        </div>
        <div class="card-frame">
          <img [src]="image()" [alt]="side() === 'front' ? 'Front of your ID card' : 'Back of your ID card'" />
        </div>
        <div class="actions">
          <app-button variant="secondary" size="sm" [disabled]="downloading()" (click)="download()"><app-icon name="download" [size]="16" />{{ downloading() ? 'Preparing…' : 'Download ID card' }}</app-button>
        </div>
      }
    </app-card>
  `,
})
export class StudentMyIdCardComponent implements OnInit {
  private readonly students = inject(StudentService);
  private readonly toast = inject(ToastService);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly preview = signal<MyIdCardPreview | null>(null);
  protected readonly loading = signal(true);
  protected readonly unavailable = signal<string | null>(null);
  protected readonly side = signal<'front' | 'back'>('front');
  protected readonly downloading = signal(false);
  protected readonly image = computed(() => {
    const p = this.preview();
    return p ? `data:image/png;base64,${this.side() === 'front' ? p.frontPng : p.backPng}` : '';
  });

  ngOnInit(): void {
    this.students
      .myIdCardPreview()
      .pipe(finalize(() => this.loading.set(false)), takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (p) => this.preview.set(p),
        error: (e: { error?: { message?: unknown } }) => this.unavailable.set(typeof e?.error?.message === 'string' && e.error.message ? e.error.message : UNAVAILABLE),
      });
  }

  protected download(): void {
    if (this.downloading()) return;
    this.downloading.set(true);
    this.students
      .downloadMyIdCard()
      .pipe(finalize(() => this.downloading.set(false)), takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (blob) => saveBlob(blob, 'IDCard.pdf'),
        error: (e: Error) => this.toast.error(e.message),
      });
  }
}
