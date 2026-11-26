import { ParentAccountResult, accountSummary, deliveryLabel } from './parent-account.model';

const result = (over: Partial<ParentAccountResult>): ParentAccountResult => ({ outcome: 'CREATED', canResend: true, loginId: 'GHS001GD001', deliveries: [], ...over });
const d = (channel: 'WHATSAPP' | 'EMAIL', status: 'SENT' | 'FAILED') => ({ deliveryId: 1, channel, status, templateCode: 't', purpose: 'p', senderKind: 'LOG', createdAt: '' });

describe('parent account wording', () => {
  it('says plainly when WhatsApp sending is off (recorded only), sent, or failed', () => {
    expect(deliveryLabel({ channel: 'WHATSAPP', status: 'LOGGED' })).toContain('nothing was sent');
    expect(deliveryLabel({ channel: 'EMAIL', status: 'SENT' })).toBe('Email: sent');
    expect(deliveryLabel({ channel: 'EMAIL', status: 'FAILED', errorMessage: 'The email could not be sent.' })).toContain('failed (The email could not be sent.)');
  });

  it('a created account with a sent link is a success; with a failed or missing link it is a warning that says where to resend', () => {
    expect(accountSummary(result({ deliveries: [d('WHATSAPP', 'SENT')] })).kind).toBe('success');
    expect(accountSummary(result({ deliveries: [d('WHATSAPP', 'FAILED')] }))).toEqual(expect.objectContaining({ kind: 'warning', text: expect.stringContaining('Resend') }));
    expect(accountSummary(result({ deliveries: [], reason: 'No website address.' })).kind).toBe('warning');
  });

  it('a sibling is told no second account was made, and a refusal shows its reason', () => {
    expect(accountSummary(result({ outcome: 'CHILD_ADDED' })).text).toContain('No second account');
    expect(accountSummary(result({ outcome: 'NOT_CREATED', reason: 'No parent consent is on record.' })).text).toContain('No parent consent');
    expect(accountSummary(result({ outcome: 'FAILED', reason: null })).kind).toBe('warning');
  });

  it('never contains a link, token or password', () => {
    const all = JSON.stringify([accountSummary(result({ deliveries: [d('WHATSAPP', 'SENT')] })), deliveryLabel({ channel: 'WHATSAPP', status: 'SENT' })]);
    expect(all).not.toMatch(/https?:|\?t=|password:/i);
  });
});
