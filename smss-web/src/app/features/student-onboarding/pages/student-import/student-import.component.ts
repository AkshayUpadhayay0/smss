import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { finalize } from 'rxjs';
import { ConfirmDialogService } from '../../../../core/services/confirm-dialog.service';
import { ToastService } from '../../../../core/services/toast.service';
import { ButtonComponent, CardComponent, IconComponent, PageHeaderComponent } from '../../../../shared/components';
import { saveBlob } from '../../../homework/utils/homework-format';
import { ImportFilter, ImportPreview, ImportResult, fileProblem, filterRows } from '../../models/student-import.model';
import { StudentImportService } from '../../services/student-import.service';

type Step = 'upload' | 'preview' | 'result';

/**
 * Import many existing students from an Excel file: download the template, upload (a dry run, nothing is saved),
 * read the preview, fix the file if needed, then confirm. One error anywhere stops the whole import, so a class is never half-imported.
 */
@Component({
  selector: 'app-student-import',
  imports: [ButtonComponent, CardComponent, IconComponent, PageHeaderComponent, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <app-page-header title="Import Students" subtitle="Add many existing students from an Excel file" icon="upload" [breadcrumbs]="breadcrumbs" />

    @switch (step()) {
      @case ('upload') {
        <app-card title="How it works">
          <ol class="how">
            <li><strong>Download the template.</strong> Its Class and Section drop-downs come from your school's own set-up.</li>
            <li><strong>Fill one student per row</strong> and save it as an Excel Workbook (.xlsx). Up to {{ maxRows }} students and 1 MB per file.</li>
            <li><strong>Upload it here.</strong> You see a preview first. Nothing is saved until you confirm.</li>
          </ol>
          <p class="note" data-test="import-type">Admission type: <strong>Existing Student</strong>. It is set automatically for every row and cannot be changed.</p>
          <div class="actions">
            <app-button variant="secondary" [disabled]="downloading()" (click)="downloadTemplate()"><app-icon name="download" [size]="16" />{{ downloading() ? 'Preparing…' : 'Download template' }}</app-button>
          </div>
        </app-card>

        <app-card title="Upload your file">
          <div class="picker">
            <label class="file" [class.has]="!!file()">
              <input type="file" accept=".xlsx" (change)="onPick($event)" aria-label="Choose the Excel file to import" />
              <app-icon name="upload" [size]="20" />
              <span>{{ file() ? file()!.name : 'Choose an .xlsx file' }}</span>
            </label>
            <app-button [disabled]="!file() || checking()" (click)="check()">{{ checking() ? 'Checking the file…' : 'Check file' }}</app-button>
          </div>
          @if (pickError()) { <p class="problem" role="alert">{{ pickError() }}</p> }
          @if (checking()) { <p class="note" role="status">Reading and checking every row. This takes a few seconds.</p> }
        </app-card>
      }

      @case ('preview') {
        @if (preview(); as p) {
          @if (p.fileErrors.length > 0) {
            <app-card title="The file cannot be imported yet">
              <ul class="file-errors" role="alert">
                @for (m of p.fileErrors; track m) { <li>{{ m }}</li> }
              </ul>
              <div class="actions"><app-button variant="secondary" (click)="startOver()"><app-icon name="arrow-left" [size]="16" />Choose another file</app-button></div>
            </app-card>
          } @else {
            @if (commitFailed()) {
              <p class="banner" role="alert">The import did not go through, and nothing was imported. The list below is up to date: fix what it shows and try again.</p>
            }
            <div class="summary" aria-label="Summary">
              <div class="stat"><span class="num">{{ p.summary.total }}</span><span class="lbl">Rows</span></div>
              <div class="stat ok"><span class="num">{{ p.summary.ok }}</span><span class="lbl">Ready</span></div>
              <div class="stat warn"><span class="num">{{ p.summary.warnings }}</span><span class="lbl">Warnings</span></div>
              <div class="stat bad"><span class="num">{{ p.summary.errors }}</span><span class="lbl">Errors</span></div>
            </div>

            @if (p.summary.errors > 0) {
              <p class="banner" role="status">{{ p.summary.errors }} row{{ p.summary.errors === 1 ? ' has' : 's have' }} an error. <strong>Nothing can be imported until every error is fixed</strong>, so you never end up with half a class. Download the error report, fix the rows and upload the file again.</p>
            } @else if (p.summary.warnings > 0) {
              <p class="banner soft" role="status">{{ p.summary.warnings }} row{{ p.summary.warnings === 1 ? ' has' : 's have' }} a warning. Warnings do not stop the import, but read them first.</p>
            } @else {
              <p class="banner good" role="status">Every row is ready. Students will be added to <strong>{{ p.academicYearName }}</strong> as "Existing Student".</p>
            }

            <app-card>
              <div class="filters" role="group" aria-label="Show rows">
                @for (f of filters; track f.key) {
                  <button type="button" class="chip" [class.on]="filter() === f.key" [attr.aria-pressed]="filter() === f.key" (click)="filter.set(f.key)">{{ f.label }} ({{ count(f.key) }})</button>
                }
              </div>

              @if (rows().length === 0) {
                <p class="note">No rows to show for this filter.</p>
              } @else {
                <div class="table-wrap">
                  <table class="list" aria-label="Rows of the uploaded file">
                    <thead><tr><th>Row</th><th>Status</th><th>Student</th><th>Class</th><th>Roll</th><th>Born</th><th>Admitted</th><th>Guardian</th><th>Messages</th></tr></thead>
                    <tbody>
                      @for (r of rows(); track r.rowNumber) {
                        <tr [class]="'r-' + r.status.toLowerCase()">
                          <td data-label="Row">{{ r.rowNumber }}</td>
                          <td data-label="Status"><span class="pill" [class.green]="r.status === 'OK'" [class.amber]="r.status === 'WARNING'" [class.red]="r.status === 'ERROR'">{{ statusLabel(r.status) }}</span></td>
                          <td data-label="Student" class="name">{{ r.fullName || '—' }}@if (r.admissionNumber) { <small>{{ r.admissionNumber }}</small> }</td>
                          <td data-label="Class">{{ r.className }} {{ r.sectionName }}</td>
                          <td data-label="Roll">{{ r.rollNumber || '—' }}</td>
                          <td data-label="Born">{{ r.dateOfBirth || '—' }}</td>
                          <td data-label="Admitted">{{ r.admissionDate || 'Year start' }}</td>
                          <td data-label="Guardian">{{ r.guardian || '—' }}@if (r.guardianMobile) { <small>{{ r.guardianMobile }}</small> }</td>
                          <td data-label="Messages" class="msgs">
                            @for (m of r.messages; track m) { <span>{{ m }}</span> }
                          </td>
                        </tr>
                      }
                    </tbody>
                  </table>
                </div>
              }
            </app-card>

            <div class="actions" style="margin-top: var(--space-5)">
              <app-button variant="secondary" [disabled]="committing()" (click)="startOver()"><app-icon name="arrow-left" [size]="16" />Choose another file</app-button>
              @if (p.summary.errors > 0) {
                <app-button variant="secondary" [disabled]="downloadingReport()" (click)="downloadReport()"><app-icon name="download" [size]="16" />{{ downloadingReport() ? 'Preparing…' : 'Download error report' }}</app-button>
              }
              <app-button [disabled]="!p.summary.canImport || committing()" (click)="confirmImport()">{{ committing() ? 'Importing…' : 'Import ' + p.summary.total + ' student' + (p.summary.total === 1 ? '' : 's') }}</app-button>
            </div>
            @if (committing()) { <p class="note" role="status">Importing. Please keep this page open.</p> }
          }
        }
      }

      @case ('result') {
        @if (result(); as r) {
          <app-card title="Import complete">
            <p class="done" role="status"><app-icon name="circle-check" [size]="20" /> <strong>{{ r.imported }}</strong> student{{ r.imported === 1 ? ' was' : 's were' }} added to <strong>{{ r.academicYearName }}</strong>.</p>
            <p class="note">Student IDs {{ r.firstStudentId }} to {{ r.lastStudentId }}.@if (r.warningRows > 0) { {{ r.warningRows }} row{{ r.warningRows === 1 ? '' : 's' }} had a warning.}</p>
            <div class="table-wrap">
              <table class="list" aria-label="Students added per class">
                <thead><tr><th>Class</th><th>Section</th><th>Students</th></tr></thead>
                <tbody>@for (c of r.byClass; track c.className + c.sectionName) { <tr><td data-label="Class">{{ c.className }}</td><td data-label="Section">{{ c.sectionName }}</td><td data-label="Students">{{ c.students }}</td></tr> }</tbody>
              </table>
            </div>
            @if (r.notes.length > 0) {
              <h3>Still to do</h3>
              <ul class="notes">@for (n of r.notes; track n) { <li>{{ n }}</li> }</ul>
            }
            <div class="actions">
              <a routerLink="/students" class="link-btn">View students</a>
              <app-button variant="secondary" (click)="startOver()">Import another file</app-button>
            </div>
          </app-card>
        }
      }
    }
  `,
  styles: `
    :host { display: block; }
    .how { margin: 0 0 var(--space-5); padding-left: var(--space-6); display: grid; gap: var(--space-3); line-height: 1.5; }
    .actions { display: flex; flex-wrap: wrap; gap: var(--space-3); align-items: center; }
    .picker { display: flex; flex-wrap: wrap; gap: var(--space-4); align-items: center; }
    .file { display: inline-flex; align-items: center; gap: var(--space-3); padding: var(--space-3) var(--space-5); border: 1px dashed var(--color-border); border-radius: var(--radius-lg); cursor: pointer; background: var(--color-surface, #fff); max-width: 100%; }
    .file:focus-within { outline: 2px solid var(--color-primary); outline-offset: 2px; }
    .file input { position: absolute; width: 1px; height: 1px; opacity: 0; }
    .file span { overflow-wrap: anywhere; }
    .note, .problem { margin: var(--space-3) 0 0; }
    .note { color: var(--color-text-muted); }
    .problem { color: var(--color-danger); }
    .file-errors { margin: 0 0 var(--space-5); padding-left: var(--space-5); color: var(--color-danger); display: grid; gap: var(--space-2); }
    .banner { margin: 0 0 var(--space-4); padding: var(--space-3) var(--space-4); border-radius: var(--radius-md); background: var(--color-danger-soft); border: 1px solid var(--color-border); line-height: 1.5; }
    .banner.soft { background: var(--color-warning-soft); }
    .banner.good { background: var(--color-success-soft); }
    .summary { display: grid; grid-template-columns: repeat(4, minmax(0, 1fr)); gap: var(--space-4); margin-bottom: var(--space-4); }
    .stat { background: var(--color-surface, #fff); border: 1px solid var(--color-border); border-radius: var(--radius-lg); padding: var(--space-4); display: flex; flex-direction: column; gap: 2px; }
    .num { font-size: 1.5rem; font-weight: var(--weight-semibold); }
    .lbl { color: var(--color-text-muted); }
    .ok .num { color: var(--color-status-active-fg); }
    .warn .num { color: #b54708; }
    .bad .num { color: var(--color-danger); }
    .filters { display: flex; flex-wrap: wrap; gap: var(--space-3); margin-bottom: var(--space-4); }
    .chip { border: 1px solid var(--color-border); background: var(--color-surface, #fff); border-radius: 999px; padding: var(--space-2) var(--space-4); font: inherit; cursor: pointer; }
    .chip.on { background: var(--color-primary); border-color: var(--color-primary); color: #fff; }
    .table-wrap { overflow-x: auto; }
    table.list { width: 100%; border-collapse: collapse; font-size: var(--text-sm); }
    .list th { text-align: left; padding: var(--space-3); border-bottom: 1px solid var(--color-border); color: var(--color-text-muted); font-weight: var(--weight-medium); white-space: nowrap; }
    .list td { padding: var(--space-3); border-bottom: 1px solid var(--color-border); vertical-align: top; }
    .list td small { display: block; color: var(--color-text-muted); }
    .list tr.r-error { background: var(--color-danger-soft); }
    .list tr.r-warning { background: var(--color-warning-soft); }
    .msgs span { display: block; }
    .msgs { min-width: 220px; }
    .pill { display: inline-block; padding: 2px var(--space-3); border-radius: 999px; font-size: var(--text-xs); font-weight: var(--weight-medium); background: var(--color-status-inactive-bg); color: var(--color-status-inactive-fg); }
    .pill.green { background: var(--color-success-soft); color: var(--color-status-active-fg); }
    .pill.amber { background: #fef0c7; color: #93370d; }
    .pill.red { background: #fee4e2; color: var(--color-danger); }
    .done { display: flex; gap: var(--space-3); align-items: center; margin: 0; }
    h3 { margin: var(--space-5) 0 var(--space-2); font-size: 1rem; }
    .notes { margin: 0 0 var(--space-5); padding-left: var(--space-5); display: grid; gap: var(--space-2); }
    .link-btn { padding: var(--space-3) var(--space-5); border-radius: var(--radius-md); background: var(--color-primary); color: #fff; text-decoration: none; }
    @media (max-width: 720px) {
      .summary { grid-template-columns: repeat(2, minmax(0, 1fr)); }
      .list thead { position: absolute; width: 1px; height: 1px; overflow: hidden; clip: rect(0 0 0 0); }
      .list, .list tbody, .list tr, .list td { display: block; width: 100%; }
      .list tr { border: 1px solid var(--color-border); border-radius: var(--radius-md); margin-bottom: var(--space-3); padding: var(--space-2) 0; }
      .list td { border: 0; padding: var(--space-2) var(--space-4); display: flex; justify-content: space-between; gap: var(--space-4); }
      .list td::before { content: attr(data-label); color: var(--color-text-muted); flex: none; }
      .msgs { min-width: 0; }
    }
  `,
})
export class StudentImportComponent {
  private readonly service = inject(StudentImportService);
  private readonly toast = inject(ToastService);
  private readonly confirm = inject(ConfirmDialogService);

  protected readonly breadcrumbs = [{ label: 'Students', link: '/students' }, { label: 'Import Students' }];
  protected readonly maxRows = 500;
  protected readonly filters: { key: ImportFilter; label: string }[] = [
    { key: 'all', label: 'All' },
    { key: 'errors', label: 'Errors' },
    { key: 'warnings', label: 'Warnings' },
  ];

  protected readonly step = signal<Step>('upload');
  protected readonly file = signal<File | null>(null);
  protected readonly pickError = signal<string | null>(null);
  protected readonly checking = signal(false);
  protected readonly committing = signal(false);
  protected readonly downloading = signal(false);
  protected readonly downloadingReport = signal(false);
  protected readonly preview = signal<ImportPreview | null>(null);
  protected readonly result = signal<ImportResult | null>(null);
  protected readonly filter = signal<ImportFilter>('all');
  protected readonly commitFailed = signal(false);

  protected readonly rows = computed(() => filterRows(this.preview()?.rows ?? [], this.filter()));

  protected count(key: ImportFilter): number { return filterRows(this.preview()?.rows ?? [], key).length; }
  protected statusLabel(s: string): string { return s === 'OK' ? 'Ready' : s === 'WARNING' ? 'Warning' : 'Error'; }

  protected onPick(event: Event): void {
    const input = event.target as HTMLInputElement;
    const picked = input.files?.[0] ?? null;
    this.pickError.set(null);
    if (!picked) { this.file.set(null); return; }
    const problem = fileProblem(picked);
    if (problem) { this.file.set(null); this.pickError.set(problem); input.value = ''; return; }
    this.file.set(picked);
  }

  protected downloadTemplate(): void {
    this.downloading.set(true);
    this.service.template().pipe(finalize(() => this.downloading.set(false))).subscribe({
      next: (blob) => saveBlob(blob, 'student-import-template.xlsx'),
      error: (e: Error) => this.toast.error(e.message),
    });
  }

  protected check(): void {
    const f = this.file();
    if (!f || this.checking()) return;
    this.checking.set(true);
    this.commitFailed.set(false);
    this.service.preview(f).pipe(finalize(() => this.checking.set(false))).subscribe({
      next: (p) => this.showPreview(p),
      error: () => undefined,   // already shown as a message by the error interceptor (e.g. a macro file or a damaged file)
    });
  }

  private showPreview(p: ImportPreview): void {
    this.preview.set(p);
    this.filter.set(p.summary.errors > 0 ? 'errors' : 'all');
    this.step.set('preview');
  }

  protected async confirmImport(): Promise<void> {
    const f = this.file(); const p = this.preview();
    if (!f || !p || !p.summary.canImport || this.committing()) return;
    const n = p.summary.total;
    const warn = p.summary.warnings > 0 ? ` ${p.summary.warnings} row${p.summary.warnings === 1 ? ' has' : 's have'} a warning.` : '';
    const ok = await this.confirm.confirm({
      title: 'Import students',
      message: `Add ${n} student${n === 1 ? '' : 's'} to ${p.academicYearName}?${warn} The file is checked again before anything is saved.`,
      confirmText: `Import ${n}`,
    });
    if (!ok) return;
    this.committing.set(true);
    this.service.commit(f).pipe(finalize(() => this.committing.set(false))).subscribe({
      next: (r) => { this.result.set(r); this.step.set('result'); this.toast.success(`${r.imported} student${r.imported === 1 ? '' : 's'} imported.`); },
      error: () => {
        // The server re-checked the file and refused (or something failed): nothing was saved. Show the current state of the file.
        this.commitFailed.set(true);
        this.service.preview(f).subscribe({ next: (fresh) => this.showPreview(fresh), error: () => undefined });
      },
    });
  }

  protected downloadReport(): void {
    const f = this.file();
    if (!f) return;
    this.downloadingReport.set(true);
    this.service.errorReport(f).pipe(finalize(() => this.downloadingReport.set(false))).subscribe({
      next: (blob) => saveBlob(blob, 'student-import-errors.xlsx'),
      error: (e: Error) => this.toast.error(e.message),
    });
  }

  protected startOver(): void {
    this.file.set(null);
    this.preview.set(null);
    this.result.set(null);
    this.pickError.set(null);
    this.commitFailed.set(false);
    this.filter.set('all');
    this.step.set('upload');
  }
}
