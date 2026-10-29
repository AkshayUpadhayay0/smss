import type { ApexOptions } from 'ng-apexcharts';
import { formatInr } from '../../../core/utils/currency';
import { CollectionBucket, CollectionKey, MonthCount, RevenueExpenseMonth } from '../../subscriptions/models/super-admin-dashboard.model';

/** Chart colours. ApexCharts needs real colour values, so they mirror the design tokens in styles/_tokens.scss. */
export interface ChartColors {
  primary: string;
  warning: string;
  success: string;
  info: string;
  danger: string;
  muted: string;
  grid: string;
}

export const FALLBACK_COLORS: ChartColors = { primary: '#7c5cfc', warning: '#d97706', success: '#16a34a', info: '#2563eb', danger: '#dc2626', muted: '#9aa0b4', grid: '#e8eaf0' };

/** Reads the colour tokens from the page; falls back to the defaults where there is no stylesheet (tests). */
export function readChartColors(root: Element = document.documentElement): ChartColors {
  const style = getComputedStyle(root);
  const pick = (name: string, fallback: string) => style.getPropertyValue(name).trim() || fallback;
  return {
    primary: pick('--color-primary', FALLBACK_COLORS.primary),
    warning: pick('--color-warning', FALLBACK_COLORS.warning),
    success: pick('--color-success', FALLBACK_COLORS.success),
    info: pick('--color-info', FALLBACK_COLORS.info),
    danger: pick('--color-danger', FALLBACK_COLORS.danger),
    muted: pick('--color-text-subtle', FALLBACK_COLORS.muted),
    grid: pick('--color-border', FALLBACK_COLORS.grid),
  };
}

/** Rupees for a chart axis, Indian style: 1500 -> "₹1.5K", 250000 -> "₹2.5L", 30000000 -> "₹3Cr". */
export function compactInr(n: number): string {
  const abs = Math.abs(n);
  const sign = n < 0 ? '-' : '';
  const trim = (x: number) => String(Math.round(x * 10) / 10);
  if (abs >= 1e7) return `${sign}₹${trim(abs / 1e7)}Cr`;
  if (abs >= 1e5) return `${sign}₹${trim(abs / 1e5)}L`;
  if (abs >= 1e3) return `${sign}₹${trim(abs / 1e3)}K`;
  return `${sign}₹${trim(abs)}`;
}

/** "Nov 2025" -> "Nov '25", short enough for twelve labels on one axis. */
export const shortMonth = (label: string): string => (label.length >= 8 ? `${label.slice(0, 3)} '${label.slice(-2)}` : label);

/** "₹…" money with a minus sign in front for a loss. */
export const signedInr = (n: number): string => (n < 0 ? '-' + formatInr(-n) : formatInr(n));

const BASE_CHART = { fontFamily: 'inherit', toolbar: { show: false }, zoom: { enabled: false }, animations: { enabled: true, speed: 400 } };

export function revenueExpenseOptions(months: RevenueExpenseMonth[], c: ChartColors = FALLBACK_COLORS): ApexOptions {
  return {
    chart: { ...BASE_CHART, type: 'area', height: 300 },
    series: [
      { name: 'Revenue', data: months.map((m) => m.revenue) },
      { name: 'Expenses', data: months.map((m) => m.expenses) },
    ],
    colors: [c.primary, c.warning],
    stroke: { curve: 'smooth', width: 2.5 },
    fill: { type: 'gradient', gradient: { shadeIntensity: 1, opacityFrom: 0.3, opacityTo: 0.02, stops: [0, 95, 100] } },
    dataLabels: { enabled: false },
    xaxis: { categories: months.map((m) => shortMonth(m.label)), axisBorder: { show: false }, axisTicks: { show: false } },
    yaxis: { min: 0, labels: { formatter: (v: number) => compactInr(v) } },
    grid: { borderColor: c.grid, strokeDashArray: 4 },
    legend: { position: 'top', horizontalAlign: 'right', markers: { size: 6 } },
    tooltip: { x: { formatter: (_v: number, o?: { dataPointIndex: number }) => months[o?.dataPointIndex ?? 0]?.label ?? '' }, y: { formatter: (v: number) => formatInr(v) } },
  };
}

