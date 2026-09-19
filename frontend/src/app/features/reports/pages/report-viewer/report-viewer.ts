import { DatePipe, DecimalPipe } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { Component, OnInit, computed, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { environment } from '../../../../../environments/environment';
import { exportToCsv } from '../../../../shared/utils/csv-export';
import { REPORT_DEFINITIONS, ReportColumn, ReportDefinition } from './report-definitions';

interface ReportResponse {
  rows: Record<string, unknown>[];
  truncated: boolean;
  generatedAt: string;
  totals?: Record<string, number>;
}

/**
 * Generic viewer for the server-side reports (GET /api/v1/reports/*). Filtering and aggregation
 * happen in the database; this page only renders what the API returns. Detail reports are capped
 * by the API and say so; CSV export serializes the rows already on screen and is labelled as such.
 */
@Component({
  selector: 'pe-report-viewer',
  imports: [DatePipe, DecimalPipe, RouterLink],
  templateUrl: './report-viewer.html',
  styleUrl: './report-viewer.scss',
})
export class ReportViewer implements OnInit {
  protected readonly definition = signal<ReportDefinition | null>(null);
  protected readonly response = signal<ReportResponse | null>(null);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  protected readonly notFound = signal(false);

  protected readonly fromDate = signal('');
  protected readonly toDate = signal('');
  protected readonly status = signal('');

  protected readonly rows = computed(() => this.response()?.rows ?? []);

  constructor(
    private readonly route: ActivatedRoute,
    private readonly http: HttpClient,
  ) {}

  ngOnInit(): void {
    // paramMap (not snapshot): the same instance is reused when navigating between reports.
    this.route.paramMap.subscribe((params) => {
      const id = params.get('id') ?? '';
      const definition = REPORT_DEFINITIONS.find((d) => d.id === id) ?? null;
      this.response.set(null);
      this.fromDate.set('');
      this.toDate.set('');
      this.status.set('');
      this.notFound.set(!definition);
      this.definition.set(definition);
      if (!definition) {
        this.loading.set(false);
        return;
      }
      this.load();
    });
  }

  onDateChange(which: 'from' | 'to', value: string): void {
    (which === 'from' ? this.fromDate : this.toDate).set(value);
    this.load();
  }

  onStatusChange(value: string): void {
    this.status.set(value);
    this.load();
  }

  clearFilters(): void {
    this.fromDate.set('');
    this.toDate.set('');
    this.status.set('');
    for (const el of Array.from(document.querySelectorAll<HTMLInputElement>('.report-filters input[type=date]'))) el.value = '';
    this.load();
  }

  protected get hasFilters(): boolean {
    return !!(this.fromDate() || this.toDate() || this.status());
  }

  retry(): void {
    this.load();
  }

  protected cell(row: Record<string, unknown>, col: ReportColumn): string | number | null {
    const value = row[col.key];
    if (value === null || value === undefined || value === '') return null;
    if (col.type === 'enum') return col.labels?.[Number(value)] ?? String(value);
    return value as string | number;
  }

  protected total(col: ReportColumn): number | null {
    const totals = this.response()?.totals;
    if (!col.totalKey || !totals || totals[col.totalKey] === undefined) return null;
    return totals[col.totalKey];
  }

  protected get hasTotals(): boolean {
    const d = this.definition();
    return !!d && d.columns.some((c) => this.total(c) !== null);
  }

  protected link(row: Record<string, unknown>, col: ReportColumn): string | null {
    return col.link ? col.link(row) : null;
  }

  export(): void {
    const d = this.definition();
    if (!d) return;
    exportToCsv(
      `${d.id}-${new Date().toISOString().slice(0, 10)}.csv`,
      d.columns.map((c) => c.label),
      this.rows().map((row) => d.columns.map((c) => this.cell(row, c) ?? '')),
    );
  }

  private load(): void {
    const d = this.definition();
    if (!d) return;
    this.loading.set(true);
    this.error.set(null);
    const params: Record<string, string> = {};
    if (this.fromDate()) params['from'] = this.fromDate();
    if (this.toDate()) params['to'] = this.toDate();
    if (this.status()) params['status'] = this.status();
    this.http.get<ReportResponse>(`${environment.apiBaseUrl}/api/v1/reports/${d.endpoint}`, { params }).subscribe({
      next: (r) => {
        this.response.set(r);
        this.loading.set(false);
      },
      error: () => {
        this.error.set('Could not load this report from the API.');
        this.loading.set(false);
      },
    });
  }
}
