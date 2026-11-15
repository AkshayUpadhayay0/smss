import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { finalize } from 'rxjs';
import { ROLES } from '../../../../core/config/route-roles';
import { AuthService } from '../../../../core/services/auth.service';
import { ConfirmDialogService } from '../../../../core/services/confirm-dialog.service';
import { ToastService } from '../../../../core/services/toast.service';
import { ButtonComponent, CardComponent, IconComponent, SelectComponent, SelectOption } from '../../../../shared/components';
import { StudentTransport, TRANSPORT_MANAGER_ROLES } from '../../../transport/models/transport.model';
import { TransportService } from '../../../transport/services/transport.service';

const VIEWER_ROLES: readonly string[] = [ROLES.SCHOOL_ADMIN, ROLES.PRINCIPAL];

/**
 * The student's school-bus area for the current academic year. The School Admin picks an Active area, changes it or removes it; the Principal sees it read-only;
 * every other role sees nothing (the API refuses them too). The student's own "Where is my bus" screen shows the buses on duty for THIS area.
 */
@Component({
  selector: 'app-student-transport-card',
  imports: [ReactiveFormsModule, ButtonComponent, CardComponent, IconComponent, SelectComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './student-transport-card.component.scss',
  template: `
    @if (visible()) {
      <app-card title="Transport">
        @if (loading()) {
          <p class="muted">Loading…</p>
        } @else if (loadFailed()) {
          <p class="muted">Could not load the transport details.</p>
          <app-button variant="secondary" size="sm" (click)="load()">Try again</app-button>
        } @else if (data(); as d) {
          <dl class="grid">
            <div><dt>Academic year</dt><dd>{{ d.academicYearName }}</dd></div>
            <div>
              <dt>School bus area</dt>
              <dd>
                @if (d.assignment; as a) {
                  {{ a.areaName }}
                  @if (!a.areaIsActive) {
                    <span class="badge-warn">Area inactive</span>
                  }
                } @else {
                  Not using school transport
                }
              </dd>
            </div>
          </dl>
          @if (d.assignment && !d.assignment.areaIsActive) {
            <p class="warn" role="alert">This area is inactive, so no bus is shown to the student. Choose another area.</p>
          }

          @if (canManage()) {
            <div class="edit">
              <app-select
                label="{{ d.assignment ? 'Change area' : 'Assign an area' }}"
                placeholder="Choose an active area"
                [options]="areaOptions()"
                [formControl]="areaControl"
              />
              <div class="actions">
                <app-button [disabled]="busy() || !areaControl.value || areaControl.value === currentAreaValue()" (click)="save()">
                  <app-icon name="check" [size]="16" />{{ busy() ? 'Saving…' : d.assignment ? 'Change area' : 'Assign area' }}
                </app-button>
                @if (d.assignment) {
                  <app-button variant="danger-ghost" [disabled]="busy()" (click)="remove()"><app-icon name="trash-2" [size]="16" />Remove</app-button>
                }
              </div>
            </div>
          } @else {
            <p class="muted">Only a School Admin can change this.</p>
          }
        }
      </app-card>
    }
  `,
})
export class StudentTransportCardComponent implements OnInit {
  private readonly transport = inject(TransportService);
  private readonly auth = inject(AuthService);
  private readonly toast = inject(ToastService);
  private readonly confirmDialog = inject(ConfirmDialogService);
  private readonly destroyRef = inject(DestroyRef);

  readonly studentId = input.required<string>();

  protected readonly loading = signal(true);
  protected readonly loadFailed = signal(false);
  protected readonly busy = signal(false);
  protected readonly data = signal<StudentTransport | null>(null);
  protected readonly areas = signal<{ id: number; name: string }[]>([]);
  protected readonly areaControl = new FormControl('', { nonNullable: true });

  private readonly roles = computed(() => this.auth.currentUserRoles().map((r) => r.trim().toLowerCase()));
  /** Only the roles the API lets read it; for anyone else (an Accountant) the card is not rendered and nothing is requested. */
  protected readonly visible = computed(() => this.roles().some((r) => VIEWER_ROLES.some((v) => v.toLowerCase() === r)));
  protected readonly canManage = computed(() => this.roles().some((r) => TRANSPORT_MANAGER_ROLES.some((v) => v.toLowerCase() === r)));
  protected readonly areaOptions = computed<SelectOption[]>(() => this.areas().map((a) => ({ label: a.name, value: String(a.id) })));
  protected readonly currentAreaValue = computed(() => (this.data()?.assignment ? String(this.data()!.assignment!.areaId) : ''));

  ngOnInit(): void {
    if (this.visible()) this.load();
    else this.loading.set(false);
  }

  protected load(): void {
    this.loading.set(true);
    this.loadFailed.set(false);
    this.transport
      .getStudentTransport(this.studentId())
      .pipe(finalize(() => this.loading.set(false)), takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (d) => {
          this.data.set(d ?? null);
          this.areaControl.setValue(d?.assignment?.areaIsActive ? String(d.assignment.areaId) : '');
          if (this.canManage()) this.loadAreas();
        },
        error: () => this.loadFailed.set(true), // already toasted by the interceptor
      });
  }

  /** Active areas only (the server refuses an inactive one too). */
  private loadAreas(): void {
    this.transport
      .listAreas({ status: 'Active', page: 1, pageSize: 100 })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({ next: (p) => this.areas.set((p?.items ?? []).map((a) => ({ id: a.areaId, name: a.areaName }))), error: () => undefined });
  }

  protected save(): void {
    const areaId = Number(this.areaControl.value);
    if (!areaId || this.busy()) return;
    this.busy.set(true);
    this.transport
      .assignStudentTransport(this.studentId(), areaId)
      .pipe(finalize(() => this.busy.set(false)), takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (d) => {
          this.data.set(d);
          this.toast.success('Transport area saved.');
        },
        error: () => undefined, // the server's reason (inactive area, no current academic year) is toasted by the interceptor
      });
  }

  protected async remove(): Promise<void> {
    const d = this.data();
    if (!d?.assignment || this.busy()) return;
    const confirmed = await this.confirmDialog.confirm({
      title: 'Remove transport?',
      message: `${d.studentName} will no longer be shown a school bus. You can assign an area again later.`,
      confirmText: 'Remove',
      variant: 'danger',
    });
    if (!confirmed) return;
    this.busy.set(true);
    this.transport
      .removeStudentTransport(this.studentId())
      .pipe(finalize(() => this.busy.set(false)), takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (r) => {
          this.data.set(r);
          this.areaControl.setValue('');
          this.toast.success('Transport removed.');
        },
        error: () => undefined,
      });
  }
}
