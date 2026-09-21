import { DatePipe } from '@angular/common';
import { Component, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AuthService } from '../../../../core/services/auth.service';
import { UserService } from '../../../../core/services/user.service';
import { ManagedUser, RolesAndPermissions, USER_ROLES, UserRoleName } from '../../../../core/models/user.model';
import { StatusBadge } from '../../../../shared/components/badge/status-badge';

type Tab = 'users' | 'roles';

interface Draft {
  loginId: string;
  displayName: string;
  email: string;
  role: UserRoleName;
  password: string;
}

const NEW_DRAFT: Draft = { loginId: '', displayName: '', email: '', role: 'Operator', password: '' };

/**
 * User Management, with Roles & Permissions as its second tab. Users created here live in the database and can be edited,
 * deactivated (never deleted, so the audit trail keeps meaning), given a new password and unlocked. Users defined in configuration are
 * listed but managed outside the app. Only Admin and IT users can open this; the API enforces it. The roles tab shows the same list the
 * API's authorization policies are built from, so it is exactly what is enforced.
 */
@Component({
  selector: 'pe-user-management',
  imports: [FormsModule, DatePipe, StatusBadge],
  templateUrl: './user-management.html',
  styleUrl: './user-management.scss',
})
export class UserManagement implements OnInit {
  protected readonly tab = signal<Tab>('users');
  protected readonly roleOptions = USER_ROLES;

  protected readonly users = signal<ManagedUser[]>([]);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  protected q = '';
  protected roleFilter = '';
  protected statusFilter = '';

  /** The add / edit form: `editing` is null when adding, otherwise the user being edited. */
  protected readonly formOpen = signal(false);
  protected readonly editing = signal<ManagedUser | null>(null);
  protected draft: Draft = { ...NEW_DRAFT };
  protected readonly formErrors = signal<string[]>([]);
  protected readonly saving = signal(false);

  protected readonly passwordFor = signal<ManagedUser | null>(null);
  protected newPassword = '';
  protected readonly passwordErrors = signal<string[]>([]);

  protected readonly confirmingId = signal<string | null>(null);
  protected readonly notice = signal<string | null>(null);

  protected readonly rolesData = signal<RolesAndPermissions | null>(null);
  protected readonly rolesError = signal<string | null>(null);

  constructor(
    private readonly userService: UserService,
    private readonly auth: AuthService,
  ) {}

  ngOnInit(): void {
    this.load();
  }

  protected get me(): string {
    return this.auth.loginId() ?? '';
  }

  protected isMe(u: ManagedUser): boolean {
    return u.loginId.toLowerCase() === this.me.toLowerCase();
  }

  protected showTab(tab: Tab): void {
    this.tab.set(tab);
    if (tab === 'roles' && !this.rolesData() && !this.rolesError()) this.loadRoles();
  }

  protected load(): void {
    this.loading.set(true);
    this.error.set(null);
    this.userService.list({ q: this.q, role: this.roleFilter, status: this.statusFilter }).subscribe({
      next: (r) => {
        this.users.set(r.items);
        this.loading.set(false);
      },
      error: (err) => {
        this.error.set(err?.status === 403 ? 'Only Admin and IT users can manage users.' : 'Could not load users from the API.');
        this.loading.set(false);
      },
    });
  }

  private loadRoles(): void {
    this.userService.roles().subscribe({
      next: (r) => this.rolesData.set(r),
      error: () => this.rolesError.set('Could not load roles and permissions from the API.'),
    });
  }

  protected clearFilters(): void {
    this.q = this.roleFilter = this.statusFilter = '';
    this.load();
  }

  // ------------------------------------------------------------------ add / edit
  protected openAdd(): void {
    this.editing.set(null);
    this.draft = { ...NEW_DRAFT };
    this.formErrors.set([]);
    this.formOpen.set(true);
    this.passwordFor.set(null);
  }

  protected openEdit(u: ManagedUser): void {
    this.editing.set(u);
    this.draft = { loginId: u.loginId, displayName: u.displayName, email: u.email, role: u.role, password: '' };
    this.formErrors.set([]);
    this.formOpen.set(true);
    this.passwordFor.set(null);
  }

  protected cancelForm(): void {
    this.formOpen.set(false);
  }

  protected save(): void {
    this.saving.set(true);
    this.formErrors.set([]);
    const editing = this.editing();
    const request = editing
      ? this.userService.update(editing.id!, { displayName: this.draft.displayName, email: this.draft.email, role: this.draft.role })
      : this.userService.create({ ...this.draft });
    request.subscribe({
      next: (u) => {
        this.saving.set(false);
        this.formOpen.set(false);
        this.notice.set(editing ? `Saved ${u.loginId}.` : `Created ${u.loginId}.`);
        this.load();
      },
      error: (err) => {
        this.saving.set(false);
        this.formErrors.set(err?.error?.problems ?? [err?.error?.error ?? 'Could not save the user.']);
      },
    });
  }

  // ------------------------------------------------------------------ activate / deactivate
  protected toggleActive(u: ManagedUser): void {
    if (u.isActive && this.confirmingId() !== u.id) {
      this.confirmingId.set(u.id);
      return;
    }
    this.confirmingId.set(null);
    this.userService.update(u.id!, { isActive: !u.isActive }).subscribe({
      next: () => {
        this.notice.set(u.isActive ? `Deactivated ${u.loginId}. They can no longer sign in.` : `Activated ${u.loginId}.`);
        this.load();
      },
      error: (err) => this.notice.set(err?.error?.error ?? 'Could not change the user.'),
    });
  }

  // ------------------------------------------------------------------ password / unlock
  protected openPassword(u: ManagedUser): void {
    this.passwordFor.set(u);
    this.newPassword = '';
    this.passwordErrors.set([]);
    this.formOpen.set(false);
  }

  protected savePassword(): void {
    const u = this.passwordFor();
    if (!u) return;
    this.userService.setPassword(u.id!, this.newPassword).subscribe({
      next: () => {
        this.passwordFor.set(null);
        this.notice.set(`Password changed for ${u.loginId}. Tell them the new password securely.`);
        this.load();
      },
      error: (err) => this.passwordErrors.set(err?.error?.problems ?? [err?.error?.error ?? 'Could not change the password.']),
    });
  }

  protected unlock(u: ManagedUser): void {
    this.userService.unlock(u.loginId).subscribe({
      next: () => {
        this.notice.set(`Cleared the sign-in lock for ${u.loginId}.`);
        this.load();
      },
      error: () => this.notice.set('Could not clear the lock.'),
    });
  }

  protected roleTone(role: UserRoleName): 'analytic' | 'info' | 'success' | 'warning' | 'neutral' {
    return role === 'Admin' ? 'analytic' : role === 'IT' ? 'info' : role === 'Operator' ? 'success' : role === 'Utility' ? 'warning' : 'neutral';
  }

  protected granted(permissionRoles: UserRoleName[], role: UserRoleName): boolean {
    return permissionRoles.includes(role);
  }
}
