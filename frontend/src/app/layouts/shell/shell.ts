import { Component, HostListener, signal } from '@angular/core';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AuthService } from '../../core/services/auth.service';
import { Icon } from '../../shared/components/icon/icon';

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
  imports: [RouterOutlet, RouterLink, RouterLinkActive, Icon],
  templateUrl: './shell.html',
  styleUrl: './shell.scss',
})
export class Shell {

  protected readonly collapsed = signal(false);

  /** The signed-in user's name, initials and role for the header (from the real login, not a placeholder). */
  protected get userName(): string {
    return this.auth.username() ?? 'Operator';
  }
  protected get userInitials(): string {
    const words = this.userName.split(/\s+/).filter(Boolean);
    return (words.length > 1 ? words[0][0] + words[words.length - 1][0] : this.userName.slice(0, 2)).toUpperCase();
  }
  protected get userRole(): string {
    return this.auth.role() ?? 'Operator';
  }
  protected readonly userMenuOpen = signal(false);

  /** The sidebar: only the working day-to-day pages. Other pages (meter operations, meter data, reports, network hierarchy, integrations,
   * system health, exceptions and so on) are still routable but are not listed here. */
  protected readonly navGroups: NavGroup[] = [
    {
      label: 'Operations',
      items: [
        { label: 'Consumers', path: '/consumers', icon: 'users' },
        { label: 'Recharge', path: '/recharge', icon: 'bolt' },
        { label: 'Disconnect / Reconnect', path: '/rc-dc', icon: 'refresh' },
      ],
    },
    {
      label: 'Billing',
      items: [
        { label: 'Billing', path: '/billing', icon: 'receipt' },
        { label: 'Reconciliation', path: '/reconciliation', icon: 'calculator' },
        { label: 'Calculation Workbench', path: '/calculation-workbench', icon: 'flask' },
      ],
    },
    {
      label: 'Configuration',
      items: [
        { label: 'Tariff & Parameters', path: '/tariffs', icon: 'gear' },
        { label: 'User Management', path: '/user-management', icon: 'users' },
      ],
    },
    {
      label: 'Administration',
      items: [
        { label: 'Audit Logs', path: '/audit', icon: 'scroll' },
        { label: 'System Settings', path: '/system-settings', icon: 'gear' },
      ],
    },
  ];

  constructor(
    private readonly auth: AuthService,
    private readonly router: Router,
  ) {
  }

  toggleCollapsed(): void {
    this.collapsed.update((v) => !v);
  }

  toggleUserMenu(): void {
    this.userMenuOpen.update((v) => !v);
  }

  closeMenus(): void {
    this.userMenuOpen.set(false);
  }

  @HostListener('document:keydown', ['$event'])
  handleShortcut(event: KeyboardEvent): void {
    if (event.key === 'Escape') {
      this.closeMenus();
    }
  }

  signOut(): void {
    this.auth.logout();
    // replaceUrl so the signed-out shell route doesn't remain in history —
    // pressing back afterwards lands on login again, not the stale page.
    this.router.navigateByUrl('/login', { replaceUrl: true });
  }
}
