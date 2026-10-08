import { HttpTestingController } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ConfirmDialogService } from '../../../core/services/confirm-dialog.service';
import { ToastService } from '../../../core/services/toast.service';
import { API, envelope, setupHttp } from '../../schools/testing/school-test-utils';
import { MasterConfig, MasterItem } from '../models/master.model';
import { MasterListComponent } from '../pages/master-list/master-list.component';
import { BOARD_TYPE_CONFIG } from './board-type.config';
import { ROLE_CONFIG } from './role.config';
import { SCHOOL_LEVEL_CONFIG } from './school-level.config';
import { SCHOOL_TYPE_CONFIG } from './school-type.config';
import { STATUS_CONFIG } from './status.config';

const ALL: [string, MasterConfig][] = [
  ['Board Type', BOARD_TYPE_CONFIG],
  ['School Type', SCHOOL_TYPE_CONFIG],
  ['School Level', SCHOOL_LEVEL_CONFIG],
  ['Status', STATUS_CONFIG],
  ['Role', ROLE_CONFIG],
];

/** A plausible API record for any config, built from its own field list. */
function sample(cfg: MasterConfig, id: number, overrides: Record<string, unknown> = {}): MasterItem {
  const item: MasterItem = { [cfg.idKey]: id, isActive: true };
  for (const f of cfg.fields) item[f.key] = f.type === 'select' ? (f.options?.[0]?.value ?? '') : `${f.label} ${id}`;
  return { ...item, ...overrides };
}

