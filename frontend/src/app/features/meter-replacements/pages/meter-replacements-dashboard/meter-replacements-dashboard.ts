import { DatePipe, DecimalPipe } from '@angular/common';
import { Component, OnInit, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { MeterReplacementService } from '../../../../core/services/meter-replacement.service';
import { MeterAssignmentEventType, MeterReplacementSummary } from '../../../../core/models/meter-replacement.model';
import { StatusBadge } from '../../../../shared/components/badge/status-badge';
import { KpiCard } from '../../../../shared/components/kpi-card/kpi-card';

/**
 * Real, API-backed Meter Replacement History dashboard (GET /api/v1/meter-replacements) — the
 * audit trail (`MeterAssignment`) that exists specifically so an old meter's cumulative reading
 * is never compared against a new meter's cumulative reading — they're different physical
 * meters. Read-only by design: a replacement is a permanent audit record, never edited here.
 */
@Component({
  selector: 'pe-meter-replacements-dashboard',
  imports: [StatusBadge, KpiCard, DecimalPipe, DatePipe, RouterLink],
  templateUrl: './meter-replacements-dashboard.html',
  styleUrl: './meter-replacements-dashboard.scss',
})
export class MeterReplacementsDashboard implements OnInit {
  protected readonly replacements = signal<MeterReplacementSummary[]>([]);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  protected readonly searchTerm = signal('');
  protected readonly MeterAssignmentEventType = MeterAssignmentEventType;

  constructor(private readonly meterReplacementService: MeterReplacementService) {}

  ngOnInit(): void {
    this.load();
  }

  private load(): void {
    this.meterReplacementService.list().subscribe({
      next: (replacements) => {
        this.replacements.set(replacements);
        this.loading.set(false);
      },
      error: () => {
        this.error.set('Could not load meter replacement history from the API.');
        this.loading.set(false);
      },
    });
  }

  protected get filtered(): MeterReplacementSummary[] {
    const term = this.searchTerm().trim().toLowerCase();
    if (!term) return this.replacements();
    return this.replacements().filter(
      (r) =>
        r.accountNumber.toLowerCase().includes(term) ||
        r.name.toLowerCase().includes(term) ||
        r.newMeterNumber.toLowerCase().includes(term) ||
        (r.oldMeterNumber ?? '').toLowerCase().includes(term),
    );
  }

  protected get replacedCount(): number {
    return this.replacements().filter((r) => r.eventType === MeterAssignmentEventType.Replaced).length;
  }
  protected get installedCount(): number {
    return this.replacements().filter((r) => r.eventType === MeterAssignmentEventType.Installed).length;
  }

  protected eventTypeLabel(type: MeterAssignmentEventType): string {
    switch (type) {
      case MeterAssignmentEventType.Installed: return 'Installed';
      case MeterAssignmentEventType.Removed: return 'Removed';
      default: return 'Replaced';
    }
  }
}
