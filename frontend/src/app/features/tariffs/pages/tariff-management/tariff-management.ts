import { DatePipe, DecimalPipe } from '@angular/common';
import { Component, OnInit, computed, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { TariffService } from '../../../../core/services/tariff.service';
import { TariffChangeRequestService } from '../../../../core/services/tariff-change-request.service';
import { AuthService } from '../../../../core/services/auth.service';
import { TariffSummary } from '../../../../core/models/tariff.model';
import { TariffChangeRequestStatus, TariffChangeRequestSummary } from '../../../../core/models/tariff-change-request.model';
import { categoryLabel } from '../../../../shared/utils/category-label';
import { changeRequestStatusLabel, changeRequestStatusTone } from '../../../../shared/utils/tariff-change-request-status';
import { StatusBadge } from '../../../../shared/components/badge/status-badge';
import { KpiCard } from '../../../../shared/components/kpi-card/kpi-card';

type ChangeRequestTab = 'pending' | 'drafts' | 'scheduled' | 'rejected' | 'history';

/**
 * Tariff Management: the real, active tariff book (GET /api/v1/tariffs) plus the tariff
 * governance workflow (GET /api/v1/tariff-change-requests) that is now the only way to change
 * one — see TariffChangeRequest's doc comment. IT sees "+ New Tariff"/"Revise" actions; Utility
 * sees a "Pending Approval" count and reviews from here instead. Both roles can view every tab;
 * only the backend's RequireAuthorization policies actually gate the mutating actions.
 */
@Component({
  selector: 'pe-tariff-management',
  imports: [DecimalPipe, DatePipe, RouterLink, StatusBadge, KpiCard],
  templateUrl: './tariff-management.html',
  styleUrl: './tariff-management.scss',
})
export class TariffManagement implements OnInit {
  protected readonly tariffs = signal<TariffSummary[]>([]);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  protected readonly categoryLabel = categoryLabel;
  protected readonly statusLabel = changeRequestStatusLabel;
  protected readonly statusTone = changeRequestStatusTone;
  protected readonly Status = TariffChangeRequestStatus;

  protected readonly activeTab = signal<'active-tariffs' | ChangeRequestTab>('active-tariffs');
  protected readonly changeRequests = signal<TariffChangeRequestSummary[]>([]);
  protected readonly changeRequestsLoading = signal(true);
  protected readonly changeRequestsError = signal<string | null>(null);

  protected readonly isIt = computed(() => this.auth.role() === 'IT');
  protected readonly isUtility = computed(() => this.auth.role() === 'Utility');

  protected readonly pendingCount = computed(
    () => this.changeRequests().filter((r) => r.status === TariffChangeRequestStatus.PendingApproval).length,
  );
  protected readonly draftCount = computed(
    () => this.changeRequests().filter((r) => r.status === TariffChangeRequestStatus.Draft).length,
  );
  protected readonly scheduledCount = computed(
    () => this.changeRequests().filter((r) => r.status === TariffChangeRequestStatus.Scheduled).length,
  );
  protected readonly rejectedCount = computed(
    () => this.changeRequests().filter((r) => r.status === TariffChangeRequestStatus.Rejected).length,
  );

  protected readonly visibleChangeRequests = computed(() => {
    const tab = this.activeTab();
    const all = this.changeRequests();
    switch (tab) {
      case 'pending':
        return all.filter((r) => r.status === TariffChangeRequestStatus.PendingApproval);
      case 'drafts':
        return all.filter((r) => r.status === TariffChangeRequestStatus.Draft);
      case 'scheduled':
        return all.filter((r) => r.status === TariffChangeRequestStatus.Scheduled);
      case 'rejected':
        return all.filter((r) => r.status === TariffChangeRequestStatus.Rejected);
      case 'history':
        return all.filter(
          (r) => r.status === TariffChangeRequestStatus.Activated || r.status === TariffChangeRequestStatus.Cancelled,
        );
      default:
        return [];
    }
  });

  constructor(
    private readonly tariffService: TariffService,
    private readonly changeRequestService: TariffChangeRequestService,
    private readonly auth: AuthService,
    private readonly router: Router,
  ) {}

  ngOnInit(): void {
    this.tariffService.list().subscribe({
      next: (tariffs) => {
        this.tariffs.set(tariffs);
        this.loading.set(false);
      },
      error: () => {
        this.error.set('Could not load tariffs from the API.');
        this.loading.set(false);
      },
    });

    this.changeRequestService.list().subscribe({
      next: (requests) => {
        this.changeRequests.set(requests);
        this.changeRequestsLoading.set(false);
      },
      error: () => {
        this.changeRequestsError.set('Could not load tariff change requests from the API.');
        this.changeRequestsLoading.set(false);
      },
    });
  }

  selectTab(tab: 'active-tariffs' | ChangeRequestTab): void {
    this.activeTab.set(tab);
  }

  open(id: string): void {
    this.router.navigate(['/tariffs', id]);
  }

  openChangeRequest(id: string): void {
    this.router.navigate(['/tariffs/change-requests', id]);
  }

  reviseTariff(tariffId: string): void {
    this.router.navigate(['/tariffs/change-requests/new'], { queryParams: { supersedes: tariffId } });
  }
}
