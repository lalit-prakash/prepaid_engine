import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';

interface ReportDefinition {
  id: string;
  title: string;
  category: string;
  description: string;
  path: string | null;
  /** 'report': a dedicated report page with date/status filters and CSV export.
   *  'dashboard': links out to that module's real (non-report) dashboard — real data, but
   *  without a report page's filtering/export — the card's CTA reflects this distinction so
   *  "Run Report" is never shown for a page that doesn't actually behave like one. */
  linkKind?: 'report' | 'dashboard';
}

/**
 * Reports Center — lists every report from the original UI/UX request's mandatory-reports
 * section. Cards link to a real dedicated report page where one exists, or to that module's
 * real dashboard where the underlying data exists but no dedicated report page has been built
 * yet; only cards with neither are shown disabled with an honest reason — never a fake page
 * with invented numbers. See docs/frontend-scope.md for the current real-vs-planned boundary.
 */
@Component({
  selector: 'pe-reports-center',
  imports: [RouterLink],
  templateUrl: './reports-center.html',
  styleUrl: './reports-center.scss',
})
export class ReportsCenter {
  protected get reportPageCount(): number {
    return this.reports.filter((r) => r.linkKind === 'report').length;
  }

  protected get enabledCount(): number {
    return this.reports.filter((r) => r.path).length;
  }

  protected readonly reports: ReportDefinition[] = [
    {
      id: 'daily-billing',
      title: 'Daily Billing Report',
      category: 'Billing',
      description: 'Every bill generated, with the full charge breakdown and totals, filterable by date range and status.',
      path: '/reports/view/daily-billing',
      linkKind: 'report',
    },
    {
      id: 'charge-calculation',
      title: 'Individual Charge Calculation Report',
      category: 'Financial',
      description: 'Full calculation trace for every bill belonging to one consumer.',
      path: '/reports/charge-calculation',
      linkKind: 'report',
    },
    {
      id: 'day-wise-rc',
      title: 'Day-wise RC Report',
      category: 'RC/DC',
      description: 'Daily remote reconnection operations and their outcomes.',
      path: '/reports/view/day-wise-rc',
      linkKind: 'report',
    },
    {
      id: 'day-wise-dc',
      title: 'Day-wise DC Report',
      category: 'RC/DC',
      description: 'Daily remote disconnection operations and their outcomes.',
      path: '/reports/view/day-wise-dc',
      linkKind: 'report',
    },
    {
      id: 'postpaid-to-prepaid',
      title: 'Postpaid → Prepaid Conversion Report',
      category: 'Conversion',
      description: 'Every RMS postpaid→prepaid conversion request and its decision trail — opens the real Conversion dashboard (not yet a dedicated date-filterable report page).',
      path: '/conversion',
      linkKind: 'dashboard',
    },
    {
      id: 'prepaid-to-postpaid',
      title: 'Prepaid → Postpaid Conversion Report',
      category: 'Conversion',
      description: 'The reverse direction (prepaid back to postpaid) has no domain model — every ConversionRequest this project handles is a postpaid→prepaid request per the AMISP spec.',
      path: null,
    },
    {
      id: 'recharge-summary',
      title: 'Day-wise Recharge Summary',
      category: 'Recharge',
      description: 'Daily recharge volumes, with payment outcome and meter-credit outcome shown separately.',
      path: '/reports/view/day-wise-recharge',
      linkKind: 'report',
    },
    {
      id: 'billing-failure',
      title: 'Billing Failure Report',
      category: 'Billing',
      description: 'Bills that failed to generate or validate.',
      path: null,
    },
    {
      id: 'recharge-failure',
      title: 'Recharge Failure Report',
      category: 'Recharge',
      description: 'Recharge attempts that failed or were declined.',
      path: '/reports/view/recharge-failure',
      linkKind: 'report',
    },
    {
      id: 'meter-credit-failure',
      title: 'Meter Credit Failure Report',
      category: 'Meter Credit',
      description: 'Meter credit commands that failed or timed out after payment was received.',
      path: '/reports/view/meter-credit-failure',
      linkKind: 'report',
    },
    {
      id: 'reconciliation',
      title: 'Reconciliation Report',
      category: 'Reconciliation',
      description: 'Every applied reconciliation adjustment (RMS vs. engine amount mismatches) — opens the real Reconciliation dashboard (not yet a dedicated date-filterable report page).',
      path: '/reconciliation',
      linkKind: 'dashboard',
    },
    {
      id: 'exception',
      title: 'Exception Report',
      category: 'Exception',
      description: 'Every auto-raised operational exception, open and resolved — opens the real Exceptions dashboard (not yet a dedicated date-filterable report page).',
      path: '/exceptions',
      linkKind: 'dashboard',
    },
    {
      id: 'audit',
      title: 'Audit Report',
      category: 'Audit',
      description: 'The immutable audit log of tracked configuration and operational changes — opens the real Audit Activity page (not yet a dedicated date-filterable report page).',
      path: '/audit',
      linkKind: 'dashboard',
    },
    {
      id: 'tariff-change',
      title: 'Tariff Change Report',
      category: 'Tariff',
      description: 'Recorded tariff parameter changes — each tariff’s own Version History is available from its detail page (not yet a single cross-tariff report view).',
      path: '/tariffs',
      linkKind: 'dashboard',
    },
  ];

  protected unavailableReason(report: ReportDefinition): string {
    switch (report.id) {
      case 'prepaid-to-postpaid':
        return 'No reverse-conversion domain model exists — every ConversionRequest handled here is postpaid→prepaid';
      case 'billing-failure':
        return 'No billing-failure tracking domain model exists yet — a rejected bill has no distinct failure record';
      default:
        return 'Not yet available';
    }
  }
}
