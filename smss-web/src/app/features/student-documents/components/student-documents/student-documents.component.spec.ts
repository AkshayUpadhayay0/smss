import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { environment } from '../../../../../environments/environment';
import { ConfirmDialogService } from '../../../../core/services/confirm-dialog.service';
import { ToastService } from '../../../../core/services/toast.service';
import { clearSessions, makeUser, seedSession } from '../../../../core/testing/auth-test-utils';
import { StudentDocuments } from '../../models/student-document.model';
import { StudentDocumentsComponent } from './student-documents.component';

const API = environment.apiUrl;
const env = <T>(data: T, statusCode = 200) => ({ status: true, statusCode, message: 'ok', data });
const type = (id: number, name: string, appliesTo: string, isRequired = false) => ({ documentTypeId: id, schoolId: 'S', documentName: name, documentCode: name.slice(0, 3), appliesTo, isRequired, isActive: true });
const row = (over: Record<string, unknown> = {}) => ({ studentDocumentId: 1, documentTypeId: 2, documentTypeName: 'Aadhaar', status: 'SUBMITTED', fileName: 'aadhaar.pdf', contentType: 'application/pdf', fileSize: 2048, remarks: null, waiverReason: null, byName: 'Asha Staff', at: '2026-11-20T00:00:00Z', fromApplication: false, voided: false, ...over });
const docs = (over: Partial<StudentDocuments> = {}): StudentDocuments => ({
  studentId: 'S1', studentName: 'Asha Rao', canManage: true, pendingCount: 1,
  required: [
    { documentTypeId: 1, documentTypeName: 'Birth Certificate', documentTypeCode: 'BC', status: 'PENDING', studentDocumentId: null },
    { documentTypeId: 2, documentTypeName: 'Aadhaar', documentTypeCode: 'AAD', status: 'SUBMITTED', studentDocumentId: 1 },
  ],
  documents: [row() as never],
  ...over,
});

