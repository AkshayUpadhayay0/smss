import { HttpTestingController } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ConfirmDialogService } from '../../../../core/services/confirm-dialog.service';
import { MasterDataService } from '../../../../core/services/master-data.service';
import { ToastService } from '../../../../core/services/toast.service';
import { API, envelope, setupHttp } from '../../../schools/testing/school-test-utils';
import { BOARD_TYPE_CONFIG } from '../../configs/board-type.config';
import { MasterConfig } from '../../models/master.model';
import { MasterListComponent } from './master-list.component';

/* eslint-disable @typescript-eslint/no-explicit-any */
const URL = `${API}/api/MasterData/board-types`;

const board = (id: number, code: string, name: string, isActive = true, description: string | null = null) => ({
  boardTypeId: id,
  boardCode: code,
  boardName: name,
  description,
  isActive,
  createdAt: '2026-01-01T00:00:00Z',
  updatedAt: '2026-01-01T00:00:00Z',
});

describe('MasterListComponent (Board Type config)', () => {
  let http: HttpTestingController;
  let fixture: ComponentFixture<MasterListComponent>;
  let confirm: ConfirmDialogService;
  let toast: ToastService;

  const el = () => fixture.nativeElement as HTMLElement;
  const bodyRows = () => [...el().querySelectorAll('tbody tr')];
  const type = (input: HTMLInputElement | HTMLTextAreaElement, value: string) => {
    input.value = value;
    input.dispatchEvent(new Event('input', { bubbles: true }));
  };
  const buttonWith = (root: ParentNode, label: string) =>
    [...root.querySelectorAll('button')].find((b) => b.textContent?.trim().startsWith(label)) as HTMLButtonElement;

  function create(config: MasterConfig = BOARD_TYPE_CONFIG) {
    fixture = TestBed.createComponent(MasterListComponent);
    fixture.componentRef.setInput('config', config);
    fixture.detectChanges();
  }
  function flushList(items = [board(1, 'CBSE', 'CBSE Board', true, 'Central'), board(2, 'ICSE', 'ICSE Board', false), board(3, 'IB', 'Intl Baccalaureate')]) {
    http.expectOne((r) => r.url === URL && r.method === 'GET').flush(envelope(items));
    fixture.detectChanges();
  }

  beforeEach(() => {
    http = setupHttp();
    confirm = TestBed.inject(ConfirmDialogService);
    toast = TestBed.inject(ToastService);
  });
  afterEach(() => http.verify());

  it('requests ALL records (includeInactive=true) and shows an Active/Inactive badge per row', () => {
    create();
    const req = http.expectOne((r) => r.url === URL);
    expect(req.request.params.get('includeInactive')).toBe('true');
    req.flush(envelope([board(1, 'CBSE', 'CBSE Board'), board(2, 'ICSE', 'ICSE Board', false)]));
    fixture.detectChanges();

    expect(bodyRows()).toHaveLength(2);
    expect([...el().querySelectorAll('.pill')].map((p) => p.textContent?.trim())).toEqual(['Active', 'Inactive']);
    expect(el().textContent).toContain('Add Board Type');
    expect(el().textContent).toContain('2 records');
  });

  it('shows Deactivate for active and Activate for inactive rows', () => {
    create();
    flushList();
    const labels = bodyRows().map((r) => (r.textContent?.includes('Deactivate') ? 'Deactivate' : 'Activate'));
    // default sort is by code: CBSE (active), IB (active), ICSE (inactive)
    expect(labels).toEqual(['Deactivate', 'Deactivate', 'Activate']);
  });

  it('searches across visible columns, including description and status', () => {
    create();
    flushList();
    const search = el().querySelector<HTMLInputElement>('input[type="text"]')!;
    type(search, 'central');
    fixture.detectChanges();
    expect(bodyRows()).toHaveLength(1);
    type(search, 'inactive');
    fixture.detectChanges();
    expect(bodyRows()[0].textContent).toContain('ICSE');
  });

  it('sorts when a header is clicked', () => {
    create();
    flushList();
    const header = [...el().querySelectorAll('th button.sort')].find((b) => b.textContent?.includes('Name')) as HTMLButtonElement;
    header.click();
    fixture.detectChanges();
    expect(bodyRows()[0].textContent).toContain('CBSE Board');
    header.click();
    fixture.detectChanges();
    expect(bodyRows()[0].textContent).toContain('Intl Baccalaureate');
  });

  describe('create / edit modal', () => {
    it('creates a record: required validation, blank description sent as null, new row appears', () => {
      create();
      flushList([]);
      buttonWith(el(), 'Add Board Type').click();
      fixture.detectChanges();

      const modal = el().querySelector('app-modal')!;
      expect(modal.textContent).toContain('Add Board Type');

      buttonWith(modal, 'Create').click(); // empty -> blocked
      fixture.detectChanges();
      http.expectNone((r) => r.method === 'POST');
      expect(modal.textContent).toContain('This field is required');

      const inputs = modal.querySelectorAll('input');
      type(inputs[0], 'I');
      type(inputs[1], 'x');
      buttonWith(modal, 'Create').click(); // below the server's 2-character minimum -> blocked
      fixture.detectChanges();
      http.expectNone((r) => r.method === 'POST');
      expect(modal.textContent).toContain('Minimum 2 characters');

      type(inputs[0], '  ICSE ');
      type(inputs[1], 'Indian Certificate');
      buttonWith(modal, 'Create').click();
      const req = http.expectOne((r) => r.method === 'POST' && r.url === URL);
      expect(req.request.body).toEqual({ boardCode: 'ICSE', boardName: 'Indian Certificate', description: null });
      req.flush(envelope(board(9, 'ICSE', 'Indian Certificate'), 201));
      fixture.detectChanges();

      expect(el().querySelector('app-modal')).toBeNull();
      expect(bodyRows()).toHaveLength(1);
      expect(bodyRows()[0].textContent).toContain('Indian Certificate');
      expect(toast.toasts().some((t) => t.type === 'success')).toBe(true);
    });

    it('edits a record: code is locked and omitted from the PUT body; fields are pre-filled', () => {
      create();
      flushList();
      (bodyRows()[0].querySelectorAll('button')[0] as HTMLButtonElement).click();
      fixture.detectChanges();

      const modal = el().querySelector('app-modal')!;
      expect(modal.textContent).toContain('Edit Board Type');
      const inputs = modal.querySelectorAll('input');
      expect(inputs[0].value).toBe('CBSE');
      expect(inputs[0].disabled).toBe(true);
      expect(inputs[1].value).toBe('CBSE Board');
      expect((modal.querySelector('textarea') as HTMLTextAreaElement).value).toBe('Central');

      type(inputs[1], 'CBSE Renamed');
      buttonWith(modal, 'Update').click();
      const req = http.expectOne((r) => r.method === 'PUT' && r.url === `${URL}/1`);
      expect(req.request.body).toEqual({ boardName: 'CBSE Renamed', description: 'Central' });
      req.flush(envelope(board(1, 'CBSE', 'CBSE Renamed', true, 'Central')));
      fixture.detectChanges();

      expect(bodyRows()[0].textContent).toContain('CBSE Renamed');
    });

    it('keeps the modal open with the typed values when the server rejects the save', () => {
      create();
      flushList([]);
      buttonWith(el(), 'Add Board Type').click();
      fixture.detectChanges();
      const modal = el().querySelector('app-modal')!;
      const inputs = modal.querySelectorAll('input');
      type(inputs[0], 'CBSE');
      type(inputs[1], 'Dup');
      buttonWith(modal, 'Create').click();
      http.expectOne((r) => r.method === 'POST').flush(
        { status: false, statusCode: 409, message: 'A board type with this code already exists.', data: null },
        { status: 409, statusText: 'Conflict' },
      );
      fixture.detectChanges();

      expect(el().querySelector('app-modal')).not.toBeNull();
      expect(el().querySelector<HTMLInputElement>('app-modal input')!.value).toBe('CBSE');
      expect(toast.toasts().at(-1)?.message).toBe('A board type with this code already exists.');
    });

    it('closes on Cancel and on Escape without saving', () => {
      create();
      flushList([]);
      buttonWith(el(), 'Add Board Type').click();
      fixture.detectChanges();
      buttonWith(el().querySelector('app-modal')!, 'Cancel').click();
      fixture.detectChanges();
      expect(el().querySelector('app-modal')).toBeNull();

      buttonWith(el(), 'Add Board Type').click();
      fixture.detectChanges();
      document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));
      fixture.detectChanges();
      expect(el().querySelector('app-modal')).toBeNull();
      http.expectNone((r) => r.method !== 'GET');
    });
  });

  describe('toggle status', () => {
    it('confirms, calls toggle-status, updates the row and invalidates cached lookups', async () => {
      const invalidate = vi.spyOn(TestBed.inject(MasterDataService), 'invalidate');
      vi.spyOn(confirm, 'confirm').mockResolvedValue(true);
      create();
      flushList();
      (bodyRows()[0].querySelectorAll('button')[1] as HTMLButtonElement).click();
      await fixture.whenStable();

      const req = http.expectOne(`${URL}/1/toggle-status`);
      expect(req.request.method).toBe('POST');
      req.flush(envelope(board(1, 'CBSE', 'CBSE Board', false, 'Central')));
      fixture.detectChanges();

      expect(bodyRows()[0].querySelector('.pill')?.textContent?.trim()).toBe('Inactive');
      expect(invalidate).toHaveBeenCalled();
      expect((confirm.confirm as any).mock.calls[0][0]).toMatchObject({ variant: 'danger', confirmText: 'Deactivate' });
    });

    it('does nothing when the confirmation is cancelled', async () => {
      vi.spyOn(confirm, 'confirm').mockResolvedValue(false);
      create();
      flushList();
      (bodyRows()[0].querySelectorAll('button')[1] as HTMLButtonElement).click();
      await fixture.whenStable();
      http.expectNone((r) => r.url.includes('toggle-status'));
    });

    it('uses the config\'s stronger warning text and surfaces the server refusal message', async () => {
      const cfg: MasterConfig = { ...BOARD_TYPE_CONFIG, deactivateWarning: (i) => `Careful: ${i['boardName']} is special.` };
      const spy = vi.spyOn(confirm, 'confirm').mockResolvedValue(true);
      create(cfg);
      flushList();
      (bodyRows()[0].querySelectorAll('button')[1] as HTMLButtonElement).click();
      await fixture.whenStable();
      expect(spy.mock.calls[0][0].message).toBe('Careful: CBSE Board is special.');

      http.expectOne(`${URL}/1/toggle-status`).flush(
        { status: false, statusCode: 409, message: 'Cannot deactivate: 3 user(s) are assigned.', data: null },
        { status: 409, statusText: 'Conflict' },
      );
      fixture.detectChanges();
      expect(toast.toasts().at(-1)?.message).toBe('Cannot deactivate: 3 user(s) are assigned.');
      expect(bodyRows()[0].querySelector('.pill')?.textContent?.trim()).toBe('Active'); // unchanged
    });
  });

  it('reloads when handed a different master config', () => {
    create();
    flushList();
    const other: MasterConfig = { ...BOARD_TYPE_CONFIG, title: 'School Type', path: 'school-types' };
    fixture.componentRef.setInput('config', other);
    fixture.detectChanges();
    http.expectOne((r) => r.url === `${API}/api/MasterData/school-types`).flush(envelope([]));
    fixture.detectChanges();
    expect(el().querySelector('h1')?.textContent).toContain('School Type');
    expect(bodyRows()).toHaveLength(0);
  });
});
