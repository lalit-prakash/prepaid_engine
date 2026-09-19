import { DatePipe, DecimalPipe } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { Component, OnInit, computed, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { environment } from '../../../../../environments/environment';
import { NETWORK_LEVELS, NetworkLevelId, NetworkNode, NetworkService } from '../../../../core/services/network.service';
import { ReportJobService } from '../../../../core/services/report-job.service';
import { OperateOnly } from '../../../../shared/directives/operate-only';
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
  imports: [DatePipe, DecimalPipe, RouterLink, OperateOnly],
  templateUrl: './report-viewer.html',
  styleUrl: './report-viewer.scss',
})
export class ReportViewer implements OnInit {
  protected readonly definition = signal<ReportDefinition | null>(null);
  protected readonly response = signal<ReportResponse | null>(null);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  protected readonly notFound = signal(false);
  protected readonly exportRequested = signal(false);
  protected readonly exportRequesting = signal(false);
  protected readonly exportError = signal<string | null>(null);

  protected readonly fromDate = signal('');
  protected readonly toDate = signal('');
  protected readonly status = signal('');

  /** Network filter: one selected node id per level ('' = any) and the options each select offers. */
  protected readonly levels = NETWORK_LEVELS;
  protected readonly selected = signal<Record<string, string>>({});
  protected readonly options = signal<Record<string, NetworkNode[]>>({});
  /** Day-wise reports only: break each day down by this level ('' = no breakdown). */
  protected readonly groupBy = signal('');

  protected readonly rows = computed(() => this.response()?.rows ?? []);
  protected readonly columns = computed(() => {
    const d = this.definition();
    return d ? d.columns.filter((c) => !c.groupedOnly || !!this.groupBy()) : [];
  });

  constructor(
    private readonly route: ActivatedRoute,
    private readonly http: HttpClient,
    private readonly network: NetworkService,
    private readonly reportJobs: ReportJobService,
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
      this.selected.set({});
      this.options.set({});
      this.groupBy.set('');
      this.exportRequested.set(false);
      this.exportError.set(null);
      this.notFound.set(!definition);
      this.definition.set(definition);
      if (!definition) {
        this.loading.set(false);
        return;
      }
      this.loadOptions(0);
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

  /** Picking a node at one level narrows the choices below it and clears anything already picked under it. */
  onLevelChange(index: number, value: string): void {
    const next: Record<string, string> = { ...this.selected() };
    next[NETWORK_LEVELS[index].id] = value;
    for (let i = index + 1; i < NETWORK_LEVELS.length; i++) next[NETWORK_LEVELS[i].id] = '';
    this.selected.set(next);
    this.loadOptions(index + 1);
    this.load();
  }

  onGroupByChange(value: string): void {
    this.groupBy.set(value);
    this.load();
  }

  /** Loads the choices for one level from the node picked one level above it (all zones for the first). */
  private loadOptions(index: number): void {
    const cleared = { ...this.options() };
    for (let i = index; i < NETWORK_LEVELS.length; i++) cleared[NETWORK_LEVELS[i].id] = [];
    this.options.set(cleared);
    if (index >= NETWORK_LEVELS.length) return;
    const parent = index === 0 ? undefined : this.selected()[NETWORK_LEVELS[index - 1].id] || undefined;
    if (index > 0 && !parent) return;
    this.network.nodes(NETWORK_LEVELS[index].id as NetworkLevelId, parent).subscribe({
      next: (nodes) => this.options.update((o) => ({ ...o, [NETWORK_LEVELS[index].id]: nodes })),
      error: () => {},
    });
  }

  clearFilters(): void {
    this.fromDate.set('');
    this.toDate.set('');
    this.status.set('');
    this.selected.set({});
    this.groupBy.set('');
    this.loadOptions(0);
    for (const el of Array.from(document.querySelectorAll<HTMLInputElement>('.report-filters input[type=date]'))) el.value = '';
    this.load();
  }

  protected get hasFilters(): boolean {
    return !!(this.fromDate() || this.toDate() || this.status() || this.groupBy() || Object.values(this.selected()).some((v) => !!v));
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
    return !!d && this.columns().some((c) => this.total(c) !== null);
  }

  protected link(row: Record<string, unknown>, col: ReportColumn): string | null {
    return col.link ? col.link(row) : null;
  }

  /** Asks the API to build the whole report as a CSV in the background, with the filters currently applied. */
  requestFullExport(): void {
    const d = this.definition();
    if (!d?.exportKey) return;
    this.exportRequesting.set(true);
    this.exportError.set(null);
    const body: Record<string, string | null> = { report: d.exportKey, from: this.fromDate() || null, to: this.toDate() || null, status: this.status() || null };
    for (const level of NETWORK_LEVELS) body[level.param] = this.selected()[level.id] || null;
    this.reportJobs.request(body as never).subscribe({
      next: () => { this.exportRequesting.set(false); this.exportRequested.set(true); },
      error: (err) => {
        this.exportRequesting.set(false);
        this.exportError.set(err?.error?.error ?? (err?.status === 403 ? 'Your role cannot export reports.' : 'Could not request the export.'));
      },
    });
  }

  export(): void {
    const d = this.definition();
    if (!d) return;
    exportToCsv(
      `${d.id}-${new Date().toISOString().slice(0, 10)}.csv`,
      this.columns().map((c) => c.label),
      this.rows().map((row) => this.columns().map((c) => this.cell(row, c) ?? '')),
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
    if (this.groupBy()) params['level'] = this.groupBy();
    for (const level of NETWORK_LEVELS) {
      const id = this.selected()[level.id];
      if (id) params[level.param] = id;
    }
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
