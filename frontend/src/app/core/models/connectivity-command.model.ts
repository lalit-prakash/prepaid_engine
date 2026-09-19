/** Mirrors backend ConnectivityCommandStatus (ConnectivityCommand.cs). */
export enum ConnectivityCommandStatus {
  Queued = 0,
  Sent = 1,
  Acknowledged = 2,
  Failed = 3,
  TimedOut = 4,
}

/** Mirrors backend ConnectivityCommandType. */
export enum ConnectivityCommandType {
  Disconnect = 0,
  Reconnect = 1,
}

/** One row of GET /api/v1/connectivity-commands. */
export interface ConnectivityCommandSummary {
  id: string;
  accountNumber: string;
  name: string;
  commandType: ConnectivityCommandType;
  reason: string;
  status: ConnectivityCommandStatus;
  retryCount: number;
  errorMessage: string | null;
  createdAt: string;
  sentAt: string | null;
  acknowledgedAt: string | null;
}

/** GET /api/v1/connectivity-commands/summary: counts computed by the database. */
export interface ConnectivityCommandSummaryStats {
  total: number;
  disconnects: number;
  reconnects: number;
  acknowledged: number;
  failedOrTimedOut: number;
  pending: number;
}

/** GET /api/v1/connectivity-commands/{id}. */
export interface ConnectivityCommandDetail {
  id: string;
  consumer: { accountNumber: string; name: string; connectionStatus: number };
  commandType: ConnectivityCommandType;
  reason: string;
  status: ConnectivityCommandStatus;
  retryCount: number;
  errorMessage: string | null;
  createdAt: string;
  sentAt: string | null;
  acknowledgedAt: string | null;
}

/** POST /api/v1/connectivity-commands/{id}/retry response. */
export interface RetryConnectivityCommandResult {
  id: string;
  status: ConnectivityCommandStatus;
  retryCount: number;
  errorMessage: string | null;
  consumerConnectionStatus: string;
}
