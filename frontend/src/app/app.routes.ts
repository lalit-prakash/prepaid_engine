import { Routes } from '@angular/router';
import { authGuard } from './core/guards/auth.guard';

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
      ...stubRoute('meter-credit', 'Meter Credit'),
      ...stubRoute('rc-dc', 'RC / DC'),
      ...stubRoute('conversion', 'Conversion'),
      ...stubRoute('exceptions', 'Exceptions'),
      ...stubRoute('reconciliation', 'Reconciliation'),
      ...stubRoute('tariffs', 'Tariffs & Rules'),
      ...stubRoute('calculation-workbench', 'Calculation Workbench'),
      ...stubRoute('reports', 'Reports'),
      ...stubRoute('automation', 'Automation Center'),
      ...stubRoute('audit', 'Audit & Activity'),
      ...stubRoute('system-health', 'System Health'),
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