describe('StudentDocumentsComponent', () => {
  let fixture: ComponentFixture<StudentDocumentsComponent>;
  let http: HttpTestingController;
  const el = () => fixture.nativeElement as HTMLElement;
  const cmp = () => fixture.componentInstance as unknown as Record<string, any>;

  function open(data: StudentDocuments | { status: number } = docs(), role = 'School Admin', beforeLoad?: () => void) {
    clearSessions();
    seedSession({ user: makeUser({ schoolId: 'S', userType: 'Employee', roles: [role] }) });
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(StudentDocumentsComponent);
    fixture.componentRef.setInput('studentId', 'S1');
    beforeLoad?.();
    fixture.detectChanges();
    const get = http.expectOne(`${API}/api/StudentDocuments/S1`);
    if ('status' in data) get.flush({ status: false, statusCode: data.status, message: 'no', data: null }, { status: data.status, statusText: 'x' });
    else get.flush(env(data));
    http.expectOne(`${API}/api/DocumentType/S`).flush(env([type(1, 'Birth Certificate', 'STUDENT', true), type(2, 'Aadhaar', 'BOTH', true), type(3, 'Resume', 'EMPLOYEE'), type(4, 'Sports certificate', 'STUDENT')]));
    fixture.detectChanges();
  }
  afterEach(() => {
    http.verify();
    clearSessions();
  });

  it('shows a chip per required document: a pending one says "Document not submitted"', () => {
    open();
    const items = [...el().querySelectorAll('[data-list="required"] li[data-status]')];
    expect(items.map((i) => i.getAttribute('data-status'))).toEqual(['PENDING', 'SUBMITTED']);
    expect(items[0].querySelector('[data-chip]')!.textContent).toContain('Document not submitted');
    expect(items[1].querySelector('[data-chip]')!.textContent).toContain('Submitted');
    expect(el().textContent).not.toMatch(/overdue/i);   // there is no overdue status
  });

  it('a manager can waive a pending document and upload; the type choices are active student types, pending ones first', () => {
    open();
    expect(el().querySelector('[data-form="upload"]')).not.toBeNull();
    expect(el().textContent).toContain('Waive');
    expect(cmp()['typeOptions']().map((o: { label: string }) => o.label)).toEqual(['Birth Certificate (required)', 'Aadhaar (required)', 'Sports certificate']);
  });

  it('a class teacher (read-only) sees the chips and no upload, waive, download or void', () => {
    open(docs({ canManage: false, documents: [row({ fileName: null, fileSize: null, byName: null }) as never] }), 'Teacher');
    expect(el().querySelectorAll('[data-chip]').length).toBe(2);
    expect(el().querySelector('[data-form="upload"]')).toBeNull();
    expect(el().textContent).not.toContain('Waive');
    expect(el().textContent).not.toContain('Download');
  });

  it('a refusal (403: not this section\'s class teacher) hides the card without a toast', () => {
    let error: ReturnType<typeof vi.spyOn> | undefined;
    open({ status: 403 }, 'Teacher', () => { error = vi.spyOn(TestBed.inject(ToastService), 'error'); });
    expect(el().querySelector('app-card')).toBeNull();
    expect(error).toBeDefined();
    expect(error).not.toHaveBeenCalled();
  });

  it('Waive needs a reason: an empty one is not sent; a reason is posted with the type', () => {
    open();
    cmp()['startWaive'](1);
    fixture.detectChanges();
    cmp()['confirmWaive'](1);
    expect(cmp()['reasonError']()).toContain('reason is required');
    http.expectNone(`${API}/api/StudentDocuments/S1/waive`);
    cmp()['reasonControl'].setValue('  Court order  ');
    cmp()['confirmWaive'](1);
    const post = http.expectOne(`${API}/api/StudentDocuments/S1/waive`);
    expect(post.request.body).toEqual({ documentTypeId: 1, reason: 'Court order' });
    post.flush(env(row({ studentDocumentId: 5, documentTypeId: 1, status: 'WAIVED' }), 201));
    http.expectOne(`${API}/api/StudentDocuments/S1`).flush(env(docs()));   // the card reads the truth again
  });

  it('Upload refuses without a type or a file, a wrong extension and an oversized file - nothing is sent', () => {
    open();
    cmp()['upload']();
    expect(cmp()['typeError']()).toBe('Select a document type');
    expect(cmp()['fileError']()).toBe('Choose a file');
    const pick = (f: File) => cmp()['onFile']({ target: { files: [f], value: '' } } as unknown as Event);
    pick(new File(['x'], 'run.exe')); expect(cmp()['fileError']()).toContain('Only PDF');
    pick(new File([new Uint8Array(5 * 1024 * 1024 + 1)], 'big.pdf')); expect(cmp()['fileError']()).toContain('5 MB');
    pick(new File([], 'empty.pdf')); expect(cmp()['fileError']()).toContain('empty');
    http.expectNone(`${API}/api/StudentDocuments/S1/upload`);
  });

  it('Upload sends the file, the type and the remarks, then reads the card again', () => {
    open();
    cmp()['typeControl'].setValue('1');
    cmp()['remarksControl'].setValue(' Front and back ');
    cmp()['file'].set(new File(['%PDF-1.4'], 'bc.pdf', { type: 'application/pdf' }));
    cmp()['upload']();
    const post = http.expectOne(`${API}/api/StudentDocuments/S1/upload`);
    const form = post.request.body as FormData;
    expect(form.get('documentTypeId')).toBe('1');
    expect(form.get('remarks')).toBe('Front and back');
    expect((form.get('file') as File).name).toBe('bc.pdf');
    post.flush(env(row({ studentDocumentId: 9, documentTypeId: 1 }), 201));
    http.expectOne(`${API}/api/StudentDocuments/S1`).flush(env(docs()));
  });

  it('Void asks first and posts to the void route; Download goes through the authenticated endpoint', async () => {
    open();
    vi.stubGlobal('URL', Object.assign(URL, { createObjectURL: vi.fn(() => 'blob:x'), revokeObjectURL: vi.fn() }));
    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => undefined);
    cmp()['download'](row());
    const get = http.expectOne(`${API}/api/StudentDocuments/S1/1/download`);
    expect(get.request.method).toBe('GET');
    get.flush(new Blob(['%PDF']));

    vi.spyOn(TestBed.inject(ConfirmDialogService), 'confirm').mockResolvedValue(true);
    await cmp()['voidIt'](row());
    const post = http.expectOne(`${API}/api/StudentDocuments/S1/1/void`);
    expect(post.request.method).toBe('POST');
    post.flush(env(row({ voided: true })));
    http.expectOne(`${API}/api/StudentDocuments/S1`).flush(env(docs()));
    vi.restoreAllMocks();
  });
});
