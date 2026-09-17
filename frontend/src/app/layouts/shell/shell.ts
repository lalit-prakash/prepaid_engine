import { Component, HostListener, OnInit, signal, ViewChild, ElementRef } from '@angular/core';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AuthService } from '../../core/services/auth.service';
import { NotificationService } from '../../core/services/notification.service';
import { NotificationStatus } from '../../core/models/notification.model';

interface NavItem {
  label: string;
  path: string;
  icon: string;
  /** Modules with no backend support yet — routed to a "not yet available" stub. */
  stub?: boolean;
}

interface NavGroup {
  label: string;
  items: NavItem[];
}

/**
 * The application shell: a dark-sidebar console-style layout with a flat,
 * always-expanded nav (grouped under section labels, no accordion/tree) + a
 * header with a real global search (reuses Consumer List's own search via a
 * query param — never a fabricated omniscient search endpoint) + routed
 * content. Every feature module renders inside this shell so the app reads
 * as one product rather than disconnected pages.
 *
 * Nav groups are shaped to match a provided visual reference; every group
 * leads with the reference's own item names (routed to a real page where one
 * exists, or an honest module-stub where it doesn't — never a fabricated
 * dashboard), then keeps every pre-existing real module reachable afterward
 * so nothing this app already built is ever removed from navigation.
 */
@Component({
  selector: 'pe-shell',
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  templateUrl: './shell.html',
  styleUrl: './shell.scss',
})
export class Shell implements OnInit {
  @ViewChild('searchInput') searchInputRef?: ElementRef<HTMLInputElement>;

  protected readonly collapsed = signal(false);
  protected readonly userMenuOpen = signal(false);
  protected readonly quickActionsOpen = signal(false);
  protected readonly pendingNotificationCount = signal(0);

  protected readonly navGroups: NavGroup[] = [
    {
      label: 'Operations',
      items: [
        { label: 'Consumers', path: '/consumers', icon: '👤' },
        { label: 'Recharge', path: '/recharge', icon: '⚡' },
        { label: 'Meter Operations', path: '/meter-credit', icon: '🔌' },
        { label: 'Disconnect / Reconnect', path: '/rc-dc', icon: '🔄' },
        { label: 'Service Requests', path: '/service-requests', icon: '🛠', stub: true },
        { label: 'Meter Replacements', path: '/meter-replacements', icon: '🧰' },
        { label: 'Conversion', path: '/conversion', icon: '🔁' },
        { label: 'Exceptions', path: '/exceptions', icon: '❗' },
      ],
    },
    {
      label: 'Data & Analytics',
      items: [
        { label: 'Meter Data', path: '/meter-data', icon: '📈' },
        { label: 'Billing', path: '/billing', icon: '🧾' },
        { label: 'Analytics', path: '/analytics', icon: '📊', stub: true },
        { label: 'Reports', path: '/reports', icon: '📄' },
        { label: 'SLA Monitoring', path: '/sla-monitoring', icon: '⏱', stub: true },
        { label: 'Billing Holds', path: '/billing-holds', icon: '⏸' },
        { label: 'Reconciliation', path: '/reconciliation', icon: '🧮' },
      ],
    },
    {
      label: 'Configuration',
      items: [
        { label: 'Tariff & Parameters', path: '/tariffs', icon: '⚙️' },
        { label: 'User Management', path: '/user-management', icon: '👥', stub: true },
        { label: 'Roles & Permissions', path: '/roles-permissions', icon: '🔐', stub: true },
        { label: 'Integrations', path: '/integrations', icon: '🔗', stub: true },
        { label: 'Calculation Workbench', path: '/calculation-workbench', icon: '🧪' },
      ],
    },
    {
      label: 'Administration',
      items: [
        { label: 'Audit Logs', path: '/audit', icon: '📜' },
        { label: 'System Settings', path: '/system-settings', icon: '🛠️', stub: true },
        { label: 'Notifications', path: '/notifications', icon: '🔔' },
        { label: 'System Health', path: '/system-health', icon: '💚', stub: true },
        { label: 'Automation Center', path: '/automation', icon: '🤖', stub: true },
      ],
    },
  ];

  protected readonly quickActions = [
    { label: 'Recharge Consumer', path: '/recharge' },
    { label: 'View Consumers', path: '/consumers' },
    { label: 'View Meter Data', path: '/meter-data' },
    { label: 'Meter Replacement', path: '/meter-replacements' },
    { label: 'Generate Daily Billing', path: '/meter-data' },
  ];

  constructor(
    private readonly auth: AuthService,
    private readonly notificationService: NotificationService,
    private readonly router: Router,
  ) {}

  ngOnInit(): void {
    // Real pending-notification count for the header bell badge — never a
    // fabricated number; if the call fails, the badge simply stays at 0
    // rather than showing something invented.
    this.notificationService.list().subscribe({
      next: (notifications) => {
        this.pendingNotificationCount.set(
          notifications.filter((n) => n.status === NotificationStatus.Pending).length,
        );
      },
      error: () => this.pendingNotificationCount.set(0),
    });
  }

  /** Reuses Consumer List's own real search (via a query param it reads on
   * load) rather than a fabricated omniscient search endpoint — an account
   * number pattern (e.g. "DEMO-0001") jumps straight to Consumer 360. */
  submitSearch(term: string): void {
    const query = term.trim();
    if (!query) return;

    if (/^[A-Za-z]+-\d+$/.test(query)) {
      this.router.navigate(['/consumers', query.toUpperCase()]);
    } else {
      this.router.navigate(['/consumers'], { queryParams: { q: query } });
    }
  }

  toggleCollapsed(): void {
    this.collapsed.update((v) => !v);
  }

  toggleUserMenu(): void {
    this.userMenuOpen.update((v) => !v);
    this.quickActionsOpen.set(false);
  }

  toggleQuickActions(): void {
    this.quickActionsOpen.update((v) => !v);
    this.userMenuOpen.set(false);
  }

  closeMenus(): void {
    this.userMenuOpen.set(false);
    this.quickActionsOpen.set(false);
  }

  @HostListener('document:keydown', ['$event'])
  handleShortcut(event: KeyboardEvent): void {
    if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 'k') {
      event.preventDefault();
      this.searchInputRef?.nativeElement.focus();
    }
    if (event.key === 'Escape') {
      this.closeMenus();
    }
  }

  signOut(): void {
    this.auth.signOut();
  }
}