export function registrationOptions(months: MonthCount[], c: ChartColors = FALLBACK_COLORS): ApexOptions {
  return {
    chart: { ...BASE_CHART, type: 'bar', height: 260 },
    series: [{ name: 'Schools registered', data: months.map((m) => m.count) }],
    colors: [c.primary],
    plotOptions: { bar: { borderRadius: 4, columnWidth: '55%' } },
    dataLabels: { enabled: false },
    xaxis: { categories: months.map((m) => shortMonth(m.label)), axisBorder: { show: false }, axisTicks: { show: false } },
    yaxis: { min: 0, forceNiceScale: true, labels: { formatter: (v: number) => String(Math.round(v)) } },
    grid: { borderColor: c.grid, strokeDashArray: 4 },
    legend: { show: false },
    tooltip: { x: { formatter: (_v: number, o?: { dataPointIndex: number }) => months[o?.dataPointIndex ?? 0]?.label ?? '' } },
  };
}

export interface StatusCounts {
  trial: number;
  active: number;
  overdue: number;
  cancelled: number;
}

export const STATUS_SLICES: { key: keyof StatusCounts; label: string; status: string }[] = [
  { key: 'trial', label: 'Trial', status: 'TRIAL' },
  { key: 'active', label: 'Active', status: 'ACTIVE' },
  { key: 'overdue', label: 'Overdue', status: 'OVERDUE' },
  { key: 'cancelled', label: 'Cancelled', status: 'CANCELLED' },
];

export function statusDonutOptions(counts: StatusCounts, c: ChartColors = FALLBACK_COLORS): ApexOptions {
  const total = counts.trial + counts.active + counts.overdue + counts.cancelled;
  return {
    chart: { ...BASE_CHART, type: 'donut', height: 240 },
    series: STATUS_SLICES.map((s) => counts[s.key]),
    labels: STATUS_SLICES.map((s) => s.label),
    colors: [c.info, c.success, c.danger, c.muted],
    dataLabels: { enabled: false },
    legend: { show: false },
    stroke: { width: 2 },
    plotOptions: { pie: { donut: { size: '70%', labels: { show: true, name: { show: true }, value: { show: true, formatter: (v: string) => v }, total: { show: true, label: 'Schools', formatter: () => String(total) } } } } },
    tooltip: { y: { formatter: (v: number) => `${v} ${v === 1 ? 'school' : 'schools'}` } },
  };
}

export const COLLECTION_LABELS: Record<CollectionKey, string> = { Paid: 'Paid', Pending: 'Pending', PartiallyPaid: 'Partially paid', Overdue: 'Overdue' };

export interface CollectionSegment extends CollectionBucket {
  label: string;
  /** Share of the invoiced total, 0-100. */
  percent: number;
  /** Width of the bar segment: the share, but never invisible for a bucket that has invoices. */
  width: number;
}

/** The segmented bar: each bucket's share of what was invoiced in the period. */
export function collectionSegments(buckets: CollectionBucket[]): CollectionSegment[] {
  const total = buckets.reduce((sum, b) => sum + b.amount, 0);
  return buckets.map((b) => {
    const percent = total > 0 ? (b.amount / total) * 100 : 0;
    return { ...b, label: COLLECTION_LABELS[b.key] ?? b.key, percent, width: b.count > 0 && percent > 0 ? Math.max(percent, 2) : 0 };
  });
}

export interface Trend {
  text: string;
  /** good / bad / flat: for expenses a rise is bad, for everything else it is good. */
  tone: 'good' | 'bad' | 'flat';
  up: boolean | null;
}

/** The arrow badge under a stat; null when there is no trend to show (no history, or nothing to compare with). */
export function trendOf(percent: number | null | undefined, risingIsBad = false): Trend | null {
  if (percent == null) return null;
  if (percent === 0) return { text: '0%', tone: 'flat', up: null };
  const up = percent > 0;
  return { text: `${Math.abs(percent)}%`, tone: up === risingIsBad ? 'bad' : 'good', up };
}
