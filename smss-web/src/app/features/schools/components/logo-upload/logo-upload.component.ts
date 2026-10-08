import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject, input, output, signal } from '@angular/core';
import { ButtonComponent, IconComponent } from '../../../../shared/components';
import { LOGO_ACCEPT, validateLogoFile } from '../../utils/logo.util';

export interface LogoChange {
  /** A newly picked file waiting to be uploaded, or null. */
  file: File | null;
  /** True when the saved logo should be removed on submit. */
  remove: boolean;
}

/**
 * Picks / removes a logo but never talks to the API: the parent applies the change on submit
 * (the logo has its own endpoints, separate from the form).
 */
@Component({
  selector: 'app-logo-upload',
  imports: [ButtonComponent, IconComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './logo-upload.component.scss',
  template: `
    <div class="logo-upload">
      <div class="preview" [class.empty]="!shownUrl()">
        @if (shownUrl(); as url) {
          <img [src]="url" alt="School logo" />
        } @else {
          <app-icon name="building-2" [size]="32" />
        }
      </div>

      <div class="body">
        @if (!disabled()) {
          <div class="buttons">
            <app-button variant="secondary" size="sm" (click)="fileInput.click()">
              <app-icon name="upload" [size]="14" />{{ shownUrl() ? 'Change logo' : 'Upload logo' }}
            </app-button>
            @if (pendingFile()) {
              <app-button variant="danger-ghost" size="sm" (click)="clearPending()"><app-icon name="x" [size]="14" />Discard</app-button>
            } @else if (removed()) {
              <app-button variant="secondary" size="sm" (click)="undoRemove()">Undo remove</app-button>
            } @else if (currentUrl()) {
              <app-button variant="danger-ghost" size="sm" (click)="markRemove()"><app-icon name="trash-2" [size]="14" />Remove</app-button>
            }
          </div>
          <input #fileInput class="file" type="file" [accept]="accept" (change)="onPick($event)" />
        }

        @if (error(); as e) {
          <span class="error">{{ e }}</span>
        } @else if (pendingFile()) {
          <span class="hint">New logo will be uploaded when you save.</span>
        } @else if (removed()) {
          <span class="hint">Logo will be removed when you save.</span>
        } @else if (!disabled()) {
          <span class="hint">PNG, JPG or WEBP, up to 2 MB.</span>
        } @else if (!currentUrl()) {
          <span class="hint">No logo uploaded.</span>
        }
      </div>
    </div>
  `,
})
export class LogoUploadComponent {
  /** Absolute URL of the saved logo, if any. */
  readonly currentUrl = input<string | null>(null);
  readonly disabled = input(false);
  readonly changed = output<LogoChange>();

  protected readonly accept = LOGO_ACCEPT;
  protected readonly pendingFile = signal<File | null>(null);
  protected readonly removed = signal(false);
  protected readonly error = signal<string | null>(null);
  private readonly previewUrl = signal<string | null>(null);

  protected readonly shownUrl = computed(() => this.previewUrl() ?? (this.removed() ? null : this.currentUrl()));

  constructor() {
    inject(DestroyRef).onDestroy(() => this.revokePreview());
  }

  protected async onPick(event: Event): Promise<void> {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    input.value = ''; // allow re-picking the same file
    if (!file) return;

    const problem = await validateLogoFile(file);
    if (problem) {
      this.error.set(problem);
      return;
    }
    this.error.set(null);
    this.setPending(file);
    this.removed.set(false);
    this.emit();
  }

  protected clearPending(): void {
    this.setPending(null);
    this.error.set(null);
    this.emit();
  }

  protected markRemove(): void {
    this.removed.set(true);
    this.emit();
  }

  protected undoRemove(): void {
    this.removed.set(false);
    this.emit();
  }

  private setPending(file: File | null): void {
    this.revokePreview();
    this.pendingFile.set(file);
    this.previewUrl.set(file ? URL.createObjectURL(file) : null);
  }

  private revokePreview(): void {
    const url = this.previewUrl();
    if (url) URL.revokeObjectURL(url);
  }

  private emit(): void {
    this.changed.emit({ file: this.pendingFile(), remove: this.removed() });
  }
}
