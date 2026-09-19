/** GET /api/v1/dashboard/summary - counts, sums and a few top rows computed by the database. */
export interface DashboardSummary {
  consumers: {
    total: number;
    active: number;
    disconnected: number;
    /** Wallet balance below the emergency credit limit, whatever the connection state. */
    lowBalance: number;
    /** Low balance among consumers that are not disconnected (used by the network health split). */
    lowBalanceConnected: number;
    walletTotal: number;
  };
  /** Most recent daily load profile date; null when no profile has been received. */
  billing: {
    profileDate: string;
    total: number;
    successful: number;
    failed: number;
    pending: number;
    latestReceivedAt: string | null;
  } | null;
  attention: {
    critical: number;
    warning: number;
    items: DashboardAttentionItem[];
  };
  recentConnectivity: DashboardConnectivityRow[];
}

export interface DashboardAttentionItem {
  id: string;
  severity: 'critical' | 'warning';
  title: string;
  detail: string;
  at: string;
  link: string;
}

export interface DashboardConnectivityRow {
  id: string;
  name: string;
  accountNumber: string;
  meterNumber: string;
  /** 0 = Disconnect, 1 = Reconnect. */
  commandType: number;
  status: number;
  createdAt: string;
}
