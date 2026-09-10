import { Component, signal } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AuthService } from '../../core/services/auth.service';

interface NavItem {
  label: string;
  path: string;
  icon: string;
  /** Modules with no backend support yet — routed to a "not yet available" stub. */
  stub?: boolean;
}

/**
 * The application shell: collapsible sidebar + top header + routed content.
 * Every feature module renders inside this shell so the app reads as one
 * product (README §100 "Visual consistency") rather than disconnected pages.
 */
@Component({
  selector: 'pe-shell',
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  templateUrl: './shell.html',
  styleUrl: './shell.scss',
})
export class Shell {
  protected readonly collapsed = signal(false);

  protected readonly navItems: NavItem[] = [
    { label: 'Overview', path: '/overview', icon: '◱' },
    { label: 'Consumers', path: '/consumers', icon: '☰' },
    { label: 'Billing', path: '/billing', icon: '▦' },
    { label: 'Recharge Operations', path: '/recharge', icon: '⇄' },
    { label: 'Meter Credit', path: '/meter-credit', icon: '◈', stub: true },
    { label: 'RC / DC', path: '/rc-dc', icon: '⏻', stub: true },
    { label: 'Conversion', path: '/conversion', icon: '⇌', stub: true },
    { label: 'Exceptions', path: '/exceptions', icon: '!', stub: true },
    { label: 'Reconciliation', path: '/reconciliation', icon: '≈', stub: true },
    { label: 'Tariffs & Rules', path: '/tariffs', icon: '§' },
    { label: 'Calculation Workbench', path: '/calculation-workbench', icon: 'ƒ', stub: true },
    { label: 'Reports', path: '/reports', icon: '▤', stub: true },
    { label: 'Automation Center', path: '/automation', icon: '⚙', stub: true },
    { label: 'Audit & Activity', path: '/audit', icon: '⏱', stub: true },
    { label: 'System Health', path: '/system-health', icon: '♥', stub: true },
  ];

  constructor(private readonly auth: AuthService) {}

  toggleCollapsed(): void {
    this.collapsed.update((v) => !v);
  }

  signOut(): void {
    this.auth.signOut();
  }
}
