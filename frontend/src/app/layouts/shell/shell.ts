import { Component, HostListener, signal, ViewChild, ElementRef } from '@angular/core';
import { Router, RouterLink, RouterLinkActive, RouterOutlet, NavigationEnd } from '@angular/router';
import { filter } from 'rxjs';
import { AuthService } from '../../core/services/auth.service';

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
 * The application shell: a light SaaS-style sidebar (grouped, collapsible
 * sections rather than one long flat list of 19 top-level links) + a header
 * with a real global search (reuses Consumer List's own search via a query
 * param — never a fabricated omniscient search endpoint) + routed content.
 * Every feature module renders inside this shell so the app reads as one
 * product rather than disconnected pages.
 */
@Component({
  selector: 'pe-shell',
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  templateUrl: './shell.html',
  styleUrl: './shell.scss',
})
export class Shell {
  @ViewChild('searchInput') searchInputRef?: ElementRef<HTMLInputElement>;

  protected readonly collapsed = signal(false);
  protected readonly userMenuOpen = signal(false);
  protected readonly quickActionsOpen = signal(false);

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
        { label: 'Billing', path: '/billing' },
        { label: 'Recharges', path: '/recharge' },
        { label: 'Meter Credit', path: '/meter-credit' },
        { label: 'RC / DC', path: '/rc-dc' },
        { label: 'Meter Replacements', path: '/meter-replacements' },
        { label: 'Conversion', path: '/conversion' },
        { label: 'Exceptions', path: '/exceptions' },
      ],
    },
    {
      label: 'Finance',
      icon: '₹',
      items: [{ label: 'Reconciliation', path: '/reconciliation' }],
    },
    {
      label: 'Meter Data',
      icon: '⏲',
      items: [
        { label: 'Daily Load Profile', path: '/meter-data' },
        { label: 'Billing Holds', path: '/billing-holds' },
      ],
    },
    {
      label: 'Configuration',
      icon: '§',
      items: [
        { label: 'Tariffs & Rules', path: '/tariffs' },
        { label: 'Calculation Workbench', path: '/calculation-workbench' },
      ],
    },
    {
      label: 'Reporting',
      icon: '▤',
      items: [{ label: 'Reports', path: '/reports' }],
    },
    {
      label: 'Administration',
      icon: '⚙',
      items: [
        { label: 'Notifications', path: '/notifications' },
        { label: 'Audit Activity', path: '/audit' },
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
    private readonly router: Router,
  ) {
    // Auto-expand the group containing the current route so a deep link or
    // page refresh doesn't land on a fully-collapsed sidebar.
    this.expandGroupForUrl(router.url);
    router.events.pipe(filter((e) => e instanceof NavigationEnd)).subscribe((e) => {
      this.expandGroupForUrl((e as NavigationEnd).urlAfterRedirects);
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
