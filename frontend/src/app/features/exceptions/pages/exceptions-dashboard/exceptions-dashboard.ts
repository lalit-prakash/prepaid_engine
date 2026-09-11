import { DatePipe } from '@angular/common';
import { Component, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { OperationalExceptionService } from '../../../../core/services/operational-exception.service';
import {
  OperationalExceptionSourceType,
  OperationalExceptionStatus,
  OperationalExceptionSummary,
} from '../../../../core/models/operational-exception.model';
import { StatusBadge } from '../../../../shared/components/badge/status-badge';
import { KpiCard } from '../../../../shared/components/kpi-card/kpi-card';

/**
 * Real, API-backed Exceptions dashboard (GET /api/v1/exceptions) — every operational exception,
 * auto-raised the moment a MeterCommand or ConnectivityCommand reaches Failed/TimedOut. Never
 * hand-entered, so nothing that needs operator attention can go unlisted. Resolving is a genuine
 * action gated behind a mandatory note, matching this project's mandatory-reason discipline.
 */
@Component({
  selector: 'pe-exceptions-dashboard',
  imports: [StatusBadge, KpiCard, DatePipe, FormsModule, RouterLink],
  templateUrl: './exceptions-dashboard.html',
  styleUrl: './exceptions-dashboard.scss',
})
export class ExceptionsDashboard implements OnInit {
  protected readonly exceptions = signal<OperationalExceptionSummary[]>([]);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  protected readonly searchTerm = signal('');
  protected readonly OperationalExceptionStatus = OperationalExceptionStatus;
  protected readonly OperationalExceptionSourceType = OperationalExceptionSourceType;

  protected readonly resolvingId = signal<string | null>(null);
  protected readonly resolutionNote = signal('');
  protected readonly resolveError = signal<string | null>(null);
  protected readonly resolveSubmitting = signal(false);

  constructor(private readonly exceptionService: OperationalExceptionService) {}

  ngOnInit(): void {
    this.load();
  }

  private load(): void {
    this.exceptionService.list().subscribe({
      next: (exceptions) => {
        this.exceptions.set(exceptions);
        this.loading.set(false);
      },
      error: () => {
        this.error.set('Could not load operational exceptions from the API.');
        this.loading.set(false);
      },
    });
  }

  protected get filtered(): OperationalExceptionSummary[] {
    const term = this.searchTerm().trim().toLowerCase();
    if (!term) return this.exceptions();
    return this.exceptions().filter(
      (e) => e.accountNumber.toLowerCase().includes(term) || e.name.toLowerCase().includes(term),
    );
  }

  protected get openCount(): number {
    return this.exceptions().filter((e) => e.status === OperationalExceptionStatus.Open).length;
  }
  protected get resolvedCount(): number {
    return this.exceptions().filter((e) => e.status === OperationalExceptionStatus.Resolved).length;
  }

  requestResolve(id: string): void {
    this.resolvingId.set(id);
    this.resolutionNote.set('');
    this.resolveError.set(null);
  }

  cancelResolve(): void {
    this.resolvingId.set(null);
  }

  confirmResolve(): void {
    const id = this.resolvingId();
    const note = this.resolutionNote().trim();
    if (!id) return;
    if (!note) {
      this.resolveError.set('A resolution note is required.');
      return;
    }

    this.resolveSubmitting.set(true);
    this.exceptionService.resolve(id, note).subscribe({
      next: () => {
        this.resolveSubmitting.set(false);
        this.resolvingId.set(null);
        this.load();
      },
      error: (err) => {
        this.resolveSubmitting.set(false);
        this.resolveError.set(err?.error?.error ?? 'Could not resolve this exception.');
      },
    });
  }
}
