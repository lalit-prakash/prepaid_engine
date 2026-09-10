import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';

interface ReportDefinition {
  id: string;
  title: string;
  category: string;
  description: string;
  path: string | null;
}

/**
 * Reports Center — lists every report from the original UI/UX request's mandatory-reports
 * section. Only reports with a real backing data source are clickable; the rest are shown
 * disabled with an honest reason, never built as fake pages with invented numbers. See
 * docs/frontend-scope.md for the current real-vs-planned boundary.
 */
@Component({
  selector: 'pe-reports-center',
  imports: [RouterLink],
  templateUrl: './reports-center.html',
  styleUrl: './reports-center.scss',
})
export class ReportsCenter {
  protected readonly reports: ReportDefinition[] = [
    {
      id: 'daily-billing',
      title: 'Daily Billing Report',
      category: 'Billing',
      description: 'Every bill generated, with the full charge breakdown, filterable by date range, category, and status.',
      path: '/reports/daily-billing',
    },
    {
      id: 'charge-calculation',
      title: 'Individual Charge Calculation Report',
      category: 'Financial',
      description: 'Full calculation trace for every bill belonging to one consumer.',
      path: '/reports/charge-calculation',
    },
    {
      id: 'day-wise-rc',
      title: 'Day-wise RC Report',
      category: 'RC/DC',
      description: 'Daily remote reconnection operations.',
      path: null,
    },
    {
      id: 'day-wise-dc',
      title: 'Day-wise DC Report',
      category: 'RC/DC',
      description: 'Daily remote disconnection operations.',
      path: null,
    },
    {
      id: 'postpaid-to-prepaid',
      title: 'Postpaid → Prepaid Conversion Report',
      category: 'Conversion',
      description: 'Consumers converted from postpaid to prepaid billing.',
      path: null,
    },
    {
      id: 'prepaid-to-postpaid',
      title: 'Prepaid → Postpaid Conversion Report',
      category: 'Conversion',
      description: 'Consumers converted from prepaid to postpaid billing.',
      path: null,
    },
    {
      id: 'recharge-summary',
      title: 'Day-wise Recharge Summary',
      category: 'Recharge',
      description: 'Daily recharge volumes and outcomes.',
      path: null,
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
      path: null,
    },
    {
      id: 'meter-credit-failure',
      title: 'Meter Credit Failure Report',
      category: 'Meter Credit',
      description: 'Meter credit commands that failed or timed out.',
      path: null,
    },
    {
      id: 'reconciliation',
      title: 'Reconciliation Report',
      category: 'Reconciliation',
      description: 'RMS vs. engine vs. meter amount mismatches.',
      path: null,
    },
    {
      id: 'exception',
      title: 'Exception Report',
      category: 'Exception',
      description: 'Open and resolved operational exceptions.',
      path: null,
    },
    {
      id: 'audit',
      title: 'Audit Report',
      category: 'Audit',
      description: 'Every tracked configuration and operational change.',
      path: null,
    },
    {
      id: 'tariff-change',
      title: 'Tariff Change Report',
      category: 'Tariff',
      description: 'Tariff parameter changes across versions.',
      path: null,
    },
  ];

  protected unavailableReason(report: ReportDefinition): string {
    switch (report.id) {
      case 'day-wise-rc':
      case 'day-wise-dc':
        return 'No RC/DC domain model exists yet';
      case 'postpaid-to-prepaid':
      case 'prepaid-to-postpaid':
        return 'No conversion workflow domain model exists yet';
      case 'recharge-summary':
        return 'Requires daily aggregation not yet built';
      case 'billing-failure':
        return 'No billing-failure tracking domain model exists yet';
      case 'recharge-failure':
        return 'Recharge failures are visible per-transaction in Recharge Operations, but not yet as a dedicated report';
      case 'meter-credit-failure':
        return 'No meter-command domain model exists yet';
      case 'reconciliation':
        return 'No reconciliation domain model exists yet';
      case 'exception':
        return 'No exception-tracking domain model exists yet';
      case 'audit':
        return 'No audit-trail domain model exists yet';
      case 'tariff-change':
        return 'Tariffs have no version history yet (single current version only)';
      default:
        return 'Not yet available';
    }
  }
}
