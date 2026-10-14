/** Indian rupees, e.g. 2500 -> "₹2,500.00" (en-IN digit grouping). */
export function formatInr(amount: number): string {
  return '₹' + amount.toLocaleString('en-IN', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
}
