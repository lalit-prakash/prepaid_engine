import { DecimalPipe } from '@angular/common';
import { Component, OnInit, computed, signal } from '@angular/core';
import { AuthService } from '../../../../core/services/auth.service';
import {
  ConsumerMappingRow,
  HierarchyRow,
  ImportResult,
  NetworkService,
  NetworkSummary,
} from '../../../../core/services/network.service';
import { KpiCard } from '../../../../shared/components/kpi-card/kpi-card';
import { exportToCsv } from '../../../../shared/utils/csv-export';
import { csvToObjects, parseCsv } from '../../../../shared/utils/csv-parse';

type Kind = 'hierarchy' | 'mapping';

const MAX_ROWS = 10_000;

const HIERARCHY_KEYS = [
  'zoneCode', 'zoneName', 'circleCode', 'circleName', 'divisionCode', 'divisionName', 'subDivisionCode', 'subDivisionName',
  'substationCode', 'substationName', 'feederCode', 'feederName', 'dtrCode', 'dtrName',
];
const MAPPING_KEYS = ['accountNumber', 'dtrCode'];

/**
 * Network Hierarchy. Shows how much of the supply network (zone, circle, division, sub-division, substation,
 * feeder, DTR) is loaded, and lets an admin load it from CSV files: first the hierarchy (one path down to a DTR
 * per row), then which DTR each consumer is supplied from. Every file is checked first with a dry run that
 * reports what would change and every problem by line; nothing is saved until the check is clean and the admin
 * confirms, and a file with any error changes nothing.
 */
@Component({
  selector: 'pe-network-management',
  imports: [KpiCard, DecimalPipe],
  templateUrl: './network-management.html',
  styleUrl: './network-management.scss',
})
export class NetworkManagement implements OnInit {
  protected readonly summary = signal<NetworkSummary | null>(null);
  protected readonly summaryError = signal(false);

  protected readonly kind = signal<Kind>('hierarchy');
  protected readonly fileName = signal('');
  protected readonly parseError = signal<string | null>(null);
  protected readonly rows = signal<Record<string, string>[]>([]);
  protected readonly checking = signal(false);
  protected readonly saving = signal(false);
  protected readonly result = signal<ImportResult | null>(null);
  protected readonly requestError = signal<string | null>(null);
  protected readonly savedMessage = signal<string | null>(null);

  protected readonly canImport = computed(() => this.auth.canManageData());
  /** True only after a dry run of the current file came back with no errors. */
  protected readonly readyToSave = computed(() => {
    const r = this.result();
    return !!r && r.dryRun && r.rowsWithErrors === 0 && this.rows().length > 0;
  });
  protected readonly shownErrors = computed(() => (this.result()?.errors ?? []).slice(0, 100));
  protected readonly createdEntries = computed(() => Object.entries(this.result()?.created ?? {}).filter(([, n]) => n > 0));
  protected readonly renamedEntries = computed(() => Object.entries(this.result()?.renamed ?? {}).filter(([, n]) => n > 0));
  protected readonly maxRows = MAX_ROWS;

  constructor(
    private readonly network: NetworkService,
    private readonly auth: AuthService,
  ) {}

  ngOnInit(): void {
    this.loadSummary();
  }

  private loadSummary(): void {
    this.summaryError.set(false);
    this.network.summary().subscribe({
      next: (s) => this.summary.set(s),
      error: () => this.summaryError.set(true),
    });
  }

  protected setKind(kind: Kind): void {
    this.kind.set(kind);
    this.reset();
  }

  private reset(): void {
    this.fileName.set('');
    this.parseError.set(null);
    this.rows.set([]);
    this.result.set(null);
    this.requestError.set(null);
    this.savedMessage.set(null);
  }

  protected downloadTemplate(): void {
    const isHierarchy = this.kind() === 'hierarchy';
    exportToCsv(isHierarchy ? 'network-hierarchy-template.csv' : 'consumer-dtr-mapping-template.csv', isHierarchy ? HIERARCHY_KEYS : MAPPING_KEYS, []);
  }

  protected async onFile(event: Event): Promise<void> {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    this.reset();
    if (!file) return;
    this.fileName.set(file.name);

    const keys = this.kind() === 'hierarchy' ? HIERARCHY_KEYS : MAPPING_KEYS;
    const { objects, missing } = csvToObjects(parseCsv(await file.text()), keys);
    input.value = '';
    if (missing.length > 0) {
      this.parseError.set(`The file is missing these columns: ${missing.join(', ')}. Download the template to see the layout.`);
      return;
    }
    if (objects.length === 0) {
      this.parseError.set('The file has a header but no data rows.');
      return;
    }
    if (objects.length > MAX_ROWS) {
      this.parseError.set(`The file has ${objects.length.toLocaleString('en-IN')} rows; at most ${MAX_ROWS.toLocaleString('en-IN')} per file. Split it and load the parts one after another.`);
      return;
    }
    this.rows.set(objects);
    this.check();
  }

  /** Dry run: asks the API what the file would do, without saving. */
  protected check(): void {
    this.run(true);
  }

  /** Saves the file that has just passed its dry run. */
  protected save(): void {
    if (!this.readyToSave()) return;
    this.run(false);
  }

  private run(dryRun: boolean): void {
    this.requestError.set(null);
    this.savedMessage.set(null);
    if (dryRun) this.checking.set(true); else this.saving.set(true);

    const rows = this.rows();
    const request = this.kind() === 'hierarchy'
      ? this.network.importHierarchy(rows as unknown as HierarchyRow[], dryRun)
      : this.network.mapConsumers(rows as unknown as ConsumerMappingRow[], dryRun);

    request.subscribe({
      next: (r) => {
        this.result.set(r);
        this.checking.set(false);
        this.saving.set(false);
        if (r.saved) {
          this.savedMessage.set(this.kind() === 'hierarchy' ? 'Hierarchy saved.' : 'Consumers mapped.');
          this.rows.set([]);
          this.loadSummary();
        }
      },
      error: (err) => {
        this.requestError.set(err?.status === 403 ? 'Your role cannot load network data.' : err?.error?.error ?? 'The API could not process this file.');
        this.checking.set(false);
        this.saving.set(false);
      },
    });
  }
}
