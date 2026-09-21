export type UserRoleName = 'Admin' | 'IT' | 'Operator' | 'Utility' | 'ReadOnly';

export const USER_ROLES: UserRoleName[] = ['Admin', 'IT', 'Operator', 'Utility', 'ReadOnly'];

/** One row of GET /api/v1/users. `source` Configuration users are bootstrap accounts: listed, but not editable here. */
export interface ManagedUser {
  id: string | null;
  loginId: string;
  displayName: string;
  email: string;
  role: UserRoleName;
  isActive: boolean;
  source: 'Database' | 'Configuration';
  createdAt: string | null;
  lastLoginAt: string | null;
  passwordChangedAt: string | null;
  /** The account is locked out of signing in after repeated failed attempts. */
  locked: boolean;
}

export interface UserList {
  items: ManagedUser[];
  total: number;
}

export interface CreateUserPayload {
  loginId: string;
  displayName: string;
  email: string;
  role: UserRoleName;
  password: string;
}

export interface UpdateUserPayload {
  displayName?: string;
  email?: string;
  role?: UserRoleName;
  isActive?: boolean;
}

/** GET /api/v1/roles: the same list the API's authorization policies are built from. */
export interface RolesAndPermissions {
  roles: { role: UserRoleName; summary: string; activeUsers: number }[];
  permissions: { policy: string; name: string; description: string; roles: UserRoleName[] }[];
}
