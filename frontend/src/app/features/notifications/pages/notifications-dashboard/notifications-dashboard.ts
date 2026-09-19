import { DatePipe, DecimalPipe } from '@angular/common';
import { Component, OnDestroy, OnInit, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Subject, Subscription, debounceTime } from 'rxjs';
import { NotificationService } from '../../../../core/services/notification.service';
import {
  NotificationEventType,
  NotificationStatus,
  NotificationSummary,
  NotificationSummaryStats,
} from '../../../../core/models/notification.model';
import { StatusBadge } from '../../../../shared/components/badge/status-badge';
import { KpiCard } from '../../../../shared/components/kpi-card/kpi-card';
import { PagedList } from '../../../../shared/utils/paged-list';

const PAGE_SIZE = 25;

/**
 * Notification History. KPIs come from GET /api/v1/notifications/summary (counted by the database) and the table from
 * the keyset-paged GET /api/v1/notifications/search, so the browser never holds more than one page. Read-only: every
 * notification was queued automatically by billing, conversion or recharge processing.
 */
@Component({
  selector: 'pe-notifications-dashboard',
  imports: [StatusBadge, KpiCard, DatePipe, DecimalPipe, RouterLink],
  templateUrl: './notifications-dashboard.html',
  styleUrl: './notifications-dashboard.scss',
})
export class NotificationsDashboard implements OnInit, OnDestroy {
  protected readonly searchTerm = signal('');
  protected readonly eventTypeFilter = signal<NotificationEventType | null>(null);
  protected readonly statusFilter = signal<NotificationStatus | null>(null);
  protected readonly stats = signal<NotificationSummaryStats | null>(null);
  protected readonly statsError = signal(false);
  protected readonly NotificationEventType = NotificationEventType;
  protected readonly NotificationStatus = NotificationStatus;

  protected readonly list = new PagedList<NotificationSummary>(
    (after) =>
      this.notificationService.search({ q: this.searchTerm(), eventType: this.eventTypeFilter(), status: this.statusFilter(), after, pageSize: PAGE_SIZE }),
    'Could not load notifications from the API.',
  );

  private readonly search$ = new Subject<string>();
  private searchSub?: Subscription;

  constructor(private readonly notificationService: NotificationService) {}

  ngOnInit(): void {
    this.searchSub = this.search$.pipe(debounceTime(300)).subscribe((term) => {
      if (term === this.searchTerm()) return;
      this.searchTerm.set(term);
      this.list.reload();
    });
    this.notificationService.summary().subscribe({ next: (s) => this.stats.set(s), error: () => this.statsError.set(true) });
    this.list.load();
  }

  ngOnDestroy(): void {
    this.searchSub?.unsubscribe();
    this.list.destroy();
  }

  protected onSearchInput(term: string): void {
    this.search$.next(term);
  }

  protected onEventTypeChange(raw: string): void {
    this.eventTypeFilter.set(raw === '' ? null : (Number(raw) as NotificationEventType));
    this.list.reload();
  }

  protected filterByStatus(status: NotificationStatus | null): void {
    this.statusFilter.set(status);
    this.list.reload();
  }

  protected clearFilters(): void {
    this.searchTerm.set('');
    this.eventTypeFilter.set(null);
    this.statusFilter.set(null);
    this.list.reload();
  }

  protected get hasActiveFilters(): boolean {
    return !!this.searchTerm().trim() || this.eventTypeFilter() !== null || this.statusFilter() !== null;
  }

  protected eventTypeLabel(type: NotificationEventType): string {
    switch (type) {
      case NotificationEventType.LowBalance: return 'Low Balance';
      case NotificationEventType.EmergencyCredit: return 'Emergency Credit';
      case NotificationEventType.DisconnectionEligible: return 'Disconnection Eligible';
      case NotificationEventType.BillingProvisional: return 'Billing Provisional';
      case NotificationEventType.PrepaidConversionCompleted: return 'Prepaid Conversion Completed';
      case NotificationEventType.AutoDisconnected: return 'Auto-Disconnected';
      case NotificationEventType.AutoReconnected: return 'Auto-Reconnected';
      default: return 'Unknown';
    }
  }
}
