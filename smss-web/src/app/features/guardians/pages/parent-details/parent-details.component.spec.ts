import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { environment } from '../../../../../environments/environment';
import { canAccess } from '../../../../core/config/route-roles';
import { ConfirmDialogService } from '../../../../core/services/confirm-dialog.service';
import { clearSessions, makeUser, seedSession } from '../../../../core/testing/auth-test-utils';
import { IncompleteGuardians } from '../../models/guardian.model';
import { ParentDetailsComponent } from './parent-details.component';

const API = environment.apiUrl;
const env = <T>(data: T) => ({ status: true, statusCode: 200, message: 'ok', data });
const guardian = (id: number, name: string, over: Record<string, unknown> = {}) => ({
  guardianId: id, name, relationship: 'MOTHER', mobileNumber: '9876500000', whatsAppNumber: null, email: null, missing: ['WhatsApp', 'Email'],
  students: [{ studentId: `S${id}`, studentName: `Child ${id}`, classId: 3, className: 'Class 3', sectionId: 31, sectionName: 'A' }], ...over,
});
const PAGE: IncompleteGuardians = { total: 3, totalIncomplete: 3, page: 1, pageSize: 25, viewedYearId: 7, items: [guardian(1, 'Asha Rao'), guardian(2, 'Meera Singh', { missing: ['Email'], whatsAppNumber: '+919876500000' }), guardian(3, 'Kavita Das', { students: [{ studentId: 'S3', studentName: 'Child 3', classId: null, className: null, sectionId: null, sectionName: null }] })] };
const SECTIONS = [
  { sectionId: 31, schoolId: 'S', classId: 3, className: 'Class 3', sectionName: 'A', statusName: 'Active' },
  { sectionId: 32, schoolId: 'S', classId: 3, className: 'Class 3', sectionName: 'B', statusName: 'Active' },
  { sectionId: 41, schoolId: 'S', classId: 4, className: 'Class 4', sectionName: 'A', statusName: 'Active' },
];

