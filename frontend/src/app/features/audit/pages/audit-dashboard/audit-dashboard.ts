import { DatePipe } from '@angular/common';
import { Component, OnInit, signal } from '@angular/core';
import { AuditEntryService } from '../../../../core/services/audit-entry.service';
import { AuditEntrySummary } from '../../../../core/models/audit-entry.model';
import { KpiCard } from '../../../../shared/components/kpi-card/kpi-card';

/**
 * Real, API-backed Audit dashboard (GET /api/v1/audit-entries) — an immutable, append-only log
 * of every tracked operational/config change (RC/DC dispatch, conversion completion, a
 * reconciliation adjustment, a recorded tariff version). Read-only by design: an audit entry is
 * never edited or deleted from the UI, matching the entity's own immutability.
 */
@Component({
  selector: 'pe-audit-dashboard',
  imports: [KpiCard, DatePipe],
  templateUrl: './audit-dashboard.html',
  styleUrl: './audit-dashboard.scss',
})
export class AuditDashboard implements OnInit {
  protected readonly entries = signal<AuditEntrySummary[]>([]);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  protected readonly searchTerm = signal('');
  protected readonly entityTypeFilter = signal('');

  constructor(private readonly auditEntryService: AuditEntryService) {}

  ngOnInit(): void {
    this.load();
  }

  private load(): void {
    this.auditEntryService.list().subscribe({
      next: (entries) => {
        this.entries.set(entries);
        this.loading.set(false);
      },
      error: () => {
        this.error.set('Could not load audit entries from the API.');
        this.loading.set(false);
      },
    });
  }

  protected get entityTypes(): string[] {
    return [...new Set(this.entries().map((e) => e.entityType))].sort();
  }

  protected get filtered(): AuditEntrySummary[] {
    const term = this.searchTerm().trim().toLowerCase();
    const type = this.entityTypeFilter();
    return this.entries().filter((e) => {
      const matchesType = !type || e.entityType === type;
      const matchesTerm =
        !term ||
        e.entityId.toLowerCase().includes(term) ||
        e.action.toLowerCase().includes(term) ||
        e.actor.toLowerCase().includes(term) ||
        (e.details ?? '').toLowerCase().includes(term);
      return matchesType && matchesTerm;
    });
  }
}
