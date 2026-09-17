import { Component, HostListener, OnInit, signal, ViewChild, ElementRef } from '@angular/core';
import { Router, RouterLink, RouterLinkActive, RouterOutlet, NavigationEnd } from '@angular/router';
import { filter } from 'rxjs';
import { AuthService } from '../../core/services/auth.service';
import { NotificationService } from '../../core/services/notification.service';
import { NotificationStatus } from '../../core/models/notification.model';

interface NavItem {
  label: string;
  path: string;
  /** Modules with no backend support yet — routed to a "not yet available" stub. */
  stub?: boolean;
}

interface NavGroup {
  label: string;
  icon: string;
  items: NavItem[];
}

/**
 * The application shell: a dark-sidebar console-style layout (grouped,
 * collapsible sections) + a header with a real global search (reuses
 * Consumer List's own search via a query param — never a fabricated
 * omniscient search endpoint) + routed content. Every feature module renders
 * inside this shell so the app reads as one product rather than disconnected
 * pages.
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

  /** Only one group's items are visible at a time (accordion), matching the
   * "only the selected group should visually expand" requirement — starts
   * on whichever group contains the current route. */
  protected readonly expandedGroup = signal<string | null>(null);

  protected readonly navGroups: NavGroup[] = [
    {
      label: 'Operations',
      icon: '◱',
      items: [
        { label: 'Consumers', path: '/consumers' },
        { label: 'Recharge', path: '/recharge' },
        { label: 'Meter Operations', path: '/meter-credit' },
        { label: 'Disconnect / Reconnect', path: '/rc-dc' },
        { label: 'Service Requests', path: '/service-requests', stub: true },
        { label: 'Meter Replacements', path: '/meter-replacements' },
        { label: 'Conversion', path: '/conversion' },
        { label: 'Exceptions', path: '/exceptions' },
      ],
    },
    {
      label: 'Data & Analytics',
      icon: '⏲',
      items: [
        { label: 'Meter Data', path: '/meter-data' },
        { label: 'Billing', path: '/billing' },
        { label: 'Analytics', path: '/analytics', stub: true },
        { label: 'Reports', path: '/reports' },
        { label: 'SLA Monitoring', path: '/sla-monitoring', stub: true },
        { label: 'Billing Holds', path: '/billing-holds' },
        { label: 'Reconciliation', path: '/reconciliation' },
      ],
    },
    {
      label: 'Configuration',
      icon: '§',
      items: [
        { label: 'Tariff & Parameters', path: '/tariffs' },
        { label: 'User Management', path: '/user-management', stub: true },
        { label: 'Roles & Permissions', path: '/roles-permissions', stub: true },
        { label: 'Integrations', path: '/integrations', stub: true },
        { label: 'Calculation Workbench', path: '/calculation-workbench' },
      ],
    },
    {
      label: 'Administration',
      icon: '⚙',
      items: [
        { label: 'Audit Logs', path: '/audit' },
        { label: 'System Settings', path: '/system-settings', stub: true },
        { label: 'Notifications', path: '/notifications' },
        { label: 'System Health', path: '/system-health', stub: true },
        { label: 'Automation Center', path: '/automation', stub: true },
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
  ) {
    // Auto-expand the group containing the current route so a deep link or
    // page refresh doesn't land on a fully-collapsed sidebar.
    this.expandGroupForUrl(router.url);
    router.events.pipe(filter((e) => e instanceof NavigationEnd)).subscribe((e) => {
      this.expandGroupForUrl((e as NavigationEnd).urlAfterRedirects);
    });
  }

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

  private expandGroupForUrl(url: string): void {
    const group = this.navGroups.find((g) => g.items.some((i) => url.startsWith(i.path)));
    if (group) this.expandedGroup.set(group.label);
  }

  toggleGroup(label: string): void {
    this.expandedGroup.update((current) => (current === label ? null : label));
  }

  isGroupExpanded(label: string): boolean {
    return this.expandedGroup() === label;
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