describe('ParentDetailsComponent', () => {
  let fixture: ComponentFixture<ParentDetailsComponent>;
  let http: HttpTestingController;
  const el = () => fixture.nativeElement as HTMLElement;
  const cmp = () => fixture.componentInstance as unknown as Record<string, any>;

  function open(page: IncompleteGuardians = PAGE) {
    clearSessions();
    seedSession({ user: makeUser({ schoolId: 'S', userType: 'Employee', roles: ['Principal'] }) });
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(ParentDetailsComponent);
    fixture.detectChanges();
    http.expectOne(`${API}/api/Sections`).flush(env(SECTIONS));
    http.expectOne((r) => r.url === `${API}/api/Guardians/incomplete`).flush(env(page));
    fixture.detectChanges();
  }
  afterEach(() => {
    http.verify();
    clearSessions();
  });

  it('lists the parents with what is missing and the child with the class of the current year; a child not enrolled in it says so', () => {
    open();
    expect(el().querySelector('[data-count="incomplete"]')!.textContent).toContain('3 parents to complete');
    const rows = [...el().querySelectorAll('tbody tr')];
    expect(rows.map((r) => r.getAttribute('data-guardian'))).toEqual(['1', '2', '3']);
    expect(rows[0].textContent).toContain('WhatsApp');
    expect(rows[0].textContent).toContain('Class 3 – A');
    expect(rows[1].textContent).not.toMatch(/WhatsApp\s*Email/);   // only what is missing
    expect(rows[2].textContent).toContain('Not enrolled this year');
    expect(el().querySelector('[data-note="year"]')!.textContent).toContain('current academic year');
  });

  it('says so when every parent is complete', () => {
    open({ ...PAGE, total: 0, totalIncomplete: 0, items: [] });
    expect(el().querySelector('[data-state="empty"]')!.textContent).toContain('Every parent has a WhatsApp number and an email');
  });

  it('the class and section filters and the search go to the server, page 1 again', () => {
    open();
    cmp()['classControl'].setValue('3');
    let req = http.expectOne((r) => r.url === `${API}/api/Guardians/incomplete`);
    expect(req.request.params.get('classId')).toBe('3');
    expect(req.request.params.get('page')).toBe('1');
    req.flush(env(PAGE));
    expect(cmp()['sectionOptions']().map((o: { label: string }) => o.label)).toEqual(['A', 'B']);   // only that class's sections
    cmp()['sectionControl'].setValue('32');
    req = http.expectOne((r) => r.url === `${API}/api/Guardians/incomplete`);
    expect(req.request.params.get('sectionId')).toBe('32');
    req.flush(env(PAGE));
    cmp()['onSearch']({ target: { value: ' asha ' } } as unknown as Event);
    req = http.expectOne((r) => r.url === `${API}/api/Guardians/incomplete`);
    expect(req.request.params.get('search')).toBe('asha');
    req.flush(env(PAGE));
  });

  it('the bulk button works on the ticked rows only, asks first and sends exactly those ids', async () => {
    open();
    const bulk = () => el().querySelector('[data-action="bulk"]') as HTMLElement;
    expect(bulk().textContent).toContain('(0)');
    cmp()['toggle'](1);
    cmp()['toggle'](3);
    fixture.detectChanges();
    expect(bulk().textContent).toContain('(2)');
    const confirm = vi.spyOn(TestBed.inject(ConfirmDialogService), 'confirm').mockResolvedValueOnce(false).mockResolvedValueOnce(true);
    await cmp()['useMobile']();
    http.expectNone(`${API}/api/Guardians/bulk-whatsapp`);   // declined: nothing is sent
    expect(confirm.mock.calls[0][0].message).toContain('2 selected parents');
    expect(confirm.mock.calls[0][0].message).toContain('Email is not changed');
    const pending = cmp()['useMobile']();
    await Promise.resolve(); await Promise.resolve();
    const post = http.expectOne(`${API}/api/Guardians/bulk-whatsapp`);
    expect(post.request.body).toEqual({ guardianIds: [1, 3] });
    post.flush(env({ selected: 2, updated: 2, skipped: 0 }));
    await pending;
    http.expectOne((r) => r.url === `${API}/api/Guardians/incomplete`).flush(env(PAGE));   // read again; the ticks are cleared
    expect(cmp()['selectedCount']()).toBe(0);
    vi.restoreAllMocks();
  });

  it('select-all ticks the rows of this page only and there is no select-everything button', () => {
    open();
    cmp()['toggleAll']({ target: { checked: true } } as unknown as Event);
    expect([...cmp()['selected']()].sort()).toEqual([1, 2, 3]);
    cmp()['toggleAll']({ target: { checked: false } } as unknown as Event);
    expect(cmp()['selectedCount']()).toBe(0);
    expect(el().textContent).not.toMatch(/select all \d+|all 355/i);
    expect(el().querySelectorAll('[data-check="page"]').length).toBe(1);
  });

  it('changing the page clears the ticks, so a bulk action can only act on rows that are on screen', () => {
    open({ ...PAGE, total: 60 });
    cmp()['toggle'](1);
    cmp()['goToPage'](2);
    http.expectOne((r) => r.url === `${API}/api/Guardians/incomplete` && r.params.get('page') === '2').flush(env({ ...PAGE, total: 60, page: 2 }));
    expect(cmp()['selectedCount']()).toBe(0);
  });

  it('Edit opens the parent\'s form', () => {
    open();
    cmp()['edit'](PAGE.items[0]);
    fixture.detectChanges();
    http.expectOne(`${API}/api/Guardians/1`);
  });

  it('only School Admin, Principal and Admission Staff may open the page', () => {
    for (const role of ['School Admin', 'Principal', 'Admission Staff']) expect(canAccess('/parent-details', [role])).toBe(true);
    for (const role of ['Teacher', 'Accountant', 'Student', 'Guardian', 'Super Admin']) expect(canAccess('/parent-details', [role])).toBe(false);
  });
});
