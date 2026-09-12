import { DatePipe } from '@angular/common';
import { Component, OnInit, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { NotificationService } from '../../../../core/services/notification.service';
import { NotificationEventType, NotificationStatus, NotificationSummary } from '../../../../core/models/notification.model';
import { StatusBadge } from '../../../../shared/components/badge/status-badge';
import { KpiCard } from '../../../../shared/components/kpi-card/kpi-card';

/**
 * Real, API-backed Notification History dashboard (GET /api/v1/notifications) — every consumer
 * notification queued automatically during hourly/daily LS/DLP billing processing (low balance,
 * emergency credit, disconnection eligibility, provisional-billing data-quality hold). Never
 * hand-entered. This project has no real SMS gateway — a `Sent` status here means "a real
 * dispatcher would pick this up next", never that an SMS actually left this system. Read-only by
 * design: there is no action to take on a notification from this page.
 */
@Component({
  selector: 'pe-notifications-dashboard',
  imports: [StatusBadge, KpiCard, DatePipe, RouterLink],
  templateUrl: './notifications-dashboard.html',
  styleUrl: './notifications-dashboard.scss',
})
export class NotificationsDashboard implements OnInit {
  protected readonly notifications = signal<NotificationSummary[]>([]);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  protected readonly searchTerm = signal('');
  protected readonly eventTypeFilter = signal('');
  protected readonly NotificationEventType = NotificationEventType;
  protected readonly NotificationStatus = NotificationStatus;

  constructor(private readonly notificationService: NotificationService) {}

  ngOnInit(): void {
    this.load();
  }

  private load(): void {
    this.notificationService.list().subscribe({
      next: (notifications) => {
        this.notifications.set(notifications);
        this.loading.set(false);
      },
      error: () => {
        this.error.set('Could not load notifications from the API.');
        this.loading.set(false);
      },
    });
  }

  protected get filtered(): NotificationSummary[] {
    const term = this.searchTerm().trim().toLowerCase();
    const type = this.eventTypeFilter();
    return this.notifications().filter((n) => {
      const matchesType = !type || String(n.eventType) === type;
      const matchesTerm =
        !term ||
        n.accountNumber.toLowerCase().includes(term) ||
        n.name.toLowerCase().includes(term) ||
        n.message.toLowerCase().includes(term);
      return matchesType && matchesTerm;
    });
  }

  protected get pendingCount(): number {
    return this.notifications().filter((n) => n.status === NotificationStatus.Pending).length;
  }
  protected get sentCount(): number {
    return this.notifications().filter((n) => n.status === NotificationStatus.Sent).length;
  }
  protected get failedCount(): number {
    return this.notifications().filter((n) => n.status === NotificationStatus.Failed).length;
  }

  protected eventTypeLabel(type: NotificationEventType): string {
    switch (type) {
      case NotificationEventType.LowBalance: return 'Low Balance';
      case NotificationEventType.EmergencyCredit: return 'Emergency Credit';
      case NotificationEventType.DisconnectionEligible: return 'Disconnection Eligible';
      case NotificationEventType.BillingProvisional: return 'Billing Provisional';
      default: return 'Unknown';
    }
  }
}
