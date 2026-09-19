import { Routes } from '@angular/router';
import { authGuard } from './core/guards/auth.guard';
import { unsavedChangesGuard } from './core/guards/unsaved-changes.guard';

export const routes: Routes = [
  {
    path: 'login',
    loadComponent: () => import('./features/auth/pages/login/login').then((m) => m.Login),
  },
  {
    path: '',
    loadComponent: () => import('./layouts/shell/shell').then((m) => m.Shell),
    canActivate: [authGuard],
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'overview' },
      {
        path: 'overview',
        loadComponent: () => import('./features/overview/pages/overview/overview').then((m) => m.Overview),
      },
      {
        path: 'consumers',
        loadComponent: () =>
          import('./features/consumers/pages/consumer-list/consumer-list').then((m) => m.ConsumerList),
      },
      {
        path: 'consumers/:accountNumber',
        loadComponent: () =>
          import('./features/consumers/pages/consumer-360/consumer-360').then((m) => m.Consumer360),
      },
      {
        path: 'billing',
        loadComponent: () =>
          import('./features/billing/pages/billing-dashboard/billing-dashboard').then((m) => m.BillingDashboard),
      },
      {
        path: 'billing/:id',
        loadComponent: () =>
          import('./features/billing/pages/bill-detail/bill-detail').then((m) => m.BillDetail),
      },
      {
        path: 'recharge',
        loadComponent: () =>
          import('./features/recharge/pages/recharge-dashboard/recharge-dashboard').then((m) => m.RechargeDashboard),
      },
      {
        path: 'recharge/:id',
        loadComponent: () =>
          import('./features/recharge/pages/recharge-detail/recharge-detail').then((m) => m.RechargeDetail),
      },
      {
        path: 'meter-credit',
        loadComponent: () =>
          import('./features/meter-credit/pages/meter-credit-dashboard/meter-credit-dashboard').then(
            (m) => m.MeterCreditDashboard,
          ),
      },
      {
        path: 'meter-credit/:id',
        loadComponent: () =>
          import('./features/meter-credit/pages/meter-credit-detail/meter-credit-detail').then(
            (m) => m.MeterCreditDetail,
          ),
      },
      {
        path: 'rc-dc',
        loadComponent: () =>
          import('./features/rc-dc/pages/rc-dc-dashboard/rc-dc-dashboard').then((m) => m.RcDcDashboard),
      },
      {
        path: 'rc-dc/:id',
        loadComponent: () =>
          import('./features/rc-dc/pages/rc-dc-detail/rc-dc-detail').then((m) => m.RcDcDetail),
      },
      {
        path: 'conversion',
        loadComponent: () =>
          import('./features/conversion/pages/conversion-dashboard/conversion-dashboard').then(
            (m) => m.ConversionDashboard,
          ),
      },
      {
        path: 'exceptions',
        loadComponent: () =>
          import('./features/exceptions/pages/exceptions-dashboard/exceptions-dashboard').then(
            (m) => m.ExceptionsDashboard,
          ),
      },
      {
        path: 'reconciliation',
        loadComponent: () =>
          import('./features/reconciliation/pages/reconciliation-dashboard/reconciliation-dashboard').then(
            (m) => m.ReconciliationDashboard,
          ),
      },
      {
        path: 'tariffs',
        loadComponent: () =>
          import('./features/tariffs/pages/tariff-management/tariff-management').then((m) => m.TariffManagement),
      },
      {
        path: 'tariffs/change-requests/new',
        canDeactivate: [unsavedChangesGuard],
        loadComponent: () =>
          import('./features/tariffs/pages/tariff-change-request-form/tariff-change-request-form').then(
            (m) => m.TariffChangeRequestForm,
          ),
      },
      {
        path: 'tariffs/change-requests/:id',
        loadComponent: () =>
          import('./features/tariffs/pages/tariff-change-request-detail/tariff-change-request-detail').then(
            (m) => m.TariffChangeRequestDetailPage,
          ),
      },
      {
        path: 'tariffs/change-requests/:id/edit',
        canDeactivate: [unsavedChangesGuard],
        loadComponent: () =>
          import('./features/tariffs/pages/tariff-change-request-form/tariff-change-request-form').then(
            (m) => m.TariffChangeRequestForm,
          ),
      },
      {
        path: 'tariffs/:id',
        loadComponent: () =>
          import('./features/tariffs/pages/tariff-detail/tariff-detail').then((m) => m.TariffDetail),
      },
      {
        path: 'calculation-workbench',
        loadComponent: () =>
          import('./features/calculation-workbench/pages/workbench/workbench').then((m) => m.Workbench),
      },
      {
        path: 'reports',
        loadComponent: () =>
          import('./features/reports/pages/reports-center/reports-center').then((m) => m.ReportsCenter),
      },
      {
        path: 'reports/daily-billing',
        pathMatch: 'full',
        redirectTo: 'reports/view/daily-billing',
      },
      {
        path: 'reports/view/:id',
        loadComponent: () =>
          import('./features/reports/pages/report-viewer/report-viewer').then((m) => m.ReportViewer),
      },
      {
        path: 'reports/charge-calculation',
        loadComponent: () =>
          import('./features/reports/pages/charge-calculation-report/charge-calculation-report').then(
            (m) => m.ChargeCalculationReport,
          ),
      },
      ...stubRoute('automation', 'Automation Center'),
      {
        path: 'audit',
        loadComponent: () =>
          import('./features/audit/pages/audit-dashboard/audit-dashboard').then((m) => m.AuditDashboard),
      },
      {
        path: 'billing-holds',
        loadComponent: () =>
          import('./features/billing-holds/pages/billing-holds-dashboard/billing-holds-dashboard').then(
            (m) => m.BillingHoldsDashboard,
          ),
      },
      {
        path: 'notifications',
        loadComponent: () =>
          import('./features/notifications/pages/notifications-dashboard/notifications-dashboard').then(
            (m) => m.NotificationsDashboard,
          ),
      },
      {
        path: 'meter-replacements',
        loadComponent: () =>
          import('./features/meter-replacements/pages/meter-replacements-dashboard/meter-replacements-dashboard').then(
            (m) => m.MeterReplacementsDashboard,
          ),
      },
      {
        path: 'meter-data',
        loadComponent: () =>
          import('./features/meter-data/pages/meter-data-dashboard/meter-data-dashboard').then(
            (m) => m.MeterDataDashboard,
          ),
      },
      ...stubRoute('system-health', 'System Health'),
      ...stubRoute('service-requests', 'Service Requests'),
      ...stubRoute('analytics', 'Analytics'),
      {
        path: 'sla-monitoring',
        loadComponent: () =>
          import('./features/sla-monitoring/pages/sla-monitoring-dashboard/sla-monitoring-dashboard').then(
            (m) => m.SlaMonitoringDashboard,
          ),
      },
      ...stubRoute('user-management', 'User Management'),
      ...stubRoute('roles-permissions', 'Roles & Permissions'),
      ...stubRoute('integrations', 'Integrations'),
      ...stubRoute('system-settings', 'System Settings'),
      { path: '**', redirectTo: 'overview' },
    ],
  },
];

/** A routed placeholder for a module not yet backed by the API — see ModuleStub. */
function stubRoute(path: string, title: string): Routes {
  return [
    {
      path,
      loadComponent: () => import('./features/module-stub/module-stub').then((m) => m.ModuleStub),
      data: { title },
    },
  ];
}
