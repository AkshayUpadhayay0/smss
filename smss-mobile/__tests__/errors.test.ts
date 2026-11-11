import { GENERIC_MESSAGE, toApiError } from '../src/api/errors';

describe('toApiError', () => {
  it('recognises the first-login 403 by its message, not by status alone', () => {
    const e = toApiError(403, { status: false, statusCode: 403, message: 'Password change required. Change your temporary password to continue.', data: null });
    expect(e.kind).toBe('password_change_required');
    expect(toApiError(403, { message: 'Only a student can see their own homework here.' }).kind).toBe('forbidden');
  });
  it('shows the first validation message for a 400', () => {
    const e = toApiError(400, { message: 'Validation failed', data: { NewPassword: ['New password must be at least 8 characters.'] } });
    expect(e.kind).toBe('validation');
    expect(e.message).toBe('New password must be at least 8 characters.');
  });
  it('uses the server message for a 4xx business rule and surfaces 409 as conflict', () => {
    const e = toApiError(409, { message: 'A School Admin has corrected today\'s attendance for this section.' });
    expect(e.kind).toBe('conflict');
    expect(e.message).toContain('corrected');
  });
  it('never shows a 5xx body: generic text only', () => {
    const e = toApiError(500, { message: 'System.InvalidOperationException: SELECT * FROM tb_users' });
    expect(e.kind).toBe('server');
    expect(e.message).toBe(GENERIC_MESSAGE);
  });
  it('survives a non-JSON body', () => {
    expect(toApiError(502, '<html>Bad gateway</html>').message).toBe(GENERIC_MESSAGE);
    expect(toApiError(400, null).message).toBe(GENERIC_MESSAGE);
  });
});
