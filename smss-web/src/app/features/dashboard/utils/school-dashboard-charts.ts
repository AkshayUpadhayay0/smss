import type { ApexOptions } from 'ng-apexcharts';
import { formatInr } from '../../../core/utils/currency';
import { AttendanceTrendPoint, FeeCollectionMonth } from '../models/school-dashboard.model';
import { shortDay } from './school-dashboard-format';
import { ChartColors, FALLBACK_COLORS, compactInr, shortMonth } from './super-admin-charts';

const BASE_CHART = { fontFamily: 'inherit', toolbar: { show: false }, zoom: { enabled: false }, animations: { enabled: true, speed: 400 } };

/**
 * Daily present % for the last 14 days. A day nothing was marked is a gap in the line (null), never a drop to 0%, so a Sunday does not look like a
 * day everyone was absent.
 */
export function attendanceTrendOptions(points: AttendanceTrendPoint[], c: ChartColors = FALLBACK_COLORS): ApexOptions {
  return {
    chart: { ...BASE_CHART, type: 'line', height: 280 },
    series: [{ name: 'Present', data: points.map((p) => p.presentPercent) }],
    colors: [c.success],
    stroke: { curve: 'smooth', width: 3 },
    markers: { size: 4 },
    dataLabels: { enabled: false },
    xaxis: { categories: points.map((p) => shortDay(p.date)), axisBorder: { show: false }, axisTicks: { show: false }, labels: { rotate: -45, hideOverlappingLabels: true } },
    yaxis: { min: 0, max: 100, tickAmount: 5, labels: { formatter: (v: number) => `${Math.round(v)}%` } },
    grid: { borderColor: c.grid, strokeDashArray: 4 },
    legend: { show: false },
    tooltip: {
      y: {
        formatter: (v: number | null, o?: { dataPointIndex: number }) => {
          const p = points[o?.dataPointIndex ?? 0];
          return v == null || !p ? 'Not marked' : `${v}% (${p.present} of ${p.marked})`;
        },
      },
    },
  } as ApexOptions;
}

/** Fees collected per month for the last 6 months. */
export function feeCollectionOptions(months: FeeCollectionMonth[], c: ChartColors = FALLBACK_COLORS): ApexOptions {
  return {
    chart: { ...BASE_CHART, type: 'bar', height: 280 },
    series: [{ name: 'Collected', data: months.map((m) => m.amount) }],
    colors: [c.primary],
    plotOptions: { bar: { borderRadius: 4, columnWidth: '50%' } },
    dataLabels: { enabled: false },
    xaxis: { categories: months.map((m) => shortMonth(m.label)), axisBorder: { show: false }, axisTicks: { show: false } },
    yaxis: { min: 0, labels: { formatter: (v: number) => compactInr(v) } },
    grid: { borderColor: c.grid, strokeDashArray: 4 },
    legend: { show: false },
    tooltip: {
      x: { formatter: (_v: number, o?: { dataPointIndex: number }) => months[o?.dataPointIndex ?? 0]?.label ?? '' },
      y: { formatter: (v: number) => formatInr(v) },
    },
  };
}
