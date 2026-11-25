import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { environment } from '../../../../../environments/environment';
import { clearSessions, makeUser, seedSession } from '../../../../core/testing/auth-test-utils';
import { MergePreview } from '../../models/guardian.model';
import { MergeDialogComponent } from './merge-dialog.component';

const API = environment.apiUrl;
const env = <T>(data: T) => ({ status: true, statusCode: 200, message: 'ok', data });
const brief = (id: number, over: Record<string, unknown> = {}) => ({ guardianId: id, name: 'Neel Tomar', relationship: 'FATHER', mobileNumber: '9200220047', whatsAppNumber: null, email: null, occupation: null, hasLogin: false, ...over });
const PREVIEW = (over: Partial<MergePreview> = {}): MergePreview => ({
  survivor: brief(1), absorbed: brief(2),
  links: [{ studentId: 'S2', studentName: 'Child Two', admissionNumber: 'ADM2', className: 'Class 3', sectionName: 'A', relationship: 'FATHER', isPrimary: true, action: 'MOVE' }, { studentId: 'S1', studentName: 'Child One', admissionNumber: 'ADM1', className: null, sectionName: null, relationship: 'FATHER', isPrimary: false, action: 'COMBINE' }],
  fieldsToCopy: { email: 'n@example.com' }, applications: [{ admissionId: 9, applicationNumber: 'ADM20260009', role: 'PRIMARY', action: 'REPOINT' }, { admissionId: 10, applicationNumber: 'ADM20260010', role: 'EXTRA', action: 'DROP' }],
  guardiansToRepoint: [7], account: 'MOVE', canMerge: true, blockers: [], ...over,
});

describe('MergeDialogComponent', () => {
  let fixture: ComponentFixture<MergeDialogComponent>;
  let http: HttpTestingController;
  const cmp = () => fixture.componentInstance as unknown as Record<string, any>;
  const text = () => document.body.textContent ?? '';

  function open(preview: MergePreview = PREVIEW()) {
    clearSessions();
    seedSession({ user: makeUser({ schoolId: 'S', userType: 'Employee', roles: ['Principal'] }) });
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(MergeDialogComponent);
    fixture.componentRef.setInput('survivorId', 1);
    fixture.componentRef.setInput('absorbedId', 2);
    fixture.detectChanges();
    http.expectOne(`${API}/api/Guardians/merge/preview`).flush(env(preview));
    fixture.detectChanges();
  }
  afterEach(() => {
    http.verify();
    clearSessions();
  });

  it('shows exactly what would move: the students (moved or combined), the contact copied, the applications, the earlier merges and the login', () => {
    open();
    expect(document.querySelector('[data-note="summary"]')!.textContent).toContain('will be merged into');
    expect(document.querySelector('[data-list="links"]')!.textContent).toContain('Child Two');
    expect(document.querySelector('[data-list="links"]')!.textContent).toContain('the two links are combined');
    expect(document.querySelector('[data-list="fields"]')!.textContent).toContain('Email: n@example.com');
    expect(document.querySelector('[data-list="applications"]')!.textContent).toContain('ADM20260009 (primary parent): moves to');
    expect(document.querySelector('[data-list="applications"]')!.textContent).toContain('this entry is dropped');
    expect(document.querySelector('[data-note="repoint"]')!.textContent).toContain('1 record(s)');
    expect(document.querySelector('[data-note="account"]')!.textContent).toContain('has a login');
  });

  it('the merge needs the acknowledgement: until it is ticked nothing is sent, then the request carries confirm and the reason', () => {
    open();
    cmp()['merge']();
    http.expectNone(`${API}/api/Guardians/merge`);
    cmp()['ack'].set(true);
    cmp()['reason'].setValue('  same father  ');
    const merged = vi.fn();
    fixture.componentInstance.merged.subscribe(merged);
    cmp()['merge']();
    const post = http.expectOne(`${API}/api/Guardians/merge`);
    expect(post.request.body).toEqual({ survivorId: 1, absorbedId: 2, confirm: true, reason: 'same father' });
    post.flush(env({ mergeId: 5, survivorId: 1, absorbedId: 2, linksMoved: 1, linksCombined: 1, applicationsRepointed: 1, guardiansRepointed: 1, fieldsCopied: ['email'], accountMoved: true }));
    expect(merged).toHaveBeenCalledTimes(1);
  });

  it('a pair that cannot be merged shows why and offers no merge', () => {
    open(PREVIEW({ canMerge: false, account: 'BLOCKED', blockers: ['Both guardians already have a login, so they cannot be merged.'] }));
    expect(document.querySelector('[data-blocker]')!.textContent).toContain('Both guardians already have a login');
    expect(document.querySelector('[data-field="ack"]')).toBeNull();
    cmp()['ack'].set(true);
    cmp()['merge']();
    http.expectNone(`${API}/api/Guardians/merge`);
    expect(text()).not.toContain('undefined');
  });
});