describe('master configs', () => {
  let http: HttpTestingController;
  let fixture: ComponentFixture<MasterListComponent>;
  let confirm: ConfirmDialogService;
  let toast: ToastService;

  const el = () => fixture.nativeElement as HTMLElement;
  const bodyRows = () => [...el().querySelectorAll('tbody tr')];
  const url = (cfg: MasterConfig) => `${API}/api/MasterData/${cfg.path}`;
  const type = (input: HTMLInputElement | HTMLTextAreaElement, value: string) => {
    input.value = value;
    input.dispatchEvent(new Event('input', { bubbles: true }));
  };
  const button = (root: ParentNode, label: string) => [...root.querySelectorAll('button')].find((b) => b.textContent?.trim().startsWith(label)) as HTMLButtonElement;

  function open(cfg: MasterConfig, items: MasterItem[]) {
    fixture = TestBed.createComponent(MasterListComponent);
    fixture.componentRef.setInput('config', cfg);
    fixture.detectChanges();
    http.expectOne((r) => r.url === url(cfg)).flush(envelope(items));
    fixture.detectChanges();
  }

  beforeEach(() => {
    http = setupHttp();
    confirm = TestBed.inject(ConfirmDialogService);
    toast = TestBed.inject(ToastService);
  });
  afterEach(() => http.verify());

  describe.each(ALL)('%s', (_name, cfg) => {
    it('lists every record (includeInactive) with a status badge and the right title', () => {
      fixture = TestBed.createComponent(MasterListComponent);
      fixture.componentRef.setInput('config', cfg);
      fixture.detectChanges();
      const req = http.expectOne((r) => r.url === url(cfg));
      expect(req.request.params.get('includeInactive')).toBe('true');
      req.flush(envelope([sample(cfg, 1), sample(cfg, 2, { isActive: false })]));
      fixture.detectChanges();

      expect(el().querySelector('h1')?.textContent).toContain(cfg.title);
      expect(bodyRows()).toHaveLength(2);
      expect([...el().querySelectorAll('.pill')].map((p) => p.textContent?.trim())).toEqual(['Active', 'Inactive']);
      expect(el().textContent).toContain(`Add ${cfg.title}`);
    });

    it('every column key is something the row actually provides', () => {
      open(cfg, [sample(cfg, 1, cfg === ROLE_CONFIG ? { isProtected: false } : {})]);
      const cells = bodyRows()[0].querySelectorAll('td');
      // no blank cells except optional description-like fields: at least id/name-ish content renders
      expect(cells.length).toBe(cfg.columns.length + 1); // + actions
      for (const col of cfg.columns.filter((c) => c.key !== 'description')) {
        const idx = cfg.columns.indexOf(col);
        expect(cells[idx].textContent?.trim(), `column ${col.key}`).not.toBe('');
      }
    });

    it('create sends exactly the config fields (blank optional -> null) and edit omits create-only fields', () => {
      open(cfg, [sample(cfg, 7)]);
      button(el(), `Add ${cfg.title}`).click();
      fixture.detectChanges();
      let modal = el().querySelector('app-modal')!;

      // fill required text fields; leave optional ones blank
      const inputs = [...modal.querySelectorAll<HTMLInputElement>('input')];
      const textFields = cfg.fields.filter((f) => f.type === 'text');
      textFields.forEach((f, i) => type(inputs[i], `ZZ ${f.key}`));
      for (const f of cfg.fields.filter((f) => f.type === 'select')) {
        const select = modal.querySelector<HTMLSelectElement>('select')!;
        select.value = f.options![0].value;
        select.dispatchEvent(new Event('change', { bubbles: true }));
      }
      button(modal, 'Create').click();
      const post = http.expectOne((r) => r.method === 'POST' && r.url === url(cfg));
      expect(Object.keys(post.request.body).sort()).toEqual(cfg.fields.map((f) => f.key).sort());
      for (const f of cfg.fields.filter((f) => !f.required)) expect(post.request.body[f.key]).toBeNull();
      post.flush(envelope(sample(cfg, 8), 201));
      fixture.detectChanges();
      expect(el().querySelector('app-modal')).toBeNull();
      expect(bodyRows()).toHaveLength(2);

      // edit the first row
      (bodyRows()[0].querySelectorAll('button')[0] as HTMLButtonElement).click();
      fixture.detectChanges();
      modal = el().querySelector('app-modal')!;
      expect(modal.textContent).toContain(`Edit ${cfg.title}`);
      button(modal, 'Update').click();
      const put = http.expectOne((r) => r.method === 'PUT');
      expect(put.request.url).toMatch(new RegExp(`${cfg.path}/\\d+$`));
      expect(Object.keys(put.request.body).sort()).toEqual(cfg.fields.filter((f) => !f.createOnly).map((f) => f.key).sort());
      put.flush(envelope(sample(cfg, 7)));
    });

    it('toggle confirms, calls toggle-status and updates the row', async () => {
      vi.spyOn(confirm, 'confirm').mockResolvedValue(true);
      open(cfg, [sample(cfg, 5, cfg === ROLE_CONFIG ? { isProtected: false } : { sname: 'ZZ plain' })]);
      const toggle = [...bodyRows()[0].querySelectorAll('button')].find((b) => b.textContent?.includes('Deactivate'))!;
      toggle.click();
      await fixture.whenStable();
      const req = http.expectOne(`${url(cfg)}/5/toggle-status`);
      req.flush(envelope(sample(cfg, 5, { isActive: false })));
      fixture.detectChanges();
      expect(bodyRows()[0].querySelector('.pill')?.textContent?.trim()).toBe('Inactive');
    });
  });

  describe('Status', () => {
    const rows = [
      { sid: 1, sname: 'Active', stype: 'general status', isActive: true },
      { sid: 2, sname: 'Inactive', stype: 'general status', isActive: true },
      { sid: 3, sname: 'Active Plan', stype: 'school plan status', isActive: true },
      { sid: 5, sname: 'Payment Due', stype: 'payment status', isActive: true },
    ];

    it('shows friendly type names and filters by type', () => {
      open(STATUS_CONFIG, rows);
      expect(bodyRows()).toHaveLength(4);
      expect(bodyRows()[0].textContent).toContain('General status');

      const select = el().querySelector<HTMLSelectElement>('.filter select')!;
      expect([...select.options].map((o) => o.textContent?.trim())).toEqual(['All types', 'General status', 'School plan status', 'Payment status']);
      select.value = 'payment status';
      select.dispatchEvent(new Event('change', { bubbles: true }));
      fixture.detectChanges();
      expect(bodyRows()).toHaveLength(1);
      expect(bodyRows()[0].textContent).toContain('Payment Due');
      expect(el().textContent).toContain('1 record');

      select.value = '';
      select.dispatchEvent(new Event('change', { bubbles: true }));
      fixture.detectChanges();
      expect(bodyRows()).toHaveLength(4);
    });

    it('warns strongly before deactivating the core Active/Inactive statuses, but not other statuses', async () => {
      const spy = vi.spyOn(confirm, 'confirm').mockResolvedValue(false);
      open(STATUS_CONFIG, rows);
      const deactivate = (i: number) => [...bodyRows()[i].querySelectorAll('button')].find((b) => b.textContent?.includes('Deactivate'))!.click();

      deactivate(0); // "Active"
      await fixture.whenStable();
      expect(spy.mock.calls[0][0].message).toMatch(/core status/i);

      deactivate(3); // "Payment Due"
      await fixture.whenStable();
      expect(spy.mock.calls[1][0].message).not.toMatch(/core status/i);
    });

    it('the add form warns not to rename Active/Inactive and requires a type', () => {
      open(STATUS_CONFIG, rows);
      button(el(), 'Add Status').click();
      fixture.detectChanges();
      const modal = el().querySelector('app-modal')!;
      expect(modal.textContent).toMatch(/do not rename/i);
      type(modal.querySelector('input')!, 'Suspended');
      button(modal, 'Create').click(); // no type chosen
      fixture.detectChanges();
      http.expectNone((r) => r.method === 'POST');
      expect(modal.textContent).toContain('This field is required');
    });
  });

  describe('Role', () => {
    const roles = [
      { roleId: 1, roleCode: 'SUPER_ADMIN', roleName: 'Super Admin', description: null, statusId: 1, isActive: true, isProtected: true },
      { roleId: 2, roleCode: 'TEACHER', roleName: 'Teacher', description: 'Teaches', statusId: 1, isActive: true, isProtected: false },
    ];

    it('labels system vs custom roles from the server flag', () => {
      open(ROLE_CONFIG, roles);
      const cells = bodyRows().map((r) => r.querySelectorAll('td')[3].textContent?.trim());
      expect(cells).toEqual(['System', 'Custom']);
    });

    it('uses a strong warning for a protected role and surfaces the server refusal', async () => {
      const spy = vi.spyOn(confirm, 'confirm').mockResolvedValue(true);
      open(ROLE_CONFIG, roles);
      [...bodyRows()[0].querySelectorAll('button')].find((b) => b.textContent?.includes('Deactivate'))!.click();
      await fixture.whenStable();
      expect(spy.mock.calls[0][0].message).toMatch(/system role/i);

      http.expectOne(`${url(ROLE_CONFIG)}/1/toggle-status`).flush(
        { status: false, statusCode: 403, message: 'System roles cannot be deactivated.', data: null },
        { status: 403, statusText: 'Forbidden' },
      );
      fixture.detectChanges();
      expect(toast.toasts().at(-1)?.message).toBe('System roles cannot be deactivated.');
      expect(bodyRows()[0].querySelector('.pill')?.textContent?.trim()).toBe('Active');
    });

    it('shows the server message when a role still has users assigned', async () => {
      vi.spyOn(confirm, 'confirm').mockResolvedValue(true);
      open(ROLE_CONFIG, roles);
      [...bodyRows()[1].querySelectorAll('button')].find((b) => b.textContent?.includes('Deactivate'))!.click();
      await fixture.whenStable();
      http.expectOne(`${url(ROLE_CONFIG)}/2/toggle-status`).flush(
        { status: false, statusCode: 409, message: 'Cannot deactivate this role: 3 user(s) are assigned to it. Reassign them first.', data: null },
        { status: 409, statusText: 'Conflict' },
      );
      expect(toast.toasts().at(-1)?.message).toContain('3 user(s) are assigned');
    });
  });
});
