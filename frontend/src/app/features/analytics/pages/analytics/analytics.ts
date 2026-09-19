import { DecimalPipe, DatePipe } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { Component, OnInit, computed, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { environment } from '../../../../../environments/environment';
import { AnalyticsOverview } from '../../../../core/models/analytics.model';
import { BarChart, BarSeries } from '../../../../shared/components/bar-chart/bar-chart';
import { KpiCard } from '../../../../shared/components/kpi-card/kpi-card';
import { StatusBadge } from '../../../../shared/components/badge/status-badge';
import { categoryLabel } from '../../../../shared/utils/category-label';


const EXCEPTION_SOURCES = ['Meter credit command', 'Connectivity command', 'Energy validation'];
const PRESETS = [
  { label: 'Last 7 days', days: 7 },
  { label: 'Last 30 days', days: 30 },
  { label: 'Last 90 days', days: 90 },
];

/**
 * Analytics. One request (GET /api/v1/analytics/overview) returns database-side aggregates over a
 * bounded date range; this page only charts them. It shows what the data model supports; area-wise
 * analysis, abnormal-consumption detection and balance-history trends are not offered because the
 * backend has no circle/division data, no anomaly detection and no balance snapshots.
 */
@Component({
  selector: 'pe-analytics',
  imports: [BarChart, KpiCard, StatusBadge, DecimalPipe, DatePipe, RouterLink],
  templateUrl: './analytics.html',
  styleUrl: './analytics.scss',
})
export class Analytics implements OnInit {
  protected readonly data = signal<AnalyticsOverview | null>(null);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);

  protected readonly fromDate = signal('');
  protected readonly toDate = signal('');
  protected readonly presets = PRESETS;
  protected readonly categoryLabel = categoryLabel;

  protected readonly money = (n: number) => '₹' + Math.round(n).toLocaleString('en-IN');
  protected readonly count = (n: number) => String(Math.round(n));
  protected readonly kwh = (n: number) => (Number.isInteger(n) ? String(n) : n.toFixed(1));

  constructor(private readonly http: HttpClient) {}

  ngOnInit(): void {
    this.load();
  }

  applyPreset(days: number): void {
    const to = new Date();
    const from = new Date();
    from.setDate(to.getDate() - (days - 1));
    this.fromDate.set(this.iso(from));
    this.toDate.set(this.iso(to));
    this.load();
  }

  onDateChange(which: 'from' | 'to', value: string): void {
    (which === 'from' ? this.fromDate : this.toDate).set(value);
    this.load();
  }

  retry(): void {
    this.load();
  }

  private iso(d: Date): string {
    return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
  }

  private label(date: string): string {
    return new Date(date.slice(0, 10) + 'T00:00:00').toLocaleDateString('en-GB', { day: 'numeric', month: 'short' });
  }

  protected readonly consumptionLabels = computed(() => (this.data()?.consumption ?? []).map((r) => this.label(r.date)));
  protected readonly consumptionSeries = computed<BarSeries[]>(() => [
    { name: 'Consumption (kWh)', color: 'var(--color-primary)', values: (this.data()?.consumption ?? []).map((r) => r.totalKwh) },
  ]);

  protected readonly rechargeLabels = computed(() => (this.data()?.recharges ?? []).map((r) => this.label(r.date)));
  protected readonly rechargeSeries = computed<BarSeries[]>(() => [
    { name: 'Payments received (₹)', color: 'var(--color-success)', values: (this.data()?.recharges ?? []).map((r) => r.amountReceived) },
  ]);

  protected readonly billingLabels = computed(() => (this.data()?.billing ?? []).map((r) => this.label(r.date)));
  protected readonly billingSeries = computed<BarSeries[]>(() => [
    { name: 'Billed (₹)', color: 'var(--color-primary)', values: (this.data()?.billing ?? []).map((r) => r.billed) },
    { name: 'Settled (₹)', color: 'var(--color-success)', values: (this.data()?.billing ?? []).map((r) => r.settled) },
  ]);

  protected readonly commandLabels = computed(() => (this.data()?.commands ?? []).map((r) => this.label(r.date)));
  protected readonly commandSeries = computed<BarSeries[]>(() => [
    { name: 'Disconnects', color: 'var(--color-danger)', values: (this.data()?.commands ?? []).map((r) => r.disconnects) },
    { name: 'Reconnects', color: 'var(--color-info)', values: (this.data()?.commands ?? []).map((r) => r.reconnects) },
  ]);

  protected readonly communicationLabels = computed(() => (this.data()?.communication ?? []).map((r) => this.label(r.date)));
  protected readonly communicationSeries = computed<BarSeries[]>(() => [
    { name: 'Communication failures', color: 'var(--color-warning)', values: (this.data()?.communication ?? []).map((r) => r.failures) },
    { name: 'Restorations', color: 'var(--color-success)', values: (this.data()?.communication ?? []).map((r) => r.restorations) },
  ]);

  protected readonly walletLabels = ['Overdrawn', '< ₹100', '₹100–500', '₹500–1,000', '₹1,000–5,000', '≥ ₹5,000'];
  protected readonly walletSeries = computed<BarSeries[]>(() => {
    const w = this.data()?.walletDistribution;
    if (!w) return [];
    return [{ name: 'Consumers', color: 'var(--color-primary)', values: [w.overdrawn, w.upTo100, w.upTo500, w.upTo1000, w.upTo5000, w.over5000] }];
  });
  protected readonly walletTotal = computed(() => this.walletSeries()[0]?.values.reduce((a, b) => a + b, 0) ?? 0);

  protected readonly exceptionRows = computed(() => {
    const rows = this.data()?.exceptions ?? [];
    return EXCEPTION_SOURCES.map((label, source) => ({
      label,
      open: rows.find((r) => r.sourceType === source && r.status === 0)?.count ?? 0,
      resolved: rows.find((r) => r.sourceType === source && r.status === 1)?.count ?? 0,
    })).filter((r) => r.open + r.resolved > 0);
  });

  private load(): void {
    this.loading.set(true);
    this.error.set(null);
    const params: Record<string, string> = {};
    if (this.fromDate()) params['from'] = this.fromDate();
    if (this.toDate()) params['to'] = this.toDate();
    this.http.get<AnalyticsOverview>(`${environment.apiBaseUrl}/api/v1/analytics/overview`, { params }).subscribe({
      next: (d) => {
        this.data.set(d);
        if (!this.fromDate()) this.fromDate.set(d.from.slice(0, 10));
        if (!this.toDate()) this.toDate.set(d.to.slice(0, 10));
        this.loading.set(false);
      },
      error: (err) => {
        this.error.set(err?.status === 400 ? (err?.error?.error ?? 'That date range is not valid.') : 'Could not load analytics from the API.');
        this.loading.set(false);
      },
    });
  }
}
