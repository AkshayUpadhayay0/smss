import { formatInr } from '../../../core/utils/currency';
import { FeeReceipt } from '../models/fee-payment.model';

const escapeHtml = (value: unknown): string =>
  String(value ?? '').replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c]!);

const longDate = (iso: string) => new Date(iso).toLocaleDateString('en-IN', { day: '2-digit', month: 'short', year: 'numeric' });
const dateTime = (iso: string) => new Date(iso).toLocaleString('en-IN', { day: '2-digit', month: 'short', year: 'numeric', hour: '2-digit', minute: '2-digit' });

/** The receipt as a standalone printable page. Every value is escaped: names and remarks are typed by people. */
export function receiptHtml(r: FeeReceipt): string {
  const rows = r.lines
    .map(
      (l) => `<tr>
        <td>${escapeHtml(l.feeTypeName)}<div class="sub">${escapeHtml(l.frequencyName)}</div></td>
        <td>${escapeHtml(longDate(l.dueDate))}</td>
        <td class="num">${escapeHtml(formatInr(l.amountDue))}</td>
        <td class="num"><strong>${escapeHtml(formatInr(l.allocatedAmount))}</strong></td>
      </tr>`,
    )
    .join('');

  const voided = r.status === 'Voided';
  const voidedBlock = voided
    ? `<div class="watermark">VOID</div>
  <div class="void-banner"><strong>VOID</strong>Voided${r.voidedAt ? ' on ' + escapeHtml(dateTime(r.voidedAt)) : ''}${r.voidedByName ? ' by ' + escapeHtml(r.voidedByName) : ''}.
    Reason: ${escapeHtml(r.voidReason ?? '—')}. This receipt is no longer valid.</div>`
    : '';

  return `<!doctype html>
<html lang="en"><head><meta charset="utf-8"><title>Receipt ${escapeHtml(r.receiptNumber)}${voided ? ' (VOID)' : ''}</title>
<style>
  * { box-sizing: border-box; }
  body { font-family: system-ui, -apple-system, 'Segoe UI', Roboto, sans-serif; color: #1c1b29; margin: 32px; }
  .head { display: flex; justify-content: space-between; gap: 24px; border-bottom: 2px solid #7c5cfc; padding-bottom: 12px; }
  h1 { margin: 0; font-size: 20px; } h2 { margin: 0 0 4px; font-size: 14px; letter-spacing: .06em; text-transform: uppercase; color: #5a38d6; }
  .muted, .sub { color: #6b6a7b; font-size: 12px; }
  .meta { display: grid; grid-template-columns: 1fr 1fr; gap: 6px 24px; margin: 16px 0; font-size: 13px; }
  .meta span { color: #6b6a7b; }
  table { width: 100%; border-collapse: collapse; font-size: 13px; }
  th { text-align: left; font-size: 11px; text-transform: uppercase; letter-spacing: .05em; color: #6b6a7b; border-bottom: 1px solid #d5d8e2; padding: 8px 6px; }
  td { padding: 8px 6px; border-bottom: 1px solid #eceef4; vertical-align: top; }
  .num { text-align: right; } th.num { text-align: right; }
  .total td { border-top: 2px solid #1c1b29; border-bottom: 0; font-size: 15px; font-weight: 700; }
  .void-banner { margin: 14px 0 0; padding: 10px 14px; border: 2px solid #c0392b; border-radius: 8px; color: #c0392b; font-size: 13px; }
  .void-banner strong { display: block; font-size: 20px; letter-spacing: .14em; }
  .watermark { position: fixed; inset: 0; display: flex; align-items: center; justify-content: center; font-size: 180px; font-weight: 800; letter-spacing: .2em; color: rgba(192,57,43,.14); transform: rotate(-22deg); pointer-events: none; z-index: 0; }
  .foot { margin-top: 28px; display: flex; justify-content: space-between; font-size: 12px; color: #6b6a7b; }
  @media print { body { margin: 12mm; } }
</style></head>
<body>
  ${voidedBlock}
  <div class="head">
    <div>
      <h1>${escapeHtml(r.school.name)}</h1>
      ${r.school.address ? `<div class="muted">${escapeHtml(r.school.address)}</div>` : ''}
      ${r.school.phone || r.school.email ? `<div class="muted">${escapeHtml([r.school.phone, r.school.email].filter(Boolean).join(' · '))}</div>` : ''}
    </div>
    <div style="text-align:right">
      <h2>Fee receipt</h2>
      <div><strong>${escapeHtml(r.receiptNumber)}</strong></div>
      <div class="muted">${escapeHtml(dateTime(r.paymentDate))}</div>
    </div>
  </div>
  <div class="meta">
    <div><span>Student</span><br><strong>${escapeHtml(r.student.name)}</strong></div>
    <div><span>Student ID</span><br><strong>${escapeHtml(r.student.studentId)}</strong></div>
    <div><span>Class</span><br>${escapeHtml(r.student.className ?? '—')}</div>
    <div><span>Academic year</span><br>${escapeHtml(r.student.academicYearName ?? '—')}</div>
    <div><span>Payment mode</span><br>${escapeHtml(r.paymentMode)}</div>
    <div><span>Received by</span><br>${escapeHtml(r.collectedByName ?? '—')}</div>
  </div>
  <table>
    <thead><tr><th>Fee</th><th>Due date</th><th class="num">Installment</th><th class="num">Paid now</th></tr></thead>
    <tbody>${rows}<tr class="total"><td colspan="3">Total received</td><td class="num">${escapeHtml(formatInr(r.amount))}</td></tr></tbody>
  </table>
  ${r.remarks ? `<p class="muted">Remarks: ${escapeHtml(r.remarks)}</p>` : ''}
  <div class="foot"><span>This is a computer generated receipt.</span><span>Thank you.</span></div>
</body></html>`;
}

/** Opens the receipt in its own window and brings up the print dialog (which also offers "Save as PDF"). False if the browser blocked the window. */
export function printReceipt(r: FeeReceipt): boolean {
  const w = window.open('', '_blank', 'width=820,height=900');
  if (!w) return false;
  w.document.open();
  w.document.write(receiptHtml(r));
  w.document.close();
  w.focus();
  // print once: whichever comes first, the load event or the fallback timer (some browsers finish loading before onload is set)
  let printed = false;
  const go = () => {
    if (printed) return;
    printed = true;
    try {
      w.print();
    } catch {
      /* the window was closed */
    }
  };
  w.onload = go;
  setTimeout(go, 400);
  return true;
}
